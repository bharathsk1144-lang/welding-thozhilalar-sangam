namespace WeldingThozhilalarSangam.Api.Services;

public static class UploadValidation
{
    public static async Task<string?> ValidateAsync(IFormFile? file, long maxBytes, string label, bool required = true, bool allowPdf = false)
    {
        if (file is null) return required ? $"{label} is required." : null;
        if (file.Length == 0) return $"{label} is empty.";
        if (file.Length > maxBytes) return $"{label} exceeds the allowed file size.";
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expectedMime = extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".pdf" when allowPdf => "application/pdf", _ => null };
        if (expectedMime is null || !string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase))
            return $"{label} has an unsupported or mismatched file type.";
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = 0;
        while (read < header.Length)
        {
            var count = await stream.ReadAsync(header.AsMemory(read));
            if (count == 0) break;
            read += count;
        }
        var valid = expectedMime switch
        {
            "image/png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => read >= 4 && header[0] == 255 && header[1] == 216 && header[2] == 255,
            "image/webp" => read == 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            "application/pdf" => read >= 8 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            _ => false
        };
        return valid ? null : $"{label} contents do not match its file type.";
    }
}
