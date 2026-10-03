using System;
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

    public GpsViewModel(TrackingViewModel tracking, TeamsViewModel teams, IFileDialogService dialogs, VehicleTracker vehicles)
    {
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

        _server.FixReceived += OnFix;
        _server.Positions = () => _tracking.PublishedPositions;
        _server.TeamNames = () => _tracking.PublishedTeamNames;
        _upstream.FixReceived += OnFix;
        _upstream.Error += message => Dispatcher.UIThread.Post(() => Log($"PC de l'OP : {message}"));
        _upstreamUrl = settings.UpstreamUrl;
        _meshtastic.FixReceived += OnFix;
        _traccar.FixReceived += OnFix;
        _traccar.Error += message => Dispatcher.UIThread.Post(() => Log($"Traccar : {message}"));
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
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync($"Impossible de démarrer le serveur (port déjà utilisé ?) : {ex.Message}");
        }

        IsServerRunning = _server.IsRunning;
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
    public void Rebind(TrackingViewModel tracking, TeamsViewModel teams, VehicleTracker vehicles)
    {
        _tracking = tracking;
        _teams = teams;
        _vehicles = vehicles;
    }

    public void NotifyTeamSelectionChanged() => AssignDeviceCommand.NotifyCanExecuteChanged();

    public async ValueTask DisposeAsync()
    {
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
