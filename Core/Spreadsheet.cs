using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lab1.Core;

public sealed class Cell
{
    public string Raw = "";
    public Node? Ast;              
    public List<Stmt>? Program;     
    public FormulaException? Error;
    public object? Value;  
    internal int State;     

    public string Display => Error != null ? "#ПОМИЛКА" : Spreadsheet.Format(Value);
}

public sealed class Spreadsheet
{

    public bool StrictLogical { get; set; } = false;

    private readonly List<List<Cell>> _rows = new();
    public int Rows => _rows.Count;
    public int Cols { get; private set; }

    public Cell this[int r, int c] => _rows[r][c];

    public Spreadsheet(int rows, int cols)
    {
        Cols = cols;
        for (int i = 0; i < rows; i++) AddRow();
    }

    public void AddRow()
    {
        var row = new List<Cell>();
        for (int i = 0; i < Cols; i++) row.Add(new Cell());
        _rows.Add(row);
    }

    public void AddColumn()
    {
        foreach (var r in _rows) r.Add(new Cell());
        Cols++;
    }

    public void RemoveRow()
    {
        if (_rows.Count > 1) { _rows.RemoveAt(_rows.Count - 1); Recalculate(); }
    }

    public void RemoveColumn()
    {
        if (Cols > 1)
        {
            foreach (var r in _rows) r.RemoveAt(r.Count - 1);
            Cols--;
            Recalculate();
        }
    }

    public void SetRaw(int r, int c, string text)
    {
        _rows[r][c].Raw = text;
        Recalculate();
    }

    public static string ColName(int c)
    {
        string s = "";
        c++;
        while (c > 0) { c--; s = (char)('A' + c % 26) + s; c /= 26; }
        return s;
    }

    public static string Address(int r, int c) => ColName(c) + (r + 1);

    public static bool TryParseAddress(string name, out int row, out int col)
    {
        row = col = 0;
        int i = 0;
        while (i < name.Length && char.IsAsciiLetter(name[i])) i++;
        if (i == 0 || i > 3 || i == name.Length) return false;
        if (!int.TryParse(name.AsSpan(i), out int r) || r < 1) return false;
        int cc = 0;
        for (int k = 0; k < i; k++) cc = cc * 26 + (char.ToUpperInvariant(name[k]) - 'A' + 1);
        row = r - 1;
        col = cc - 1;
        return true;
    }

    public static string Format(object? v) => v switch
    {
        null => "",
        bool b => b ? "ІСТИНА" : "ХИБНІСТЬ",
        BigInteger i => i.ToString(),
        _ => v.ToString() ?? ""
    };

    public void Recalculate()
    {
        foreach (var row in _rows)
            foreach (var cell in row)
            {
                cell.Ast = null; cell.Program = null; cell.Error = null; cell.Value = null; cell.State = 0;
                if (string.IsNullOrWhiteSpace(cell.Raw)) { cell.State = 2; continue; }
                try
                {
                    if (LooksLikeProgram(cell.Raw)) cell.Program = ExpressionParser.ParseProgram(cell.Raw);
                    else cell.Ast = ExpressionParser.Parse(cell.Raw);
                }
                catch (FormulaException e) { cell.Error = e; cell.State = 2; }
            }

        foreach (var row in _rows)
            foreach (var cell in row)
                Resolve(cell, null);
    }

    private void Resolve(Cell cell, Node? from)
    {
        if (cell.State == 2) return;
        if (cell.State == 1)
            throw Err($"Циклічне посилання на клітинку {from?.Name}", from!, false);

        cell.State = 1;
        try
        {
            object v = cell.Program != null ? RunProgram(cell.Program) : Eval(cell.Ast!, null);
            if (StrictLogical && v is not bool)
            {
                if (cell.Program != null)
                    throw new FormulaException("Результат програми має бути логічним", cell.Program[0].Pos, TotalLen(cell.Program), false);
                throw Err("Результат виразу має бути логічним", cell.Ast!, false);
            }
            cell.Value = v;
        }
        catch (FormulaException e) { cell.Error = e; }
        cell.State = 2;
    }

    private static FormulaException Err(string msg, Node n, bool syntax) => new(msg, n.Pos, n.Len, syntax);

