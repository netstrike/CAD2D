using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Cad.App;

/// <summary>Finestra modale con una casella di testo (nome di un nuovo layer, rinomina...).</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box;
    private string? _result;

    private InputDialog(string title, string message, string initial)
    {
        Title = title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox { Text = initial };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            _result = _box.Text;
            Close();
        };
        var cancel = new Button { Content = "Annulla", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message },
                _box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    /// <summary>Testo inserito, oppure null se l'utente annulla.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string message, string initial = "")
    {
        var dialog = new InputDialog(title, message, initial);
        await dialog.ShowDialog(owner);
        return dialog._result?.Trim();
    }
}
