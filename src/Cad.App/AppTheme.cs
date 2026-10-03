using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Cad.App;

/// <summary>Colori dell'interfaccia legati al tema (chiavi "Cad.*" in App.axaml) e cambio di tema.</summary>
internal static class AppTheme
{
    /// <summary>Lega la proprietà al pennello <paramref name="key"/> del tema corrente: cambia da sola con il tema.</summary>
    public static T Themed<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    public static bool IsLight => Application.Current?.ActualThemeVariant == ThemeVariant.Light;

    public static void Apply(bool light)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
