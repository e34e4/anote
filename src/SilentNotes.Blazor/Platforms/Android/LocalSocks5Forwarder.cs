// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Services;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Local no-auth SOCKS5 listener that forwards to an upstream SOCKS5 server with username/password.
    /// Android WebView <see cref="AndroidX.WebKit.ProxyConfig"/> rejects credentials in proxy URLs
    /// ("Invalid Proxy URL"), so WebViews must point at this loopback forwarder instead.
    /// </summary>
    internal static class LocalSocks5Forwarder
    {
        private static readonly object Gate = new object();
        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _localPort;
        private static string _upstreamHost;
        private static int _upstreamPort;
        private static string _username;
        private static string _password;
        private static string _fingerprint;

        /// <summary>
        /// Ensures a loopback SOCKS5 forwarder is listening for the given upstream credentials.
        /// Returns the local port, or 0 on failure.
        /// </summary>
        public static int EnsureRunning(string host, int port, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
                return 0;

            string user = username?.Trim() ?? string.Empty;
            string pass = password ?? string.Empty;
            string fingerprint = host.Trim().ToLowerInvariant() + "|" + port + "|" + user + "|" + pass;

            lock (Gate)
            {
                if (_listener != null && _localPort > 0 && string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
                    return _localPort;

                StopLocked();

                _upstreamHost = host.Trim();
                _upstreamPort = port;
                _username = user;
                _password = pass;
                _fingerprint = fingerprint;
                _cts = new CancellationTokenSource();

                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start();
                    _localPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    CancellationToken token = _cts.Token;
                    Task.Run(() => AcceptLoopAsync(token), token);
                    AnoteFileLog.Append(
                        "ytdlp",
                        "Local SOCKS5 forwarder on 127.0.0.1:" + _localPort
                        + " → " + _upstreamHost + ":" + _upstreamPort);
                    return _localPort;
                }
                catch (Exception ex)
                {
                    AnoteFileLog.Append("ytdlp", "Local SOCKS5 forwarder start failed: " + ex.Message);
                    StopLocked();
                    return 0;
                }
            }
        }

        public static void Stop()
        {
            lock (Gate)
                StopLocked();
        }

        private static void StopLocked()
        {
            try { _cts?.Cancel(); } catch (Exception) { }
            try { _listener?.Stop(); } catch (Exception) { }
            _cts = null;
            _listener = null;
            _localPort = 0;
            _fingerprint = null;
        }

        private static async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested)
                        break;
                    await Task.Delay(50, token).ConfigureAwait(false);
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            TcpClient upstream = null;
            try
            {
                client.NoDelay = true;
                NetworkStream clientStream = client.GetStream();

                // SOCKS5 greeting from client
                byte[] buf = new byte[512];
                int n = await ReadAtLeastAsync(clientStream, buf, 2, token).ConfigureAwait(false);
                if (n < 2 || buf[0] != 0x05)
                    return;

                int nMethods = buf[1];
                await ReadAtLeastAsync(clientStream, buf, nMethods, token).ConfigureAwait(false);
                // No auth required toward local clients
                await clientStream.WriteAsync(new byte[] { 0x05, 0x00 }, token).ConfigureAwait(false);

                // Connect request
                n = await ReadAtLeastAsync(clientStream, buf, 4, token).ConfigureAwait(false);
                if (n < 4 || buf[0] != 0x05 || buf[1] != 0x01)
                {
                    await WriteSocksFailureAsync(clientStream, 0x07, token).ConfigureAwait(false);
                    return;
                }

                byte atyp = buf[3];
                string destHost;
                int destPort;
                if (atyp == 0x01)
                {
                    await ReadAtLeastAsync(clientStream, buf, 4 + 2, token).ConfigureAwait(false);
                    destHost = new IPAddress(new[] { buf[0], buf[1], buf[2], buf[3] }).ToString();
                    destPort = (buf[4] << 8) | buf[5];
                }
                else if (atyp == 0x03)
                {
                    await ReadAtLeastAsync(clientStream, buf, 1, token).ConfigureAwait(false);
                    int len = buf[0];
                    await ReadAtLeastAsync(clientStream, buf, len + 2, token).ConfigureAwait(false);
                    destHost = Encoding.ASCII.GetString(buf, 0, len);
                    destPort = (buf[len] << 8) | buf[len + 1];
                }
                else if (atyp == 0x04)
                {
                    await ReadAtLeastAsync(clientStream, buf, 16 + 2, token).ConfigureAwait(false);
                    var addr = new byte[16];
                    Buffer.BlockCopy(buf, 0, addr, 0, 16);
                    destHost = new IPAddress(addr).ToString();
                    destPort = (buf[16] << 8) | buf[17];
                }
                else
                {
                    await WriteSocksFailureAsync(clientStream, 0x08, token).ConfigureAwait(false);
                    return;
                }

                upstream = new TcpClient { NoDelay = true };
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(20000);
                    await upstream.ConnectAsync(_upstreamHost, _upstreamPort, connectCts.Token).ConfigureAwait(false);
                }

                NetworkStream up = upstream.GetStream();
                if (!await Socks5HandshakeUpstreamAsync(up, destHost, destPort, token).ConfigureAwait(false))
                {
                    await WriteSocksFailureAsync(clientStream, 0x05, token).ConfigureAwait(false);
                    return;
                }

                // Success reply to local client (bind 0.0.0.0:0)
                await clientStream.WriteAsync(
                    new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 },
                    token).ConfigureAwait(false);

                Task a = PumpAsync(clientStream, up, token);
                Task b = PumpAsync(up, clientStream, token);
                await Task.WhenAny(a, b).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                try { client?.Close(); } catch (Exception) { }
                try { upstream?.Close(); } catch (Exception) { }
            }
        }

        private static async Task<bool> Socks5HandshakeUpstreamAsync(
            NetworkStream up,
            string destHost,
            int destPort,
            CancellationToken token)
        {
            byte[] buf = new byte[512];
            bool needAuth = !string.IsNullOrEmpty(_username);

            if (needAuth)
                await up.WriteAsync(new byte[] { 0x05, 0x01, 0x02 }, token).ConfigureAwait(false);
            else
                await up.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, token).ConfigureAwait(false);

            int n = await ReadAtLeastAsync(up, buf, 2, token).ConfigureAwait(false);
            if (n < 2 || buf[0] != 0x05)
                return false;

            if (buf[1] == 0x02)
            {
                byte[] userBytes = Encoding.UTF8.GetBytes(_username ?? string.Empty);
                byte[] passBytes = Encoding.UTF8.GetBytes(_password ?? string.Empty);
                if (userBytes.Length > 255 || passBytes.Length > 255)
                    return false;

                var auth = new byte[3 + userBytes.Length + passBytes.Length];
                auth[0] = 0x01;
                auth[1] = (byte)userBytes.Length;
                Buffer.BlockCopy(userBytes, 0, auth, 2, userBytes.Length);
                auth[2 + userBytes.Length] = (byte)passBytes.Length;
                Buffer.BlockCopy(passBytes, 0, auth, 3 + userBytes.Length, passBytes.Length);
                await up.WriteAsync(auth, token).ConfigureAwait(false);

                n = await ReadAtLeastAsync(up, buf, 2, token).ConfigureAwait(false);
                if (n < 2 || buf[1] != 0x00)
                    return false;
            }
            else if (buf[1] != 0x00)
            {
                return false;
            }

            byte[] hostBytes = Encoding.ASCII.GetBytes(destHost);
            if (hostBytes.Length > 255)
                return false;

            var req = new byte[4 + 1 + hostBytes.Length + 2];
            req[0] = 0x05;
            req[1] = 0x01;
            req[2] = 0x00;
            req[3] = 0x03;
            req[4] = (byte)hostBytes.Length;
            Buffer.BlockCopy(hostBytes, 0, req, 5, hostBytes.Length);
            req[5 + hostBytes.Length] = (byte)(destPort >> 8);
            req[6 + hostBytes.Length] = (byte)(destPort & 0xff);
            await up.WriteAsync(req, token).ConfigureAwait(false);

            n = await ReadAtLeastAsync(up, buf, 4, token).ConfigureAwait(false);
            if (n < 4 || buf[0] != 0x05 || buf[1] != 0x00)
                return false;

            // Consume bind address from the reply.
            byte replyAtyp = buf[3];
            if (replyAtyp == 0x01)
                await ReadAtLeastAsync(up, buf, 4 + 2, token).ConfigureAwait(false);
            else if (replyAtyp == 0x04)
                await ReadAtLeastAsync(up, buf, 16 + 2, token).ConfigureAwait(false);
            else if (replyAtyp == 0x03)
            {
                if (await ReadAtLeastAsync(up, buf, 1, token).ConfigureAwait(false) < 1)
                    return false;
                int len = buf[0];
                await ReadAtLeastAsync(up, buf, len + 2, token).ConfigureAwait(false);
            }
            else
                return false;

            return true;
        }

        private static async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken token)
        {
            byte[] buf = new byte[64 * 1024];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int n = await from.ReadAsync(buf.AsMemory(0, buf.Length), token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    await to.WriteAsync(buf.AsMemory(0, n), token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
        }

        private static async Task WriteSocksFailureAsync(NetworkStream stream, byte code, CancellationToken token)
        {
            try
            {
                await stream.WriteAsync(
                    new byte[] { 0x05, code, 0x00, 0x01, 0, 0, 0, 0, 0, 0 },
                    token).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private static async Task<int> ReadAtLeastAsync(
            NetworkStream stream,
            byte[] buffer,
            int count,
            CancellationToken token)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), token).ConfigureAwait(false);
                if (n <= 0)
                    return read;
                read += n;
            }

            return read;
        }
    }
}
