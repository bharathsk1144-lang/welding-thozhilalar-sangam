namespace WeldingThozhilalarSangam.Api.DTOs;

public sealed record AdminLoginRequest(string Username, string Password);
public sealed record AdminLoginResponse(string Token, DateTime ExpiresAt);
public sealed record DistrictCountDto(string District, long Count);
public sealed record DashboardDto(long TotalUsers, long PaymentPendingUsers, IReadOnlyList<DistrictCountDto> DistrictCounts);
