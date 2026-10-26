using System.Security.Cryptography;
using System.Text;

namespace MeuClima.Core.Switches;

/// <summary>Cifra do modo LAN eWeLink: AES-128-CBC, PKCS#7, chave = MD5(devicekey).</summary>
public static class EwelinkCrypto
{
    public static (string Data, string Iv) Encrypt(string json, string devicekey, byte[]? iv = null)
    {
        iv ??= RandomNumberGenerator.GetBytes(16);
        using var aes = Create(devicekey);
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(json), iv, PaddingMode.PKCS7);
        return (Convert.ToBase64String(cipher), Convert.ToBase64String(iv));
    }

    public static string Decrypt(string data, string iv, string devicekey)
    {
        using var aes = Create(devicekey);
        return Encoding.UTF8.GetString(aes.DecryptCbc(Convert.FromBase64String(data), Convert.FromBase64String(iv), PaddingMode.PKCS7));
    }

    private static Aes Create(string devicekey)
    {
        var aes = Aes.Create();
        aes.Key = MD5.HashData(Encoding.UTF8.GetBytes(devicekey));
        return aes;
    }
}
