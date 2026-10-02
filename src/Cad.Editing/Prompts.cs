using System.Globalization;
using Cad.Geometry;

namespace Cad.Editing;

public enum PromptStatus
{
    /// <summary>Valore ricevuto.</summary>
    Ok,

    /// <summary>Invio senza valore: di solito chiude il comando o accetta il default.</summary>
    None,

    /// <summary>È stata scelta un'opzione del comando.</summary>
    Keyword,

    /// <summary>Esc o un altro comando hanno interrotto la richiesta.</summary>
    Cancel,
}

public readonly record struct PromptResult(PromptStatus Status, Vector2 Point = default, double Value = 0, string? Keyword = null)
{
    public bool IsOk => Status == PromptStatus.Ok;

    public static PromptResult Cancelled { get; } = new(PromptStatus.Cancel);
    public static PromptResult Empty { get; } = new(PromptStatus.None);
}

internal enum PromptKind
{
    Point,
    Distance,
    Angle,
    Selection,
}

/// <summary>Interpretazione di ciò che si scrive sulla riga di comando.</summary>
public static class InputParser
{
    /// <summary>
    /// Punto scritto come "x,y" (assoluto), "@dx,dy" (relativo), "d&lt;a" o "@d&lt;a" (polare, angolo in gradi).
    /// I punti relativi partono da <paramref name="lastPoint"/>.
    /// </summary>
    public static bool TryParsePoint(string text, Vector2? lastPoint, out Vector2 point)
    {
        point = default;
        var s = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        var relative = s.StartsWith('@');
        if (relative)
        {
            s = s[1..];
        }

        var origin = relative ? lastPoint ?? Vector2.Zero : Vector2.Zero;
        if (relative && s.Length == 0)
        {
            point = origin;
            return true;
        }

        var polar = s.Split('<');
        if (polar.Length == 2 && TryParseNumber(polar[0], out var distance) && TryParseNumber(polar[1], out var degrees))
        {
            point = origin + Vector2.FromPolar(distance, DegreesToRadians(degrees));
            return true;
        }

        var parts = s.Split(',');
        if (parts.Length == 2 && TryParseNumber(parts[0], out var x) && TryParseNumber(parts[1], out var y))
        {
            point = origin + new Vector2(x, y);
            return true;
        }

        return false;
    }

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    public static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;

    /// <summary>Trova l'opzione che inizia con il testo scritto ("c" sceglie "Chiudi").</summary>
    public static string? MatchKeyword(string text, IReadOnlyList<string> keywords)
    {
        var s = text.Trim();
        if (s.Length == 0)
        {
            return null;
        }

        return keywords.FirstOrDefault(k => k.Equals(s, StringComparison.OrdinalIgnoreCase))
               ?? keywords.FirstOrDefault(k => k.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
