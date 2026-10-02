using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cad.IO;

namespace Cad.App;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType DxfFiles = new("Disegni DXF") { Patterns = ["*.dxf"] };

    public MainWindow()
    {
        InitializeComponent();

        Canvas.CursorWorldPositionChanged += (_, p) =>
            CoordinatesText.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.0000}, {1:0.0000}", p.X, p.Y);
        Canvas.ViewChanged += (_, _) =>
            ScaleText.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0} entità · zoom {1:0.###}", Canvas.Scene.EntityCount, Canvas.View.Scale);

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.O, KeyModifiers.Control), Command = new RelayCommand(() => _ = OpenWithPickerAsync()) });
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Canvas.Focus();

        // Primo argomento della riga di comando, altrimenti il disegno di esempio.
        var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault()
                   ?? Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf");
        if (File.Exists(path))
        {
            await OpenAsync(path);
        }
        else
        {
            StatusText.Text = "File > Apri (Ctrl+O) oppure trascina qui un file DXF";
        }
    }

    private async Task OpenAsync(string path)
    {
        StatusText.Text = $"Apertura di {Path.GetFileName(path)}...";
        try
        {
            var result = await Task.Run(() => DxfImporter.Load(path));
            Canvas.Document = result.Document;
            LayerList.ItemsSource = result.Document.Layers
                .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(l => new LayerItem(l, Canvas.RefreshScene))
                .ToList();
            Title = $"CAD2D - {Path.GetFileName(path)}";
            StatusText.Text = DescribeImport(path, result);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossibile aprire {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private static string DescribeImport(string path, ImportResult result)
    {
        var parts = new List<string> { Path.GetFileName(path) };
        var unsupported = result.Document.UnsupportedEntities;
        if (unsupported.Count > 0)
        {
            parts.Add("non ancora gestite: " + string.Join(", ", unsupported.OrderByDescending(u => u.Value).Select(u => $"{u.Key} {u.Value}")));
        }

        if (result.Errors.Count > 0)
        {
            parts.Add($"{result.Errors.Count} errori di lettura, parti del file saltate");
        }

        return string.Join(" · ", parts);
    }

    private async Task OpenWithPickerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri disegno",
            AllowMultiple = false,
            FileTypeFilter = [DxfFiles, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await OpenAsync(path);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // API di drag and drop ancora valida in Avalonia 11.
        var file = e.Data.GetFiles()?.FirstOrDefault();
#pragma warning restore CS0618
        if (file?.TryGetLocalPath() is { } path)
        {
            await OpenAsync(path);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await OpenWithPickerAsync();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnZoomExtentsClick(object? sender, RoutedEventArgs e) => Canvas.ZoomExtents();
}