    private object Eval(Node n, Dictionary<string, object>? env)
    {
        switch (n.Kind)
        {
            case NodeKind.Num: return n.Num;
            case NodeKind.Bool: return n.Bool;

            case NodeKind.Var:
                if (env != null && env.TryGetValue(n.Name, out var varValue)) return varValue;
                throw Err($"Змінна '{n.Name}' не визначена (їй не присвоєно значення)", n, false);

            case NodeKind.Ref:
            {
                if (n.Row >= Rows || n.Col >= Cols)
                    throw Err($"Клітинка {n.Name} не існує в таблиці", n, false);
                var target = _rows[n.Row][n.Col];
                Resolve(target, n);
                if (target.Error != null)
                    throw Err($"Клітинка {n.Name} містить помилку", n, false);
                return target.Value ?? BigInteger.Zero; // порожня клітинка = 0
            }

            case NodeKind.Unary:
            {
                object v = Eval(n.A!, env);
                return n.Op switch
                {
                    Op.Not => !AsBool(v, n.A!, "not"),
                    Op.Minus => -AsInt(v, n.A!, "-"),
                    _ => AsInt(v, n.A!, "+")
                };
            }

            default:
            {
                object l = Eval(n.A!, env);
                object r = Eval(n.B!, env);
                switch (n.Op)
                {
                    case Op.And: return AsBool(l, n.A!, "and") & AsBool(r, n.B!, "and");
                    case Op.Or: return AsBool(l, n.A!, "or") | AsBool(r, n.B!, "or");
                    case Op.Eqv: return AsBool(l, n.A!, "eqv") == AsBool(r, n.B!, "eqv");
                    case Op.Lt: return AsInt(l, n.A!, "<") < AsInt(r, n.B!, "<");
                    case Op.Gt: return AsInt(l, n.A!, ">") > AsInt(r, n.B!, ">");
                    case Op.Eq:
                        if (l is bool lb && r is bool rb) return lb == rb;
                        if (l is BigInteger li && r is BigInteger ri) return li == ri;
                        throw Err("Неможливо порівняти число з логічним значенням", n, false);
                    default:
                        throw Err("Невідома операція", n, false);
                }
            }
        }
    }


    public const int MaxSteps = 100_000;

    internal static bool LooksLikeProgram(string raw) =>
        raw.Contains(":=") || Regex.IsMatch(raw, @"^\s*(if|while)\b", RegexOptions.IgnoreCase);

    private static int TotalLen(List<Stmt> program)
    {
        var last = program[^1];
        return last.Pos + last.Len - program[0].Pos;
    }

    private object RunProgram(List<Stmt> program)
    {
        var env = new Dictionary<string, object>();
        int steps = 0;
        Exec(program, env, ref steps);
        if (!env.TryGetValue("result", out var result))
            throw new FormulaException("Програма не присвоїла значення змінній result",
                program[0].Pos, TotalLen(program), false);
        return result;
    }

    private void Exec(List<Stmt> block, Dictionary<string, object> env, ref int steps)
    {
        foreach (var s in block)
        {
            Tick(s, ref steps);
            switch (s)
            {
                case AssignStmt a:
                    env[a.Name] = Eval(a.Value, env);
                    break;

                case IfStmt i:
                    if (Condition(Eval(i.Cond, env), i.Cond, "if")) Exec(i.Then, env, ref steps);
                    else if (i.Else != null) Exec(i.Else, env, ref steps);
                    break;

                case WhileStmt w:
                    while (Condition(Eval(w.Cond, env), w.Cond, "while"))
                    {
                        Tick(w, ref steps);
                        Exec(w.Body, env, ref steps);
                    }
                    break;
            }
        }
    }

    private static void Tick(Stmt s, ref int steps)
    {
        if (++steps > MaxSteps)
            throw new FormulaException(
                $"Перевищено ліміт {MaxSteps} кроків виконання (можливо, нескінченний цикл)", s.Pos, s.Len, false);
    }

    private static bool Condition(object v, Node n, string keyword) =>
        v is bool b ? b : throw Err($"Умова оператора '{keyword}' має бути логічною, а отримано число", n, false);

    private static bool AsBool(object v, Node n, string op) =>
        v is bool b ? b : throw Err($"Операція '{op}' вимагає логічного операнда, а отримано число", n, false);

    private static BigInteger AsInt(object v, Node n, string op) =>
        v is BigInteger i ? i : throw Err($"Операція '{op}' вимагає числового операнда, а отримано логічне значення", n, false);

    public string ToJson() =>
        JsonSerializer.Serialize(
            _rows.Select(r => r.Select(c => c.Raw).ToArray()).ToArray(),
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static Spreadsheet FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<string[][]>(json);
        if (data is null || data.Length == 0 || data[0].Length == 0 || data.Any(r => r is null || r.Length != data[0].Length))
            throw new FormatException("Файл має неправильний формат таблиці.");

        var sheet = new Spreadsheet(data.Length, data[0].Length);
        for (int r = 0; r < data.Length; r++)
            for (int c = 0; c < data[r].Length; c++)
                sheet[r, c].Raw = data[r][c] ?? "";
        sheet.Recalculate();
        return sheet;
    }
}
