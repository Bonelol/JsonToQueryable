namespace JsonQ
{
    public class Parser
    {
        private readonly ILexer _lexer;
        private ParserContext _parserContext;

        private Parser(ILexer lexer)
        {
            this._lexer = lexer;
        }

        public static Parser Parse(ISource source)
        {
            var parser = new Parser(new Lexer());
            parser._parserContext = new ParserContext(source, parser._lexer);
            return parser;
        }

        public Token Next()
        {
            return this._parserContext.Next();
        }

        public Token Back()
        {
            return this._parserContext.Back();
        }
    }
}
