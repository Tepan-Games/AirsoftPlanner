using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.Services.Gps;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public record AllyShareModeOption(AllyShareMode Value, string Label)
{
    public static IReadOnlyList<AllyShareModeOption> All { get; } =
    [
        new(AllyShareMode.None, L.T("rien_sa_propre_position_seulement")),
        new(AllyShareMode.Coordinates, L.T("allies_en_coordonnees_version_difficile")),
        new(AllyShareMode.Map, L.T("allies_sur_la_carte")),
    ];

    public static AllyShareModeOption Of(AllyShareMode mode) => All.First(o => o.Value == mode);

    public override string ToString() => Label;
}

/// <summary>Téléphone enrôlé, tel qu'affiché.</summary>
public record EnrolledDeviceRow(EnrolledDevice Model, string Team, string Device, string LastSeen, bool IsRevoked);

/// <summary>Appareil qui envoie des positions sans être associé à une équipe.</summary>
public record UnknownDevice(string DeviceId, string Source, string LastSeen);

/// <summary>
/// Réception automatique des positions : smartphones sur le réseau local, Meshtastic (MQTT), serveur Traccar,
/// fichiers GPX/CSV. Chaque position est attribuée à l'équipe dont l'un des identifiants GPS correspond.
/// </summary>
public partial class GpsViewModel : ViewModelBase, IAsyncDisposable
{
    private TrackingViewModel _tracking;
    private TeamsViewModel _teams;
    private readonly IFileDialogService _dialogs;
    private readonly LocalGpsServer _server = new();
    private readonly MeshtasticMqttSource _meshtastic = new();
    private readonly TraccarServerSource _traccar = new();
    private readonly OperationServerSource _upstream = new();

    private VehicleTracker _vehicles;

    private Data.OperationFile _file;
    private readonly List<EnrolledDevice> _devices;

    public GpsViewModel(Data.OperationFile file, TrackingViewModel tracking, TeamsViewModel teams, IFileDialogService dialogs, VehicleTracker vehicles)
    {
        _file = file;
        _devices = file.LoadEnrolledDevices().ToList();
        _intervalSeconds = file.Operation.TrackingIntervalSeconds;
        _vehicles = vehicles;
        _tracking = tracking;
        _teams = teams;
        _dialogs = dialogs;
        var settings = AppSettings.Current;
        _serverPort = settings.GpsServerPort;
        _mqttHost = settings.MqttHost;
        _mqttPort = settings.MqttPort;
        _mqttTopic = settings.MqttTopic;
        _mqttUser = settings.MqttUser;
        _mqttPassword = Secret.Unprotect(settings.MqttPasswordProtected);
        _traccarUrl = settings.TraccarUrl;
        _traccarUser = settings.TraccarUser;
        _traccarPassword = Secret.Unprotect(settings.TraccarPasswordProtected);

        _dynDnsUpdateUrl = Secret.Unprotect(settings.DynDnsUpdateUrlProtected);
        _serverAddress = file.Operation.ServerAddress;
        _server.FixReceived += OnFix;
        _server.OperationInfo = () => (_file.Operation.Name, _file.Operation.Id);
        _server.Positions = () => _tracking.PublishedPositions;
        _server.TeamNames = () => _tracking.PublishedTeamNames;
        // Appelés depuis le fil du serveur : le travail se fait sur le fil de l'interface (données de l'OP).
        _server.Enroll = request => Dispatcher.UIThread.InvokeAsync(() => EnrollDevice(request)).GetAwaiter().GetResult();
        _server.Authorize = token => Dispatcher.UIThread.InvokeAsync(() => AuthorizeDevice(token)).GetAwaiter().GetResult();
        _server.MessagePhoto = (token, id) => Dispatcher.UIThread.InvokeAsync(() => MessagePhotoFor(token, id)).GetAwaiter().GetResult();
        _server.MapImage = token => Dispatcher.UIThread.InvokeAsync(() => MapImageFor(token)).GetAwaiter().GetResult();
        _shareMode = AllyShareModeOption.Of(file.Operation.AllyShareMode);
        RefreshDevices();
        _upstream.FixReceived += OnFix;
        _upstream.Error += message => Dispatcher.UIThread.Post(() => Log(L.F("pc_de_l_op_x_2", message)));
        _upstreamUrl = settings.UpstreamUrl;
        _meshtastic.FixReceived += OnFix;
        _traccar.FixReceived += OnFix;
        _traccar.Error += message => Dispatcher.UIThread.Post(() => Log(L.F("traccar_x", message)));

        // Démarrage automatique du serveur (réglage du poste, ou option de lancement « --serveur-gps »).
        // Lors d'un rechargement de l'OP, l'ancienne réception GPS est reprise : celle-ci ne démarre alors rien.
        _autoStartServer = AppSettings.Current.AutoStartGpsServer;
        if (_autoStartServer || AppSettings.StartGpsServerOnce)
            Dispatcher.UIThread.Post(async () =>
            {
                if (_tracking.Gps == this && !_server.IsRunning)
                    await ToggleServerAsync();
            });
    }

