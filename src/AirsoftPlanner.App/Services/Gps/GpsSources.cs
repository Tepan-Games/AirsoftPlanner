using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services.Gps;

/// <summary>
/// Serveur intégré sur le réseau local du terrain : reçoit les positions des smartphones (Traccar Client,
/// OsmAnd, GPSLogger : protocole OsmAnd) et de tout système capable d'envoyer du JSON. Aucun accès Internet n'est nécessaire.
/// </summary>
public sealed class LocalGpsServer : IAsyncDisposable
{
    private WebApplication? _app;
    private DiscoveryResponder? _discovery;

    public event Action<GpsFix>? FixReceived;

    /// <summary>Dernière position connue de chaque équipe, publiée pour les autres postes (lue depuis le fil du serveur).</summary>
    public Func<IReadOnlyList<PublishedPosition>> Positions { get; set; } = () => [];

    /// <summary>Équipes proposées sur la page de saisie (lue depuis le fil du serveur).</summary>
    public Func<IReadOnlyList<string>> TeamNames { get; set; } = () => [];

    /// <summary>Enrôlement d'un téléphone : null si le code n'existe pas.</summary>
    public Func<EnrollRequest, EnrollResponse?> Enroll { get; set; } = _ => null;

    /// <summary>Équipe et intervalle associés à un jeton d'appareil : null si le jeton est inconnu ou révoqué.</summary>
    public Func<string, TrackResponse?> Authorize { get; set; } = _ => null;

    /// <summary>Photo d'un message destiné à l'équipe du jeton, sinon null.</summary>
    public Func<string, Guid, byte[]?> MessagePhoto { get; set; } = (_, _) => null;

    /// <summary>Image du fond de carte pour un jeton autorisé en mode carte, sinon null.</summary>
    public Func<string, byte[]?> MapImage { get; set; } = _ => null;

    /// <summary>OP menée par ce PC, annoncée aux téléphones qui le cherchent sur le Wi-Fi.</summary>
    public Func<(string Name, Guid Id)> OperationInfo { get; set; } = () => ("", Guid.Empty);

    /// <summary>Recherche sur le Wi-Fi active (port UDP libre).</summary>
    public bool IsDiscoverable => _discovery is not null;

    public bool IsRunning => _app is not null;

    public int Port { get; private set; }

