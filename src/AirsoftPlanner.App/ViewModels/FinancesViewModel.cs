using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Finance;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Montants en euros, à la française (« 1 234,50 € »), et saisie avec virgule ou point.</summary>
public static class Money
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    public static string Format(decimal amount) => amount.ToString("#,##0.00 €", French);

    public static bool TryParse(string? text, out decimal amount) =>
        decimal.TryParse((text ?? "").Replace("€", "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    public static decimal Parse(string? text) =>
        TryParse(text, out var amount) ? amount : throw new FormatException(L.T("montant_non_reconnu_ex_25_ou_12_50"));
}

public record PaymentMethodOption(PaymentMethod Value, string Label)
{
    public static IReadOnlyList<PaymentMethodOption> All { get; } =
    [
        new(PaymentMethod.BankTransfer, L.T("virement")),
        new(PaymentMethod.Cash, L.T("especes")),
        new(PaymentMethod.Check, L.T("cheque")),
        new(PaymentMethod.Card, L.T("carte_bancaire")),
        new(PaymentMethod.PayPal, "PayPal"),
        new(PaymentMethod.HelloAsso, "HelloAsso"),
        new(PaymentMethod.Other, L.T("autre")),
    ];

    public static PaymentMethodOption Of(PaymentMethod method) => All.First(o => o.Value == method);

    public override string ToString() => Label;
}

public record ExpenseCategoryOption(ExpenseCategory Value, string Label)
{
    public static IReadOnlyList<ExpenseCategoryOption> All { get; } =
    [
        new(ExpenseCategory.Supplies, L.T("fournitures")),
        new(ExpenseCategory.Provider, L.T("prestataire")),
        new(ExpenseCategory.FieldRental, L.T("location_du_terrain")),
        new(ExpenseCategory.Insurance, L.T("assurance")),
        new(ExpenseCategory.Pyrotechnics, L.T("pyrotechnie")),
        new(ExpenseCategory.Catering, L.T("restauration")),
        new(ExpenseCategory.Other, L.T("autre")),
    ];

    public static ExpenseCategoryOption Of(ExpenseCategory category) => All.First(o => o.Value == category);

    public override string ToString() => Label;
}

public record AdjustmentKindOption(AdjustmentKind Value, string Label)
{
    public static IReadOnlyList<AdjustmentKindOption> All { get; } =
    [
        new(AdjustmentKind.Discount, L.T("remise")),
        new(AdjustmentKind.Gift, L.T("cadeau")),
    ];

    public static AdjustmentKindOption Of(AdjustmentKind kind) => All.First(o => o.Value == kind);

    public override string ToString() => Label;
}

/// <summary>Ligne de remise, cadeau ou carburant d'une équipe.</summary>
public record CreditRow(TeamAdjustment? Model, string Text, string AmountText);

/// <summary>Ligne affichée d'un paiement ou d'une autre recette.</summary>
public record PaymentRow(Payment Model, string DateText, string AmountText, string MethodText, string Details);

/// <summary>Situation financière d'une équipe.</summary>
public partial class TeamFinanceRow(TeamViewModel team, FinancesViewModel owner) : ViewModelBase
{
    public TeamViewModel Team => team;

    public TeamBalance Balance { get; private set; } = new(0, 0);

    /// <summary>« 300,00 € − remises 20,00 € − carburant 12,35 € (61,7 km) ».</summary>
    public string BreakdownText { get; private set; } = "";

    public IReadOnlyList<CreditRow> Credits { get; private set; } = [];

    public string DueText => Money.Format(Balance.Due);

    public string PaidText => Money.Format(Balance.Paid);

    public string RemainingText => Balance.Remaining > 0 ? $"reste {Money.Format(Balance.Remaining)}"
        : Balance.Remaining < 0 ? L.F("trop_percu_x", Money.Format(-Balance.Remaining)) : "";

    public string StatusLabel => Balance.Status switch
    {
        PaymentStatus.Paid => L.T("paye"),
        PaymentStatus.Partial => L.T("paiement_partiel"),
        PaymentStatus.Unpaid => L.T("non_paye"),
        PaymentStatus.Overpaid => L.T("trop_percu"),
        _ => team.IsPlaying ? L.T("gratuit") : $"{team.Status.Label}",
    };

    public string StatusColor => Balance.Status switch
    {
        PaymentStatus.Paid => "#2E7D32",
        PaymentStatus.Partial => "#EF6C00",
        PaymentStatus.Unpaid => "#C62828",
        PaymentStatus.Overpaid => "#6A1B9A",
        _ => "#757575",
    };

    /// <summary>Somme due fixée à la main (vide = effectif × tarif).</summary>
    public string OverrideText
    {
        get => team.Model.AmountDueOverride is { } amount ? amount.ToString("0.##", AirsoftPlanner.Core.Localization.L.Culture) : "";
        set
        {
            team.Model.AmountDueOverride = string.IsNullOrWhiteSpace(value) ? null : Money.Parse(value);
            owner.Refresh();
        }
    }

    public void Update(TeamBalance balance, string breakdown, IReadOnlyList<CreditRow> credits)
    {
        Balance = balance;
        BreakdownText = breakdown;
        Credits = credits;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Une dépense, modifiable dans le tableau.</summary>
public class ExpenseViewModel(Expense expense, Action onChanged) : ViewModelBase
{
    public Expense Model => expense;

    public string Label
    {
        get => expense.Label;
        set => SetProperty(expense.Label, value, expense, (e, v) => e.Label = v);
    }

    public ExpenseCategoryOption Category
    {
        get => ExpenseCategoryOption.Of(expense.Category);
        set
        {
            if (value is not null && SetProperty(expense.Category, value.Value, expense, (e, v) => e.Category = v))
                onChanged();
        }
    }

    public string Supplier
    {
        get => expense.Supplier;
        set => SetProperty(expense.Supplier, value, expense, (e, v) => e.Supplier = v);
    }

    public string AmountText
    {
        get => expense.Amount.ToString("0.00", AirsoftPlanner.Core.Localization.L.Culture);
        set
        {
            if (SetProperty(expense.Amount, Money.Parse(value), expense, (e, v) => e.Amount = v))
                onChanged();
        }
    }

    public DateTime? Date
    {
        get => expense.Date.LocalDateTime.Date;
        set
        {
            if (value is { } date)
                SetProperty(expense.Date, new DateTimeOffset(date.Date), expense, (e, v) => e.Date = v);
        }
    }

    public bool IsPaid
    {
        get => expense.IsPaid;
        set
        {
            if (SetProperty(expense.IsPaid, value, expense, (e, v) => e.IsPaid = v))
                onChanged();
        }
    }

    public string Notes
    {
        get => expense.Notes;
        set => SetProperty(expense.Notes, value, expense, (e, v) => e.Notes = v);
    }
}

/// <summary>Finances de l'OP : participation des équipes, autres recettes, dépenses et bilan.</summary>
public partial class FinancesViewModel : ViewModelBase
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    private readonly OperationFile _file;
    private readonly TeamsViewModel _teams;
    private readonly IFileDialogService _dialogs;
    private readonly List<Payment> _payments;
    private readonly List<TeamAdjustment> _adjustments;
    private readonly VehicleTracker _vehicles;

    public FinancesViewModel(OperationFile file, TeamsViewModel teams, IFileDialogService dialogs, VehicleTracker vehicles)
    {
        _file = file;
        _teams = teams;
        _dialogs = dialogs;
        _vehicles = vehicles;
        _payments = file.LoadPayments().ToList();
        _adjustments = file.LoadAdjustments().ToList();
        vehicles.Changed += Refresh;
        Expenses = new ObservableCollection<ExpenseViewModel>(file.LoadExpenses().Select(e => new ExpenseViewModel(e, Refresh)));
        teams.Items.CollectionChanged += (_, e) =>
        {
            foreach (var team in e.NewItems?.OfType<TeamViewModel>() ?? [])
                team.PropertyChanged += OnTeamChanged;
            Refresh();
        };
        foreach (var team in teams.Items)
            team.PropertyChanged += OnTeamChanged;
        Refresh();
    }

    public ObservableCollection<TeamFinanceRow> TeamRows { get; } = [];

    public ObservableCollection<ExpenseViewModel> Expenses { get; }

    public IReadOnlyList<PaymentMethodOption> PaymentMethods => PaymentMethodOption.All;

    public IReadOnlyList<ExpenseCategoryOption> ExpenseCategories => ExpenseCategoryOption.All;

    /// <summary>Participation demandée par joueur.</summary>
    public string PricePerPlayerText
    {
        get => _file.Operation.PricePerPlayer.ToString("0.##", French);
        set
        {
            _file.Operation.PricePerPlayer = Money.Parse(value);
            OnPropertyChanged();
            Refresh();
        }
    }

    /// <summary>Carburant remboursé par kilomètre aux véhicules mis en jeu.</summary>
    public string FuelRatePerKmText
    {
        get => _file.Operation.FuelRatePerKm.ToString("0.###", French);
        set
        {
            _file.Operation.FuelRatePerKm = Money.Parse(value);
            OnPropertyChanged();
            Refresh();
        }
    }

    public IReadOnlyList<AdjustmentKindOption> AdjustmentKinds => AdjustmentKindOption.All;

    [ObservableProperty]
    private AdjustmentKindOption _newAdjustmentKind = AdjustmentKindOption.Of(AdjustmentKind.Discount);

    [ObservableProperty]
    private string _newAdjustmentLabel = "";

    [ObservableProperty]
    private string _newAdjustmentAmount = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAdjustmentCommand))]
    private CreditRow? _selectedCredit;

    [RelayCommand(CanExecute = nameof(HasSelectedTeam))]
    private async Task AddAdjustmentAsync()
    {
        var amount = 0m;
        if (NewAdjustmentAmount.Trim().Length > 0 && (!Money.TryParse(NewAdjustmentAmount, out amount) || amount < 0))
        {
            await _dialogs.ShowErrorAsync(L.T("montant_non_reconnu_ex_20_ou_12_50_vide_pour_un"));
            return;
        }

        var adjustment = new TeamAdjustment
        {
            TeamId = SelectedTeam!.Team.Model.Id,
            Kind = NewAdjustmentKind.Value,
            Label = NewAdjustmentLabel.Trim().Length > 0 ? NewAdjustmentLabel.Trim() : NewAdjustmentKind.Label,
            Amount = amount,
        };
        _file.Add(adjustment);
        _adjustments.Add(adjustment);
        NewAdjustmentLabel = "";
        NewAdjustmentAmount = "";
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveAdjustment))]
    private void RemoveAdjustment()
    {
        _file.Remove(SelectedCredit!.Model!);
        _adjustments.Remove(SelectedCredit.Model!);
        Refresh();
    }

    private bool CanRemoveAdjustment => SelectedCredit?.Model is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPaymentCommand), nameof(AddAdjustmentCommand))]
    private TeamFinanceRow? _selectedTeam;

    [ObservableProperty]
    private IReadOnlyList<PaymentRow> _teamPayments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePaymentCommand))]
    private PaymentRow? _selectedPayment;

    [ObservableProperty]
    private string _newAmount = "";

    [ObservableProperty]
    private PaymentMethodOption _newMethod = PaymentMethodOption.Of(PaymentMethod.BankTransfer);

    [ObservableProperty]
    private string _newReference = "";

    [ObservableProperty]
    private IReadOnlyList<PaymentRow> _otherIncome = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveIncomeCommand))]
    private PaymentRow? _selectedIncome;

    [ObservableProperty]
    private string _newIncomeLabel = "";

    [ObservableProperty]
    private string _newIncomeAmount = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveExpenseCommand))]
    private ExpenseViewModel? _selectedExpense;

    [ObservableProperty]
    private FinanceSummary _summary = new(0, 0, 0, 0, 0);

    public string ExpectedIncomeText => Money.Format(Summary.ExpectedIncome);

    public string ReceivedIncomeText => Money.Format(Summary.ReceivedIncome);

    public string OutstandingText => Money.Format(Summary.Outstanding);

    public string RefundsText => Money.Format(Summary.Refunds);

    public bool HasRefunds => Summary.Refunds > 0;

    public string ExpensesText => Money.Format(Summary.Expenses);

    public string UnpaidExpensesText => Money.Format(Summary.UnpaidExpenses);

    public string CurrentBalanceText => Money.Format(Summary.CurrentBalance);

    public string ProjectedBalanceText => Money.Format(Summary.ProjectedBalance);

    public bool IsProjectedNegative => Summary.ProjectedBalance < 0;

    partial void OnSummaryChanged(FinanceSummary value) => OnPropertyChanged(string.Empty);

    partial void OnSelectedTeamChanged(TeamFinanceRow? value)
    {
        NewAmount = value is { Balance.Remaining: > 0 } row ? row.Balance.Remaining.ToString("0.##", French) : "";
        RefreshTeamPayments();
    }

    // ----- Paiements des équipes -----

    [RelayCommand(CanExecute = nameof(HasSelectedTeam))]
    private async Task AddPaymentAsync()
    {
        if (!Money.TryParse(NewAmount, out var amount) || amount <= 0)
        {
            await _dialogs.ShowErrorAsync(L.T("montant_non_reconnu_ex_25_ou_12_50"));
            return;
        }

        AddPayment(new Payment
        {
            TeamId = SelectedTeam!.Team.Model.Id,
            Amount = amount,
            Method = NewMethod.Value,
            Reference = NewReference.Trim(),
            Date = DateTimeOffset.Now,
        });
        NewReference = "";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPayment))]
    private void RemovePayment() => RemovePaymentModel(SelectedPayment!.Model);

    // ----- Autres recettes -----

    [RelayCommand]
    private async Task AddIncomeAsync()
    {
        if (!Money.TryParse(NewIncomeAmount, out var amount) || amount <= 0)
        {
            await _dialogs.ShowErrorAsync(L.T("montant_non_reconnu_ex_150_ou_12_50"));
            return;
        }

        AddPayment(new Payment { Amount = amount, Label = NewIncomeLabel.Trim(), Method = NewMethod.Value, Date = DateTimeOffset.Now });
        NewIncomeLabel = "";
        NewIncomeAmount = "";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedIncome))]
    private void RemoveIncome() => RemovePaymentModel(SelectedIncome!.Model);

    // ----- Dépenses -----

    [RelayCommand]
    private void AddExpense()
    {
        var expense = new Expense { Label = L.T("nouvelle_depense"), Category = ExpenseCategory.Supplies, Date = DateTimeOffset.Now };
        _file.Add(expense);
        var viewModel = new ExpenseViewModel(expense, Refresh);
        Expenses.Add(viewModel);
        SelectedExpense = viewModel;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedExpense))]
    private void RemoveExpense()
    {
        _file.Remove(SelectedExpense!.Model);
        Expenses.Remove(SelectedExpense);
        SelectedExpense = null;
        Refresh();
    }

    /// <summary>Export pour la comptabilité : une ligne par recette et par dépense (CSV pour Excel).</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var path = await _dialogs.PickSaveFileAsync(L.T("exporter_les_finances"), "finances.csv", L.T("fichier_csv"), ".csv");
        if (path is null)
            return;

        static string Quote(string value) => value.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        var teams = _teams.Items.ToDictionary(t => t.Model.Id, t => t.Name);
        var text = new StringBuilder(L.T("type_date_libelle_categorie_moyen_tiers_montant"));
        foreach (var p in _payments.OrderBy(p => p.Date))
            text.AppendLine(string.Join(";", L.T("recette"), p.Date.LocalDateTime.ToString("dd/MM/yyyy"),
                Quote(p.TeamId is { } id ? L.F("participation_x", teams.GetValueOrDefault(id, L.T("equipe_supprimee_2"))) : p.Label),
                PaymentMethodOption.Of(p.Method).Label, Quote(p.Reference), p.Amount.ToString("0.00", French), "oui"));
        foreach (var e in Expenses.Select(x => x.Model).OrderBy(e => e.Date))
            text.AppendLine(string.Join(";", L.T("depense_2"), e.Date.LocalDateTime.ToString("dd/MM/yyyy"), Quote(e.Label),
                ExpenseCategoryOption.Of(e.Category).Label, Quote(e.Supplier), (-e.Amount).ToString("0.00", French), e.IsPaid ? "oui" : "non"));
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Recalcule les sommes dues, les statuts et le bilan.</summary>
    public void Refresh()
    {
        var price = _file.Operation.PricePerPlayer;
        var involved = _teams.Items
            .Where(t => t.IsPlaying || _payments.Any(p => p.TeamId == t.Model.Id))
            .ToList();
        var selected = SelectedTeam?.Team;
        if (!TeamRows.Select(r => r.Team).SequenceEqual(involved))
        {
            TeamRows.Clear();
            foreach (var team in involved)
                TeamRows.Add(new TeamFinanceRow(team, this));
        }

        var dues = new Dictionary<Guid, decimal>();
        var fuelRate = _file.Operation.FuelRatePerKm;
        foreach (var row in TeamRows)
        {
            var gross = row.Team.IsPlaying ? FinanceCalculator.AmountDue(row.Team.Size, price, row.Team.Model.AmountDueOverride) : 0;
            var credits = new List<CreditRow>();
            foreach (var adjustment in _adjustments.Where(a => a.TeamId == row.Team.Model.Id))
                credits.Add(new CreditRow(adjustment, $"{AdjustmentKindOption.Of(adjustment.Kind).Label} : {adjustment.Label}",
                    adjustment.Amount > 0 ? $"− {Money.Format(adjustment.Amount)}" : "offert"));
            var fuel = 0m;
            foreach (var vehicle in row.Team.Vehicles.Where(v => v.InGame))
            {
                var km = _vehicles.Kilometers(vehicle.Model);
                var refund = VehicleMileage.FuelRefund(km, fuelRate);
                fuel += refund;
                credits.Add(new CreditRow(null, L.F("carburant_x_x", vehicle.Kind, vehicle.KilometersText), $"− {Money.Format(refund)}"));
            }

            var discounts = _adjustments.Where(a => a.TeamId == row.Team.Model.Id).Sum(a => a.Amount);
            // Remises et carburant viennent en déduction ; au-delà de la somme due, c'est l'orga qui doit à l'équipe.
            var due = gross - discounts - fuel;
            if (row.Team.IsPlaying)
                dues[row.Team.Model.Id] = due;
            var breakdown = discounts + fuel == 0 ? ""
                : $"{Money.Format(gross)}{(discounts > 0 ? L.F("remises_x", Money.Format(discounts)) : "")}{(fuel > 0 ? L.F("carburant_x", Money.Format(fuel)) : "")}";
            row.Update(FinanceCalculator.Balance(due, _payments.Where(p => p.TeamId == row.Team.Model.Id)), breakdown, credits);
        }

        SelectedTeam = TeamRows.FirstOrDefault(r => r.Team == selected) ?? SelectedTeam;
        Summary = FinanceCalculator.Summarize(dues, _payments, Expenses.Select(e => e.Model).ToList());
        OtherIncome = _payments.Where(p => p.TeamId is null).OrderBy(p => p.Date).Select(ToRow).ToList();
        RefreshTeamPayments();
    }

    private bool HasSelectedTeam => SelectedTeam is not null;

    private bool HasSelectedPayment => SelectedPayment is not null;

    private bool HasSelectedIncome => SelectedIncome is not null;

    private bool HasSelectedExpense => SelectedExpense is not null;

    private void AddPayment(Payment payment)
    {
        _file.Add(payment);
        _payments.Add(payment);
        Refresh();
    }

    private void RemovePaymentModel(Payment payment)
    {
        _file.Remove(payment);
        _payments.Remove(payment);
        Refresh();
    }

    private void RefreshTeamPayments() =>
        TeamPayments = SelectedTeam is null ? [] : _payments.Where(p => p.TeamId == SelectedTeam.Team.Model.Id).OrderBy(p => p.Date).Select(ToRow).ToList();

    private static PaymentRow ToRow(Payment p) => new(p, p.Date.LocalDateTime.ToString("dd/MM/yyyy", French), Money.Format(p.Amount),
        PaymentMethodOption.Of(p.Method).Label, p.TeamId is null ? p.Label : p.Reference);

    private void OnTeamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TeamViewModel.Size) or nameof(TeamViewModel.Status) or nameof(TeamViewModel.Name)
            or nameof(TeamViewModel.VehicleSummary))
            Refresh();
    }
}
