using System.Diagnostics;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using Cad.IO;
using Cad.Rendering;
using SkiaSharp;

// Prova di velocità: un disegno con N entità (100.000 di default) e i tempi delle operazioni che l'utente sente.
// Uso: dotnet run -c Release --project tools/Cad.Bench [numero di entità] [cartella per i file]
var count = args.Length > 0 ? int.Parse(args[0]) : 100_000;
var folder = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "cad2d-bench");
Directory.CreateDirectory(folder);

var results = new List<(string Name, double Ms)>();
T Time<T>(string name, Func<T> action)
{
    var watch = Stopwatch.StartNew();
    var result = action();
    watch.Stop();
    results.Add((name, watch.Elapsed.TotalMilliseconds));
    Console.WriteLine($"{name,-48} {watch.Elapsed.TotalMilliseconds,10:0.0} ms");
    return result;
}

void TimeAction(string name, Action action) => Time(name, () => { action(); return 0; });

var document = Time("Crea il disegno", () => Generate(count));
Console.WriteLine($"  {document.ModelSpace.Count} entità, ingombro {document.Bounds.Width:0} × {document.Bounds.Height:0}");

var cache = new SceneCache();
var scene = Time("Scena (SceneBuilder.Build)", () => SceneBuilder.Build(document, exactArcs: true, cache));
Console.WriteLine($"  {scene.Batches.Sum(b => b.Arcs.Count)} archi, {scene.Batches.Sum(b => b.Polylines.Count)} spezzate, {scene.Batches.Sum(b => b.Polylines.Sum(p => p.Length))} vertici, {scene.Texts.Count} testi");
var paths = Time("Percorsi Skia (come SceneGeometry)", () => ToPaths(scene));

using var surface = SKSurface.Create(new SKImageInfo(1920, 1080));
var all = document.Bounds;
TimeAction("Disegno: tutto il disegno (1920×1080)", () => Draw(surface.Canvas, paths, scene, all));
TimeAction("Disegno: tutto il disegno, di nuovo", () => Draw(surface.Canvas, paths, scene, all));
var detail = new BoundingBox(all.Center, all.Center + new Vector2(all.Width / 20, all.Height / 20));
TimeAction("Disegno: zoom su 1/400 dell'area", () => Draw(surface.Canvas, paths, scene, detail));

var editor = new Editor(document);
var probe = all.Center + new Vector2(3, 3);
Time("Primo clic (costruisce l'indice spaziale)", () => editor.Locator.Pick(probe, 1));
TimeAction("1000 clic di selezione", () =>
{
    for (var i = 0; i < 1000; i++)
    {
        editor.Locator.Pick(probe + new Vector2(i % 50, i / 50), 1);
    }
});
TimeAction("1000 ricerche di snap", () =>
{
    for (var i = 0; i < 1000; i++)
    {
        var p = probe + new Vector2(i % 50, i / 50);
        SnapEngine.Find(editor.Locator.Near(p, 1), p, 1, SnapModes.Default);
    }
});

// Una modifica piccola: la scena riusa i pezzi delle entità che non sono cambiate; percorsi e indice si rifanno.
var line = new LineEntity(document.GetOrAddLayer("0"), Vector2.Zero, new Vector2(10, 10));
TimeAction("Una linea in più: la modifica", () => document.Edit("LINEA", e => e.Add(line)));
var rebuilt = Time("  scena", () => SceneBuilder.Build(document, exactArcs: true, cache));
Console.WriteLine($"  pezzi riusati {cache.Reused}, ricalcolati {cache.Computed}");
Dispose(Time("  percorsi", () => ToPaths(rebuilt)));
Time("  indice spaziale", () => editor.Locator.Pick(probe, 1));

var everything = document.ModelSpace.ToList();
TimeAction("Seleziona tutto", () => editor.Selection.Add(everything));
TimeAction("SPOSTA di tutto (una modifica)", () => document.Edit("SPOSTA", e =>
{
    var m = Matrix2D.Translation(new Vector2(5, 0));
    foreach (var entity in everything)
    {
        e.Replace(entity, entity.Transformed(m));
    }
}));
TimeAction("ANNULLA dello spostamento", () => document.History.Undo());
TimeAction("CANCELLA di metà disegno", () => document.Edit("CANCELLA", e =>
{
    foreach (var entity in everything.Take(everything.Count / 2))
    {
        e.Remove(entity);
    }
}));
TimeAction("ANNULLA della cancellazione", () => document.History.Undo());
editor.Selection.Clear();

var dxf = Path.Combine(folder, "bench.dxf");
var dwg = Path.Combine(folder, "bench.dwg");
TimeAction("Salva DXF", () => CadFile.Save(document, dxf));
TimeAction("Salva DWG", () => CadFile.Save(document, dwg));
Console.WriteLine($"  DXF {new FileInfo(dxf).Length / 1e6:0.0} MB, DWG {new FileInfo(dwg).Length / 1e6:0.0} MB");
var fromDxf = Time("Apri DXF", () => CadFile.Load(dxf).Document);
var fromDwg = Time("Apri DWG", () => CadFile.Load(dwg).Document);
Console.WriteLine($"  riaperti: {fromDxf.ModelSpace.Count} e {fromDwg.ModelSpace.Count} entità");

