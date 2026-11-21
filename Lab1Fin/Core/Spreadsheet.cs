using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lab1.Core;

internal enum CellState { Pending, Evaluating, Done }

public sealed class Cell
{
    public string Raw = "";
    public Node? Ast;
    public List<Stmt>? Program;
    public FormulaException? Error;
    public object? Value;
    internal CellState State;

    public string Display => Error != null ? "#ПОМИЛКА" : Spreadsheet.Format(Value);
}

public sealed class Spreadsheet
{
    public const int MaxSteps = 100_000;

    public const string DeletedRef = "#ПОСИЛАННЯ!";

    private readonly List<List<Cell>> _rows = new();
    public int Rows => _rows.Count;
    public int Cols { get; private set; }

    public Cell this[int r, int c] => _rows[r][c];

    private IEnumerable<Cell> AllCells => _rows.SelectMany(row => row);

    public Spreadsheet(int rows, int cols)
    {
        Cols = cols;
        for (int i = 0; i < rows; i++) AddRow();
    }

    public void AddRow() => _rows.Add(Enumerable.Range(0, Cols).Select(_ => new Cell()).ToList());

    public void AddColumn()
    {
        foreach (var row in _rows) row.Add(new Cell());
        Cols++;
    }

    public void RemoveRow(int index)
    {
        if (Rows <= 1 || index < 0 || index >= Rows) return;
        _rows.RemoveAt(index);
        ShiftRefs((r, c) => r == index ? null : (r > index ? r - 1 : r, c));
    }

    public void RemoveColumn(int index)
    {
        if (Cols <= 1 || index < 0 || index >= Cols) return;
        foreach (var row in _rows) row.RemoveAt(index);
        Cols--;
        ShiftRefs((r, c) => c == index ? null : (r, c > index ? c - 1 : c));
    }

    private void ShiftRefs(Func<int, int, (int Row, int Col)?> move)
    {
        foreach (var cell in AllCells)
            cell.Raw = ExpressionParser.ReplaceCellRefs(cell.Raw, name =>
            {
                if (!TryParseAddress(name, out int r, out int c)) return name;
                var to = move(r, c);
                if (to == null) return DeletedRef;
                return to.Value == (r, c) ? name : Address(to.Value.Row, to.Value.Col);
            });
        Recalculate();
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
        row = 0;
        int i = 0;
        while (i < name.Length && char.IsAsciiLetter(name[i])) i++;
        if (!TryParseColumn(name[..i], out col) || !int.TryParse(name.AsSpan(i), out int r) || r < 1) return false;
        row = r - 1;
        return true;
    }

    public static bool TryParseColumn(string letters, out int col)
    {
        col = -1;
        if (letters.Length is 0 or > 3 || !letters.All(char.IsAsciiLetter)) return false;
        col = letters.Aggregate(0, (acc, ch) => acc * 26 + (char.ToUpperInvariant(ch) - 'A' + 1)) - 1;
        return true;
    }

    public static string Format(object? v) => v switch
    {
        null => "",
        bool b => b ? "ІСТИНА" : "ХИБНІСТЬ",
        _ => v.ToString() ?? ""
    };

    public void Recalculate()
    {
        foreach (var cell in AllCells)
        {
            cell.Ast = null; cell.Program = null; cell.Error = null; cell.Value = null;
            cell.State = CellState.Pending;
            if (string.IsNullOrWhiteSpace(cell.Raw)) { cell.State = CellState.Done; continue; }
            try
            {
                int deleted = cell.Raw.IndexOf(DeletedRef, StringComparison.Ordinal);
                if (deleted >= 0)
                    throw new FormulaException("Посилання на клітинку з видаленого рядка або стовпця", deleted, DeletedRef.Length, false);

                if (LooksLikeProgram(cell.Raw)) cell.Program = ExpressionParser.ParseProgram(cell.Raw);
                else cell.Ast = ExpressionParser.Parse(cell.Raw);
            }
            catch (FormulaException e) { cell.Error = e; cell.State = CellState.Done; }
        }

        foreach (var cell in AllCells)
            Resolve(cell, null);
    }

    private void Resolve(Cell cell, Node? from)
    {
        if (cell.State == CellState.Done) return;
        if (cell.State == CellState.Evaluating)
            throw Err($"Циклічне посилання на клітинку {from?.Name}", from!);

        cell.State = CellState.Evaluating;
        try
        {
            cell.Value = cell.Program != null ? RunProgram(cell.Program) : Eval(cell.Ast!, null);
        }
        catch (FormulaException e) { cell.Error = e; }
        cell.State = CellState.Done;
    }

    private static FormulaException Err(string msg, Node n) => new(msg, n.Pos, n.Len, false);

    private object Eval(Node n, Dictionary<string, object>? env)
    {
        switch (n.Kind)
        {
            case NodeKind.Num: return n.Num;
            case NodeKind.Bool: return n.Bool;

            case NodeKind.Var:
                if (env != null && env.TryGetValue(n.Name, out var varValue)) return varValue;
                throw Err($"Змінна '{n.Name}' не визначена (їй не присвоєно значення)", n);

            case NodeKind.Ref:
            {
                if (n.Row >= Rows || n.Col >= Cols)
                    throw Err($"Клітинка {n.Name} не існує в таблиці", n);
                var target = _rows[n.Row][n.Col];
                Resolve(target, n);
                if (target.Error != null)
                    throw Err($"Клітинка {n.Name} містить помилку", n);
                return target.Value ?? BigInteger.Zero;
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
                        throw Err("Неможливо порівняти число з логічним значенням", n);
                    default:
                        throw Err("Невідома операція", n);
                }
            }
        }
    }

    private static bool AsBool(object v, Node n, string op) =>
        v is bool b ? b : throw Err($"Операція '{op}' вимагає логічного операнда, а отримано число", n);

    private static BigInteger AsInt(object v, Node n, string op) =>
        v is BigInteger i ? i : throw Err($"Операція '{op}' вимагає числового операнда, а отримано логічне значення", n);

    internal static bool LooksLikeProgram(string raw) =>
        raw.Contains(":=") || Regex.IsMatch(raw, @"^\s*(if|while)\b", RegexOptions.IgnoreCase);

    private object RunProgram(List<Stmt> program)
    {
        var env = new Dictionary<string, object>();
        int steps = 0;
        Exec(program, env, ref steps);
        if (env.TryGetValue("result", out var result)) return result;

        var last = program[^1];
        throw new FormulaException("Програма не присвоїла значення змінній result",
            program[0].Pos, last.Pos + last.Len - program[0].Pos, false);
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
        v is bool b ? b : throw Err($"Умова оператора '{keyword}' має бути логічною, а отримано число", n);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string ToJson() =>
        JsonSerializer.Serialize(_rows.Select(r => r.Select(c => c.Raw).ToArray()).ToArray(), JsonOptions);

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
