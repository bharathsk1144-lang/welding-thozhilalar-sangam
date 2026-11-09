using Microsoft.Extensions.Options;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Data;

public sealed class MongoContext
{
    public MongoContext(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);

        Users = database.GetCollection<UserRegistration>("Users");
        Districts = database.GetCollection<District>("Districts");
        PrivateUploads = database.GetCollection<PrivateUploadRecord>("PrivateUploads");
    }

    public IMongoCollection<UserRegistration> Users { get; }
    public IMongoCollection<District> Districts { get; }
    public IMongoCollection<PrivateUploadRecord> PrivateUploads { get; }

    public async Task EnsureIndexesAsync()
    {
        var districtNameIndex = new CreateIndexModel<District>(
            Builders<District>.IndexKeys.Ascending(district => district.Name),
            new CreateIndexOptions { Unique = true, Name = "ux_districts_name" });

        var memberIdIndex = new CreateIndexModel<UserRegistration>(
            Builders<UserRegistration>.IndexKeys.Ascending(user => user.MemberId),
            new CreateIndexOptions { Name = "ix_users_member_id" });

        await MobileIndexMigration.EnsureReadyAsync(Users);
        await Users.Indexes.CreateOneAsync(memberIdIndex);
        await Districts.Indexes.CreateOneAsync(districtNameIndex);
    }
}
