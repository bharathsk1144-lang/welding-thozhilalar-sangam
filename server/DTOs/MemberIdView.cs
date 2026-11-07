namespace WeldingThozhilalarSangam.Api.DTOs;

public interface IMemberIdView
{
    string MemberId { get; }
    string DisplayMemberId { get; set; }
    string? MemberIdDisplayWarning { get; set; }
}

public sealed record MemberIdView(string MemberId) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}
