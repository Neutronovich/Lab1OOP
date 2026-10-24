namespace Lab1.Core;


public abstract class Stmt
{
    public int Pos, Len;
}


public sealed class AssignStmt : Stmt
{
    public string Name = "";
    public Node Value = null!;
}

public sealed class IfStmt : Stmt
{
    public Node Cond = null!;
    public List<Stmt> Then = new();
    public List<Stmt>? Else;
}

public sealed class WhileStmt : Stmt
{
    public Node Cond = null!;
    public List<Stmt> Body = new();
}
