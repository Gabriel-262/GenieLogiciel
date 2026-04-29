# Diagrammes UML — EasySave v2.0 (Livrable 2)

Diagrammes Mermaid centrés sur les **nouveautés du livrable 2** par rapport à v1.0 :

- Interface graphique **WPF MVVM** (remplace la console).
- Travaux **illimités** (suppression de la limite à 5).
- **Cryptage** via CryptoSoft (sélection par extensions configurables).
- Log enrichi avec **CryptoTimeMs**.
- **Détection du logiciel métier** + pause/reprise + bouton *Reprendre*.
- Format de log **XML ou JSON** sélectionnable.

---

## 1. Diagramme de classes — Cryptage + Logiciel métier + WPF

```mermaid
classDiagram
    direction LR

    class JobStatus {
        <<enumeration>>
        Inactive
        Active
        Paused_NEW
    }

    class LogAction {
        <<enumeration>>
        Create
        Update
        Delete
        JobUpdated
        JobDeleted
        BusinessSoftwareDetected_NEW
    }

    class AppSettings {
        +bool AutoAssignJobId
        +string Language
        +string LogFormat
        +string BusinessSoftwareName_NEW
        +List EncryptedExtensions_NEW
        +string CryptoMode_NEW
        +string CryptoKey_NEW
        +string Theme_NEW
    }

    class LogEntry {
        +DateTime Timestamp
        +int JobId
        +string BackupName
        +LogAction Action
        +string SourceFilePath
        +string DestinationFilePath
        +long FileSizeBytes
        +long TransferTimeMs
        +long CryptoTimeMs_NEW
    }

    class IBusinessSoftwareMonitor {
        <<interface>>
        +IsRunning() bool
    }

    class ICryptoSoft {
        <<interface>>
        +Encrypt(filePath) long
    }

    class ILogger {
        <<interface>>
        +Log(LogEntry) void
    }

    class ProcessMonitorService {
        -processNameProvider Func
        +IsRunning() bool
    }

    class XorCryptoService {
        +Encrypt(path) long
    }
    class AesCryptoService {
        +Encrypt(path) long
    }
    class EciesCryptoService {
        +Encrypt(path) long
    }
    class CryptoDispatcher {
        -SettingsService settings
        -ICryptoSoft xor
        -ICryptoSoft aes
        -ICryptoSoft ecies
        +Encrypt(path) long
    }

    class BackupEngine {
        -JobRepository repo
        -ILogger logger
        -IBusinessSoftwareMonitor businessMonitor
        -ICryptoSoft crypto
        -ManualResetEventSlim resumeSignal_NEW
        +event JobStarted
        +event JobCompleted
        +event ProgressChanged
        +event JobPaused_NEW
        +event JobResumed_NEW
        +bool IsPaused_NEW
        +Resume() void_NEW
        +ExecuteJob(BackupJob) void
        -WaitWhileBusinessSoftwareRunning_NEW()
        -WaitWhileFileLocked_NEW()
        -IsFileLocked_NEW(FileInfo) bool
        -ShouldEncrypt_NEW(path) bool
    }

    class JsonLineLogger {
        +Log(LogEntry) void
    }
    class XmlAppendLogger_NEW {
        +Log(LogEntry) void
    }

    class JobExecutionViewModel {
        -BackupEngine engine
        -SynchronizationContext ui_NEW
        +bool IsRunning
        +bool IsPaused_NEW
        +ICommand ResumeCommand_NEW
        +Dispose() void
    }

    ProcessMonitorService ..|> IBusinessSoftwareMonitor
    XorCryptoService ..|> ICryptoSoft
    AesCryptoService ..|> ICryptoSoft
    EciesCryptoService ..|> ICryptoSoft
    CryptoDispatcher ..|> ICryptoSoft
    JsonLineLogger ..|> ILogger
    XmlAppendLogger_NEW ..|> ILogger

    CryptoDispatcher --> XorCryptoService
    CryptoDispatcher --> AesCryptoService
    CryptoDispatcher --> EciesCryptoService

    BackupEngine --> IBusinessSoftwareMonitor
    BackupEngine --> ICryptoSoft
    BackupEngine --> ILogger
    BackupEngine ..> LogEntry

    JobExecutionViewModel --> BackupEngine
```

---

## 2. Diagramme de cas d'utilisation — nouveautés v2

