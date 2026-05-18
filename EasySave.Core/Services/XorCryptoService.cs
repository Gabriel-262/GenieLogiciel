using System.Diagnostics;

namespace EasySave.Services;

/// <summary>
/// Mode "Rapide" — délègue le chiffrement XOR à l'exe externe CryptoSoft (VRE).
/// CLI: CryptoSoft.exe &lt;file&gt; &lt;key&gt;.
/// Codes de retour CryptoSoft :
///     >=0  durée en ms
///     -1   fichier introuvable
///     -99  exception interne
///     -100 une autre instance tourne déjà (mono-instance)
/// </summary>
public class XorCryptoService : ICryptoSoft
{
    // File d'attente IN-PROCESS : sérialise les appels CryptoSoft émis par
    // EasySave depuis ses threads de sauvegarde parallèles. Combiné avec le
    // Mutex côté CryptoSoft.exe, ça garantit qu'on ne se prend jamais
    // d'exit -100 ("already running") en interne.
    private static readonly SemaphoreSlim _queue = new(1, 1);

    private readonly SettingsService _settings;

    public XorCryptoService(SettingsService settings)
    {
        _settings = settings;
    }

    public long Encrypt(string filePath)
    {
        if (!File.Exists(filePath)) return -1;

        string exePath = ResolveExePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return -2;

        string key = string.IsNullOrEmpty(_settings.Current.CryptoKey)
            ? "EasySaveDefaultKey"
            : _settings.Current.CryptoKey;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add(key);

        _queue.Wait();
        try
        {
            using var process = Process.Start(psi);
            if (process is null) return -2;
            process.WaitForExit();

            int code = process.ExitCode;
            // VRE renvoie 0 quand l'opération est instantanée. On clamp à 1
            // pour rester dans la convention "0 = pas de chiffrement".
            if (code == 0) return 1;
            return code;
        }
        catch
        {
            return -2;
        }
        finally
        {
            _queue.Release();
        }
    }

    private string ResolveExePath()
    {
        string? configured = _settings.Current.CryptoSoftPath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string baseDir = AppContext.BaseDirectory;
        string exeName = OperatingSystem.IsWindows() ? "CryptoSoft.exe" : "CryptoSoft";

        string sideBySide = Path.Combine(baseDir, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

        string rid = OperatingSystem.IsWindows() ? "win-x64"
                   : OperatingSystem.IsLinux()   ? "linux-x64"
                   : OperatingSystem.IsMacOS()   ? "osx-x64"
                   : "";

        string[] candidates =
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Debug", "net8.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Debug", "net8.0", rid, exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Release", "net8.0", exeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Release", "net8.0", rid, exeName),
        };

        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        return Path.GetFullPath(candidates[0]);
    }
}