    /// <summary>Adresses à saisir dans les applications des smartphones (une par carte réseau active).</summary>
    public static IReadOnlyList<string> LocalAddresses(int port) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => $"http://{a.Address}:{port}")
            .Distinct()
            .ToList();

    /// <summary>
    /// Adresse du serveur à donner à un téléphone : celle de la carte réseau du même sous-réseau que lui
    /// (PC relié à plusieurs réseaux), sinon la première adresse locale.
    /// </summary>
    public static string? AddressFor(IPAddress phone, int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        var same = addresses.FirstOrDefault(a => SameSubnet(a.Address, phone, a.IPv4Mask)) ?? addresses.FirstOrDefault();
        return same is null ? null : $"http://{same.Address}:{port}";
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, IPAddress? mask)
    {
        if (mask is null || b.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var (x, y, m) = (a.GetAddressBytes(), b.MapToIPv4().GetAddressBytes(), mask.GetAddressBytes());
        return Enumerable.Range(0, 4).All(i => (x[i] & m[i]) == (y[i] & m[i]));
    }

    public async Task StartAsync(int port)
    {
        if (_app is not null)
            return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // Réglages échangés en texte (« Coordinates », « Map »...) : lisibles et indépendants de l'ordre des valeurs.
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        var app = builder.Build();

        app.MapGet("/", (HttpContext context) => HandleQuery(context));
        app.MapPost("/", async (HttpContext context) => await HandlePostAsync(context));
        // Application Android : enrôlement avec le code de l'équipe, puis envoi périodique des positions.
        app.MapPost("/api/enroll", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<EnrollRequest>();
            return request is not null && Enroll(request) is { } response ? Results.Json(response) : Results.NotFound();
        });
        app.MapPost("/api/track", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<TrackRequest>();
            if (request is null || Authorize(request.Token) is not { } authorization)
                return Results.Unauthorized();

            foreach (var point in request.Positions.OrderBy(p => p.Time))
            {
                var position = new GeoPoint(point.Latitude, point.Longitude);
                if (position.IsValid)
                    FixReceived?.Invoke(new GpsFix(authorization.Team, position, point.Time, L.T("appli_android")));
            }

            return Results.Json(authorization);
        });

        app.MapGet("/api/message/photo", (string token, Guid id) =>
            MessagePhoto(token, id) is { } photo ? Results.File(photo, "image/jpeg") : Results.NotFound());

        app.MapGet("/api/map/image", (string token) =>
            MapImage(token) is { } image ? Results.File(image, "image/jpeg") : Results.NotFound());

        // Lecture des positions par un autre poste Airsoft Planner (suivi en direct sur plusieurs PC).
        app.MapGet("/api/positions", () => Results.Json(Positions()));

        // Saisie manuelle depuis un téléphone (sans application, sans géolocalisation du navigateur).
        app.MapGet("/saisie", () => Results.Content(EntryPage(null), "text/html; charset=utf-8"));
        app.MapPost("/saisie", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var team = form["equipe"].ToString();
            var text = form["coordonnees"].ToString();
            if (!TeamNames().Contains(team) || !AirsoftPlanner.Core.Geo.Coordinates.TryParse(text, out var point))
                return Results.Content(EntryPage(L.T("equipe_ou_coordonnees_non_reconnues_ex_31u_47743")), "text/html; charset=utf-8");

            FixReceived?.Invoke(new GpsFix(team, point, DateTimeOffset.Now, L.T("saisie_web")));
            return Results.Content(EntryPage(L.F("position_de_x_enregistree_a_x", WebUtility.HtmlEncode(team), DateTime.Now)), "text/html; charset=utf-8");
        });

        app.MapPost("/api/positions", async (HttpContext context) =>
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            foreach (var fix in GpsParsers.FromGenericJson(body))
                FixReceived?.Invoke(fix);
            return Results.Ok();
        });

        await app.StartAsync();
        _app = app;
        Port = port;

        // Recherche par les téléphones sur le Wi-Fi (IP du PC changée depuis l'enrôlement) : facultative.
        try
        {
            _discovery = new DiscoveryResponder(phone =>
            {
                var (name, id) = OperationInfo();
                return AddressFor(phone, port) is { } address ? new DiscoveryReply(address, name, id) : null;
            });
        }
        catch (SocketException)
        {
            _discovery = null; // port UDP occupé (autre logiciel ouvert) : le reste fonctionne
        }
    }

    public async Task StopAsync()
    {
        if (_app is null)
            return;

        _discovery?.Dispose();
        _discovery = null;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private string EntryPage(string? message)
    {
        var options = string.Concat(TeamNames().Select(t => $"<option>{WebUtility.HtmlEncode(t)}</option>"));
        static string H(string key) => WebUtility.HtmlEncode(L.T(key));
        return $$"""
            <!doctype html><html lang="{{L.Code}}"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>{{H("web_titre_saisie")}}</title>
            <body style="font-family:sans-serif;padding:16px;max-width:480px;margin:auto">
            <h2>{{H("web_envoyer_position")}}</h2>
            <p style="font-weight:bold">{{message}}</p>
            <form method="post" action="/saisie">
              <p><label>{{H("equipe")}}<br><select name="equipe" style="font-size:1.2em;width:100%">{{options}}</select></label></p>
              <p><label>{{H("web_coordonnees_utm_ou_degres")}}<br>
                <input name="coordonnees" style="font-size:1.2em;width:100%" placeholder="31U 477439 5361677" autocomplete="off"></label></p>
              <p><button style="font-size:1.2em;width:100%;padding:12px">{{H("envoyer")}}</button></p>
            </form>
            </body></html>
            """;
    }

    private IResult HandleQuery(HttpContext context)
    {
        var query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        if (GpsParsers.FromOsmAndQuery(query) is { } fix)
        {
            FixReceived?.Invoke(fix);
            return Results.Ok();
        }

        // Page d'accueil : vérifier depuis un téléphone que le serveur est joignable.
        return Results.Content($$"""
            <!doctype html><html lang="{{L.Code}}"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Airsoft Planner</title><body style="font-family:sans-serif;padding:16px">
            <h2>{{WebUtility.HtmlEncode(L.T("web_reception_gps"))}}</h2>
            <p>{{WebUtility.HtmlEncode(L.T("web_serveur_joignable"))}}</p>
            </body></html>
            """, "text/html; charset=utf-8");
    }

    private async Task<IResult> HandlePostAsync(HttpContext context)
    {
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        GpsFix? fix = null;
        if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('['))
        {
            try
            {
                fix = GpsParsers.FromTraccarClientJson(body);
                if (fix is null)
                    foreach (var generic in GpsParsers.FromGenericJson(body))
                        FixReceived?.Invoke(generic);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }
        }
        else
        {
            var values = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2))
                values[WebUtility.UrlDecode(pair[0])] = WebUtility.UrlDecode(pair[1]);
            fix = GpsParsers.FromOsmAndQuery(values);
        }

        if (fix is not null)
            FixReceived?.Invoke(fix);
        return Results.Ok();
    }
}

/// <summary>Position publiée par le PC de l'OP pour les autres postes.</summary>
public record PublishedPosition(string Team, double Latitude, double Longitude, DateTimeOffset Time, string Source);

