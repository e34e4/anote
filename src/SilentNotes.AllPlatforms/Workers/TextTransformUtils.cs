// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Enumeration of the text transformations which can be applied by
    /// <see cref="TextTransformUtils"/>.
    /// </summary>
    public enum TextTransformKind
    {
        Base64Encode,
        Base64Decode,
        UrlEncode,
        UrlDecode,
        JsonPrettyPrint,
        JsonMinify,
        XmlPrettyPrint,
        XmlMinify,
        Sha256Hash,
        Md5Hash,
        SortLinesAscending,
        SortLinesDescending,
        DeduplicateLines,
        RemoveEmptyLines,
        RegexExtract,
        Uppercase,
        Lowercase,
    }

    /// <summary>
    /// A single step of a text transformation pipeline. Some transformations (currently only
    /// <see cref="TextTransformKind.RegexExtract"/>) require an additional parameter.
    /// </summary>
    public class TextTransformStep
    {
        /// <summary>Gets or sets the transformation to apply.</summary>
        public TextTransformKind Kind { get; set; }

        /// <summary>Gets or sets the optional parameter, e.g. the regular expression pattern.</summary>
        public string Parameter { get; set; }
    }

    /// <summary>
    /// Exception which is thrown when a text transformation cannot be applied, e.g. because the
    /// input is not valid Base64 or the regular expression is invalid.
    /// </summary>
    public class TextTransformException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="TextTransformException"/> class.</summary>
        /// <param name="message">The error message.</param>
        public TextTransformException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Collection of reversible and converting text operations, used by the batch text-processing
    /// sheet. All functions work on plain text and are independent of the user interface.
    /// </summary>
    public static class TextTransformUtils
    {
        private static readonly JsonSerializerOptions PrettyJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        /// <summary>
        /// Gets all available transformation kinds in a sensible order for the user interface.
        /// </summary>
        public static IReadOnlyList<TextTransformKind> AllKinds { get; } = new[]
        {
            TextTransformKind.Base64Encode,
            TextTransformKind.Base64Decode,
            TextTransformKind.UrlEncode,
            TextTransformKind.UrlDecode,
            TextTransformKind.JsonPrettyPrint,
            TextTransformKind.JsonMinify,
            TextTransformKind.XmlPrettyPrint,
            TextTransformKind.XmlMinify,
            TextTransformKind.Sha256Hash,
            TextTransformKind.Md5Hash,
            TextTransformKind.SortLinesAscending,
            TextTransformKind.SortLinesDescending,
            TextTransformKind.DeduplicateLines,
            TextTransformKind.RemoveEmptyLines,
            TextTransformKind.RegexExtract,
            TextTransformKind.Uppercase,
            TextTransformKind.Lowercase,
        };

        /// <summary>
        /// Determines whether the given transformation needs a parameter to work.
        /// </summary>
        /// <param name="kind">The transformation to check.</param>
        /// <returns>True if a parameter is required, otherwise false.</returns>
        public static bool RequiresParameter(TextTransformKind kind)
        {
            return kind == TextTransformKind.RegexExtract;
        }

        /// <summary>
        /// Gets a short human readable name for a transformation.
        /// </summary>
        /// <param name="kind">The transformation.</param>
        /// <returns>Display name.</returns>
        public static string GetDisplayName(TextTransformKind kind)
        {
            switch (kind)
            {
                case TextTransformKind.Base64Encode: return "Base64 encode";
                case TextTransformKind.Base64Decode: return "Base64 decode";
                case TextTransformKind.UrlEncode: return "URL encode";
                case TextTransformKind.UrlDecode: return "URL decode";
                case TextTransformKind.JsonPrettyPrint: return "JSON pretty-print";
                case TextTransformKind.JsonMinify: return "JSON minify";
                case TextTransformKind.XmlPrettyPrint: return "XML pretty-print";
                case TextTransformKind.XmlMinify: return "XML minify";
                case TextTransformKind.Sha256Hash: return "SHA-256";
                case TextTransformKind.Md5Hash: return "MD5";
                case TextTransformKind.SortLinesAscending: return "Sort lines A-Z";
                case TextTransformKind.SortLinesDescending: return "Sort lines Z-A";
                case TextTransformKind.DeduplicateLines: return "Deduplicate lines";
                case TextTransformKind.RemoveEmptyLines: return "Remove empty lines";
                case TextTransformKind.RegexExtract: return "Regex extract";
                case TextTransformKind.Uppercase: return "UPPERCASE";
                case TextTransformKind.Lowercase: return "lowercase";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// Applies a whole pipeline of transformations, one after the other.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <param name="steps">The transformations to apply, in order.</param>
        /// <param name="output">Receives the transformed text.</param>
        /// <param name="error">Receives the error message if a step failed.</param>
        /// <returns>True if all steps succeeded, otherwise false.</returns>
        public static bool TryApply(string text, IEnumerable<TextTransformStep> steps, out string output, out string error)
        {
            output = text ?? string.Empty;
            error = null;
            if (steps == null)
                return true;

            foreach (TextTransformStep step in steps)
            {
                if (step == null)
                    continue;

                try
                {
                    output = ApplyStep(output, step);
                }
                catch (TextTransformException ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Applies a single transformation.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <param name="step">The transformation to apply.</param>
        /// <returns>The transformed text.</returns>
        /// <exception cref="TextTransformException">Thrown when the transformation cannot be applied.</exception>
        public static string ApplyStep(string text, TextTransformStep step)
        {
            text = text ?? string.Empty;
            switch (step.Kind)
            {
                case TextTransformKind.Base64Encode:
                    return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                case TextTransformKind.Base64Decode:
                    return DecodeBase64(text);
                case TextTransformKind.UrlEncode:
                    return Uri.EscapeDataString(text);
                case TextTransformKind.UrlDecode:
                    return Uri.UnescapeDataString(text.Replace("+", " "));
                case TextTransformKind.JsonPrettyPrint:
                    return PrettyPrintJson(text);
                case TextTransformKind.JsonMinify:
                    return MinifyJson(text);
                case TextTransformKind.XmlPrettyPrint:
                    return PrettyPrintXml(text);
                case TextTransformKind.XmlMinify:
                    return MinifyXml(text);
                case TextTransformKind.Sha256Hash:
                    return Hash(text, SHA256.Create());
                case TextTransformKind.Md5Hash:
                    return Hash(text, MD5.Create());
                case TextTransformKind.SortLinesAscending:
                    return SortLines(text, descending: false);
                case TextTransformKind.SortLinesDescending:
                    return SortLines(text, descending: true);
                case TextTransformKind.DeduplicateLines:
                    return DeduplicateLines(text);
                case TextTransformKind.RemoveEmptyLines:
                    return RemoveEmptyLines(text);
                case TextTransformKind.RegexExtract:
                    return RegexExtract(text, step.Parameter);
                case TextTransformKind.Uppercase:
                    return text.ToUpperInvariant();
                case TextTransformKind.Lowercase:
                    return text.ToLowerInvariant();
                default:
                    throw new TextTransformException("Unknown transformation: " + step.Kind);
            }
        }

        private static string DecodeBase64(string text)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(text.Trim());
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                throw new TextTransformException("The text is not valid Base64.");
            }
        }

        private static string PrettyPrintJson(string text)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(text))
                {
                    return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
                }
            }
            catch (JsonException ex)
            {
                throw new TextTransformException("Invalid JSON: " + ex.Message);
            }
        }

        private static string MinifyJson(string text)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(text))
                {
                    return JsonSerializer.Serialize(document.RootElement);
                }
            }
            catch (JsonException ex)
            {
                throw new TextTransformException("Invalid JSON: " + ex.Message);
            }
        }

        private static string PrettyPrintXml(string text)
        {
            try
            {
                XDocument document = XDocument.Parse(text, LoadOptions.None);
                return document.ToString();
            }
            catch (Exception ex)
            {
                throw new TextTransformException("Invalid XML: " + ex.Message);
            }
        }

        private static string MinifyXml(string text)
        {
            try
            {
                XDocument document = XDocument.Parse(text, LoadOptions.None);
                return document.ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex)
            {
                throw new TextTransformException("Invalid XML: " + ex.Message);
            }
        }

        private static string Hash(string text, HashAlgorithm algorithm)
        {
            using (algorithm)
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static string SortLines(string text, bool descending)
        {
            string[] lines = SplitLines(text);
            IOrderedEnumerable<string> sorted = descending
                ? lines.OrderByDescending(line => line, StringComparer.Ordinal)
                : lines.OrderBy(line => line, StringComparer.Ordinal);
            return string.Join("\n", sorted);
        }

        private static string DeduplicateLines(string text)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (string line in SplitLines(text))
            {
                if (seen.Add(line))
                    result.Add(line);
            }
            return string.Join("\n", result);
        }

        private static string RemoveEmptyLines(string text)
        {
            IEnumerable<string> lines = SplitLines(text)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.TrimEnd());
            return string.Join("\n", lines);
        }

        private static string RegexExtract(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                throw new TextTransformException("A regular expression is required.");

            try
            {
                var regex = new Regex(pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(2));
                var matches = new List<string>();
                foreach (Match match in regex.Matches(text))
                {
                    matches.Add(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value);
                }
                return string.Join("\n", matches);
            }
            catch (ArgumentException ex)
            {
                throw new TextTransformException("Invalid regular expression: " + ex.Message);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new TextTransformException("The regular expression took too long.");
            }
        }
    }
}
