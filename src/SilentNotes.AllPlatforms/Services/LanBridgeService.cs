// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Self contained HTTP/WebSocket server. It is implemented on top of a <see cref="TcpListener"/>
    /// instead of ASP.NET Core, so it works on Android without adding a web host to the app.
    /// The server only exposes read access to the notes and read/write access to the clipboard
    /// history; it never deletes or modifies notes.
    /// </summary>
    public class LanBridgeService : ILanBridgeService, IDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MaxRequestBytes = 1024 * 1024;

        private readonly IRepositoryStorageService _repositoryStorageService;
        private readonly IClipboardHistoryService _clipboardHistoryService;

        private readonly object _syncRoot = new object();
        private readonly List<WebSocketConnection> _clients = new List<WebSocketConnection>();
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private string _accessToken = string.Empty;
        private int _port;
        private bool _allowClipboardWrites;

        /// <summary>
        /// Initializes a new instance of the <see cref="LanBridgeService"/> class.
        /// </summary>
        public LanBridgeService(
            IRepositoryStorageService repositoryStorageService,
            IClipboardHistoryService clipboardHistoryService)
        {
            _repositoryStorageService = repositoryStorageService;
            _clipboardHistoryService = clipboardHistoryService;
        }

        /// <inheritdoc/>
        public bool IsRunning
        {
            get { return _listener != null; }
        }

        /// <inheritdoc/>
        public int Port
        {
            get { return _port; }
        }

        /// <inheritdoc/>
        public string AccessToken
        {
            get { return _accessToken; }
        }

        /// <inheritdoc/>
        public string LastError { get; private set; }

        /// <inheritdoc/>
        public bool AllowClipboardWrites
        {
            get { return _allowClipboardWrites; }
        }

        /// <inheritdoc/>
        public string Start(int port, string accessToken, bool allowClipboardWrites)
        {
            lock (_syncRoot)
            {
                if (_listener != null)
                    StopInternal();

                LastError = null;
                _accessToken = accessToken ?? string.Empty;
                _allowClipboardWrites = allowClipboardWrites;

                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    _listener = listener;
                    _cancellation = new CancellationTokenSource();
                    _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Task.Run(() => AcceptLoopAsync(listener, _cancellation.Token));
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    _listener = null;
                    _cancellation = null;
                    return null;
                }

                IReadOnlyList<string> urls = GetLanUrls();
                return urls.Count > 0 ? urls[0] : null;
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            lock (_syncRoot)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            try
            {
                _cancellation?.Cancel();
                _listener?.Stop();
            }
            catch (Exception)
            {
            }

            WebSocketConnection[] clients;
            lock (_clients)
            {
                clients = _clients.ToArray();
                _clients.Clear();
            }
            foreach (WebSocketConnection client in clients)
                client.Dispose();

            _listener = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _port = 0;
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetLanUrls()
        {
            var result = new List<string>();
            if (!IsRunning)
                return result;

            string tokenPart = string.IsNullOrEmpty(_accessToken) ? string.Empty : "?token=" + Uri.EscapeDataString(_accessToken);
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                        continue;
                    result.Add("http://" + address + ":" + _port + "/" + tokenPart);
                }
            }
            catch (Exception)
            {
            }

            if (result.Count == 0)
                result.Add("http://127.0.0.1:" + _port + "/" + tokenPart);
            return result;
        }

        /// <inheritdoc/>
        public void NotifyClipboardChanged()
        {
            if (!IsRunning)
                return;
            _ = BroadcastAsync("{\"type\":\"clipboard-changed\"}");
        }

        private async Task BroadcastAsync(string message)
        {
            WebSocketConnection[] clients;
            lock (_clients)
            {
                clients = _clients.ToArray();
            }

            foreach (WebSocketConnection client in clients)
            {
                try
                {
                    await client.SendTextAsync(message);
                }
                catch (Exception)
                {
                    RemoveClient(client);
                }
            }
        }

        private void RemoveClient(WebSocketConnection client)
        {
            lock (_clients)
            {
                _clients.Remove(client);
            }
            client.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (token.IsCancellationRequested)
                        break;
                    continue;
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(() => HandleConnectionAsync(client, token), CancellationToken.None);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    client.NoDelay = true;
                    HttpRequest request = await ReadRequestAsync(stream, token);
                    if (request == null)
                        return;

                    if (IsWebSocketRequest(request))
                    {
                        await HandleWebSocketAsync(stream, request, token);
                        return;
                    }

                    await HandleHttpRequestAsync(stream, request, token);
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool IsWebSocketRequest(HttpRequest request)
        {
            return request.Path.Equals("/ws", StringComparison.OrdinalIgnoreCase)
                && request.Headers.TryGetValue("upgrade", out string upgrade)
                && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleWebSocketAsync(NetworkStream stream, HttpRequest request, CancellationToken token)
        {
            if (!IsAuthorized(request))
            {
                await WriteResponseAsync(stream, 401, "Unauthorized", "text/plain", Encoding.UTF8.GetBytes("Invalid token"), token);
                return;
            }

            if (!request.Headers.TryGetValue("sec-websocket-key", out string key) || string.IsNullOrWhiteSpace(key))
            {
                await WriteResponseAsync(stream, 400, "Bad Request", "text/plain", Encoding.UTF8.GetBytes("Missing websocket key"), token);
                return;
            }

            string accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.UTF8.GetBytes(key.Trim() + WebSocketGuid)));
            string handshake =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] handshakeBytes = Encoding.ASCII.GetBytes(handshake);
            await stream.WriteAsync(handshakeBytes, token);
            await stream.FlushAsync(token);

            var connection = new WebSocketConnection(stream);
            lock (_clients)
            {
                _clients.Add(connection);
            }

            try
            {
                await connection.ReceiveLoopAsync(token);
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_clients)
                {
                    _clients.Remove(connection);
                }
                connection.Dispose();
            }
        }

        private async Task HandleHttpRequestAsync(NetworkStream stream, HttpRequest request, CancellationToken token)
        {
            string path = request.Path;

            if (path == "/" || path == "/index.html" || path == "/index")
            {
                byte[] page = Encoding.UTF8.GetBytes(LanBridgeWebPage.Html);
                await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", page, token);
                return;
            }

            if (path == "/favicon.ico")
            {
                await WriteResponseAsync(stream, 204, "No Content", "image/x-icon", Array.Empty<byte>(), token);
                return;
            }

            if (!IsAuthorized(request))
            {
                await WriteResponseAsync(stream, 401, "Unauthorized", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Missing or invalid access token."), token);
                return;
            }

            try
            {
                if (path == "/api/status")
                {
                    await WriteJsonAsync(stream, BuildStatusJson(), token);
                    return;
                }
                if (path == "/api/clipboard")
                {
                    if (request.Method == "POST")
                    {
                        await HandleClipboardPostAsync(stream, request, token);
                        return;
                    }
                    await WriteJsonAsync(stream, BuildClipboardJson(), token);
                    return;
                }
                if (path == "/api/notes")
                {
                    await WriteJsonAsync(stream, BuildNoteListJson(), token);
                    return;
                }
                if (path.StartsWith("/api/notes/", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleNoteRequestAsync(stream, request, token);
                    return;
                }
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(stream, 500, "Internal Server Error", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Error: " + ex.Message), token);
                return;
            }

            await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8",
                Encoding.UTF8.GetBytes("Not found."), token);
        }

        private async Task HandleClipboardPostAsync(NetworkStream stream, HttpRequest request, CancellationToken token)
        {
            if (!_allowClipboardWrites)
            {
                await WriteResponseAsync(stream, 403, "Forbidden", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Writing clipboard items is disabled in aNote."), token);
                return;
            }

            string text = ExtractTextFromBody(request);
            if (string.IsNullOrWhiteSpace(text))
            {
                await WriteResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("The request does not contain any text."), token);
                return;
            }

            int limit = 500;
            _clipboardHistoryService.AddText(text, limit);
            NotifyClipboardChanged();

            await WriteJsonAsync(stream, Encoding.UTF8.GetBytes("{\"ok\":true}"), token);
        }

        private static string ExtractTextFromBody(HttpRequest request)
        {
            if (request.Body == null || request.Body.Length == 0)
                return null;

            string body = Encoding.UTF8.GetString(request.Body).Trim();
            if (request.ContentType != null && request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        if (document.RootElement.ValueKind == JsonValueKind.Object
                            && document.RootElement.TryGetProperty("text", out JsonElement textElement)
                            && textElement.ValueKind == JsonValueKind.String)
                        {
                            return textElement.GetString();
                        }
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
                return null;
            }
            return body;
        }

        private async Task HandleNoteRequestAsync(NetworkStream stream, HttpRequest request, CancellationToken token)
        {
            string remainder = request.Path.Substring("/api/notes/".Length);
            bool download = remainder.EndsWith("/download", StringComparison.OrdinalIgnoreCase);
            if (download)
                remainder = remainder.Substring(0, remainder.Length - "/download".Length);

            if (!Guid.TryParse(remainder, out Guid noteId))
            {
                await WriteResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Invalid note id."), token);
                return;
            }

            _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            NoteModel note = repository.Notes.FirstOrDefault(item => item.Id == noteId && !item.InRecyclingBin);
            if (note == null)
            {
                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Note not found."), token);
                return;
            }

            bool locked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty;
            string format = request.Query.TryGetValue("format", out string requestedFormat) ? requestedFormat.ToLowerInvariant() : "text";
            byte[] content;
            string contentType;
            if (locked)
            {
                content = Encoding.UTF8.GetBytes("This note is protected by a safe and cannot be read while the safe is closed.");
                contentType = "text/plain; charset=utf-8";
            }
            else if (format == "html")
            {
                content = Encoding.UTF8.GetBytes(note.HtmlContent ?? string.Empty);
                contentType = "text/html; charset=utf-8";
            }
            else if (format == "md" || format == "markdown")
            {
                content = Encoding.UTF8.GetBytes(SimpleHtmlToMarkdown.Convert(note.HtmlContent));
                contentType = "text/markdown; charset=utf-8";
            }
            else
            {
                content = Encoding.UTF8.GetBytes(NoteHtmlUtils.GetPlainText(note.HtmlContent));
                contentType = "text/plain; charset=utf-8";
            }

            if (download)
            {
                string title = locked ? "Encrypted note" : NoteHtmlUtils.GetTitle(note.HtmlContent);
                string fileName = NoteHtmlUtils.Slugify(title) + ".txt";
                string header = "Content-Disposition: attachment; filename=\"" + fileName.Replace("\"", string.Empty) + "\"\r\n";
                await WriteResponseAsync(stream, 200, "OK", contentType, content, token, header);
            }
            else
            {
                await WriteResponseAsync(stream, 200, "OK", contentType, content, token);
            }
        }

        private bool IsAuthorized(HttpRequest request)
        {
            if (string.IsNullOrEmpty(_accessToken))
                return true;

            string token = null;
            if (request.Query.TryGetValue("token", out string queryToken))
                token = queryToken;
            else if (request.Headers.TryGetValue("x-anote-token", out string headerToken))
                token = headerToken;
            else if (request.Headers.TryGetValue("authorization", out string authorization)
                && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = authorization.Substring("Bearer ".Length).Trim();

            return string.Equals(token, _accessToken, StringComparison.Ordinal);
        }

        private byte[] BuildStatusJson()
        {
            _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            ClipboardHistoryModel history = _clipboardHistoryService.LoadHistoryOrDefault();
            return BuildJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteBoolean("running", IsRunning);
                writer.WriteNumber("port", _port);
                writer.WriteBoolean("allowClipboardWrites", _allowClipboardWrites);
                writer.WriteNumber("noteCount", repository.Notes.Count(note => !note.InRecyclingBin));
                writer.WriteNumber("clipboardCount", history.Items.Count(item => !item.IsDeleted));
                writer.WriteEndObject();
            });
        }

        private byte[] BuildClipboardJson()
        {
            ClipboardHistoryModel history = _clipboardHistoryService.LoadHistoryOrDefault();
            List<ClipboardItemModel> items = history.Items
                .Where(item => !item.IsDeleted && item.Kind == ClipboardItemKind.Text && !string.IsNullOrEmpty(item.Text))
                .OrderByDescending(item => item.CreatedUtc)
                .Take(500)
                .ToList();

            return BuildJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartArray("items");
                foreach (ClipboardItemModel item in items)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", item.Id.ToString());
                    writer.WriteString("createdAt", item.CreatedUtc.ToString("o"));
                    writer.WriteBoolean("favorite", item.IsFavorite);
                    writer.WriteString("text", item.Text);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
        }

        private byte[] BuildNoteListJson()
        {
            _repositoryStorageService.LoadRepositoryOrDefault(out NoteRepositoryModel repository);
            List<NoteModel> notes = repository.Notes
                .Where(note => !note.InRecyclingBin)
                .OrderByDescending(note => note.ModifiedAt)
                .ToList();

            return BuildJson(writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartArray("items");
                foreach (NoteModel note in notes)
                {
                    bool locked = note.SafeId.HasValue && note.SafeId.Value != Guid.Empty;
                    string title = locked ? "Encrypted note" : NoteHtmlUtils.GetTitle(note.HtmlContent);
                    string preview = locked ? string.Empty : NoteHtmlUtils.GetPlainText(note.HtmlContent);
                    if (preview.Length > 220)
                        preview = preview.Substring(0, 220).TrimEnd() + "…";

                    writer.WriteStartObject();
                    writer.WriteString("id", note.Id.ToString());
                    writer.WriteString("title", string.IsNullOrWhiteSpace(title) ? "Untitled note" : title);
                    writer.WriteString("noteType", note.NoteType.ToString());
                    writer.WriteString("modifiedAt", note.ModifiedAt.ToString("o"));
                    writer.WriteBoolean("pinned", note.IsPinned);
                    writer.WriteBoolean("locked", locked);
                    writer.WriteString("preview", preview);
                    writer.WriteStartArray("tags");
                    foreach (string tag in note.Tags ?? new List<string>())
                        writer.WriteStringValue(tag);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
        }

        private static byte[] BuildJson(Action<Utf8JsonWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
                {
                    write(writer);
                }
                return stream.ToArray();
            }
        }

        private static async Task WriteJsonAsync(NetworkStream stream, byte[] json, CancellationToken token)
        {
            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json, token);
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int status,
            string reason,
            string contentType,
            byte[] body,
            CancellationToken token,
            string extraHeaders = null)
        {
            body = body ?? Array.Empty<byte>();
            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            header.Append("Content-Type: ").Append(contentType).Append("\r\n");
            header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("Connection: close\r\n");
            header.Append("Access-Control-Allow-Origin: *\r\n");
            if (!string.IsNullOrEmpty(extraHeaders))
                header.Append(extraHeaders);
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            await stream.WriteAsync(headerBytes, token);
            if (body.Length > 0)
                await stream.WriteAsync(body, token);
            await stream.FlushAsync(token);
        }

        private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] buffer = new byte[16 * 1024];
            int length = 0;
            int headerEnd = -1;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), token);
                if (read <= 0)
                    return null;
                length += read;
                headerEnd = FindHeaderEnd(buffer, length);
                if (headerEnd >= 0)
                    break;
            }
            if (headerEnd < 0)
                return null;

            string headerText = Encoding.UTF8.GetString(buffer, 0, headerEnd);
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return null;

            var request = new HttpRequest
            {
                Method = requestLine[0].ToUpperInvariant(),
                RawTarget = requestLine[1],
            };

            for (int i = 1; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator <= 0)
                    continue;
                string name = lines[i].Substring(0, separator).Trim().ToLowerInvariant();
                string value = lines[i].Substring(separator + 1).Trim();
                request.Headers[name] = value;
            }

            string target = request.RawTarget;
            int queryIndex = target.IndexOf('?');
            if (queryIndex >= 0)
            {
                request.Path = Uri.UnescapeDataString(target.Substring(0, queryIndex));
                foreach (string pair in target.Substring(queryIndex + 1).Split('&'))
                {
                    if (pair.Length == 0)
                        continue;
                    int equals = pair.IndexOf('=');
                    string key = equals >= 0 ? pair.Substring(0, equals) : pair;
                    string value = equals >= 0 ? pair.Substring(equals + 1) : string.Empty;
                    request.Query[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
                }
            }
            else
            {
                request.Path = Uri.UnescapeDataString(target);
            }

            request.ContentType = request.Headers.TryGetValue("content-type", out string contentType) ? contentType : null;

            int contentLength = 0;
            if (request.Headers.TryGetValue("content-length", out string contentLengthText))
                int.TryParse(contentLengthText, out contentLength);
            if (contentLength > 0)
            {
                if (contentLength > MaxRequestBytes)
                    return null;

                byte[] body = new byte[contentLength];
                int bodyStart = headerEnd + 4;
                int available = Math.Max(0, length - bodyStart);
                int copied = Math.Min(available, contentLength);
                if (copied > 0)
                    Array.Copy(buffer, bodyStart, body, 0, copied);
                while (copied < contentLength)
                {
                    int read = await stream.ReadAsync(body.AsMemory(copied, contentLength - copied), token);
                    if (read <= 0)
                        break;
                    copied += read;
                }
                request.Body = body;
            }

            return request;
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                    return i - 3;
            }
            return -1;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Stop();
        }

        private sealed class HttpRequest
        {
            public string Method { get; set; }

            public string RawTarget { get; set; }

            public string Path { get; set; }

            public string ContentType { get; set; }

            public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, string> Query { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public byte[] Body { get; set; }
        }

        private sealed class WebSocketConnection : IDisposable
        {
            private readonly NetworkStream _stream;
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
            private bool _disposed;

            public WebSocketConnection(NetworkStream stream)
            {
                _stream = stream;
            }

            public async Task SendTextAsync(string text)
            {
                await SendFrameAsync(0x1, Encoding.UTF8.GetBytes(text));
            }

            private async Task SendFrameAsync(int opcode, byte[] payload)
            {
                if (_disposed)
                    return;

                byte[] frame = BuildFrame(opcode, payload);
                await _sendLock.WaitAsync();
                try
                {
                    await _stream.WriteAsync(frame);
                    await _stream.FlushAsync();
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            private static byte[] BuildFrame(int opcode, byte[] payload)
            {
                payload = payload ?? Array.Empty<byte>();
                using (var stream = new MemoryStream())
                {
                    stream.WriteByte((byte)(0x80 | opcode));
                    if (payload.Length < 126)
                    {
                        stream.WriteByte((byte)payload.Length);
                    }
                    else if (payload.Length <= 0xFFFF)
                    {
                        stream.WriteByte(126);
                        stream.WriteByte((byte)(payload.Length >> 8));
                        stream.WriteByte((byte)(payload.Length & 0xFF));
                    }
                    else
                    {
                        stream.WriteByte(127);
                        for (int i = 7; i >= 0; i--)
                            stream.WriteByte((byte)(((long)payload.Length >> (8 * i)) & 0xFF));
                    }
                    stream.Write(payload, 0, payload.Length);
                    return stream.ToArray();
                }
            }

            public async Task ReceiveLoopAsync(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] header = await ReadExactAsync(2, token);
                    if (header == null)
                        return;

                    int opcode = header[0] & 0x0F;
                    bool masked = (header[1] & 0x80) != 0;
                    long length = header[1] & 0x7F;

                    if (length == 126)
                    {
                        byte[] extended = await ReadExactAsync(2, token);
                        if (extended == null)
                            return;
                        length = (extended[0] << 8) | extended[1];
                    }
                    else if (length == 127)
                    {
                        byte[] extended = await ReadExactAsync(8, token);
                        if (extended == null)
                            return;
                        length = 0;
                        for (int i = 0; i < 8; i++)
                            length = (length << 8) | extended[i];
                    }

                    if (length < 0 || length > MaxRequestBytes)
                        return;

                    byte[] mask = null;
                    if (masked)
                    {
                        mask = await ReadExactAsync(4, token);
                        if (mask == null)
                            return;
                    }

                    byte[] payload = await ReadExactAsync((int)length, token);
                    if (payload == null)
                        return;
                    if (mask != null)
                    {
                        for (int i = 0; i < payload.Length; i++)
                            payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                    }

                    if (opcode == 0x8) // close
                    {
                        try
                        {
                            await SendFrameAsync(0x8, Array.Empty<byte>());
                        }
                        catch (Exception)
                        {
                        }
                        return;
                    }
                    if (opcode == 0x9) // ping
                        await SendFrameAsync(0xA, payload);
                    // text/binary/pong frames from the browser are intentionally ignored.
                }
            }

            private async Task<byte[]> ReadExactAsync(int count, CancellationToken token)
            {
                if (count == 0)
                    return Array.Empty<byte>();

                byte[] buffer = new byte[count];
                int copied = 0;
                while (copied < count)
                {
                    int read = await _stream.ReadAsync(buffer.AsMemory(copied, count - copied), token);
                    if (read <= 0)
                        return null;
                    copied += read;
                }
                return buffer;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                try
                {
                    _stream.Dispose();
                }
                catch (Exception)
                {
                }
                _sendLock.Dispose();
            }
        }
    }
}
