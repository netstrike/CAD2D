using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public sealed class ImageTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-img").FullName;
    private readonly CadDocument _document = new();
    private readonly Editor _editor;

    public ImageTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false, TrackingEnabled = false };
        _document.GetOrAddLayer("0");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Basta l'intestazione: la misura in pixel si legge senza decodificare l'immagine.</summary>
    internal static string WritePng(string folder, int width, int height)
    {
        var path = Path.Combine(folder, $"pianta{width}x{height}.png");
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    [Fact]
    public void Pixel_size_is_read_from_png_jpeg_bmp_and_gif_headers()
    {
        Assert.Equal((800, 600), ImageEntity.ReadPixelSize(WritePng(_folder, 800, 600)));

        var jpeg = Path.Combine(_folder, "foto.jpg");
        File.WriteAllBytes(jpeg, [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x01, 0x2C, 0x02, 0x58, 3, .. new byte[20]]);
        Assert.Equal((600, 300), ImageEntity.ReadPixelSize(jpeg));

        var bmp = Path.Combine(_folder, "scansione.bmp");
        var header = new byte[54];
        header[0] = (byte)'B'; header[1] = (byte)'M';
        BitConverter.GetBytes(320).CopyTo(header, 18);
        BitConverter.GetBytes(-200).CopyTo(header, 22);
        File.WriteAllBytes(bmp, header);
        Assert.Equal((320, 200), ImageEntity.ReadPixelSize(bmp));

        var gif = Path.Combine(_folder, "schizzo.gif");
        File.WriteAllBytes(gif, [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x40, 0x01, 0xF0, 0x00, .. new byte[20]]);
        Assert.Equal((320, 240), ImageEntity.ReadPixelSize(gif));

        Assert.Null(ImageEntity.ReadPixelSize(Path.Combine(_folder, "manca.png")));
    }

    [Fact]
    public void Image_is_attached_on_its_own_layer_with_proportional_height()
    {
        var path = WritePng(_folder, 800, 400);
        Type("IMMAGINE", path, "Opacità", "30", "10,20", "200");

        var image = Assert.Single(_document.ModelSpace.OfType<ImageEntity>());
        Assert.Equal(ImageCommands.LayerName, image.Layer.Name);
        Assert.Equal(new Vector2(10, 20), image.Corner);
        Assert.Equal(new Vector2(200, 0), image.U);
        Assert.Equal(new Vector2(0, 100), image.V);
        Assert.Equal(0.3, image.Opacity, 9);
        Assert.Equal(new BoundingBox(new Vector2(10, 20), new Vector2(210, 120)), image.Bounds);

        // Ruotata, l'immagine ruota con i suoi lati.
        var rotated = (ImageEntity)image.Transformed(Matrix2D.Rotation(Math.PI / 2, image.Corner));
        Assert.True(rotated.U.IsAlmostEqual(new Vector2(0, 200)));
        Assert.Equal(0.3, rotated.Opacity, 9);
    }

    [Fact]
    public void Calibrate_scales_the_image_so_two_points_are_at_the_real_distance()
    {
        var path = WritePng(_folder, 1000, 500);
        Type("IMMAGINE", path, "0,0", "");
        var image = Assert.Single(_document.ModelSpace.OfType<ImageEntity>());
        Assert.Equal(1000, image.U.Length, 9);

        // Sulla scansione una quota di 5 m misura 250 unità.
        // Due immagini: si sceglie con un clic quella da calibrare.
        ImageCommands.Insert(_editor, path, new Vector2(0, -2000), 100, 1);
        Type("CALIBRA");
        _editor.Click(new Vector2(500, 0), 0.5);
        Type("100,100", "350,100", "5000");

        var calibrated = _document.ModelSpace.OfType<ImageEntity>().First();
        Assert.Equal(20000, calibrated.U.Length, 6);
        Assert.Equal(new Vector2(100 - 100 * 20, 100 - 100 * 20), calibrated.Corner);

        _document.History.Undo();
        Assert.Same(image, _document.ModelSpace.OfType<ImageEntity>().First());

        // Con una sola immagine nel disegno non c'è da selezionarla.
        _document.History.Undo();
        Type("CALIBRA", "0,0", "500,0", "1000");
        Assert.Equal(2000, _document.ModelSpace.OfType<ImageEntity>().Single().U.Length, 6);
    }

    [Fact]
    public void Image_properties_change_width_and_opacity()
    {
        var image = ImageCommands.Insert(_editor, WritePng(_folder, 400, 200), Vector2.Zero, 40, 1)!;
        var rows = PropertySheet.For(_document, [image]);
        Assert.Equal("Immagine", PropertySheet.TypeName(image));
        Assert.True(PropertySheet.Apply(_editor, [image], rows.Single(r => r.Name == "Larghezza").Definition, "80"));
        var wider = _document.ModelSpace.OfType<ImageEntity>().Single();
        Assert.Equal(40, wider.V.Length, 9);
        Assert.True(PropertySheet.Apply(_editor, [wider], rows.Single(r => r.Name == "Opacità %").Definition, "40"));
        Assert.Equal(0.4, _document.ModelSpace.OfType<ImageEntity>().Single().Opacity, 9);
        Assert.False(PropertySheet.Apply(_editor, [wider], rows.Single(r => r.Name == "Opacità %").Definition, "0"));
    }
}
