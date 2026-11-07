using System.Security.Cryptography;
using MongoDB.Bson;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Data;

// Private local BSON manifest. Contains canonical phones, but no passwords or document contents.
// SHA256 over every field except CanonicalMobile verifies preservation of all original user data.
public static class MobileMigrationJournal
{
    public static BsonDocument Create(string collectionNamespace, IReadOnlyList<BsonDocument> records, MobileAudit audit)
    {
        if (!audit.CanMigrate || records.Count != audit.TotalUsers) throw new InvalidOperationException("Blocked migration plan.");
        return new BsonDocument {
            { "Version", 1 }, { "Namespace", collectionNamespace }, { "PlanFingerprint", audit.PlanFingerprint },
            { "IndexExisted", audit.CanonicalIndexReady }, { "CreatedAtUtc", DateTime.UtcNow },
            { "Entries", new BsonArray(records.Select(r => new BsonDocument {
                { "_id", r["_id"] }, { "OriginalPresent", r.Contains("CanonicalMobile") },
                { "OriginalCanonical", r.GetValue("CanonicalMobile", BsonNull.Value) },
                { "Canonical", MobileNumber.Normalize(r["PhoneNumber"].AsString) },
                { "PreservedHash", PreservedHash(r) }
            })) }
        };
    }

    public static string PreservedHash(BsonDocument record)
    {
        var preserved = record.DeepClone().AsBsonDocument;
        preserved.Remove("CanonicalMobile");
        return Convert.ToHexString(SHA256.HashData(preserved.ToBson()));
    }

    public static void Validate(BsonDocument journal, string collectionNamespace, IReadOnlyList<BsonDocument> current,
        MobileAudit audit, string approvedFingerprint)
    {
        if (journal.GetValue("Version", 0) != 1 || journal["Namespace"] != collectionNamespace ||
            journal["PlanFingerprint"] != approvedFingerprint || !audit.CanMigrate)
            throw new InvalidOperationException("Manifest target, approval, or current audit is invalid; no further writes allowed.");
        var entries = journal["Entries"].AsBsonArray.Select(e => e.AsBsonDocument).ToList();
        if (current.Count != entries.Count || entries.Select(e => e["_id"]).Distinct().Count() != entries.Count)
            throw new InvalidOperationException("User set changed since approval. No further writes allowed.");
        var original = new List<BsonDocument>();
        foreach (var entry in entries)
        {
            var record = current.SingleOrDefault(r => r["_id"] == entry["_id"]);
            if (record is null || PreservedHash(record) != entry["PreservedHash"].AsString ||
                (!IsOriginal(entry, record) && record.GetValue("CanonicalMobile", BsonNull.Value) != entry["Canonical"]))
                throw new InvalidOperationException("A member record changed outside this migration. No further writes allowed.");
            var before = record.DeepClone().AsBsonDocument;
            RestoreOriginal(entry, before);
            original.Add(before);
        }
        var indexes = audit.ExistingIndexes.Select(BsonDocument.Parse).Where(i => journal["IndexExisted"].AsBoolean ||
            i.GetValue("name", "") != MobileIndexMigration.IndexName).ToList();
        var reconstructed = MobileMigrationPlan.Analyze(original, indexes, BsonDocument.Parse(audit.CollectionOptions));
        if (reconstructed.PlanFingerprint != approvedFingerprint)
            throw new InvalidOperationException("Indexes, collection options, or identities changed since approval. No further writes allowed.");
    }

    public static bool IsOriginal(BsonDocument entry, BsonDocument record) =>
        record.Contains("CanonicalMobile") == entry["OriginalPresent"].AsBoolean &&
        (!record.Contains("CanonicalMobile") || record["CanonicalMobile"] == entry["OriginalCanonical"]);

    public static void RestoreOriginal(BsonDocument entry, BsonDocument record)
    {
        if (entry["OriginalPresent"].AsBoolean) record["CanonicalMobile"] = entry["OriginalCanonical"].DeepClone();
        else record.Remove("CanonicalMobile");
    }

    public static BsonDocument? Change(BsonDocument entry, BsonDocument record, bool rollback = false)
    {
        if (rollback)
        {
            if (IsOriginal(entry, record)) return null;
            return entry["OriginalPresent"].AsBoolean
                ? new BsonDocument("$set", new BsonDocument("CanonicalMobile", entry["OriginalCanonical"]))
                : new BsonDocument("$unset", new BsonDocument("CanonicalMobile", ""));
        }
        return record.GetValue("CanonicalMobile", BsonNull.Value) == entry["Canonical"] ? null :
            new BsonDocument("$set", new BsonDocument("CanonicalMobile", entry["Canonical"]));
    }

    public static BsonDocument Match(BsonDocument record)
    {
        var filter = new BsonDocument { { "_id", record["_id"] }, { "PhoneNumber", record["PhoneNumber"] } };
        filter["CanonicalMobile"] = !record.Contains("CanonicalMobile") ? new BsonDocument("$exists", false) :
            record["CanonicalMobile"].IsBsonNull ? new BsonDocument("$type", 10) : record["CanonicalMobile"];
        return filter;
    }
}
