using EasySave.Models;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave.Views;

public class ConsoleMenu
{
    private readonly MainViewModel _vm;

    public ConsoleMenu(MainViewModel vm)
    {
        _vm = vm;
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
                case "1": ListJobs();     break;
                case "2": AddJob();       break;
                case "3": EditJob();      break;
                case "4": DeleteJob();    break;
                case "5": ExecuteJob();   break;
                case "6": ExecuteAll();   break;
                case "7": SettingsMenu(); break;
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

        _vm.JobList.Refresh();
        if (_vm.JobList.IsEmpty)
        {
            Console.WriteLine(Translator.Get("Jobs_Empty"));
        }
        else
        {
            foreach (var job in _vm.JobList.Jobs)
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

        _vm.JobList.Refresh();

        int id;
        if (_vm.Settings.AutoAssignJobId)
        {
            id = _vm.JobList.GetNextAvailableId();
        }
        else
        {
            int? result = PromptNewId();
            if (result is null) return;
            id = result.Value;
        }

        _vm.JobForm.LoadForCreate(id);

        if (!PromptField(v => _vm.JobForm.Name = v, Translator.Get("Prompt_Name"),
                InputValidator.IsValidJobName, Translator.Get("Error_InvalidName"))) return;

        if (!PromptField(v => _vm.JobForm.SourcePath = v, Translator.Get("Prompt_Source"),
                InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"))) return;

        if (!PromptField(v => _vm.JobForm.TargetPath = v, Translator.Get("Prompt_Target"),
                InputValidator.IsValidPath, Translator.Get("Error_InvalidPath"))) return;

        BackupType? type = PromptBackupType();
        if (type is null) return;
        _vm.JobForm.Type = type.Value;

        if (_vm.JobForm.SaveCommand.CanExecute(null)) _vm.JobForm.SaveCommand.Execute(null);
        _vm.JobList.Refresh();

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

        var existing = _vm.JobList.FindById(id);
        if (existing is null)
        {
            Console.WriteLine(Translator.Get("Error_JobNotFound"));
            Wait();
            return;
        }

        _vm.JobForm.LoadForEdit(existing.ToModel());

        if (!PromptField(v => _vm.JobForm.Name = v, Translator.Get("Prompt_Name"),
                InputValidator.IsValidJobName, Translator.Get("Error_InvalidName"))) return;

        if (!PromptField(v => _vm.JobForm.SourcePath = v, Translator.Get("Prompt_Source"),
                InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"))) return;

        if (!PromptField(v => _vm.JobForm.TargetPath = v, Translator.Get("Prompt_Target"),
                InputValidator.IsValidPath, Translator.Get("Error_InvalidPath"))) return;

        BackupType? type = PromptBackupType();
        if (type is null) return;
        _vm.JobForm.Type = type.Value;

        if (_vm.JobForm.SaveCommand.CanExecute(null)) _vm.JobForm.SaveCommand.Execute(null);
        _vm.JobList.Refresh();

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

        bool existed = _vm.JobList.FindById(id) is not null;
        _vm.JobList.DeleteJobCommand.Execute(id);
        Console.WriteLine(existed ? Translator.Get("Job_Deleted") : Translator.Get("Error_JobNotFound"));
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

        var job = _vm.JobList.FindById(id);
        if (job is null)
        {
            Console.WriteLine(Translator.Get("Error_JobNotFound"));
            Wait();
            return;
        }

        Console.WriteLine(string.Format(Translator.Get("Job_Executing"), job.Name));
        _vm.JobList.ExecuteJobCommand.Execute(id);
        Console.WriteLine(Translator.Get("Job_Done"));
        Wait();
    }

    private void ExecuteAll()
    {
        Console.Clear();
        Console.WriteLine(Translator.Get("Job_ExecutingAll"));
        _vm.JobList.ExecuteAllCommand.Execute(null);
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

            string onoff = _vm.Settings.AutoAssignJobId
                ? Translator.Get("Settings_On")
                : Translator.Get("Settings_Off");
            Console.WriteLine($"{Translator.Get("Settings_AutoId")}: [{onoff}]");
            Console.WriteLine($"{Translator.Get("Settings_Language")}: {_vm.Settings.LanguageDisplayName}");
            Console.WriteLine($"{Translator.Get("Settings_BackKey")}: [{_vm.Settings.BackKey}]");
            Console.WriteLine($"{Translator.Get("Settings_LogFormat")}: [{_vm.Settings.LogFormat.ToUpperInvariant()}]");
            Console.WriteLine($"{Translator.Get("Settings_Paths")}");
            Console.WriteLine(Translator.Get("Settings_Back"));
            Console.WriteLine();
            Console.Write(Translator.Get("Prompt_Choice"));

            string? input = Console.ReadLine()?.Trim();
            if (IsBack(input)) return;

            switch (input)
            {
                case "1": _vm.Settings.ToggleAutoIdCommand.Execute(null);    break;
                case "2": ChangeLanguage();                                  break;
                case "3": ChangeBackKey();                                   break;
                case "4": _vm.Settings.ToggleLogFormatCommand.Execute(null); break;
                case "5": PathsMenu();                                       break;
                case "6": return;
                default:
                    Console.WriteLine(Translator.Get("Error_InvalidChoice"));
                    Wait();
                    break;
            }
        }
    }

    private void PathsMenu()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine(Translator.Get("Paths_Title"));
            Console.WriteLine(BackHint());
            Console.WriteLine();
            Console.WriteLine($"1. LogPath:    [{Display(_vm.Settings.LogPath)}]");
            Console.WriteLine($"2. StatePath:  [{Display(_vm.Settings.StatePath)}]");
            Console.WriteLine($"3. ConfigPath: [{Display(_vm.Settings.ConfigPath)}]");
            Console.WriteLine($"4. LangPath:   [{Display(_vm.Settings.LangPath)}]");
            Console.WriteLine($"5. {Translator.Get("Settings_Back")}");
            Console.WriteLine();
            Console.WriteLine(Translator.Get("Paths_RestartNote"));
            Console.WriteLine();
            Console.Write(Translator.Get("Prompt_Choice"));

            string? input = Console.ReadLine()?.Trim();
            if (IsBack(input)) return;

            switch (input)
            {
                case "1": ChangePath(p => _vm.Settings.ChangeLogPathCommand.Execute(p));    break;
                case "2": ChangePath(p => _vm.Settings.ChangeStatePathCommand.Execute(p));  break;
                case "3": ChangePath(p => _vm.Settings.ChangeConfigPathCommand.Execute(p)); break;
                case "4": ChangePath(p => _vm.Settings.ChangeLangPathCommand.Execute(p));   break;
                case "5": return;
                default:
                    Console.WriteLine(Translator.Get("Error_InvalidChoice"));
                    Wait();
                    break;
            }
        }
    }

    private void ChangePath(Action<string> apply)
    {
        Console.Write(Translator.Get("Prompt_NewPath"));
        string? input = Console.ReadLine();
        if (input is null || IsBack(input)) return;
        apply(input.Trim());
    }

    private static string Display(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "(default)" : path;

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

        if (code is null || !_vm.Settings.ChangeLanguageCommand.CanExecute(code))
        {
            Console.WriteLine(Translator.Get("Error_InvalidChoice"));
            Wait();
            return;
        }

        _vm.Settings.ChangeLanguageCommand.Execute(code);
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

        if (input is null || !_vm.Settings.ChangeBackKeyCommand.CanExecute(input))
        {
            Console.WriteLine(Translator.Get("Error_InvalidBackKey"));
            Wait();
            return;
        }

        _vm.Settings.ChangeBackKeyCommand.Execute(input);
        Wait();
    }

    private int? PromptNewId()
    {
        while (true)
        {
            Console.Write(Translator.Get("Prompt_JobIdNew"));
            string? input = Console.ReadLine()?.Trim();
            if (IsBack(input)) return null;

            if (!int.TryParse(input, out int id) || id < 1)
            {
                Console.WriteLine(Translator.Get("Error_InvalidId"));
                continue;
            }
            if (_vm.JobList.IdExists(id))
            {
                Console.WriteLine(Translator.Get("Error_IdExists"));
                continue;
            }
            return id;
        }
    }

    private bool PromptField(Action<string> setter, string label, Func<string, bool> validator, string errorMsg)
    {
        while (true)
        {
            Console.Write(label);
            string input = Console.ReadLine() ?? string.Empty;
            if (IsBack(input)) return false;
            if (validator(input)) { setter(input); return true; }
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

    private void PrintJobList()
    {
        _vm.JobList.Refresh();
        foreach (var job in _vm.JobList.Jobs)
            Console.WriteLine($"  [{job.Id}] {job.Name}");
        Console.WriteLine();
    }

    private bool IsBack(string? input) => _vm.Settings.IsBackInput(input);

    private string BackHint() =>
        string.Format(Translator.Get("Prompt_BackHint"), _vm.Settings.BackKey);

    private void Wait()
    {
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_Continue"));
        Console.ReadKey();
    }
}
