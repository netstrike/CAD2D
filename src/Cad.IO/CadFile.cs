using Acad = ACadSharp;
using ACadSharp.IO;
using Cad.Document;

namespace Cad.IO;

/// <summary>
/// Apertura e salvataggio di disegni DXF e DWG: il formato si sceglie dall'estensione. I due formati passano dallo stesso
/// documento ACadSharp, quindi un DWG si può salvare come DXF e viceversa senza perdere ciò che non è stato modificato.
/// </summary>
public static class CadFile
{
    /// <summary>
    /// Versione dei DWG creati da CAD2D (disegni nuovi o aperti da DXF): AutoCAD 2000, che ogni CAD apre e che LibreDWG
    /// rilegge senza errori. I colori RGB diventano il colore indice più vicino. Un DWG aperto si risalva nella sua versione.
    /// </summary>
    public const Acad.ACadVersion NewDwgVersion = Acad.ACadVersion.AC1015;

    /// <summary>Versione per i DWG 2007, che ACadSharp non sa scrivere: AutoCAD 2018.</summary>
    public const Acad.ACadVersion DefaultDwgVersion = Acad.ACadVersion.AC1032;

    public static bool IsDwg(string path) => string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase);

    public static ImportResult Load(string path)
    {
        if (!IsDwg(path))
        {
            return DxfImporter.Load(path);
        }

        var errors = new List<string>();
        Acad.CadDocument source;
        using (var stream = File.OpenRead(path))
        {
            source = DwgReader.Read(stream, (_, e) =>
            {
                if (e.NotificationType is NotificationType.Error)
                {
                    errors.Add(e.Message);
                }
            });
        }

        var document = DxfImporter.Convert(source);
        ((DxfSource)document.Source!).DwgVersion = source.Header.Version;
        document.FilePath = path;
        DxfImporter.ResolveImagePaths(document, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        return new ImportResult(document, errors);
    }

    /// <summary>Salva nel formato indicato dall'estensione e segna il documento come salvato.</summary>
    public static void Save(CadDocument document, string path)
    {
        using (var stream = File.Create(path))
        {
            Save(document, stream, IsDwg(path));
        }

        document.FilePath = path;
        document.MarkSaved();
    }

    public static void Save(CadDocument document, Stream stream, bool dwg)
    {
        var target = DxfExporter.Prepare(document);
        if (!dwg)
        {
            using var writer = new DxfWriter(stream, target, binary: false);
            writer.Write();
            return;
        }

        target.Header.Version = ((DxfSource)document.Source!).DwgVersion is { } read ? WritableDwgVersion(read) : NewDwgVersion;
        if (target.Header.Version <= Acad.ACadVersion.AC1015)
        {
            DxfExporter.IndexTrueColors(target);
        }

        DwgWriter.Write(stream, target, new DwgWriterConfiguration { CloseStream = false });
    }

    /// <summary>ACadSharp scrive i DWG R14, 2000, 2004 e dal 2010 in poi: le altre versioni passano alla più vicina.</summary>
    public static Acad.ACadVersion WritableDwgVersion(Acad.ACadVersion version) => version switch
    {
        Acad.ACadVersion.AC1014 or Acad.ACadVersion.AC1015 or Acad.ACadVersion.AC1018 or Acad.ACadVersion.AC1024 or Acad.ACadVersion.AC1027 or Acad.ACadVersion.AC1032 => version,
        < Acad.ACadVersion.AC1014 => Acad.ACadVersion.AC1015,
        _ => DefaultDwgVersion,
    };
}
