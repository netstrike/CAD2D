namespace Cad.Document;

/// <summary>Colore RGB già risolto.</summary>
public readonly record struct CadColor(byte R, byte G, byte B)
{
    public static readonly CadColor White = new(255, 255, 255);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public enum ColorSource
{
    ByLayer,
    ByBlock,
    Explicit,
}

/// <summary>Colore di un'entità: dal layer, dal blocco che la contiene, oppure esplicito.</summary>
public readonly record struct EntityColor(ColorSource Source, CadColor Value)
{
    public static readonly EntityColor ByLayer = new(ColorSource.ByLayer, CadColor.White);
    public static readonly EntityColor ByBlock = new(ColorSource.ByBlock, CadColor.White);

    public static EntityColor Explicit(CadColor color) => new(ColorSource.Explicit, color);
}
