using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EasySave.Services;

/// <summary>
/// Mode "Standard" — AES-256-CBC en interne, sans appel d'exe externe.
/// Clé 256 bits dérivée par SHA-256 sur la passphrase, IV aléatoire préfixé au ciphertext.
/// Codes erreur: -1 fichier introuvable, -3 erreur de chiffrement.
/// </summary>
public class AesCryptoService : ICryptoSoft
{
    private readonly SettingsService _settings;

    public AesCryptoService(SettingsService settings)
    {
        _settings = settings;
    }

    public long Encrypt(string filePath)
    {
        if (!File.Exists(filePath)) return -1;

        string passphrase = string.IsNullOrEmpty(_settings.Current.CryptoKey)
            ? "EasySaveDefaultKey"
            : _settings.Current.CryptoKey;

        var sw = Stopwatch.StartNew();
        try
        {
            byte[] plain = File.ReadAllBytes(filePath);
            byte[] key   = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            byte[] body = encryptor.TransformFinalBlock(plain, 0, plain.Length);

            byte[] output = new byte[aes.IV.Length + body.Length];
            Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
            Buffer.BlockCopy(body,  0, output, aes.IV.Length, body.Length);

            File.WriteAllBytes(filePath, output);
            sw.Stop();
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch
        {
            return -3;
        }
    }
}
