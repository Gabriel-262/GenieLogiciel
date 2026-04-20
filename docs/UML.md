# Diagrammes UML — EasySave

Diagrammes Mermaid pour la présentation du projet **EasySave** (v1.0 — console, sauvegarde complète/différentielle, mode CLI silencieux, i18n) et de la bibliothèque **EasyLog**.

---

## 1. Diagramme de classes

```mermaid
classDiagram
    direction LR

    %% ===== Models =====
    class BackupJob {
        +int Id
        +string Name
        +string SourcePath
        +string TargetPath
        +BackupType Type
    }

    class StateEntry {
        +string JobName
        +DateTime LastActionTime
        +JobStatus Status
        +int TotalFiles
        +long TotalSizeBytes
        +double ProgressPercent
        +int RemainingFiles
        +long RemainingSizeBytes
        +string CurrentSourceFile
        +string CurrentDestinationFile
    }

    class BackupType {
        <<enumeration>>
        Full
        Differential
    }

    class JobStatus {
        <<enumeration>>
        Inactive
        Active
    }

    %% ===== Interfaces =====
    class IStateManager {
        <<interface>>
        +UpdateState(StateEntry) void
        +ClearState(string) void
    }

    class ILogger {
        <<interface>>
        +Log(LogEntry) void
    }

    %% ===== Services =====
    class BackupEngine {
        -BackupJobService _jobService
        -IStateManager _stateService
        -ILogger _logger
        +ExecuteJobs(IEnumerable~int~) void
        +ExecuteJob(BackupJob) void
        -UpdateProgress(...) void
        -ScanDirectory(string) List~FileInfo~
        -NeedsCopy(FileInfo, string) bool
        -CopyFile(string, string) long
    }

    class BackupJobService {
        -PathService _paths
        -List~BackupJob~ _jobs
        +Count int
        +GetAll() List~BackupJob~
        +GetById(int) BackupJob
        +IdExists(int) bool
        +Add(BackupJob) void
        +Update(int, BackupJob) bool
        +Delete(int) bool
    }

    class StateService {
        -PathService _paths
        +UpdateState(StateEntry) void
        +ClearState(string) void
    }

    class PathService {
        -string _baseDirectory
        -string _logsDirectory
        -string _configDirectory
        -string _stateDirectory
        +GetDailyLogFilePath() string
        +GetJobsConfigFilePath() string
        +GetStateFilePath() string
    }

    %% ===== Views =====
    class ConsoleMenu {
        -BackupJobService _jobService
        -BackupEngine _engine
        +Run() void
    }

    class CliParser {
        <<static>>
        +Parse(string) List~int~
    }

    class InputValidator {
        <<static>>
        +IsValidJobName(string) bool
        +IsValidPath(string) bool
        +IsExistingDirectory(string) bool
    }

    %% ===== Resources / Config =====
    class Translator {
        <<static>>
        +Get(string) string
        +SetLanguage(string) void
    }

    class AppConfig {
        <<static>>
        +DefaultLanguage string
        +MaxJobs int
    }

    %% ===== EasyLog library =====
    class EasyLogger {
        -Func~string~ _logPathProvider
        +Log(LogEntry) void
    }

    class LogEntry {
        +DateTime Timestamp
        +string BackupName
        +string SourceFilePath
        +string DestinationFilePath
        +long FileSizeBytes
        +long TransferTimeMs
    }

    %% ===== Relationships =====
    BackupJob --> BackupType
    StateEntry --> JobStatus

    StateService ..|> IStateManager
    EasyLogger ..|> ILogger

    BackupEngine --> BackupJobService
    BackupEngine --> IStateManager
    BackupEngine --> ILogger
    BackupEngine ..> BackupJob : uses
    BackupEngine ..> StateEntry : creates
    BackupEngine ..> LogEntry : creates

    BackupJobService --> PathService
    BackupJobService o-- BackupJob : manages
    StateService --> PathService

    ConsoleMenu --> BackupJobService
    ConsoleMenu --> BackupEngine
    ConsoleMenu ..> InputValidator : uses
    ConsoleMenu ..> Translator : uses

    EasyLogger ..> LogEntry : writes
```

---

## 2. Diagramme de cas d'utilisation

```mermaid
graph LR
    User((Utilisateur))

    subgraph EasySave
        UC1[Lister les travaux]
        UC2[Créer un travail]
        UC3[Modifier un travail]
        UC4[Supprimer un travail]
        UC5[Exécuter un travail]
        UC6[Exécuter plusieurs travaux]
        UC7[Changer la langue]
        UC8[Mode silencieux - CLI]
    end

    User --> UC1
    User --> UC2
    User --> UC3
    User --> UC4
    User --> UC5
    User --> UC6
    User --> UC7
    User --> UC8

    UC6 -. include .-> UC5
    UC8 -. include .-> UC5
    UC2 -. include .-> UC1
    UC3 -. include .-> UC1
    UC4 -. include .-> UC1
```

