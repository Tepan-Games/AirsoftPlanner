using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Finance;

public enum PaymentStatus
{
    /// <summary>Rien à payer (gratuit, ou équipe qui ne participe pas).</summary>
    NothingDue,

    Unpaid,

    Partial,

    Paid,

    /// <summary>Trop-perçu : à rembourser.</summary>
    Overpaid,
}

/// <param name="Due">Somme due.</param>
/// <param name="Paid">Somme reçue.</param>
public record TeamBalance(decimal Due, decimal Paid)
{
    public decimal Remaining => Due - Paid;

    public PaymentStatus Status => (Due, Paid) switch
    {
        _ when Paid > Due => PaymentStatus.Overpaid,
        (0, _) => PaymentStatus.NothingDue,
        _ when Paid == 0 => PaymentStatus.Unpaid,
        _ when Paid < Due => PaymentStatus.Partial,
        _ => PaymentStatus.Paid,
    };
}

/// <param name="ExpectedIncome">Recettes définitives attendues : sommes dues par les équipes (après remises) + autres recettes.</param>
/// <param name="ReceivedIncome">Recettes encaissées.</param>
/// <param name="Outstanding">Reste à encaisser auprès des équipes.</param>
/// <param name="Expenses">Total des dépenses.</param>
/// <param name="PaidExpenses">Dépenses déjà réglées.</param>
/// <param name="Refunds">Sommes à rendre (trop-perçus, équipes annulées qui avaient payé, remises après paiement).</param>
public record FinanceSummary(decimal ExpectedIncome, decimal ReceivedIncome, decimal Outstanding, decimal Expenses, decimal PaidExpenses,
    decimal Refunds = 0)
{
    public decimal UnpaidExpenses => Expenses - PaidExpenses;

    /// <summary>Résultat à ce jour : encaissé moins dépenses réglées (trésorerie).</summary>
    public decimal CurrentBalance => ReceivedIncome - PaidExpenses;

    /// <summary>Résultat prévisionnel : recettes attendues moins toutes les dépenses.</summary>
    public decimal ProjectedBalance => ExpectedIncome - Expenses;
}

public static class FinanceCalculator
{
    /// <summary>Somme due par une équipe : montant fixé à la main s'il y en a un, sinon effectif × tarif.</summary>
    public static decimal AmountDue(int players, decimal pricePerPlayer, decimal? overrideAmount) =>
        overrideAmount ?? players * pricePerPlayer;

    public static TeamBalance Balance(decimal due, IEnumerable<Payment> teamPayments) =>
        new(due, teamPayments.Sum(p => p.Amount));

    /// <param name="teamDues">Somme due par chaque équipe qui participe.</param>
    /// <param name="payments">Tous les paiements et autres recettes.</param>
    /// <param name="expenses">Toutes les dépenses.</param>
    public static FinanceSummary Summarize(IReadOnlyDictionary<Guid, decimal> teamDues, IReadOnlyCollection<Payment> payments,
        IReadOnlyCollection<Expense> expenses)
    {
        var otherIncome = payments.Where(p => p.TeamId is null).Sum(p => p.Amount);
        var paidByTeam = payments.Where(p => p.TeamId is not null)
            .GroupBy(p => p.TeamId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var teams = teamDues.Keys.Union(paidByTeam.Keys);

        // Ce que l'orga garde au final : la somme due, qui peut être négative (carburant remboursé supérieur à la
        // participation). Tout ce qui a été versé au-delà, ou dû par l'orga, est à rendre.
        var expectedFromTeams = teamDues.Values.Sum();
        var outstanding = teamDues.Sum(due => Math.Max(0, due.Value - paidByTeam.GetValueOrDefault(due.Key)));
        var refunds = teams.Sum(t => Math.Max(0, paidByTeam.GetValueOrDefault(t) - teamDues.GetValueOrDefault(t)));
        return new FinanceSummary(
            Refunds: refunds,
            ExpectedIncome: expectedFromTeams + otherIncome,
            ReceivedIncome: payments.Sum(p => p.Amount),
            Outstanding: outstanding,
            Expenses: expenses.Sum(e => e.Amount),
            PaidExpenses: expenses.Where(e => e.IsPaid).Sum(e => e.Amount));
    }
}
