namespace JsonToQueryable
{
    using Exceptions;
    using System;

    public class LexerContext : IDisposable
    {
        private int _currentIndex;
        private readonly ISource _source;

        public LexerContext(ISource source, int index)
        {
            this._currentIndex = index;
            this._source = source;
        }

        public void Dispose()
        {
        }

        public Token GetToken()
        {
            if (this._source.Body == null)
                return this.CreateEOFToken();

            this._currentIndex = this.GetPositionAfterWhitespace(this._source.Body, this._currentIndex);

            if (this._currentIndex >= this._source.Body.Length)
                return this.CreateEOFToken();

            var unicode = this.IfUnicodeGetString();

            var code = this._source.Body[this._currentIndex];

            this.ValidateCharacterCode(code);

            var token = this.CheckForPunctuationTokens(code);
            if (token != null)
                return token;

            if (char.IsLetter(code) || code == '_')
                return this.ReadNameOrBoolean();

            if (char.IsNumber(code) || code == '-')
                return this.ReadNumber();

            if (code == '"' || code == '\'')
                return this.ReadString();

            throw new QuerySyntaxErrorException(
                $"Unexpected character {this.ResolveCharName(code, unicode)}", this._source, this._currentIndex);
        }

        public bool OnlyHexInString(string test)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(test, @"\A\b[0-9a-fA-F]+\b\Z");
        }

        public Token ReadNumber()
        {
            var isFloat = false;
            var start = this._currentIndex;
            var code = this._source.Body[start];

            if (code == '-')
                code = this.NextCode();

            var nextCode = code == '0'
                ? this.NextCode()
                : this.ReadDigitsFromOwnSource(code);

            if (nextCode >= 48 && nextCode <= 57)
            {
                throw new QuerySyntaxErrorException(
                    $"Invalid number, unexpected digit after {code}: \"{nextCode}\"", this._source, this._currentIndex);
            }

            code = nextCode;
            if (code == '.')
            {
                isFloat = true;
                code = this.ReadDigitsFromOwnSource(this.NextCode());
            }

            if (code == 'E' || code == 'e')
            {
                isFloat = true;
                code = this.NextCode();
                if (code == '+' || code == '-')
                {
                    code = this.NextCode();
                }

                code = this.ReadDigitsFromOwnSource(code);
            }

            return isFloat ? this.CreateFloatToken(start) : this.CreateIntToken(start);
        }

        public Token ReadString()
        {
            var start = this._currentIndex;
            var value = this.ProcessStringChunks();

            return new Token()
            {
                Kind = TokenKind.STRING,
                Value = value,
                Start = start,
                End = this._currentIndex + 1
            };
        }

        private static bool IsValidNameCharacter(char code)
        {
            return code == '_' || char.IsLetterOrDigit(code);
        }

        private string AppendCharactersFromLastChunk(string value, int chunkStart)
        {
            return value + this._source.Body.Substring(chunkStart, this._currentIndex - chunkStart - 1);
        }

        private string AppendToValueByCode(string value, char code)
        {
            switch (code)
            {
                case '"': value += '"'; break;
                case '/': value += '/'; break;
                case '\\': value += '\\'; break;
                case 'b': value += '\b'; break;
                case 'f': value += '\f'; break;
                case 'n': value += '\n'; break;
                case 'r': value += '\r'; break;
                case 't': value += '\t'; break;
                case 'u': value += this.GetUnicodeChar(); break;
                default:
                    throw new QuerySyntaxErrorException($"Invalid character escape sequence: \\{code}.", this._source, this._currentIndex);
            }

            return value;
        }

        private int CharToHex(char code)
        {
            return Convert.ToByte(code.ToString(), 16);
        }

        private void CheckForInvalidCharacters(char code)
        {
            if (code < 0x0020 && code != 0x0009)
            {
                throw new QuerySyntaxErrorException(
                    $"Invalid character within String: \\u{((int)code).ToString("D4")}.", this._source, this._currentIndex);
            }
        }

