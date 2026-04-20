using System.Text.Json;
using EasySave.Interfaces;
using EasySave.Models;

namespace EasySave.Services;

public class StateService : IStateManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _fileLock = new();
    private readonly PathService _paths;

    public StateService(PathService paths)
    {
        _paths = paths;
    }

    public void UpdateState(StateEntry entry)
    {
        lock (_fileLock)
        {
            var states = ReadStates();
            int idx = states.FindIndex(s => s.JobName == entry.JobName);
            if (idx >= 0) states[idx] = entry;
            else states.Add(entry);
            WriteStates(states);
        }
    }

    public void ClearState(string jobName)
    {
        lock (_fileLock)
        {
            var states = ReadStates();
            int idx = states.FindIndex(s => s.JobName == jobName);
            if (idx < 0) return;

            states[idx] = new StateEntry
            {
                JobName = jobName,
                LastActionTime = DateTime.Now,
                Status = JobStatus.Inactive
            };
            WriteStates(states);
        }
    }

    private List<StateEntry> ReadStates()
    {
        string path = _paths.GetStateFilePath();
        if (!File.Exists(path)) return new List<StateEntry>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<StateEntry>>(json) ?? new List<StateEntry>();
        }
        catch (JsonException)
        {
            return new List<StateEntry>();
        }
    }

    private void WriteStates(List<StateEntry> states)
    {
        File.WriteAllText(
            _paths.GetStateFilePath(),
            JsonSerializer.Serialize(states, JsonOptions));
    }
}
