namespace Cad.Document;

/// <summary>
/// Gruppo di oggetti con nome: un clic su un membro seleziona tutto il gruppo (se <see cref="Selectable"/>).
/// L'appartenenza sta su ogni entità (<see cref="Entity.Group"/>), così segue modifiche, annulla e ripeti.
/// </summary>
public sealed class CadGroup(string name)
{
    public string Name { get; internal set; } = name;
    public string Description { get; set; } = string.Empty;
    public bool Selectable { get; set; } = true;

    /// <summary>Gruppo senza nome scelto dall'utente (nome "*A..."), come quelli creati copiando un gruppo.</summary>
    public bool IsUnnamed => Name.StartsWith('*');

    public override string ToString() => Name;
}
