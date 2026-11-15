using System.Globalization;

namespace WeldingThozhilalarSangam.Api.Services;

public static class PostingPolicy
{
    public const string Default = "MEMBER";
    public static readonly string[] Suggested = [Default, "ஊர் தலைவர்", "பொதுச் செயலாளர்", "துணைத் தலைவர்", "பொருளாளர்", "மாவட்ட ஒருங்கிணைப்பாளர்"];

    public static bool TryNormalize(string? value, out string posting)
    {
        posting = (value ?? Default).Trim();
        if (posting.Length is < 1 or > 60 || posting.Any(char.IsControl)) return false;
        return posting.All(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ||
            CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark ||
            c is '-' or '/' or '&' or '(' or ')' or '.');
    }

    public static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? Default : value;
}
