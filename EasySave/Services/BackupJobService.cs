using EasySave.Models;
using System.Text.Json;

namespace EasySave.Services;

public class BackupJobService
{
    private const int MaxJobs = 5;
    private readonly string _configFilePath;
    private List<BackupJob> _jobs;

    public BackupJobService(PathService pathService)
    {
        _configFilePath = pathService.GetConfigFilePath();
        _jobs = Load();
    }

    public IReadOnlyList<BackupJob> GetAll() => _jobs.AsReadOnly();

    public bool Add(BackupJob job)
    {
        if (_jobs.Count >= MaxJobs) return false;
        job.Id = _jobs.Count > 0 ? _jobs.Max(j => j.Id) + 1 : 1;
        _jobs.Add(job);
        Save();
        return true;
    }

    public bool Update(int id, BackupJob updated)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == id);
        if (job == null) return false;
        job.Name = updated.Name;
        job.SourcePath = updated.SourcePath;
        job.TargetPath = updated.TargetPath;
        job.Type = updated.Type;
        Save();
        return true;
    }

    public bool Delete(int id)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == id);
        if (job == null) return false;
        _jobs.Remove(job);
        Save();
        return true;
    }

    private void Save() =>
        File.WriteAllText(_configFilePath, JsonSerializer.Serialize(
            _jobs, new JsonSerializerOptions { WriteIndented = true }));

    private List<BackupJob> Load()
    {
        if (!File.Exists(_configFilePath)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<BackupJob>>(
                File.ReadAllText(_configFilePath)) ?? new();
        }
        catch { return new(); }
    }
}
