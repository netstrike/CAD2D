using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class LayerCommandTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly List<string> _messages = [];

    public LayerCommandTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false };
        _editor.Message += _messages.Add;
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    [Fact]
    public void Creates_layers_sets_color_linetype_and_current()
    {
        Type("LA", "Nuovo", "ASSI, QUOTE", "Colore", "rosso", "ASSI", "Tipolinea", "center", "ASSI", "Corrente", "QUOTE", "");

        var axes = _document.FindLayer("ASSI")!;
        Assert.Equal(new CadColor(255, 0, 0), axes.Color);
        Assert.Equal("CENTER", axes.Linetype.Name);
        Assert.Equal("QUOTE", _editor.CurrentLayer.Name);
        Assert.False(_editor.IsCommandActive);
        Assert.True(_document.IsModified);
    }

    [Fact]
    public void Locked_layer_cannot_be_selected()
    {
        Type("LA", "Nuovo", "FISSO", "Corrente", "FISSO", "");
        Type("L", "0,0", "10,0", "");
        Type("LA", "Blocca", "FISSO", "");
        _editor.Click(new Vector2(5, 0), 0.5);
        Assert.Empty(_editor.Selection.Items);
    }

    [Fact]
    public void Current_layer_cannot_be_frozen_or_deleted()
    {
        Type("LA", "Nuovo", "A", "Corrente", "A", "Congela", "A", "Elimina", "A", "");
        var layer = _document.FindLayer("A")!;
        Assert.False(layer.IsFrozen);
        Assert.Contains(_messages, m => m.Contains("corrente"));
    }

    [Fact]
    public void Rename_and_delete()
    {
        Type("LA", "Nuovo", "VECCHIO, VUOTO", "Rinomina", "VECCHIO", "NUOVO", "Elimina", "VUOTO", "");
        Assert.Null(_document.FindLayer("VECCHIO"));
        Assert.Null(_document.FindLayer("VUOTO"));
        Assert.Equal("NUOVO", _document.FindLayer("NUOVO")!.Name);
    }

    [Fact]
    public void Layer_with_objects_is_not_deleted()
    {
        Type("LA", "Nuovo", "PIENO", "Corrente", "PIENO", "");
        Type("C", "0,0", "5");
        Type("LA", "Corrente", "0", "Elimina", "PIENO", "");
        Assert.NotNull(_document.FindLayer("PIENO"));
    }

    [Theory]
    [InlineData("rosso", 255, 0, 0)]
    [InlineData("Grigio chiaro", 192, 192, 192)]
    [InlineData("5", 0, 0, 255)]
    [InlineData("10, 20, 30", 10, 20, 30)]
    public void Color_parsing(string text, byte r, byte g, byte b) =>
        Assert.Equal(new CadColor(r, g, b), StandardColors.Parse(text));
}
