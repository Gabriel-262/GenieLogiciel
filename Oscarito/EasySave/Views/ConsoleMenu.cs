using EasySave.Models;
using EasySave.Resources;
using EasySave.Services;

namespace EasySave.Views;

public class ConsoleMenu
{
    private readonly BackupJobService _jobService;
    private readonly BackupEngine _engine;

    public ConsoleMenu(BackupJobService jobService, BackupEngine engine)
    {
        _jobService = jobService;
        _engine = engine;
    }

    // Main interactive loop
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
            Console.WriteLine(Translator.Get("Menu_Language"));
            Console.WriteLine(Translator.Get("Menu_Quit"));
            Console.WriteLine();
            Console.Write(Translator.Get("Prompt_Choice"));

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ListJobs();    break;
                case "2": AddJob();      break;
                case "3": EditJob();     break;
                case "4": DeleteJob();   break;
                case "5": ExecuteJob();  break;
                case "6": ExecuteAll();  break;
                case "7": ChangeLanguage(); break;
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
        Console.WriteLine();

        var jobs = _jobService.GetAll();
        if (jobs.Count == 0)
        {
            Console.WriteLine(Translator.Get("Jobs_Empty"));
        }
        else
        {
            foreach (var job in jobs)
                Console.WriteLine($"  [{job.Id}] {job.Name,-20} {job.Type,-15} {job.SourcePath} --> {job.TargetPath}");
        }

        Wait();
    }

    private void AddJob()
    {
        Console.Clear();
        Console.WriteLine(Translator.Get("Job_Add_Title"));
        Console.WriteLine();

        if (_jobService.GetAll().Count >= 5)
        {
            Console.WriteLine(Translator.Get("Error_MaxJobs"));
            Wait();
            return;
        }

        string name   = Prompt(Translator.Get("Prompt_Name"),   InputValidator.IsValidJobName,      Translator.Get("Error_InvalidName"));
        string source = Prompt(Translator.Get("Prompt_Source"), InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"));
        string target = Prompt(Translator.Get("Prompt_Target"), InputValidator.IsValidPath,         Translator.Get("Error_InvalidPath"));
        BackupType type = PromptBackupType();

        _jobService.Add(new BackupJob { Name = name, SourcePath = source, TargetPath = target, Type = type });
        Console.WriteLine(Translator.Get("Job_Added"));
        Wait();
    }

    private void EditJob()
    {
        Console.Clear();
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        if (!int.TryParse(Console.ReadLine(), out int id)) { Wait(); return; }

        if (_jobService.GetAll().All(j => j.Id != id))
        {
            Console.WriteLine(Translator.Get("Error_JobNotFound"));
            Wait();
            return;
        }

        string name   = Prompt(Translator.Get("Prompt_Name"),   InputValidator.IsValidJobName,      Translator.Get("Error_InvalidName"));
        string source = Prompt(Translator.Get("Prompt_Source"), InputValidator.IsExistingDirectory, Translator.Get("Error_PathNotFound"));
        string target = Prompt(Translator.Get("Prompt_Target"), InputValidator.IsValidPath,         Translator.Get("Error_InvalidPath"));
        BackupType type = PromptBackupType();

        _jobService.Update(id, new BackupJob { Name = name, SourcePath = source, TargetPath = target, Type = type });
        Console.WriteLine(Translator.Get("Job_Updated"));
        Wait();
    }

    private void DeleteJob()
    {
        Console.Clear();
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        if (!int.TryParse(Console.ReadLine(), out int id)) { Wait(); return; }

        bool ok = _jobService.Delete(id);
        Console.WriteLine(ok ? Translator.Get("Job_Deleted") : Translator.Get("Error_JobNotFound"));
        Wait();
    }

    private void ExecuteJob()
    {
        Console.Clear();
        PrintJobList();
        Console.Write(Translator.Get("Prompt_JobId"));
        if (!int.TryParse(Console.ReadLine(), out int id)) { Wait(); return; }

        var job = _jobService.GetAll().FirstOrDefault(j => j.Id == id);
        if (job == null)
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

    private void ChangeLanguage()
    {
        Console.Clear();
        Console.WriteLine("1. English");
        Console.WriteLine("2. Français");
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_Choice"));
        Translator.SetLanguage(Console.ReadLine() == "2" ? "fr" : "en");
        Console.WriteLine(Translator.Get("Language_Changed"));
        Wait();
    }

    private static string Prompt(string label, Func<string, bool> validator, string errorMsg)
    {
        while (true)
        {
            Console.Write(label);
            string input = Console.ReadLine() ?? string.Empty;
            if (validator(input)) return input;
            Console.WriteLine(errorMsg);
        }
    }

    private static BackupType PromptBackupType()
    {
        while (true)
        {
            Console.WriteLine($"1. {Translator.Get("Type_Full")}");
            Console.WriteLine($"2. {Translator.Get("Type_Differential")}");
            Console.Write(Translator.Get("Prompt_Choice"));
            string? choice = Console.ReadLine();
            if (choice == "1") return BackupType.Full;
            if (choice == "2") return BackupType.Differential;
            Console.WriteLine(Translator.Get("Error_InvalidChoice"));
        }
    }

    private void PrintJobList()
    {
        foreach (var job in _jobService.GetAll())
            Console.WriteLine($"  [{job.Id}] {job.Name}");
        Console.WriteLine();
    }

    private static void Wait()
    {
        Console.WriteLine();
        Console.Write(Translator.Get("Prompt_Continue"));
        Console.ReadKey();
    }
}
