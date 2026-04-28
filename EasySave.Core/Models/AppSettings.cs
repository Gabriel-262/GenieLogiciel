namespace EasySave.Models;

public class AppSettings
{
    public bool AutoAssignJobId { get; set; } = false;
    public string Language { get; set; } = "en";
    public string BackKey { get; set; } = "r";
    public string? LogFormat { get; set; }
    public int? MaxJobs { get; set; }
    public string BusinessSoftwareName { get; set; } = string.Empty;

    public string? LogPath { get; set; }
    public string? StatePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? LangPath { get; set; }

    public List<string> EncryptedExtensions { get; set; } = new();

    // "Rapide" (XOR), "Standard" (AES) ou "Premium" (ECIES: ECDH P-256 + AES-GCM)
    public string CryptoMode { get; set; } = "Rapide";

    public string? CryptoKey { get; set; }
    public string? CryptoSoftPath { get; set; }

    // Mode Premium: keypair ECC du destinataire (base64). Auto-générée à la
    // première utilisation si absente. Privée stockée pour pouvoir déchiffrer.
    public string? CryptoPublicKey { get; set; }
    public string? CryptoPrivateKey { get; set; }

    // v2 (WPF): "light" or "dark". Ignored by the CLI.
    public string Theme { get; set; } = "light";
}
