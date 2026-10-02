using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Cad.App;

/// <summary>Risposta alla domanda "salvare le modifiche?".</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Finestra modale Salva / Non salvare / Annulla.</summary>
public sealed class ConfirmDialog : Window
{
    private SaveChoice _choice = SaveChoice.Cancel;

    private ConfirmDialog(string message)
    {
        Title = "CAD2D";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(MakeButton("Salva", SaveChoice.Save, isDefault: true));
        buttons.Children.Add(MakeButton("Non salvare", SaveChoice.Discard));
        buttons.Children.Add(MakeButton("Annulla", SaveChoice.Cancel, isCancel: true));

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
    }

    public static async Task<SaveChoice> AskAsync(Window owner, string message)
    {
        var dialog = new ConfirmDialog(message);
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }

    private Button MakeButton(string text, SaveChoice choice, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) =>
        {
            _choice = choice;
            Close();
        };
        return button;
    }
}
