using System.Net;
using System.Net.Http.Json;
using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Mobile;

/// <summary>Échanges avec le serveur du PC de l'OP (Wi-Fi local du terrain).</summary>
internal static class ServerApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Le jeton n'est plus reconnu (révoqué par l'orga, ou autre OP).</summary>
    public sealed class RevokedException() : Exception("Ce téléphone n'est plus autorisé par l'orga.");

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
               ?? throw new HttpRequestException("Réponse vide du PC de l'OP.");
    }

    public static async Task<byte[]?> MapImageAsync(string server, string token)
    {
        using var response = await Http.GetAsync($"{Normalize(server)}/api/map/image?token={Uri.EscapeDataString(token)}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }
}
