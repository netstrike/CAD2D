using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Strumenti di misura che non cambiano il disegno: DISTANZA, AREA e ID.</summary>
internal static class MeasureCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("DISTANZA", Distance, "DI", "DIST");
        editor.RegisterCommand("AREA", Area, "AA");
        editor.RegisterCommand("ID", Identify);
    }

    private static string F(double value) => Editor.Format(Math.Round(value, 4));

    private static string Degrees(Vector2 delta)
    {
        var degrees = InputParser.RadiansToDegrees(delta.Angle);
        return F(((degrees % 360) + 360) % 360);
    }

    private static async Task Distance(Editor ed)
    {
        var first = await ed.GetPointAsync("Primo punto:");
        if (!first.IsOk)
        {
            return;
        }

        var second = await ed.GetPointAsync("Secondo punto:", first.Point);
        if (!second.IsOk)
        {
            return;
        }

        var delta = second.Point - first.Point;
        ed.Write($"Distanza = {F(delta.Length)}, Angolo = {Degrees(delta)}°, Delta X = {F(delta.X)}, Delta Y = {F(delta.Y)}");
    }

    private static async Task Identify(Editor ed)
    {
        var point = await ed.GetPointAsync("Punto da identificare:");
        if (point.IsOk)
        {
            ed.Write($"X = {F(point.Point.X)}  Y = {F(point.Point.Y)}");
        }
    }

    /// <summary>Area e perimetro di un poligono indicato per punti, oppure di un oggetto chiuso.</summary>
    private static async Task Area(Editor ed)
    {
        var points = new List<Vector2>();
        var first = await ed.GetPointAsync("Primo punto:", null, null, "Oggetto");
        if (first.Status == PromptStatus.Keyword)
        {
            await AreaOfObject(ed);
            return;
        }

        if (!first.IsOk)
        {
            return;
        }

        points.Add(first.Point);
        while (true)
        {
            var next = await ed.GetPointAsync(
                points.Count < 3 ? "Punto successivo:" : "Punto successivo o <Invio per il totale>:",
                points[^1],
                p => points.Count < 2 ? [] : [new PolylineEntity(ed.CurrentLayer, [.. points.Append(p).Select(v => new PolylineVertex(v))], true)]);
            if (next.IsOk)
            {
                points.Add(next.Point);
                continue;
            }

            if (next.Status == PromptStatus.Cancel)
            {
                return;
            }

            break;
        }

        if (points.Count < 3)
        {
            ed.Write("Servono almeno tre punti.");
            return;
        }

        var vertices = points.Select(p => new PolylineVertex(p)).ToList();
        var perimeter = points.Select((p, i) => Vector2.Distance(p, points[(i + 1) % points.Count])).Sum();
        ed.Write($"Area = {F(PropertySheet.PolylineArea(vertices))}, Perimetro = {F(perimeter)}");
    }

    private static async Task AreaOfObject(Editor ed)
    {
        var picked = await ed.GetEntityAsync("Seleziona un oggetto chiuso:");
        if (!picked.IsOk || picked.Entity is not { } entity)
        {
            return;
        }

        if (entity is HatchEntity hatch)
        {
            var area = hatch.Loops.Select(l => PropertySheet.PolylineArea(l)).OrderByDescending(a => a).ToList();
            // Il contorno più grande meno le isole.
            ed.Write($"Area = {F(area.Count == 0 ? 0 : area[0] - area.Skip(1).Sum())}");
            return;
        }

        if (Hatching.ClosedLoop(entity) is not { } loop)
        {
            ed.Write($"{PropertySheet.TypeName(entity)}: non è chiuso. Lunghezza = {F(PropertySheet.Length(entity))}");
            return;
        }

        var perimeter = PropertySheet.Length(new PolylineEntity(entity.Layer, loop, true));
        ed.Write($"Area = {F(PropertySheet.PolylineArea(loop))}, {(entity is CircleEntity ? "Circonferenza" : "Perimetro")} = {F(perimeter)}");
    }
}
