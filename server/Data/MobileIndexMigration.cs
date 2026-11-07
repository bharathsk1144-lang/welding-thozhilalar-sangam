using MongoDB.Bson;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Data;

public static class MobileIndexMigration
{
    public const string IndexName = "ux_users_canonical_mobile";

    public static async Task<MobileAudit> AuditAsync(IMongoCollection<UserRegistration> users)
    {
        // Raw projected BSON reports unsupported types instead of failing typed deserialization.
        var raw = Raw(users);
        var records = await raw.Find(FilterDefinition<BsonDocument>.Empty)
            .Project(new BsonDocument { { "_id", 1 }, { "PhoneNumber", 1 }, { "CanonicalMobile", 1 } }).ToListAsync();
        var indexes = new List<BsonDocument>();
        try { indexes = await (await users.Indexes.ListAsync()).ToListAsync(); }
        catch (MongoCommandException ex) when (ex.Code == 26) { /* New, empty collection. */ }
        var collections = await (await users.Database.ListCollectionsAsync(new ListCollectionsOptions {
            Filter = new BsonDocument("name", users.CollectionNamespace.CollectionName) })).ToListAsync();
        var options = collections.SingleOrDefault()?.GetValue("options", new BsonDocument()).AsBsonDocument ?? new BsonDocument();
        return MobileMigrationPlan.Analyze(records, indexes, options) with { Database = users.Database.DatabaseNamespace.DatabaseName };
    }

    public static async Task ApplyAsync(IMongoCollection<UserRegistration> users)
    {
        var audit = await AuditAsync(users);
        if (!audit.CanMigrate) throw new InvalidOperationException("Mobile audit has blockers. No records were changed. Resolve via reviewed migration; never auto-merge or delete.");
        // Operator must stop all application writers for this migration. Never change PhoneNumber,
        // MemberId, passwords, document paths, or other member data.
        var records = await users.Find(FilterDefinition<UserRegistration>.Empty)
            .Project(u => new { u.Id, u.PhoneNumber, u.CanonicalMobile }).ToListAsync();
        foreach (var record in records.Where(u => string.IsNullOrEmpty(u.CanonicalMobile)))
        {
            var result = await users.WithWriteConcern(WriteConcern.WMajority).UpdateOneAsync(u => u.Id == record.Id && u.PhoneNumber == record.PhoneNumber &&
                (u.CanonicalMobile == null || u.CanonicalMobile == ""),
                Builders<UserRegistration>.Update.Set(u => u.CanonicalMobile, MobileNumber.Normalize(record.PhoneNumber)));
            if (result.MatchedCount != 1)
                throw new InvalidOperationException("Account changed during backfill. Keep writers stopped; re-audit before resuming.");
        }
        await EnsureReadyAsync(users);
    }

    public static async Task EnsureReadyAsync(IMongoCollection<UserRegistration> users)
    {
        var audit = await AuditAsync(users);
        if (!audit.CanMigrate || audit.NeedsBackfill != 0)
            throw new InvalidOperationException("Canonical mobile index blocked. Run --audit-mobile, review its report, then --migrate-mobile with all writers stopped. No automatic data repair is performed.");
        if (audit.CanonicalIndexReady) return;
        // Full (non-sparse, non-partial) unique index. All accounts must participate.
        await users.Indexes.CreateOneAsync(new CreateIndexModel<UserRegistration>(
            Builders<UserRegistration>.IndexKeys.Ascending(u => u.CanonicalMobile),
            new CreateIndexOptions { Name = IndexName, Unique = true, Collation = Collation.Simple }));
        var verified = await AuditAsync(users);
        if (!verified.CanMigrate || verified.NeedsBackfill != 0 || !verified.CanonicalIndexReady)
            throw new InvalidOperationException("Post-index verification failed. Keep writers stopped and review rollback manifest.");
    }

    private static IMongoCollection<BsonDocument> Raw(IMongoCollection<UserRegistration> users) =>
        users.Database.GetCollection<BsonDocument>(users.CollectionNamespace.CollectionName).WithWriteConcern(WriteConcern.WMajority);

    public static async Task ExecuteApprovedAsync(IMongoCollection<UserRegistration> users, string manifestPath,
        int expectedCount, string approvedFingerprint, bool rollback = false)
    {
        var raw = Raw(users);
        var audit = await AuditAsync(users);
        var records = await raw.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        if (records.Count != expectedCount || audit.TotalUsers != expectedCount)
            throw new InvalidOperationException("User count differs from approved count; no writes performed.");
        BsonDocument journal;
        if (File.Exists(manifestPath))
            journal = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(await File.ReadAllBytesAsync(manifestPath));
        else
        {
            if (rollback) throw new InvalidOperationException("Rollback requires the original migration manifest.");
            if (!audit.CanMigrate || audit.PlanFingerprint != approvedFingerprint)
                throw new InvalidOperationException("Dry-run plan is blocked or changed; no writes performed.");
            journal = MobileMigrationJournal.Create(users.CollectionNamespace.FullName, records, audit);
            // Never overwrite a previous manifest. Persist preimages before the first database write.
            await using var file = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(journal.ToBson());
            file.Flush(flushToDisk: true);
        }
        MobileMigrationJournal.Validate(journal, users.CollectionNamespace.FullName, records, audit, approvedFingerprint);

        if (rollback && !journal["IndexExisted"].AsBoolean && audit.CanonicalIndexReady)
            await raw.Indexes.DropOneAsync(IndexName);

        foreach (var entry in journal["Entries"].AsBsonArray.Select(e => e.AsBsonDocument))
        {
            var current = records.Single(r => r["_id"] == entry["_id"]);
            var update = MobileMigrationJournal.Change(entry, current, rollback);
            if (update is null) continue;
            var result = await raw.UpdateOneAsync(MobileMigrationJournal.Match(current), update);
            if (result.MatchedCount != 1) throw new InvalidOperationException("Account changed during migration. Stop and review the retained manifest.");
        }
        if (!rollback) await EnsureReadyAsync(users);
        var after = await raw.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        var afterAudit = await AuditAsync(users);
        MobileMigrationJournal.Validate(journal, users.CollectionNamespace.FullName, after, afterAudit, approvedFingerprint);
        foreach (var entry in journal["Entries"].AsBsonArray.Select(e => e.AsBsonDocument))
            if (MobileMigrationJournal.Change(entry, after.Single(r => r["_id"] == entry["_id"]), rollback) != null)
                throw new InvalidOperationException("Final field verification failed; keep writers stopped.");
    }
}
