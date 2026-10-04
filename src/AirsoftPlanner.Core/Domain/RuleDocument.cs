namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Un document de règles joint aux packages des équipes : soit un fichier importé (PDF, Word...)
/// conservé tel quel, soit un texte rédigé dans le logiciel (les lignes « # Titre » ouvrent une section).
/// </summary>
public class RuleDocument : Entity
{
    public string Title { get; set; } = "";

    /// <summary>Fichier importé, ou vide pour un texte rédigé.</summary>
    public byte[] FileContent { get; set; } = [];

    public string FileName { get; set; } = "";

    /// <summary>Texte rédigé dans le logiciel (ignoré pour un fichier importé).</summary>
    public string Text { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>Document ajouté automatiquement (« acp » : règlement ACP), vide pour un document de l'orga.</summary>
    public string Origin { get; set; } = "";

    public bool IsImported => FileContent.Length > 0;
}
