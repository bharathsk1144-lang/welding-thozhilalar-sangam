using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

// Keep existing references valid without serving private documents as static files.
[ApiController]
[Route("uploads")]
[Authorize(Policy = "MemberOrAdmin")]
public sealed class UploadsController(MongoContext mongo, PrivateUploadStorage uploads) : ControllerBase
{
    [HttpGet("{filename}")]
    public async Task<IActionResult> Read(string filename)
    {
        if (filename != Path.GetFileName(filename) || filename.Contains('\\') || filename.Contains('/') || filename.Contains(':'))
            return NotFound();
        var reference = $"/uploads/{filename}";
        var filter = Builders<Models.UserRegistration>.Filter;
        // Resolve ownership from the stored record, never from a filename or client userId.
        var privateDocument = filter.Or(filter.Eq(u => u.AadhaarPhotoPath, reference), filter.Eq(u => u.PaymentProofUrl, reference));
        if (User.IsInRole("Admin"))
        {
            if (!await mongo.Users.Find(filter.Or(filter.Eq(u => u.ProfilePhotoPath, reference), privateDocument)).AnyAsync())
                return NotFound();
        }
        else
        {
            // A legacy file referenced by multiple records must never expose a private document.
            if (await mongo.Users.Find(privateDocument).AnyAsync() || !await mongo.Users.Find(filter.And(
                filter.Eq(u => u.Id, User.FindFirstValue(ClaimTypes.NameIdentifier)),
                filter.Eq(u => u.ProfilePhotoPath, reference))).AnyAsync()) return Forbid();
        }
        var root = uploads.RootPath;
        var path = Path.GetFullPath(Path.Combine(root, filename));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
            return NotFound();
        new FileExtensionContentTypeProvider().TryGetContentType(path, out var contentType);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, contentType ?? "application/octet-stream");
    }
}
