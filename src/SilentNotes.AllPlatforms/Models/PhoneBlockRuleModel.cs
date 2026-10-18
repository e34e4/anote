// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;

namespace SilentNotes.Models
{
    /// <summary>
    /// Represents a phone number blocklist rule supporting exact numbers and wildcard masks
    /// such as <c>+7903*</c>, <c>8903*</c>, <c>*1234</c>, or <c>+7903???1234</c>.
    /// </summary>
    public class PhoneBlockRuleModel
    {
        /// <summary>Gets or sets the unique identifier of the rule.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Gets or sets the number pattern or mask (e.g. "+7903*").</summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>Gets or sets an optional user note or label for this rule.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether this rule is active.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Gets or sets how many calls were blocked by this rule.</summary>
        public int BlockedCount { get; set; }

        /// <summary>Gets or sets the UTC timestamp when this rule last blocked a call.</summary>
        public DateTime? LastBlockedAtUtc { get; set; }

        /// <summary>Gets or sets the UTC timestamp when this rule was created.</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Checks whether the specified phone number matches this rule's pattern/mask.
        /// Supports <c>*</c> (any sequence of digits) and <c>?</c> (any single digit),
        /// and treats Russian <c>+7...</c>, <c>7...</c>, and <c>8...</c> prefixes equivalently.
        /// </summary>
        public bool Matches(string phoneNumber)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(Pattern) || string.IsNullOrWhiteSpace(phoneNumber))
                return false;

            return MatchesPattern(Pattern, phoneNumber);
        }

        /// <summary>
        /// Evaluates whether <paramref name="phoneNumber"/> matches the mask <paramref name="rawPattern"/>.
        /// </summary>
        public static bool MatchesPattern(string rawPattern, string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(rawPattern) || string.IsNullOrWhiteSpace(phoneNumber))
                return false;

            string pattern = NormalizeMask(rawPattern);
            string number = NormalizePhoneKeepPlus(phoneNumber);
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(number))
                return false;

            foreach (string candidateNumber in ExpandNumberVariants(number))
            {
                foreach (string candidatePattern in ExpandPatternVariants(pattern))
                {
                    if (WildcardMatch(candidatePattern, candidateNumber))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Normalizes a mask pattern by keeping only digits, leading '+', and wildcards '*' and '?'.
        /// </summary>
        public static string NormalizeMask(string rawPattern)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
                return string.Empty;

            var sb = new StringBuilder(rawPattern.Length);
            foreach (char c in rawPattern.Trim())
            {
                if (char.IsDigit(c) || c == '*' || c == '?')
                    sb.Append(c);
                else if (c == '+' && sb.Length == 0)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Normalizes a phone number keeping only digits and a leading '+'.
        /// </summary>
        public static string NormalizePhoneKeepPlus(string rawNumber)
        {
            if (string.IsNullOrWhiteSpace(rawNumber))
                return string.Empty;

            var sb = new StringBuilder(rawNumber.Length);
            foreach (char c in rawNumber.Trim())
            {
                if (char.IsDigit(c))
                    sb.Append(c);
                else if (c == '+' && sb.Length == 0)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Normalizes a Russian or international number to canonical digits (e.g. "79031234567").
        /// Converts 11-digit Russian "89..." / "84..." / "88..." numbers to "7...".
        /// </summary>
        public static string NormalizeCanonicalDigits(string rawNumber)
        {
            if (string.IsNullOrWhiteSpace(rawNumber))
                return string.Empty;

            var sb = new StringBuilder(rawNumber.Length);
            foreach (char c in rawNumber)
            {
                if (char.IsDigit(c))
                    sb.Append(c);
            }
            string digits = sb.ToString();
            if (digits.Length == 11 && digits[0] == '8')
                digits = "7" + digits.Substring(1);
            else if (digits.Length == 10 && (digits[0] == '9' || digits[0] == '4' || digits[0] == '8' || digits[0] == '3'))
                digits = "7" + digits;
            return digits;
        }

        private static IEnumerable<string> ExpandNumberVariants(string normalized)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(normalized);

            string digits = normalized.StartsWith("+") ? normalized.Substring(1) : normalized;
            if (!string.IsNullOrEmpty(digits))
            {
                set.Add(digits);
                set.Add("+" + digits);

                // Russian +7 / 7 / 8 equivalence
                if (digits.Length == 11 && digits[0] == '7')
                {
                    string national10 = digits.Substring(1);
                    set.Add("8" + national10);
                    set.Add(national10);
                }
                else if (digits.Length == 11 && digits[0] == '8')
                {
                    string national10 = digits.Substring(1);
                    set.Add("7" + national10);
                    set.Add("+7" + national10);
                    set.Add(national10);
                }
                else if (digits.Length == 10 && (digits[0] == '9' || digits[0] == '4' || digits[0] == '3' || digits[0] == '8'))
                {
                    set.Add("7" + digits);
                    set.Add("+7" + digits);
                    set.Add("8" + digits);
                }
            }
            return set;
        }

        private static IEnumerable<string> ExpandPatternVariants(string pattern)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(pattern);

            string noPlus = pattern.StartsWith("+") ? pattern.Substring(1) : pattern;
            if (!string.IsNullOrEmpty(noPlus))
            {
                set.Add(noPlus);
                set.Add("+" + noPlus);

                if (noPlus.StartsWith("7") && noPlus.Length >= 2)
                {
                    set.Add("8" + noPlus.Substring(1));
                }
                else if (noPlus.StartsWith("8") && noPlus.Length >= 2)
                {
                    set.Add("7" + noPlus.Substring(1));
                    set.Add("+7" + noPlus.Substring(1));
                }
            }
            return set;
        }

        private static bool WildcardMatch(string pattern, string text)
        {
            int p = 0, t = 0;
            int starIdx = -1, matchIdx = 0;

            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
                {
                    p++;
                    t++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    starIdx = p;
                    matchIdx = t;
                    p++;
                }
                else if (starIdx != -1)
                {
                    p = starIdx + 1;
                    matchIdx++;
                    t = matchIdx;
                }
                else
                {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*')
                p++;

            return p == pattern.Length;
        }
    }
}
