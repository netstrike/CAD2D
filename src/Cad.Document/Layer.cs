namespace Cad.Document;

public sealed class Layer(string name)
{
    public const string DefaultName = "0";

    public string Name { get; internal set; } = name;

    /// <summary>Nome del layer nel file letto o salvato: se il layer viene rinominato, al salvataggio si rinomina quello.</summary>
    public string? FileName { get; set; }
    public CadColor Color { get; set; } = CadColor.White;
    public bool IsOn { get; set; } = true;
    public bool IsFrozen { get; set; }
    public bool IsLocked { get; set; }
    public Linetype Linetype { get; set; } = Linetype.Continuous;

    /// <summary>Spessore di linea in centesimi di millimetro, o <see cref="Document.LineWeight.Default"/>.</summary>
    public int LineWeight { get; set; } = Document.LineWeight.Default;

    /// <summary>Un layer è disegnato solo se è acceso e non congelato.</summary>
    public bool IsVisible => IsOn && !IsFrozen;

    public override string ToString() => Name;
}
