# Diagrammes UML — EasySave 2.0 (Cryptage)

Diagrammes Mermaid pour les évolutions **2.0**: intégration du chiffrement via le logiciel externe **CryptoSoft**, et enrichissement de `LogEntry` avec le temps de chiffrement.

---

## 1. Diagramme de classes — Chiffrement

Périmètre: contrat `ICryptoSoft`, implémentation `CryptoSoftService` (lance l'exe externe), évolution de `LogEntry`, points d'intégration côté `BackupEngine` et `AppSettings`.

```mermaid
classDiagram
    direction LR

    %% ===== Contrat & implémentation =====
    class ICryptoSoft {
        <<interface>>
        +Encrypt(filePath string) long
    }

    class CryptoSoftService {
        -SettingsService _settings
        +CryptoSoftService(settings SettingsService)
        +Encrypt(filePath string) long
        -ResolveExePath() string
        -ResolveAlgo(mode string)$ string
    }

    %% ===== Log enrichi =====
    class LogEntry {
        +DateTime Timestamp
        +string BackupName
        +LogAction Action
        +string SourceFilePath
        +string DestinationFilePath
        +long FileSizeBytes
        +long TransferTimeMs
        +long CryptoTimeMs
    }

    note for LogEntry "CryptoTimeMs:\n 0  = pas de chiffrement\n >0 = durée en ms\n <0 = code erreur (-1/-2/-3)"

    %% ===== Configuration =====
    class AppSettings {
        +List~string~ EncryptedExtensions
        +string CryptoMode
        +string? CryptoKey
        +string? CryptoSoftPath
        +string? CryptoPublicKey
        +string? CryptoPrivateKey
    }

    note for AppSettings "CryptoMode:\n Rapide   -> XOR\n Standard -> AES-256-CBC\n Premium  -> ECIES (ECDH P-256 + AES-256-GCM)\nKeypair ECC auto-generee a la 1ere utilisation"

    %% ===== Intégration =====
    class BackupEngine {
        -JobRepository _repo
        -ILogger _logger
        -ICryptoSoft? _crypto
        -SettingsService? _settings
        +ExecuteJob(job BackupJob) void
        -ShouldEncrypt(sourceFilePath string) bool
        -CopyFile(source, destination string)$ long
    }

    class ILogger {
        <<interface>>
        +Log(entry LogEntry) void
    }

    %% ===== Exe externe =====
    class CryptoSoftExe {
        <<external process>>
        +main(args string[]) int
        -XorTransform(data, key byte[])$ byte[]
        -AesEncrypt(data, key byte[])$ byte[]
        -EccEncrypt(data byte[], pubKeyB64 string)$ byte[]
    }

    note for CryptoSoftExe "CLI: CryptoSoft.exe <file> <key> <xor|aes|ecc>\n  xor/aes: <key> = passphrase\n  ecc    : <key> = clé publique destinataire (base64 SPKI)\nExit: 0 OK / 1 args / 2 not found / 3 crypto error"

    %% ===== Relations =====
    ICryptoSoft <|.. CryptoSoftService
    CryptoSoftService ..> CryptoSoftExe : Process.Start
    CryptoSoftService --> SettingsService : reads
    BackupEngine --> ICryptoSoft : uses
    BackupEngine --> ILogger : uses
    BackupEngine ..> LogEntry : emits
    BackupEngine ..> AppSettings : reads via SettingsService
    SettingsService --> AppSettings : owns
```

---

## 2. Diagramme de séquence — Sauvegarde avec chiffrement

Flux d'un fichier dont l'extension figure dans `AppSettings.EncryptedExtensions`. Cas nominal en mode **Rapide** (XOR). En **Standard** seul l'argument `algo` change (`aes`). En **Premium**, voir ci-dessous: la clé passée à l'exe est la clé publique ECC (et non la passphrase), et le service garantit la présence d'une keypair via `EnsureEccKeyPair()`.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Engine as BackupEngine
    participant Crypto as CryptoSoftService
    participant Exe as CryptoSoft.exe
    participant FS as FileSystem
    participant Logger as ILogger
    participant Settings as SettingsService

    User->>Engine: ExecuteJob(job)
    Engine->>FS: scan source files

    loop pour chaque fichier
        Engine->>FS: CopyFile(source, destination)
        FS-->>Engine: elapsedMs (transfert)

        Engine->>Settings: Current.EncryptedExtensions
        Settings-->>Engine: liste extensions

        alt extension dans la liste
            Engine->>Crypto: Encrypt(destination)
            Crypto->>Settings: CryptoMode, CryptoKey
            Settings-->>Crypto: ("Rapide", "MaCle...")
            Crypto->>Crypto: ResolveExePath() & ResolveAlgo()

            Crypto->>Exe: Process.Start(destination, key, "xor")
            Exe->>FS: ReadAllBytes(destination)
            FS-->>Exe: plaintext
            Exe->>Exe: SHA-256(key) -> keyBytes
            Exe->>Exe: XorTransform(plaintext, keyBytes)
            Exe->>FS: WriteAllBytes(destination, ciphertext)
            Exe-->>Crypto: exit code 0

            Crypto-->>Engine: cryptoMs (>0)
        else extension hors liste
            Engine->>Engine: cryptoMs = 0
        end

        Engine->>Logger: Log(LogEntry { TransferTimeMs, CryptoTimeMs, ... })
        Logger->>FS: append to daily log
    end

    Engine-->>User: JobCompleted
```

### Mode Premium (ECC) — flux spécifique

```mermaid
sequenceDiagram
    autonumber
    participant Engine as BackupEngine
    participant Crypto as CryptoSoftService
    participant Settings as SettingsService
    participant Exe as CryptoSoft.exe

    Engine->>Crypto: Encrypt(destination)
    Crypto->>Settings: CryptoMode
    Settings-->>Crypto: "Premium" -> algo "ecc"

    Crypto->>Crypto: EnsureEccKeyPair()
    alt keypair absente
        Crypto->>Crypto: ECDiffieHellman.Create(P-256)
        Crypto->>Settings: CryptoPublicKey, CryptoPrivateKey (base64)
        Crypto->>Settings: Save()
    end

    Crypto->>Exe: Process.Start(file, recipientPubKeyB64, "ecc")

    Exe->>Exe: import recipient pub (SPKI)
    Exe->>Exe: ECDH ephemeral keypair (P-256)
    Exe->>Exe: shared = DeriveKeyFromHash(SHA-256)
    Exe->>Exe: AES-256-GCM(nonce, data) -> cipher + tag
    Exe->>Exe: write [ephemPubLen][ephemPub][nonce][tag][cipher]
    Exe-->>Crypto: exit code 0

    Crypto-->>Engine: cryptoMs (>0)
```

### Variantes d'erreur (CryptoTimeMs négatif)

```mermaid
sequenceDiagram
    autonumber
    participant Engine as BackupEngine
    participant Crypto as CryptoSoftService
    participant Exe as CryptoSoft.exe
    participant Logger as ILogger

    Engine->>Crypto: Encrypt(destination)

    alt destination introuvable
        Crypto-->>Engine: -1
    else CryptoSoft.exe introuvable / Process.Start échoue
        Crypto-->>Engine: -2
    else exit code != 0 (args invalides, erreur crypto)
        Crypto->>Exe: Process.Start(...)
        Exe-->>Crypto: exit code 1/2/3
        Crypto-->>Engine: -3
    end

    Engine->>Logger: Log(LogEntry { CryptoTimeMs = -1 | -2 | -3 })
```

---

## 3. Notes de conception

- **Chiffrement en place**: l'exe écrase le fichier de destination — la source reste intacte. Best practice pour un backup-at-rest qui ne doit jamais réexposer le clair côté cible.
- **Dérivation de clé**: la passphrase `CryptoKey` est hashée en SHA-256 dans CryptoSoft pour produire 256 bits, utilisés tels quels par AES-256-CBC, ou en clé répétée pour XOR.
- **IV AES**: généré aléatoirement à chaque chiffrement et préfixé au ciphertext (`IV || ciphertext`).
- **Mesure de la durée**: c'est `CryptoSoftService` (côté EasySave) qui chronomètre l'appel `Process.Start` → `WaitForExit`, pas l'exe lui-même. Garantit la cohérence des unités même si CryptoSoft évolue.
- **Codes erreur négatifs**: stables et distincts (`-1` fichier, `-2` exe, `-3` exit code), exploitables côté supervision/log analytics.
- **Mode Premium / ECIES**: chiffrement asymétrique de bout en bout sans secret partagé à l'avance. La keypair P-256 du destinataire est auto-générée à la première utilisation et persistée en base64 dans `settings.json` (PKCS#8 pour la privée, SubjectPublicKeyInfo pour la publique). Chaque fichier utilise sa propre clé symétrique éphémère dérivée par ECDH+SHA-256, puis AES-256-GCM (chiffrement authentifié — détecte toute altération du fichier chiffré). La clé privée stockée permet la décryption ultérieure.
