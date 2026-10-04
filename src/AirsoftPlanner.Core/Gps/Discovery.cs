using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirsoftPlanner.Core.Gps;

/// <summary>Réponse du PC de l'OP à un appel de recherche sur le réseau local.</summary>
/// <param name="Server">Adresse actuelle du serveur (ex. http://192.168.1.20:5055).</param>
/// <param name="Operation">Nom de l'OP.</param>
/// <param name="OperationId">Identifiant de l'OP : le téléphone ne retient que le PC de son OP.</param>
public record DiscoveryReply(string Server, string Operation, Guid OperationId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DiscoveryReply))]
public partial class DiscoveryJson : JsonSerializerContext;

/// <summary>
/// Recherche du PC de l'OP sur le Wi-Fi du terrain, sans Internet : le téléphone diffuse un appel UDP,
/// le PC répond avec son adresse du moment. Utile quand l'IP du PC a changé depuis l'enrôlement
/// (adresse imprimée dans un package, autre box sur le terrain...).
/// </summary>
public static class Discovery
{
    /// <summary>Port UDP d'écoute du PC de l'OP.</summary>
    public const int Port = 5056;

    public const string Request = "AIRSOFTPLANNER-DISCOVER/1";

    /// <summary>
    /// Diffuse l'appel et attend les réponses. Renvoie le premier PC dont l'OP correspond
    /// (ou le premier qui répond si <paramref name="operationId"/> est null, avant l'enrôlement).
    /// </summary>
    /// <param name="targets">Adresses où envoyer l'appel ; par défaut la diffusion générale du réseau local.</param>
    public static async Task<DiscoveryReply?> FindAsync(Guid? operationId, TimeSpan timeout, IEnumerable<IPEndPoint>? targets = null,
        CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = Encoding.UTF8.GetBytes(Request);
        foreach (var target in targets ?? [new IPEndPoint(IPAddress.Broadcast, Port)])
        {
            try
            {
                await udp.SendAsync(request, target, cancellationToken);
            }
            catch (SocketException)
            {
                // Réseau sans diffusion possible : on essaie les autres cibles.
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        while (true)
        {
            try
            {
                var result = await udp.ReceiveAsync(deadline.Token);
                var reply = JsonSerializer.Deserialize(result.Buffer, DiscoveryJson.Default.DiscoveryReply);
                if (reply is not null && (operationId is null || reply.OperationId == operationId))
                    return reply;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (JsonException)
            {
                // Réponse d'un autre programme : ignorée.
            }
            catch (SocketException)
            {
                // Windows signale « port fermé » (ICMP) sur la socket quand personne n'écoute à une adresse :
                // on continue d'attendre jusqu'au délai, d'autres PC peuvent répondre.
                await Task.Delay(50, CancellationToken.None);
                if (deadline.IsCancellationRequested)
                    return null;
            }
        }
    }
}

/// <summary>Côté PC de l'OP : répond aux appels de recherche des téléphones.</summary>
public sealed class DiscoveryResponder : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Func<IPAddress, DiscoveryReply?> _reply;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="reply">Réponse à envoyer (adresse actuelle, OP) selon l'adresse du téléphone, recalculée à chaque appel.</param>
    public DiscoveryResponder(Func<IPAddress, DiscoveryReply?> reply, int port = Discovery.Port)
    {
        _reply = reply;
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        if (OperatingSystem.IsWindows())
            _udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null); // SIO_UDP_CONNRESET : ignorer les « port fermé »
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _ = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
        _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var request = await _udp.ReceiveAsync(_stop.Token);
                if (Encoding.UTF8.GetString(request.Buffer) != Discovery.Request || _reply(request.RemoteEndPoint.Address) is not { } reply)
                    continue;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, DiscoveryJson.Default.DiscoveryReply);
                await _udp.SendAsync(bytes, request.RemoteEndPoint, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                if (_stop.IsCancellationRequested)
                    return;
            }
        }
    }
}
