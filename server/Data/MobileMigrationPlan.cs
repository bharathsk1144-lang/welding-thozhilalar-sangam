using System.Security.Cryptography;
using MongoDB.Bson;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Data;

public sealed record MobileMigrationRow(string UserId, string PhoneFormat, string CanonicalState, string Action);

public sealed record MobileAudit(int TotalUsers, string[][] DuplicateUserIds, string[] InvalidUserIds,
    string[] ConflictingCanonicalUserIds, int NeedsBackfill, string[] ExistingIndexes,
    MobileMigrationRow[] Records, string[] IndexConflicts, string[] CollectionBlockers,
    bool CanonicalIndexReady, string PlanFingerprint)
{
    public string Database { get; init; } = string.Empty;
    public string CollectionOptions { get; init; } = "{}";
    public DateTime AuditedAtUtc { get; init; } = DateTime.UtcNow;
    public bool CanMigrate => DuplicateUserIds.Length == 0 && InvalidUserIds.Length == 0 &&
        ConflictingCanonicalUserIds.Length == 0 && IndexConflicts.Length == 0 && CollectionBlockers.Length == 0;
    public int RecordsReady => CanMigrate ? NeedsBackfill : 0;
    public int AlreadyCanonical => TotalUsers - NeedsBackfill - InvalidUserIds.Length - ConflictingCanonicalUserIds.Length;
}

// Pure analysis. No database/file IO and no mutation of its inputs.
public static class MobileMigrationPlan
{
    public static MobileAudit Analyze(IReadOnlyList<BsonDocument> records, IReadOnlyList<BsonDocument> indexes,
        BsonDocument? collectionOptions = null)
    {
        collectionOptions ??= new BsonDocument();
        var valid = new List<(string Id, string Phone)>();
        var invalid = new List<string>();
        var conflicting = new List<string>();
        var rows = new List<MobileMigrationRow>();
        var pending = 0;
        foreach (var record in records)
        {
            var id = record.GetValue("_id", BsonNull.Value).ToString()!;
            var phone = record.GetValue("PhoneNumber", BsonNull.Value);
            var state = CanonicalState(record);
            if (!record.GetValue("_id", BsonNull.Value).IsObjectId || !phone.IsString ||
                !MobileNumber.TryNormalize(phone.AsString, out var canonical))
            {
                invalid.Add(id);
                rows.Add(new(id, "unsupported-or-invalid", state, "blocked"));
                continue;
            }
            valid.Add((id, canonical));
            var format = phone.AsString.Trim().StartsWith("+91", StringComparison.Ordinal) ? "+91" :
                phone.AsString.Trim().Length == 12 ? "91" : "10-digit";
            if (state is "missing" or "null" or "empty")
            {
                pending++;
                rows.Add(new(id, format, state, "backfill-canonical-only"));
            }
            else if (!record["CanonicalMobile"].IsString || record["CanonicalMobile"].AsString != canonical)
            {
                conflicting.Add(id);
                rows.Add(new(id, format, state, "blocked"));
            }
            else rows.Add(new(id, format, state, "no-op"));
        }
        var indexConflicts = indexes.Where(i =>
                (i.GetValue("name", "") == MobileIndexMigration.IndexName && !IsCanonicalIndex(i)) ||
                (IsCanonicalKey(i) && i.GetValue("name", "") != MobileIndexMigration.IndexName))
            .Select(i => $"Review existing index {i.GetValue("name", "<unnamed>")}; no automatic drop or conversion.").ToArray();
        var collectionBlockers = new List<string>();
        if (collectionOptions.TryGetValue("validator", out var validator) && validator.IsBsonDocument && validator.AsBsonDocument.ElementCount != 0)
            collectionBlockers.Add("Collection validator requires operator review before adding CanonicalMobile.");
        if (collectionOptions.TryGetValue("collation", out var collation) &&
            (!collation.IsBsonDocument || collation.AsBsonDocument.GetValue("locale", "") != "simple"))
            collectionBlockers.Add("Non-simple default collation requires operator review.");
        foreach (var option in new[] { "capped", "timeseries", "encryptedFields" })
            if (collectionOptions.Contains(option) && collectionOptions[option] != BsonBoolean.False)
                collectionBlockers.Add($"Unsupported collection option: {option}.");
        var identity = records.OrderBy(r => r.GetValue("_id", BsonNull.Value).ToString(), StringComparer.Ordinal)
            .Select(r => new BsonDocument(r.Elements.Where(e => e.Name is "_id" or "PhoneNumber" or "CanonicalMobile")
                .OrderBy(e => e.Name, StringComparer.Ordinal)));
        var snapshot = new BsonDocument {
            { "records", new BsonArray(identity) },
            { "indexes", new BsonArray(indexes.OrderBy(i => i.GetValue("name", "").ToString(), StringComparer.Ordinal)) },
            { "options", collectionOptions }
        };
        return new(records.Count, valid.GroupBy(v => v.Phone).Where(g => g.Count() > 1).Select(g => g.Select(v => v.Id).ToArray()).ToArray(),
            invalid.ToArray(), conflicting.ToArray(), pending,
            indexes.Select(i => i.ToJson()).ToArray(), rows.ToArray(), indexConflicts, collectionBlockers.ToArray(),
            indexes.Any(IsCanonicalIndex), Convert.ToHexString(SHA256.HashData(snapshot.ToBson()))) {
                CollectionOptions = collectionOptions.ToJson()
            };
    }

    public static string CanonicalState(BsonDocument record) => !record.Contains("CanonicalMobile") ? "missing" :
        record["CanonicalMobile"].IsBsonNull ? "null" :
        record["CanonicalMobile"].IsString && record["CanonicalMobile"].AsString.Length == 0 ? "empty" : "present";

    public static bool IsCanonicalKey(BsonDocument index) => index.TryGetValue("key", out var key) && key.IsBsonDocument &&
        key.AsBsonDocument.ElementCount == 1 && key.AsBsonDocument.GetValue("CanonicalMobile", 0) == 1;

    public static bool IsCanonicalIndex(BsonDocument index) => IsCanonicalKey(index) &&
        index.GetValue("name", "") == MobileIndexMigration.IndexName && index.GetValue("unique", false) == true &&
        index.GetValue("sparse", false) == false && !index.Contains("partialFilterExpression") &&
        index.GetValue("hidden", false) == false && !index.Contains("expireAfterSeconds") &&
        (!index.Contains("collation") || (index["collation"].IsBsonDocument &&
            index["collation"].AsBsonDocument.GetValue("locale", "") == "simple"));
}
