using EasySave.Models;
using EasySave.Resources;
using EasySave.Services;

namespace EasySave.Views;

public class ConsoleMenu
{
    private readonly BackupJobService _jobService;
    private readonly BackupEngine _engine;
    private readonly SettingsService _settings;

    public ConsoleMenu(BackupJobService jobService, BackupEngine engine, SettingsService settings)
    {
        _jobService = jobService;
        _engine = engine;
        _settings = settings;
    }

    public void Run()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine(Translator.Get("Menu_Title"));
            Console.WriteLine();
            Console.WriteLine(Translator.Get("Menu_ListJobs"));
            Console.WriteLine(Translator.Get("Menu_AddJob"));
            Console.WriteLine(Translator.Get("Menu_EditJob"));
            Console.WriteLine(Translator.Get("Menu_DeleteJob"));
            Console.WriteLine(Translator.Get("Menu_ExecuteJob"));
            Console.WriteLine(Translator.Get("Menu_ExecuteAll"));
            Console.WriteLine(Translator.Get("Menu_Settings"));
            Console.WriteLine(Translator.Get("Menu_Quit"));
            Console.WriteLine();
            Console.Write(Translator.Get("Prompt_Choice"));

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ListJobs();       break;
                case "2": AddJob();         break;
                case "3": EditJob();        break;
                case "4": DeleteJob();      break;
                case "5": ExecuteJob();     break;
                case "6": ExecuteAll();     break;
                case "7": SettingsMenu();   break;
                case "8": return;
                default:
                    Console.WriteLine(Translator.Get("Error_InvalidChoice"));
                    Wait();
                    break;
            }
        }
    }

    private void ListJobs()
    {
        Console.Clear();
        Console.WriteLine(Translator.Get("Jobs_List_Title"));
        Console.WriteLine(BackHint());
        Console.WriteLine();

        var jobs = _jobService.GetAll();
        if (jobs.Count == 0)
        {
            Console.WriteLine(Translator.Get("Jobs_Empty"));
        }
        else
        {
            foreach (var job in jobs)
                Console.WriteLine($"  [{job.Id}] {job.Name,-20} {LocalizeType(job.Type),-25} {job.SourcePath} --> {job.TargetPath}");
        }

        Wait();
    }

    private void AddJob()
    {
        Console.Clear();
        Console.WriteLine(Translator.Get("Job_Add_Title"));
        Console.WriteLine(BackHint());
        Console.WriteLine();

        if (_jobService.Count >= AppConfig.MaxJobs)
        {
            Console.WriteLine(string.Format(Translator.Get("Error_MaxJobs"), AppConfig.MaxJobs));
            Wait();
            return;
        }

        int id;
        if (_settings.Current.AutoAssignJobId)
        {
            id = GetAutoAssignedId();
        }
        else
        {
            int? result = PromptNewId();
            if (result is null) return;
            id = result.Value;
        }

        string? name = Prompt(Translator.Get("Prompt_Name"), InputValidator.IsValidJobName, Translator.Get("Error_InvalidName"));
        if (name is null) return;

        string? source = Prompt(Translator.Get("Prompt_Source"), InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"));
        if (source is null) return;

        string? target = Prompt(Translator.Get("Prompt_Target"), InputValidator.IsValidPath, Translator.Get("Error_InvalidPath"));
        if (target is null) return;

        BackupType? type = PromptBackupType();
        if (type is null) return;

        _jobService.Add(new BackupJob
        {
            Id = id,
            Name = name,
            SourcePath = source,
            TargetPath = target,
            Type = type.Value
        });
        Console.WriteLine(Translator.Get("Job_Added"));
        Wait();
    }

    private void EditJob()
    {
        Console.Clear();
        Console.WriteLine(BackHint());
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        string? rawId = Console.ReadLine()?.Trim();
        if (IsBack(rawId)) return;
        if (!int.TryParse(rawId, out int id)) { Wait(); return; }

        if (!_jobService.IdExists(id))
        {
            Console.WriteLine(Translator.Get("Error_JobNotFound"));
            Wait();
            return;
        }

        string? name = Prompt(Translator.Get("Prompt_Name"), InputValidator.IsValidJobName, Translator.Get("Error_InvalidName"));
        if (name is null) return;

        string? source = Prompt(Translator.Get("Prompt_Source"), InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"));
        if (source is null) return;

        string? target = Prompt(Translator.Get("Prompt_Target"), InputValidator.IsValidPath, Translator.Get("Error_InvalidPath"));
        if (target is null) return;

        BackupType? type = PromptBackupType();
        if (type is null) return;

        _jobService.Update(id, new BackupJob
        {
            Name = name,
            SourcePath = source,
            TargetPath = target,
            Type = type.Value
        });
        Console.WriteLine(Translator.Get("Job_Updated"));
        Wait();
    }

    private void DeleteJob()
    {
        Console.Clear();
        Console.WriteLine(BackHint());
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        string? rawId = Console.ReadLine()?.Trim();
        if (IsBack(rawId)) return;
        if (!int.TryParse(rawId, out int id)) { Wait(); return; }

        bool ok = _jobService.Delete(id);
        Console.WriteLine(ok ? Translator.Get("Job_Deleted") : Translator.Get("Error_JobNotFound"));
        Wait();
    }

    private void ExecuteJob()
    {
        Console.Clear();
        Console.WriteLine(BackHint());
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        string? rawId = Console.ReadLine()?.Trim();
        if (IsBack(rawId)) return;
        if (!int.TryParse(rawId, out int id)) { Wait(); return; }

        var job = _jobService.GetById(id);
        if (job is null)
        {
            Console.WriteLine(Translator.Get("Error_JobNotFound"));
            Wait();
            return;
        }

        Console.WriteLine(string.Format(Translator.Get("Job_Executing"), job.Name));
        _engine.ExecuteJob(job);
        Console.WriteLine(Translator.Get("Job_Done"));
        Wait();
    }

    private void ExecuteAll()
    {
        Console.Clear();
        Console.WriteLine(Translator.Get("Job_ExecutingAll"));
        _engine.ExecuteJobs(_jobService.GetAll().Select(j => j.Id));
        Console.WriteLine(Translator.Get("Job_Done"));
        Wait();
    }

    private void SettingsMenu()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine(Translator.Get("Settings_Title"));
            Console.WriteLine(BackHint());
            Console.WriteLine();

            string onoff = _settings.Current.AutoAssignJobId
                ? Translator.Get("Settings_On")
                : Translator.Get("Settings_Off");
            Console.WriteLine($"{Translator.Get("Settings_AutoId")}: [{onoff}]");
            Console.WriteLine($"{Translator.Get("Settings_Language")}: {LanguageDisplay(_settings.Current.Language)}");
            Console.WriteLine($"{Translator.Get("Settings_BackKey")}: [{_settings.Current.BackKey}]");
            Console.WriteLine(Translator.Get("Settings_Back"));
            Console.WriteLine();
            Console.Write(Translator.Get("Prompt_Choice"));

            string? input = Console.ReadLine()?.Trim();
            if (IsBack(input)) return;

            switch (input)
            {
                case "1": ToggleAutoId();    break;
                case "2": ChangeLanguage();  break;
                case "3": ChangeBackKey();   break;
                case "4": return;
                default:
                    Console.WriteLine(Translator.Get("Error_InvalidChoice"));
                    Wait();
                    break;
            }
        }
    }

    private void ToggleAutoId()
    {
        _settings.Current.AutoAssignJobId = !_settings.Current.AutoAssignJobId;
        _settings.Save();
    }

    private void ChangeLanguage()
    {
        Console.Clear();
        Console.WriteLine(BackHint());
        Console.WriteLine();
        Console.WriteLine("1. English");
        Console.WriteLine("2. Français");
        Console.WriteLine("3. 中文");
        Console.WriteLine("4. עברית");
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_Choice"));

        string? input = Console.ReadLine()?.Trim();
        if (IsBack(input)) return;

        string? code = input switch
        {
            "1" => "en",
            "2" => "fr",
            "3" => "zh",
            "4" => "he",
            _   => null
        };

        if (code is null)
        {
            Console.WriteLine(Translator.Get("Error_InvalidChoice"));
            Wait();
            return;
        }

        _settings.Current.Language = code;
        _settings.Save();
        Translator.SetLanguage(code);
        Console.WriteLine(Translator.Get("Language_Changed"));
        Wait();
    }

    private void ChangeBackKey()
    {
        Console.Clear();
        Console.WriteLine(BackHint());
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_NewBackKey"));

        string? input = Console.ReadLine()?.Trim();
        if (IsBack(input)) return;

        if (string.IsNullOrEmpty(input) || input.Length != 1 || !char.IsLetter(input[0]))
        {
            Console.WriteLine(Translator.Get("Error_InvalidBackKey"));
            Wait();
            return;
        }

        _settings.Current.BackKey = input.ToLowerInvariant();
        _settings.Save();
        Wait();
    }

    private int? PromptNewId()
    {
        while (true)
        {
            Console.Write(string.Format(Translator.Get("Prompt_JobIdNew"), AppConfig.MaxJobs));
            string? input = Console.ReadLine()?.Trim();
            if (IsBack(input)) return null;

            if (!int.TryParse(input, out int id) || id < 1 || id > AppConfig.MaxJobs)
            {
                Console.WriteLine(string.Format(Translator.Get("Error_InvalidId"), AppConfig.MaxJobs));
                continue;
            }
            if (_jobService.IdExists(id))
            {
                Console.WriteLine(Translator.Get("Error_IdExists"));
                continue;
            }
            return id;
        }
    }

    private int GetAutoAssignedId()
    {
        for (int i = 1; i <= AppConfig.MaxJobs; i++)
            if (!_jobService.IdExists(i)) return i;
        throw new InvalidOperationException();
    }

    private string? Prompt(string label, Func<string, bool> validator, string errorMsg)
    {
        while (true)
        {
            Console.Write(label);
            string input = Console.ReadLine() ?? string.Empty;
            if (IsBack(input)) return null;
            if (validator(input)) return input;
            Console.WriteLine(errorMsg);
        }
    }

    private BackupType? PromptBackupType()
    {
        while (true)
        {
            Console.WriteLine($"1. {Translator.Get("Type_Full")}");
            Console.WriteLine($"2. {Translator.Get("Type_Differential")}");
            Console.Write(Translator.Get("Prompt_Choice"));
            string? choice = Console.ReadLine()?.Trim();
            if (IsBack(choice)) return null;
            if (choice == "1") return BackupType.Full;
            if (choice == "2") return BackupType.Differential;
            Console.WriteLine(Translator.Get("Error_InvalidChoice"));
        }
    }

    private static string LocalizeType(BackupType type) =>
        type == BackupType.Full
            ? Translator.Get("Type_Full")
            : Translator.Get("Type_Differential");

    private static string LanguageDisplay(string code) => code switch
    {
        "en" => "English",
        "fr" => "Français",
        "zh" => "中文",
        "he" => "עברית",
        _    => code
    };

    private void PrintJobList()
    {
        foreach (var job in _jobService.GetAll())
            Console.WriteLine($"  [{job.Id}] {job.Name}");
        Console.WriteLine();
    }

    private bool IsBack(string? input) =>
        !string.IsNullOrEmpty(input) &&
        input.Trim().Equals(_settings.Current.BackKey, StringComparison.OrdinalIgnoreCase);

    private string BackHint() =>
        string.Format(Translator.Get("Prompt_BackHint"), _settings.Current.BackKey);

    private void Wait()
    {
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_Continue"));
        Console.ReadKey();
    }
}
