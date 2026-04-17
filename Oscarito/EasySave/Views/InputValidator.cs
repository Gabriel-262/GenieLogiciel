namespace EasySave.Views;

public static class InputValidator
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    // Returns true if the name is non-empty and contains no forbidden file system characters
    public static bool IsValidJobName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return !name.Any(c => InvalidNameChars.Contains(c));
    }

    // Returns true if the path syntax is valid
    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { Path.GetFullPath(path); return true; }
        catch { return false; }
    }

    // Returns true if the path is valid AND the directory exists on disk
    public static bool IsExistingDirectory(string path) =>
        IsValidPath(path) && Directory.Exists(path);
}
