using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EasySave.Services;

/// <summary>
/// "Standard" mode — AES-256-CBC streamed via CryptoStream, no external exe.
/// 256-bit key derived by SHA-256 from the passphrase, random IV prefixed
/// to the ciphertext. Encryption is performed to a sibling .tmp file then
/// atomically moved over the original to bound memory usage and avoid
/// corruption on crash. Error codes: -1 file not found, -3 encryption error.
/// </summary>
public class AesCryptoService : ICryptoSoft
{
    private const int BufferSize = 81920;

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

        string tmpPath = filePath + ".enc.tmp";
        var sw = Stopwatch.StartNew();
        try
        {
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));

            using (var aes = Aes.Create())
            {
                aes.Key     = key;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using var input  = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                using var output = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

                output.Write(aes.IV, 0, aes.IV.Length);

                using var encryptor = aes.CreateEncryptor();
                using var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write, leaveOpen: true);
                input.CopyTo(crypto, BufferSize);
                crypto.FlushFinalBlock();
            }

            File.Move(tmpPath, filePath, overwrite: true);
            sw.Stop();
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch
        {
            TryDelete(tmpPath);
            return -3;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
