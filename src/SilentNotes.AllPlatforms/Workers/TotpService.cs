// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace SilentNotes.Workers
{
    /// <summary>
    /// RFC 6238 TOTP generator (HMAC-SHA1, default 30s / 6 digits).
    /// </summary>
    public static class TotpService
    {
        /// <summary>
        /// Parses a raw Base32 secret or an otpauth:// URI into secret + parameters.
        /// </summary>
        public static bool TryParseSecret(
            string secretOrUri,
            out byte[] secret,
            out int period,
            out int digits)
        {
            secret = null;
            period = 30;
            digits = 6;
            if (string.IsNullOrWhiteSpace(secretOrUri))
                return false;

            string trimmed = secretOrUri.Trim();
            string secretText = trimmed;

            if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
                    return false;
                Dictionary<string, string> query = ParseQuery(uri.Query);
                query.TryGetValue("secret", out secretText);
                if (query.TryGetValue("period", out string periodText) &&
                    int.TryParse(periodText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p > 0)
                    period = p;
                if (query.TryGetValue("digits", out string digitsText) &&
                    int.TryParse(digitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d > 0)
                    digits = d;
            }

            if (string.IsNullOrWhiteSpace(secretText))
                return false;

            try
            {
                secret = Base32Decode(secretText);
                return secret != null && secret.Length > 0;
            }
            catch
            {
                secret = null;
                return false;
            }
        }

        /// <summary>
        /// Generates the current TOTP code for the given secret parameters.
        /// </summary>
        public static string GenerateCode(byte[] secret, int period, int digits, DateTime utcNow)
        {
            if (secret == null || secret.Length == 0)
                return null;
            if (period <= 0)
                period = 30;
            if (digits <= 0)
                digits = 6;

            long timestep = (long)Math.Floor((utcNow - DateTime.UnixEpoch).TotalSeconds / period);
            byte[] counter = BitConverter.GetBytes(timestep);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counter);

            using (var hmac = new HMACSHA1(secret))
            {
                byte[] hash = hmac.ComputeHash(counter);
                int offset = hash[hash.Length - 1] & 0x0F;
                int binary =
                    ((hash[offset] & 0x7F) << 24) |
                    ((hash[offset + 1] & 0xFF) << 16) |
                    ((hash[offset + 2] & 0xFF) << 8) |
                    (hash[offset + 3] & 0xFF);
                int otp = binary % (int)Math.Pow(10, digits);
                return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
            }
        }

        /// <summary>
        /// Generates a code from a stored secret string (Base32 or otpauth URI).
        /// </summary>
        public static string GenerateCode(string secretOrUri, int period, int digits, DateTime utcNow)
        {
            if (!TryParseSecret(secretOrUri, out byte[] secret, out int parsedPeriod, out int parsedDigits))
                return null;
            if (period <= 0)
                period = parsedPeriod;
            if (digits <= 0)
                digits = parsedDigits;
            return GenerateCode(secret, period, digits, utcNow);
        }

        /// <summary>
        /// Seconds remaining in the current TOTP period.
        /// </summary>
        public static int SecondsRemaining(int period, DateTime utcNow)
        {
            if (period <= 0)
                period = 30;
            int elapsed = (int)((utcNow - DateTime.UnixEpoch).TotalSeconds % period);
            return period - elapsed;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
                return result;
            string trimmed = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            foreach (string pair in trimmed.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = Uri.UnescapeDataString(pair.Substring(0, eq));
                string value = Uri.UnescapeDataString(pair.Substring(eq + 1));
                result[key] = value;
            }
            return result;
        }

        private static byte[] Base32Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            string cleaned = input.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
            if (cleaned.EndsWith("=", StringComparison.Ordinal))
                cleaned = cleaned.TrimEnd('=');

            var output = new byte[cleaned.Length * 5 / 8];
            int bitBuffer = 0;
            int bitsInBuffer = 0;
            int index = 0;

            foreach (char c in cleaned)
            {
                int value = alphabet.IndexOf(c);
                if (value < 0)
                    throw new FormatException("Invalid Base32 character.");
                bitBuffer = (bitBuffer << 5) | value;
                bitsInBuffer += 5;
                if (bitsInBuffer >= 8)
                {
                    bitsInBuffer -= 8;
                    output[index++] = (byte)((bitBuffer >> bitsInBuffer) & 0xFF);
                }
            }

            if (index != output.Length)
                Array.Resize(ref output, index);
            return output;
        }
    }
}
