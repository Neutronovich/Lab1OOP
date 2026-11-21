using System.Numerics;

namespace Lab1.Core;

public enum Op { Plus, Minus, Not, And, Or, Eqv, Eq, Lt, Gt }

public enum NodeKind { Num, Bool, Ref, Var, Unary, Binary }

public sealed class Node
{
    public NodeKind Kind;
    public Op Op;
    public BigInteger Num;
    public bool Bool;
    public int Row, Col;
    public string Name = "";
    public Node? A, B;
    public int Pos, Len;
}

public sealed class FormulaException : Exception
{
    public int Pos { get; }
    public int Len { get; }
    public bool IsSyntax { get; }

    public FormulaException(string message, int pos, int len, bool isSyntax) : base(message)
    {
        Pos = pos;
        Len = Math.Max(1, len);
        IsSyntax = isSyntax;
    }

    public string Describe(string raw)
    {
        string frag = Pos >= raw.Length ? "кінець виразу" : raw.Substring(Pos, Math.Min(Len, raw.Length - Pos));
        string kind = IsSyntax ? "Синтаксична помилка" : "Помилка обчислення";
        return $"{kind}: {Message} (позиція {Pos + 1}, «{frag}»)";
    }
}
