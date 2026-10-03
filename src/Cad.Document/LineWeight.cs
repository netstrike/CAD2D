using System.Globalization;

namespace Cad.Document;

/// <summary>
/// Spessori di linea come nei file DWG/DXF: centesimi di millimetro, oppure i valori speciali DaLayer, DaBlocco e
/// Predefinito.
/// </summary>
public static class LineWeight
{
    public const int ByLayer = -1;
    public const int ByBlock = -2;
    public const int Default = -3;

    /// <summary>Spessore usato per "Predefinito", in centesimi di millimetro.</summary>
    public const int DefaultValue = 25;

    /// <summary>Gli spessori ammessi dal formato.</summary>
    public static IReadOnlyList<int> Standard { get; } =
        [0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211];

    public static string Format(int value) => value switch
    {
        ByLayer => "DaLayer",
        ByBlock => "DaBlocco",
        Default => "Predefinito",
        _ => (value / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + " mm",
    };

    /// <summary>Legge "0.35", "0,35 mm", "DaLayer"...; accetta solo i valori del formato.</summary>
    public static bool TryParse(string text, out int value)
    {
        var t = text.Trim();
        foreach (var special in new[] { ByLayer, ByBlock, Default })
        {
            if (string.Equals(t, Format(special), StringComparison.OrdinalIgnoreCase))
            {
                value = special;
                return true;
            }
        }

        t = t.Replace("mm", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(',', '.');
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm))
        {
            var hundredths = (int)Math.Round(mm * 100);
            if (Standard.Contains(hundredths))
            {
                value = hundredths;
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Spessore in millimetri dopo aver risolto i valori speciali.</summary>
    public static double Millimetres(int resolved) => (resolved < 0 ? DefaultValue : resolved) / 100.0;
}
