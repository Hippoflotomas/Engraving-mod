using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Engraving.Core.Language
{
    public enum TokenKind { Word, Number, String, Colon, Comma, End /* statement separator: newline ; . */ }

    public struct Token
    {
        public TokenKind Kind;
        /// <summary>Lower-cased for words; original text for strings.</summary>
        public string Text;
        public float Number;
        public int Line;

        public override string ToString() => Kind == TokenKind.End ? "<end>" : Text;
    }

    /// <summary>
    /// Splits a script into tokens. Statements end at a newline, ';' or a '.' that is not part of a number.
    /// '#' starts a comment to the end of the line. Strings use straight or curly double quotes.
    /// </summary>
    public static class Lexer
    {
        public static List<Token> Tokenize(string src, List<Diagnostic> diags)
        {
            var tokens = new List<Token>();
            int line = 1;
            int i = 0;
            void Emit(TokenKind k, string text, float num = 0f) =>
                tokens.Add(new Token { Kind = k, Text = text, Number = num, Line = line });

            while (i < src.Length)
            {
                char c = src[i];
                if (c == '\n') { Emit(TokenKind.End, "\n"); line++; i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
                if (c == ';' || c == '.') { Emit(TokenKind.End, c.ToString()); i++; continue; }
                if (c == ':') { Emit(TokenKind.Colon, ":"); i++; continue; }
                if (c == ',') { Emit(TokenKind.Comma, ","); i++; continue; }

                if (c == '"' || c == '“' || c == '”')
                {
                    int start = ++i;
                    while (i < src.Length && src[i] != '"' && src[i] != '”' && src[i] != '“' && src[i] != '\n') i++;
                    string text = src.Substring(start, i - start);
                    if (i < src.Length && src[i] != '\n') i++;
                    else diags.Add(new Diagnostic(Severity.Error, line, "text in quotes is missing its closing quote"));
                    Emit(TokenKind.String, text);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < src.Length && char.IsDigit(src[i])) i++;
                    if (i + 1 < src.Length && src[i] == '.' && char.IsDigit(src[i + 1]))
                    {
                        i++;
                        while (i < src.Length && char.IsDigit(src[i])) i++;
                    }
                    string text = src.Substring(start, i - start);
                    // "45°" or "45deg" is still just a number
                    if (i < src.Length && src[i] == '°') i++;
                    Emit(TokenKind.Number, text, float.Parse(text, CultureInfo.InvariantCulture));
                    continue;
                }

                if (IsWordChar(c))
                {
                    var sb = new StringBuilder();
                    while (i < src.Length && (IsWordChar(src[i]) || char.IsDigit(src[i]))) sb.Append(src[i++]);
                    Emit(TokenKind.Word, sb.ToString().ToLowerInvariant());
                    continue;
                }

                diags.Add(new Diagnostic(Severity.Warning, line, $"ignored unexpected character '{c}'"));
                i++;
            }
            Emit(TokenKind.End, "");
            return tokens;
        }

        static bool IsWordChar(char c) => char.IsLetter(c) || c == '-' || c == '_' || c == '\'';
    }
}
