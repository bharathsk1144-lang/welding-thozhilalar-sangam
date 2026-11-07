using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed class DistrictService
{
    private readonly MongoContext _context;

    public DistrictService(MongoContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<District>> GetAllAsync()
    {
        return await _context.Districts.Find(_ => true).SortBy(d => d.Name).ToListAsync();
    }

    public async Task<District?> GetByNameAsync(string name)
    {
        return await _context.Districts.Find(d => d.Name == name.Trim()).FirstOrDefaultAsync();
    }

    public async Task<District> CreateAsync(District district)
    {
        district.Name = district.Name.Trim();
        district.WhatsAppGroupLink = district.WhatsAppGroupLink.Trim();
        district.CreatedAt = DateTime.UtcNow;
        district.UpdatedAt = DateTime.UtcNow;
        await _context.Districts.InsertOneAsync(district);
        return district;
    }

    public async Task<bool> UpdateAsync(string id, string name, string whatsAppGroupLink)
    {
        var update = Builders<District>.Update
            .Set(d => d.Name, name.Trim())
            .Set(d => d.WhatsAppGroupLink, whatsAppGroupLink.Trim())
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        var result = await _context.Districts.UpdateOneAsync(d => d.Id == id, update);
        return result.MatchedCount == 1;
    }

    public async Task SeedTamilNaduDistrictsAsync()
    {
        if (await _context.Districts.CountDocumentsAsync(_ => true) > 0)
        {
            return;
        }

        var districts = TamilNaduDistrictNames.Select(name => new District
        {
            Name = name,
            WhatsAppGroupLink = "https://chat.whatsapp.com/replace-with-" + name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()
        });

        await _context.Districts.InsertManyAsync(districts);
    }

    private static readonly string[] TamilNaduDistrictNames =
    [
        "Ariyalur",
        "Chengalpattu",
        "Chennai",
        "Coimbatore",
        "Cuddalore",
        "Dharmapuri",
        "Dindigul",
        "Erode",
        "Kallakurichi",
        "Kanchipuram",
        "Kanniyakumari",
        "Karur",
        "Krishnagiri",
        "Madurai",
        "Mayiladuthurai",
        "Nagapattinam",
        "Namakkal",
        "Nilgiris",
        "Perambalur",
        "Pudukkottai",
        "Ramanathapuram",
        "Ranipet",
        "Salem",
        "Sivaganga",
        "Tenkasi",
        "Thanjavur",
        "Theni",
        "Thoothukudi",
        "Tiruchirappalli",
        "Tirunelveli",
        "Tirupathur",
        "Tiruppur",
        "Tiruvallur",
        "Tiruvannamalai",
        "Tiruvarur",
        "Vellore",
        "Viluppuram",
        "Virudhunagar"
    ];
}
