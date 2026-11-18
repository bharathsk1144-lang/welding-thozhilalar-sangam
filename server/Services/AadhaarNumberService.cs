using System.Security.Cryptography;
using System.Text;

namespace WeldingThozhilalarSangam.Api.Services;

// Format checks only. A valid checksum does not authenticate an identity.
public sealed class AadhaarNumberService
{
    private static readonly int[,] D = {
        {0,1,2,3,4,5,6,7,8,9}, {1,2,3,4,0,6,7,8,9,5},
        {2,3,4,0,1,7,8,9,5,6}, {3,4,0,1,2,8,9,5,6,7},
        {4,0,1,2,3,9,5,6,7,8}, {5,9,8,7,6,0,4,3,2,1},
        {6,5,9,8,7,1,0,4,3,2}, {7,6,5,9,8,2,1,0,4,3},
        {8,7,6,5,9,3,2,1,0,4}, {9,8,7,6,5,4,3,2,1,0}
    };
    private static readonly int[,] P = {
        {0,1,2,3,4,5,6,7,8,9}, {1,5,7,6,2,8,3,0,9,4},
        {5,8,0,3,7,9,6,1,4,2}, {8,9,1,6,0,4,3,5,2,7},
        {9,4,5,3,1,2,6,8,7,0}, {4,2,8,6,5,7,3,9,0,1},
        {2,7,9,3,8,0,6,4,1,5}, {7,0,4,6,9,1,3,2,5,8}
    };

    private readonly byte[] _key;

    public AadhaarNumberService(IConfiguration configuration)
    {
        try { _key = Convert.FromBase64String(configuration["Aadhaar:EncryptionKey"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("Configure a base64 Aadhaar encryption key."); }
        if (_key.Length != 32) throw new InvalidOperationException("Configure a 32-byte Aadhaar encryption key.");
    }

    public static bool IsValid(string? number)
    {
        if (number is null || number.Length != 12 || number[0] is < '2' or > '9' ||
            number.Any(c => c is < '0' or > '9')) return false;
        var checksum = 0;
        for (var i = 0; i < number.Length; i++)
            checksum = D[checksum, P[i % 8, number[number.Length - 1 - i] - '0']];
        return checksum == 0;
    }

    public string Encrypt(string number)
    {
        if (!IsValid(number)) throw new ArgumentException("Invalid Aadhaar number.", nameof(number));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.ASCII.GetBytes(number);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try { using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, plaintext, ciphertext, tag); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        return "v1:" + Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    public string Mask(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty; // Existing image-only members.
        var number = Decrypt(encrypted);
        return "•••• •••• " + number[8..];
    }

    public string Decrypt(string encrypted)
    {
        if (!encrypted.StartsWith("v1:", StringComparison.Ordinal)) throw new CryptographicException("Unsupported Aadhaar number format.");
        var bytes = Convert.FromBase64String(encrypted[3..]);
        if (bytes.Length != 40) throw new CryptographicException("Invalid Aadhaar number ciphertext.");
        var plaintext = new byte[12];
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28, 12), bytes.AsSpan(12, 16), plaintext);
            var number = Encoding.ASCII.GetString(plaintext);
            if (!IsValid(number)) throw new CryptographicException("Invalid Aadhaar number plaintext.");
            return number;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
