using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Finance;

namespace AirsoftPlanner.Core.Tests;

public class FinanceTests
{
    private static readonly Guid Alpha = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();
    private static readonly Guid Cancelled = Guid.NewGuid();

    [Fact]
    public void Amount_due_is_players_times_price_unless_overridden()
    {
        Assert.Equal(200m, FinanceCalculator.AmountDue(8, 25m, null));
        Assert.Equal(150m, FinanceCalculator.AmountDue(8, 25m, 150m));
    }

    [Theory]
    [InlineData(0, 0, PaymentStatus.NothingDue)]
    [InlineData(200, 0, PaymentStatus.Unpaid)]
    [InlineData(200, 50, PaymentStatus.Partial)]
    [InlineData(200, 200, PaymentStatus.Paid)]
    [InlineData(200, 250, PaymentStatus.Overpaid)]
    public void Team_payment_status(int due, int paid, PaymentStatus expected)
    {
        var balance = FinanceCalculator.Balance(due, paid == 0 ? [] : [new Payment { Amount = paid }]);

        Assert.Equal(expected, balance.Status);
        Assert.Equal(due - paid, balance.Remaining);
    }

    [Fact]
    public void Discount_after_payment_becomes_a_refund()
    {
        var dues = new Dictionary<Guid, decimal> { [Alpha] = 179.5m }; // 200 - 20 de remise - 0,50 de carburant
        var summary = FinanceCalculator.Summarize(dues, [new Payment { TeamId = Alpha, Amount = 200m }], []);

        Assert.Equal(179.5m, summary.ExpectedIncome);
        Assert.Equal(20.5m, summary.Refunds);
        Assert.Equal(0m, summary.Outstanding);
    }

    [Fact]
    public void Fuel_refund_larger_than_the_participation_is_owed_to_the_team()
    {
        var summary = FinanceCalculator.Summarize(new Dictionary<Guid, decimal> { [Alpha] = -15m }, [], []);

        Assert.Equal(15m, summary.Refunds);
        Assert.Equal(-15m, summary.ExpectedIncome);
        Assert.Equal(0m, summary.Outstanding);
    }

    [Fact]
    public void Summary_combines_team_dues_other_income_and_expenses()
    {
        var dues = new Dictionary<Guid, decimal> { [Alpha] = 200m, [Bravo] = 150m };
        Payment[] payments =
        [
            new() { TeamId = Alpha, Amount = 200m },
            new() { TeamId = Bravo, Amount = 50m },
            new() { TeamId = Cancelled, Amount = 30m }, // équipe annulée qui avait payé (à rembourser ou garder)
            new() { Amount = 100m, Label = "Sponsor" },
        ];
        Expense[] expenses =
        [
            new() { Label = "Location du terrain", Amount = 300m, IsPaid = true },
            new() { Label = "Fumigènes", Amount = 80m },
        ];

        var summary = FinanceCalculator.Summarize(dues, payments, expenses);

        Assert.Equal(200m + 150m + 100m, summary.ExpectedIncome);
        Assert.Equal(380m, summary.ReceivedIncome);
        Assert.Equal(100m, summary.Outstanding);
        Assert.Equal(30m, summary.Refunds); // l'équipe annulée doit être remboursée
        Assert.Equal(380m, summary.Expenses);
        Assert.Equal(80m, summary.UnpaidExpenses);
        Assert.Equal(80m, summary.CurrentBalance);   // 380 encaissés - 300 réglés
        Assert.Equal(70m, summary.ProjectedBalance); // 450 gardés - 380 de dépenses
    }
}
