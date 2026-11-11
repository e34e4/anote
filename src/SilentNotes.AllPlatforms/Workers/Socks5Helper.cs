// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Services;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Minimal SOCKS5 client (CONNECT) for <see cref="HttpClient"/> via <see cref="SocketsHttpHandler"/>.
    /// </summary>
    public static class Socks5Helper
    {
        private const byte Version5 = 0x05;
        private const byte CmdConnect = 0x01;
        private const byte AtypDomain = 0x03;
        private const byte AuthNone = 0x00;
        private const byte AuthUserPass = 0x02;

        /// <summary>
        /// Creates an <see cref="HttpMessageHandler"/> that routes connections through SOCKS5 when
        /// <paramref name="settings"/> is enabled and configured; otherwise returns a plain handler.
        /// </summary>
        public static HttpMessageHandler CreateHttpHandler(IProxySettings settings)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            };

            if (settings != null && settings.Socks5Enabled && settings.HasSocks5Config)
            {
                handler.ConnectCallback = (context, cancellationToken) =>
                    ConnectThroughProxyAsync(settings, context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken);
            }

            return handler;
        }

        /// <summary>
        /// Opens a TCP stream to <paramref name="targetHost"/>:<paramref name="targetPort"/> via SOCKS5.
        /// </summary>
        public static async ValueTask<Stream> ConnectThroughProxyAsync(
            IProxySettings settings,
            string targetHost,
            int targetPort,
            CancellationToken cancellationToken)
        {
            if (settings == null || !settings.HasSocks5Config)
                throw new InvalidOperationException("SOCKS5 proxy is not configured.");

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(settings.Socks5Host.Trim(), settings.Socks5Port, cancellationToken)
                    .ConfigureAwait(false);
                NetworkStream stream = new NetworkStream(socket, ownsSocket: true);
                await PerformSocks5ConnectAsync(stream, settings, targetHost, targetPort, cancellationToken)
                    .ConfigureAwait(false);
                return stream;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task PerformSocks5ConnectAsync(
            Stream stream,
            IProxySettings settings,
            string targetHost,
            int targetPort,
            CancellationToken cancellationToken)
        {
            bool useAuth = !string.IsNullOrEmpty(settings.Socks5Username);
            byte method = useAuth ? AuthUserPass : AuthNone;

            await stream.WriteAsync(new[] { Version5, 0x01, method }, cancellationToken).ConfigureAwait(false);
            byte[] methodReply = await ReadExactAsync(stream, 2, cancellationToken).ConfigureAwait(false);
            if (methodReply[0] != Version5 || methodReply[1] != method)
                throw new IOException("SOCKS5 handshake failed (method selection).");

            if (useAuth)
            {
                byte[] userBytes = System.Text.Encoding.UTF8.GetBytes(settings.Socks5Username ?? string.Empty);
                byte[] passBytes = System.Text.Encoding.UTF8.GetBytes(settings.Socks5Password ?? string.Empty);
                if (userBytes.Length > 255 || passBytes.Length > 255)
                    throw new IOException("SOCKS5 username or password too long.");

                var authRequest = new byte[3 + userBytes.Length + passBytes.Length];
                authRequest[0] = 0x01;
                authRequest[1] = (byte)userBytes.Length;
                Buffer.BlockCopy(userBytes, 0, authRequest, 2, userBytes.Length);
                authRequest[2 + userBytes.Length] = (byte)passBytes.Length;
                Buffer.BlockCopy(passBytes, 0, authRequest, 3 + userBytes.Length, passBytes.Length);
                await stream.WriteAsync(authRequest, cancellationToken).ConfigureAwait(false);
                byte[] authReply = await ReadExactAsync(stream, 2, cancellationToken).ConfigureAwait(false);
                if (authReply[1] != 0x00)
                    throw new IOException("SOCKS5 authentication failed.");
            }

            byte[] hostBytes = System.Text.Encoding.UTF8.GetBytes(targetHost);
            if (hostBytes.Length > 255)
                throw new IOException("SOCKS5 target host name too long.");

            var request = new byte[7 + hostBytes.Length];
            request[0] = Version5;
            request[1] = CmdConnect;
            request[2] = 0x00;
            request[3] = AtypDomain;
            request[4] = (byte)hostBytes.Length;
            Buffer.BlockCopy(hostBytes, 0, request, 5, hostBytes.Length);
            request[5 + hostBytes.Length] = (byte)((targetPort >> 8) & 0xFF);
            request[6 + hostBytes.Length] = (byte)(targetPort & 0xFF);
            await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            byte[] header = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
            if (header[1] != 0x00)
                throw new IOException("SOCKS5 CONNECT failed with code " + header[1].ToString());

            int remaining = header[3] switch
            {
                0x01 => 4 + 2,
                0x03 => (await ReadExactAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0] + 2,
                0x04 => 16 + 2,
                _ => throw new IOException("SOCKS5 reply has unknown address type."),
            };
            if (remaining > 0)
                await ReadExactAsync(stream, remaining, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new IOException("SOCKS5 connection closed unexpectedly.");
                offset += read;
            }

            return buffer;
        }
    }
}
