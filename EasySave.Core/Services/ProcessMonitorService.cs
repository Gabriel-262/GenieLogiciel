using System.Diagnostics;

namespace EasySave.Services;

public class ProcessMonitorService : IBusinessSoftwareMonitor
{
    private readonly Func<string?> _processNameProvider;

    public ProcessMonitorService(Func<string?> processNameProvider)
    {
        _processNameProvider = processNameProvider;
    }

    public bool IsRunning()
    {
        string? name = _processNameProvider();
        if (string.IsNullOrWhiteSpace(name)) return false;

        string normalized = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;

        try
        {
            return Process.GetProcessesByName(normalized).Any();
        }
        catch
        {
            return false;
        }
    }
}
