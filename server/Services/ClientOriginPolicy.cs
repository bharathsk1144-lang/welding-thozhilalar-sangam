using System.Net;
using System.Net.Sockets;

namespace WeldingThozhilalarSangam.Api.Services;

public static class ClientOriginPolicy
{
    public static bool IsDevelopmentOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.Port is not (5173 or 5174 or 5175) || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "") return false;
        if (uri.Host == "localhost") return true;
        if (!IPAddress.TryParse(uri.Host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }
}
