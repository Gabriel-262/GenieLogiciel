# EasySave v1.0 — Documentation technique

> Document interne ProSoft — destiné aux développeurs, au support technique et aux mainteneurs futurs.

## 1. Vue d'ensemble

EasySave est une application console .NET 8 de sauvegarde. Elle permet à un opérateur de définir jusqu'à cinq travaux de sauvegarde (complète ou différentielle), puis de les exécuter soit via un menu console interactif, soit via des arguments en ligne de commande.

Deux livrables sont produits :

| Artefact | Projet | Sortie |
|---|---|---|
| `EasySave.exe` | `EasySave/` | Application console .NET 8 |
| `EasyLog.dll` | `EasyLog/` | Bibliothèque de classes .NET 8 (réutilisable par d'autres produits ProSoft) |

## 2. Structure de la solution

```
GenieLogiciel/
├── EasySave.sln
├── EasyLog/                      Bibliothèque — journalisation JSON quotidienne (dll)
│   ├── ILogger.cs
│   ├── LogEntry.cs
│   └── EasyLogger.cs
└── EasySave/                     Application console
    ├── Program.cs                Point d'entrée (modes CLI + interactif)
    ├── AppConfig.cs              Lit .env (MAX_JOBS), expose les valeurs par défaut
    ├── .env                      MAX_JOBS=5
    ├── Models/                   Types de données purs
    │   ├── AppSettings.cs        Préférences utilisateur persistées
    │   ├── BackupJob.cs
    │   ├── BackupType.cs         { Full, Differential }
    │   ├── JobStatus.cs          { Inactive, Active }
    │   └── StateEntry.cs
    ├── Interfaces/
    │   └── IStateManager.cs
    ├── Services/
    │   ├── PathService.cs        Résolution des chemins sous %AppData%\ProSoft\EasySave
    │   ├── BackupJobService.cs   CRUD sur jobs.json, applique MaxJobs
    │   ├── SettingsService.cs    Lecture/écriture de settings.json
    │   ├── StateService.cs       Mises à jour temps réel de state.json
    │   └── BackupEngine.cs       Orchestrateur : scan, copie, log, progression
    ├── Views/                    UI console
    │   ├── ConsoleMenu.cs
    │   ├── CliParser.cs          Parsing "1-3" / "1;3"
    │   └── InputValidator.cs
    └── Resources/                Ressources i18n
        ├── Translator.cs         Encapsulation ResourceManager
        ├── Strings.resx          Anglais (neutre / par défaut)
        ├── Strings.fr.resx       Français
        ├── Strings.zh.resx       Chinois simplifié
        └── Strings.he.resx       Hébreu
```

## 3. Emplacements à l'exécution (poste client)

Le logiciel n'écrit **pas** dans `C:\temp`. Tous les fichiers d'exécution sont placés sous le dossier Windows standard des données d'application utilisateur :

```
%AppData%\ProSoft\EasySave\
├── Config\
│   ├── jobs.json                 Travaux enregistrés par BackupJobService
│   └── settings.json             Préférences utilisateur (SettingsService)
├── Logs\
│   └── yyyy-MM-dd.json           Un fichier quotidien créé par EasyLog.dll
└── State\
    └── state.json                Fichier de progression, écrasé en temps réel
```

`PathService` crée chaque dossier au premier démarrage via `Directory.CreateDirectory`.

## 4. Architecture

### 4.1 Couches

```
┌──────────────────────────────────────────────────┐
│ Views         ConsoleMenu / CliParser / Validator │  UI pure
├──────────────────────────────────────────────────┤
│ Services      BackupEngine                        │  Orchestration
│               BackupJobService / StateService     │  Persistance
│               PathService                         │  Système de fichiers
├──────────────────────────────────────────────────┤
│ Models        BackupJob, StateEntry, enums        │  Données
├──────────────────────────────────────────────────┤
│ EasyLog.dll   ILogger, LogEntry, EasyLogger       │  Inter-produits
└──────────────────────────────────────────────────┘
```

Aucune couche ne dépend d'une couche supérieure. `EasyLog.dll` n'a aucune dépendance sur l'application console — elle peut être réutilisée par la v2/v3 (WPF/MVVM) ou par tout autre produit ProSoft.

### 4.2 Injection de dépendances (manuelle)

`Program.cs` câble le graphe une seule fois :

```csharp
Console.OutputEncoding = Encoding.UTF8;
var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
Translator.SetLanguage(settingsService.Current.Language);
var jobService   = new BackupJobService(pathService);
var stateService = new StateService(pathService);
var engine       = new BackupEngine(jobService, stateService, pathService);
```

L'encodage UTF-8 est activé immédiatement pour que les glyphes chinois et hébreux s'affichent correctement sur Windows Terminal / cmd.exe.

`BackupEngine` reçoit un `IStateManager` (pas le `StateService` concret), ce qui facilite les tests unitaires.

## 5. Algorithmes clés

### 5.1 Scan de la source (`BackupEngine.ScanDirectory`)

Énumère récursivement chaque fichier sous `SourcePath` avec `DirectoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)`. Le nombre total de fichiers et la taille cumulée sont calculés **avant** la boucle de copie afin que `state.json` contienne les totaux réels dès la première mise à jour.

### 5.2 Sauvegarde complète

Pour chaque fichier scanné :
1. Calcul du chemin relatif par rapport à `SourcePath`.
2. Recréation de l'arborescence des sous-dossiers cibles avec `Directory.CreateDirectory`.
3. Appel à `File.Copy(src, dst, overwrite: true)` encapsulé dans un `Stopwatch`.
4. Envoi d'un `LogEntry` via `EasyLog.ILogger`.
5. Mise à jour de la progression dans `state.json`.

### 5.3 Sauvegarde différentielle

Même boucle que la complète, mais chaque fichier passe par `NeedsCopy` :

```
copie nécessaire = !File.Exists(dst)
                || source.Length != dest.Length
                || source.LastWriteTimeUtc > dest.LastWriteTimeUtc
```

Les fichiers ignorés contribuent quand même à `processed` / `bytesDone` pour que la barre de progression atteigne 100 %.

### 5.4 Mesure du temps de transfert

Chaque `File.Copy` est encapsulé dans un `Stopwatch` :
- succès → `TransferTimeMs = sw.ElapsedMilliseconds`
- exception (accès refusé, disque plein, coupure réseau…) → `TransferTimeMs = -sw.ElapsedMilliseconds` (négatif, conformément au cahier des charges).

Le moteur poursuit avec le fichier suivant après une erreur ; le fichier log fait foi sur ce qui s'est passé.

## 6. Persistance

| Fichier | Propriétaire | Format | Stratégie d'écriture |
|---|---|---|---|
| `jobs.json` | `BackupJobService` | `List<BackupJob>` indenté | Réécriture complète à chaque mutation |
| `state.json` | `StateService` | `List<StateEntry>` indenté | Réécriture complète après chaque fichier, protégé par `object _fileLock` |
| `yyyy-MM-dd.json` | `EasyLogger` | `List<LogEntry>` indenté | Lecture-ajout-écriture, protégé par `object _fileLock` |

Tout le JSON est produit avec `JsonSerializerOptions { WriteIndented = true }` pour préserver la lisibilité dans Notepad.

## 7. Interface en ligne de commande

```
EasySave.exe                  Lance le menu console interactif
EasySave.exe 1-3              Exécute les travaux 1, 2, 3 séquentiellement (headless)
EasySave.exe 1;3              Exécute les travaux 1 et 3 (headless)
```

Le parsing est assuré par `CliParser.Parse(string)` avec deux branches regex/split. Des arguments inconnus ou mal formés renvoient le code de sortie `1` avec un message localisé.

Le mode headless n'appelle jamais `Console.Clear` et n'attend pas d'appui sur une touche — il exécute et quitte, le processus peut donc être planifié par le Planificateur de tâches ou invoqué depuis une chaîne CI.

## 8. Internationalisation

La culture neutre (par défaut) est l'anglais — définie via `<NeutralLanguage>en</NeutralLanguage>` dans `EasySave.csproj`. La langue réellement utilisée au démarrage est lue depuis `settings.json` (retombe sur l'anglais si absente).

