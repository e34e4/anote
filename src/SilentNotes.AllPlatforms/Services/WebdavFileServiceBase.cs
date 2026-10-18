// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using SilentNotes.Models;

namespace SilentNotes.Services
{
    /// <summary>
    /// Base implementation of <see cref="IWebdavFileService"/> providing WebDAV protocol operations
    /// (PROPFIND, GET, PUT, DELETE, MKCOL) over SocketsHttpHandler.
    /// </summary>
    public abstract class WebdavFileServiceBase : IWebdavFileService
    {
        private const string PropFindRequestXml = @"<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
  </D:prop>
</D:propfind>";

        /// <inheritdoc/>
        public async Task<IReadOnlyList<WebdavFileItemModel>> ListDirectoryAsync(
            string baseUrl, string remoteFolder, string username, string password)
        {
            string dirUrl = BuildTargetUrl(baseUrl, remoteFolder, null, ensureTrailingSlash: true);
            using HttpClient client = CreateHttpClient(username, password);
            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), dirUrl);
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(PropFindRequestXml));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using Stream stream = await response.Content.ReadAsStreamAsync();
            XDocument xml = XDocument.Load(stream);
            return ParsePropFindResponse(xml, dirUrl);
        }

        /// <inheritdoc/>
        public async Task UploadFileAsync(
            string baseUrl, string remoteFolder, string fileName, byte[] content, string username, string password)
        {
            string fileUrl = BuildTargetUrl(baseUrl, remoteFolder, fileName, ensureTrailingSlash: false);
            using HttpClient client = CreateHttpClient(username, password, timeoutSeconds: 120);
            using var requestContent = new ByteArrayContent(content ?? Array.Empty<byte>());
            using HttpResponseMessage response = await client.PutAsync(fileUrl, requestContent);
            response.EnsureSuccessStatusCode();
        }

        /// <inheritdoc/>
        public async Task<byte[]> DownloadFileAsync(
            string baseUrl, string remoteFolder, string fileName, string username, string password)
        {
            string fileUrl = BuildTargetUrl(baseUrl, remoteFolder, fileName, ensureTrailingSlash: false);
            using HttpClient client = CreateHttpClient(username, password, timeoutSeconds: 120);
            using HttpResponseMessage response = await client.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <inheritdoc/>
        public async Task DeleteItemAsync(
            string baseUrl, string remoteFolder, string itemName, bool isDirectory, string username, string password)
        {
            string targetUrl = BuildTargetUrl(baseUrl, remoteFolder, itemName, ensureTrailingSlash: isDirectory);
            using HttpClient client = CreateHttpClient(username, password);
            using HttpResponseMessage response = await client.DeleteAsync(targetUrl);
            response.EnsureSuccessStatusCode();
        }

        /// <inheritdoc/>
        public async Task CreateDirectoryAsync(
            string baseUrl, string remoteFolder, string newFolderName, string username, string password)
        {
            string folderUrl = BuildTargetUrl(baseUrl, remoteFolder, newFolderName, ensureTrailingSlash: true);
            using HttpClient client = CreateHttpClient(username, password);
            using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), folderUrl);
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <inheritdoc/>
        public abstract Task<string> SaveDownloadedFileAsync(string fileName, byte[] content);

        /// <inheritdoc/>
        public abstract Task<bool> ShareDownloadedFileAsync(string fileName, byte[] content);

        internal static List<WebdavFileItemModel> ParsePropFindResponse(XDocument responseXml, string requestedDirUrl)
        {
            var result = new List<WebdavFileItemModel>();
            if (responseXml == null)
                return result;

            string requestedPath = ExtractNormalizedPath(requestedDirUrl);

            IEnumerable<XElement> responses = responseXml
                .Descendants()
                .Where(el => string.Equals("response", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));

            bool isFirst = true;
            foreach (XElement responseEl in responses)
            {
                XElement hrefEl = responseEl
                    .Elements()
                    .FirstOrDefault(el => string.Equals("href", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                if (hrefEl == null || string.IsNullOrWhiteSpace(hrefEl.Value))
                    continue;

                string rawHref = hrefEl.Value.Trim();
                string itemPath = ExtractNormalizedPath(rawHref);

                if (string.Equals(itemPath, requestedPath, StringComparison.OrdinalIgnoreCase) ||
                    (isFirst && string.IsNullOrEmpty(requestedPath)))
                {
                    isFirst = false;
                    continue;
                }
                isFirst = false;

                string trimmed = itemPath.TrimEnd('/');
                int lastSlash = trimmed.LastIndexOf('/');
                string name = (lastSlash >= 0) ? trimmed.Substring(lastSlash + 1) : trimmed;
                name = Uri.UnescapeDataString(name);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                XElement resourceTypeEl = responseEl
                    .Descendants()
                    .FirstOrDefault(el => string.Equals("resourcetype", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                bool isDir = resourceTypeEl != null && resourceTypeEl
                    .Descendants()
                    .Any(el => string.Equals("collection", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));

                long sizeBytes = 0;
                XElement lengthEl = responseEl
                    .Descendants()
                    .FirstOrDefault(el => string.Equals("getcontentlength", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                if (lengthEl != null)
                    long.TryParse(lengthEl.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sizeBytes);

                DateTime? lastModified = null;
                XElement modEl = responseEl
                    .Descendants()
                    .FirstOrDefault(el => string.Equals("getlastmodified", el.Name.LocalName, StringComparison.OrdinalIgnoreCase));
                if (modEl != null && DateTimeOffset.TryParse(modEl.Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto))
                    lastModified = dto.UtcDateTime;

                result.Add(new WebdavFileItemModel
                {
                    Name = name,
                    IsDirectory = isDir,
                    SizeBytes = sizeBytes,
                    LastModifiedUtc = lastModified,
                });
            }

            return result
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractNormalizedPath(string urlOrHref)
        {
            if (string.IsNullOrWhiteSpace(urlOrHref))
                return string.Empty;

            string path = urlOrHref;
            if (Uri.TryCreate(urlOrHref, UriKind.Absolute, out Uri absUri))
                path = absUri.AbsolutePath;

            return Uri.UnescapeDataString(path).TrimEnd('/');
        }

        private static string BuildTargetUrl(string baseUrl, string remoteFolder, string itemName, bool ensureTrailingSlash)
        {
            string url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(remoteFolder))
            {
                string[] folderParts = remoteFolder.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in folderParts)
                    url += "/" + Uri.EscapeDataString(part);
            }

            if (!string.IsNullOrWhiteSpace(itemName))
                url += "/" + Uri.EscapeDataString(itemName.Trim('/'));

            if (ensureTrailingSlash && !url.EndsWith('/'))
                url += "/";

            return url;
        }

        private static HttpClient CreateHttpClient(string username, string password, int timeoutSeconds = 30)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                },
            };

            if (!string.IsNullOrEmpty(username))
                handler.Credentials = new NetworkCredential(username, password ?? string.Empty);

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };

            if (!string.IsNullOrEmpty(username))
            {
                byte[] authBytes = Encoding.UTF8.GetBytes(username + ":" + (password ?? string.Empty));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            }

            return client;
        }
    }
}
