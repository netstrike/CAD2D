using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class BlockTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly List<string> _messages = [];

    public BlockTests()
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

    private void DrawBolt()
    {
        Type("C", "10,10", "3");
        Type("L", "5,10", "15,10", "");
        Type("L", "10,5", "10,15", "");
    }

    private void SelectAll()
    {
        _editor.Selection.Clear();
        _editor.Selection.Add(_document.ModelSpace.ToList());
    }

    [Fact]
    public void Block_replaces_objects_with_an_insert_at_the_base_point()
    {
        DrawBolt();
        SelectAll();
        Type("B", "FORO", "10,10", "");

        var insert = Assert.Single(_document.ModelSpace);
        var block = Assert.IsType<InsertEntity>(insert).Block;
        Assert.Equal("FORO", block.Name);
        Assert.Equal(3, block.Entities.Count);
        Assert.Equal(new Vector2(10, 10), ((InsertEntity)insert).Position);
        // Coordinate del blocco relative al punto base.
        Assert.Equal(Vector2.Zero, block.Entities.OfType<CircleEntity>().Single().Center);
        Assert.Equal(new BoundingBox(new Vector2(5, 5), new Vector2(15, 15)), insert.Bounds);
    }

    [Fact]
    public void Block_creation_can_be_undone()
    {
        DrawBolt();
        SelectAll();
        Type("B", "FORO", "10,10", "");
        Type("U");
        Assert.Equal(3, _document.ModelSpace.Count());
    }

    [Fact]
    public void Duplicate_block_name_is_refused()
    {
        DrawBolt();
        SelectAll();
        Type("B", "FORO", "10,10", "");
        Type("B", "foro");
        Assert.Contains(_messages, m => m.Contains("esiste già"));
    }

    [Fact]
    public void Insert_places_scaled_and_rotated_copy()
    {
        DrawBolt();
        SelectAll();
        Type("B", "FORO", "10,10", "");
        Type("I", "", "S", "2", "R", "90", "100,50");

        var inserts = _document.ModelSpace.OfType<InsertEntity>().ToList();
        Assert.Equal(2, inserts.Count);
        var placed = inserts.Single(i => i.Position.IsAlmostEqual(new Vector2(100, 50)));
        // Il punto (5, 0) del blocco, scalato 2 e ruotato di 90°, finisce sopra il punto di inserimento.
        Assert.True(placed.Transform.Transform(new Vector2(5, 0)).IsAlmostEqual(new Vector2(100, 60), 1e-9));
    }

    [Fact]
    public void Exploded_insert_gives_back_world_entities()
    {
        DrawBolt();
        SelectAll();
        Type("B", "FORO", "10,10", "");
        Type("I", "FORO", "50,50");
        var insert = _document.ModelSpace.OfType<InsertEntity>().Single(i => i.Position.IsAlmostEqual(new Vector2(50, 50)));
        _editor.Selection.Clear();
        _editor.Selection.Add([insert]);
        Type("X");
        Assert.Contains(_document.ModelSpace.OfType<CircleEntity>(), c => c.Center.IsAlmostEqual(new Vector2(50, 50)));
    }
}
