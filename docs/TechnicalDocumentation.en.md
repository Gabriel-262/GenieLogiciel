# EasySave v1.0 — Technical Documentation

> ProSoft internal document — intended for developers, support engineers and future maintainers.

## 1. Overview

EasySave is a .NET 8 console backup application. It allows an operator to define up to five backup jobs (full or differential), then execute them either through an interactive console menu or via command-line arguments.

Two deliverables are produced:

| Artifact | Project | Output |
|---|---|---|
| `EasySave.exe` | `EasySave/` | .NET 8 console application |
| `EasyLog.dll` | `EasyLog/` | .NET 8 class library (reusable by other ProSoft products) |

## 2. Solution layout

```
GenieLogiciel/
├── EasySave.sln
├── EasyLog/                      Class library — daily JSON logging (dll)
│   ├── ILogger.cs
│   ├── LogEntry.cs
│   └── EasyLogger.cs
└── EasySave/                     Console application
    ├── Program.cs                Entry point (CLI + interactive modes)
    ├── AppConfig.cs              Reads .env (MAX_JOBS), exposes defaults
    ├── .env                      MAX_JOBS=5
    ├── Models/                   Pure data types
    │   ├── AppSettings.cs        Persisted user preferences
    │   ├── BackupJob.cs
    │   ├── BackupType.cs         { Full, Differential }
    │   ├── JobStatus.cs          { Inactive, Active }
    │   └── StateEntry.cs
    ├── Interfaces/
    │   └── IStateManager.cs
    ├── Services/
    │   ├── PathService.cs        Resolves paths under %AppData%\ProSoft\EasySave
    │   ├── BackupJobService.cs   CRUD on jobs.json, enforces MaxJobs
    │   ├── SettingsService.cs    Load/save settings.json
    │   ├── StateService.cs       Real-time updates to state.json
    │   └── BackupEngine.cs       Orchestrator: scan, copy, log, progress
    ├── Views/                    Console UI
    │   ├── ConsoleMenu.cs
    │   ├── CliParser.cs          "1-3" / "1;3" parsing
    │   └── InputValidator.cs
    └── Resources/                i18n resources
        ├── Translator.cs         ResourceManager wrapper
        ├── Strings.resx          English (neutral / default)
        ├── Strings.fr.resx       French
        ├── Strings.zh.resx       Simplified Chinese
        └── Strings.he.resx       Hebrew
```

## 3. Runtime layout (installed machine)

The software does **not** write under `C:\temp`. All runtime files are placed under the standard Windows per-user application data folder:

```
%AppData%\ProSoft\EasySave\
├── Config\
│   ├── jobs.json                 Jobs saved by BackupJobService
│   └── settings.json             User preferences (SettingsService)
├── Logs\
│   └── yyyy-MM-dd.json           One daily file created by EasyLog.dll
└── State\
    └── state.json                Live progress file, overwritten in real time
```

`PathService` creates every directory on first run via `Directory.CreateDirectory`.

## 4. Architecture

### 4.1 Layers

```
┌──────────────────────────────────────────────────┐
│ Views         ConsoleMenu / CliParser / Validator │  Pure UI
├──────────────────────────────────────────────────┤
│ Services      BackupEngine                        │  Orchestration
│               BackupJobService / StateService     │  Persistence
│               PathService                         │  File system
├──────────────────────────────────────────────────┤
│ Models        BackupJob, StateEntry, enums        │  Data
├──────────────────────────────────────────────────┤
│ EasyLog.dll   ILogger, LogEntry, EasyLogger       │  Cross-product
└──────────────────────────────────────────────────┘
```

No layer depends on a layer above it. `EasyLog.dll` has zero dependencies on the console app — it can be re-used by v2/v3 (WPF/MVVM) or any other ProSoft product.

### 4.2 Dependency injection (manual)

`Program.cs` wires the graph once:

```csharp
Console.OutputEncoding = Encoding.UTF8;
var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
Translator.SetLanguage(settingsService.Current.Language);
var jobService   = new BackupJobService(pathService);
var stateService = new StateService(pathService);
var engine       = new BackupEngine(jobService, stateService, pathService);
```

UTF-8 console encoding is set up front so Chinese and Hebrew glyphs render correctly on Windows Terminal / cmd.exe.

`BackupEngine` receives `IStateManager` (not the concrete `StateService`), making it mock-friendly.

## 5. Key algorithms

### 5.1 Source scan (`BackupEngine.ScanDirectory`)

Enumerates every file recursively under `SourcePath` with `DirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)`. Total file count and cumulative size are computed **before** the copy loop so `state.json` carries the true totals from the first update.

### 5.2 Full backup

For every scanned file:
1. Compute relative path against `SourcePath`.
2. Recreate the destination sub-directory tree with `Directory.CreateDirectory`.
3. Call `File.Copy(src, dst, overwrite: true)` wrapped in a `Stopwatch`.
4. Push a `LogEntry` via `EasyLog.ILogger`.
5. Update progress on `state.json`.

### 5.3 Differential backup

