using System.Globalization;
using System.Text;

namespace Cad.IO;

/// <summary>
/// Conversione dei codici di formattazione di TEXT e MTEXT in testo semplice.
/// Scritta qui perché il parser di ACadSharp 3.8 non termina su alcuni codici di paragrafo (es. "\pi1,qj;").
/// </summary>
public static class TextCodes
{
    /// <summary>Codici con un parametro numerico, chiuso da ';' (a volte omesso): colore, altezza, larghezza...</summary>
    private const string NumericCodes = "ACcHQTW";

    /// <summary>Codici con un parametro di testo chiuso da ';': font e formato di paragrafo.</summary>
    private const string TextParameterCodes = "Ffp";

    /// <summary>Codici a una lettera che attivano o disattivano uno stile (sottolineato, barrato...).</summary>
    private const string ToggleCodes = "LlOoKkNn";

    public static string MTextToPlain(string value)
    {
        var result = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (c is '{' or '}')
            {
                i++;
                continue;
            }

            if (c != '\\' || i + 1 >= value.Length)
            {
                result.Append(c);
                i++;
                continue;
            }

            var code = value[i + 1];
            i += 2;
            switch (code)
            {
                case 'P':
                    result.Append('\n');
                    break;
                case '~':
                    result.Append(' ');
                    break;
                case '\\' or '{' or '}':
                    result.Append(code);
                    break;
                case 'U' when TryReadUnicode(value, i, out var unicode):
                    result.Append(unicode);
                    i += 5;
                    break;
                case 'S':
                {
                    // Frazioni impilate: "\S1/2;" o "\S1^2;" diventano "1/2".
                    var end = value.IndexOf(';', i);
                    var stacked = end < 0 ? value[i..] : value[i..end];
                    result.Append(stacked.Replace('^', '/').Replace('#', '/'));
                    i = end < 0 ? value.Length : end + 1;
                    break;
                }

                default:
                    if (NumericCodes.Contains(code))
                    {
                        while (i < value.Length && (char.IsAsciiDigit(value[i]) || value[i] is '.' or '-' or '+' or 'x' or 'X'))
                        {
                            i++;
                        }

                        if (i < value.Length && value[i] == ';')
                        {
                            i++;
                        }
                    }
                    else if (TextParameterCodes.Contains(code))
                    {
                        var end = value.IndexOfAny([';', '}'], i);
                        i = end < 0 ? value.Length : value[end] == ';' ? end + 1 : end;
                    }
                    else if (!ToggleCodes.Contains(code))
                    {
                        // Codice sconosciuto: meglio mostrarlo che perdere testo.
                        result.Append('\\').Append(code);
                    }

                    break;
            }
        }

        return DecodeSpecialCharacters(result.ToString());
    }

    /// <summary>Sostituisce i codici %%c (diametro), %%d (gradi), %%p (più/meno), %%nnn e rimuove %%u/%%o.</summary>
    public static string DecodeSpecialCharacters(string value)
    {
        if (!value.Contains("%%", StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            if (i + 2 < value.Length && value[i] == '%' && value[i + 1] == '%')
            {
                var code = char.ToLowerInvariant(value[i + 2]);
                switch (code)
                {
                    case 'c':
                        result.Append('Ø');
                        i += 3;
                        continue;
                    case 'd':
                        result.Append('°');
                        i += 3;
                        continue;
                    case 'p':
                        result.Append('±');
                        i += 3;
                        continue;
                    case 'u' or 'o' or 'k':
                        i += 3;
                        continue;
                    case '%':
                        result.Append('%');
                        i += 3;
                        continue;
                }

                if (i + 4 < value.Length && int.TryParse(value.AsSpan(i + 2, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    result.Append((char)number);
                    i += 5;
                    continue;
                }
            }

            result.Append(value[i]);
            i++;
        }

        return result.ToString();
    }

    private static bool TryReadUnicode(string value, int start, out char character)
    {
        character = default;
        if (start + 5 > value.Length || value[start] != '+')
        {
            return false;
        }

        if (!int.TryParse(value.AsSpan(start + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        character = (char)code;
        return true;
    }
}
