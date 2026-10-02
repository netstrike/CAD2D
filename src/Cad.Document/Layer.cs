namespace Cad.Document;

public sealed class Layer(string name)
{
    public const string DefaultName = "0";

    public string Name { get; } = name;
    public CadColor Color { get; set; } = CadColor.White;
    public bool IsOn { get; set; } = true;
    public bool IsFrozen { get; set; }
    public bool IsLocked { get; set; }

    /// <summary>Un layer è disegnato solo se è acceso e non congelato.</summary>
    public bool IsVisible => IsOn && !IsFrozen;

    public override string ToString() => Name;
}
