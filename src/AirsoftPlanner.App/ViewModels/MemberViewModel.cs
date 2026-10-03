using System;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.App.ViewModels;

public class MemberViewModel(TeamMember member, Action<MemberViewModel> onLeaderChanged) : ViewModelBase
{
    public TeamMember Model => member;

    public string FirstName
    {
        get => member.FirstName;
        set => SetProperty(member.FirstName, value, member, (m, v) => m.FirstName = v);
    }

    public string LastName
    {
        get => member.LastName;
        set => SetProperty(member.LastName, value, member, (m, v) => m.LastName = v);
    }

    public string Callsign
    {
        get => member.Callsign;
        set => SetProperty(member.Callsign, value, member, (m, v) => m.Callsign = v);
    }

    public string Role
    {
        get => member.Role;
        set => SetProperty(member.Role, value, member, (m, v) => m.Role = v);
    }

    public string Phone
    {
        get => member.Phone;
        set => SetProperty(member.Phone, value, member, (m, v) => m.Phone = v);
    }

    public string Email
    {
        get => member.Email;
        set => SetProperty(member.Email, value, member, (m, v) => m.Email = v);
    }

    /// <summary>Un seul chef par équipe : en désigner un retire le titre aux autres.</summary>
    public bool IsLeader
    {
        get => member.IsLeader;
        set
        {
            if (SetProperty(member.IsLeader, value, member, (m, v) => m.IsLeader = v) && value)
                onLeaderChanged(this);
        }
    }

    public string DisplayName
    {
        get
        {
            var name = $"{FirstName} {LastName}".Trim();
            return Callsign.Length == 0 ? name : name.Length == 0 ? Callsign : $"{name} « {Callsign} »";
        }
    }
}

public class VehicleViewModel(TeamVehicle vehicle) : ViewModelBase
{
    public TeamVehicle Model => vehicle;

    public string Kind
    {
        get => vehicle.Kind;
        set => SetProperty(vehicle.Kind, value, vehicle, (v, x) => v.Kind = x);
    }

    public decimal? Quantity
    {
        get => vehicle.Quantity;
        set => SetProperty(vehicle.Quantity, Math.Max(1, (int)(value ?? 1)), vehicle, (v, x) => v.Quantity = x);
    }

    public string Notes
    {
        get => vehicle.Notes;
        set => SetProperty(vehicle.Notes, value, vehicle, (v, x) => v.Notes = x);
    }
}
