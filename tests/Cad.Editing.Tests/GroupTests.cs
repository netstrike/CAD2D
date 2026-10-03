using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class GroupTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public GroupTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false };
        _layer = _document.GetOrAddLayer("0");
    }

    private Entity Add(Entity entity)
    {
        _document.Edit("test", e => e.Add(entity));
        return entity;
    }

    private CadGroup MakeGroup(string name, params Entity[] entities)
    {
        _editor.Selection.Add(entities);
        _editor.RunCommand("GRUPPO");
        _editor.SubmitText("");
        _editor.SubmitText(name);
        return _document.FindGroup(name)!;
    }

    [Fact]
    public void Clicking_a_member_selects_the_whole_group_and_undo_removes_it()
    {
        var a = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(10, 0)));
        var b = Add(new CircleEntity(_layer, new Vector2(50, 0), 5));
        Add(new CircleEntity(_layer, new Vector2(100, 0), 5));
        var group = MakeGroup("FLANGIA", a, b);
        Assert.Equal(2, _document.Members(group).Count());

        _editor.Click(new Vector2(5, 0), 0.5);
        Assert.Equal(2, _editor.Selection.Count);

        _editor.GroupSelectionEnabled = false;
        _editor.Cancel();
        _editor.Click(new Vector2(5, 0), 0.5);
        Assert.Equal(1, _editor.Selection.Count);

        _editor.Cancel();
        _editor.SubmitText("ANNULLA");
        Assert.Empty(_document.Members(group));
    }

    [Fact]
    public void Moved_members_stay_in_the_group_and_copies_of_a_whole_group_form_a_new_one()
    {
        var a = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(10, 0)));
        var b = Add(new LineEntity(_layer, new Vector2(0, 5), new Vector2(10, 5)));
        var group = MakeGroup("G1", a, b);

        _editor.Click(new Vector2(5, 0), 0.5);
        _editor.RunCommand("SPOSTA");
        _editor.SubmitText("0,0");
        _editor.SubmitText("100,0");
        Assert.Equal(2, _document.Members(group).Count());

        _editor.Click(new Vector2(105, 0), 0.5);
        _editor.RunCommand("COPIA");
        _editor.SubmitText("0,0");
        _editor.SubmitText("0,50");
        _editor.Cancel();
        var copies = _document.ModelSpace.Where(e => e.Group != group).ToList();
        Assert.Equal(2, copies.Count);
        Assert.NotNull(copies[0].Group);
        Assert.Same(copies[0].Group, copies[1].Group);
        Assert.True(copies[0].Group!.IsUnnamed);
    }

    [Fact]
    public void Ungroup_releases_all_members()
    {
        var a = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(10, 0)));
        var b = Add(new LineEntity(_layer, new Vector2(0, 5), new Vector2(10, 5)));
        MakeGroup("G2", a, b);

        _editor.Click(new Vector2(5, 0), 0.5);
        _editor.RunCommand("SEPARA");
        Assert.All(_document.ModelSpace, e => Assert.Null(e.Group));
        Assert.Null(_document.FindGroup("G2"));
    }
}
