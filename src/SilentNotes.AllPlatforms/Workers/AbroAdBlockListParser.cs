// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Extracts hostnames from DNS / hosts style ad-blocking lists.
    /// Supports hosts files, plain domain lists, and AdGuard-style <c>||domain^</c> lines.
    /// Ignores cosmetic filters and complex path rules.
    /// </summary>
    public static class AbroAdBlockListParser
    {
        // Pure domain DNS rules only: ||example.com^  (optional trailing comment).
        // Rejects optioned rules like ||example.com^$script.
        private static readonly Regex AdGuardDomain = new Regex(
            @"^\|\|([a-z0-9](?:[a-z0-9\-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?)+)\^\s*(?:#.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HostsLine = new Regex(
            @"^(?:0\.0\.0\.0|127\.0\.0\.1|::1?)\s+([^\s#]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PlainDomain = new Regex(
            @"^([a-z0-9](?:[a-z0-9\-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?)+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parses list text into a set of lower-case hostnames.
        /// </summary>
        public static HashSet<string> Parse(string text)
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
                return domains;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '!' || line.StartsWith("["))
                    continue;

                // Strip trailing comments for hosts / plain lines.
                int hash = line.IndexOf('#');
                if (hash >= 0 && !line.StartsWith("||", StringComparison.Ordinal))
                    line = line.Substring(0, hash).Trim();
                if (line.Length == 0)
                    continue;

                Match adguard = AdGuardDomain.Match(line);
                if (adguard.Success)
                {
                    TryAdd(domains, adguard.Groups[1].Value);
                    continue;
                }

                // Skip other Adblock rules (path, element hiding, exceptions with @@, etc.).
                if (line.StartsWith("||", StringComparison.Ordinal)
                    || line.StartsWith("@@", StringComparison.Ordinal)
                    || line.Contains("##")
                    || line.Contains("#@#")
                    || line.StartsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                Match hosts = HostsLine.Match(line);
                if (hosts.Success)
                {
                    TryAdd(domains, hosts.Groups[1].Value);
                    continue;
                }

                Match plain = PlainDomain.Match(line);
                if (plain.Success)
                    TryAdd(domains, plain.Groups[1].Value);
            }

            return domains;
        }

        private static void TryAdd(HashSet<string> domains, string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            host = host.Trim().Trim('.').ToLowerInvariant();
            if (host.Length == 0)
                return;
            if (host == "localhost" || host == "local" || host == "broadcasthost")
                return;
            if (host.IndexOf('.') < 0)
                return;
            if (host.StartsWith("*.", StringComparison.Ordinal))
                host = host.Substring(2);
            if (!IsPlausibleDomain(host))
                return;

            domains.Add(host);
        }

        private static bool IsPlausibleDomain(string host)
        {
            for (int i = 0; i < host.Length; i++)
            {
                char c = host[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-'))
                    return false;
            }

            return host.IndexOf('.') > 0;
        }
    }
}
