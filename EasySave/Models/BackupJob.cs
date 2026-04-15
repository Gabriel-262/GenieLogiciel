namespace EasySave.Models
{

    public enum BackupType
    {
        Full,
        Differential
    }
    //Entite BackupJob

    public class BackupJob
    {

        public string Name { get; set; } = string.Empty;

        public string SourceDirectory { get; set; } = string.Empty;

        public string TargetDirectory { get; set; } = string.Empty;

        public BackupType Type { get; set; } = BackupType.Full;
    }
}