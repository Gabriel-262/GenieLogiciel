using System.Diagnostics;

namespace EasySave.Services;

/// <summary>
/// Mode "Rapide" — délègue le chiffrement XOR à l'exe externe CryptoSoft (VRE).
/// CLI: CryptoSoft.exe &lt;file&gt; &lt;key&gt;.
/// Convention de retour VRE: ExitCode = durée en ms (>=0), -1 fichier introuvable, -99 exception.
/// On propage ces codes tels quels dans CryptoTimeMs (avec -2 ajouté pour "exe introuvable").
/// </summary>
public class XorCryptoService : ICryptoSoft
{
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
    }

    private string ResolveExePath()
    {
        string? configured = _settings.Current.CryptoSoftPath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string baseDir = AppContext.BaseDirectory;
        string exeName = OperatingSystem.IsWindows() ? "CryptoSoft.exe" : "CryptoSoft";

        string sideBySide = Path.Combine(baseDir, exeName);
        if (File.Exists(sideBySide)) return sideBySide;

        return Path.GetFullPath(Path.Combine(
            baseDir, "..", "..", "..", "..", "CryptoSoft", "bin", "Debug", "net8.0", exeName));
    }
}
