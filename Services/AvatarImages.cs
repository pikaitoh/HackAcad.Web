using SkiaSharp;

namespace HackAcad.Web.Services;

public static class AvatarImages
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public static async Task<byte[]> ReadAsync(IFormFile file)
    {
        if (file.Length is <= 0 or > MaxBytes)
            throw new ArgumentException("Choose a JPG, PNG or WebP image up to 2 MB.");
        using var stream = file.OpenReadStream();
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        using var data = SKData.CreateCopy(bytes.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new ArgumentException("Choose a valid JPG, PNG or WebP image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 4096 || info.Height > 4096)
            throw new ArgumentException("Choose an image no larger than 4,096 × 4,096 pixels.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException("This image could not be read. Choose another image.");
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Translate(128, 128);
        canvas.Scale(256f / Math.Min(info.Width, info.Height));
        // Apply the camera's EXIF orientation around the image centre before square cropping.
        switch (codec.EncodedOrigin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.RotateDegrees(270); break;
        }
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, new SKRect(-info.Width / 2f, -info.Height / 2f, info.Width / 2f, info.Height / 2f),
            new SKSamplingOptions(SKFilterMode.Linear));
        // Re-encode pixels, remove metadata and discard the original filename/content.
        using var output = surface.Snapshot();
        using var encoded = output.Encode(SKEncodedImageFormat.Jpeg, 85);
        return encoded.ToArray();
    }
}
