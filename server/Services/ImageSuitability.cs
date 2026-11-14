using SkiaSharp;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed record ImageAssessment(string Status, string Reason);

public static class ImageSuitability
{
    public static async Task<(string? Error, ImageAssessment? Assessment)> CheckAsync(IFormFile file, bool passport, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            using var metadataStream = new MemoryStream(bytes);
            using var codec = SKCodec.Create(metadataStream);
            if (codec is null) return ("Image is damaged or cannot be decoded.", null);
            int width = codec.Info.Width, height = codec.Info.Height;
            if (width < 320 || height < 240 || width > 10000 || height > 10000 || (long)width * height > 40_000_000)
                return ("Image dimensions are outside the supported range.", null);
            using var image = SKBitmap.Decode(bytes);
            if (image is null) return ("Image is damaged or cannot be decoded.", null);
            double ratio = (double)width / height;
            if (passport && (ratio < 0.45 || ratio > 1.15))
                return ("Passport photo must show a portrait with head and shoulders. Choose another photo or crop it.", null);
            if (!passport && (ratio < 0.4 || ratio > 3.0))
                return ("Aadhaar image is severely cropped or has an unsuitable shape. Choose a clearer image.", null);

            // Sample pixels without retaining the document or any extracted text.
            double sum = 0, sumSquares = 0, edges = 0;
            int samples = 0;
            for (int y = 8; y < height - 8; y += Math.Max(1, height / 64))
            for (int x = 8; x < width - 8; x += Math.Max(1, width / 64))
            {
                var pixel = image.GetPixel(x, y);
                var right = image.GetPixel(Math.Min(x + 5, width - 1), y);
                var down = image.GetPixel(x, Math.Min(y + 5, height - 1));
                double light = (pixel.Red + pixel.Green + pixel.Blue) / 3.0;
                sum += light; sumSquares += light * light;
                edges += Math.Abs(light - (right.Red + right.Green + right.Blue) / 3.0)
                    + Math.Abs(light - (down.Red + down.Green + down.Blue) / 3.0);
                samples++;
            }
            double variance = sumSquares / samples - Math.Pow(sum / samples, 2);
            double detail = edges / samples;
            if (variance < 15 || detail < 1.5)
                return ("Image appears blank or too blurred. Upload a readable image.", null);
            if (passport)
                return (null, new ImageAssessment("Needs review", ratio > 0.95 || detail < 8
                    ? "Check head-and-shoulders framing and clarity."
                    : "Automated checks cannot confirm one face or a plain background."));
            return (null, new ImageAssessment("Needs review", detail < 7
                ? "Check document readability and cropping."
                : "Document layout cannot reliably establish that this is Aadhaar; admin review required."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        {
            return ("Image is damaged or cannot be decoded.", null);
        }
    }
}
