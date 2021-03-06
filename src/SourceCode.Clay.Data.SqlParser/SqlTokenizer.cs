#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceCode.Clay.Data.SqlParser
{
    public static class SqlTokenizer
    {
        /// <summary>
        /// Tokenizes the provided sql statement.
        /// </summary>
        /// <param name="sql">The tsql statement to tokenize.</param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        public static IReadOnlyCollection<SqlTokenInfo> Tokenize(string sql, bool skipSundry)
        {
            if (sql is null) throw new ArgumentNullException(nameof(sql));

            using (var reader = new SqlCharReader(sql))
            {
                IReadOnlyCollection<SqlTokenInfo> tokens = Tokenize(reader, skipSundry);
                return tokens;
            }
        }

        /// <summary>
        /// Tokenizes the provided sql statement.
        /// </summary>
        /// <param name="reader">A reader providing the tsql statement to tokenize.</param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        public static IReadOnlyCollection<SqlTokenInfo> Tokenize(TextReader reader, bool skipSundry)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            using (var cr = new SqlCharReader(reader))
            {
                IReadOnlyCollection<SqlTokenInfo> tokens = Tokenize(cr, skipSundry);
                return tokens;
            }
        }

        /// <summary>
        /// Encodes an identifier using tsql [square-delimited-name] convention.
        /// </summary>
        /// <param name="identifier">The identifier to encode.</param>
        public static string EncodeNameSquare(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;

            const string openSquare = "[";
            const string closeSquare = "]";
            const string closeSquareEscaped = closeSquare + closeSquare;

            // Need 2 delimiters. Assume 1 escape.
            var capacity = identifier.Length + 2 + 1;

            var sb = new StringBuilder(openSquare, capacity);
            {
                sb.Append(identifier);
                sb.Replace(closeSquare, closeSquareEscaped); // Escape embedded delimiters
            }
            sb.Append(closeSquare);

            var quoted = sb.ToString();
            return quoted;
        }

        /// <summary>
        /// Encodes an identifier using tsql "quoted-name" convention.
        /// </summary>
        /// <param name="identifier">The identifier to encode.</param>
        public static string EncodeNameQuotes(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return identifier;

            const string quote = "\"";
            const string quoteEscaped = quote + quote;

            // Need 2 delimiters. Assume 1 escape.
            var capacity = identifier.Length + 2 + 1;

            var sb = new StringBuilder("~", capacity); // Placeholder. See #1 below
            {
                sb.Append(identifier);
                sb.Replace(quote, quoteEscaped); // Escape embedded delimiters
            }
            sb[0] = quote[0]; // Replace placeholder. See #1 above
            sb.Append(quote);

            var quoted = sb.ToString();
            return quoted;
        }

        /// <summary>
        /// Encodes an identifier using tsql naming conventions.
        /// </summary>
        /// <param name="identifier">The identifier to encode.</param>
        /// <param name="useQuotes">If true, uses "quotes" else uses [square] delimiters.</param>
        public static string EncodeName(string identifier, bool useQuotes)
            => useQuotes ?
            EncodeNameQuotes(identifier) :
            EncodeNameSquare(identifier);

        /// <summary>
        /// Tokenizes the provided sql statement.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        private static IReadOnlyCollection<SqlTokenInfo> Tokenize(SqlCharReader reader, bool skipSundry)
        {
            Debug.Assert(!(reader is null));
            Span<char> peekBuffer = stackalloc char[2];

            var tokens = new List<SqlTokenInfo>();
            while (true)
            {
                reader.FillLength(peekBuffer, peekBuffer.Length, out var peekLength);
                if (peekLength <= 0) break;

                SqlTokenInfo token;
                SqlTokenKind kind = Peek(peekBuffer.Slice(0, peekLength));
                switch (kind)
                {
                    case SqlTokenKind.Whitespace:
                        token = ReadWhitespace(peekBuffer, peekLength, reader, skipSundry);
                        if (skipSundry) continue;
                        break;

                    case SqlTokenKind.LineComment:
                        token = ReadLineComment(reader, skipSundry);
                        if (skipSundry) continue;
                        break;

                    case SqlTokenKind.BlockComment:
                        token = ReadBlockComment(reader, skipSundry);
                        if (skipSundry) continue;
                        break;

                    case SqlTokenKind.Literal:
                        token = ReadLiteral(peekBuffer, peekLength, reader);
                        break;

                    case SqlTokenKind.Symbol:
                        token = ReadSymbol(peekBuffer, peekLength, reader);
                        break;

                    case SqlTokenKind.SquareString:
                        token = ReadSquareString(peekBuffer, peekLength, reader);
                        break;

                    case SqlTokenKind.QuotedString:
                        token = ReadQuotedString(peekBuffer, peekLength, reader);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown {nameof(SqlTokenKind)}: {kind}");
                }

                tokens.Add(token);
            }

            return tokens;
        }

        private static SqlTokenKind Peek(ReadOnlySpan<char> peekBuffer)
        {
            Debug.Assert(peekBuffer.Length >= 1);

            var canPeekDouble = peekBuffer.Length >= 2;

            switch (peekBuffer[0])
            {
                // Line Comment
                case '-':
                    return canPeekDouble && peekBuffer[1] == '-' ? SqlTokenKind.LineComment : SqlTokenKind.Symbol;

                // Block Comment
                case '/':
                    return canPeekDouble && peekBuffer[1] == '*' ? SqlTokenKind.BlockComment : SqlTokenKind.Symbol;

                // Quoted String
                case 'N':
                    return canPeekDouble && peekBuffer[1] == '\'' ? SqlTokenKind.QuotedString : SqlTokenKind.Literal;

                // Quoted String
                case '"':
                case '\'':
                    return SqlTokenKind.QuotedString;

                // Square String
                case '[':
                    return SqlTokenKind.SquareString;

                // Math
                case '+':
                case '*':
                case '%':

                // Logical
                case '&':
                case '|':
                case '^':
                case '~':

                // Comparison
                case '>':
                case '<':
                case '=':

                // Symbol
                case ',':
                case '.':
                case ';':
                case '(':
                case ')':
                    return SqlTokenKind.Symbol;
            }

            if (canPeekDouble)
            {
                var s2 = new string(peekBuffer.Slice(0, 2));
                switch (s2)
                {
                    // Math
                    case "+=":
                    case "-=":
                    case "*=":
                    case "/=":
                    case "%=":

                    // Logical
                    case "&=":
                    case "|=":
                    case "^=":

                    // Comparison
                    case ">=":
                    case "<=":
                    case "<>":
                    case "!<":
                    case "!=":
                    case "!>":

                    // Symbol
                    case "::":
                        return SqlTokenKind.Symbol;
                }
            }

            return char.IsWhiteSpace(peekBuffer[0]) ? SqlTokenKind.Whitespace : SqlTokenKind.Literal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Contains(ReadOnlySpan<char> buffer, char c0)
            => buffer.Length >= 1
            && buffer[0] == c0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Contains(ReadOnlySpan<char> buffer, char c0, char c1)
            => buffer.Length >= 2
            && buffer[0] == c0
            && buffer[1] == c1;

        /// <summary>
        ///
        /// </summary>
        /// <param name="peekBuffer"></param>
        /// <param name="peekLength"></param>
        /// <param name="reader"></param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        private static SqlTokenInfo ReadWhitespace(ReadOnlySpan<char> peekBuffer, int peekLength, SqlCharReader reader, bool skipSundry)
        {
            Debug.Assert(peekBuffer.Length >= 1);
            Debug.Assert(peekLength >= 1);
            Debug.Assert(!(reader is null));

            // Fast path for single whitespace
            if (peekLength >= 2
                && !char.IsWhiteSpace(peekBuffer[1]))
            {
                // Undo any extraneous data
                reader.Undo(peekBuffer.Slice(1, peekLength - 1));

                var t0 = new SqlTokenInfo(SqlTokenKind.Whitespace, peekBuffer[0]);
                return t0;
            }

            // Perf: Assume whitespace is N chars
            const int averageLen = 10;
            var cap = peekLength + averageLen;
            Span<char> buffer = stackalloc char[cap];
            StringBuilder sb = skipSundry ? null : new StringBuilder(cap);

            // We already know the incoming data is "<whitespace>", so keep it
            if (!skipSundry)
                sb.Append(peekBuffer.Slice(0, peekLength));

            while (true)
            {
                reader.FillRemaining(buffer, out var count);
                if (count <= 0) break;

                // Try find any non-whitespace in current batch
                var idx = -1;
                for (var i = 0; i < count; i++)
                {
                    if (!char.IsWhiteSpace(buffer[i]))
                    {
                        idx = i;
                        break;
                    }
                }

                // If we found any non-whitespace
                if (idx >= 0)
                {
                    // Undo any extraneous data and exit the loop
                    if (!skipSundry)
                        sb.Append(buffer.Slice(0, idx));

                    reader.Undo(buffer.Slice(idx, count - idx));
                    break;
                }

                // Else keep the whitespace and check next batch
                if (!skipSundry)
                    sb.Append(buffer.Slice(0, count));
            }

            var token = new SqlTokenInfo(SqlTokenKind.Whitespace, sb);
            return token;
        }

        private static SqlTokenInfo ReadSquareString(ReadOnlySpan<char> peekBuffer, int peekLength, SqlCharReader reader)
        {
            Debug.Assert(peekBuffer.Length >= 1);
            Debug.Assert(peekBuffer[0] == '[');
            Debug.Assert(peekLength >= 1);
            Debug.Assert(!(reader is null));

            // Sql Identifiers are max 128 chars
            var cap = peekLength + 128;
            Span<char> buffer = stackalloc char[cap];
            var sb = new StringBuilder(cap);

            // We already know the incoming data is [, so keep it
            sb.Append(peekBuffer[0]);

            // Undo any extraneous data
            if (peekLength > 1)
            {
                reader.Undo(peekBuffer.Slice(1, peekLength - 1));
            }

            while (true)
            {
                reader.FillLength(buffer, 4, out var count);
                if (count <= 0) break;

                var eof = count <= 3;
                var cnt = eof ? count : count - 3;

                // Try find a delimiter
                var idx = -1;
                for (var i = 0; i < cnt; i++)
                {
                    if (!Contains(buffer.Slice(i, count - i), ']'))
                        continue;

                    idx = i;
                    break;
                }

                // If we found a delimiter
                if (idx >= 0)
                {
                    // If it is an escaped delimiter
                    // https://docs.microsoft.com/en-us/sql/t-sql/functions/quotename-transact-sql
                    if (idx >= 1 && Contains(buffer.Slice(idx, count - idx), ']', ']'))
                    {
                        // Keep going
                        sb.Append(buffer.Slice(0, idx + 2));
                        reader.Undo(buffer.Slice(idx + 2, count - idx - 2));

                        continue;
                    }

                    // Else undo any extraneous data and exit the loop
                    sb.Append(buffer.Slice(0, idx + 1));
                    reader.Undo(buffer.Slice(idx + 1, count - idx - 1));

                    break;
                }

                sb.Append(buffer.Slice(0, cnt));

                // Exit if no more data
                if (eof) break;

                // Ensure we can peek again
                reader.Undo(buffer.Slice(cnt, 3));
            }

            var token = new SqlTokenInfo(SqlTokenKind.SquareString, sb);
            return token;
        }

        private static SqlTokenInfo ReadQuotedString(ReadOnlySpan<char> peekBuffer, int peekLength, SqlCharReader reader)
        {
            Debug.Assert(peekBuffer.Length >= 1);
            Debug.Assert(peekLength >= 1);
            Debug.Assert(!(reader is null));

            // Perf: Assume string is N chars
            const int averageLen = 256;
            var cap = peekLength + averageLen;
            var sb = new StringBuilder(cap);

            // We already know the incoming data is "<quote>", so keep it
            var len = peekBuffer[0] == '\'' || peekBuffer[0] == '"' ? 1 : 2;
            sb.Append(peekBuffer.Slice(0, len));

            // Undo any extraneous data
            if (peekLength > len)
            {
                reader.Undo(peekBuffer.Slice(len, peekLength - len));
            }

            // Read string value
            switch (peekBuffer[0])
            {
                case '"': // "abc"
                    {
                        SqlTokenInfo token = ReadQuotedString(sb, '"', reader);
                        return token;
                    }

                case '\'': // 'abc'
                    {
                        SqlTokenInfo token = ReadQuotedString(sb, '\'', reader);
                        return token;
                    }

                case 'N': // N'abc'
                    if (peekLength >= 2 && peekBuffer[1] == '\'')
                    {
                        SqlTokenInfo token = ReadQuotedString(sb, '\'', reader);
                        return token;
                    }
                    break;
            }

            throw new ArgumentException("Invalid quoted string: " + new string(peekBuffer.Slice(0, peekLength)));
        }

        private static SqlTokenInfo ReadQuotedString(StringBuilder sb, char delimiter, SqlCharReader reader)
        {
            Debug.Assert(!(sb is null));
            Debug.Assert(!(reader is null));

            Span<char> buffer = stackalloc char[sb.Capacity];

            while (true)
            {
                reader.FillLength(buffer, 4, out var count);
                if (count <= 0) break;

                var eof = count <= 3;
                var cnt = eof ? count : count - 3;

                // Try find a delimiter
                var idx = -1;
                for (var i = 0; i < cnt; i++)
                {
                    if (!Contains(buffer.Slice(i, count - i), delimiter))
                        continue;

                    idx = i;
                    break;
                }

                // If we found a delimiter
                if (idx >= 0)
                {
                    // If it is an escaped delimiter
                    if (idx >= 1 && Contains(buffer.Slice(idx - 1, count - idx + 1), delimiter, delimiter))
                    {
                        // Keep going
                        sb.Append(buffer.Slice(0, idx + 2));
                        reader.Undo(buffer.Slice(idx + 2, count - idx - 2));

                        continue;
                    }

                    // Else undo any extraneous data and exit the loop
                    sb.Append(buffer.Slice(0, idx + 1));
                    reader.Undo(buffer.Slice(idx + 1, count - idx - 1));

                    break;
                }

                sb.Append(buffer.Slice(0, cnt));

                // Exit if no more data
                if (eof) break;

                // Ensure we can peek again
                reader.Undo(buffer.Slice(cnt, 3));
            }

            var token = new SqlTokenInfo(SqlTokenKind.QuotedString, sb);
            return token;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="peekBuffer"></param>
        /// <param name="reader"></param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        private static SqlTokenInfo ReadBlockComment(SqlCharReader reader, bool skipSundry)
        {
            Debug.Assert(!(reader is null));

            // Perf: Assume comment is N chars
            const int bufferLen = 50;
            Span<char> buffer = stackalloc char[bufferLen];
            StringBuilder sb = skipSundry ? null : new StringBuilder(2 + bufferLen);

            // We already know the peeked token is /*, so keep it
            if (!skipSundry)
                sb.Append("/*");

            while (true)
            {
                reader.FillLength(buffer, bufferLen, out var count);
                if (count <= 0) break;

                // Try find a delimiter
                var idx = -1;
                for (var i = 0; i < count; i++)
                {
                    if (!Contains(buffer.Slice(i, count - i), '*', '/'))
                        continue;

                    idx = i;
                    break;
                }

                // If we found a delimiter
                if (idx >= 0)
                {
                    // Undo any extraneous data and exit the loop
                    if (!skipSundry)
                        sb.Append(buffer.Slice(0, idx + 2));

                    reader.Undo(buffer.Slice(idx + 2, count - idx - 2));
                    break;
                }

                if (!skipSundry)
                    sb.Append(buffer.Slice(0, count));

                // Exit if eof
                if (count < bufferLen) break;
            }

            var token = new SqlTokenInfo(SqlTokenKind.BlockComment, sb);
            return token;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="peekBuffer"></param>
        /// <param name="reader"></param>
        /// <param name="skipSundry">Do not emit sundry tokens (such as comments and whitespace) in the output.</param>
        private static SqlTokenInfo ReadLineComment(SqlCharReader reader, bool skipSundry)
        {
            Debug.Assert(!(reader is null));

            // Perf: Assume comment is N chars
            const int bufferLen = 30;
            Span<char> buffer = stackalloc char[bufferLen];
            StringBuilder sb = skipSundry ? null : new StringBuilder(2 + bufferLen);

            // We already know the peeked token is --, so keep it
            if (!skipSundry)
                sb.Append("--");

            while (true)
            {
                reader.FillLength(buffer, bufferLen, out var count);
                if (count <= 0) break;

                // Try find a delimiter
                var idx = -1;
                for (var i = 0; i < count; i++)
                {
                    // Final character in both Windows (\r\n) and Linux (\n) line-endings is \n.
                    if (!Contains(buffer.Slice(i, count - i), '\n'))
                        continue;

                    idx = i;
                    break;
                }

                // If we found a delimiter
                if (idx >= 0)
                {
                    // Undo any extraneous data and exit the loop
                    if (!skipSundry)
                        sb.Append(buffer.Slice(0, idx + 1));

                    reader.Undo(buffer.Slice(idx + 1, count - idx - 1));
                    break;
                }

                if (!skipSundry)
                    sb.Append(buffer.Slice(0, count));

                // Exit if eof
                if (count < bufferLen) break;
            }

            var token = new SqlTokenInfo(SqlTokenKind.LineComment, sb);
            return token;
        }

        private static SqlTokenInfo ReadSymbol(ReadOnlySpan<char> peekBuffer, int peekLength, SqlCharReader reader)
        {
            Debug.Assert(peekBuffer.Length >= 1);
            Debug.Assert(peekLength >= 1);
            Debug.Assert(!(reader is null));

            if (peekLength >= 2)
            {
                // First check if it's a double-symbol
                var peek = new string(peekBuffer.Slice(0, 2));
                switch (peek)
                {
                    // Math
                    case "+=":
                    case "-=":
                    case "*=":
                    case "/=":
                    case "%=":

                    // Logical
                    case "&=":
                    case "|=":
                    case "^=":

                    // Comparison
                    case ">=":
                    case "<=":
                    case "<>":
                    case "!<":
                    case "!=":
                    case "!>":

                    // Programmatic
                    case "::":

                        var doubleToken = new SqlTokenInfo(SqlTokenKind.Symbol, peekBuffer.Slice(0, peekLength));
                        return doubleToken;
                }
            }

            // It must have been a single symbol
            reader.Undo(peekBuffer.Slice(1, peekLength - 1));

            var token = new SqlTokenInfo(SqlTokenKind.Symbol, peekBuffer[0]);
            return token;
        }

        private static SqlTokenInfo ReadLiteral(ReadOnlySpan<char> peekBuffer, int peekLength, SqlCharReader reader)
        {
            Debug.Assert(peekBuffer.Length >= 1);
            Debug.Assert(peekLength >= 1);
            Debug.Assert(!(reader is null));

            // Sql literals are generally short
            var cap = peekLength + 32;
            Span<char> buffer = stackalloc char[cap];
            var sb = new StringBuilder(cap);

            // We already know the incoming data is "<literal>", so keep it
            sb.Append(peekBuffer[0]);

            // Undo any extraneous data
            if (peekLength >= 2)
            {
                reader.Undo(peekBuffer.Slice(1, peekLength - 1));
            }

            while (true)
            {
                reader.FillRemaining(buffer, out var count);
                if (count <= 0) break;

                for (var i = 0; i < count; i++)
                {
                    switch (buffer[i])
                    {
                        // Math
                        case '+':
                        case '-':
                        case '*':
                        case '/':
                        case '%':

                        // Logical
                        case '&':
                        case '|':
                        case '^':
                        case '~':

                        // Comparison
                        case '>':
                        case '<':
                        case '!':
                        case '=':

                        // Quote
                        case '\'':
                        case '"':

                        // Symbol
                        case ',':
                        case '.':
                        case ';':
                        case ':':
                        case '(':
                        case ')':
                            // Exit if delimiter
                            break;

                        default:
                            // Exit if whitespace
                            if (char.IsWhiteSpace(buffer[i])) break;

                            // Loop if not whitespace
                            continue;
                    }

                    if (i >= 1)
                        sb.Append(buffer.Slice(0, i));

                    reader.Undo(buffer.Slice(i, count - i));

                    var t1 = new SqlTokenInfo(SqlTokenKind.Literal, sb);
                    return t1;
                }

                sb.Append(buffer.Slice(0, count));
            }

            var token = new SqlTokenInfo(SqlTokenKind.Literal, sb);
            return token;
        }
    }
}