---

## 3. Diagramme de composants / packages

```mermaid
graph TB
    subgraph EasySave["📦 EasySave (application)"]
        direction TB
        Views["Views<br/>ConsoleMenu · CliParser · InputValidator"]
        Services["Services<br/>BackupEngine · BackupJobService · StateService · PathService"]
        Models["Models<br/>BackupJob · StateEntry · BackupType · JobStatus"]
        Interfaces["Interfaces<br/>IStateManager"]
        Resources["Resources<br/>Translator · Strings.resx (en/fr)"]
        Config["AppConfig<br/>.env · MAX_JOBS"]
    end

    subgraph EasyLog["📦 EasyLog (bibliothèque réutilisable)"]
        direction TB
        LoggerAPI["ILogger · EasyLogger · LogEntry"]
    end

    subgraph FS["💾 Système de fichiers (%AppData%/ProSoft/EasySave)"]
        direction TB
        JobsFile["Config/jobs.json"]
        StateFile["State/state.json"]
        LogsFile["Logs/yyyy-MM-dd.json"]
    end

    Views --> Services
    Views --> Resources
    Views --> Config
    Services --> Models
    Services --> Interfaces
    Services --> EasyLog
    Config --> Resources

    Services -.write/read.-> JobsFile
    Services -.write/read.-> StateFile
    EasyLog -.append.-> LogsFile
```

---

## 4. Diagramme de séquence — Exécution en mode silencieux (ex. EasySave.exe "1;3")

```mermaid
sequenceDiagram
    autonumber
    actor User as Utilisateur
    participant Prog as Program
    participant Parser as CliParser
    participant Engine as BackupEngine
    participant Jobs as BackupJobService
    participant State as StateService
    participant Logger as EasyLogger
    participant FS as Système de fichiers

    User->>Prog: EasySave.exe "1,3"
    Prog->>Parser: Parse(input)
    Parser-->>Prog: [1, 3]
    Prog->>Engine: ExecuteJobs([1, 3])

    loop pour chaque id
        Engine->>Jobs: GetById(id)
        Jobs-->>Engine: BackupJob
        Engine->>FS: ScanDirectory(SourcePath)
        FS-->>Engine: List~FileInfo~
        Engine->>State: UpdateState(Active, progress=0)
        State->>FS: write state.json

        loop pour chaque fichier
            alt Differential && fichier à jour
                Engine->>Engine: skip
            else copie nécessaire
                Engine->>FS: File.Copy(src, dest)
                Engine->>Logger: Log(LogEntry)
                Logger->>FS: append yyyy-MM-dd.json
            end
            Engine->>State: UpdateState(progress++)
            State->>FS: write state.json
        end

        Engine->>State: ClearState(jobName)
        State->>FS: write state.json (Inactive)
    end

    Engine-->>Prog: terminé
    Prog-->>User: exit 0
```

---

## 5. Diagramme d'états — cycle de vie d'un `BackupJob` (via `JobStatus`)

```mermaid
stateDiagram-v2
    [*] --> Inactive : job créé

    Inactive --> Active : ExecuteJob()<br/>UpdateState(Active)
    Active --> Active : progression fichier<br/>UpdateProgress()
    Active --> Inactive : tous fichiers traités<br/>ClearState()

    Inactive --> [*] : Delete(id)

    note right of Active
        StateEntry contient :
        - ProgressPercent
        - RemainingFiles / RemainingSizeBytes
        - CurrentSourceFile / CurrentDestinationFile
    end note
```

---

## 6. (Bonus) Diagramme d'activité — choix Full vs Differential

```mermaid
flowchart TD
    Start([Début ExecuteJob]) --> Check{SourcePath<br/>existe ?}
    Check -- Non --> End([Fin])
    Check -- Oui --> Scan[Scanner récursivement<br/>les fichiers source]
    Scan --> Init[Initialiser StateEntry<br/>Active, total files/size]
    Init --> Loop{Fichiers<br/>restants ?}
    Loop -- Non --> Clear[ClearState → Inactive]
    Clear --> End
    Loop -- Oui --> Type{Type du job ?}
    Type -- Full --> Copy[Copier le fichier]
    Type -- Differential --> Needs{NeedsCopy ?<br/>taille/date diff}
    Needs -- Non --> Skip[Ignorer]
    Needs -- Oui --> Copy
    Copy --> Log[EasyLogger.Log<br/>LogEntry dans yyyy-MM-dd.json]
    Log --> Update
    Skip --> Update[UpdateProgress<br/>state.json]
    Update --> Loop
```
