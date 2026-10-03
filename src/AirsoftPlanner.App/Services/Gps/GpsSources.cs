using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace AirsoftPlanner.App.Services.Gps;

/// <summary>
/// Serveur intégré sur le réseau local du terrain : reçoit les positions des smartphones (Traccar Client,
/// OsmAnd, GPSLogger : protocole OsmAnd) et de tout système capable d'envoyer du JSON. Aucun accès Internet n'est nécessaire.
/// </summary>
public sealed class LocalGpsServer : IAsyncDisposable
{
    private WebApplication? _app;

    public event Action<GpsFix>? FixReceived;

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

    public async Task StartAsync(int port)
    {
        if (_app is not null)
            return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        var app = builder.Build();

        app.MapGet("/", (HttpContext context) => HandleQuery(context));
        app.MapPost("/", async (HttpContext context) => await HandlePostAsync(context));
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
    }

    public async Task StopAsync()
    {
        if (_app is null)
            return;

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private IResult HandleQuery(HttpContext context)
    {
        var query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        if (GpsParsers.FromOsmAndQuery(query) is { } fix)
        {
            FixReceived?.Invoke(fix);
            return Results.Ok();
        }

        // Page d'accueil : vérifier depuis un téléphone que le serveur est joignable.
        return Results.Content("""
            <!doctype html><html lang="fr"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Airsoft Planner</title><body style="font-family:sans-serif;padding:16px">
            <h2>Airsoft Planner — réception GPS</h2>
            <p>Le serveur est joignable. Dans <b>Traccar Client</b>, indiquez cette adresse comme
            « adresse du serveur » et l'identifiant GPS de votre équipe comme « identifiant de l'appareil ».</p>
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
