namespace WeldingThozhilalarSangam.Api.Services;

public static class CardValidity
{
    public static DateTime Expiry(DateTime registeredAt) => registeredAt.AddYears(3);
}
