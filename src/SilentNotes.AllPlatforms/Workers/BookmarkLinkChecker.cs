// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SilentNotes.Models;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Known values for <see cref="BookmarkModel.LinkCheckStatus"/>.
    /// </summary>
    public static class BookmarkLinkCheckStatuses
    {
        public const string Unknown = "unknown";
        public const string Ok = "ok";
        public const string Broken = "broken";
        public const string Redirect = "redirect";
    }

    /// <summary>
    /// Result of an HTTP reachability check for a bookmark URL.
    /// </summary>
    public sealed class BookmarkLinkCheckResult
    {
        public string Status { get; set; } = BookmarkLinkCheckStatuses.Unknown;
        public int HttpStatus { get; set; }
        public bool IsBroken => string.Equals(Status, BookmarkLinkCheckStatuses.Broken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HEAD (with GET fallback) checks for Booky bookmark URLs.
    /// </summary>
    public static class BookmarkLinkChecker
    {
        private const int RequestTimeoutSeconds = 8;
        private const int MaxParallelChecks = 4;

        private static readonly HttpClient SharedClient = CreateClient();

        /// <summary>
        /// Checks whether <paramref name="url"/> is reachable and classifies the HTTP response.
        /// </summary>
        public static async Task<BookmarkLinkCheckResult> CheckUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            var result = new BookmarkLinkCheckResult();
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
            {
                result.Status = BookmarkLinkCheckStatuses.Broken;
                return result;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = BookmarkLinkCheckStatuses.Ok;
                return result;
            }

            HttpResponseMessage response = null;
            try
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, uri);
                response = await SharedClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.BadRequest)
                {
                    response.Dispose();
                    using var getReq = new HttpRequestMessage(HttpMethod.Get, uri);
                    response = await SharedClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                result.Status = BookmarkLinkCheckStatuses.Broken;
                result.HttpStatus = 0;
                return result;
            }

            try
            {
                int code = (int)response.StatusCode;
                result.HttpStatus = code;
                result.Status = ClassifyStatusCode(code);
                return result;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Applies a check result to a bookmark model and sets <see cref="BookmarkModel.LinkCheckedAt"/>.
        /// </summary>
        public static void ApplyResult(BookmarkModel bookmark, BookmarkLinkCheckResult result)
        {
            if (bookmark == null)
                throw new ArgumentNullException(nameof(bookmark));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            bookmark.LinkCheckStatus = result.Status ?? BookmarkLinkCheckStatuses.Unknown;
            bookmark.LinkHttpStatus = result.HttpStatus;
            bookmark.LinkCheckedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Checks all bookmarks in parallel (limited concurrency) and updates each model.
        /// </summary>
        public static async Task CheckBookmarksAsync(
            IEnumerable<BookmarkModel> bookmarks,
            CancellationToken cancellationToken = default)
        {
            if (bookmarks == null)
                return;

            using var gate = new SemaphoreSlim(MaxParallelChecks);
            var tasks = new List<Task>();

            foreach (BookmarkModel bookmark in bookmarks)
            {
                if (bookmark == null || string.IsNullOrWhiteSpace(bookmark.Url))
                    continue;

                await gate.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        BookmarkLinkCheckResult result = await CheckUrlAsync(bookmark.Url, cancellationToken);
                        ApplyResult(bookmark, result);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        private static string ClassifyStatusCode(int code)
        {
            if (code >= 200 && code < 300)
                return BookmarkLinkCheckStatuses.Ok;
            if (code >= 300 && code < 400)
                return BookmarkLinkCheckStatuses.Redirect;
            if (code == 404 || code == 410)
                return BookmarkLinkCheckStatuses.Broken;
            if (code >= 400)
                return BookmarkLinkCheckStatuses.Ok;
            return BookmarkLinkCheckStatuses.Unknown;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");
            return client;
        }
    }
}