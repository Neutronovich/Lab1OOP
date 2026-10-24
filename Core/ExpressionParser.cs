using Antlr4.Runtime;
using Lab1.Core.Parsing;

namespace Lab1.Core;

public static class ExpressionParser
{
    public static Node Parse(string text)
    {
        var tree = CreateParser(text).compileUnit();
        return new AstBuilder(allowVars: false).Visit(tree);
    }

    public static List<Stmt> ParseProgram(string text)
    {
        var tree = CreateParser(text).programUnit();
        return new AstBuilder(allowVars: true).BuildBlock(tree.block());
    }

    private static ExprParser CreateParser(string text)
    {
        var lexer = new ExprLexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new LexerErrors());

        var parser = new ExprParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ParserErrors());
        return parser;
    }

    private sealed class LexerErrors : IAntlrErrorListener<int>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
            => throw new FormulaException("Недопустимий символ", charPositionInLine, 1, true);
    }

    private sealed class ParserErrors : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            if (offendingSymbol.Type == TokenConstants.EOF)
                throw new FormulaException(
                    "Неочікуваний кінець тексту (не вистачає операнда, закриваючої дужки або end)",
                    charPositionInLine, 1, true);

            throw new FormulaException(
                $"Неочікуваний елемент '{offendingSymbol.Text}': перевірте операції, операнди, дужки та ключові слова",
                charPositionInLine, offendingSymbol.Text?.Length ?? 1, true);
        }
    }
}
