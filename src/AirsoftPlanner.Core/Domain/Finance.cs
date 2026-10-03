namespace AirsoftPlanner.Core.Domain;

public enum PaymentMethod
{
    BankTransfer,
    Cash,
    Check,
    Card,
    PayPal,
    HelloAsso,
    Other,
}

/// <summary>Somme reçue : paiement d'une équipe, ou autre recette (sponsor, buvette...) si aucune équipe.</summary>
public class Payment : Entity
{
    public Guid? TeamId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset Date { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>Référence du virement, numéro de chèque...</summary>
    public string Reference { get; set; } = "";

    /// <summary>Libellé (pour une autre recette) ou remarque.</summary>
    public string Label { get; set; } = "";
}

public enum ExpenseCategory
{
    Supplies,
    Provider,
    FieldRental,
    Insurance,
    Pyrotechnics,
    Catering,
    Other,
}

/// <summary>Dépense de l'OP : fournitures, prestataire externe, location du terrain...</summary>
public class Expense : Entity
{
    public string Label { get; set; } = "";

    public ExpenseCategory Category { get; set; }

    /// <summary>Fournisseur ou prestataire.</summary>
    public string Supplier { get; set; } = "";

    public decimal Amount { get; set; }

    public DateTimeOffset Date { get; set; }

    public bool IsPaid { get; set; }

    public string Notes { get; set; } = "";
}
