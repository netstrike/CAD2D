using System.Diagnostics;
using Cad.IO;
using Cad.Rendering;

// Apre tutti i DXF e DWG di una cartella come farebbe l'applicazione e riassume l'esito di ciascuno.
// Uso: dotnet run --project tools/Cad.DxfCheck -- <cartella>
if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/Cad.DxfCheck -- <cartella con file .dxf o .dwg>");
    return 2;
}

var files = Directory.EnumerateFiles(args[0], "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
    .Order()
    .ToList();
var failures = 0;
foreach (var file in files)
{
    var name = Path.GetRelativePath(args[0], file);
    var watch = Stopwatch.StartNew();
    try
    {
        var result = CadFile.Load(file);
        var scene = SceneBuilder.Build(result.Document);
        var unsupported = string.Join(", ", result.Document.UnsupportedEntities.Select(u => $"{u.Key} {u.Value}"));
        Console.WriteLine($"OK    {name}: {scene.EntityCount} entità, {result.Document.Layers.Count()} layer, {watch.ElapsedMilliseconds} ms" +
                          (unsupported.Length > 0 ? $", non gestite: {unsupported}" : string.Empty) +
                          (result.Errors.Count > 0 ? $", {result.Errors.Count} errori di lettura" : string.Empty));
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"ERRORE {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine($"{files.Count - failures} di {files.Count} file aperti.");
return failures == 0 ? 0 : 1;
