using System.ComponentModel;
using Avalonia.Media;
using Cad.Document;

namespace Cad.App;

/// <summary>Riga del gestore layer: stato acceso, congelato e bloccato modificabile direttamente dalla lista.</summary>
public sealed class LayerItem(Layer layer, bool isCurrent, Action onChanged) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Layer Layer => layer;

    public string Name => layer.Name;

    public bool IsCurrent => isCurrent;

    public FontWeight NameWeight => isCurrent ? FontWeight.Bold : FontWeight.Normal;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B));

    public string Details => layer.Linetype.Name;

    public bool IsOn
    {
        get => layer.IsOn;
        set => Set(layer.IsOn, value, v => layer.IsOn = v, nameof(IsOn));
    }

    public bool IsFrozen
    {
        get => layer.IsFrozen;
        // Il layer corrente non si congela, come negli altri CAD.
        set => Set(layer.IsFrozen, value && !isCurrent, v => layer.IsFrozen = v, nameof(IsFrozen));
    }

    public bool IsLocked
    {
        get => layer.IsLocked;
        set => Set(layer.IsLocked, value, v => layer.IsLocked = v, nameof(IsLocked));
    }

    private void Set(bool current, bool value, Action<bool> apply, string property)
    {
        if (current != value)
        {
            apply(value);
            onChanged();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
