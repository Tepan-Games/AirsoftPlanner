using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Registration;

namespace AirsoftPlanner.App.ViewModels;

public record RegistrationStatusOption(RegistrationStatus Value, string Label, string Color)
{
    public static IReadOnlyList<RegistrationStatusOption> All { get; } =
    [
        new(RegistrationStatus.PreRegistered, "Pré-inscrite", "#1565C0"),
        new(RegistrationStatus.Confirmed, "Confirmée", "#2E7D32"),
        new(RegistrationStatus.WaitingList, "Liste d'attente", "#EF6C00"),
        new(RegistrationStatus.Cancelled, "Annulée", "#9E9E9E"),
    ];

    public static RegistrationStatusOption Of(RegistrationStatus status) => All.First(o => o.Value == status);

    public override string ToString() => Label;
}
