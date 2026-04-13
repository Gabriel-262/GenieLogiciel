using EasySave.Models;
using System.Text.Json;

namespace EasySave.Services;

public class StateService
{
    private readonly string _stateFilePath;
    private readonly List<StateEntry> _states = new();
    private static readonly object Lock = new();

    public StateService(PathService pathService)
    {
        _stateFilePath = pathService.GetStateFilePath();
    }

    public void UpdateState(StateEntry entry)
    {
        entry.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        lock (Lock)
        {
            var existing = _states.FirstOrDefault(s => s.Name == entry.Name);
            if (existing != null) _states.Remove(existing);
            _states.Add(entry);
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(
                _states, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public void SetInactive(string name) =>
        UpdateState(new StateEntry { Name = name, Status = "Inactive" });
}
