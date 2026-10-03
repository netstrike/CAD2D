using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.App.Controls;

/// <summary>
/// Tutto ciò che si disegna sopra il disegno, fotografato sul thread dell'interfaccia e poi letto dal thread di rendering:
/// evidenziazione della selezione e dell'oggetto sotto il cursore, grip, anteprima del comando, elastico, finestra, snap e mirino.
/// </summary>
internal sealed record Overlay(
    IReadOnlyList<Vector2[]> Highlight,
    IReadOnlyList<Vector2> Grips,
    IReadOnlyList<(CadColor Color, Vector2[] Points)> Preview,
    Vector2? RubberBandFrom,
    Vector2? RubberBandTo,
    (Vector2 Start, Vector2 End)? Window,
    SnapResult? Snap,
    Vector2? Crosshair,
    IReadOnlyList<Vector2[]> Hover)
{
    public static Overlay Empty { get; } = new([], [], [], null, null, null, null, null, []);
}