```mermaid
graph LR
    User((Utilisateur))

    subgraph EasySaveV2["EasySave v2.0"]
        UC1[Lister les travaux]
        UC2[Creer un travail]
        UC3[Modifier un travail]
        UC4[Supprimer un travail]
        UC5[Executer un travail]
        UC6[Executer plusieurs travaux]
        UC7["Mettre en pause<br/>NEW v2"]
        UC8["Reprendre<br/>NEW v2"]
        UC9["Configurer logiciel metier<br/>NEW v2"]
        UC10["Configurer extensions chiffrees<br/>NEW v2"]
        UC11["Choisir format log JSON ou XML<br/>NEW v2"]
        UC12[Changer la langue]
        UC13["Choisir theme clair ou sombre<br/>NEW v2"]
    end

    User --> UC1
    User --> UC2
    User --> UC3
    User --> UC4
    User --> UC5
    User --> UC6
    User --> UC8
    User --> UC9
    User --> UC10
    User --> UC11
    User --> UC12
    User --> UC13

    UC5 -.->|trigger auto| UC7
    UC6 -.->|trigger auto| UC7
    UC7 -.->|include| UC8
    UC10 -.->|include| UC5

    classDef new fill:#FFE0B2,stroke:#E67E22,stroke-width:2px,color:#000;
    class UC7,UC8,UC9,UC10,UC11,UC13 new;
```

---

## 3. Diagramme de composants / packages — nouvelle architecture v2

```mermaid
graph TB
    subgraph WPF["EasySave WPF"]
        Views["Views<br/>MainWindow JobListView JobFormView<br/>JobExecutionView SettingsView"]
        AppCfg["App.xaml.cs<br/>Composition Root"]
    end

    subgraph CLI["EasySave.Cli"]
        CliApp["Mode silencieux<br/>EasySave.exe 1-3"]
    end

    subgraph Core["EasySave.Core - NEW v2"]
        VM["ViewModels<br/>MainVM JobListVM JobFormVM<br/>JobExecutionVM SettingsVM"]
        Services["Services<br/>BackupEngine JobRepository<br/>SettingsService PathService<br/>ProcessMonitorService NEW<br/>CryptoDispatcher NEW<br/>Xor Aes Ecies CryptoService"]
        Models["Models<br/>BackupJob JobEntry AppSettings<br/>BackupType JobStatus + Paused NEW"]
    end

    subgraph Log["EasyLog DLL"]
        LogTypes["ILogger JsonLineLogger<br/>XmlAppendLogger NEW<br/>LogEntry + CryptoTimeMs NEW<br/>LogAction + BusinessSoftwareDetected NEW"]
    end

    subgraph CryptoExe["CryptoSoft - binaire externe NEW v2"]
        CryptoBin["Process externe via Process.Start"]
    end

    subgraph Toolkit["CommunityToolkit.Mvvm - NEW v2"]
        MvvmTk["ObservableProperty RelayCommand"]
    end

    subgraph FS["Systeme de fichiers"]
        StateF["State/state.json"]
        SettingsF["Config/settings.json"]
        LogsF["Logs/yyyy-MM-dd .json ou .xml"]
        LangF["Lang/en.json fr.json zh.json he.json"]
    end

    WPF --> Core
    CLI --> Core
    WPF --> Toolkit
    Core --> Log
    Core -.->|invoque| CryptoExe
    Core -.->|read write| StateF
    Core -.->|read write| SettingsF
    Core -.->|read| LangF
    Log -.->|append| LogsF

    classDef newPkg fill:#FFE0B2,stroke:#E67E22,stroke-width:2px,color:#000;
    class Core,CryptoExe,Toolkit,WPF newPkg;
```

---

## 4. Diagramme de séquence — Sauvegarde avec pause + reprise + cryptage

```mermaid
sequenceDiagram
    autonumber
    actor User as Utilisateur
    participant View as JobExecutionView
    participant VM as JobExecutionViewModel
    participant Engine as BackupEngine
    participant Mon as ProcessMonitorService
    participant Crypto as CryptoDispatcher
    participant CSExe as CryptoSoft.exe
    participant Repo as JobRepository
    participant Log as ILogger

    User->>View: clic Executer
    View->>VM: ExecuteJobCommand
    VM->>Engine: ExecuteJobAsync(job)

    Engine->>Mon: IsRunning ?
    Mon-->>Engine: false
    Engine-->>VM: JobStarted

    loop pour chaque fichier
        Engine->>Engine: IsFileLocked(file) ?

        alt fichier docx ouvert dans Word
            Engine->>Repo: UpdateState Paused
            Engine->>Log: BusinessSoftwareDetected
            Engine-->>VM: JobPaused
            VM-->>View: IsPaused true
            View-->>User: bandeau orange + bouton Reprendre

            User->>View: ferme Word puis clic Reprendre
            View->>VM: ResumeCommand
            VM->>Engine: Resume
            Engine->>Engine: re-check IsFileLocked
            Engine->>Repo: UpdateState Active
            Engine-->>VM: JobResumed
        end

        Engine->>Engine: File.Copy src dst

        alt extension dans EncryptedExtensions
            Engine->>Crypto: Encrypt dst
            Crypto->>CSExe: Process.Start
            CSExe-->>Crypto: code retour + temps ms
            Crypto-->>Engine: cryptoMs
        end

        Engine->>Log: LogEntry TransferTimeMs CryptoTimeMs
        Engine->>Repo: UpdateState progress
        Engine-->>VM: ProgressChanged
        VM-->>View: barre de progression
    end

    Engine->>Repo: ClearState
    Engine-->>VM: JobCompleted
    VM-->>View: 100 pourcent
```

