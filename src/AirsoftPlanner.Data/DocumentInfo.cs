namespace AirsoftPlanner.Data;

/// <summary>Métadonnées du fichier d'OP (une seule ligne).</summary>
public class DocumentInfo
{
    public int Id { get; set; } = 1;

    /// <summary>Version du format de fichier, pour refuser ou migrer les fichiers d'une autre version.</summary>
    public int FormatVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = "";
}
