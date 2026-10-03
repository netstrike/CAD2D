using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// Annotazioni: DIRETTRICE, QCONTINUA, QBASE, QCOORDINATA, QARCO, SEGNOCENTRO, ASSE, TABELLA, CELLA, TOLLERANZA,
/// NUVOLA. Le annotazioni fatte di più oggetti (direttrice e testo, tabelle, tolleranze, segni di centro) formano un
/// gruppo senza nome, così si selezionano e si spostano insieme.
/// </summary>
public static class AnnotationTools
{
    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("DIRETTRICE", Leader, "LE", "LEADER", "QLEADER", "DIR", "MLEADER", "MLD");
        editor.RegisterCommand("QCONTINUA", ContinueDimension, "QC", "DCO", "DIMCONTINUE");
        editor.RegisterCommand("QBASE", BaselineDimension, "QB", "DBA", "DIMBASELINE");
        editor.RegisterCommand("QCOORDINATA", OrdinateDimension, "QO", "DOR", "DIMORDINATE");
        editor.RegisterCommand("QARCO", ArcLengthDimension, "QLA", "DAR", "DIMARC");
        editor.RegisterCommand("SEGNOCENTRO", CenterMark, "CM", "CENTERMARK", "DCE", "DIMCENTER");
        editor.RegisterCommand("ASSE", CenterLine, "CL", "CENTERLINE");
        editor.RegisterCommand("TABELLA", Table, "TB", "TABLE");
        editor.RegisterCommand("CELLA", EditCell, "TABLEDIT");
        editor.RegisterCommand("TOLLERANZA", GeometricTolerance, "TOL", "TOLERANCE");
        editor.RegisterCommand("NUVOLA", RevisionCloud, "NV", "REVCLOUD");
        editor.RegisterCommand("STILETESTO", TextStyleCommand, "ST", "STYLE");
    }

    /// <summary>Aggiunge le parti come un nuovo gruppo senza nome con la descrizione data, in un'unica modifica annullabile.</summary>
    public static void AddAsGroup(Editor ed, string command, string description, IReadOnlyList<Entity> parts)
    {
        if (parts.Count > 1)
        {
            var group = ed.Document.AddGroup()!;
            group.Description = description;
            foreach (var part in parts)
            {
                part.Group = group;
            }
        }

        ed.Document.Edit(command, e =>
        {
            foreach (var part in parts)
            {
                e.Add(part);
            }
        });
    }

    private static double AnnotationTextHeight(Editor ed) =>
        ed.Document.CurrentDimensionStyle.TextHeight * ed.Document.CurrentDimensionStyle.Scale;

    // ---------- Direttrice ----------

    private static async Task Leader(Editor ed)
    {
        ed.Selection.Clear();
        var first = await ed.GetPointAsync("Punta della freccia:");
        if (!first.IsOk)
        {
            return;
        }

        var points = new List<Vector2> { first.Point };
        while (true)
        {
            var next = await ed.GetPointAsync(
                points.Count == 1 ? "Punto successivo:" : "Punto successivo (Invio per scrivere il testo):",
                points[^1],
                p => [NewLeader(ed, [.. points, p])]);
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
            ed.Write("Servono almeno due punti.");
            return;
        }

        var lines = new List<string>();
        while (true)
        {
            var line = await ed.GetStringAsync(lines.Count == 0 ? "Testo della direttrice (Invio a vuoto: nessun testo):" : $"Riga {lines.Count + 1} (Invio a vuoto per finire):");
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

        AddAsGroup(ed, "DIRETTRICE", AnnotationGroups.Leader, BuildLeader(ed, points, lines));
    }

    private static LeaderEntity NewLeader(Editor ed, IEnumerable<Vector2> points) =>
        ed.Styled(new LeaderEntity(ed.CurrentLayer, points, ed.Document.CurrentDimensionStyle));

    /// <summary>
    /// Direttrice e testo: se l'ultimo tratto non è orizzontale si aggiunge un breve tratto orizzontale (l'"approdo"),
    /// e il testo si allinea a quello, a destra o a sinistra secondo la direzione.
    /// </summary>
    public static IReadOnlyList<Entity> BuildLeader(Editor ed, IReadOnlyList<Vector2> points, IReadOnlyList<string> lines)
    {
        var vertices = points.ToList();
        var style = ed.Document.CurrentDimensionStyle;
        var last = vertices[^1] - vertices[^2];
        var side = last.X < -Tolerance.Default ? -1 : 1;
        if (lines.Count > 0 && Math.Abs(last.Y) > Math.Abs(last.X) * Math.Tan(Math.PI / 12))
        {
            vertices.Add(vertices[^1] + new Vector2(side * style.ArrowSize * style.Scale, 0));
        }

        var parts = new List<Entity> { NewLeader(ed, vertices) };
        if (lines.Count > 0)
        {
            var text = ed.Styled(new TextEntity(ed.CurrentLayer, vertices[^1] + new Vector2(side * style.TextGap * style.Scale, 0), AnnotationTextHeight(ed), string.Join('\n', lines))
            {
                HorizontalAlignment = side < 0 ? TextHorizontalAlignment.Right : TextHorizontalAlignment.Left,
                VerticalAlignment = TextVerticalAlignment.Middle,
            });
            parts.Add(text);
        }

        return parts;
    }

    // ---------- Quote in serie e da linea di base ----------

    private static Task ContinueDimension(Editor ed) => ChainDimensions(ed, baseline: false);

    private static Task BaselineDimension(Editor ed) => ChainDimensions(ed, baseline: true);

    private static bool IsChainable(Entity entity) =>
        entity is DimensionEntity { Kind: DimensionKind.Linear or DimensionKind.Aligned or DimensionKind.Angular or DimensionKind.Ordinate };

    /// <summary>
    /// QCONTINUA e QBASE: si parte dall'ultima quota disegnata (o da una scelta con Seleziona) e ogni punto aggiunge una
    /// quota. In serie ognuna parte dalla fine della precedente sulla stessa linea; da base partono tutte dalla stessa
    /// origine, ognuna più lontana della precedente della distanza dello stile.
    /// </summary>
    private static async Task ChainDimensions(Editor ed, bool baseline)
    {
        ed.Selection.Clear();
        var previous = ed.Document.ModelSpace.LastOrDefault(IsChainable) as DimensionEntity;
        Vector2? origin = null;
        if (previous is null)
        {
            (previous, origin) = await PickDimension(ed, baseline);
            if (previous is null)
            {
                return;
            }
        }

        origin ??= baseline ? previous.First : previous.Second;
        var count = 0;
        while (true)
        {
            var current = previous;
            var from = origin.Value;
            var next = await ed.GetPointAsync(
                $"Origine della linea di estensione successiva [Seleziona] <fine>:",
                null,
                p => Next(ed, current, from, p, baseline) is { } d ? [d] : [],
                "Seleziona");
            switch (next.Status)
            {
                case PromptStatus.Ok when Next(ed, current, from, next.Point, baseline) is { } dimension:
                    ed.Document.Edit(baseline ? "QBASE" : "QCONTINUA", e => e.Add(dimension));
                    ed.Write($"Misura: {dimension.Text}");
                    previous = dimension;
                    if (!baseline)
                    {
                        origin = dimension.Kind == DimensionKind.Ordinate ? dimension.First : dimension.Second;
                    }

                    count++;
                    continue;
                case PromptStatus.Ok:
                    ed.Write("La misura sarebbe nulla: indica un altro punto.");
                    continue;
                case PromptStatus.Keyword:
                {
                    var (picked, pickedOrigin) = await PickDimension(ed, baseline);
                    if (picked is not null)
                    {
                        previous = picked;
                        origin = pickedOrigin;
                    }

                    continue;
                }

                default:
                    if (count == 0 && next.Status == PromptStatus.None)
                    {
                        ed.Write("Nessuna quota aggiunta.");
                    }

                    return;
            }
        }
    }

    /// <summary>Quota di partenza scelta con un clic: l'origine è la linea di estensione più vicina al clic.</summary>
    private static async Task<(DimensionEntity?, Vector2)> PickDimension(Editor ed, bool baseline)
    {
        while (true)
        {
            var picked = await ed.GetEntityAsync(baseline ? "Seleziona la quota di base:" : "Seleziona la quota da continuare:");
            if (picked.Entity is null)
            {
                return (null, default);
            }

            if (picked.Entity is DimensionEntity dimension && IsChainable(dimension))
            {
                if (dimension.Kind == DimensionKind.Ordinate)
                {
                    return (dimension, dimension.First);
                }

                var nearFirst = Vector2.Distance(picked.Point, dimension.First) <= Vector2.Distance(picked.Point, dimension.Second);
                return (dimension, nearFirst ? dimension.First : dimension.Second);
            }

            ed.Write("Serve una quota lineare, allineata, angolare o a coordinata.");
        }
    }

    /// <summary>Quota successiva dalla quota <paramref name="previous"/>, con origine <paramref name="from"/> e nuovo punto <paramref name="point"/>.</summary>
    public static DimensionEntity? Next(Editor ed, DimensionEntity previous, Vector2 from, Vector2 point, bool baseline)
    {
        var style = previous.Style;
        var spacing = baseline ? style.BaselineSpacing * style.Scale : 0;
        switch (previous.Kind)
        {
            case DimensionKind.Ordinate:
            {
                var dimension = ed.Styled(new DimensionEntity(ed.CurrentLayer, previous.Kind, style));
                // Stessa origine e stesso tipo; la direttrice finisce alla stessa altezza (o distanza) della precedente.
                dimension.Vertex = previous.Vertex;
                dimension.OrdinateX = previous.OrdinateX;
                dimension.First = point;
                dimension.Second = previous.OrdinateX
                    ? new Vector2(point.X, previous.Second.Y)
                    : new Vector2(previous.Second.X, point.Y);
                dimension.Location = dimension.Second;
                return dimension;
            }

            case DimensionKind.Angular:
            {
                var radius = Vector2.Distance(previous.Vertex, previous.Location) + spacing;
                var a1 = (from - previous.Vertex).Angle;
                var a2 = (point - previous.Vertex).Angle;
                if (point.IsAlmostEqual(previous.Vertex) || Tolerance.IsZero(Arc2D.NormalizeAngle(a2 - a1)))
                {
                    return null;
                }

                // Si prosegue nello stesso verso di rotazione della quota precedente.
                var prevStart = (previous.First - previous.Vertex).Angle;
                var prevEnd = (previous.Second - previous.Vertex).Angle;
                var location = (previous.Location - previous.Vertex).Angle;
                var counterClockwise = Arc2D.NormalizeAngle(location - prevStart) <= Arc2D.NormalizeAngle(prevEnd - prevStart);
                var middle = counterClockwise
                    ? a1 + Arc2D.NormalizeAngle(a2 - a1) / 2
                    : a1 - Arc2D.NormalizeAngle(a1 - a2) / 2;
                return ed.Styled(new DimensionEntity(ed.CurrentLayer, DimensionKind.Angular, style)
                {
                    Vertex = previous.Vertex,
                    First = from,
                    Second = point,
                    Location = previous.Vertex + Vector2.FromPolar(radius, middle),
                });
            }

            default:
            {
                // Lineare (anche al posto di un'allineata): misura nella stessa direzione, sulla stessa linea di quota.
                var direction = previous.Kind == DimensionKind.Linear
                    ? Vector2.FromPolar(1, previous.Rotation)
                    : (previous.Second - previous.First).Normalized();
                var normal = direction.Perpendicular();
                var side = Vector2.Dot(previous.Location - from, normal) < 0 ? -1 : 1;
                var result = ed.Styled(new DimensionEntity(ed.CurrentLayer, DimensionKind.Linear, style)
                {
                    First = from,
                    Second = point,
                    Rotation = direction.Angle,
                    Location = previous.Location + normal * (side * spacing),
                });
                return result.Measurement > Tolerance.Default ? result : null;
            }
        }
    }

    // ---------- Quota a coordinata ----------

    private static async Task OrdinateDimension(Editor ed)
    {
        ed.Selection.Clear();
        PromptResult feature;
        while (true)
        {
            feature = await ed.GetPointAsync($"Punto da quotare [Origine] (origine {Editor.Format(ed.Settings.OrdinateOrigin.X)},{Editor.Format(ed.Settings.OrdinateOrigin.Y)}):", null, null, "Origine");
            if (feature.Status != PromptStatus.Keyword)
            {
                break;
            }

            var origin = await ed.GetPointAsync("Nuova origine delle coordinate:");
            if (origin.IsOk)
            {
                ed.Settings.OrdinateOrigin = origin.Point;
            }
        }

        if (!feature.IsOk)
        {
            return;
        }

        var point = feature.Point;
        bool? forcedX = null;
        string? textOverride = null;
        DimensionEntity Build(Vector2 end)
        {
            var delta = end - point;
            var dimension = ed.Styled(new DimensionEntity(ed.CurrentLayer, DimensionKind.Ordinate, ed.Document.CurrentDimensionStyle)
            {
                Vertex = ed.Settings.OrdinateOrigin,
                First = point,
                Second = end,
                Location = end,
                // Direttrice soprattutto verticale: si misura la X.
                OrdinateX = forcedX ?? Math.Abs(delta.Y) >= Math.Abs(delta.X),
                TextOverride = textOverride,
            });
            return dimension;
        }

        while (true)
        {
            var end = await ed.GetPointAsync("Fine della direttrice [X/Y/Testo]:", point, p => [Build(p)], "X", "Y", "Testo");
            switch (end.Status)
            {
                case PromptStatus.Ok:
                {
                    var dimension = Build(end.Point);
                    ed.Document.Edit("QCOORDINATA", e => e.Add(dimension));
                    ed.Write($"{(dimension.OrdinateX ? "X" : "Y")} = {dimension.Text}");
                    return;
                }

                case PromptStatus.Keyword when end.Keyword == "Testo":
                {
                    var text = await ed.GetStringAsync("Testo della quota (<> = misura):", "<>");
                    textOverride = text.IsOk && text.Text!.Trim() != "<>" ? text.Text : null;
                    continue;
                }

                case PromptStatus.Keyword:
                    forcedX = end.Keyword == "X";
                    continue;
                default:
                    return;
            }
        }
    }

    // ---------- Quota della lunghezza d'arco ----------

    private static async Task ArcLengthDimension(Editor ed)
    {
        ed.Selection.Clear();
        var picked = await ed.GetEntityAsync("Seleziona un arco o un tratto curvo di polilinea:");
        Arc2D? found = picked.Entity switch
        {
            ArcEntity arc => arc.Arc,
            PolylineEntity polyline => PolylineArc(polyline, picked.Point),
            _ => null,
        };
        if (found is not { } arc0)
        {
            if (picked.Entity is not null)
            {
                ed.Write("Serve un arco o un tratto curvo di polilinea.");
            }

            return;
        }

        DimensionEntity Build(Vector2 location) =>
            ed.Styled(new DimensionEntity(ed.CurrentLayer, DimensionKind.ArcLength, ed.Document.CurrentDimensionStyle)
            {
                Vertex = arc0.Center,
                First = arc0.StartPoint,
                Second = arc0.EndPoint,
                Location = location.IsAlmostEqual(arc0.Center) ? arc0.Midpoint : location,
            });

        var position = await ed.GetPointAsync("Posizione dell'arco di quota:", arc0.Center, p => [Build(p)]);
        if (position.IsOk)
        {
            var dimension = Build(position.Point);
            ed.Document.Edit("QARCO", e => e.Add(dimension));
            ed.Write($"Lunghezza: {dimension.Text}");
        }
    }

    /// <summary>Tratto ad arco di una polilinea più vicino al punto cliccato.</summary>
    private static Arc2D? PolylineArc(PolylineEntity polyline, Vector2 pick)
    {
        Arc2D? best = null;
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
                best = arc;
            }
        }

        return best;
    }

    // ---------- Segni di centro e assi ----------

    /// <summary>Lunghezza del ciclo del tipo di linea CENTER: tratto lungo, spazio, tratto corto, spazio.</summary>
    private const double CenterPatternLength = 50.8;

    /// <summary>Metà del tratto lungo, del primo spazio e del tratto corto: la distanza tra l'inizio e il centro del tratto corto.</summary>
    private const double CenterDashMiddle = 41.275;

    /// <summary>
    /// Linea d'asse con tipo di linea CENTER, scalata perché il tratto corto cada esattamente a metà
    /// (il segno al centro del cerchio) e la linea finisca con un tratto lungo, come nei disegni a mano.
    /// </summary>
    private static LineEntity CenterLineSegment(Editor ed, Vector2 start, Vector2 end)
    {
        var line = ed.Styled(new LineEntity(ed.CurrentLayer, start, end));
        line.Linetype = CenterLinetype(ed.Document);
        var halfLength = Vector2.Distance(start, end) / 2;
        var cycles = Math.Max(1, Math.Round((halfLength - CenterDashMiddle) / CenterPatternLength + 1));
        var scale = halfLength / (CenterDashMiddle + (cycles - 1) * CenterPatternLength);
        line.LinetypeScale = scale / ed.Document.LinetypeScale;
        return line;
    }

    private static Linetype CenterLinetype(CadDocument document) =>
        document.FindLinetype("CENTER") ?? document.AddLinetype(Linetype.Standard.First(l => l.Name == "CENTER"));

    private static async Task CenterMark(Editor ed)
    {
        ed.Selection.Clear();
        var count = 0;
        while (true)
        {
            var picked = await ed.GetEntityAsync(count == 0 ? "Seleziona un cerchio o un arco:" : "Seleziona un cerchio o un arco (Invio per finire):");
            (Vector2 Center, double Radius)? circle = picked.Entity switch
            {
                CircleEntity c => (c.Center, c.Radius),
                ArcEntity a => (a.Center, a.Radius),
                PolylineEntity p when PolylineArc(p, picked.Point) is { } arc => (arc.Center, arc.Radius),
                _ => null,
            };
            if (picked.Entity is null)
            {
                return;
            }

            if (circle is not { } c0)
            {
                ed.Write("Serve un cerchio o un arco.");
                continue;
            }

            AddAsGroup(ed, "SEGNOCENTRO", AnnotationGroups.CenterMark, BuildCenterMark(ed, c0.Center, c0.Radius));
            count++;
        }
    }

    /// <summary>Due assi perpendicolari che sporgono dal cerchio della lunghezza delle frecce dello stile di quota.</summary>
    public static IReadOnlyList<Entity> BuildCenterMark(Editor ed, Vector2 center, double radius)
    {
        var style = ed.Document.CurrentDimensionStyle;
        var reach = radius + style.ArrowSize * style.Scale;
        return
        [
            CenterLineSegment(ed, center - new Vector2(reach, 0), center + new Vector2(reach, 0)),
            CenterLineSegment(ed, center - new Vector2(0, reach), center + new Vector2(0, reach)),
        ];
    }

    private static async Task CenterLine(Editor ed)
    {
        ed.Selection.Clear();
        var first = await ed.GetEntityAsync("Seleziona la prima linea:");
        if (first.Entity is null)
        {
            return;
        }

        if (first.Entity is not LineEntity line1)
        {
            ed.Write("Servono due linee.");
            return;
        }

        ed.Selection.Add([line1]);
        var second = await ed.GetEntityAsync("Seleziona la seconda linea:");
        ed.Selection.Clear();
        if (second.Entity is not LineEntity line2 || ReferenceEquals(line1, line2))
        {
            if (second.Entity is not null)
            {
                ed.Write("Serve una seconda linea.");
            }

            return;
        }

        if (BuildCenterLine(ed, line1.Segment, line2.Segment) is not { } axis)
        {
            ed.Write("Le linee non si sovrappongono: l'asse non si può tracciare.");
            return;
        }

        ed.Document.Edit("ASSE", e => e.Add(axis));
    }

    /// <summary>
    /// Asse tra due linee: a metà strada tra le due, lungo il tratto in cui si affacciano, sporgente della lunghezza
    /// delle frecce. Per due linee che si incontrano è la bisettrice.
    /// </summary>
    public static LineEntity? BuildCenterLine(Editor ed, Segment2D a, Segment2D b)
    {
        var (b1, b2) = Vector2.Dot(a.End - a.Start, b.End - b.Start) < 0 ? (b.End, b.Start) : (b.Start, b.End);
        var start = (a.Start + b1) / 2;
        var end = (a.End + b2) / 2;
        var direction = (end - start).Normalized();
        if (direction == Vector2.Zero)
        {
            return null;
        }

        // Si tiene solo il tratto in cui entrambe le linee hanno una proiezione sull'asse.
        double Along(Vector2 p) => Vector2.Dot(p - start, direction);
        var from = Math.Max(Math.Min(Along(a.Start), Along(a.End)), Math.Min(Along(b.Start), Along(b.End)));
        var to = Math.Min(Math.Max(Along(a.Start), Along(a.End)), Math.Max(Along(b.Start), Along(b.End)));
        if (to - from <= Tolerance.Default)
        {
            return null;
        }

        var style = ed.Document.CurrentDimensionStyle;
        var extend = style.ArrowSize * style.Scale;
        return CenterLineSegment(ed, start + direction * (from - extend), start + direction * (to + extend));
    }

    // ---------- Tabelle ----------

    private static async Task Table(Editor ed)
    {
        ed.Selection.Clear();
        var settings = ed.Settings;
        var height = AnnotationTextHeight(ed);
        PromptResult corner;
        while (true)
        {
            ed.Write($"Tabella di {settings.TableColumns} colonne × {settings.TableRows} righe, celle {Editor.Format(settings.TableColumnWidth)} × {Editor.Format(settings.TableRowHeight)}.");
            corner = await ed.GetPointAsync(
                "Angolo in alto a sinistra [Colonne/Righe/Larghezza/Altezza]:",
                null,
                p => BuildTable(ed, p, new string[settings.TableRows, settings.TableColumns], height),
                "Colonne", "Righe", "Larghezza", "Altezza");
            if (corner.Status != PromptStatus.Keyword)
            {
                break;
            }

            var value = await ed.GetNumberAsync(corner.Keyword switch
            {
                "Colonne" => "Numero di colonne (1-50):",
                "Righe" => "Numero di righe (1-200):",
                "Larghezza" => "Larghezza delle colonne:",
                _ => "Altezza delle righe:",
            });
            if (!value.IsOk)
            {
                continue;
            }

            switch (corner.Keyword)
            {
                case "Colonne" when value.Value is >= 1 and <= 50:
                    settings.TableColumns = (int)Math.Round(value.Value);
                    break;
                case "Righe" when value.Value is >= 1 and <= 200:
                    settings.TableRows = (int)Math.Round(value.Value);
                    break;
                case "Larghezza" when value.Value > height:
                    settings.TableColumnWidth = value.Value;
                    break;
                case "Altezza" when value.Value > height:
                    settings.TableRowHeight = value.Value;
                    break;
                default:
                    ed.Write("Valore non valido.");
                    break;
            }
        }

        if (!corner.IsOk)
        {
            return;
        }

        var cells = new string[settings.TableRows, settings.TableColumns];
        ed.Write("Scrivi il contenuto delle celle, riga per riga. Invio a vuoto lascia la cella vuota, Fine lascia vuote le altre.");
        for (var r = 0; r < settings.TableRows; r++)
        {
            for (var c = 0; c < settings.TableColumns; c++)
            {
                var text = await ed.GetStringAsync($"Riga {r + 1}, colonna {c + 1}:");
                if (text.Status == PromptStatus.Cancel)
                {
                    return;
                }

                if (text.IsOk && text.Text!.Trim().Equals("Fine", StringComparison.OrdinalIgnoreCase))
                {
                    r = settings.TableRows;
                    break;
                }

                cells[r, c] = text.IsOk ? text.Text! : string.Empty;
            }
        }

        AddAsGroup(ed, "TABELLA", AnnotationGroups.Table, BuildTable(ed, corner.Point, cells, height));
        ed.Write("Per scrivere o cambiare una cella in seguito: CELLA.");
    }

    /// <summary>Griglia di linee con i testi centrati nelle celle; <paramref name="topLeft"/> è l'angolo in alto a sinistra.</summary>
    public static IReadOnlyList<Entity> BuildTable(Editor ed, Vector2 topLeft, string?[,] cells, double textHeight)
    {
        var settings = ed.Settings;
        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);
        var width = columns * settings.TableColumnWidth;
        var height = rows * settings.TableRowHeight;
        var parts = new List<Entity>();
        for (var r = 0; r <= rows; r++)
        {
            var y = topLeft.Y - r * settings.TableRowHeight;
            parts.Add(ed.Styled(new LineEntity(ed.CurrentLayer, new Vector2(topLeft.X, y), new Vector2(topLeft.X + width, y))));
        }

        for (var c = 0; c <= columns; c++)
        {
            var x = topLeft.X + c * settings.TableColumnWidth;
            parts.Add(ed.Styled(new LineEntity(ed.CurrentLayer, new Vector2(x, topLeft.Y), new Vector2(x, topLeft.Y - height))));
        }

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (!string.IsNullOrEmpty(cells[r, c]))
                {
                    var center = topLeft + new Vector2((c + 0.5) * settings.TableColumnWidth, -(r + 0.5) * settings.TableRowHeight);
                    parts.Add(CellText(ed, center, textHeight, 0, cells[r, c]!));
                }
            }
        }

        return parts;
    }

    private static TextEntity CellText(Editor ed, Vector2 center, double height, double rotation, string value) =>
        ed.Styled(new TextEntity(ed.CurrentLayer, center, height, value)
        {
            Rotation = rotation,
            HorizontalAlignment = TextHorizontalAlignment.Center,
            VerticalAlignment = TextVerticalAlignment.Middle,
        });

    /// <summary>CELLA: un clic dentro una cella di una tabella per scriverne o cambiarne il testo (vuoto = cancella).</summary>
    private static async Task EditCell(Editor ed)
    {
        ed.Selection.Clear();
        while (true)
        {
            var click = await ed.GetPointAsync("Clic dentro una cella della tabella (Invio per finire):");
            if (!click.IsOk)
            {
                return;
            }

            if (FindCell(ed.Document, click.Point) is not { } cell)
            {
                ed.Write("Nessuna cella di tabella in quel punto.");
                continue;
            }

            var value = await ed.GetStringAsync("Testo della cella (vuoto = cancella):", cell.Text?.Value);
            if (value.Status == PromptStatus.Cancel)
            {
                return;
            }

            var newText = value.IsOk ? value.Text! : string.Empty;
            ed.Document.Edit("CELLA", e =>
            {
                if (cell.Text is { } old)
                {
                    if (newText.Length == 0)
                    {
                        e.Remove(old);
                    }
                    else
                    {
                        var copy = (TextEntity)old.CopyDetached();
                        copy.Value = newText;
                        e.Replace(old, copy);
                    }
                }
                else if (newText.Length > 0)
                {
                    var text = CellText(ed, cell.Center, cell.TextHeight, cell.Rotation, newText);
                    text.Group = cell.Group;
                    e.Add(text);
                }
            });
        }
    }

    /// <summary>Cella di una tabella: centro, rotazione, altezza del testo, gruppo e testo che contiene (se c'è).</summary>
    public sealed record TableCell(CadGroup Group, Vector2 Center, double Rotation, double TextHeight, TextEntity? Text);

    /// <summary>
    /// Cella che contiene il punto, ricavata dalle linee del gruppo della tabella (anche spostata o ruotata):
    /// le linee parallele alla prima delimitano le righe, quelle perpendicolari le colonne.
    /// </summary>
    public static TableCell? FindCell(CadDocument document, Vector2 point)
    {
        foreach (var group in document.Groups.Where(g => g.Description == AnnotationGroups.Table))
        {
            var members = document.Members(group).ToList();
            var lines = members.OfType<LineEntity>().ToList();
            if (lines.Count < 4)
            {
                continue;
            }

            var u = (lines[0].End - lines[0].Start).Normalized();
            var v = u.Perpendicular();
            var origin = lines[0].Start;
            double U(Vector2 p) => Vector2.Dot(p - origin, u);
            double V(Vector2 p) => Vector2.Dot(p - origin, v);
            var rows = new List<double>();
            var columns = new List<double>();
            foreach (var line in lines)
            {
                var direction = (line.End - line.Start).Normalized();
                if (Math.Abs(Vector2.Dot(direction, u)) > 0.999)
                {
                    rows.Add(V(line.Start));
                }
                else if (Math.Abs(Vector2.Dot(direction, v)) > 0.999)
                {
                    columns.Add(U(line.Start));
                }
            }

            var pu = U(point);
            var pv = V(point);
            var left = columns.Where(x => x <= pu).DefaultIfEmpty(double.NaN).Max();
            var right = columns.Where(x => x >= pu).DefaultIfEmpty(double.NaN).Min();
            var bottom = rows.Where(y => y <= pv).DefaultIfEmpty(double.NaN).Max();
            var top = rows.Where(y => y >= pv).DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(left) || double.IsNaN(right) || double.IsNaN(bottom) || double.IsNaN(top) ||
                right - left <= Tolerance.Default || top - bottom <= Tolerance.Default)
            {
                continue;
            }

            var center = origin + u * ((left + right) / 2) + v * ((bottom + top) / 2);
            var texts = members.OfType<TextEntity>().ToList();
            var inside = texts.FirstOrDefault(t => U(t.Position) > left && U(t.Position) < right && V(t.Position) > bottom && V(t.Position) < top);
            var textHeight = texts.Count > 0 ? texts[0].Height : Math.Min(2.5, (top - bottom) / 2);
            return new TableCell(group, center, u.Angle, textHeight, inside);
        }

        return null;
    }

    // ---------- Tolleranze geometriche ----------

    /// <summary>Caratteristiche geometriche (ISO 1101) con il nome usato come opzione del comando.</summary>
    public static readonly string[] ToleranceSymbols =
    [
        "Posizione", "Concentricità", "Simmetria", "Parallelismo", "Perpendicolarità", "Inclinazione", "Cilindricità",
        "Planarità", "Circolarità", "Rettilineità", "ProfiloSuperficie", "ProfiloLinea", "Oscillazione", "OscillazioneTotale", "Nessuno",
    ];

    private static async Task GeometricTolerance(Editor ed)
    {
        ed.Selection.Clear();
        var symbol = await ed.GetKeywordAsync("Caratteristica [" + string.Join('/', ToleranceSymbols) + "] <Posizione>:", ToleranceSymbols);
        if (symbol.Status == PromptStatus.Cancel)
        {
            return;
        }

        var name = symbol.Status == PromptStatus.Keyword ? symbol.Keyword! : "Posizione";
        var value = await ed.GetStringAsync("Tolleranza (per esempio Ø0,05; %%c = Ø):");
        if (!value.IsOk)
        {
            return;
        }

        var datums = await ed.GetStringAsync("Riferimenti separati da spazi (per esempio A B C; Invio = nessuno):");
        if (datums.Status == PromptStatus.Cancel)
        {
            return;
        }

        var tolerance = value.Text!;
        var references = (datums.Text ?? string.Empty).Split(new[] { ' ', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        var position = await ed.GetPointAsync("Posizione del riquadro (lato sinistro):", null, p => BuildTolerance(ed, p, name, tolerance, references));
        if (position.IsOk)
        {
            AddAsGroup(ed, "TOLLERANZA", AnnotationGroups.Tolerance, BuildTolerance(ed, position.Point, name, tolerance, references));
        }
    }

    /// <summary>
    /// Riquadro di tolleranza: casella del simbolo (disegnato con linee e archi), casella del valore e una casella per
    /// ogni riferimento. <paramref name="leftMiddle"/> è il punto a metà del lato sinistro.
    /// </summary>
    public static IReadOnlyList<Entity> BuildTolerance(Editor ed, Vector2 leftMiddle, string symbol, string tolerance, IReadOnlyList<string> datums)
    {
        var textHeight = AnnotationTextHeight(ed);
        var h = 2 * textHeight;
        var parts = new List<Entity>();
        double TextWidth(string s) => s.Length * textHeight * TextEntity.AverageCharacterWidth + textHeight;

        var widths = new List<double>();
        var hasSymbol = symbol != "Nessuno";
        if (hasSymbol)
        {
            widths.Add(h);
        }

        widths.Add(Math.Max(h, TextWidth(tolerance)));
        widths.AddRange(datums.Select(d => Math.Max(h, TextWidth(d))));

        var top = leftMiddle.Y + h / 2;
        var bottom = leftMiddle.Y - h / 2;
        var x = leftMiddle.X;
        var total = widths.Sum();
        parts.Add(new LineEntity(ed.CurrentLayer, new Vector2(x, top), new Vector2(x + total, top)));
        parts.Add(new LineEntity(ed.CurrentLayer, new Vector2(x, bottom), new Vector2(x + total, bottom)));
        parts.Add(new LineEntity(ed.CurrentLayer, new Vector2(x, top), new Vector2(x, bottom)));
        var texts = new List<string>();
        if (hasSymbol)
        {
            texts.Add(string.Empty);
        }

        texts.Add(tolerance);
        texts.AddRange(datums);
        for (var i = 0; i < widths.Count; i++)
        {
            var center = new Vector2(x + widths[i] / 2, leftMiddle.Y);
            if (i == 0 && hasSymbol)
            {
                parts.AddRange(ToleranceSymbol(ed.CurrentLayer, symbol, center, h * 0.6));
            }
            else if (texts[i].Length > 0)
            {
                parts.Add(CellText(ed, center, textHeight, 0, texts[i]));
            }

            x += widths[i];
            parts.Add(new LineEntity(ed.CurrentLayer, new Vector2(x, top), new Vector2(x, bottom)));
        }

        foreach (var part in parts)
        {
            ed.Styled(part);
        }

        return parts;
    }

    /// <summary>Simbolo della caratteristica geometrica, centrato in <paramref name="c"/> e alto circa <paramref name="s"/>.</summary>
    public static IEnumerable<Entity> ToleranceSymbol(Layer layer, string symbol, Vector2 c, double s)
    {
        var h = s / 2;
        LineEntity L(double x1, double y1, double x2, double y2) => new(layer, c + new Vector2(x1, y1) * h, c + new Vector2(x2, y2) * h);
        CircleEntity C(double r) => new(layer, c, r * h);
        SolidEntity Arrowhead(Vector2 tip, Vector2 pointing) => LeaderArrow(layer, c + tip * h, pointing, 0.5 * h);
        switch (symbol)
        {
            case "Posizione":
                return [C(0.6), L(-1, 0, 1, 0), L(0, -1, 0, 1)];
            case "Concentricità":
                return [C(0.45), C(0.9)];
            case "Simmetria":
                return [L(-1, 0, 1, 0), L(-0.6, 0.5, 0.6, 0.5), L(-0.6, -0.5, 0.6, -0.5)];
            case "Parallelismo":
                return [L(-0.8, -0.9, -0.1, 0.9), L(0.1, -0.9, 0.8, 0.9)];
            case "Perpendicolarità":
                return [L(-1, -0.8, 1, -0.8), L(0, -0.8, 0, 0.9)];
            case "Inclinazione":
                return [L(-0.9, -0.8, 1, -0.8), L(-0.9, -0.8, 0.8, 0.6)];
            case "Cilindricità":
                return [C(0.55), L(-0.95, -0.9, -0.2, 0.9), L(0.2, -0.9, 0.95, 0.9)];
            case "Planarità":
                return [L(-1, -0.5, 0.5, -0.5), L(0.5, -0.5, 1, 0.5), L(1, 0.5, -0.5, 0.5), L(-0.5, 0.5, -1, -0.5)];
            case "Circolarità":
                return [C(0.85)];
            case "Rettilineità":
                return [L(-1, 0, 1, 0)];
            case "ProfiloSuperficie":
                return [new ArcEntity(layer, c - new Vector2(0, 0.4 * h), 0.9 * h, 0, Math.PI), L(-0.9, -0.4, 0.9, -0.4)];
            case "ProfiloLinea":
                return [new ArcEntity(layer, c - new Vector2(0, 0.4 * h), 0.9 * h, 0, Math.PI)];
            case "Oscillazione":
            {
                var pointing = new Vector2(1, 1).Normalized();
                return [L(-0.8, -0.8, 0.8, 0.8), Arrowhead(new Vector2(0.8, 0.8), pointing)];
            }

            case "OscillazioneTotale":
            {
                var pointing = new Vector2(1, 1).Normalized();
                return
                [
                    L(-0.9, -0.9, 0.9, -0.9), L(-1, -0.6, 0.1, 0.5), L(-0.1, -0.6, 1, 0.5),
                    Arrowhead(new Vector2(0.1, 0.5), pointing), Arrowhead(new Vector2(1, 0.5), pointing),
                ];
            }

            default:
                return [];
        }
    }

    private static SolidEntity LeaderArrow(Layer layer, Vector2 tip, Vector2 pointing, double size)
    {
        var back = tip - pointing * size;
        var side = pointing.Perpendicular() * (size / 4);
        return new SolidEntity(layer, [tip, back + side, back - side]);
    }

    // ---------- Nuvole di revisione ----------

    /// <summary>Angolo al centro di ogni arco della nuvola.</summary>
    private const double CloudArcSweep = 2 * Math.PI / 3;

    private static async Task RevisionCloud(Editor ed)
    {
        ed.Selection.Clear();
        var settings = ed.Settings;
        while (true)
        {
            var first = await ed.GetPointAsync(
                $"Primo angolo [Arco/Poligono/Oggetto] (arco {Editor.Format(settings.CloudArcLength)}):", null, null, "Arco", "Poligono", "Oggetto");
            switch (first.Status)
            {
                case PromptStatus.Ok:
                {
                    var corner = first.Point;
                    var second = await ed.GetPointAsync("Angolo opposto:", corner, p => Rectangle(corner, p) is { } r ? [BuildCloud(ed, r, settings.CloudArcLength)] : []);
                    if (second.IsOk && Rectangle(corner, second.Point) is { } rectangle)
                    {
                        ed.Document.Edit("NUVOLA", e => e.Add(BuildCloud(ed, rectangle, settings.CloudArcLength)));
                    }

                    return;
                }

                case PromptStatus.Keyword when first.Keyword == "Arco":
                {
                    var length = await ed.GetNumberAsync($"Lunghezza degli archi <{Editor.Format(settings.CloudArcLength)}>:");
                    if (length.IsOk && length.Value > Tolerance.Default)
                    {
                        settings.CloudArcLength = length.Value;
                    }

                    continue;
                }

                case PromptStatus.Keyword when first.Keyword == "Poligono":
                {
                    var points = new List<Vector2>();
                    while (true)
                    {
                        var next = await ed.GetPointAsync(
                            points.Count < 3 ? "Punto del contorno:" : "Punto del contorno (Invio per chiudere):",
                            points.Count > 0 ? points[^1] : null,
                            p => points.Count >= 2 ? [BuildCloud(ed, [.. points, p], settings.CloudArcLength)] : []);
                        if (next.Status == PromptStatus.Cancel)
                        {
                            return;
                        }

                        if (!next.IsOk)
                        {
                            break;
                        }

                        points.Add(next.Point);
                    }

                    if (points.Count < 3)
                    {
                        ed.Write("Servono almeno tre punti.");
                        return;
                    }

                    ed.Document.Edit("NUVOLA", e => e.Add(BuildCloud(ed, points, settings.CloudArcLength)));
                    return;
                }

                case PromptStatus.Keyword:
                {
                    var picked = await ed.GetEntityAsync("Seleziona un cerchio, un'ellisse o una polilinea chiusa:");
                    if (picked.Entity is null)
                    {
                        return;
                    }

                    if (CloudOutline(picked.Entity, settings.CloudArcLength) is not { } outline)
                    {
                        ed.Write("Serve un oggetto chiuso: cerchio, ellisse o polilinea chiusa.");
                        continue;
                    }

                    // Come negli altri CAD l'oggetto diventa la nuvola, con il suo layer e colore.
                    var cloud = BuildCloud(ed, outline, settings.CloudArcLength);
                    cloud.CopyStyleFrom<Entity>(picked.Entity);
                    ed.Document.Edit("NUVOLA", e => e.Replace(picked.Entity, cloud));
                    return;
                }

                default:
                    return;
            }
        }
    }

    private static List<Vector2>? Rectangle(Vector2 a, Vector2 b) =>
        Math.Abs(a.X - b.X) > Tolerance.Default && Math.Abs(a.Y - b.Y) > Tolerance.Default
            ? [a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y)]
            : null;

    /// <summary>Contorno di un oggetto chiuso come poligono, con i tratti curvi spezzati in corde lunghe circa un arco.</summary>
    private static List<Vector2>? CloudOutline(Entity entity, double arcLength)
    {
        if (entity is PolylineEntity { IsClosed: false } || entity is not (CircleEntity or EllipseEntity or PolylineEntity))
        {
            return null;
        }

        var curve = Curve.From(entity);
        if (curve is null)
        {
            return null;
        }

        var points = new List<Vector2>();
        foreach (var piece in curve.Pieces)
        {
            var steps = !piece.IsArc ? 1 : Math.Max(4, (int)Math.Ceiling(piece.Length / arcLength));
            for (var i = 0; i < steps; i++)
            {
                points.Add(piece.PointAt((double)i / steps));
            }
        }

        return points.Count >= 3 ? points : null;
    }

    /// <summary>
    /// Nuvola di revisione: polilinea chiusa di archi lunghi circa <paramref name="arcLength"/>, tutti rivolti verso l'esterno.
    /// </summary>
    public static PolylineEntity BuildCloud(Editor ed, IReadOnlyList<Vector2> outline, double arcLength)
    {
        var points = outline.ToList();
        var area = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            area += Vector2.Cross(points[i], points[(i + 1) % points.Count]);
        }

        // In un contorno antiorario l'esterno è a destra del verso di percorrenza: lì un bulge positivo fa sporgere l'arco.
        if (area < 0)
        {
            points.Reverse();
        }

        var chord = arcLength * 2 * Math.Sin(CloudArcSweep / 2) / CloudArcSweep;
        var bulge = Math.Tan(CloudArcSweep / 4);
        var vertices = new List<PolylineVertex>();
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var steps = Math.Max(1, (int)Math.Round(Vector2.Distance(a, b) / chord));
            for (var k = 0; k < steps; k++)
            {
                vertices.Add(new PolylineVertex(a + (b - a) * ((double)k / steps), bulge));
            }
        }

        return ed.Styled(new PolylineEntity(ed.CurrentLayer, vertices, true));
    }

    // ---------- Stili di testo ----------

    /// <summary>STILETESTO dalla riga di comando: nuovo stile, stile corrente, carattere, altezza, larghezza, inclinazione.</summary>
    private static async Task TextStyleCommand(Editor ed)
    {
        var document = ed.Document;
        while (true)
        {
            var style = document.CurrentTextStyle;
            ed.Write($"Stile {style.Name}: carattere {style.FontFamily}, altezza {(style.Height > 0 ? Editor.Format(style.Height) : "libera")}, " +
                     $"larghezza {Editor.Format(style.WidthFactor)}, inclinazione {Editor.Format(InputParser.RadiansToDegrees(style.ObliqueAngle))}°.");
            var option = await ed.GetKeywordAsync(
                "Stile [Nuovo/Corrente/Carattere/Altezza/Larghezza/Inclinazione/Elenco] <fine>:",
                "Nuovo", "Corrente", "Carattere", "Altezza", "Larghezza", "Inclinazione", "Elenco");
            if (option.Status != PromptStatus.Keyword)
            {
                return;
            }

            switch (option.Keyword)
            {
                case "Nuovo":
                {
                    var name = await ed.GetStringAsync("Nome del nuovo stile:");
                    if (!name.IsOk)
                    {
                        continue;
                    }

                    var trimmed = name.Text!.Trim();
                    if (!CadDocument.IsValidName(trimmed) || document.FindTextStyle(trimmed) is not null)
                    {
                        ed.Write("Nome non valido o già usato.");
                        continue;
                    }

                    var created = document.GetOrAddTextStyle(trimmed);
                    created.FontFamily = style.FontFamily;
                    created.Height = style.Height;
                    created.WidthFactor = style.WidthFactor;
                    created.ObliqueAngle = style.ObliqueAngle;
                    document.CurrentTextStyle = created;
                    break;
                }

                case "Corrente":
                {
                    var name = await ed.GetStringAsync("Nome dello stile da rendere corrente:");
                    if (name.IsOk && document.FindTextStyle(name.Text!.Trim()) is { } found)
                    {
                        document.CurrentTextStyle = found;
                    }
                    else if (name.IsOk)
                    {
                        ed.Write("Stile inesistente.");
                    }

                    continue;
                }

                case "Carattere":
                {
                    var family = await ed.GetStringAsync("Nome del carattere (per esempio Arial, Times New Roman):", style.FontFamily);
                    if (family.IsOk && family.Text!.Trim().Length > 0)
                    {
                        style.FontFamily = family.Text.Trim();
                        style.FontFile = null;
                    }

                    break;
                }

                case "Elenco":
                    foreach (var item in document.TextStyles.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        ed.Write($"  {item.Name}{(item == style ? " (corrente)" : string.Empty)}: {item.FontFamily}");
                    }

                    continue;

                default:
                {
                    var value = await ed.GetNumberAsync(option.Keyword switch
                    {
                        "Altezza" => "Altezza fissa (0 = chiesta ogni volta):",
                        "Larghezza" => "Fattore di larghezza:",
                        _ => "Inclinazione in gradi (-85..85):",
                    });
                    if (!value.IsOk)
                    {
                        continue;
                    }

                    switch (option.Keyword)
                    {
                        case "Altezza" when value.Value >= 0:
                            style.Height = value.Value;
                            break;
                        case "Larghezza" when value.Value > 0:
                            style.WidthFactor = value.Value;
                            break;
                        case "Inclinazione" when value.Value is >= -85 and <= 85:
                            style.ObliqueAngle = InputParser.DegreesToRadians(value.Value);
                            break;
                        default:
                            ed.Write("Valore non valido.");
                            continue;
                    }

                    break;
                }
            }

            // I testi con questo stile cambiano aspetto: il disegno va salvato.
            document.MarkModified();
        }
    }
}