Langues livrées :

| Code | Langue |
|------|--------|
| `en` | Anglais (neutre) |
| `fr` | Français |
| `zh` | Chinois simplifié |
| `he` | Hébreu |

`Translator.SetLanguage("<code>")` bascule la culture UI du processus courant. Chaque clé de `.resx` est le **contrat** entre `ConsoleMenu` et le fichier de ressources — ajouter une nouvelle langue consiste à copier `Strings.resx` en `Strings.<culture>.resx` et à traduire uniquement les balises `<value>`.

## 8.bis Paramètres et navigation retour

Les préférences utilisateur sont persistées par `SettingsService` dans `settings.json` :

```json
{
  "AutoAssignJobId": false,
  "Language": "en",
  "BackKey": "r"
}
```

L'entrée **Paramètres** (touche `7` du menu principal) ouvre un sous-menu avec trois options :

| # | Option | Effet |
|---|---|---|
| 1 | Attribution automatique de l'ID | ACTIVÉ : le prochain ID libre dans `1..MaxJobs` est choisi automatiquement lors de l'ajout. DÉSACTIVÉ : l'utilisateur saisit l'ID et son unicité est vérifiée. |
| 2 | Langue | Bascule la culture immédiatement et persiste le choix. |
| 3 | Touche retour | Une seule lettre (pas un chiffre). Les chiffres sont interdits pour éviter les collisions avec les choix du menu. |

