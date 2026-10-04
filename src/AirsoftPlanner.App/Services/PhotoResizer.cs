using System;
using SkiaSharp;

namespace AirsoftPlanner.App.Services;

/// <summary>Photos jointes aux messages : réduites pour rester légères dans le fichier d'OP et sur le Wi-Fi du terrain.</summary>
public static class PhotoResizer
{
    /// <returns>JPEG d'au plus <paramref name="maxSide"/> pixels de côté.</returns>
    public static byte[] ToJpeg(byte[] source, int maxSide = 1600, int quality = 80)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(source)) ?? throw new InvalidOperationException("Image illisible.");
        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("Image illisible.");
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(oriented.Width, oriented.Height));
        using var resized = oriented.Resize(new SKImageInfo((int)(oriented.Width * scale), (int)(oriented.Height * scale)),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(resized);
        return image.Encode(SKEncodedImageFormat.Jpeg, quality).ToArray();
    }

    // Photos de téléphone : l'orientation est souvent indiquée dans les métadonnées plutôt qu'appliquée aux pixels.
    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var rotate = origin switch
        {
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (rotate == 0)
            return bitmap.Copy();

        var swap = rotate != 180;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(result);
        canvas.Translate(result.Width / 2f, result.Height / 2f);
        canvas.RotateDegrees(rotate);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return result;
    }
}
