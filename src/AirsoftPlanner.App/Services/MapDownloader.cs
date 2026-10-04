using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using SkiaSharp;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>
/// Télécharge les tuiles d'une zone et les assemble en une seule image, stockée ensuite
/// dans le fichier d'OP pour un usage hors ligne.
/// </summary>
public class MapDownloader
{
    /// <summary>Limite de taille de l'image assemblée : environ 2,5 km de côté à 0,4 m par pixel, ~150 Mo une fois décodée.</summary>
    public const int MaxImageSide = 6144;

    private const int ParallelDownloads = 4;

    private static readonly HttpClient Http = CreateClient();

    public async Task<MapLayer> DownloadAsync(MapSource source, TilePlan plan, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var surface = SKSurface.Create(new SKImageInfo(plan.PixelWidth, plan.PixelHeight))
            ?? throw new InvalidOperationException(L.T("image_trop_grande_pour_etre_assemblee"));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xE0, 0xE0, 0xE0));

        var completed = 0;
        var missing = 0;
        var canvasLock = new object();
        await Parallel.ForEachAsync(plan.Tiles(),
            new ParallelOptions { MaxDegreeOfParallelism = ParallelDownloads, CancellationToken = cancellationToken },
            async (tile, token) =>
            {
                var bytes = await DownloadTileAsync(source.TileUrl(plan.Zoom, tile.X, tile.Y), token);
                using var image = bytes is null ? null : SKImage.FromEncodedData(bytes);
                lock (canvasLock)
                {
                    if (image is null)
                        missing++;
                    else
                        canvas.DrawImage(image, (tile.X - plan.MinX) * TileGrid.TileSize, (tile.Y - plan.MinY) * TileGrid.TileSize);
                }

                progress?.Report((double)Interlocked.Increment(ref completed) / plan.TileCount);
            });

        if (missing == plan.TileCount)
            throw new HttpRequestException(L.T("aucune_tuile_n_a_pu_etre_telechargee_pour_cette"));

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 88);
        return new MapLayer
        {
            Name = source.Name,
            Attribution = source.Attribution,
            Image = encoded.ToArray(),
            Bounds = plan.Bounds,
        };
    }

    /// <summary>Renvoie null pour une tuile absente (hors couverture), lève une exception pour une erreur réseau.</summary>
    private static async Task<byte[]?> DownloadTileAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                return null;
            if (attempt == 3 || (int)response.StatusCode is >= 400 and < 500 and not 429)
                response.EnsureSuccessStatusCode();

            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var version = typeof(MapDownloader).Assembly.GetName().Version?.ToString(3) ?? "0.1";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AirsoftPlanner/{version}");
        return client;
    }
}
