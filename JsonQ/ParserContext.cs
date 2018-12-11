using System;

namespace JsonQ
{
    public class ParserContext : IDisposable
    {
        private Token _currentToken;
        private readonly ILexer _lexer;
        private readonly ISource _source;

        public ParserContext(ISource source, ILexer lexer)
        {
            this._source = source;
            this._lexer = lexer;

            this._currentToken = null;
        }

        public Token Next()
        {
            if (_currentToken?.Next != null)
            {
                _currentToken = _currentToken.Next;
            }
            else
            {
                var t = _currentToken;
                _currentToken = this._lexer.Lex(_source, _currentToken?.End ?? 0);
                _currentToken.Previous = t;

                if(t != null)
                    t.Next = _currentToken;
            }

            return _currentToken;
        }

        public void Dispose()
        {
        }

        public Token Back()
        {
            this._currentToken = _currentToken?.Previous ?? throw new Exception("_currentToken.PreviousToken == null");
            return _currentToken;
        }

        public void Advance()
        {
            this._currentToken = this._lexer.Lex(this._source, this._currentToken.End);
        }

        public Token CurrentToken => _currentToken;
    }
}
