namespace EasySave.Models;

public class AppSettings
{
    public string Language { get; set; } = "en";
    public string BackKey { get; set; } = "r";
    public string? LogFormat { get; set; }
    public int? MaxJobs { get; set; }
    public string? LogPath { get; set; }
    public string? StatePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? LangPath { get; set; }
}
