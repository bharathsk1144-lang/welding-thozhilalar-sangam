using System.Text.RegularExpressions;

namespace WeldingThozhilalarSangam.Api.Services;

public static class MobileNumber
{
    public const string DuplicateMessage = "This mobile number is already registered. Please login to view or download your ID card.";
    public const string InvalidMessage = "Enter a valid Indian mobile number (10 digits, optionally prefixed by +91 or 91).";

    public static bool TryNormalize(string? value, out string mobile)
    {
        mobile = (value ?? string.Empty).Trim();
        if (mobile.StartsWith("+91", StringComparison.Ordinal)) mobile = mobile[3..];
        else if (mobile.Length == 12 && mobile.StartsWith("91", StringComparison.Ordinal)) mobile = mobile[2..];
        return Regex.IsMatch(mobile, "^[6-9][0-9]{9}$", RegexOptions.CultureInvariant);
    }

    public static string Normalize(string? value) => TryNormalize(value, out var mobile)
        ? mobile : throw new ArgumentException(InvalidMessage);
}

public sealed class DuplicateMobileException : Exception
{
    public DuplicateMobileException() : base(MobileNumber.DuplicateMessage) { }
}

public sealed class AmbiguousMobileException : Exception
{
    public AmbiguousMobileException() : base("This account needs support review before login or password reset.") { }
}