        private Token CheckForPunctuationTokens(char code)
        {
            switch (code)
            {
                case '$': return CreatePunctuationToken(TokenKind.DOLLAR, 1);
                case '(': return CreatePunctuationToken(TokenKind.PAREN_L, 1);
                case ')': return CreatePunctuationToken(TokenKind.PAREN_R, 1);
                case ':': return CreatePunctuationToken(TokenKind.COLON, 1);
                case '@': return CreatePunctuationToken(TokenKind.AT, 1);
                case '[': return CreatePunctuationToken(TokenKind.BRACKET_L, 1);
                case ']': return CreatePunctuationToken(TokenKind.BRACKET_R, 1);
                case '{': return CreatePunctuationToken(TokenKind.BRACE_L, 1);
                case '}': return CreatePunctuationToken(TokenKind.BRACE_R, 1);
                case ',': return CreatePunctuationToken(TokenKind.COMMA, 1);
                case '<': return CreatePunctuationToken(TokenKind.LESSTHAN, 1);
                case '>': return CreatePunctuationToken(TokenKind.GREATERTHAN, 1);
                case '.': return CheckForSpreadOperator();
                case '&': return CheckForAnd();
                case '|': return CheckForOr();
                case '=': return CheckForEquals();
                case '!': return CheckForNot();
                default: return null;
            }
        }

        private Token CheckForAnd()
        {
            return CheckFor('&', 1, TokenKind.AND) ?? this.CreatePunctuationToken(TokenKind.AND, 1);
        }

        private Token CheckForOr()
        {
            return CheckFor('|', 1, TokenKind.OR) ?? this.CreatePunctuationToken(TokenKind.PIPE, 1);
        }

        private Token CheckForNot()
        {
            return CheckFor('=', 1, TokenKind.NOT) ?? this.CreatePunctuationToken(TokenKind.BANG, 1);
        }

        private Token CheckForEquals()
        {
            return CheckFor('=', 1, TokenKind.EQUALS) ?? this.CreatePunctuationToken(TokenKind.EQUALS, 1);
        }

        private Token CheckFor(char c, int length, TokenKind kind)
        {
            for (int i = 1; i <= length; i++)
            {
                var cc = this._source.Body.Length > this._currentIndex + i ? this._source.Body[this._currentIndex + i] : 0;

                if (cc != c)
                {
                    return null;
                }
            }

            return this.CreatePunctuationToken(kind, length + 1);
        }

        private Token CheckForSpreadOperator()
        {
            return CheckFor('.', 2, TokenKind.SPREAD);
        }

        private void CheckStringTermination(char code)
        {
            if (code != '"' && code != '\'')
            {
                throw new QuerySyntaxErrorException("Unterminated string.", this._source, this._currentIndex);
            }
        }

        private Token CreateEOFToken()
        {
            return new Token()
            {
                Start = this._currentIndex,
                End = this._currentIndex,
                Kind = TokenKind.EOF
            };
        }

        private Token CreateFloatToken(int start)
        {
            return new Token()
            {
                Kind = TokenKind.FLOAT,
                Start = start,
                End = this._currentIndex,
                Value = this._source.Body.Substring(start, this._currentIndex - start)
            };
        }

        private Token CreateIntToken(int start)
        {
            return new Token()
            {
                Kind = TokenKind.INT,
                Start = start,
                End = this._currentIndex,
                Value = this._source.Body.Substring(start, this._currentIndex - start)
            };
        }

        private Token CreateNameOrBooleanToken(int start)
        {
            var value = this._source.Body.Substring(start, this._currentIndex - start);
            var isBoolean = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase);

