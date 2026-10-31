// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Parses Telegram deep links (https://t.me/…, tg://…) and Mini App <c>path_full</c> values.
    /// </summary>
    public static class TelegramLinkParser
    {
        /// <summary>Parsed Telegram link target.</summary>
        public sealed class ParsedLink
        {
            public string Username { get; init; }

            public string InviteHash { get; init; }

            /// <summary>Phone digits (international, without leading +) from a phone deep link.</summary>
            public string Phone { get; init; }

            /// <summary>Private channel id from <c>t.me/c/&lt;id&gt;</c> (without -100 prefix).</summary>
            public long PrivateChannelId { get; init; }

            public int MessageId { get; init; }
        }

        /// <summary>Returns true when <paramref name="linkOrPath"/> looks like a Telegram deep link.</summary>
        public static bool IsTelegramLink(string linkOrPath)
        {
            return TryParse(linkOrPath, out _);
        }

        /// <summary>Tries to parse a Telegram URL, <c>tg://</c> URI, or Mini App path.</summary>
        public static bool TryParse(string linkOrPath, out ParsedLink parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(linkOrPath))
                return false;

            string raw = linkOrPath.Trim();
            if (raw.StartsWith("tg:", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("telegram:", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseTgScheme(raw, out parsed);
            }

            if (Uri.TryCreate(raw, UriKind.Absolute, out Uri absolute)
                && (string.Equals(absolute.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(absolute.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                if (!IsTelegramHost(absolute.Host))
                    return false;
                return TryParsePath(absolute.AbsolutePath, absolute.Query, out parsed);
            }

            // Mini App path_full: "/username" or "/+hash"
            if (raw.StartsWith('/'))
                return TryParsePath(raw, null, out parsed);

            // Bare username
            if (IsValidUsername(raw))
            {
                parsed = new ParsedLink { Username = raw };
                return true;
            }

            return false;
        }

        private static bool TryParseTgScheme(string raw, out ParsedLink parsed)
        {
            parsed = null;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri uri))
                return false;

            string host = (uri.Host ?? string.Empty).ToLowerInvariant();
            var query = ParseQuery(uri.Query);

            if (host == "resolve" || host == "user")
            {
                if (query.TryGetValue("phone", out string phone) && IsPhoneDigits(phone))
                {
                    parsed = new ParsedLink { Phone = NormalizePhoneDigits(phone) };
                    return true;
                }

                if (!query.TryGetValue("domain", out string domain) || !IsValidUsername(domain))
                    return false;
                int.TryParse(query.TryGetValue("post", out string post) ? post : null, out int messageId);
                parsed = new ParsedLink { Username = domain, MessageId = messageId };
                return true;
            }

            if (host == "join")
            {
                if (!query.TryGetValue("invite", out string invite) || string.IsNullOrWhiteSpace(invite))
                    return false;
                parsed = new ParsedLink { InviteHash = invite.TrimStart('+') };
                return true;
            }

            if (host == "privatepost")
            {
                if (!long.TryParse(query.TryGetValue("channel", out string ch) ? ch : null, out long channelId)
                    || channelId <= 0)
                    return false;
                int.TryParse(query.TryGetValue("post", out string post) ? post : null, out int messageId);
                parsed = new ParsedLink { PrivateChannelId = channelId, MessageId = messageId };
                return true;
            }

            return false;
        }

        private static bool TryParsePath(string absolutePath, string query, out ParsedLink parsed)
        {
            parsed = null;
            string path = (absolutePath ?? string.Empty).Trim('/');
            if (string.IsNullOrEmpty(path))
                return false;

            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string first = parts[0];

            // t.me/+PHONE (digits only) or t.me/+HASH invite
            if (first.StartsWith('+'))
            {
                string rest = first.TrimStart('+');
                if (IsPhoneDigits(rest))
                {
                    parsed = new ParsedLink { Phone = NormalizePhoneDigits(rest) };
                    return true;
                }

                parsed = new ParsedLink { InviteHash = rest };
                return !string.IsNullOrEmpty(parsed.InviteHash);
            }

            // t.me/phone?… is unsupported; bare international numbers as path are rare.

            if (string.Equals(first, "joinchat", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                parsed = new ParsedLink { InviteHash = parts[1] };
                return !string.IsNullOrEmpty(parsed.InviteHash);
            }

            // t.me/c/<channel_id>[/<msg>]
            if (string.Equals(first, "c", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2
                && long.TryParse(parts[1], out long channelId) && channelId > 0)
            {
                int messageId = 0;
                if (parts.Length >= 3)
                    int.TryParse(parts[2], out messageId);
                parsed = new ParsedLink { PrivateChannelId = channelId, MessageId = messageId };
                return true;
            }

            // t.me/s/username — public preview; open the username itself.
            if (string.Equals(first, "s", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2
                && IsValidUsername(parts[1]))
            {
                parsed = new ParsedLink { Username = parts[1] };
                return true;
            }

            // Unsupported special paths (stickers, proxy, socks, share, …)
            if (IsReservedPath(first))
                return false;

            if (!IsValidUsername(first))
                return false;

            int msgId = 0;
            if (parts.Length >= 2)
                int.TryParse(parts[1], out msgId);

            parsed = new ParsedLink { Username = first, MessageId = msgId };
            return true;
        }

        private static bool IsTelegramHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;
            host = host.Trim().ToLowerInvariant();
            return host == "t.me"
                || host == "www.t.me"
                || host == "telegram.me"
                || host == "www.telegram.me"
                || host == "telegram.dog"
                || host == "www.telegram.dog";
        }

        private static bool IsReservedPath(string segment)
        {
            return string.Equals(segment, "addstickers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "addtheme", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "share", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "proxy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "socks", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "iv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "setlanguage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "bg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 64)
                return false;
            foreach (char c in username)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }

            return true;
        }

        private static bool IsPhoneDigits(string value)
        {
            string digits = NormalizePhoneDigits(value);
            return digits.Length >= 7 && digits.Length <= 15 && digits.All(char.IsDigit);
        }

        private static string NormalizePhoneDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var chars = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsDigit(c))
                    chars.Append(c);
            }
            return chars.ToString();
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
                return map;
            string q = query.StartsWith('?') ? query.Substring(1) : query;
            foreach (string part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = Uri.UnescapeDataString(part.Substring(0, eq));
                string value = Uri.UnescapeDataString(part.Substring(eq + 1));
                map[key] = value;
            }

            return map;
        }
    }
}
