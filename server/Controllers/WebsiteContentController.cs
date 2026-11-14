using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/website-content")]
public sealed class WebsiteContentController(MongoContext context, IWebHostEnvironment environment, IPrivateUploadStorage uploads) : ControllerBase
{
    private string DefaultAbout => System.IO.File.ReadAllText(Path.Combine(environment.ContentRootPath, "Assets", "about-tamil.md"));
    private static bool ContainsHtml(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"<\s*/?\s*[A-Za-z][^>]*>");

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var content = await context.WebsiteContents.Find(x => x.Id == "website").FirstOrDefaultAsync() ?? new WebsiteContent();
        if (string.IsNullOrWhiteSpace(content.AboutMarkdown)) content.AboutMarkdown = DefaultAbout;
        return Ok(new { content.AboutMarkdown, content.AboutContact, content.Gallery, content.Products, content.Revision });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var content = await context.WebsiteContents.Find(x => x.Id == "website").FirstOrDefaultAsync();
        return Ok(content?.History ?? []);
    }

    public sealed record SaveRequest(string Section, int Revision, string? AboutMarkdown, string? AboutContact, List<WebsiteItem>? Items);

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveRequest request)
    {
        if (request.Section is not ("about" or "gallery" or "products")) return BadRequest(new { message = "Unknown section." });
        var current = await context.WebsiteContents.Find(x => x.Id == "website").FirstOrDefaultAsync() ?? new WebsiteContent();
        if (current.Revision != request.Revision) return Conflict(new { message = "Content changed elsewhere. Reload and review before saving." });
        if (string.IsNullOrWhiteSpace(current.AboutMarkdown)) current.AboutMarkdown = DefaultAbout;
        if (request.Section == "about")
        {
            var value = request.AboutMarkdown?.Trim() ?? "";
            var rules = System.Text.RegularExpressions.Regex.Matches(value, @"(?m)^### விதி\s+(\d+)").Select(x => int.Parse(x.Groups[1].Value)).ToHashSet();
            var parts = System.Text.RegularExpressions.Regex.Matches(value, @"(?m)^# பகுதி\s*[–-]\s*(\d+)").Select(x => int.Parse(x.Groups[1].Value)).ToHashSet();
            if (value.Length < 1000 || value.Length > 100_000 || ContainsHtml(value) ||
                !Enumerable.Range(1, 30).All(rules.Contains) || !Enumerable.Range(1, 8).All(parts.Contains) ||
                !value.Contains("# சங்கத்தின் இறுதி உறுதிமொழி") || !value.Contains("# முக்கியமான சில ஆலோசனைகள்"))
                return BadRequest(new { message = "Keep all eight constitution parts, thirty rules, final pledge and recommendations." });
            current.AboutMarkdown = value;
            if ((request.AboutContact?.Length ?? 0) > 2000 || ContainsHtml(request.AboutContact ?? string.Empty))
                return BadRequest(new { message = "Contact details must be plain text of up to 2000 characters." });
            current.AboutContact = request.AboutContact?.Trim() ?? string.Empty;
        }
        else
        {
            var items = request.Items ?? [];
            if (items.Count > 100 || items.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.Title) || item.Title.Trim().Length > 150 || ContainsHtml(item.Title) ||
                item.Description is null || item.Description.Length > 2000 || ContainsHtml(item.Description) ||
                string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 80 ||
                item.ImagePath is null || (item.ImagePath.Length > 0 && !item.ImagePath.StartsWith("/uploads/content-", StringComparison.Ordinal)) ||
                item.Order is < 0 or > 10000) || items.Select(x => x.Id).Distinct().Count() != items.Count)
                return BadRequest(new { message = "Invalid item title, description, image, or display order." });
            if (request.Section == "gallery") current.Gallery = items;
            else current.Products = items;
        }
        current.Revision++;
        current.History.Add(new WebsiteContentChange { Editor = User.FindFirstValue(ClaimTypes.Name) ?? "Admin", Timestamp = DateTime.UtcNow, Section = request.Section, Revision = current.Revision });
        var filter = Builders<WebsiteContent>.Filter.Eq(x => x.Id, "website") & Builders<WebsiteContent>.Filter.Eq(x => x.Revision, request.Revision);
        ReplaceOneResult result;
        try { result = await context.WebsiteContents.ReplaceOneAsync(filter, current, new ReplaceOptions { IsUpsert = request.Revision == 0 }); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return Conflict(new { message = "Content changed elsewhere. Reload and review before saving." }); }
        if (result.MatchedCount == 0 && result.UpsertedId is null) return Conflict(new { message = "Content changed elsewhere. Reload and review before saving." });
        return Ok(new { current.Revision });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("images")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? image)
    {
        var error = await UploadValidation.ValidateAsync(image, 5 * 1024 * 1024, "Website image");
        if (error is not null) return BadRequest(new { message = error });
        var (imageError, _) = await ImageSuitability.CheckAsync(image!, false, HttpContext.RequestAborted);
        if (imageError is not null) return BadRequest(new { message = imageError });
        return Ok(new { imagePath = await uploads.SaveAsync(image!, "content", HttpContext.RequestAborted) });
    }

    [HttpGet("images/{name}")]
    public async Task<IActionResult> PublicImage(string name)
    {
        if (!name.StartsWith("content-", StringComparison.Ordinal) || name.Contains('/') || name.Contains('\\')) return NotFound();
        var reference = "/uploads/" + name;
        var filter = Builders<WebsiteContent>.Filter.ElemMatch(x => x.Gallery, i => i.ImagePath == reference) |
            Builders<WebsiteContent>.Filter.ElemMatch(x => x.Products, i => i.ImagePath == reference);
        if (!await context.WebsiteContents.Find(filter).AnyAsync()) return NotFound();
        var upload = await uploads.ReadAsync(name, HttpContext.RequestAborted);
        if (upload is null || !upload.ContentType.StartsWith("image/")) return NotFound();
        Response.Headers.CacheControl = "public, max-age=300";
        return File(upload.Bytes, upload.ContentType);
    }
}
