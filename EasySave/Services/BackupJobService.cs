using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

public class BackupJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly PathService _paths;
    private readonly List<BackupJob> _jobs;

    public BackupJobService(PathService paths)
    {
        _paths = paths;
        _jobs  = LoadFromDisk();
    }

    public List<BackupJob> GetAll() => _jobs.ToList();

    public BackupJob? GetById(int id) => _jobs.FirstOrDefault(j => j.Id == id);

    public bool IdExists(int id) => _jobs.Any(j => j.Id == id);

    public int Count => _jobs.Count;

    public void Add(BackupJob job)
    {
        if (_jobs.Count >= AppConfig.MaxJobs)
            throw new InvalidOperationException($"Maximum number of backup jobs ({AppConfig.MaxJobs}) reached.");
        if (_jobs.Any(j => j.Id == job.Id))
            throw new InvalidOperationException($"Job ID {job.Id} already exists.");

        _jobs.Add(job);
        SaveToDisk();
    }

    public bool Update(int id, BackupJob updated)
    {
        int idx = _jobs.FindIndex(j => j.Id == id);
        if (idx < 0) return false;

        updated.Id = id;
        _jobs[idx] = updated;
        SaveToDisk();
        return true;
    }

    public bool Delete(int id)
    {
        int removed = _jobs.RemoveAll(j => j.Id == id);
        if (removed == 0) return false;
        SaveToDisk();
        return true;
    }

    private List<BackupJob> LoadFromDisk()
    {
        string path = _paths.GetJobsConfigFilePath();
        if (!File.Exists(path)) return new List<BackupJob>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<BackupJob>>(json) ?? new List<BackupJob>();
        }
        catch (JsonException)
        {
            return new List<BackupJob>();
        }
    }

    private void SaveToDisk()
    {
        File.WriteAllText(
            _paths.GetJobsConfigFilePath(),
            JsonSerializer.Serialize(_jobs, JsonOptions));
    }
}
