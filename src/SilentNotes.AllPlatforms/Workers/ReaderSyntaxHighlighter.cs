// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SilentNotes.Workers
{
    /// <summary>
    /// Lightweight syntax highlighter for the Reader text editor. Produces HTML with token
    /// spans plus visible whitespace (space dots) and end-of-line markers.
    /// </summary>
    public static class ReaderSyntaxHighlighter
    {
        private static readonly Dictionary<string, string> ExtensionToLang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".java"] = "java",
            [".kt"] = "kotlin",
            [".kts"] = "kotlin",
            [".js"] = "javascript",
            [".jsx"] = "javascript",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".py"] = "python",
            [".rb"] = "ruby",
            [".go"] = "go",
            [".rs"] = "rust",
            [".php"] = "php",
            [".swift"] = "swift",
            [".c"] = "c",
            [".h"] = "c",
            [".cpp"] = "cpp",
            [".cc"] = "cpp",
            [".hpp"] = "cpp",
            [".css"] = "css",
            [".scss"] = "css",
            [".html"] = "html",
            [".htm"] = "html",
            [".xml"] = "xml",
            [".json"] = "json",
            [".yaml"] = "yaml",
            [".yml"] = "yaml",
            [".sql"] = "sql",
            [".sh"] = "shell",
            [".bash"] = "shell",
            [".ps1"] = "powershell",
            [".md"] = "markdown",
            [".markdown"] = "markdown",
            [".ini"] = "ini",
            [".toml"] = "ini",
            [".properties"] = "ini",
            [".gradle"] = "groovy",
            [".lua"] = "lua",
            [".r"] = "r",
            [".m"] = "objc",
            [".vb"] = "vb",
            [".dart"] = "dart",
        };

        private static readonly Dictionary<string, string[]> Keywords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = new[]
            {
                "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
                "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
                "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock",
                "long","namespace","new","null","object","operator","out","override","params","private","protected",
                "public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string",
                "struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
                "using","virtual","void","volatile","while","var","async","await","record","init","required","nint","nuint",
            },
            ["java"] = new[]
            {
                "abstract","assert","boolean","break","byte","case","catch","char","class","const","continue","default",
                "do","double","else","enum","extends","final","finally","float","for","goto","if","implements","import",
                "instanceof","int","interface","long","native","new","package","private","protected","public","return",
                "short","static","strictfp","super","switch","synchronized","this","throw","throws","transient","try",
                "void","volatile","while","true","false","null","var","record","sealed","permits","yield",
            },
            ["kotlin"] = new[]
            {
                "as","break","class","continue","do","else","false","for","fun","if","in","interface","is","null","object",
                "package","return","super","this","throw","true","try","typealias","typeof","val","var","when","while",
                "by","catch","constructor","delegate","dynamic","field","file","finally","get","import","init","param",
                "property","receiver","set","setparam","where","actual","abstract","annotation","companion","const",
                "crossinline","data","enum","expect","external","final","infix","inline","inner","internal","lateinit",
                "noinline","open","operator","out","override","private","protected","public","reified","sealed","suspend",
                "tailrec","vararg",
            },
            ["javascript"] = new[]
            {
                "break","case","catch","class","const","continue","debugger","default","delete","do","else","export",
                "extends","false","finally","for","function","if","import","in","instanceof","let","new","null","return",
                "super","switch","this","throw","true","try","typeof","var","void","while","with","yield","async","await",
                "of","static","enum","implements","interface","package","private","protected","public",
            },
            ["typescript"] = new[]
            {
                "break","case","catch","class","const","continue","debugger","default","delete","do","else","export",
                "extends","false","finally","for","function","if","import","in","instanceof","let","new","null","return",
                "super","switch","this","throw","true","try","typeof","var","void","while","with","yield","async","await",
                "of","static","enum","implements","interface","package","private","protected","public","type","namespace",
                "module","declare","abstract","readonly","keyof","infer","as","is","satisfies",
            },
            ["python"] = new[]
            {
                "False","None","True","and","as","assert","async","await","break","class","continue","def","del","elif",
                "else","except","finally","for","from","global","if","import","in","is","lambda","nonlocal","not","or",
                "pass","raise","return","try","while","with","yield",
            },
            ["go"] = new[]
            {
                "break","case","chan","const","continue","default","defer","else","fallthrough","for","func","go","goto",
                "if","import","interface","map","package","range","return","select","struct","switch","type","var",
            },
            ["rust"] = new[]
            {
                "as","async","await","break","const","continue","crate","dyn","else","enum","extern","false","fn","for",
                "if","impl","in","let","loop","match","mod","move","mut","pub","ref","return","self","Self","static",
                "struct","super","trait","true","type","unsafe","use","where","while",
            },
            ["sql"] = new[]
            {
                "select","from","where","and","or","not","insert","into","values","update","set","delete","create","table",
                "alter","drop","index","join","left","right","inner","outer","on","as","order","by","group","having",
                "limit","offset","union","all","distinct","null","true","false","case","when","then","else","end",
            },
            ["shell"] = new[]
            {
                "if","then","else","elif","fi","for","while","do","done","case","esac","in","function","select","until",
                "time","coproc","true","false",
            },
            ["php"] = new[]
            {
                "abstract","and","array","as","break","callable","case","catch","class","clone","const","continue","declare",
                "default","do","echo","else","elseif","empty","enddeclare","endfor","endforeach","endif","endswitch",
                "endwhile","eval","exit","extends","final","finally","fn","for","foreach","function","global","goto","if",
                "implements","include","include_once","instanceof","insteadof","interface","isset","list","match","namespace",
                "new","or","print","private","protected","public","readonly","require","require_once","return","static",
                "switch","throw","trait","try","unset","use","var","while","xor","yield","true","false","null",
            },
            ["ruby"] = new[]
            {
                "alias","and","begin","break","case","class","def","defined","do","else","elsif","end","ensure","false",
                "for","if","in","module","next","nil","not","or","redo","rescue","retry","return","self","super","then",
                "true","undef","unless","until","when","while","yield",
            },
            ["swift"] = new[]
            {
                "associatedtype","class","deinit","enum","extension","fileprivate","func","import","init","inout","internal",
                "let","open","operator","private","protocol","public","rethrows","static","struct","subscript","typealias",
                "var","break","case","continue","default","defer","do","else","fallthrough","for","guard","if","in","repeat",
                "return","switch","where","while","as","Any","catch","false","is","nil","super","self","Self","throw","throws",
                "true","try","async","await",
            },
            ["c"] = new[]
            {
                "auto","break","case","char","const","continue","default","do","double","else","enum","extern","float","for",
                "goto","if","inline","int","long","register","restrict","return","short","signed","sizeof","static","struct",
                "switch","typedef","union","unsigned","void","volatile","while","_Bool","_Complex","_Imaginary",
            },
            ["cpp"] = new[]
            {
                "alignas","alignof","and","and_eq","asm","auto","bitand","bitor","bool","break","case","catch","char",
                "char16_t","char32_t","class","compl","const","constexpr","const_cast","continue","decltype","default",
                "delete","do","double","dynamic_cast","else","enum","explicit","export","extern","false","float","for",
                "friend","goto","if","inline","int","long","mutable","namespace","new","noexcept","not","not_eq","nullptr",
                "operator","or","or_eq","private","protected","public","register","reinterpret_cast","return","short",
                "signed","sizeof","static","static_assert","static_cast","struct","switch","template","this","thread_local",
                "throw","true","try","typedef","typeid","typename","union","unsigned","using","virtual","void","volatile",
                "wchar_t","while","xor","xor_eq","override","final","constexpr","consteval","constinit","concept","requires",
                "co_await","co_return","co_yield",
            },
            ["powershell"] = new[]
            {
                "begin","break","catch","class","continue","data","define","do","dynamicparam","else","elseif","end",
                "exit","filter","finally","for","foreach","from","function","if","in","param","process","return","switch",
                "throw","trap","try","until","using","var","while","workflow","parallel","sequence","inlinescript",
            },
        };

        /// <summary>Resolves a language id from a file path or name.</summary>
        public static string DetectLanguage(string pathOrName)
        {
            string ext = Path.GetExtension(pathOrName ?? string.Empty);
            if (string.IsNullOrEmpty(ext))
                return null;
            return ExtensionToLang.TryGetValue(ext, out string lang) ? lang : null;
        }

        /// <summary>
        /// Builds highlighted HTML with space dots and EOL markers for the editor overlay.
        /// </summary>
        public static string ToHighlightedHtml(string text, string language)
        {
            text ??= string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder(text.Length * 2);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                AppendHighlightedLine(sb, line, language);
                sb.Append("<span class=\"rte-eol\">↵</span>");
                if (i < lines.Length - 1)
                    sb.Append('\n');
            }

            return sb.ToString();
        }

        private static void AppendHighlightedLine(StringBuilder sb, string line, string language)
        {
            if (string.IsNullOrEmpty(line))
                return;

            if (string.IsNullOrEmpty(language) || !Keywords.ContainsKey(language))
            {
                AppendWithWhitespace(sb, line);
                return;
            }

            if (language is "html" or "xml")
            {
                AppendMarkupLine(sb, line);
                return;
            }

            if (language == "json")
            {
                AppendJsonLine(sb, line);
                return;
            }

            if (language == "css")
            {
                AppendCssLine(sb, line);
                return;
            }

            if (language == "markdown")
            {
                AppendMarkdownLine(sb, line);
                return;
            }

            AppendCodeLine(sb, line, language);
        }

        private static void AppendCodeLine(StringBuilder sb, string line, string language)
        {
            var keywords = new HashSet<string>(Keywords[language], StringComparer.Ordinal);
            // Python / SQL / shell: case-sensitive keywords already listed with correct case.
            if (language is "sql" or "shell" or "powershell")
                keywords = new HashSet<string>(Keywords[language], StringComparer.OrdinalIgnoreCase);

            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];

                if (char.IsWhiteSpace(c))
                {
                    AppendWhitespaceChar(sb, c);
                    i++;
                    continue;
                }

                // Comments
                if (IsLineCommentStart(line, i, language))
                {
                    AppendSpan(sb, "rte-comment", line.Substring(i));
                    return;
                }

                if (c == '"' || c == '\'' || (c == '`' && language is "javascript" or "typescript" or "shell" or "kotlin"))
                {
                    int end = FindStringEnd(line, i, c);
                    AppendSpan(sb, "rte-string", line.Substring(i, end - i));
                    i = end;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_'))
                        i++;
                    AppendSpan(sb, "rte-number", line.Substring(start, i - start));
                    continue;
                }

                if (IsIdentStart(c))
                {
                    int start = i;
                    i++;
                    while (i < line.Length && IsIdentPart(line[i]))
                        i++;
                    string word = line.Substring(start, i - start);
                    if (keywords.Contains(word))
                        AppendSpan(sb, "rte-keyword", word);
                    else
                        AppendWithWhitespace(sb, word);
                    continue;
                }

                AppendEscaped(sb, c);
                i++;
            }
        }

        private static bool IsLineCommentStart(string line, int i, string language)
        {
            if (language == "python" || language == "ruby" || language == "shell" || language == "powershell" || language == "yaml" || language == "ini")
                return line[i] == '#';
            if (language == "sql")
                return i + 1 < line.Length && line[i] == '-' && line[i + 1] == '-';
            return i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/';
        }

        private static void AppendMarkupLine(StringBuilder sb, string line)
        {
            // Work on escaped text with simple tag coloring.
            string escaped = WebUtility.HtmlEncode(line);
            int last = 0;
            foreach (Match m in Regex.Matches(escaped, @"&lt;/?[\w:.-]+(?:\s[^&]*?)?/&gt;|&lt;/?[\w:.-]+(?:\s[^&]*?)?&gt;|&lt;!--.*?--&gt;"))
            {
                if (m.Index > last)
                    AppendEncodedWhitespace(sb, escaped.Substring(last, m.Index - last));
                string cls = m.Value.Contains("!--") ? "rte-comment" : "rte-tag";
                sb.Append("<span class=\"").Append(cls).Append("\">").Append(m.Value).Append("</span>");
                last = m.Index + m.Length;
            }
            if (last < escaped.Length)
                AppendEncodedWhitespace(sb, escaped.Substring(last));
        }

        private static void AppendJsonLine(StringBuilder sb, string line)
        {
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c))
                {
                    AppendWhitespaceChar(sb, c);
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    int end = FindStringEnd(line, i, '"');
                    string chunk = line.Substring(i, end - i);
                    bool isKey = end < line.Length && line.Substring(end).TrimStart().StartsWith(":", StringComparison.Ordinal);
                    AppendSpan(sb, isKey ? "rte-keyword" : "rte-string", chunk);
                    i = end;
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
                {
                    int start = i;
                    i++;
                    while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == 'e' || line[i] == 'E' || line[i] == '+' || line[i] == '-'))
                        i++;
                    AppendSpan(sb, "rte-number", line.Substring(start, i - start));
                    continue;
                }
                if (StartsWithWord(line, i, "true") || StartsWithWord(line, i, "false") || StartsWithWord(line, i, "null"))
                {
                    int len = line[i] == 't' ? 4 : line[i] == 'n' ? 4 : 5;
                    AppendSpan(sb, "rte-keyword", line.Substring(i, len));
                    i += len;
                    continue;
                }
                AppendEscaped(sb, c);
                i++;
            }
        }

        private static void AppendCssLine(StringBuilder sb, string line)
        {
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c))
                {
                    AppendWhitespaceChar(sb, c);
                    i++;
                    continue;
                }
                if (i + 1 < line.Length && c == '/' && line[i + 1] == '*')
                {
                    AppendSpan(sb, "rte-comment", line.Substring(i));
                    return;
                }
                if (c == '"' || c == '\'')
                {
                    int end = FindStringEnd(line, i, c);
                    AppendSpan(sb, "rte-string", line.Substring(i, end - i));
                    i = end;
                    continue;
                }
                if (c == '#' || c == '.' || IsIdentStart(c))
                {
                    int start = i;
                    i++;
                    while (i < line.Length && (IsIdentPart(line[i]) || line[i] == '-' || line[i] == '#' || line[i] == '.'))
                        i++;
                    AppendSpan(sb, "rte-keyword", line.Substring(start, i - start));
                    continue;
                }
                AppendEscaped(sb, c);
                i++;
            }
        }

        private static void AppendMarkdownLine(StringBuilder sb, string line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                AppendSpan(sb, "rte-keyword", line);
                return;
            }
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            {
                AppendSpan(sb, "rte-comment", line);
                return;
            }
            AppendWithWhitespace(sb, line);
        }

        private static bool StartsWithWord(string line, int i, string word)
        {
            if (i + word.Length > line.Length)
                return false;
            if (!string.Equals(line.Substring(i, word.Length), word, StringComparison.Ordinal))
                return false;
            int after = i + word.Length;
            return after >= line.Length || !IsIdentPart(line[after]);
        }

        private static int FindStringEnd(string line, int start, char quote)
        {
            int i = start + 1;
            while (i < line.Length)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    i += 2;
                    continue;
                }
                if (line[i] == quote)
                    return i + 1;
                i++;
            }
            return line.Length;
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

        private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        private static void AppendWithWhitespace(StringBuilder sb, string text)
        {
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) && c != '\n' && c != '\r')
                    AppendWhitespaceChar(sb, c);
                else
                    AppendEscaped(sb, c);
            }
        }

        private static void AppendEncodedWhitespace(StringBuilder sb, string encoded)
        {
            // encoded is already HTML-escaped; only spaces/tabs need decoration.
            foreach (char c in encoded)
            {
                if (c == ' ')
                    sb.Append("<span class=\"rte-space\"> </span>");
                else if (c == '\t')
                    sb.Append("<span class=\"rte-tab\">\t</span>");
                else
                    sb.Append(c);
            }
        }

        private static void AppendWhitespaceChar(StringBuilder sb, char c)
        {
            if (c == ' ')
                sb.Append("<span class=\"rte-space\"> </span>");
            else if (c == '\t')
                sb.Append("<span class=\"rte-tab\">\t</span>");
            else
                AppendEscaped(sb, c);
        }

        private static void AppendSpan(StringBuilder sb, string cssClass, string raw)
        {
            sb.Append("<span class=\"").Append(cssClass).Append("\">");
            AppendWithWhitespace(sb, raw);
            sb.Append("</span>");
        }

        private static void AppendEscaped(StringBuilder sb, char c)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
    }
}
