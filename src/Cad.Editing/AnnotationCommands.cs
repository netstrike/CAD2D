using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Testi e quote: TESTO, TESTOM, MODIFICATESTO, QLINEARE, QALLINEATA, QRAGGIO, QDIAMETRO, QANGOLARE, STILEQUOTA.</summary>
public static class AnnotationCommands
{
    /// <summary>Separatore delle righe quando un testo su più righe si modifica sulla riga di comando (come in MTEXT).</summary>
    private const string LineBreak = "\\P";

    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("TESTO", Text, "DT", "TEXT", "DTEXT");
        editor.RegisterCommand("TESTOM", MultilineText, "T", "MT", "MTEXT");
        editor.RegisterCommand("MODIFICATESTO", EditText, "ED", "DDEDIT", "TEDIT", "TEXTEDIT");
        editor.RegisterCommand("QLINEARE", LinearDimension, "QL", "DLI", "DIMLINEAR");
        editor.RegisterCommand("QALLINEATA", AlignedDimension, "QA", "DAL", "DIMALIGNED");
        editor.RegisterCommand("QRAGGIO", RadiusDimension, "QR", "DRA", "DIMRADIUS");
        editor.RegisterCommand("QDIAMETRO", DiameterDimension, "QD", "DDI", "DIMDIAMETER");
        editor.RegisterCommand("QANGOLARE", AngularDimension, "QAN", "DAN", "DIMANGULAR");
        editor.RegisterCommand("STILEQUOTA", DimensionStyleCommand, "DST", "DIMSTYLE", "DDIM");
    }

    // ---------- Testi ----------

    private static async Task Text(Editor ed)
    {
        var alignment = TextHorizontalAlignment.Left;
        var vertical = TextVerticalAlignment.Baseline;
        PromptResult start;
        while (true)
        {
            start = await ed.GetPointAsync("Punto iniziale del testo:", null, null, "Centro", "Destra", "Mezzo");
            if (start.Status != PromptStatus.Keyword)
            {
                break;
            }

            (alignment, vertical) = start.Keyword switch
            {
                "Centro" => (TextHorizontalAlignment.Center, TextVerticalAlignment.Baseline),
                "Destra" => (TextHorizontalAlignment.Right, TextVerticalAlignment.Baseline),
                _ => (TextHorizontalAlignment.Center, TextVerticalAlignment.Middle),
            };
            ed.Write($"Allineamento: {start.Keyword!.ToLowerInvariant()}.");
        }

        if (!start.IsOk)
        {
            return;
        }

        var position = start.Point;
        if (await AskHeight(ed, position) is not { } height)
        {
            return;
        }

        var rotation = await ed.GetAngleAsync("Rotazione del testo <0>:", position);
        if (rotation.Status == PromptStatus.Cancel)
        {
            return;
        }

        var angle = rotation.IsOk ? rotation.Value : 0;
        var down = -Vector2.FromPolar(1, angle).Perpendicular();
        var lines = 0;
        while (true)
        {
            var line = await ed.GetStringAsync("Testo (Invio a vuoto per finire):");
            if (!line.IsOk)
            {
                break;
            }

            var text = ed.Styled(new TextEntity(ed.CurrentLayer, position, height, line.Text!)
            {
                Rotation = angle,
                HorizontalAlignment = alignment,
                VerticalAlignment = vertical,
            });
            ed.Document.Edit("TESTO", e => e.Add(text));
            position += down * height * text.LineSpacing;
            lines++;
        }

        if (lines == 0)
        {
            ed.Write("Nessun testo inserito.");
        }
    }

    private static async Task MultilineText(Editor ed)
    {
        var start = await ed.GetPointAsync("Angolo in alto a sinistra del testo:");
        if (!start.IsOk)
        {
            return;
        }

        if (await AskHeight(ed, start.Point) is not { } height)
        {
            return;
        }

        var lines = new List<string>();
        while (true)
        {
            var line = await ed.GetStringAsync($"Riga {lines.Count + 1} (Invio a vuoto per finire):");
            if (line.Status == PromptStatus.Cancel)
            {
                return;
            }

            if (!line.IsOk)
            {
                break;
            }

            lines.Add(line.Text!);
        }

        if (lines.Count == 0)
        {
            return;
        }

        var text = ed.Styled(new TextEntity(ed.CurrentLayer, start.Point, height, string.Join('\n', lines))
        {
            VerticalAlignment = TextVerticalAlignment.Top,
        });
        ed.Document.Edit("TESTOM", e => e.Add(text));
    }

    private static async Task<double?> AskHeight(Editor ed, Vector2 basePoint)
    {
        var settings = ed.Settings;
        var height = await ed.GetDistanceAsync($"Altezza <{Editor.Format(settings.TextHeight)}>:", basePoint);
        switch (height.Status)
        {
            case PromptStatus.Ok when height.Value > Tolerance.Default:
                settings.TextHeight = height.Value;
                return height.Value;
            case PromptStatus.None:
                return settings.TextHeight;
            case PromptStatus.Ok:
                ed.Write("L'altezza deve essere maggiore di zero.");
                return null;
            default:
                return null;
        }
    }

    private static async Task EditText(Editor ed)
    {
        ed.Selection.Clear();
        while (true)
        {
            var picked = await ed.GetEntityAsync("Seleziona un testo o una quota:");
            switch (picked.Entity)
            {
                case TextEntity text:
                {
                    var value = await ed.GetStringAsync("Nuovo testo:", text.Value.Replace("\n", LineBreak, StringComparison.Ordinal));
                    if (value.IsOk)
                    {
                        var copy = (TextEntity)text.CopyDetached();
                        copy.Value = value.Text!.Replace(LineBreak, "\n", StringComparison.Ordinal);
                        ed.Document.Edit("MODIFICATESTO", e => e.Replace(text, copy));
                    }

                    break;
                }

                case DimensionEntity dimension:
                {
                    ed.Write("<> rappresenta la misura; testo vuoto torna alla sola misura.");
                    var value = await ed.GetStringAsync("Testo della quota:", dimension.TextOverride ?? "<>");
                    if (value.Status == PromptStatus.Cancel)
                    {
                        break;
                    }

                    // La grafica letta dal file non corrisponderebbe più: con WithGripMoved la quota si rigenera.
                    var copy = (DimensionEntity)dimension.WithGripMoved(dimension.Grips.Count - 1, dimension.Location);
                    copy.TextOverride = !value.IsOk || value.Text!.Trim() == "<>" ? null : value.Text;
                    ed.Document.Edit("MODIFICATESTO", e => e.Replace(dimension, copy));
                    break;
                }

                case null:
                    return;

                default:
                    ed.Write("L'oggetto non è un testo né una quota.");
                    break;
            }
        }
    }

    // ---------- Quote ----------

    private static DimensionEntity NewDimension(Editor ed, DimensionKind kind) =>
        ed.Styled(new DimensionEntity(ed.CurrentLayer, kind, ed.Document.CurrentDimensionStyle));

    private static async Task LinearDimension(Editor ed) => await TwoPointDimension(ed, DimensionKind.Linear);

    private static async Task AlignedDimension(Editor ed) => await TwoPointDimension(ed, DimensionKind.Aligned);

    private static async Task TwoPointDimension(Editor ed, DimensionKind kind)
    {
        ed.Selection.Clear();
        var first = await ed.GetPointAsync("Origine della prima linea di estensione:");
        if (!first.IsOk)
        {
            return;
        }

        var p1 = first.Point;
        var second = await ed.GetPointAsync("Origine della seconda linea di estensione:", p1);
        if (!second.IsOk)
        {
            return;
        }

        var p2 = second.Point;
        if (p1.IsAlmostEqual(p2))
        {
            ed.Write("I due punti coincidono.");
            return;
        }

        string? textOverride = null;
        double? forcedRotation = null;
        DimensionEntity Build(Vector2 location)
        {
            var dimension = NewDimension(ed, kind);
            dimension.First = p1;
            dimension.Second = p2;
            dimension.Location = location;
            dimension.TextOverride = textOverride;
            if (kind == DimensionKind.Linear)
            {
                dimension.Rotation = forcedRotation ?? AutomaticRotation(p1, p2, location);
            }

            return dimension;
        }

        while (true)
        {
            var keywords = kind == DimensionKind.Linear ? new[] { "Testo", "Orizzontale", "Verticale" } : ["Testo"];
            var location = await ed.GetPointAsync("Posizione della linea di quota:", null, p => [Build(p)], keywords);
            switch (location.Status)
            {
                case PromptStatus.Ok:
                {
                    var dimension = Build(location.Point);
                    if (dimension.Measurement <= Tolerance.Default)
                    {
                        ed.Write("La misura in quella direzione è nulla.");
                        continue;
                    }

                    ed.Document.Edit("QUOTA", e => e.Add(dimension));
                    ed.Write($"Misura: {dimension.Text}");
                    return;
                }

                case PromptStatus.Keyword when location.Keyword == "Testo":
                {
                    var text = await ed.GetStringAsync("Testo della quota (<> = misura):", "<>");
                    if (text.IsOk && text.Text!.Trim() != "<>")
                    {
                        textOverride = text.Text;
                    }

                    continue;
                }

                case PromptStatus.Keyword when location.Keyword == "Orizzontale":
                    forcedRotation = 0;
                    continue;
                case PromptStatus.Keyword:
                    forcedRotation = Math.PI / 2;
                    continue;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Come negli altri CAD: se la linea di quota è portata di lato ai due punti la quota è verticale, se sopra o sotto è orizzontale.
    /// </summary>
    public static double AutomaticRotation(Vector2 p1, Vector2 p2, Vector2 location)
    {
        var box = BoundingBox.FromPoints(p1, p2);
        var outsideX = Math.Max(Math.Max(box.Min.X - location.X, location.X - box.Max.X), 0);
        var outsideY = Math.Max(Math.Max(box.Min.Y - location.Y, location.Y - box.Max.Y), 0);
        if (outsideX == 0 && outsideY == 0)
        {
            return box.Width >= box.Height ? 0 : Math.PI / 2;
        }

        return outsideX > outsideY ? Math.PI / 2 : 0;
    }

    private static Task RadiusDimension(Editor ed) => CircleDimension(ed, DimensionKind.Radius);

    private static Task DiameterDimension(Editor ed) => CircleDimension(ed, DimensionKind.Diameter);

    private static async Task CircleDimension(Editor ed, DimensionKind kind)
    {
        ed.Selection.Clear();
        var picked = await ed.GetEntityAsync("Seleziona un arco o un cerchio:");
        (Vector2 Center, double Radius)? circle = picked.Entity switch
        {
            CircleEntity c => (c.Center, c.Radius),
            ArcEntity a => (a.Center, a.Radius),
            PolylineEntity polyline => PolylineArc(polyline, picked.Point),
            _ => null,
        };

        if (circle is not { } c0)
        {
            if (picked.Entity is not null)
            {
                ed.Write("Serve un arco, un cerchio o un tratto curvo di polilinea.");
            }

            return;
        }

        DimensionEntity Build(Vector2 location)
        {
            var direction = (location - c0.Center).Normalized();
            if (direction == Vector2.Zero)
            {
                direction = Vector2.UnitX;
            }

            var dimension = NewDimension(ed, kind);
            dimension.First = c0.Center;
            dimension.Second = c0.Center + direction * c0.Radius;
            dimension.Location = location;
            return dimension;
        }

        var location = await ed.GetPointAsync("Posizione della quota:", null, p => [Build(p)]);
        if (location.IsOk)
        {
            var dimension = Build(location.Point);
            ed.Document.Edit("QUOTA", e => e.Add(dimension));
            ed.Write($"Misura: {dimension.Text}");
        }
    }

    private static async Task AngularDimension(Editor ed)
    {
        ed.Selection.Clear();
        var firstPick = await ed.GetEntityAsync("Seleziona la prima linea o un arco:");
        Vector2 vertex, first, second;
        switch (firstPick.Entity)
        {
            case ArcEntity arc:
                vertex = arc.Center;
                first = arc.Arc.StartPoint;
                second = arc.Arc.EndPoint;
                break;

            case not null when StraightSide(firstPick.Entity, firstPick.Point) is { } side1:
            {
                ed.Selection.Add([firstPick.Entity]);
                var secondPick = await ed.GetEntityAsync("Seleziona la seconda linea:");
                ed.Selection.Clear();
                if (secondPick.Entity is null)
                {
                    return;
                }

                if (StraightSide(secondPick.Entity, secondPick.Point) is not { } side2)
                {
                    ed.Write("Serve una seconda linea.");
                    return;
                }

                var hits = Intersections.SegmentSegment(side1, side2, extend: true);
                if (hits.Count == 0)
                {
                    ed.Write("Le linee sono parallele.");
                    return;
                }

                vertex = hits[0];
                // I lati dell'angolo passano per i punti cliccati.
                first = Side(side1, firstPick.Point, vertex);
                second = Side(side2, secondPick.Point, vertex);
                break;
            }

            case null:
                return;

            default:
                ed.Write("Servono due linee (o lati di polilinea) oppure un arco.");
                return;
        }

        DimensionEntity Build(Vector2 location)
        {
            var dimension = NewDimension(ed, DimensionKind.Angular);
            dimension.Vertex = vertex;
            dimension.First = first;
            dimension.Second = second;
            dimension.Location = location;
            return dimension;
        }

        var position = await ed.GetPointAsync("Posizione dell'arco di quota:", vertex, p => [Build(p)]);
        if (position.IsOk && !position.Point.IsAlmostEqual(vertex))
        {
            var dimension = Build(position.Point);
            ed.Document.Edit("QUOTA", e => e.Add(dimension));
            ed.Write($"Misura: {dimension.Text}");
        }
    }

    /// <summary>Arco di una polilinea (per esempio un raccordo) vicino al punto cliccato.</summary>
    private static (Vector2 Center, double Radius)? PolylineArc(PolylineEntity polyline, Vector2 pick)
    {
        (Vector2, double)? best = null;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < polyline.SegmentCount; i++)
        {
            var (segment, arc) = polyline.GetSegment(i);
            var distance = arc is { } a
                ? a.ContainsAngle((pick - a.Center).Angle)
                    ? Math.Abs(Vector2.Distance(a.Center, pick) - a.Radius)
                    : Math.Min(Vector2.Distance(a.StartPoint, pick), Vector2.Distance(a.EndPoint, pick))
                : segment.DistanceTo(pick);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = arc is { } found ? (found.Center, found.Radius) : null;
            }
        }

        return best;
    }

    /// <summary>Tratto rettilineo cliccato: una linea o un lato diritto di una polilinea.</summary>
    private static Segment2D? StraightSide(Entity entity, Vector2 pick)
    {
        switch (entity)
        {
            case LineEntity line:
                return line.Segment;
            case PolylineEntity polyline:
            {
                Segment2D? best = null;
                var bestDistance = double.PositiveInfinity;
                for (var i = 0; i < polyline.SegmentCount; i++)
                {
                    var (segment, arc) = polyline.GetSegment(i);
                    var distance = arc is { } a ? Math.Abs(Vector2.Distance(a.Center, pick) - a.Radius) : segment.DistanceTo(pick);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = arc is null ? segment : null;
                    }
                }

                return best;
            }

            default:
                return null;
        }
    }

    /// <summary>Punto del lato dalla parte cliccata rispetto al vertice.</summary>
    private static Vector2 Side(Segment2D side, Vector2 pick, Vector2 vertex)
    {
        var point = side.ClosestPoint(pick);
        if (!point.IsAlmostEqual(vertex))
        {
            return point;
        }

        return Vector2.Distance(vertex, side.Start) > Vector2.Distance(vertex, side.End) ? side.Start : side.End;
    }

    private static async Task DimensionStyleCommand(Editor ed)
    {
        var style = ed.Document.CurrentDimensionStyle;
        while (true)
        {
            ed.Write($"Stile {style.Name}: testo {Editor.Format(style.TextHeight)}, frecce {Editor.Format(style.ArrowSize)}, " +
                     $"decimali {style.Decimals}, scala globale {Editor.Format(style.Scale)}.");
            var option = await ed.GetKeywordAsync("Cosa cambiare [Testo/Frecce/Decimali/Scala] <fine>:", "Testo", "Frecce", "Decimali", "Scala");
            if (option.Status != PromptStatus.Keyword)
            {
                return;
            }

            var value = await ed.GetNumberAsync(option.Keyword switch
            {
                "Testo" => "Altezza del testo:",
                "Frecce" => "Lunghezza delle frecce:",
                "Decimali" => "Numero di decimali (0-8):",
                _ => "Scala globale (moltiplica tutte le grandezze):",
            });
            if (!value.IsOk)
            {
                continue;
            }

            switch (option.Keyword)
            {
                case "Testo" when value.Value > 0:
                    style.TextHeight = value.Value;
                    break;
                case "Frecce" when value.Value > 0:
                    style.ArrowSize = value.Value;
                    break;
                case "Decimali" when value.Value is >= 0 and <= 8:
                    style.Decimals = (int)Math.Round(value.Value);
                    break;
                case "Scala" when value.Value > 0:
                    style.Scale = value.Value;
                    break;
                default:
                    ed.Write("Valore non valido.");
                    continue;
            }

            // Le quote già disegnate con questo stile si aggiornano; la modifica va salvata.
            ed.Document.MarkModified();
        }
    }
}
