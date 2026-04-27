using System.Diagnostics;
using EasySave.Models;

namespace EasySave.Services;

public class CryptoSoftService : ICryptoSoft
{
    private readonly SettingsService _settings;

    public CryptoSoftService(SettingsService settings)
    {
        _settings = settings;
    }

    public long Encrypt(string filePath)
    {
        if (!File.Exists(filePath)) return -1;

        string exePath = ResolveExePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return -2;

        string algo = ResolveAlgo(_settings.Current.CryptoMode);
        string key  = string.IsNullOrEmpty(_settings.Current.CryptoKey)
            ? "EasySaveDefaultKey"
            : _settings.Current.CryptoKey;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow  = true,
            RedirectStandardError  = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add(key);
        psi.ArgumentList.Add(algo);

        var sw = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return -2;
            process.WaitForExit();
            sw.Stop();

            if (process.ExitCode != 0) return -3;
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch
        {
            return -2;
        }
    }

    private string ResolveExePath()
    {
        string? configured = _settings.Current.CryptoSoftPath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string baseDir = AppContext.BaseDirectory;
        string exeName = OperatingSystem.IsWindows() ? "CryptoSoft.exe" : "CryptoSoft";

        // Recherche standard: à côté de l'exe EasySave puis dans une sortie sœur (dev).
        string sideBySide = Path.Combine(baseDir, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

        string devSibling = Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Debug", "net8.0", exeName));
        return devSibling;
    }

    private static string ResolveAlgo(string? mode) =>
        string.Equals(mode, "Standard", StringComparison.OrdinalIgnoreCase) ? "aes" : "xor";
}
