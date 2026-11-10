// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace SilentNotes.Models
{
    /// <summary>
    /// How Abro applies the forced black background / white text theme.
    /// </summary>
    public enum AbroBlackMode
    {
        /// <summary>Show pages without color modifications.</summary>
        Disable = 0,

        /// <summary>Force black background and white text on every page.</summary>
        Enable = 1,

        /// <summary>Force the theme only on listed domains.</summary>
        Selectively = 2,
    }

    /// <summary>
    /// How Abro blocks network images on pages.
    /// </summary>
    public enum AbroBlockImagesMode
    {
        /// <summary>Load images normally.</summary>
        Disable = 0,

        /// <summary>Block images on every page.</summary>
        Enable = 1,

        /// <summary>Block images only on listed domains.</summary>
        Selectively = 2,
    }

    /// <summary>
    /// Local Abro browser settings: search engines, custom font, and black mode.
    /// </summary>
    public class AbroBrowserSettingsModel
    {
        /// <summary>Well-known intercept URL used to serve the copied custom font to pages.</summary>
        public const string CustomFontInterceptUrl = "https://abro.internal/custom-font";

        /// <summary>CSS / @font-face family name for the custom font.</summary>
        public const string CustomFontFamilyName = "AbroCustom";

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroBrowserSettingsModel"/> class.
        /// </summary>
        public AbroBrowserSettingsModel()
        {
            Version = 1;
            UpdatedAt = DateTime.UtcNow;
            SearchSuggestionsEnabled = true;
            Engines = new List<AbroSearchEngineModel>();
            UseCustomFont = false;
            CustomFontFileName = string.Empty;
            BlackMode = AbroBlackMode.Disable;
            BlackModeDomains = new List<string>();
            BlockImagesMode = AbroBlockImagesMode.Disable;
            BlockImagesDomains = new List<string>();
            Socks5Enabled = false;
            Socks5Host = string.Empty;
            Socks5Port = 1080;
            Socks5Username = string.Empty;
            Socks5Password = string.Empty;
        }

        /// <summary>Gets or sets the store format version.</summary>
        public int Version { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the last change.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>Gets or sets whether address-bar search suggestions are enabled.</summary>
        public bool SearchSuggestionsEnabled { get; set; }

        /// <summary>Gets or sets the id of the default search engine.</summary>
        public Guid? DefaultEngineId { get; set; }

        /// <summary>Gets or sets the configured search engines.</summary>
        public List<AbroSearchEngineModel> Engines { get; set; }

        /// <summary>Gets or sets whether pages should use the copied custom TTF.</summary>
        public bool UseCustomFont { get; set; }

        /// <summary>
        /// Gets or sets the file name of the copied font under <c>abro/fonts/</c>
        /// (e.g. <c>custom.ttf</c>). Empty when using the system font.
        /// </summary>
        public string CustomFontFileName { get; set; }

        /// <summary>Gets or sets whether pages get a forced black / white color theme.</summary>
        public AbroBlackMode BlackMode { get; set; }

        /// <summary>
        /// Gets or sets hostnames that receive black mode when
        /// <see cref="BlackMode"/> is <see cref="AbroBlackMode.Selectively"/>.
        /// </summary>
        public List<string> BlackModeDomains { get; set; }

        /// <summary>Gets or sets whether Abro blocks network images.</summary>
        public AbroBlockImagesMode BlockImagesMode { get; set; }

        /// <summary>
        /// Gets or sets hostnames that block images when
        /// <see cref="BlockImagesMode"/> is <see cref="AbroBlockImagesMode.Selectively"/>.
        /// </summary>
        public List<string> BlockImagesDomains { get; set; }

        /// <summary>
        /// Legacy convenience: true when images are blocked on every page.
        /// Prefer <see cref="BlockImagesMode"/> / <see cref="ShouldApplyBlockImages"/>.
        /// </summary>
        public bool BlockImages
        {
            get => BlockImagesMode == AbroBlockImagesMode.Enable;
            set => BlockImagesMode = value ? AbroBlockImagesMode.Enable : AbroBlockImagesMode.Disable;
        }

        /// <summary>When true and host/port are set, Abro WebView traffic uses this SOCKS5 proxy.</summary>
        public bool Socks5Enabled { get; set; }

        /// <summary>SOCKS5 proxy hostname or IP.</summary>
        public string Socks5Host { get; set; }

        /// <summary>SOCKS5 proxy port (1–65535).</summary>
        public int Socks5Port { get; set; }

        /// <summary>Optional SOCKS5 username.</summary>
        public string Socks5Username { get; set; }

        /// <summary>Optional SOCKS5 password.</summary>
        public string Socks5Password { get; set; }

        /// <summary>True when host and port look usable for SOCKS5.</summary>
        public bool HasSocks5Config =>
            !string.IsNullOrWhiteSpace(Socks5Host)
            && Socks5Port > 0
            && Socks5Port <= 65535;

        /// <summary>
        /// Touches <see cref="UpdatedAt"/> to now (UTC).
        /// </summary>
        public void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Returns whether the forced black theme should apply for <paramref name="pageUrl"/>.
        /// </summary>
        public bool ShouldApplyBlackMode(string pageUrl)
        {
            return ShouldApplyDomainMode(pageUrl, BlackMode == AbroBlackMode.Disable, BlackMode == AbroBlackMode.Enable, BlackModeDomains);
        }

        /// <summary>
        /// Returns whether image blocking should apply for <paramref name="pageUrl"/>.
        /// </summary>
        public bool ShouldApplyBlockImages(string pageUrl)
        {
            return ShouldApplyDomainMode(
                pageUrl,
                BlockImagesMode == AbroBlockImagesMode.Disable,
                BlockImagesMode == AbroBlockImagesMode.Enable,
                BlockImagesDomains);
        }

        private static bool ShouldApplyDomainMode(
            string pageUrl,
            bool disabled,
            bool enabledEverywhere,
            List<string> selectiveDomains)
        {
            if (disabled)
                return false;
            if (enabledEverywhere)
                return true;

            string host = TryGetHost(pageUrl);
            if (string.IsNullOrEmpty(host))
                return false;

            host = NormalizeDomain(host);
            if (selectiveDomains == null || selectiveDomains.Count == 0)
                return false;

            foreach (string entry in selectiveDomains)
            {
                string domain = NormalizeDomain(entry);
                if (string.IsNullOrEmpty(domain))
                    continue;
                if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Normalizes a domain for storage / matching (lowercase, no scheme/path/www).</summary>
        public static string NormalizeDomain(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string text = value.Trim().ToLowerInvariant();
            if (text.StartsWith("https://", StringComparison.Ordinal))
                text = text.Substring("https://".Length);
            else if (text.StartsWith("http://", StringComparison.Ordinal))
                text = text.Substring("http://".Length);

            int slash = text.IndexOf('/');
            if (slash >= 0)
                text = text.Substring(0, slash);
            int colon = text.IndexOf(':');
            if (colon >= 0)
                text = text.Substring(0, colon);
            if (text.StartsWith("www.", StringComparison.Ordinal))
                text = text.Substring(4);
            return text.Trim().Trim('.');
        }

        private static string TryGetHost(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
                return null;
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri uri))
                return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return null;
            return uri.Host;
        }

        /// <summary>
        /// Returns the default engine, falling back to the first entry, or null if empty.
        /// </summary>
        public AbroSearchEngineModel GetDefaultEngine()
        {
            if (Engines == null || Engines.Count == 0)
                return null;

            if (DefaultEngineId.HasValue)
            {
                AbroSearchEngineModel match = Engines.Find(e => e.Id == DefaultEngineId.Value);
                if (match != null)
                    return match;
            }

            return Engines[0];
        }

        /// <summary>
        /// Seeds built-in engines when the list is empty.
        /// </summary>
        public void EnsureDefaultEngines()
        {
            if (Engines == null)
                Engines = new List<AbroSearchEngineModel>();
            if (Engines.Count > 0)
                return;

            var ddg = new AbroSearchEngineModel
            {
                Name = "DuckDuckGo",
                SearchUrl = "https://duckduckgo.com/?q={query}",
                SuggestUrl = "https://duckduckgo.com/ac/?q={query}&type=list",
            };
            var google = new AbroSearchEngineModel
            {
                Name = "Google",
                SearchUrl = "https://www.google.com/search?q={query}",
                SuggestUrl = "https://suggestqueries.google.com/complete/search?client=firefox&q={query}",
            };
            var bing = new AbroSearchEngineModel
            {
                Name = "Bing",
                SearchUrl = "https://www.bing.com/search?q={query}",
                SuggestUrl = "https://api.bing.com/osjson.aspx?query={query}",
            };
            Engines.Add(ddg);
            Engines.Add(google);
            Engines.Add(bing);
            DefaultEngineId = ddg.Id;
        }
    }
}
