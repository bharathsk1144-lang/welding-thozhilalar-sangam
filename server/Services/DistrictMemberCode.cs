namespace WeldingThozhilalarSangam.Api.Services;

public static class DistrictMemberCode
{
    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Ariyalur"] = "ALR", ["Chengalpattu"] = "CGL", ["Chennai"] = "CHN",
        ["Coimbatore"] = "CBE", ["Cuddalore"] = "CDL", ["Dharmapuri"] = "DMP",
        ["Dindigul"] = "DGL", ["Erode"] = "ERD", ["Kallakurichi"] = "KLK",
        ["Kanchipuram"] = "KPM", ["Kanniyakumari"] = "KKM", ["Karur"] = "KRR",
        ["Krishnagiri"] = "KGI", ["Madurai"] = "MDU", ["Mayiladuthurai"] = "MYD",
        ["Nagapattinam"] = "NGP", ["Namakkal"] = "NMK", ["Nilgiris"] = "NLG",
        ["Perambalur"] = "PBL", ["Pudukkottai"] = "PDK", ["Ramanathapuram"] = "RMD",
        ["Ranipet"] = "RPT", ["Salem"] = "SLM", ["Sivaganga"] = "SVG",
        ["Tenkasi"] = "TKS", ["Thanjavur"] = "TJV", ["Theni"] = "TNI",
        ["Thoothukudi"] = "TUT", ["Tiruchirappalli"] = "TRI", ["Tirunelveli"] = "TNV",
        ["Tirupathur"] = "TPR", ["Tiruppur"] = "TPU", ["Tiruvallur"] = "TVL",
        ["Tiruvannamalai"] = "TVM", ["Tiruvarur"] = "TVR", ["Vellore"] = "VLR",
        ["Viluppuram"] = "VPM", ["Virudhunagar"] = "VNR"
    };

    public static IReadOnlyDictionary<string, string> All => Codes;
    public static bool TryGet(string? district, out string code) => Codes.TryGetValue(district?.Trim() ?? "", out code!);
}
