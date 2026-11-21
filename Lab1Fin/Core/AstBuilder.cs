using System.Numerics;
using Antlr4.Runtime;
using Lab1.Core.Parsing;

namespace Lab1.Core;

internal sealed class AstBuilder : ExprBaseVisitor<Node>
{
    private readonly bool _allowVars;

    public AstBuilder(bool allowVars) => _allowVars = allowVars;

    private static Node Make(ParserRuleContext c, NodeKind kind) => new()
    {
        Kind = kind,
        Pos = c.Start.StartIndex,
        Len = c.Stop.StopIndex - c.Start.StartIndex + 1
    };

    private Node Unary(ParserRuleContext c, Op op, ExprParser.ExpressionContext operand)
    {
        var n = Make(c, NodeKind.Unary);
        n.Op = op;
        n.A = Visit(operand);
        return n;
    }

    private Node Binary(ParserRuleContext c, Op op, ExprParser.ExpressionContext l, ExprParser.ExpressionContext r)
    {
        var n = Make(c, NodeKind.Binary);
        n.Op = op;
        n.A = Visit(l);
        n.B = Visit(r);
        return n;
    }

    public override Node VisitCompileUnit(ExprParser.CompileUnitContext c) => Visit(c.expression());

    public override Node VisitParenExpr(ExprParser.ParenExprContext c) => Visit(c.expression());

    public override Node VisitUnaryExpr(ExprParser.UnaryExprContext c) =>
        Unary(c, c.op.Type == ExprLexer.MINUS ? Op.Minus : Op.Plus, c.expression());

    public override Node VisitNotExpr(ExprParser.NotExprContext c) => Unary(c, Op.Not, c.expression());

    public override Node VisitCompareExpr(ExprParser.CompareExprContext c)
    {
        Op op = c.op.Type switch { ExprLexer.EQ => Op.Eq, ExprLexer.LT => Op.Lt, _ => Op.Gt };
        return Binary(c, op, c.expression(0), c.expression(1));
    }

    public override Node VisitAndExpr(ExprParser.AndExprContext c) => Binary(c, Op.And, c.expression(0), c.expression(1));
    public override Node VisitOrExpr(ExprParser.OrExprContext c) => Binary(c, Op.Or, c.expression(0), c.expression(1));
    public override Node VisitEqvExpr(ExprParser.EqvExprContext c) => Binary(c, Op.Eqv, c.expression(0), c.expression(1));

    public override Node VisitNumberExpr(ExprParser.NumberExprContext c)
    {
        var n = Make(c, NodeKind.Num);
        n.Num = BigInteger.Parse(c.NUMBER().GetText());
        return n;
    }

    public override Node VisitBoolExpr(ExprParser.BoolExprContext c)
    {
        var n = Make(c, NodeKind.Bool);
        n.Bool = c.TRUE() != null;
        return n;
    }

    public override Node VisitCellExpr(ExprParser.CellExprContext c)
    {
        var n = Make(c, NodeKind.Ref);
        string text = c.CELL().GetText();
        if (!Spreadsheet.TryParseAddress(text, out int row, out int col))
            throw new FormulaException($"Некоректна адреса клітинки '{text}'", n.Pos, n.Len, true);
        n.Row = row;
        n.Col = col;
        n.Name = text.ToUpperInvariant();
        return n;
    }

    public override Node VisitVarExpr(ExprParser.VarExprContext c)
    {
        var n = Make(c, NodeKind.Var);
        string text = c.VAR().GetText();
        if (!_allowVars)
            throw new FormulaException(
                $"Невідомий ідентифікатор '{text}' (очікувалось ім'я клітинки, наприклад A1)", n.Pos, n.Len, true);
        n.Name = text.ToLowerInvariant();
        return n;
    }

    public List<Stmt> BuildBlock(ExprParser.BlockContext block) => block.statement().Select(BuildStatement).ToList();

    private Stmt BuildStatement(ExprParser.StatementContext s)
    {
        int pos = s.Start.StartIndex;
        int len = s.Stop.StopIndex - s.Start.StartIndex + 1;

        switch (s)
        {
            case ExprParser.AssignStmtContext a:
                return new AssignStmt
                {
                    Name = a.name.Text.ToLowerInvariant(),
                    Value = Visit(a.value),
                    Pos = pos,
                    Len = len
                };

            case ExprParser.IfStmtContext i:
                return new IfStmt
                {
                    Cond = Visit(i.cond),
                    Then = BuildBlock(i.thenBlock),
                    Else = i.elseBlock != null ? BuildBlock(i.elseBlock) : null,
                    Pos = pos,
                    Len = len
                };

            case ExprParser.WhileStmtContext w:
                return new WhileStmt
                {
                    Cond = Visit(w.cond),
                    Body = BuildBlock(w.body),
                    Pos = pos,
                    Len = len
                };

            default:
                throw new FormulaException("Невідомий оператор", pos, len, true);
        }
    }
}
