using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Registration;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public record RegistrationStatusOption(RegistrationStatus Value, string Label, string Color)
{
    public static IReadOnlyList<RegistrationStatusOption> All { get; } =
    [
        new(RegistrationStatus.PreRegistered, L.T("pre_inscrite"), "#1565C0"),
        new(RegistrationStatus.Confirmed, L.T("confirmee"), "#2E7D32"),
        new(RegistrationStatus.WaitingList, L.T("liste_d_attente_2"), "#EF6C00"),
        new(RegistrationStatus.Cancelled, L.T("annulee"), "#9E9E9E"),
    ];

    public static RegistrationStatusOption Of(RegistrationStatus status) => All.First(o => o.Value == status);

    public override string ToString() => Label;
}
