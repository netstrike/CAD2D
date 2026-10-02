using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Cad.App.Controls;

namespace Cad.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Canvas.Scene = DemoScene.CreateLines(10_000);
        Canvas.CursorWorldPositionChanged += (_, p) =>
            CoordinatesText.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.0000}, {1:0.0000}", p.X, p.Y);
        Canvas.ViewChanged += (_, _) =>
            ScaleText.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} entità · zoom {1:0.###}", Canvas.Scene.Count, Canvas.View.Scale);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Canvas.Focus();
    }
}