/// <summary>
/// Second poste : récupère périodiquement les positions collectées par le PC qui mène l'OP
/// (son serveur local, <c>GET /api/positions</c>), pour suivre l'OP en direct sur plusieurs PC.
/// </summary>
public sealed class OperationServerSource : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? _polling;

    public event Action<GpsFix>? FixReceived;

    public event Action<string>? Error;

    public bool IsConnected => _polling is not null;

    public async Task ConnectAsync(string baseUrl, TimeSpan interval)
    {
        Disconnect();
        var url = baseUrl.TrimEnd('/') + "/api/positions";
        (await _http.GetAsync(url)).EnsureSuccessStatusCode(); // vérifie tout de suite que le PC de l'OP répond
        _polling = new CancellationTokenSource();
        var token = _polling.Token;
        _ = Task.Run(async () =>
        {
            var last = new Dictionary<string, DateTimeOffset>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var positions = await _http.GetFromJsonAsync<List<PublishedPosition>>(url, token) ?? [];
                    foreach (var p in positions)
                    {
                        if (last.TryGetValue(p.Team, out var previous) && previous >= p.Time)
                            continue;
                        last[p.Team] = p.Time;
                        FixReceived?.Invoke(new GpsFix(p.Team, new GeoPoint(p.Latitude, p.Longitude), p.Time, L.F("pc_de_l_op_x", p.Source)));
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
                {
                    Error?.Invoke(ex.Message);
                }

                try
                {
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Disconnect()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    public void Dispose()
    {
        Disconnect();
        _http.Dispose();
    }
}

/// <summary>
/// Positions des nœuds Meshtastic via MQTT : les passerelles (nœud relié au Wi-Fi, ou un PC/Raspberry Pi)
/// publient les messages en JSON sur un broker (Mosquitto...) que le logiciel écoute.
/// </summary>
public sealed class MeshtasticMqttSource : IAsyncDisposable
{
    private IMqttClient? _client;

    public event Action<GpsFix>? FixReceived;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(string host, int port, string topic, string user, string password, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            try
            {
                var json = e.ApplicationMessage.ConvertPayloadToString();
                if (GpsParsers.FromMeshtasticJson(json) is { } fix)
                    FixReceived?.Invoke(fix);
            }
            catch (JsonException)
            {
                // Message chiffré ou non JSON (canal protobuf) : ignoré.
            }

            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder().WithTcpServer(host, port).WithClientId($"airsoft-planner-{Environment.MachineName}");
        if (user.Length > 0)
            options = options.WithCredentials(user, password);
        await client.ConnectAsync(options.Build(), cancellationToken);
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(topic).Build(), cancellationToken);
        _client = client;
    }

    public async Task DisconnectAsync()
    {
        if (_client is null)
            return;

        if (_client.IsConnected)
            await _client.DisconnectAsync();
        _client.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}

/// <summary>
/// Webservice externe : serveur Traccar (auto-hébergé ou en ligne). Les positions de ses appareils sont
/// récupérées périodiquement ; l'identifiant utilisé est l'« identifiant unique » de l'appareil dans Traccar.
/// </summary>
public sealed class TraccarServerSource : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private CancellationTokenSource? _polling;

    public event Action<GpsFix>? FixReceived;

    public event Action<string>? Error;

    public bool IsConnected => _polling is not null;

    public async Task ConnectAsync(string baseUrl, string user, string password, TimeSpan interval)
    {
        Disconnect();
        var root = baseUrl.TrimEnd('/');
        _http.DefaultRequestHeaders.Authorization = user.Length == 0
            ? new AuthenticationHeaderValue("Bearer", password)
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

        // Premier appel immédiat : une erreur d'adresse ou d'identifiants est signalée tout de suite.
        var devices = await LoadDevicesAsync(root, CancellationToken.None);
        _polling = new CancellationTokenSource();
        var token = _polling.Token;
        _ = Task.Run(async () =>
        {
            var lastFix = new Dictionary<long, DateTimeOffset>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    devices = await LoadDevicesAsync(root, token);
                    using var response = await _http.GetAsync($"{root}/api/positions", token);
                    response.EnsureSuccessStatusCode();
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    foreach (var position in document.RootElement.EnumerateArray())
                    {
                        var deviceId = position.GetProperty("deviceId").GetInt64();
                        var time = position.GetProperty("fixTime").GetDateTimeOffset();
                        if (lastFix.TryGetValue(deviceId, out var previous) && previous >= time || !devices.TryGetValue(deviceId, out var uniqueId))
                            continue;

                        lastFix[deviceId] = time;
                        var point = new GeoPoint(position.GetProperty("latitude").GetDouble(), position.GetProperty("longitude").GetDouble());
                        FixReceived?.Invoke(new GpsFix(uniqueId, point, time, "Traccar"));
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
                {
                    Error?.Invoke(ex.Message);
                }

                try
                {
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Disconnect()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    public void Dispose()
    {
        Disconnect();
        _http.Dispose();
    }

    private async Task<Dictionary<long, string>> LoadDevicesAsync(string root, CancellationToken token)
    {
        using var response = await _http.GetAsync($"{root}/api/devices", token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.EnumerateArray()
            .ToDictionary(d => d.GetProperty("id").GetInt64(), d => d.GetProperty("uniqueId").GetString() ?? "");
    }
}
