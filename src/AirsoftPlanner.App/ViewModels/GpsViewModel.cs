using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.Services.Gps;
using AirsoftPlanner.Core.Gps;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public record AllyShareModeOption(AllyShareMode Value, string Label)
{
    public static IReadOnlyList<AllyShareModeOption> All { get; } =
    [
        new(AllyShareMode.None, "Rien (sa propre position seulement)"),
        new(AllyShareMode.Coordinates, "Alliés en coordonnées (version difficile)"),
        new(AllyShareMode.Map, "Alliés sur la carte"),
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
        _server.MapImage = token => Dispatcher.UIThread.InvokeAsync(() => MapImageFor(token)).GetAwaiter().GetResult();
        _shareMode = AllyShareModeOption.Of(file.Operation.AllyShareMode);
        RefreshDevices();
        _upstream.FixReceived += OnFix;
        _upstream.Error += message => Dispatcher.UIThread.Post(() => Log($"PC de l'OP : {message}"));
        _upstreamUrl = settings.UpstreamUrl;
        _meshtastic.FixReceived += OnFix;
        _traccar.FixReceived += OnFix;
        _traccar.Error += message => Dispatcher.UIThread.Post(() => Log($"Traccar : {message}"));

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
                Log("Serveur local arrêté.");
            }
            else
            {
                var port = (int)(ServerPort ?? 5055);
                await _server.StartAsync(port);
                AppSettings.Current.GpsServerPort = port;
                AppSettings.Current.Save();
                ServerAddresses = string.Join("   ", LocalGpsServer.LocalAddresses(port));
                Log($"Serveur local démarré : {ServerAddresses}");
                if (!_server.IsDiscoverable)
                    Log($"Recherche automatique sur le Wi-Fi indisponible (port UDP {Discovery.Port} occupé).");
                StartDynDns();
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync($"Impossible de démarrer le serveur (port déjà utilisé ?) : {ex.Message}");
        }

        IsServerRunning = _server.IsRunning;
    }

    // ----- Application Android : enrôlement par code d'équipe -----

    public ObservableCollection<EnrolledDeviceRow> Devices { get; } = [];

    public ObservableCollection<TeamViewModel> Teams => _teams.Items;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCodeCommand), nameof(ShowQrCodeCommand))]
    private TeamViewModel? _enrollmentTeam;

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
        var existing = _teams.Items.Select(t => t.EnrollmentCode).Where(c => c.Length > 0).ToList();
        EnrollmentTeam!.EnrollmentCode = EnrollmentCodes.Generate(existing);
        Log($"Code d'enrôlement de {EnrollmentTeam.Name} : {EnrollmentTeam.EnrollmentCodeText}");
    }

    /// <summary>QR code à scanner avec l'application : adresse du serveur et code de l'équipe.</summary>
    [RelayCommand(CanExecute = nameof(HasEnrollmentTeam))]
    private async Task ShowQrCodeAsync()
    {
        var team = EnrollmentTeam!;
        if (team.EnrollmentCode.Length == 0)
            GenerateCode();

        var address = PublishedAddress((int)(ServerPort ?? 5055));
        if (address is null)
        {
            await _dialogs.ShowErrorAsync("Aucune connexion réseau active : reliez ce PC au Wi-Fi du terrain.");
            return;
        }

        var link = EnrollmentLink.Create(address, team.EnrollmentCode);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(10);
        await _dialogs.ShowImageAsync($"Enrôlement — {team.Name}",
            $"Dans l'application Airsoft Planner du chef d'équipe : « Scanner le QR code », ou saisir :\n" +
            $"Serveur : {address}\nCode : {team.EnrollmentCodeText}" +
            (IsServerRunning ? "" : "\n\n⚠ Pensez à activer le serveur local avant l'enrôlement."), png);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDevice))]
    private void RevokeDevice()
    {
        SelectedDevice!.Model.IsRevoked = true;
        Log($"Téléphone « {SelectedDevice.Device} » révoqué.");
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
        if (team is null)
        {
            Log($"Enrôlement refusé : code « {request.Code} » inconnu ({request.DeviceName}).");
            return null;
        }

        var device = new EnrolledDevice
        {
            TeamId = team.Model.Id,
            Token = EnrollmentCodes.NewToken(),
            DeviceName = request.DeviceName.Trim().Length > 0 ? request.DeviceName.Trim() : "Téléphone",
            EnrolledAt = DateTimeOffset.Now,
        };
        _file.Add(device);
        _devices.Add(device);
        RefreshDevices();
        Log($"Téléphone « {device.DeviceName} » enrôlé pour {team.Name}.");
        return new EnrollResponse(device.Token, team.Name, _file.Operation.Name, team.Faction?.Name ?? "",
            team.RadioFrequency, _file.Operation.TrackingIntervalSeconds, _file.Operation.AllyShareMode, CommsFor(team), _file.Operation.Id);
    }

    private TrackResponse? AuthorizeDevice(string token)
    {
        var device = _devices.FirstOrDefault(d => d.Token == token && !d.IsRevoked);
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
                Core.Geo.Coordinates.Format(p.Point, format), p.Time, p.Team.RadioFrequency))
            .ToList();
        var layer = _tracking.Terrain.SelectedLayer ?? _tracking.Terrain.Layers.FirstOrDefault();
        var map = mode == AllyShareMode.Map && layer is not null
            ? new MapInfo(layer.Name, layer.Attribution, layer.Bounds.North, layer.Bounds.South, layer.Bounds.West, layer.Bounds.East)
            : null;
        return new TrackResponse(team.Name, _file.Operation.TrackingIntervalSeconds, mode, allies, _tracking.MissionBriefFor(team, format), map,
            CommsFor(team), format);
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

    /// <summary>Fond de carte (redimensionné pour un téléphone), uniquement si l'OP autorise le mode carte.</summary>
    private byte[]? MapImageFor(string token)
    {
        if (_file.Operation.AllyShareMode != AllyShareMode.Map || !_devices.Any(d => d.Token == token && !d.IsRevoked))
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
            var team = _teams.Items.FirstOrDefault(t => t.Model.Id == device.TeamId)?.Name ?? "équipe supprimée";
            var seen = device.IsRevoked ? "révoqué"
                : device.LastSeenAt is { } at ? $"dernier envoi {at.LocalDateTime:HH:mm:ss}" : "enrôlé, aucun envoi";
            Devices.Add(new EnrolledDeviceRow(device, team, device.DeviceName, seen, device.IsRevoked));
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Model == selected);
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
                DynDnsStatus = $"✔ Nom DynDNS à jour ({ip}) à {DateTime.Now:HH:mm}";
            }
            else
            {
                DynDnsStatus = $"⚠ Le service DynDNS refuse la mise à jour : {response.Trim()}";
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            DynDnsStatus = $"⚠ Service DynDNS injoignable (pas d'Internet ?) : la recherche sur le Wi-Fi prend le relais. {ex.Message}";
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
                Log("Déconnecté du PC de l'OP.");
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
                Log($"Connecté au PC de l'OP : {url}");
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"Le PC de l'OP ne répond pas (adresse, même réseau, serveur actif, pare-feu ?) : {ex.Message}");
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
                Log("Meshtastic déconnecté.");
            }
            else
            {
                await _meshtastic.ConnectAsync(MqttHost.Trim(), (int)(MqttPort ?? 1883), MqttTopic.Trim(), MqttUser.Trim(), MqttPassword);
                var settings = AppSettings.Current;
                (settings.MqttHost, settings.MqttPort, settings.MqttTopic, settings.MqttUser) = (MqttHost.Trim(), (int)(MqttPort ?? 1883), MqttTopic.Trim(), MqttUser.Trim());
                settings.MqttPasswordProtected = Secret.Protect(MqttPassword);
                settings.Save();
                Log($"Meshtastic connecté au broker {MqttHost} ({MqttTopic}).");
            }
        }
        catch (Exception ex)
        {
            // Erreurs réseau très variées selon le broker : toutes signalées, sans fermer le logiciel.
            await _dialogs.ShowErrorAsync($"Connexion au broker MQTT impossible : {ex.Message}");
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
                Log("Serveur Traccar déconnecté.");
            }
            else
            {
                await _traccar.ConnectAsync(TraccarUrl.Trim(), TraccarUser.Trim(), TraccarPassword, TimeSpan.FromSeconds(10));
                var settings = AppSettings.Current;
                (settings.TraccarUrl, settings.TraccarUser) = (TraccarUrl.Trim(), TraccarUser.Trim());
                settings.TraccarPasswordProtected = Secret.Protect(TraccarPassword);
                settings.Save();
                Log($"Serveur Traccar connecté : {TraccarUrl}");
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"Connexion au serveur Traccar impossible : {ex.Message}");
        }

        IsTraccarConnected = _traccar.IsConnected;
    }

    // ----- Fichiers -----

    /// <summary>Importe une trace GPX pour l'équipe sélectionnée dans le suivi, ou un CSV (colonne Équipe/Appareil).</summary>
    [RelayCommand]
    private async Task ImportFileAsync()
    {
        var path = await _dialogs.PickOpenFileAsync("Importer des positions", "Trace GPX ou fichier CSV", ["*.gpx", "*.csv", "*.txt"]);
        if (path is null)
            return;

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var isGpx = Path.GetExtension(path).Equals(".gpx", StringComparison.OrdinalIgnoreCase);
            if (isGpx && _tracking.Selected is null)
            {
                await _dialogs.ShowErrorAsync("Sélectionnez d'abord l'équipe à qui attribuer cette trace (onglet Équipes du suivi).");
                return;
            }

            var fixes = isGpx ? GpsParsers.FromGpx(text, _tracking.Selected!.Team.Name) : GpsParsers.FromCsv(text);
            foreach (var fix in fixes)
                Apply(fix);
            Log($"{fixes.Count} position(s) importée(s) depuis {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Xml.XmlException)
        {
            await _dialogs.ShowErrorAsync($"Import impossible : {ex.Message}");
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
        Log($"Appareil {device.DeviceId} associé à {team.Name}.");
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
            Log($"{(fix.Time ?? DateTimeOffset.Now).LocalDateTime:HH:mm:ss} véhicule {vehicle.Kind} ({fix.Source}, {fix.DeviceId}) — {vehicle.KilometersText}");
            _tracking.Refresh();
            return;
        }

        var team = GpsParsers.FindTeam(_teams.Items.Select(t => t.Model), fix.DeviceId) is { } model
            ? _teams.Items.First(t => t.Model == model)
            : null;
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
