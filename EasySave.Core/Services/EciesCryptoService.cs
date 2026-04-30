using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

namespace EasySave.Services;

/// <summary>
/// "Premium" mode — ECIES streaming: ECDH (P-256) + AES-256-GCM in 64 KB
/// chunks. Recipient keypair auto-generated and persisted as base64 in
/// settings.json (public as SubjectPublicKeyInfo, private as PKCS#8). For
/// each file: ephemeral keypair, shared secret derived via ECDH+SHA-256,
/// then each chunk is sealed independently with a fresh nonce + tag.
/// File format:
///   [ephemPubLen:2 BE][ephemPub SPKI]
///   for each chunk:
///       [ctLen:4 BE][nonce:12][tag:16][ciphertext:ctLen]
/// Chunk loop reads up to 64 KB until EOF. Encryption writes to a sibling
/// .tmp file then atomically renames it, bounding memory and avoiding
/// corruption on crash. Error codes: -1 file not found, -3 encryption error.
/// </summary>
public class EciesCryptoService : ICryptoSoft
{
    private const int ChunkSize = 64 * 1024;
    private const int BufferSize = 81920;

    private readonly SettingsService _settings;

    public EciesCryptoService(SettingsService settings)
    {
        _settings = settings;
    }

    public long Encrypt(string filePath)
    {
        if (!File.Exists(filePath)) return -1;

        string tmpPath = filePath + ".enc.tmp";
        var sw = Stopwatch.StartNew();
        try
        {
            EnsureKeyPair();
            byte[] recipientSpki = Convert.FromBase64String(_settings.Current.CryptoPublicKey!);

            using var recipient = ECDiffieHellman.Create();
            recipient.ImportSubjectPublicKeyInfo(recipientSpki, out _);

            using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] ephemeralSpki = ephemeral.ExportSubjectPublicKeyInfo();

            byte[] sharedKey = ephemeral.DeriveKeyFromHash(
                recipient.PublicKey,
                HashAlgorithmName.SHA256);

            using (var input  = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            using (var output = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            using (var gcm = new AesGcm(sharedKey, AesGcm.TagByteSizes.MaxSize))
            {
                Span<byte> header2 = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(header2, (ushort)ephemeralSpki.Length);
                output.Write(header2);
                output.Write(ephemeralSpki);

                byte[] plainBuf = new byte[ChunkSize];
                byte[] cipherBuf = new byte[ChunkSize];
                Span<byte> lenBuf = stackalloc byte[4];
                Span<byte> nonce = stackalloc byte[AesGcm.NonceByteSizes.MaxSize];
                Span<byte> tag = stackalloc byte[AesGcm.TagByteSizes.MaxSize];

                int read;
                while ((read = ReadFully(input, plainBuf)) > 0)
                {
                    RandomNumberGenerator.Fill(nonce);
                    gcm.Encrypt(nonce, plainBuf.AsSpan(0, read), cipherBuf.AsSpan(0, read), tag);

                    BinaryPrimitives.WriteInt32BigEndian(lenBuf, read);
                    output.Write(lenBuf);
                    output.Write(nonce);
                    output.Write(tag);
                    output.Write(cipherBuf, 0, read);

                    if (read < plainBuf.Length) break;
                }
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

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private void EnsureKeyPair()
    {
        var s = _settings.Current;
        if (!string.IsNullOrEmpty(s.CryptoPublicKey) && !string.IsNullOrEmpty(s.CryptoPrivateKey))
            return;

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        s.CryptoPublicKey  = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
        s.CryptoPrivateKey = Convert.ToBase64String(ecdh.ExportPkcs8PrivateKey());
        _settings.Save();
    }
}
