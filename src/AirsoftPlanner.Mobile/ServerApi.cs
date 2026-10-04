using System.Net;
using System.Net.Http.Json;
using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Mobile;

/// <summary>Échanges avec le serveur du PC de l'OP (Wi-Fi local du terrain).</summary>
internal static class ServerApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Le jeton n'est plus reconnu (révoqué par l'orga, ou autre OP).</summary>
    public sealed class RevokedException() : Exception(L.T("ce_telephone_n_est_plus_autorise_par_l_orga"));

    public static string Normalize(string server)
    {
        var url = server.Trim().TrimEnd('/');
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "http://" + url;
    }

    /// <returns>La réponse, ou null si le code est refusé.</returns>
    public static async Task<EnrollResponse?> EnrollAsync(string server, string code, string deviceName)
    {
        using var response = await Http.PostAsJsonAsync($"{Normalize(server)}/api/enroll",
            new EnrollRequest(code, deviceName), ProtocolJson.Default.EnrollRequest);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ProtocolJson.Default.EnrollResponse);
    }

    public static async Task<TrackResponse> TrackAsync(string server, string token, IReadOnlyList<TrackPoint> positions)
    {
        using var response = await Http.PostAsJsonAsync($"{Normalize(server)}/api/track",
            new TrackRequest(token, positions), ProtocolJson.Default.TrackRequest);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new RevokedException();
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ProtocolJson.Default.TrackResponse)
               ?? throw new HttpRequestException(L.T("reponse_vide_du_pc_de_l_op"));
    }

    /// <summary>
    /// Cherche le PC de l'OP sur le Wi-Fi (son IP a changé, nom DynDNS injoignable sans Internet) :
    /// diffusion sur le réseau local, plus un appel direct à la dernière adresse connue (port du serveur changé).
    /// </summary>
    /// <returns>Nouvelle adresse du serveur, ou null si aucun PC de cette OP ne répond.</returns>
    public static async Task<string?> DiscoverAsync(Guid? operationId, string lastServer)
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, Discovery.Port) };
        try
        {
            var host = new Uri(Normalize(lastServer)).Host;
            var address = IPAddress.TryParse(host, out var ip) ? ip
                : (await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(3)))
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (address is not null)
                targets.Add(new IPEndPoint(address, Discovery.Port));
        }
        catch (Exception ex) when (ex is UriFormatException or System.Net.Sockets.SocketException or TimeoutException)
        {
            // Dernière adresse inutilisable : la diffusion suffit.
        }

        try
        {
            return (await Discovery.FindAsync(operationId, TimeSpan.FromSeconds(3), targets))?.Server;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null; // pas de réseau
        }
    }

    /// <summary>Message (texte, photo) vers l'orga.</summary>
    public static async Task ReportAsync(string server, ReportRequest request)
    {
        using var response = await Http.PostAsJsonAsync($"{Normalize(server)}/api/report", request, ProtocolJson.Default.ReportRequest);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new RevokedException();
        response.EnsureSuccessStatusCode();
    }

    public static async Task<byte[]?> MessagePhotoAsync(string server, string token, Guid id)
    {
        using var response = await Http.GetAsync($"{Normalize(server)}/api/message/photo?token={Uri.EscapeDataString(token)}&id={id}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }

    public static async Task<byte[]?> MapImageAsync(string server, string token)
    {
        using var response = await Http.GetAsync($"{Normalize(server)}/api/map/image?token={Uri.EscapeDataString(token)}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }
}
