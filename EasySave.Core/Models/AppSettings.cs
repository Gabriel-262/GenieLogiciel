namespace EasySave.Models;

public class AppSettings
{
    public bool AutoAssignJobId { get; set; } = false;
    public string Language { get; set; } = "en";
    public string BackKey { get; set; } = "r";
    public string? LogFormat { get; set; }
    public int? MaxJobs { get; set; }
    public string? LogPath { get; set; }
    public string? StatePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? LangPath { get; set; }

    // v2 (WPF): "light" or "dark". Ignored by the CLI.
    public string Theme { get; set; } = "light";

    // TODO (Oscar): nom du processus du logiciel métier (ex: "Calculator", "notepad").
    // Ajouter: public string? BusinessSoftwareName { get; set; }

    // TODO (Bastien): extensions à chiffrer via CryptoSoft (ex: [".txt", ".pdf"]).
    // Ajouter: public List<string> EncryptedExtensions { get; set; } = new();
}
