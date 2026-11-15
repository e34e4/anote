// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VanillaCloudStorageClient.CloudStorageProviders
{
    /// <summary>
    /// Uses another aNote device in the same Wi-Fi as storage. The device must run the LAN bridge
    /// (Settings → LAN bridge), which exposes <c>/api/repository</c> for the encrypted repository
    /// container. The credentials are the base url of the peer and its access token as password.
    /// </summary>
    public class LanCloudStorageClient : ICloudStorageClient
    {
        private const string RepositoryPath = "/api/repository";
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        /// <inheritdoc/>
        public CloudStorageCredentialsRequirements CredentialsRequirements
            => CloudStorageCredentialsRequirements.Url | CloudStorageCredentialsRequirements.Password;

        /// <inheritdoc/>
        public async Task UploadFileAsync(string filename, byte[] fileContent, CloudStorageCredentials credentials)
        {
            credentials.ThrowIfInvalid(CredentialsRequirements);
            using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(credentials));
            request.Headers.TryAddWithoutValidation("X-Anote-Token", GetToken(credentials));
            request.Content = new ByteArrayContent(fileContent ?? Array.Empty<byte>());
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using HttpResponseMessage response = await SendAsync(request).ConfigureAwait(false);
            EnsureSuccess(response);
        }

        /// <inheritdoc/>
        public async Task<byte[]> DownloadFileAsync(string filename, CloudStorageCredentials credentials)
        {
            credentials.ThrowIfInvalid(CredentialsRequirements);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(credentials));
            request.Headers.TryAddWithoutValidation("X-Anote-Token", GetToken(credentials));
            using HttpResponseMessage response = await SendAsync(request).ConfigureAwait(false);
            EnsureSuccess(response);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DeleteFileAsync(string filename, CloudStorageCredentials credentials)
        {
            credentials.ThrowIfInvalid(CredentialsRequirements);
            using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(credentials));
            request.Headers.TryAddWithoutValidation("X-Anote-Token", GetToken(credentials));
            using HttpResponseMessage response = await SendAsync(request).ConfigureAwait(false);
            EnsureSuccess(response);
        }

        /// <inheritdoc/>
        public async Task<List<string>> ListFileNamesAsync(CloudStorageCredentials credentials)
        {
            var result = new List<string>();
            if (await ExistsFileAsync("repository", credentials).ConfigureAwait(false))
                result.Add("repository");
            return result;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsFileAsync(string filename, CloudStorageCredentials credentials)
        {
            credentials.ThrowIfInvalid(CredentialsRequirements);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(credentials) + "/status");
            request.Headers.TryAddWithoutValidation("X-Anote-Token", GetToken(credentials));
            using HttpResponseMessage response = await SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;
            EnsureSuccess(response);

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty("exists", out JsonElement exists)
                    && exists.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string BuildUrl(CloudStorageCredentials credentials)
        {
            string url = (credentials.Url ?? string.Empty).Trim().TrimEnd('/');
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }
            return url + RepositoryPath;
        }

        private static string GetToken(CloudStorageCredentials credentials)
        {
            return credentials.UnprotectedPassword ?? string.Empty;
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            try
            {
                return await Client.SendAsync(request).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                throw new ConnectionFailedException("The LAN device did not answer in time.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ConnectionFailedException("Could not reach the LAN device.", ex);
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new AccessDeniedException();
            }

            throw new CloudStorageException(
                "The LAN device answered with " + (int)response.StatusCode + " " + response.ReasonPhrase + ".",
                null);
        }
    }
}