Same loop as full, but each file is passed through `NeedsCopy`:

```
needs copy = !File.Exists(dst)
          || source.Length != dest.Length
          || source.LastWriteTimeUtc > dest.LastWriteTimeUtc
```

Skipped files still contribute to `processed` / `bytesDone` so the progress bar reaches 100%.

### 5.4 Transfer timing

Each `File.Copy` is wrapped in a `Stopwatch`:
- success → `TransferTimeMs = sw.ElapsedMilliseconds`
- exception (access denied, disk full, network drop…) → `TransferTimeMs = -sw.ElapsedMilliseconds` (negative, as required by the spec).

The engine continues with the next file after a failure; the log file is the authoritative record of what happened.

## 6. Persistence

| File | Owner | Format | Write strategy |
|---|---|---|---|
| `jobs.json` | `BackupJobService` | `List<BackupJob>` pretty-printed | Full rewrite on every mutation |
| `state.json` | `StateService` | `List<StateEntry>` pretty-printed | Full rewrite after each file transfer, guarded by `object _fileLock` |
| `yyyy-MM-dd.json` | `EasyLogger` | `List<LogEntry>` pretty-printed | Read-append-write, guarded by `object _fileLock` |

All JSON is produced with `JsonSerializerOptions { WriteIndented = true }` so Notepad readability is preserved.

## 7. Command-line interface

```
EasySave.exe                  Launches the interactive console menu
EasySave.exe 1-3              Runs jobs 1, 2, 3 sequentially (headless)
EasySave.exe 1;3              Runs jobs 1 and 3 (headless)
```

Parsing is done by `CliParser.Parse(string)` using two regex/split branches. Unknown or malformed arguments return exit code `1` with a localised message.

Headless mode never calls `Console.Clear` nor waits for a key — it executes and exits, so the process can be scheduled by Task Scheduler or invoked from a CI pipeline.

## 8. Internationalisation

Neutral (default) culture is English — set via `<NeutralLanguage>en</NeutralLanguage>` in `EasySave.csproj`. The language actually used at startup is read from `settings.json` (falls back to English if absent).

Shipped languages:

| Code | Language |
|------|----------|
| `en` | English (neutral) |
| `fr` | French |
| `zh` | Simplified Chinese |
| `he` | Hebrew |

`Translator.SetLanguage("<code>")` switches the UI culture for the current process. Each `.resx` key is the **contract** between `ConsoleMenu` and the resource file — adding a new language means copying `Strings.resx` to `Strings.<culture>.resx` and translating the `<value>` tags only.

## 8.bis Settings and back-navigation

User preferences are persisted by `SettingsService` in `settings.json`:

```json
{
  "AutoAssignJobId": false,
  "Language": "en",
  "BackKey": "r"
}
```

The **Settings** entry (key `7` in the main menu) exposes a submenu with three toggles:

| # | Option | Effect |
|---|---|---|
| 1 | Auto-assign Job ID | ON: next free id in `1..MaxJobs` is picked automatically during Add. OFF: the user types the id and uniqueness is checked. |
| 2 | Language | Switches culture immediately and persists the choice. |
| 3 | Back key | Single letter (not a digit). Digits are forbidden to avoid collisions with menu choices. |

The back key can be pressed on **any** prompt. When the menu detects it (`IsBack(input)`), the current operation is abandoned and control returns to the main menu. Every screen prints a localised hint at the top: `(press 'r' to go back)`.

## 9. Configuration

`.env` at the root of `EasySave/` is copied to the output folder (`CopyToOutputDirectory=PreserveNewest`). `AppConfig.MaxJobs` reads it once and caches the result. If `.env` is missing or malformed, the fallback value `5` is used.

## 10. Minimum requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 / Windows Server 2016 or newer |
| Runtime | .NET 8 Desktop Runtime |
| Disk | Source volume + enough free space on target |
| Permissions | Read on source, read/write on target, write on `%AppData%\ProSoft\EasySave` |

## 11. Support checklist

When troubleshooting a customer site:
1. Check `%AppData%\ProSoft\EasySave\Config\jobs.json` — are the jobs defined?
2. Check `%AppData%\ProSoft\EasySave\Config\settings.json` — language and back key correct? Delete the file to reset to defaults.
3. Check `%AppData%\ProSoft\EasySave\State\state.json` — is a job stuck in `Active`?
4. Open today's `%AppData%\ProSoft\EasySave\Logs\yyyy-MM-dd.json` — any `TransferTimeMs < 0`? That row identifies a failing file.
5. Verify that the `EasySave` folder has write permissions for the user running the process.
6. Confirm `.env` next to `EasySave.exe` is present and `MAX_JOBS` is a positive integer.

## 12. Known v1.0 limits

- Single-threaded copy (spec does not require concurrency; parallelism is scheduled for v3).
- No cancellation token — a running backup completes or crashes.
- No retries on transient I/O errors; the file is flagged negative and the loop moves on.
- State file is overwritten — there is no history of past runs (history lives in the daily log).
