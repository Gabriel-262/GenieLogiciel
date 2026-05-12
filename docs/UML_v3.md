# Diagrammes UML — EasySave v3.0 (Livrable 3)

Diagrammes Mermaid centrés sur les **nouveautés du livrable 3** par rapport à v2.0 :

- **Sauvegarde en parallèle** (abandon du séquentiel) + parallélisme intra-job.
- **Fichiers prioritaires** : aucun fichier non prioritaire ne démarre tant qu'il reste des extensions prioritaires en attente sur au moins un job (barrière globale).
- **Limite gros fichiers** : interdiction de copier simultanément deux fichiers > *n* Ko (paramétrable).
- **Play / Pause / Stop** par travail ou pour l'ensemble + pause auto si logiciel métier détecté.
- **CryptoSoft mono-instance** (mutex inter-processus).
- **Architecture client / serveur TCP** : l'application est désormais accessible à distance — un serveur (`EasySave.Server`) héberge le moteur de sauvegarde, les clients WPF s'y connectent en TCP / NDJSON pour piloter les jobs en temps réel.

---

## 1. Diagramme de classes — Moteur parallèle + priorité + contrôle multi-causes

```mermaid
classDiagram
    direction LR

    class PauseReason {
        <<enumeration>>
        User_NEW
        Business_NEW
        FileLocked_NEW
    }

    class AppSettings {
        +int MaxParallelJobs_NEW
        +int MaxParallelFilesPerJob_NEW
        +int LargeFileThresholdKb_NEW
        +List PriorityExtensions_NEW
        +List EncryptedExtensions
        +string BusinessSoftwareName
        +string LogFormat
    }

    class IBackupEngine {
        <<interface>>
        +ExecuteJobsAsync(ids) Task
        +Pause(jobId) void_NEW
        +Resume(jobId) void_NEW
        +Stop(jobId) void_NEW
        +PauseAll() void_NEW
        +ResumeAll() void_NEW
        +StopAll() void_NEW
        +event ProgressChanged
        +event JobPaused
        +event JobResumed
        +event JobStopped_NEW
    }

    class JobController {
        -HashSet~PauseReason~ reasons_NEW
        -ManualResetEventSlim signal
        -CancellationTokenSource cts
        +AddReason(PauseReason) void_NEW
        +RemoveReason(PauseReason) void_NEW
        +WaitIfPaused() void
        +RequestStop() void_NEW
        +event Paused
        +event Resumed
    }

    class PriorityGate {
        -int pending
        -ManualResetEventSlim open
        +Register(n) void_NEW
        +NotifyOneDone() void_NEW
        +WaitForNoPriority() void_NEW
    }

    class FileScanner {
        +Scan(root, isPriority) ScanResult_NEW
    }
    class ScanResult {
        +List~FileInfo~ PriorityFiles_NEW
        +List~FileInfo~ NormalFiles_NEW
        +int TotalFiles
        +long TotalSizeBytes
    }

    class CopyService {
        -SemaphoreSlim largeFileGate_NEW
        +Copy(src, dst, thresholdBytes, ct) long_NEW
    }

    class BusinessSoftwareWatcher {
        -Timer poll_NEW
        +event Started_NEW
        +event Stopped_NEW
    }

    class BackupEngine {
        -ConcurrentDictionary~int,JobController~ controllers_NEW
        -PriorityGate priorityGate_NEW
        -FileScanner scanner
        -CopyService copier
        +ExecuteJobsAsync(ids) Task_NEW
        +PauseAllForBusinessSoftware() void_NEW
        +ResumeAllAfterBusinessSoftware() void_NEW
    }

    class CryptoSoftMutex {
        <<mutex inter-process>>
        +AcquireOrFail() void_NEW
    }

    BackupEngine ..|> IBackupEngine
    BackupEngine --> JobController : 1..n actifs_NEW
    BackupEngine --> PriorityGate : 1 global_NEW
    BackupEngine --> FileScanner
    BackupEngine --> CopyService
    BackupEngine --> BusinessSoftwareWatcher_NEW
    FileScanner ..> ScanResult
    JobController --> PauseReason
    CopyService ..> CryptoSoftMutex : via CryptoSoft.exe_NEW
```

---

## 2. Diagramme de composants — Architecture client / serveur TCP (accès distant)

