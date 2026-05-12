namespace EasySave.Models;

public class AppSettings
{
    public bool AutoAssignJobId { get; set; } = false;
    public string Language { get; set; } = "en";
    public string BackKey { get; set; } = "r";
    public string? LogFormat { get; set; }
    public int? MaxJobs { get; set; }
    public string BusinessSoftwareName { get; set; } = string.Empty;

    // "StartOnly"  : on vérifie une seule fois avant le démarrage du job.
    // "Continuous" : on vérifie aussi entre chaque fichier (ancien comportement).
    public string BusinessSoftwareCheckMode { get; set; } = "StartOnly";

    public string? LogPath { get; set; }
    public string? StatePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? LangPath { get; set; }

    public List<string> EncryptedExtensions { get; set; } = new();

    // Extensions prioritaires : un fichier non-prioritaire ne peut pas être
    // copié tant qu'il reste, dans l'ensemble des jobs en cours, au moins un
    // fichier dont l'extension figure dans cette liste à traiter.
    public List<string> PriorityExtensions { get; set; } = new();

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

    // Parallélisme : nombre maximum de jobs exécutés simultanément.
    public int MaxParallelJobs { get; set; } = 4;

    // Parallélisme : nombre maximum de fichiers copiés simultanément à l'intérieur d'un même job.
    public int MaxParallelFilesPerJob { get; set; } = 4;

    // Bande passante : taille (en Ko) au-delà de laquelle un fichier est
    // considéré "gros". Deux fichiers >= ce seuil ne sont jamais copiés en
    // même temps (sérialisation globale, tous jobs confondus).
    // 0 = désactivé (pas de sérialisation).
    public int LargeFileThresholdKb { get; set; } = 1024;

    // === Champs CLIENT uniquement (ignorés côté serveur) ===
    // Dernière IP/port utilisés pour se connecter au serveur. Si 127.0.0.1
    // répond au démarrage, ces valeurs ne sont pas consultées.
    public string? RemoteServerHost { get; set; }
    public int? RemoteServerPort { get; set; }
}
