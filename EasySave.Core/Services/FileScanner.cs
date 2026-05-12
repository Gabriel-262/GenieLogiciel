namespace EasySave.Services;

// Scan d'un dossier source et partition prioritaires / non prioritaires.
// Extraction de BackupEngine pour isoler la logique d'énumération de fichiers
// (testable indépendamment du moteur de copie).
public sealed class FileScanner
{
    public sealed record Result(
        List<FileInfo> PriorityFiles,
        List<FileInfo> NormalFiles,
        int TotalFiles,
        long TotalSizeBytes);

    // root : dossier racine à scanner.
    // isPriority : prédicat sur le chemin complet ; retourne true si le
    //              fichier doit être traité en priorité (extensions config).
    public Result Scan(string root, Func<string, bool> isPriority)
    {
        var priority = new List<FileInfo>();
        var normal = new List<FileInfo>();
        long size = 0;
        int count = 0;

        var files = new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("~$"));

        foreach (var f in files)
        {
            size += f.Length;
            count++;
            if (isPriority(f.FullName)) priority.Add(f);
            else normal.Add(f);
        }

        return new Result(priority, normal, count, size);
    }
}
