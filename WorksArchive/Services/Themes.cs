using System.Globalization;

namespace WorksArchive.Services;

/// <summary>Кольорова схема (акцентний колір інтерфейсу).</summary>
public record ThemeAccent(string Key, string Name, string Color, string Hover)
{
    /// <summary>"13,110,253" — для CSS-змінних Bootstrap виду --bs-primary-rgb.</summary>
    public string Rgb
    {
        get
        {
            var h = Color.TrimStart('#');
            int P(int i) => int.Parse(h.Substring(i, 2), NumberStyles.HexNumber);
            return $"{P(0)},{P(2)},{P(4)}";
        }
    }
}

/// <summary>Доступні теми: режим (світлий/темний/авто) + акцентний колір.</summary>
public static class Themes
{
    public static readonly ThemeAccent[] Accents =
    [
        new("blue", "Синя", "#0d6efd", "#0b5ed7"),
        new("green", "Зелена", "#198754", "#157347"),
        new("teal", "Бірюзова", "#0f7c80", "#0c6366"),
        new("purple", "Фіолетова", "#6f42c1", "#59359a"),
        new("pink", "Малинова", "#c2255c", "#a01f4c"),
        new("orange", "Помаранчева", "#d9480f", "#b83d0d"),
        new("graphite", "Графітова", "#495057", "#343a40"),
    ];

    public static readonly (string Key, string Name, string ShortName, string Icon)[] Modes =
    [
        ("light", "Світла", "Світла", "bi-sun"),
        ("dark", "Темна", "Темна", "bi-moon-stars"),
        ("auto", "Як у системі (автоматично)", "Авто", "bi-circle-half"),
    ];

    public static ThemeAccent FindAccent(string? key) =>
        Accents.FirstOrDefault(a => a.Key == key) ?? Accents[0];

    public static string NormalizeMode(string? mode) =>
        Modes.Any(m => m.Key == mode) ? mode! : "auto";
}
