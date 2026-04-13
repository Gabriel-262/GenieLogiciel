using System.Text.RegularExpressions;

namespace EasySave.Views;

public static class CliParser
{
    // Parses "1-3" (range) or "1;3" (discrete list) into job IDs (1-based)
    public static List<int> Parse(string input)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(input)) return ids;

        string trimmed = input.Trim();

        // Range pattern: "1-3"
        var rangeMatch = Regex.Match(trimmed, @"^(\d+)-(\d+)$");
        if (rangeMatch.Success)
        {
            int start = int.Parse(rangeMatch.Groups[1].Value);
            int end = int.Parse(rangeMatch.Groups[2].Value);
            for (int i = start; i <= end; i++) ids.Add(i);
            return ids;
        }

        // Discrete list pattern: "1;3" or "2;4;5"
        foreach (string part in trimmed.Split(';'))
        {
            if (int.TryParse(part.Trim(), out int id))
                ids.Add(id);
        }

        return ids;
    }
}
