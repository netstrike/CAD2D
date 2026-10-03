using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Comandi di disegno e modifica dell'MVP. Ogni comando ha un nome italiano e gli alias in stile AutoCAD/DraftSight.</summary>
internal static class BuiltInCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("LINEA", Line, "L", "LINE");
        editor.RegisterCommand("CERCHIO", Circle, "C", "CIRCLE");
        editor.RegisterCommand("ARCO", Arc, "A", "ARC");
        editor.RegisterCommand("POLILINEA", Polyline, "PL", "PLINE");
        editor.RegisterCommand("RETTANGOLO", Rectangle, "REC", "RECTANGLE", "RETT");
        editor.RegisterCommand("SPOSTA", Move, "M", "MOVE");
        editor.RegisterCommand("COPIA", Copy, "CO", "CP", "COPY");
        editor.RegisterCommand("RUOTA", Rotate, "RO", "ROTATE");
        editor.RegisterCommand("CANCELLA", Erase, "E", "ERASE", "CANC");
        editor.RegisterCommand("ANNULLA", Undo, "U", "UNDO");
        editor.RegisterCommand("RIPETI", Redo, "REDO");
        ModifyCommands.Register(editor);
        DraftingCommands.Register(editor);
        AnnotationCommands.Register(editor);
        BlockCommands.Register(editor);
        LayerCommands.Register(editor);
        ClipboardCommands.Register(editor);
        GroupCommands.Register(editor);
        AnnotationTools.Register(editor);
        DrawingScaleCommands.Register(editor);
        ImageCommands.Register(editor);
        AidCommands.Register(editor);
        MeasureCommands.Register(editor);
        PropertyCommands.Register(editor);
    }

    private static async Task Line(Editor ed)
    {
        var first = await ed.GetPointAsync("Primo punto:");
        if (!first.IsOk)
        {
            return;
        }

        var points = new List<Vector2> { first.Point };
        var segments = 0;
        while (true)
        {
            var last = points[^1];
            var keywords = segments >= 2 ? new[] { "Chiudi", "Annulla" } : segments >= 1 ? new[] { "Annulla" } : [];
            var next = await ed.GetPointAsync("Punto successivo:", last, p => [new LineEntity(ed.CurrentLayer, last, p)], keywords);
            switch (next.Status)
            {
                case PromptStatus.Keyword when next.Keyword == "Chiudi":
                    ed.Document.Edit("LINEA", e => e.Add(ed.Styled(new LineEntity(ed.CurrentLayer, last, points[0]))));
                    return;
                case PromptStatus.Keyword when next.Keyword == "Annulla":
                    ed.Document.History.Undo();
                    points.RemoveAt(points.Count - 1);
                    segments--;
                    continue;
                case PromptStatus.Ok when !next.Point.IsAlmostEqual(last):
                    ed.Document.Edit("LINEA", e => e.Add(ed.Styled(new LineEntity(ed.CurrentLayer, last, next.Point))));
                    points.Add(next.Point);
                    segments++;
                    continue;
                case PromptStatus.Ok:
                    continue;
                default:
                    return;
            }
        }
    }

    private static async Task Circle(Editor ed)
    {
        var center = await ed.GetPointAsync("Centro:");
        if (!center.IsOk)
        {
            return;
        }

        var c = center.Point;
        var radius = await ed.GetDistanceAsync("Raggio:", c, p => [new CircleEntity(ed.CurrentLayer, c, Vector2.Distance(c, p))], "Diametro");
        var value = radius.Value;
        if (radius.Status == PromptStatus.Keyword)
        {
            var diameter = await ed.GetDistanceAsync("Diametro:", c, p => [new CircleEntity(ed.CurrentLayer, c, Vector2.Distance(c, p) / 2)]);
            if (!diameter.IsOk)
            {
                return;
            }

            value = diameter.Value / 2;
        }
        else if (!radius.IsOk)
        {
            return;
        }

        if (value <= Tolerance.Default)
        {
            ed.Write("Il raggio deve essere maggiore di zero.");
            return;
        }

        ed.Document.Edit("CERCHIO", e => e.Add(ed.Styled(new CircleEntity(ed.CurrentLayer, c, value))));
    }

    private static async Task Arc(Editor ed)
    {
        var start = await ed.GetPointAsync("Punto iniziale:");
        if (!start.IsOk)
        {
            return;
        }

        var s = start.Point;
        var second = await ed.GetPointAsync("Secondo punto:", s, p => [new LineEntity(ed.CurrentLayer, s, p)]);
        if (!second.IsOk)
        {
            return;
        }

        var m = second.Point;
        var end = await ed.GetPointAsync("Punto finale:", m, p => ArcThrough(ed.CurrentLayer, s, m, p));
        if (!end.IsOk)
        {
            return;
        }

        var arc = ArcThrough(ed.CurrentLayer, s, m, end.Point);
        if (arc.Length == 0)
        {
            ed.Write("I tre punti sono allineati: nessun arco.");
            return;
        }

        ed.Document.Edit("ARCO", e => e.Add(ed.Styled(arc[0])));
    }

    private static Entity[] ArcThrough(Layer layer, Vector2 start, Vector2 through, Vector2 end) =>
        Arc2D.FromThreePoints(start, through, end) is { } a
            ? [new ArcEntity(layer, a.Center, a.Radius, a.StartAngle, a.EndAngle)]
            : [];

    private static async Task Polyline(Editor ed)
    {
        var first = await ed.GetPointAsync("Punto iniziale:");
        if (!first.IsOk)
        {
            return;
        }

        var points = new List<Vector2> { first.Point };
        var closed = false;
        while (true)
        {
            var keywords = points.Count >= 3 ? new[] { "Chiudi", "Annulla" } : points.Count >= 2 ? new[] { "Annulla" } : [];
            var next = await ed.GetPointAsync(
                "Punto successivo:",
                points[^1],
                p => [new PolylineEntity(ed.CurrentLayer, points.Append(p).Select(v => new PolylineVertex(v)), isClosed: false)],
                keywords);
            if (next.Status == PromptStatus.Keyword && next.Keyword == "Chiudi")
            {
                closed = true;
                break;
            }

            if (next.Status == PromptStatus.Keyword && next.Keyword == "Annulla")
            {
                points.RemoveAt(points.Count - 1);
                continue;
            }

            if (next.Status == PromptStatus.Cancel)
            {
                return;
            }

            if (!next.IsOk)
            {
                break;
            }

            if (!next.Point.IsAlmostEqual(points[^1]))
            {
                points.Add(next.Point);
            }
        }

        if (points.Count < 2)
        {
            return;
        }

        ed.Document.Edit("POLILINEA", e => e.Add(ed.Styled(new PolylineEntity(ed.CurrentLayer, points.Select(p => new PolylineVertex(p)), closed))));
    }

    private static async Task Rectangle(Editor ed)
    {
        var first = await ed.GetPointAsync("Primo angolo:");
        if (!first.IsOk)
        {
            return;
        }

        var a = first.Point;
        var second = await ed.GetPointAsync("Angolo opposto:", a, p => [RectangleEntity(ed.CurrentLayer, a, p)]);
        if (!second.IsOk)
        {
            return;
        }

        if (Tolerance.IsZero(a.X - second.Point.X) || Tolerance.IsZero(a.Y - second.Point.Y))
        {
            ed.Write("Il rettangolo ha un lato nullo.");
            return;
        }

        ed.Document.Edit("RETTANGOLO", e => e.Add(ed.Styled(RectangleEntity(ed.CurrentLayer, a, second.Point))));
    }

    private static PolylineEntity RectangleEntity(Layer layer, Vector2 a, Vector2 b) => new(
        layer,
        [new PolylineVertex(a), new PolylineVertex(new Vector2(b.X, a.Y)), new PolylineVertex(b), new PolylineVertex(new Vector2(a.X, b.Y))],
        isClosed: true);

    private static async Task Move(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var from = await ed.GetPointAsync("Punto base:");
        if (!from.IsOk)
        {
            return;
        }

        var b = from.Point;
        var to = await ed.GetPointAsync("Secondo punto:", b, p => Translate(entities, p - b));
        if (!to.IsOk)
        {
            return;
        }

        var delta = to.Point - b;
        ed.Document.Edit("SPOSTA", e =>
        {
            foreach (var entity in entities)
            {
                e.Replace(entity, entity.Transformed(Matrix2D.Translation(delta)));
            }
        });
        ed.Selection.Clear();
    }

    private static async Task Copy(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var from = await ed.GetPointAsync("Punto base:");
        if (!from.IsOk)
        {
            return;
        }

        var b = from.Point;
        ed.Selection.Clear();
        while (true)
        {
            var to = await ed.GetPointAsync("Secondo punto:", b, p => Translate(entities, p - b));
            if (!to.IsOk)
            {
                return;
            }

            var delta = to.Point - b;
            var copies = GroupCommands.Copies(ed.Document, entities, entity => entity.Transformed(Matrix2D.Translation(delta)));
            ed.Document.Edit("COPIA", e =>
            {
                foreach (var copy in copies)
                {
                    e.Add(copy);
                }
            });
        }
    }

    private static async Task Rotate(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var center = await ed.GetPointAsync("Punto base:");
        if (!center.IsOk)
        {
            return;
        }

        var c = center.Point;
        var angle = await ed.GetAngleAsync(
            "Angolo di rotazione (gradi):",
            c,
            p => entities.Select(e => e.Transformed(Matrix2D.Rotation((p - c).Angle, c))));
        if (!angle.IsOk)
        {
            return;
        }

        var rotation = Matrix2D.Rotation(angle.Value, c);
        ed.Document.Edit("RUOTA", e =>
        {
            foreach (var entity in entities)
            {
                e.Replace(entity, entity.Transformed(rotation));
            }
        });
        ed.Selection.Clear();
    }

    private static async Task Erase(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        ed.Document.Edit("CANCELLA", e =>
        {
            foreach (var entity in entities)
            {
                e.Remove(entity);
            }
        });
        ed.Write($"{entities.Count} entità cancellate.");
    }

    private static Task Undo(Editor ed)
    {
        ed.Write(ed.Document.History.Undo() is { } name ? $"Annullato: {name}" : "Niente da annullare.");
        return Task.CompletedTask;
    }

    private static Task Redo(Editor ed)
    {
        ed.Write(ed.Document.History.Redo() is { } name ? $"Ripetuto: {name}" : "Niente da ripetere.");
        return Task.CompletedTask;
    }

    private static IEnumerable<Entity> Translate(IEnumerable<Entity> entities, Vector2 delta) =>
        entities.Select(e => e.Transformed(Matrix2D.Translation(delta)));
}