```mermaid
graph TB
    subgraph Poste1["Poste client 1 (WPF)"]
        WPF1["EasySave (WPF MVVM)"]
        Core1["EasySave.Core<br/>ViewModels + Models"]
        Cli1["EasySave.Client<br/>RemoteBackupEngine NEW<br/>RemoteJobRepository NEW<br/>BackupServerConnection NEW"]
    end

    subgraph Poste2["Poste client 2 (WPF)"]
        WPF2["EasySave (WPF MVVM)"]
        Cli2["EasySave.Client NEW"]
    end

    subgraph Server["Machine serveur - EasySave.Server NEW v3"]
        Listener["TcpBackupServer<br/>accept loop + sessions"]
        Session["ClientSession * N<br/>dispatch cmd. / rsp. / evt."]
        Engine["BackupEngine<br/>(parallele + priorite + gate)"]
        Repo["JobRepository<br/>source de verite jobs/state"]
        Log["EasyLog DLL<br/>fichier journalier serveur"]
        CS["CryptoSoft.exe<br/>mono-instance (mutex) NEW"]
    end

    subgraph Proto["EasySave.Protocol - NEW v3"]
        Env["Envelope { Type, CorrelationId, Payload }<br/>NdjsonChannel (NDJSON sur TCP)<br/>cmd.* / rsp.* / evt.*"]
    end

    subgraph FS["Disques serveur"]
        StateF["State/state.json"]
        SettingsF["Config/settings.json"]
        LogsF["Logs/yyyy-MM-dd.json|.xml"]
        Backups["Repertoires sources / cibles"]
    end

    WPF1 --> Core1 --> Cli1
    WPF2 --> Cli2

    Cli1 ==>|TCP NDJSON| Listener
    Cli2 ==>|TCP NDJSON| Listener

    Cli1 -. utilise .- Proto
    Listener -. utilise .- Proto

    Listener --> Session
    Session --> Engine
    Session --> Repo
    Engine --> Repo
    Engine --> Log
    Engine -.invoque.-> CS
    Repo --> StateF
    Repo --> SettingsF
    Log --> LogsF
    Engine --> Backups

    classDef new fill:#FFE0B2,stroke:#E67E22,stroke-width:2px,color:#000;
    class Listener,Session,Engine,Cli1,Cli2,Proto,CS new;
```

> Les clients WPF n'embarquent plus le moteur : ils délèguent toutes les opérations (CRUD jobs, run, pause, resume, stop, settings) au serveur via le protocole NDJSON. Le serveur diffuse en retour les événements de progression à **tous** les clients connectés (broadcast).

---

## 3. Diagramme de séquence — Pause temps réel d'un job distant

```mermaid
sequenceDiagram
    autonumber
    actor User as Utilisateur (client A)
    participant View as JobExecutionView (WPF)
    participant VM as JobExecutionViewModel
    participant Cli as RemoteBackupEngine (client)
    participant Conn as BackupServerConnection
    participant Sess as ClientSession (serveur)
    participant Eng as BackupEngine (serveur)
    participant Ctrl as JobController
    participant ClientB as Client B (autre poste)

    User->>View: clic Pause
    View->>VM: PauseCommand
    VM->>Cli: Pause(jobId)
    Cli->>Conn: SendCommandAsync(cmd.jobs.pause, {jobId})
    Note over Conn: serialisation NDJSON + CorrelationId
    Conn->>Sess: Envelope sur TCP

    Sess->>Eng: Pause(jobId)
    Eng->>Ctrl: AddReason(User)
    Ctrl-->>Eng: event Paused (1re cause)
    Note over Ctrl: thread copie en cours<br/>bloque sur WaitIfPaused<br/>apres fichier courant

    par Reponse correlee au demandeur
        Sess-->>Conn: rsp.ok (CorrelationId)
        Conn-->>Cli: ack
        Cli-->>VM: terminé
    and Broadcast aux clients connectes
        Eng-->>Sess: JobPaused event
        Sess->>Conn: evt.job.paused
        Sess->>ClientB: evt.job.paused
        Conn-->>VM: OnEvent JobPaused
        VM-->>View: IsPaused = true (bandeau orange)
        ClientB-->>ClientB: meme MAJ UI
    end

    Note over User,ClientB: tous les clients voient la pause en temps reel
```

---

## Récap des nouveautés UML par rapport au livrable 2

| Catégorie | v2.0 | v3.0 |
|---|---|---|
| Exécution | Mono / séquentielle | **Parallèle multi-jobs + parallèle intra-job** |
| Pause / Reprise | 1 cause (logiciel métier) | **Multi-causes** `PauseReason.{User, Business, FileLocked}` |
| Stop | absent | **`Stop(jobId)` + `StopAll()`** via `CancellationToken` |
| Fichiers prioritaires | absent | **`PriorityGate` global** (barrière inter-jobs) |
| Gros fichiers | absent | **`largeFileGate` (SemaphoreSlim 1)** + seuil paramétrable |
| Logiciel métier | bloque le démarrage | **Pause/reprise auto** via `BusinessSoftwareWatcher` |
| CryptoSoft | multi-instances | **Mono-instance** via mutex inter-processus |
| Architecture | monolithe WPF local | **Client / serveur TCP** (`EasySave.Server`, `EasySave.Client`, `EasySave.Protocol`) — pilotage à distance, broadcast d'événements |
