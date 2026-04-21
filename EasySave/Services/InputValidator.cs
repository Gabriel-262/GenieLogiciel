namespace EasySave.Services;

public static class InputValidator
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public static bool IsValidJobName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return !name.Any(c => InvalidNameChars.Contains(c));
    }

    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { Path.GetFullPath(path); return true; }
        catch { return false; }
    }

    public static bool IsExistingDirectory(string path) =>
        IsValidPath(path) && Directory.Exists(path);
}