    public ObservableCollection<string> Journal { get; } = [];

    public ObservableCollection<UnknownDevice> UnknownDevices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignDeviceCommand))]
    private UnknownDevice? _selectedUnknownDevice;

    // ----- Serveur local -----

    [ObservableProperty]
    private decimal? _serverPort;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private string _serverAddresses = "";

    [RelayCommand]
    private async Task ToggleServerAsync()
    {
        try
        {
            if (_server.IsRunning)
            {
                await _server.StopAsync();
                StopDynDns();
                ServerAddresses = "";
                Log(L.T("serveur_local_arrete"));
            }
            else
            {
                var port = (int)(ServerPort ?? 5055);
                await _server.StartAsync(port);
                AppSettings.Current.GpsServerPort = port;
                AppSettings.Current.Save();
                ServerAddresses = string.Join("   ", LocalGpsServer.LocalAddresses(port));
                Log(L.F("serveur_local_demarre_x", ServerAddresses));
                if (!_server.IsDiscoverable)
                    Log(L.F("recherche_automatique_sur_le_wi_fi_indisponible", Discovery.Port));
                StartDynDns();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync(L.F("impossible_de_demarrer_le_serveur_port_deja_util", ex.Message));
        }

        IsServerRunning = _server.IsRunning;
    }

    // ----- Application Android : enrôlement par code d'équipe -----

    public ObservableCollection<EnrolledDeviceRow> Devices { get; } = [];

    public ObservableCollection<TeamViewModel> Teams => _teams.Items;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCodeCommand), nameof(ShowQrCodeCommand))]
    private TeamViewModel? _enrollmentTeam;

    partial void OnEnrollmentTeamChanged(TeamViewModel? value) => RefreshDevices();

    /// <summary>Orga sélectionné dans l'onglet Orgas (enrôlement de son téléphone).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateOrganizerCodeCommand), nameof(ShowOrganizerQrCodeCommand))]
    private OrganizerViewModel? _enrollmentOrganizer;

    partial void OnEnrollmentOrganizerChanged(OrganizerViewModel? value) => RefreshDevices();

    /// <summary>Orgas de l'OP (codes d'enrôlement de leurs téléphones).</summary>
    public OrganizersViewModel? Organizers { get; set; }

    /// <summary>Téléphones de l'équipe choisie (onglet Équipes).</summary>
    public ObservableCollection<EnrolledDeviceRow> TeamDevices { get; } = [];

    /// <summary>Téléphones de l'orga choisi (onglet Orgas).</summary>
    public ObservableCollection<EnrolledDeviceRow> OrganizerDevices { get; } = [];

    /// <summary>État du serveur, affiché dans le suivi (« 📡 Serveur actif · 3 téléphones »).</summary>
    public string ServerStatus => (IsServerRunning ? L.T("serveur_actif_2") : L.T("serveur_arrete"))
                                  + L.F("x_telephone_s", _devices.Count(d => !d.IsRevoked));

    partial void OnIsServerRunningChanged(bool value) => OnPropertyChanged(nameof(ServerStatus));

    [RelayCommand(CanExecute = nameof(HasEnrollmentOrganizer))]
    private void GenerateOrganizerCode()
    {
        EnrollmentOrganizer!.EnrollmentCode = EnrollmentCodes.Generate(AllCodes());
        Log(L.F("code_d_enrolement_de_l_orga_x_x", EnrollmentOrganizer.Name, EnrollmentOrganizer.EnrollmentCodeText));
    }

    [RelayCommand(CanExecute = nameof(HasEnrollmentOrganizer))]
    private async Task ShowOrganizerQrCodeAsync()
    {
        var organizer = EnrollmentOrganizer!;
        if (organizer.EnrollmentCode.Length == 0)
            GenerateOrganizerCode();
        await ShowQrAsync(L.F("enrolement_orga_x", organizer.Name), organizer.EnrollmentCode, organizer.EnrollmentCodeText);
    }

    private bool HasEnrollmentOrganizer => EnrollmentOrganizer is not null;

    /// <summary>Codes déjà attribués (équipes et orgas) : chaque code est unique dans l'OP.</summary>
    private List<string> AllCodes() => _teams.Items.Select(t => t.EnrollmentCode)
        .Concat(Organizers?.Items.Select(o => o.EnrollmentCode) ?? [])
        .Where(c => c.Length > 0).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevokeDeviceCommand))]
    private EnrolledDeviceRow? _selectedDevice;

    /// <summary>Intervalle d'envoi des positions par les téléphones (renvoyé à chaque envoi : modifiable pendant l'OP).</summary>
    [ObservableProperty]
    private decimal? _intervalSeconds;

    public IReadOnlyList<AllyShareModeOption> ShareModes => AllyShareModeOption.All;

    /// <summary>Ce que les téléphones voient des alliés : rien, coordonnées (version difficile) ou carte.</summary>
    [ObservableProperty]
    private AllyShareModeOption _shareMode;

    partial void OnShareModeChanged(AllyShareModeOption value) => _file.Operation.AllyShareMode = value.Value;

    partial void OnIntervalSecondsChanged(decimal? value) =>
        _file.Operation.TrackingIntervalSeconds = Math.Clamp((int)(value ?? 30), 5, 3600);

    /// <summary>Code de l'équipe choisie (nouveau code : les téléphones déjà enrôlés restent valides).</summary>
    [RelayCommand(CanExecute = nameof(HasEnrollmentTeam))]
    private void GenerateCode()
    {
        EnrollmentTeam!.EnrollmentCode = EnrollmentCodes.Generate(AllCodes());
        Log(L.F("code_d_enrolement_de_x_x", EnrollmentTeam.Name, EnrollmentTeam.EnrollmentCodeText));
    }

    /// <summary>QR code à scanner avec l'application : adresse du serveur et code de l'équipe.</summary>
    [RelayCommand(CanExecute = nameof(HasEnrollmentTeam))]
    private async Task ShowQrCodeAsync()
    {
        var team = EnrollmentTeam!;
        if (team.EnrollmentCode.Length == 0)
            GenerateCode();

        await ShowQrAsync(L.F("enrolement_x", team.Name), team.EnrollmentCode, team.EnrollmentCodeText);
    }

    private async Task ShowQrAsync(string title, string code, string codeText)
    {
        var address = PublishedAddress((int)(ServerPort ?? 5055));
        if (address is null)
        {
            await _dialogs.ShowErrorAsync(L.T("aucune_connexion_reseau_active_reliez_ce_pc_au_w"));
            return;
        }

        var link = EnrollmentLink.Create(address, code);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(10);
        await _dialogs.ShowImageAsync(title,
            L.T("dans_l_application_airsoft_planner_scanner_ce_qr") +
            L.F("serveur_x_code_x", address, codeText) +
            (IsServerRunning ? "" : L.T("pensez_a_activer_le_serveur_local_avant_l_enrole")), png);
    }

    [RelayCommand]
    private void RevokeRow(EnrolledDeviceRow? row)
    {
        if (row is null || row.IsRevoked)
            return;
        row.Model.IsRevoked = true;
        Log(L.F("telephone_x_revoque", row.Device));
        RefreshDevices();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDevice))]
    private void RevokeDevice()
    {
        SelectedDevice!.Model.IsRevoked = true;
        Log(L.F("telephone_x_revoque", SelectedDevice.Device));
        RefreshDevices();
    }

    /// <summary>Démarrer le serveur local dès l'ouverture d'une OP (réglage du poste).</summary>
    [ObservableProperty]
    private bool _autoStartServer;

    partial void OnAutoStartServerChanged(bool value)
    {
        AppSettings.Current.AutoStartGpsServer = value;
        AppSettings.Current.Save();
    }

    private bool HasEnrollmentTeam => EnrollmentTeam is not null;

    private bool HasSelectedDevice => SelectedDevice is { IsRevoked: false };

    private EnrollResponse? EnrollDevice(EnrollRequest request)
    {
        var code = EnrollmentCodes.Normalize(request.Code);
        var team = _teams.Items.FirstOrDefault(t => t.EnrollmentCode.Length > 0 && t.EnrollmentCode == code);
        var organizer = team is null ? Organizers?.Items.FirstOrDefault(o => o.EnrollmentCode.Length > 0 && o.EnrollmentCode == code) : null;
        if (organizer is not null)
            return EnrollOrganizer(organizer, request);
        if (team is null)
        {
            Log(L.F("enrolement_refuse_code_x_inconnu_x", request.Code, request.DeviceName));
            return null;
        }

        var device = new EnrolledDevice
        {
            TeamId = team.Model.Id,
            Token = EnrollmentCodes.NewToken(),
            DeviceName = request.DeviceName.Trim().Length > 0 ? request.DeviceName.Trim() : L.T("telephone"),
            EnrolledAt = DateTimeOffset.Now,
        };
        _file.Add(device);
        _devices.Add(device);
        RefreshDevices();
        Log(L.F("telephone_x_enrole_pour_x", device.DeviceName, team.Name));
        return new EnrollResponse(device.Token, team.Name, _file.Operation.Name, team.Faction?.Name ?? "",
            team.RadioFrequency, _file.Operation.TrackingIntervalSeconds, _file.Operation.AllyShareMode, CommsFor(team), _file.Operation.Id);
    }

    private EnrollResponse EnrollOrganizer(OrganizerViewModel organizer, EnrollRequest request)
    {
        var device = new EnrolledDevice
        {
            TeamId = organizer.Model.Id,
            IsOrganizer = true,
            Token = EnrollmentCodes.NewToken(),
            DeviceName = request.DeviceName.Trim().Length > 0 ? request.DeviceName.Trim() : L.T("telephone"),
            EnrolledAt = DateTimeOffset.Now,
        };
        _file.Add(device);
        _devices.Add(device);
        RefreshDevices();
        Log(L.F("telephone_x_enrole_pour_l_orga_x", device.DeviceName, organizer.Name));
        return new EnrollResponse(device.Token, organizer.Name, _file.Operation.Name, L.T("orga_2"), organizer.RadioFrequency,
            _file.Operation.TrackingIntervalSeconds, AllyShareMode.Map, OrgaComms(), _file.Operation.Id);
    }

    private Comms OrgaComms() => new(L.T("orga_2"), _file.Operation.OrgaRadioFrequency,
        _teams.Items.Select(t => new TeamFrequency(t.Name, t.RadioFrequency, t.Faction?.CommandTeam == t)).ToList(),
        _file.Operation.OrgaRadioFrequency, _file.Operation.EmergencyPhone);

    /// <summary>Téléphone d'orga : il voit toutes les équipes et tous les points, sur la carte.</summary>
    private TrackResponse? AuthorizeOrganizer(EnrolledDevice device)
    {
        var organizer = Organizers?.Items.FirstOrDefault(o => o.Model.Id == device.TeamId);
        if (organizer is null)
            return null;
        device.LastSeenAt = DateTimeOffset.Now;
        RefreshDevices();
        var format = _file.Operation.CoordinateFormat;
        var teams = _tracking.LatestPositions()
            .Select(p => new AllyPosition(p.Team.Name, p.Point.Latitude, p.Point.Longitude, Core.Geo.Coordinates.Format(p.Point, format), p.Time,
                p.Team.RadioFrequency, p.Team.ResolvedSymbol, p.Team.Echelon, p.Team.Faction?.Color ?? "#607D8B"))
            .ToList();
        var layer = _tracking.Terrain.SelectedLayer ?? _tracking.Terrain.Layers.FirstOrDefault();
        var map = layer is null ? null : new MapInfo(layer.Name, layer.Attribution, layer.Bounds.North, layer.Bounds.South, layer.Bounds.West, layer.Bounds.East);
        var points = _tracking.Terrain.Zones.Where(z => z.IsComplete).Select(PoiFor).ToList();
        return new TrackResponse(organizer.Name, _file.Operation.TrackingIntervalSeconds, map is null ? AllyShareMode.Coordinates : AllyShareMode.Map,
            teams, null, map, OrgaComms(), format, _tracking.Dispatch?.PhoneMessagesForOrga() ?? [], points);
    }

    private PoiInfo PoiFor(ZoneViewModel z)
    {
        var center = z.IsArea ? Core.Geo.GeoMath.Centroid(z.Points) : z.Points[0];
        return new PoiInfo(z.Name, PoiCategories.Label(z.Model.Category), PoiCategories.Symbol(z.Model.Category),
            Core.Geo.Coordinates.Format(center, _file.Operation.CoordinateFormat), center.Latitude, center.Longitude, z.Description, z.Color,
            z.IsArea ? z.Points.Select(p => new LatLon(p.Latitude, p.Longitude)).ToList() : [],
            z.ResolvedSymbol, z.Model.Echelon, z.SymbolColor);
    }

    private TrackResponse? AuthorizeDevice(string token)
    {
        var device = _devices.FirstOrDefault(d => d.Token == token && !d.IsRevoked);
        if (device is { IsOrganizer: true })
            return AuthorizeOrganizer(device);
        var team = device is null ? null : _teams.Items.FirstOrDefault(t => t.Model.Id == device.TeamId);
        if (device is null || team is null)
            return null;

        device.LastSeenAt = DateTimeOffset.Now;
        RefreshDevices();
        var mode = _file.Operation.AllyShareMode;
        var format = _file.Operation.CoordinateFormat;
        var allies = mode == AllyShareMode.None ? [] : _tracking.LatestPositions()
            .Where(p => p.Team != team && p.Team.Faction is not null && p.Team.Faction == team.Faction)
            .Select(p => new AllyPosition(p.Team.Name, p.Point.Latitude, p.Point.Longitude,
                Core.Geo.Coordinates.Format(p.Point, format), p.Time, p.Team.RadioFrequency,
                p.Team.ResolvedSymbol, p.Team.Echelon, p.Team.Faction?.Color ?? "#607D8B"))
            .ToList();
        var layer = _tracking.Terrain.SelectedLayer ?? _tracking.Terrain.Layers.FirstOrDefault();
        var map = mode == AllyShareMode.Map && layer is not null
            ? new MapInfo(layer.Name, layer.Attribution, layer.Bounds.North, layer.Bounds.South, layer.Bounds.West, layer.Bounds.East)
            : null;
        var points = _tracking.Terrain.Zones
            .Where(z => z.IsComplete && z.Model.IsVisibleTo(team.Model))
            .Select(PoiFor)
            .ToList();
        var dispatch = _tracking.Dispatch;
        return new TrackResponse(team.Name, _file.Operation.TrackingIntervalSeconds, mode, allies, dispatch?.MissionBriefFor(team, format), map,
            CommsFor(team), format, dispatch?.PhoneMessagesFor(team) ?? [], points);
    }

    /// <summary>Fréquences de la faction, des équipes alliées et de l'orga, numéro d'urgence.</summary>
    private Comms CommsFor(TeamViewModel team)
    {
        var faction = team.Faction;
        var teams = faction is null
            ? [new TeamFrequency(team.Name, team.RadioFrequency, false)]
            : faction.PlayingTeams.Select(t => new TeamFrequency(t.Name, t.RadioFrequency, faction.CommandTeam == t)).ToList();
        return new Comms(faction?.Name ?? "", faction?.RadioFrequency ?? "", teams,
            _file.Operation.OrgaRadioFrequency, _file.Operation.EmergencyPhone);
    }

    private byte[]? MessagePhotoFor(string token, Guid id)
    {
        var device = _devices.FirstOrDefault(d => d.Token == token && !d.IsRevoked);
        if (device is { IsOrganizer: true })
            return _tracking.Dispatch?.AllMessages.FirstOrDefault(m => m.Id == id)?.Photo;
        var team = device is null ? null : _teams.Items.FirstOrDefault(t => t.Model.Id == device.TeamId);
        return team is null ? null : _tracking.Dispatch?.PhotoFor(team, id);
    }

    /// <summary>Fond de carte (redimensionné pour un téléphone), uniquement si l'OP autorise le mode carte.</summary>
    private byte[]? MapImageFor(string token)
    {
        var device = _devices.FirstOrDefault(d => d.Token == token && !d.IsRevoked);
        if (device is null || (_file.Operation.AllyShareMode != AllyShareMode.Map && !device.IsOrganizer))
            return null;
        var layer = _tracking.Terrain.SelectedLayer ?? _tracking.Terrain.Layers.FirstOrDefault();
        return layer is null ? null : MapSnapshot.Render(layer.Model, [], maxSide: 2048);
    }

    private void RefreshDevices()
    {
        var selected = SelectedDevice?.Model;
        Devices.Clear();
        foreach (var device in _devices.OrderBy(d => d.IsRevoked).ThenByDescending(d => d.LastSeenAt ?? d.EnrolledAt))
        {
            var team = device.IsOrganizer
                ? L.F("orga_x", Organizers?.Items.FirstOrDefault(o => o.Model.Id == device.TeamId)?.Name ?? L.T("supprime"))
                : _teams.Items.FirstOrDefault(t => t.Model.Id == device.TeamId)?.Name ?? L.T("equipe_supprimee_2");
            var seen = device.IsRevoked ? L.T("revoque")
                : device.LastSeenAt is { } at ? L.F("dernier_envoi_x", at.LocalDateTime) : L.T("enrole_aucun_envoi");
            Devices.Add(new EnrolledDeviceRow(device, team, device.DeviceName, seen, device.IsRevoked));
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Model == selected);
        TeamDevices.Clear();
        foreach (var row in Devices.Where(d => !d.Model.IsOrganizer && d.Model.TeamId == EnrollmentTeam?.Model.Id))
            TeamDevices.Add(row);
        OrganizerDevices.Clear();
        foreach (var row in Devices.Where(d => d.Model.IsOrganizer && d.Model.TeamId == EnrollmentOrganizer?.Model.Id))
            OrganizerDevices.Add(row);
        OnPropertyChanged(nameof(ServerStatus));
    }

    // ----- Adresse publiée et DynDNS -----

    /// <summary>Nom DynDNS (ou adresse fixe) donné aux équipes dans les QR codes et les packages ; vide : IP locale du PC.</summary>
    [ObservableProperty]
    private string _serverAddress;

    partial void OnServerAddressChanged(string value) => _file.Operation.ServerAddress = value.Trim();

    /// <summary>Adresse de mise à jour du service DynDNS (réglage du poste, avec le jeton du compte).</summary>
    [ObservableProperty]
    private string _dynDnsUpdateUrl;

    [ObservableProperty]
    private string _dynDnsStatus = "";

    partial void OnDynDnsUpdateUrlChanged(string value)
    {
        AppSettings.Current.DynDnsUpdateUrlProtected = Secret.Protect(value.Trim());
        AppSettings.Current.Save();
        _lastDynDnsIp = null;
    }

    private System.Threading.Timer? _dynDnsTimer;
    private string? _lastDynDnsIp;
    private static readonly System.Net.Http.HttpClient DynDnsHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Adresse à donner aux téléphones : nom publié de l'OP, sinon IP locale actuelle du PC.</summary>
    public string? PublishedAddress(int port) =>
        _file.Operation.ServerAddress.Length > 0
            ? EnrollmentLink.ServerUrl(_file.Operation.ServerAddress, port)
            : LocalGpsServer.LocalAddresses(port).FirstOrDefault();

    /// <summary>Met à jour le nom DynDNS tout de suite (puis toutes les 5 minutes tant que le serveur tourne, si l'IP change).</summary>
    [RelayCommand]
    private async Task UpdateDynDnsAsync()
    {
        _lastDynDnsIp = null;
        await RefreshDynDnsAsync();
    }

    private void StartDynDns()
    {
        _dynDnsTimer?.Dispose();
        _dynDnsTimer = new System.Threading.Timer(_ => Dispatcher.UIThread.Post(async () => await RefreshDynDnsAsync()),
            null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private void StopDynDns()
    {
        _dynDnsTimer?.Dispose();
        _dynDnsTimer = null;
    }

    private async Task RefreshDynDnsAsync()
    {
        var template = DynDnsUpdateUrl.Trim();
        if (template.Length == 0)
            return;

        var local = LocalGpsServer.LocalAddresses(80).FirstOrDefault();
        var ip = local is null ? "" : new Uri(local).Host;
        if (ip.Length == 0 || ip == _lastDynDnsIp)
            return;

        try
        {
            var response = await DynDnsHttp.GetStringAsync(DynDns.BuildUrl(template, ip));
            if (DynDns.IsSuccess(response))
            {
                _lastDynDnsIp = ip;
                DynDnsStatus = L.F("nom_dyndns_a_jour_x_a_x", ip, DateTime.Now);
            }
            else
            {
                DynDnsStatus = L.F("le_service_dyndns_refuse_la_mise_a_jour_x", response.Trim());
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            DynDnsStatus = L.F("service_dyndns_injoignable_pas_d_internet_la_rec", ex.Message);
        }

        Log(DynDnsStatus);
    }

    // ----- Second poste : connexion au PC de l'OP -----

    [ObservableProperty]
    private string _upstreamUrl;

    [ObservableProperty]
    private bool _isUpstreamConnected;

    /// <summary>Récupère les positions collectées par le PC qui mène l'OP (son serveur local).</summary>
    [RelayCommand]
    private async Task ToggleUpstreamAsync()
    {
        try
        {
            if (_upstream.IsConnected)
            {
                _upstream.Disconnect();
                Log(L.T("deconnecte_du_pc_de_l_op"));
            }
            else
            {
                var url = UpstreamUrl.Trim();
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = "http://" + url;
                await _upstream.ConnectAsync(url, TimeSpan.FromSeconds(10));
                AppSettings.Current.UpstreamUrl = url;
                AppSettings.Current.Save();
                UpstreamUrl = url;
                Log(L.F("connecte_au_pc_de_l_op_x", url));
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(L.F("le_pc_de_l_op_ne_repond_pas_adresse_meme_reseau", ex.Message));
        }

        IsUpstreamConnected = _upstream.IsConnected;
    }

    // ----- Meshtastic (MQTT) -----

    [ObservableProperty]
    private string _mqttHost;

    [ObservableProperty]
    private decimal? _mqttPort;

    [ObservableProperty]
    private string _mqttTopic;

    [ObservableProperty]
    private string _mqttUser;

    [ObservableProperty]
    private string _mqttPassword;

    [ObservableProperty]
    private bool _isMeshtasticConnected;

    [RelayCommand]
    private async Task ToggleMeshtasticAsync()
    {
        try
        {
            if (_meshtastic.IsConnected)
            {
                await _meshtastic.DisconnectAsync();
                Log(L.T("meshtastic_deconnecte"));
            }
            else
            {
                await _meshtastic.ConnectAsync(MqttHost.Trim(), (int)(MqttPort ?? 1883), MqttTopic.Trim(), MqttUser.Trim(), MqttPassword);
                var settings = AppSettings.Current;
                (settings.MqttHost, settings.MqttPort, settings.MqttTopic, settings.MqttUser) = (MqttHost.Trim(), (int)(MqttPort ?? 1883), MqttTopic.Trim(), MqttUser.Trim());
                settings.MqttPasswordProtected = Secret.Protect(MqttPassword);
                settings.Save();
                Log(L.F("meshtastic_connecte_au_broker_x_x", MqttHost, MqttTopic));
            }
        }
        catch (Exception ex)
        {
            // Erreurs réseau très variées selon le broker : toutes signalées, sans fermer le logiciel.
            await _dialogs.ShowErrorAsync(L.F("connexion_au_broker_mqtt_impossible_x", ex.Message));
        }

        IsMeshtasticConnected = _meshtastic.IsConnected;
    }

    // ----- Serveur Traccar (webservice) -----

    [ObservableProperty]
    private string _traccarUrl;

    [ObservableProperty]
    private string _traccarUser;

    [ObservableProperty]
    private string _traccarPassword;

    [ObservableProperty]
    private bool _isTraccarConnected;

    [RelayCommand]
    private async Task ToggleTraccarAsync()
    {
        try
        {
            if (_traccar.IsConnected)
            {
                _traccar.Disconnect();
                Log(L.T("serveur_traccar_deconnecte"));
            }
            else
            {
                await _traccar.ConnectAsync(TraccarUrl.Trim(), TraccarUser.Trim(), TraccarPassword, TimeSpan.FromSeconds(10));
                var settings = AppSettings.Current;
                (settings.TraccarUrl, settings.TraccarUser) = (TraccarUrl.Trim(), TraccarUser.Trim());
                settings.TraccarPasswordProtected = Secret.Protect(TraccarPassword);
                settings.Save();
                Log(L.F("serveur_traccar_connecte_x", TraccarUrl));
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(L.F("connexion_au_serveur_traccar_impossible_x", ex.Message));
        }

        IsTraccarConnected = _traccar.IsConnected;
    }

    // ----- Fichiers -----

    /// <summary>Importe une trace GPX pour l'équipe sélectionnée dans le suivi, ou un CSV (colonne Équipe/Appareil).</summary>
    [RelayCommand]
    private async Task ImportFileAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(L.T("importer_des_positions"), L.T("trace_gpx_ou_fichier_csv"), ["*.gpx", "*.csv", "*.txt"]);
        if (path is null)
            return;

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var isGpx = Path.GetExtension(path).Equals(".gpx", StringComparison.OrdinalIgnoreCase);
            if (isGpx && _tracking.Selected is null)
            {
                await _dialogs.ShowErrorAsync(L.T("selectionnez_d_abord_l_equipe_a_qui_attribuer_ce"));
                return;
            }

            var fixes = isGpx ? GpsParsers.FromGpx(text, _tracking.Selected!.Team.Name) : GpsParsers.FromCsv(text);
            foreach (var fix in fixes)
                Apply(fix);
            Log(L.F("x_position_s_importee_s_depuis_x", fixes.Count, Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Xml.XmlException)
        {
            await _dialogs.ShowErrorAsync(L.F("import_impossible_x", ex.Message));
        }
    }

    /// <summary>Associe l'appareil inconnu sélectionné à l'équipe sélectionnée dans le suivi.</summary>
    [RelayCommand(CanExecute = nameof(CanAssignDevice))]
    private void AssignDevice()
    {
        var device = SelectedUnknownDevice!;
        var team = _tracking.Selected!.Team;
        team.GpsDeviceIds = string.Join(", ", GpsParsers.DeviceIds(team.Model).Append(device.DeviceId));
        UnknownDevices.Remove(device);
        Log(L.F("appareil_x_associe_a_x", device.DeviceId, team.Name));
    }

    private bool CanAssignDevice => SelectedUnknownDevice is not null && _tracking.Selected is not null;

    /// <summary>Après un rechargement de l'OP : les sources restent connectées, seules les cibles changent.</summary>
    public void Rebind(Data.OperationFile file, TrackingViewModel tracking, TeamsViewModel teams, VehicleTracker vehicles)
    {
        _file = file;
        ServerAddress = file.Operation.ServerAddress;
        _devices.Clear();
        _devices.AddRange(file.LoadEnrolledDevices());
        RefreshDevices();
        _tracking = tracking;
        _teams = teams;
        _vehicles = vehicles;
    }

    public void NotifyTeamSelectionChanged() => AssignDeviceCommand.NotifyCanExecuteChanged();

    public async ValueTask DisposeAsync()
    {
        StopDynDns();
        await _server.DisposeAsync();
        await _meshtastic.DisposeAsync();
        _traccar.Dispose();
        _upstream.Dispose();
    }

    // Les sources reçoivent sur leurs propres fils : tout est ramené sur le fil de l'interface.
    private void OnFix(GpsFix fix) => Dispatcher.UIThread.Post(() => Apply(fix));

    private void Apply(GpsFix fix)
    {
        // Traceur d'un véhicule mis en jeu : alimente son kilométrage (et sa position sur la carte).
        var vehicle = _teams.Items.SelectMany(t => t.Vehicles)
            .FirstOrDefault(v => v.GpsDeviceId.Length > 0 && v.GpsDeviceId.Equals(fix.DeviceId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (vehicle is not null)
        {
            _vehicles.Add(vehicle.Model, fix.Point, fix.Time ?? DateTimeOffset.Now);
            Log(L.F("x_vehicule_x_x_x_x_2", (fix.Time ?? DateTimeOffset.Now).LocalDateTime, vehicle.Kind, fix.Source, fix.DeviceId, vehicle.KilometersText));
            _tracking.Refresh();
            return;
        }

        var team = GpsParsers.FindTeam(_teams.Items.Select(t => t.Model), fix.DeviceId) is { } model
            ? _teams.Items.First(t => t.Model == model)
            : null;
        if (team is null && fix.Source == L.T("appli_android") && Organizers?.Items.FirstOrDefault(o => o.Name == fix.DeviceId) is { } organizer)
        {
            _tracking.RecordOrganizerPosition(organizer, fix.Point, fix.Time);
            Log(L.F("x_orga_x_telephone_2", (fix.Time ?? DateTimeOffset.Now).LocalDateTime, organizer.Name));
            return;
        }
        var time = (fix.Time ?? DateTimeOffset.Now).LocalDateTime.ToString("HH:mm:ss");
        if (team is null)
        {
            var existing = UnknownDevices.FirstOrDefault(d => d.DeviceId == fix.DeviceId);
            if (existing is not null)
                UnknownDevices.Remove(existing);
            UnknownDevices.Insert(0, new UnknownDevice(fix.DeviceId, fix.Source, time));
            return;
        }

        _tracking.RecordPosition(team, fix.Point, fix.Source, fix.Time);
        Log($"{time} {team.Name} ({fix.Source}, {fix.DeviceId})");
    }

    private void Log(string message)
    {
        Journal.Insert(0, message);
        while (Journal.Count > 200)
            Journal.RemoveAt(Journal.Count - 1);
    }
}