Dispose(paths);
return;

static CadDocument Generate(int count)
{
    // Celle di 100 × 100 con una piccola "piastra" ciascuna: linee, cerchi, archi, polilinee, testi, quote e tratteggi.
    var document = new CadDocument();
    var outline = document.GetOrAddLayer("CONTORNO");
    var holes = document.GetOrAddLayer("FORI");
    holes.Color = new CadColor(0, 255, 255);
    var axes = document.GetOrAddLayer("ASSI");
    axes.Color = new CadColor(255, 0, 0);
    axes.Linetype = document.FindLinetype("CENTER") ?? axes.Linetype;
    var notes = document.GetOrAddLayer("TESTI");
    notes.Color = new CadColor(255, 255, 0);
    var hatch = document.GetOrAddLayer("TRATTEGGI");
    hatch.Color = new CadColor(128, 128, 128);

    var entities = new List<Entity>(count);
    var side = (int)Math.Ceiling(Math.Sqrt(count / 12.0));
    for (var cell = 0; entities.Count < count; cell++)
    {
        var o = new Vector2(cell % side * 100, cell / side * 100);
        entities.Add(new PolylineEntity(outline, [new(o + new Vector2(10, 10)), new(o + new Vector2(90, 10), 0.4142), new(o + new Vector2(90, 90)), new(o + new Vector2(10, 90))], isClosed: true));
        for (var k = 0; k < 4; k++)
        {
            var c = o + new Vector2(25 + k % 2 * 50, 25 + k / 2 * 50);
            entities.Add(new CircleEntity(holes, c, 6));
            entities.Add(new LineEntity(axes, c - new Vector2(9, 0), c + new Vector2(9, 0)));
        }

        entities.Add(new ArcEntity(outline, o + new Vector2(50, 50), 15, 0.2, 2.9));
        entities.Add(new LineEntity(outline, o + new Vector2(35, 50), o + new Vector2(65, 50)));
        entities.Add(new TextEntity(notes, o + new Vector2(12, 92), 3, $"P{cell}"));
        entities.Add(Hatching.Create(hatch, [[new(o + new Vector2(40, 60)), new(o + new Vector2(60, 60)), new(o + new Vector2(60, 70)), new(o + new Vector2(40, 70))]], "ANSI31", 1, 0));
    }

    document.Edit("GENERA", e =>
    {
        foreach (var entity in entities.Take(count))
        {
            e.Add(entity);
        }
    });
    return document;
}

static List<(SKColor Color, SKPath Path)> ToPaths(Scene scene)
{
    var origin = scene.Bounds.Center;
    var result = new List<(SKColor, SKPath)>();
    foreach (var batch in scene.Batches)
    {
        var path = new SKPath();
        foreach (var polyline in batch.Polylines)
        {
            var first = polyline[0] - origin;
            path.MoveTo((float)first.X, (float)first.Y);
            for (var i = 1; i < polyline.Length; i++)
            {
                var p = polyline[i] - origin;
                path.LineTo((float)p.X, (float)p.Y);
            }
        }

        foreach (var arc in batch.Arcs)
        {
            var c = arc.Center - origin;
            var r = (float)arc.Radius;
            if (arc.IsCircle)
            {
                path.AddCircle((float)c.X, (float)c.Y, r);
            }
            else
            {
                path.AddArc(new SKRect((float)c.X - r, (float)c.Y - r, (float)c.X + r, (float)c.Y + r), (float)(arc.StartAngle * 180 / Math.PI), (float)(arc.Sweep * 180 / Math.PI));
            }
        }

        result.Add((new SKColor(batch.Color.R, batch.Color.G, batch.Color.B), path));
    }

    return result;
}

static void Draw(SKCanvas canvas, List<(SKColor Color, SKPath Path)> paths, Scene scene, BoundingBox view)
{
    var origin = scene.Bounds.Center;
    var scale = (float)Math.Min(1920 / view.Width, 1080 / view.Height);
    canvas.Clear(new SKColor(0x21, 0x21, 0x21));
    canvas.Save();
    canvas.Translate(960, 540);
    canvas.Scale(scale, -scale);
    canvas.Translate((float)(origin.X - view.Center.X), (float)(origin.Y - view.Center.Y));
    using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0, IsAntialias = true };
    foreach (var (color, path) in paths)
    {
        paint.Color = color;
        canvas.DrawPath(path, paint);
    }

    canvas.Restore();
    canvas.Flush();
}

static void Dispose(List<(SKColor Color, SKPath Path)> paths)
{
    foreach (var (_, path) in paths)
    {
        path.Dispose();
    }
}