---

## 5. Diagramme d'états — JobStatus enrichi avec Paused

```mermaid
stateDiagram-v2
    [*] --> Inactive : AddJob

    Inactive --> Active : ExecuteJob
    Active --> Active : copie fichier (PushProgress)
    Active --> Paused : logiciel metier detecte OU fichier verrouille (NEW v2)
    Paused --> Paused : Reprendre mais condition encore presente
    Paused --> Active : Reprendre + condition liberee (NEW v2)
    Active --> Inactive : tous fichiers traites (ClearState)
    Inactive --> [*] : DeleteJob

    note right of Paused
        NEW v2
        Bandeau orange affiche
        Bouton Reprendre actif
        Thread bloque sur resumeSignal
        Aucune copie en cours
    end note
```

---

## 6. Diagramme d'activité — Cycle d'un fichier (avec cryptage et pause)

```mermaid
flowchart TD
    Start([Fichier suivant dans la boucle]) --> CheckBiz{Logiciel metier global actif ?}

    CheckBiz -- Oui --> PauseBiz[Pause + log BusinessSoftwareDetected, Wait Resume]
    PauseBiz --> CheckBiz
    CheckBiz -- Non --> CheckLock{Fichier verrouille FileShare.None ?}

    CheckLock -- Oui --> PauseLock[Pause + log + fire JobPaused, Wait Resume - NEW v2]
    PauseLock --> CheckLock
    CheckLock -- Non --> ExistsCheck{Fichier existe encore ?}

    ExistsCheck -- Non --> Skip([Skip fichier disparu - NEW v2])
    Skip --> Next([Fichier suivant])

    ExistsCheck -- Oui --> Diff{Type Differential et inchange ?}

    Diff -- Oui --> Next
    Diff -- Non --> Copy[File.Copy avec Stopwatch transferTimeMs]

    Copy --> CryptoCheck{Extension dans EncryptedExtensions ? NEW v2}

    CryptoCheck -- Oui --> EncryptStep[CryptoDispatcher.Encrypt - NEW v2 - cryptoMs ou code erreur]
    CryptoCheck -- Non --> NoCrypto[cryptoMs = 0]

    EncryptStep --> LogStep[ILogger.Log + TransferTimeMs + CryptoTimeMs - NEW v2]
    NoCrypto --> LogStep

    LogStep --> Push[PushProgress + fire ProgressChanged]
    Push --> Next

    classDef new fill:#FFE0B2,stroke:#E67E22,stroke-width:2px,color:#000;
    class PauseLock,Skip,CryptoCheck,EncryptStep,LogStep new;
```

---

## Récap des nouveautés UML par rapport au livrable 1

| Catégorie | v1.0 | v2.0 |
|---|---|---|
| Interface | Console | **WPF MVVM** + `EasySave.Core` partagé |
| Travaux max | 5 | **illimité** (vérification supprimée) |
| Logiciel métier | absent | **`IBusinessSoftwareMonitor` + `ProcessMonitorService`** |
| Pause / Reprise | absent | **`JobStatus.Paused`, `Resume()`, events `JobPaused` / `JobResumed`** |
| Cryptage | absent | **`ICryptoSoft` + `CryptoDispatcher` (XOR / AES / ECIES) + `CryptoSoft.exe`** |
| Format log | JSON only | **JSON _ou_ XML** (`JsonLineLogger` / `XmlAppendLogger`) |
| LogEntry | sans crypto | **+ `CryptoTimeMs`** |
| LogAction | 5 valeurs | **+ `BusinessSoftwareDetected`** |
| AppSettings | 4 champs | **+ `BusinessSoftwareName`, `EncryptedExtensions`, `CryptoMode`, `CryptoKey`, `Theme`** |
| Thread UI | sync | **`Task.Run` + `SynchronizationContext` marshalling dans VM** |
