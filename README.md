# EasySave v1.1

Backup management software developed by **ProSoft**.

## Description

EasySave is a .NET 8 console application that allows users to create, manage and execute up to 5 backup jobs. It supports full and differential backups across local drives, external drives and network shares.

## Features

- Create, edit, delete and execute backup jobs (up to 5)
- Full backup and differential backup modes
- Multi-language support (English, French, Chinese, Hebrew)
- Interactive console menu with settings panel
- Headless CLI mode for automated execution
- Real-time JSON and XML log files (daily) via EasyLog.dll
- Real-time state tracking (state.json)
- Configurable settings: auto-assign job ID, back key, language

## Project Structure

```
GenieLogiciel/
|-- EasySave.sln
|-- EasySave/                    # Main console application
|   |-- .env                     # Environment configuration (MAX_JOBS)
|   |-- AppConfig.cs             # Reads .env constants
|   |-- Program.cs               # Entry point (CLI + interactive)
|   |-- Models/
|   |   |-- BackupJob.cs         # Backup job model
|   |   |-- BackupType.cs        # Full / Differential enum
|   |   |-- StateEntry.cs        # Real-time state model
|   |   |-- AppSettings.cs       # User preferences model
|   |   |-- JobStatus.cs         # Active / Inactive enum
|   |-- Services/
|   |   |-- PathService.cs       # AppData path resolution
|   |   |-- BackupJobService.cs  # CRUD for backup jobs
|   |   |-- BackupEngine.cs      # Copy engine (full + differential)
|   |   |-- StateService.cs      # Real-time state.json writer
|   |   |-- SettingsService.cs   # User settings persistence
|   |-- Views/
|   |   |-- ConsoleMenu.cs       # Interactive console menu
|   |   |-- InputValidator.cs    # Input validation (names, paths)
|   |   |-- CliParser.cs         # CLI argument parser (1-3, 1;3)
|   |-- Resources/
|       |-- Strings.resx         # English strings (default)
|       |-- Strings.fr.resx      # French strings
|       |-- Strings.zh.resx      # Chinese strings
|       |-- Strings.he.resx      # Hebrew strings
|       |-- Translator.cs        # Runtime language switching
|-- EasyLog/                     # Class library (DLL)
|   |-- EasyLog.csproj
|   |-- LogEntry.cs              # Log entry model
|   |-- EasyLogger.cs            # Daily JSON log writer
|   |-- ILogger.cs               # Logger interface
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 (1709 or later)

## Build and Run

```bash
# Build
dotnet build

# Run interactive mode
dotnet run --project EasySave

# Run headless mode (execute jobs 1 to 3)
dotnet run --project EasySave -- 1-3

# Run headless mode (execute jobs 1 and 3)
dotnet run --project EasySave -- "1;3"
```

## Configuration Files Location

All files are stored in `%APPDATA%/ProSoft/EasySave/`:

| File        | Path                   | Description         |
| ----------- | ---------------------- | ------------------- |
| Jobs config | `Config/jobs.json`     | Saved backup jobs   |
| Settings    | `Config/settings.json` | User preferences    |
| State       | `State/state.json`     | Real-time progress  |
| Logs        | `Logs/YYYY-MM-DD.json` | Daily transfer logs |

## CLI Usage

| Command            | Description                          |
| ------------------ | ------------------------------------ |
| `EasySave.exe`     | Start interactive console menu       |
| `EasySave.exe 1-3` | Execute jobs 1, 2 and 3 sequentially |
| `EasySave.exe 1;3` | Execute jobs 1 and 3                 |

## Team

| Member      | Role                                            |
| ----------- | ----------------------------------------------- |
| **Bastien** | Backup engine, copy algorithms, orchestrator    |
| **Gabriel** | Data management, logs, state files, EasyLog DLL |
| **Oscar**   | Console UI, CLI parsing, i18n, input validation |

## License

Internal software - ProSoft Suite. Unit price: 200 EUR HT.
