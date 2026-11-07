using System.Text.RegularExpressions;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public static partial class MemberIdFormat
{
    public static bool IsValidOrganizationNumber(string? value)
    {
        var number = value?.Trim() ?? string.Empty;
        return number.Length == 0 || (number.Length <= 48 && OrganizationNumberPattern().IsMatch(number));
    }

    public static string Generate(UserRegistration user, DateTime? issuedAt = null)
    {
        // Never reformat an already issued ID, even after organization settings change.
        if (!string.IsNullOrWhiteSpace(user.MemberId)) return user.MemberId;
        var prefix = (issuedAt ?? DateTime.UtcNow).Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var suffix = string.IsNullOrWhiteSpace(user.Id) || user.Id.Length < 6
            ? Guid.NewGuid().ToString("N")[..6] : user.Id[^6..];
        return $"WTS-{prefix}-{suffix.ToUpperInvariant()}";
    }

    public static bool TryGetSuffix(string? storedId, out string suffix)
    {
        suffix = string.Empty;
        if (storedId is null || !storedId.StartsWith("WTS-", StringComparison.Ordinal)) return false;
        var delimiter = storedId.LastIndexOf('-');
        if (delimiter <= 4 || delimiter == storedId.Length - 1 ||
            !IsValidOrganizationNumber(storedId[4..delimiter])) return false;
        suffix = storedId[(delimiter + 1)..];
        return suffix.Length <= 64 && suffix.All(char.IsAsciiLetterOrDigit);
    }

    public static string Display(string? storedId, string? organizationNumber)
    {
        if (!IsValidOrganizationNumber(organizationNumber)) throw new ArgumentException("Invalid official organization number.");
        return string.IsNullOrWhiteSpace(organizationNumber) || !TryGetSuffix(storedId, out var suffix)
            ? storedId ?? string.Empty : $"WTS-{organizationNumber.Trim()}-{suffix}";
    }

    public static bool TryParseDisplay(string id, string? organizationNumber, out string suffix)
    {
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(organizationNumber) || !IsValidOrganizationNumber(organizationNumber)) return false;
        var prefix = $"WTS-{organizationNumber.Trim()}-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        suffix = id[prefix.Length..];
        return suffix.Length is > 0 and <= 64 && suffix.All(char.IsAsciiLetterOrDigit);
    }

    [GeneratedRegex("^[A-Za-z0-9]+(?:[-/][A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationNumberPattern();
}

public sealed class AmbiguousMemberIdException : Exception { }