La touche retour peut être pressée sur **n'importe quelle** saisie. Lorsque le menu la détecte (`IsBack(input)`), l'opération en cours est abandonnée et le contrôle revient au menu principal. Chaque écran affiche un indice localisé en haut : `(appuyez sur 'r' pour revenir en arrière)`.

## 9. Configuration

Le fichier `.env` à la racine de `EasySave/` est copié dans le dossier de sortie (`CopyToOutputDirectory=PreserveNewest`). `AppConfig.MaxJobs` le lit une seule fois et met le résultat en cache. Si `.env` est absent ou mal formé, la valeur de repli `5` est utilisée.

## 10. Configuration minimale requise

| Élément | Requis |
|---|---|
| OS | Windows 10 / Windows Server 2016 ou plus récent |
| Runtime | .NET 8 Desktop Runtime |
| Disque | Volume source + espace libre suffisant sur la cible |
| Permissions | Lecture sur la source, lecture/écriture sur la cible, écriture sur `%AppData%\ProSoft\EasySave` |

## 11. Checklist support

Lors d'un diagnostic chez un client :
1. Vérifier `%AppData%\ProSoft\EasySave\Config\jobs.json` — les travaux sont-ils définis ?
2. Vérifier `%AppData%\ProSoft\EasySave\Config\settings.json` — langue et touche retour correctes ? Supprimer le fichier pour revenir aux valeurs par défaut.
3. Vérifier `%AppData%\ProSoft\EasySave\State\state.json` — un travail est-il bloqué en `Active` ?
4. Ouvrir le fichier `%AppData%\ProSoft\EasySave\Logs\yyyy-MM-dd.json` du jour — des `TransferTimeMs < 0` ? Cette ligne identifie un fichier en échec.
5. Vérifier que le dossier `EasySave` dispose des droits d'écriture pour l'utilisateur qui lance le processus.
6. Confirmer que le `.env` à côté de `EasySave.exe` est présent et que `MAX_JOBS` est un entier positif.

## 12. Limites connues en v1.0

- Copie mono-thread (le cahier des charges n'impose pas la concurrence ; le parallélisme est prévu pour la v3).
- Pas de jeton d'annulation — une sauvegarde en cours se termine ou plante.
- Aucune reprise sur erreurs d'E/S transitoires ; le fichier est marqué négatif et la boucle continue.
- Le fichier d'état est écrasé — il n'y a pas d'historique des exécutions passées (l'historique vit dans le log quotidien).
