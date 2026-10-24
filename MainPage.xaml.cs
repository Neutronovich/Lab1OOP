using Lab1.Core;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Lab1Fin;

public partial class MainPage : ContentPage
{
    private Spreadsheet _sheet = new(10, 8);
    private Entry[,] _entries = new Entry[0, 0];
    private bool _showValues;

    public MainPage()
    {
        InitializeComponent();
        BuildGrid();
    }

    private void BuildGrid()
    {
        SheetGrid.Children.Clear();
        SheetGrid.RowDefinitions.Clear();
        SheetGrid.ColumnDefinitions.Clear();

        SheetGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
        for (int c = 0; c < _sheet.Cols; c++)
            SheetGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        for (int r = 0; r <= _sheet.Rows; r++)
            SheetGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int c = 0; c < _sheet.Cols; c++)
            SheetGrid.Add(Header(Spreadsheet.ColName(c)), c + 1, 0);
        for (int r = 0; r < _sheet.Rows; r++)
            SheetGrid.Add(Header((r + 1).ToString()), 0, r + 1);

        _entries = new Entry[_sheet.Rows, _sheet.Cols];
        for (int r = 0; r < _sheet.Rows; r++)
            for (int c = 0; c < _sheet.Cols; c++)
            {
                int rr = r, cc = c;
                var e = new Entry { MinimumWidthRequest = 110, FontSize = 14 };
                e.Focused += (_, _) => ShowStatus(rr, cc);
                e.Completed += (_, _) => Commit(rr, cc);
                e.Unfocused += (_, _) => Commit(rr, cc);
                _entries[r, c] = e;
                SheetGrid.Add(e, c + 1, r + 1);
            }

        RefreshAll();
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };

    private void RefreshAll()
    {
        for (int r = 0; r < _sheet.Rows; r++)
            for (int c = 0; c < _sheet.Cols; c++)
            {
                var cell = _sheet[r, c];
                var e = _entries[r, c];
                string text = _showValues ? cell.Display : cell.Raw;
                if (e.Text != text) e.Text = text;
                e.IsReadOnly = _showValues;
                e.BackgroundColor = cell.Error != null ? Color.FromArgb("#55FF0000") : Colors.Transparent;
            }
    }

    private void Commit(int r, int c)
    {
        if (_showValues) return;
        string text = _entries[r, c].Text ?? "";
        if (text != _sheet[r, c].Raw)
        {
            _sheet.SetRaw(r, c, text);  
            RefreshAll();
        }
        ShowStatus(r, c);
    }

    private void ShowStatus(int r, int c)
    {
        var cell = _sheet[r, c];
        string addr = Spreadsheet.Address(r, c);
        StatusLabel.Text = cell.Error != null
            ? $"{addr}: {cell.Error.Describe(cell.Raw)}"
            : string.IsNullOrWhiteSpace(cell.Raw)
                ? $"{addr}: порожня клітинка"
                : $"{addr}: {cell.Raw}  →  {cell.Display}";
    }

    private void OnModeToggled(object? sender, ToggledEventArgs e)
    {
        _showValues = e.Value;
        ModeLabel.Text = _showValues ? "Режим: ЗНАЧЕННЯ" : "Режим: ВИРАЗ";
        RefreshAll();
    }

    private void OnShowExpr(object? s, EventArgs e) => ModeSwitch.IsToggled = false;
    private void OnShowValues(object? s, EventArgs e) => ModeSwitch.IsToggled = true;

    private void OnAddRow(object? s, EventArgs e) { _sheet.AddRow(); BuildGrid(); }
    private void OnAddColumn(object? s, EventArgs e) { _sheet.AddColumn(); BuildGrid(); }
    private void OnRemoveRow(object? s, EventArgs e) { _sheet.RemoveRow(); BuildGrid(); }
    private void OnRemoveColumn(object? s, EventArgs e) { _sheet.RemoveColumn(); BuildGrid(); }

    private async void OnNew(object? s, EventArgs e)
    {
        if (!await DisplayAlert("Нова таблиця", "Створити нову таблицю? Незбережені зміни буде втрачено.", "Так", "Ні"))
            return;
        _sheet = new Spreadsheet(10, 8);
        BuildGrid();
    }

    private async void OnSave(object? s, EventArgs e)
    {
        string? name = await DisplayPromptAsync("Збереження", "Ім'я файлу (без розширення):", "Зберегти", "Скасувати", "table");
        if (string.IsNullOrWhiteSpace(name)) return;

        string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + ".sheet.json");
        try
        {
            await File.WriteAllTextAsync(path, _sheet.ToJson());
            await DisplayAlert("Збережено", path, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Помилка збереження", ex.Message, "OK");
        }
    }

    private async void OnOpen(object? s, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Оберіть файл таблиці" });
            if (file == null) return;
            _sheet = Spreadsheet.FromJson(await File.ReadAllTextAsync(file.FullPath));
            BuildGrid();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Не вдалося відкрити файл", ex.Message, "OK");
        }
    }

    // ---------- Довідка ----------
    private async void OnHelp(object? s, EventArgs e) =>
        await DisplayAlert("Допомога",
            "Клітинка містить вираз без знака '=' на початку.\n\n" +
            "Операнди: цілі числа довільної довжини, імена клітинок (A1, B12, AA3), true, false, дужки ( ).\n" +
            "Унарні: +  -\n" +
            "Порівняння: =  <  >  (дають логічне значення)\n" +
            "Логічні: not, and, or, eqv\n\n" +
            "Пріоритет (від вищого): унарні +/- ; = < > ; not ; and ; or ; eqv.\n\n" +
            "ПРОГРАМА В КЛІТИНЦІ (замість виразу). Оператори розділяються символом ';':\n" +
            "  x := вираз  — присвоєння (імена змінних лише з літер, наприклад x або flag; x1 — це вже клітинка);\n" +
            "  if вираз then ... [else ...] end  — умова;\n" +
            "  while вираз do ... end  — цикл (не більше 100000 кроків).\n" +
            "Значення клітинки — значення змінної result після виконання програми.\n" +
            "Приклад: if A1 > 3 then result := true else result := false end\n" +
            "Клітинка вважається програмою, якщо містить ':=' або починається зі слова if/while.\n\n" +
            "Порожня клітинка при посиланні вважається 0.\n" +
            "Режим ВИРАЗ показує текст, ЗНАЧЕННЯ — результат. Помилки підсвічуються червоним, " +
            "а деталі та позиція помилки показуються внизу після вибору клітинки.",
            "OK");

    private async void OnAbout(object? s, EventArgs e) =>
        await DisplayAlert("Про програму",
            "Лабораторна робота №1. Електронні таблиці (аналіз та обчислення виразів).\n" +
            "Варіант 69: унарні +/-; =, <, >; not; or, and; eqv.\n" +
            "Додатково: клітинки-програми (присвоєння, if/else, while).", "OK");
}
