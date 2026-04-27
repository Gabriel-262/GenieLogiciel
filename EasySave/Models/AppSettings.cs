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

    public List<string> EncryptedExtensions { get; set; } = new();

    // "Rapide" (XOR) ou "Standard" (AES)
    public string CryptoMode { get; set; } = "Rapide";

    public string? CryptoKey { get; set; }
    public string? CryptoSoftPath { get; set; }
}
