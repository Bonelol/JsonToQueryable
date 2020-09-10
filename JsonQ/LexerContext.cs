namespace JsonQ
{
    using Exceptions;
    using System;

    public class LexerContext : IDisposable
    {
        private int _currentIndex;
        private readonly ISource _source;

        public LexerContext(ISource source, int index)
        {
            _currentIndex = index;
            _source = source;
        }

        public void Dispose()
        {
        }

        public Token GetToken()
        {
            if (_source.Body == null)
                return CreateEOFToken();

            _currentIndex = GetPositionAfterWhitespace(_source.Body, _currentIndex);

            if (_currentIndex >= _source.Body.Length)
                return CreateEOFToken();

            var unicode = IfUnicodeGetString();

            var code = _source.Body[_currentIndex];

            ValidateCharacterCode(code);

            var token = CheckForPunctuationTokens(code);
            if (token != null)
                return token;

            if (char.IsLetter(code) || code == '_')
                return ReadNameOrBoolean();

            if (char.IsNumber(code) || code == '-')
                return ReadNumber();

            if (code == '"' || code == '\'')
                return ReadString();

            throw new QuerySyntaxErrorException(
                $"Unexpected character {ResolveCharName(code, unicode)}", _source, _currentIndex);
        }

        public bool OnlyHexInString(string test)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(test, @"\A\b[0-9a-fA-F]+\b\Z");
        }

        public Token ReadNumber()
        {
            var isFloat = false;
            var start = _currentIndex;
            var code = _source.Body[start];

            if (code == '-')
                code = NextCode();

            var nextCode = code == '0'
                ? NextCode()
                : ReadDigitsFromOwnSource(code);

            if (nextCode >= 48 && nextCode <= 57)
            {
                throw new QuerySyntaxErrorException(
                    $"Invalid number, unexpected digit after {code}: \"{nextCode}\"", _source, _currentIndex);
            }

            code = nextCode;
            if (code == '.')
            {
                isFloat = true;
                code = ReadDigitsFromOwnSource(NextCode());
            }

            if (code == 'E' || code == 'e')
            {
                isFloat = true;
                code = NextCode();
                if (code == '+' || code == '-')
                {
                    NextCode();
                }
            }

            return isFloat ? CreateFloatToken(start) : CreateIntToken(start);
        }

        public Token ReadString()
        {
            var start = _currentIndex;
            var value = ProcessStringChunks();

            return new Token()
            {
                Kind = TokenKind.STRING,
                Value = value,
                Start = start,
                End = _currentIndex + 1
            };
        }

        private static bool IsValidNameCharacter(char code)
        {
            return code == '_' || char.IsLetterOrDigit(code);
        }

        private string AppendCharactersFromLastChunk(string value, int chunkStart)
        {
            return value + _source.Body.Substring(chunkStart, _currentIndex - chunkStart - 1);
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
                case 'u': value += GetUnicodeChar(); break;
                default:
                    throw new QuerySyntaxErrorException($"Invalid character escape sequence: \\{code}.", _source, _currentIndex);
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
                    $"Invalid character within String: \\u{((int)code):D4}.", _source, _currentIndex);
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
            return CheckFor('&', 1, TokenKind.AND) ?? CreatePunctuationToken(TokenKind.AND, 1);
        }

        private Token CheckForOr()
        {
            return CheckFor('|', 1, TokenKind.OR) ?? CreatePunctuationToken(TokenKind.PIPE, 1);
        }

        private Token CheckForNot()
        {
            return CheckFor('=', 1, TokenKind.NOT) ?? CreatePunctuationToken(TokenKind.BANG, 1);
        }

        private Token CheckForEquals()
        {
            return CheckFor('=', 1, TokenKind.EQUALS) ?? CreatePunctuationToken(TokenKind.EQUALS, 1);
        }

        private Token CheckFor(char c, int length, TokenKind kind)
        {
            for (var i = 1; i <= length; i++)
            {
                var cc = _source.Body.Length > _currentIndex + i ? _source.Body[_currentIndex + i] : 0;

                if (cc != c)
                {
                    return null;
                }
            }

            return CreatePunctuationToken(kind, length + 1);
        }

        private Token CheckForSpreadOperator()
        {
            return CheckFor('.', 2, TokenKind.SPREAD);
        }

        private void CheckStringTermination(char code)
        {
            if (code != '"' && code != '\'')
            {
                throw new QuerySyntaxErrorException("Unterminated string.", _source, _currentIndex);
            }
        }

        private Token CreateEOFToken()
        {
            return new Token()
            {
                Start = _currentIndex,
                End = _currentIndex,
                Kind = TokenKind.EOF
            };
        }

        private Token CreateFloatToken(int start)
        {
            return new Token()
            {
                Kind = TokenKind.FLOAT,
                Start = start,
                End = _currentIndex,
                Value = _source.Body.Substring(start, _currentIndex - start)
            };
        }

        private Token CreateIntToken(int start)
        {
            return new Token()
            {
                Kind = TokenKind.INT,
                Start = start,
                End = _currentIndex,
                Value = _source.Body.Substring(start, _currentIndex - start)
            };
        }

        private Token CreateNameOrBooleanToken(int start)
        {
            var value = _source.Body.Substring(start, _currentIndex - start);
            var isBoolean = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase);

            return new Token()
            {
                Start = start,
                End = _currentIndex,
                Kind = isBoolean ? TokenKind.BOOLEAN : TokenKind.NAME,
                Value = value
            };
        }

        private Token CreatePunctuationToken(TokenKind kind, int offset)
        {
            return new Token()
            {
                Start = _currentIndex,
                End = _currentIndex + offset,
                Kind = kind,
                Value = null
            };
        }

        private char GetCode()
        {
            return IsNotAtTheEndOfQuery()
                ? _source.Body[_currentIndex]
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
                        position = WaitForEndOfComment(body, position, code);
                        break;

                    default:
                        return position;
                }
            }

            return position;
        }

        private char GetUnicodeChar()
        {
            var expression = _source.Body.Substring(_currentIndex, 5);

            if (!OnlyHexInString(expression.Substring(1)))
            {
                throw new QuerySyntaxErrorException($"Invalid character escape sequence: \\{expression}.", _source, _currentIndex);
            }

            var character = (char)(
                CharToHex(NextCode()) << 12 |
                CharToHex(NextCode()) << 8 |
                CharToHex(NextCode()) << 4 |
                CharToHex(NextCode()));

            return character;
        }

        private string IfUnicodeGetString()
        {
            return _source.Body.Length > _currentIndex + 5 &&
                OnlyHexInString(_source.Body.Substring(_currentIndex + 2, 4))
                ? _source.Body.Substring(_currentIndex, 6)
                : null;
        }

        private bool IsNotAtTheEndOfQuery()
        {
            return _currentIndex < _source.Body.Length;
        }

        private char NextCode()
        {
            _currentIndex++;
            return IsNotAtTheEndOfQuery()
                ? _source.Body[_currentIndex]
                : (char)0;
        }

        private char ProcessCharacter(ref string value, ref int chunkStart)
        {
            var code = GetCode();
            ++_currentIndex;

            if (code == '\\')
            {
                value = AppendToValueByCode(AppendCharactersFromLastChunk(value, chunkStart), GetCode());

                ++_currentIndex;
                chunkStart = _currentIndex;
            }

            return GetCode();
        }

        private string ProcessStringChunks()
        {
            var chunkStart = ++_currentIndex;
            var code = GetCode();
            var value = string.Empty;

            while (IsNotAtTheEndOfQuery() && code != 0x000A && code != 0x000D && code != '"' && code != '\'')
            {
                CheckForInvalidCharacters(code);
                code = ProcessCharacter(ref value, ref chunkStart);
            }

            CheckStringTermination(code);
            value += _source.Body.Substring(chunkStart, _currentIndex - chunkStart);
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
                    $"Invalid number, expected digit but got: {ResolveCharName(code)}", _source, _currentIndex);
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
            _currentIndex = ReadDigits(_source, _currentIndex, code);
            code = GetCode();
            return code;
        }

        private Token ReadNameOrBoolean()
        {
            var start = _currentIndex;
            var code = (char)0;

            do
            {
                _currentIndex++;
                code = GetCode();
            }
            while (IsNotAtTheEndOfQuery() && IsValidNameCharacter(code));

            return CreateNameOrBooleanToken(start);
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
                    $"Invalid character \"\\u{code:D4}\".", _source, _currentIndex);
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