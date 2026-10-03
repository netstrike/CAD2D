using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Immagine raster collegata a un file esterno (PNG, JPG, BMP, GIF), disegnata dietro al resto per ricalcarla.
/// <see cref="Corner"/> è l'angolo in basso a sinistra, <see cref="U"/> il lato inferiore e <see cref="V"/> quello sinistro
/// (vettori interi, non per pixel): così l'immagine si sposta, ruota, scala e specchia come le altre entità.
/// </summary>
public sealed class ImageEntity(Layer layer, string path, Vector2 corner, Vector2 u, Vector2 v, int pixelWidth, int pixelHeight) : Entity(layer)
{
    /// <summary>Percorso del file; all'apertura di un DXF si risolve rispetto alla cartella del disegno.</summary>
    public string Path { get; set; } = path;
    public Vector2 Corner { get; } = corner;
    public Vector2 U { get; } = u;
    public Vector2 V { get; } = v;
    public int PixelWidth { get; } = pixelWidth;
    public int PixelHeight { get; } = pixelHeight;

    /// <summary>Opacità da 0,1 (quasi trasparente) a 1; più bassa si ricalca meglio.</summary>
    public double Opacity { get; set; } = 1;

    public IReadOnlyList<Vector2> Corners => [Corner, Corner + U, Corner + U + V, Corner + V];

    public override BoundingBox Bounds => BoundingBox.FromPoints(Corners);

    public override IReadOnlyList<Vector2> Grips => Corners;

    protected override Entity TransformCore(Matrix2D m) =>
        new ImageEntity(Layer, Path, m.Transform(Corner), m.TransformVector(U), m.TransformVector(V), PixelWidth, PixelHeight) { Opacity = Opacity };

    /// <summary>Larghezza e altezza in pixel lette dall'intestazione del file, senza decodificare l'immagine; null se il formato non è riconosciuto.</summary>
    public static (int Width, int Height)? ReadPixelSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[30];
            if (stream.Read(header, 0, header.Length) < 26)
            {
                return null;
            }

            // PNG: blocco IHDR subito dopo la firma, larghezza e altezza big-endian.
            if (header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
            {
                return Valid(BigEndian(header, 16), BigEndian(header, 20));
            }

            // GIF: larghezza e altezza little-endian a 16 bit.
            if (header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
            {
                return Valid(header[6] | header[7] << 8, header[8] | header[9] << 8);
            }

            // BMP: intestazione BITMAPINFOHEADER, l'altezza è negativa per le immagini dall'alto in basso.
            if (header[0] == (byte)'B' && header[1] == (byte)'M')
            {
                return Valid(BitConverter.ToInt32(header, 18), Math.Abs(BitConverter.ToInt32(header, 22)));
            }

            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                return ReadJpegSize(stream);
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>JPEG: si scorrono i segmenti fino al primo SOF, che contiene altezza e larghezza.</summary>
    private static (int, int)? ReadJpegSize(Stream stream)
    {
        stream.Position = 2;
        var buffer = new byte[7];
        while (stream.Read(buffer, 0, 4) == 4)
        {
            if (buffer[0] != 0xFF)
            {
                return null;
            }

            var marker = buffer[1];
            var length = buffer[2] << 8 | buffer[3];
            var isFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrame)
            {
                if (stream.Read(buffer, 0, 5) != 5)
                {
                    return null;
                }

                return Valid(buffer[3] << 8 | buffer[4], buffer[1] << 8 | buffer[2]);
            }

            stream.Position += length - 2;
        }

        return null;
    }

    private static int BigEndian(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];

    private static (int, int)? Valid(int width, int height) => width > 0 && height > 0 ? (width, height) : null;
}
