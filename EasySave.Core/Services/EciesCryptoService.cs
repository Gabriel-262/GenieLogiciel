using System.Diagnostics;
using System.Security.Cryptography;

namespace EasySave.Services;

/// <summary>
/// Mode "Premium" — ECIES en interne: ECDH (P-256) + AES-256-GCM.
/// Keypair destinataire auto-générée et persistée en base64 dans settings.json
/// (publique en SubjectPublicKeyInfo, privée en PKCS#8). Pour chaque fichier:
/// keypair éphémère, secret partagé via ECDH+SHA-256, AES-GCM authentifié.
/// Format: [ephemPubLen:2 BE][ephemPub SPKI][nonce:12][tag:16][ciphertext].
/// Codes erreur: -1 fichier introuvable, -3 erreur de chiffrement.
/// </summary>
public class EciesCryptoService : ICryptoSoft
{
    private readonly SettingsService _settings;

    public EciesCryptoService(SettingsService settings)
    {
        _settings = settings;
    }

    public long Encrypt(string filePath)
    {
        if (!File.Exists(filePath)) return -1;

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

            byte[] plain      = File.ReadAllBytes(filePath);
            byte[] nonce      = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            byte[] tag        = new byte[AesGcm.TagByteSizes.MaxSize];
            byte[] ciphertext = new byte[plain.Length];

            using (var gcm = new AesGcm(sharedKey, tag.Length))
                gcm.Encrypt(nonce, plain, ciphertext, tag);

            using var ms = new MemoryStream();
            ms.WriteByte((byte)(ephemeralSpki.Length >> 8));
            ms.WriteByte((byte)(ephemeralSpki.Length & 0xFF));
            ms.Write(ephemeralSpki);
            ms.Write(nonce);
            ms.Write(tag);
            ms.Write(ciphertext);

            File.WriteAllBytes(filePath, ms.ToArray());
            sw.Stop();
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch
        {
            return -3;
        }
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
