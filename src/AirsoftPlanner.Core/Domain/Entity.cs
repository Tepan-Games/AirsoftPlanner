namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Base de toutes les données d'une OP.
/// L'identifiant global (Guid), la date de modification et la suppression logique
/// permettent de fusionner plus tard deux copies d'un même fichier d'OP.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }
}