            return new Token()
            {
                Start = start,
                End = this._currentIndex,
                Kind = isBoolean ? TokenKind.BOOLEAN : TokenKind.NAME,
                Value = value
            };
        }

        private Token CreatePunctuationToken(TokenKind kind, int offset)
        {
            return new Token()
            {
                Start = this._currentIndex,
                End = this._currentIndex + offset,
                Kind = kind,
                Value = null
            };
        }

        private char GetCode()
        {
            return this.IsNotAtTheEndOfQuery()
                ? this._source.Body[this._currentIndex]
                : (char)0;
        }

        private int GetPositionAfterWhitespace(string body, int start)
        {
            var position = start;

            while (position < body.Length)
            {
                var code = body[position];
                switch (code)
                {
                    case '\xFEFF': // BOM
                    case '\t': // tab
                    case ' ': // space
                    case '\n': // new line
                    case '\r': // carriage return
                    //case ',': // Comma
                        ++position;
                        break;

                    case '#':
                        position = this.WaitForEndOfComment(body, position, code);
                        break;

                    default:
                        return position;
                }
            }

            return position;
        }

        private char GetUnicodeChar()
        {
            var expression = this._source.Body.Substring(this._currentIndex, 5);

            if (!this.OnlyHexInString(expression.Substring(1)))
            {
                throw new QuerySyntaxErrorException($"Invalid character escape sequence: \\{expression}.", this._source, this._currentIndex);
            }

            var character = (char)(
                this.CharToHex(this.NextCode()) << 12 |
                this.CharToHex(this.NextCode()) << 8 |
                this.CharToHex(this.NextCode()) << 4 |
                this.CharToHex(this.NextCode()));

            return character;
        }

        private string IfUnicodeGetString()
        {
            return this._source.Body.Length > this._currentIndex + 5 &&
                this.OnlyHexInString(this._source.Body.Substring(this._currentIndex + 2, 4))
                ? this._source.Body.Substring(this._currentIndex, 6)
                : null;
        }

        private bool IsNotAtTheEndOfQuery()
        {
            return this._currentIndex < this._source.Body.Length;
        }

        private char NextCode()
        {
            this._currentIndex++;
            return this.IsNotAtTheEndOfQuery()
                ? this._source.Body[this._currentIndex]
                : (char)0;
        }

        private char ProcessCharacter(ref string value, ref int chunkStart)
        {
            var code = this.GetCode();
            ++this._currentIndex;

            if (code == '\\')
            {
                value = this.AppendToValueByCode(this.AppendCharactersFromLastChunk(value, chunkStart), this.GetCode());

                ++this._currentIndex;
                chunkStart = this._currentIndex;
            }

            return this.GetCode();
        }

        private string ProcessStringChunks()
        {
            var chunkStart = ++this._currentIndex;
            var code = this.GetCode();
            var value = string.Empty;

            while (this.IsNotAtTheEndOfQuery() && code != 0x000A && code != 0x000D && code != '"' && code != '\'')
            {
                this.CheckForInvalidCharacters(code);
                code = this.ProcessCharacter(ref value, ref chunkStart);
            }

            this.CheckStringTermination(code);
            value += this._source.Body.Substring(chunkStart, this._currentIndex - chunkStart);
            return value;
        }

        private int ReadDigits(ISource source, int start, char firstCode)
        {
            var body = source.Body;
            var position = start;
            var code = firstCode;

            if (!char.IsNumber(code))
            {
                throw new QuerySyntaxErrorException(
                    $"Invalid number, expected digit but got: {this.ResolveCharName(code)}", this._source, this._currentIndex);
            }

            do
            {
                code = ++position < body.Length
                    ? body[position]
                    : (char)0;
            }
            while (char.IsNumber(code));

            return position;
        }

        private char ReadDigitsFromOwnSource(char code)
        {
            this._currentIndex = this.ReadDigits(this._source, this._currentIndex, code);
            code = this.GetCode();
            return code;
        }

        private Token ReadNameOrBoolean()
        {
            var start = this._currentIndex;
            var code = (char)0;

            do
            {
                this._currentIndex++;
                code = this.GetCode();
            }
            while (this.IsNotAtTheEndOfQuery() && IsValidNameCharacter(code));

            return this.CreateNameOrBooleanToken(start);
        }

        private string ResolveCharName(char code, string unicodeString = null)
        {
            if (code == '\0')
                return "<EOF>";

            if (!string.IsNullOrWhiteSpace(unicodeString))
                return $"\"{unicodeString}\"";

            return $"\"{code}\"";
        }

        private void ValidateCharacterCode(int code)
        {
            if (code < 0x0020 && code != 0x0009 && code != 0x000A && code != 0x000D)
            {
                throw new QuerySyntaxErrorException(
                    $"Invalid character \"\\u{code.ToString("D4")}\".", this._source, this._currentIndex);
            }
        }

        private int WaitForEndOfComment(string body, int position, char code)
        {
            while (++position < body.Length && (code = body[position]) != 0 && (code > 0x001F || code == 0x0009) && code != 0x000A && code != 0x000D)
            {
            }

            return position;
        }
    }
}