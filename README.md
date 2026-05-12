# EasySave v3

Logiciel de sauvegarde professionnel développé par **ProSoft**.

EasySave est une solution .NET 8 qui permet de créer, planifier et exécuter des travaux de sauvegarde locaux ou distants, avec supervision temps réel, chiffrement, multithreading et architecture client/serveur.

---

## ✨ Fonctionnalités

### Sauvegarde
- Travaux **complets** ou **différentiels** illimités
- Sauvegarde locale, disques externes ou partages réseau
- **Multithreading** : exécution parallèle de plusieurs jobs
- Système de **fichiers prioritaires** (gate globale cross-jobs)
- Pause / Reprise / Stop coopératif par job (avec annulation au milieu d'un fichier)
- Détection automatique de **logiciel métier** (auto-pause)

### Sécurité
- Chiffrement **trois modes** :
  - **Rapide** — XOR via exécutable externe CryptoSoft (mono-instance)
  - **Standard** — AES-256-CBC in-process
  - **Premium** — ECIES (ECDH P-256 + AES-256-GCM)

### Interfaces
- **WPF (V3)** — IHM moderne avec thèmes, multilangue, suivi visuel par job
- **CLI** — mode headless pour automatisation (`1-3`, `1;3`)
- **Multilangue** : Français, Anglais, Chinois, Hébreu

### Mode distant (Client/Serveur)
- Serveur TCP exposant le moteur de sauvegarde
- Console graphique distante connectée via **NDJSON over TCP**
- Broadcast temps réel des événements (progression, pause, fin) à tous les clients connectés
- API symétrique : l'UI ne distingue pas un moteur local d'un moteur distant

### Observabilité
- Logs journaliers **JSON ou XML** (configurable à chaud)
- Fichier `state.json` temps réel (un job = une entrée)
- Événements de cycle de vie (start / progress / pause / resume / stop / complete)

---

## 🏗️ Architecture

```
┌────────────────────────┐   ┌─────────────────────────┐
│   EasySave (WPF)       │   │   EasySave.Cli          │   ← Views
└──────────┬─────────────┘   └────────────┬────────────┘
           │                              │
           ▼                              ▼
       ┌──────────────────────────────────────────┐
       │   EasySave.Core / ViewModels             │   ← MVVM (partagé)
       │   MainVM • JobListVM • SettingsVM …      │
       ├──────────────────────────────────────────┤
       │   EasySave.Core / Services               │   ← Logique métier
       │   BackupEngine • JobController           │
       │   PriorityGate • CryptoDispatcher        │
       ├──────────────────────────────────────────┤
       │   EasySave.Core / Models                 │   ← Entités
       └──────┬────────────────────┬──────────────┘
              │                    │
       ┌──────▼──────┐      ┌──────▼─────────┐
       │ EasyLog     │      │ CryptoSoft     │   ← Modules externes
       │ (JSON/XML)  │      │ (singleton exe)│
       └─────────────┘      └────────────────┘

       ┌──────────────────────────────────────────┐
       │ Mode distant : Client ⇄ Protocol ⇄ Server│
       └──────────────────────────────────────────┘
```

### Design patterns clés
| Couche / Feature | Patterns |
|---|---|
| Architecture globale | **MVVM**, **Dependency Injection** |
| Chiffrement / Logger | **Strategy**, **Factory** |
| Persistance | **Repository** (local + distant) |
| Pause multi-cause | **State** + Observer |
| Priorité globale | **Barrier / Gate** lock-free |
| Stop coopératif | **Cooperative Cancellation** (CancellationToken) |
| Mode client | **Proxy distant** sur `IBackupEngine` / `IJobRepository` |
| Serveur | **Mediator / Broadcast**, **Reactor** (read-loop async) |
| Protocole TCP | **Message Envelope**, **NDJSON framing**, **Command** |
| Singleton | CryptoSoft (mutex inter-processus), SettingsService |

---

## 📁 Structure de la solution

```
GenieLogiciel/
├── EasySave.sln
│
├── EasySave/                   # IHM WPF (V3)
│   ├── App.xaml(.cs)           # Composition root + DI manuelle
│   ├── Views/                  # MainWindow, JobListView, JobFormView,
│   │                           # JobExecutionView, LogsView, SettingsView
│   └── Services/               # FolderPicker, ThemeManager, ServerConnectionPrompt
│
├── EasySave.Cli/               # IHM console (mode headless + interactif)
│   ├── CliParser.cs            # Parsing 1-3 / 1;3
│   └── ConsoleMenu.cs
│
├── EasySave.Core/              # Cœur métier partagé WPF + CLI
│   ├── Models/                 # BackupJob, JobEntry, JobStatus, AppSettings…
│   ├── ViewModels/             # MVVM partagé : Main, JobList, Settings, Logs…
│   ├── Services/
│   │   ├── BackupEngine.cs     # Moteur multithread
│   │   ├── JobController.cs    # Pause multi-cause + cancellation
│   │   ├── PriorityGate.cs     # Barrière globale fichiers prioritaires
│   │   ├── FileScanner.cs      # Scan + classification (priorité, taille)
│   │   ├── CopyService.cs      # Copie buffer par buffer (cancellable)
│   │   ├── JobRepository.cs    # Persistance JSON
│   │   ├── CryptoDispatcher.cs # Strategy : XOR / AES / ECIES
│   │   ├── Aes/Ecies/XorCryptoService.cs
│   │   ├── BusinessSoftwareWatcher.cs
│   │   ├── ProcessMonitorService.cs
│   │   └── SettingsService.cs
│   └── Resources/Translator.cs # i18n runtime (fr/en/zh/he)
│
├── EasyLog/                    # Lib de log JSON/XML
│   ├── ILogger.cs
│   ├── JsonLineLogger.cs       # NDJSON append daily
│   ├── XmlAppendLogger.cs      # XML append daily
│   └── LoggerFactory.cs        # Factory selon LogFormat
│
├── CryptoSoft/                 # Exécutable de chiffrement XOR mono-instance
│   ├── Program.cs              # Mutex inter-processus
│   └── FileManager.cs
│
├── EasySave.Protocol/          # Protocole TCP partagé client/serveur
│   ├── Envelope.cs             # Enveloppe typée { type, payload, correlationId }
│   ├── Messages.cs             # Constantes MessageTypes
│   ├── Dtos.cs                 # Payloads sérialisables
│   └── NdjsonChannel.cs        # Canal line-delimited JSON (SemaphoreSlim write)
│
├── EasySave.Server/            # Serveur TCP (héberge le moteur réel)
│   ├── TcpBackupServer.cs      # Accept loop + broadcast
│   ├── ClientSession.cs        # 1 session = 1 read loop async
│   └── DtoMapper.cs
│
└── EasySave.Client/            # Client TCP (RemoteBackupEngine + RemoteRepo)
    ├── BackupServerConnection.cs
    ├── RemoteBackupEngine.cs   # Proxy IBackupEngine
    ├── RemoteJobRepository.cs  # Proxy IJobRepository
    └── RemoteDtoMapper.cs
```

---

## ⚙️ Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (1709+) — la WPF requiert Windows ; le serveur TCP et la CLI sont multiplateforme

---

## 🚀 Build & Run

```bash
# Build complet
dotnet build EasySave.sln

# Application WPF (mode local par défaut)
dotnet run --project EasySave

# CLI interactive
dotnet run --project EasySave.Cli

# CLI headless : exécute les jobs 1, 2 et 3
dotnet run --project EasySave.Cli -- 1-3

# CLI headless : exécute les jobs 1 et 3
dotnet run --project EasySave.Cli -- "1;3"
```

### Mode client/serveur

```bash
# Démarrer le serveur (port par défaut configuré dans settings.json)
dotnet run --project EasySave.Server

# Démarrer le client WPF connecté au serveur
dotnet run --project EasySave
# → au démarrage, choisir "Se connecter à un serveur" et renseigner host:port
```

---

## 📂 Emplacement des fichiers

Tous les fichiers utilisateur sont stockés dans `%APPDATA%/ProSoft/EasySave/` :

| Fichier        | Chemin                       | Description                       |
| -------------- | ---------------------------- | --------------------------------- |
| Jobs           | `Config/jobs.json`           | Travaux de sauvegarde sauvegardés |
| Paramètres     | `Config/settings.json`       | Préférences utilisateur           |
| État temps réel| `State/state.json`           | Progression de chaque job actif   |
| Logs JSON      | `Logs/YYYY-MM-DD.json`       | Logs quotidiens NDJSON            |
| Logs XML       | `Logs/YYYY-MM-DD.xml`        | Logs quotidiens XML               |

---

## 💻 CLI

| Commande             | Description                              |
| -------------------- | ---------------------------------------- |
| `EasySave.Cli`       | Menu interactif                          |
| `EasySave.Cli 1-3`   | Exécute les jobs 1, 2, 3 séquentiellement |
| `EasySave.Cli 1;3`   | Exécute les jobs 1 et 3                   |

---

## 🔐 Configuration du chiffrement

Réglable via l'IHM (Settings) ou directement dans `settings.json` :

| Mode      | Algorithme                       | Implémentation        |
| --------- | -------------------------------- | --------------------- |
| Rapide    | XOR                              | CryptoSoft.exe (externe, mono-instance) |
| Standard  | AES-256-CBC                      | In-process            |
| Premium   | ECIES (ECDH P-256 + AES-256-GCM) | In-process            |

Liste des **extensions à chiffrer** et liste **logiciels métier** également configurables.

---

## 👥 Équipe

| Membre      | Rôle principal                                      |
| ----------- | --------------------------------------------------- |
| **Bastien** | Moteur de sauvegarde, multithreading, orchestrateur |
| **Gabriel** | Persistance, logs, état, EasyLog DLL, protocole TCP |
| **Oscar**   | IHM (WPF + CLI), i18n, parseurs, ergonomie          |

---

## 📜 Licence

Logiciel interne — **ProSoft Suite**. Prix unitaire : **200 € HT**.
