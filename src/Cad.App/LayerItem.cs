using System.ComponentModel;
using Avalonia.Media;
using Cad.Document;

namespace Cad.App;

/// <summary>Riga del pannello layer: espone lo stato acceso/spento e avvisa quando cambia.</summary>
public sealed class LayerItem(Layer layer, Action onChanged) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Layer Layer => layer;

    public string Name => layer.Name;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B));

    public bool IsFrozen => layer.IsFrozen;

    public bool IsOn
    {
        get => layer.IsOn;
        set
        {
            if (layer.IsOn == value)
            {
                return;
            }

            layer.IsOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
            onChanged();
        }
    }
}
