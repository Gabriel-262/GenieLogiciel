using System.Text.RegularExpressions;

namespace EasySave.Views;

public static class CliParser
{
    // Parses "1-3" (inclusive range) or "1;3;5" (discrete list) into a list of job IDs
    public static List<int> Parse(string input)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(input)) return ids;

        string trimmed = input.Trim();

        // Range format: "1-3" → [1, 2, 3]
        var rangeMatch = Regex.Match(trimmed, @"^(\d+)-(\d+)$");
        if (rangeMatch.Success)
        {
            int start = int.Parse(rangeMatch.Groups[1].Value);
            int end   = int.Parse(rangeMatch.Groups[2].Value);
            for (int i = start; i <= end; i++) ids.Add(i);
            return ids;
        }

        // Discrete list format: "1;3;5" → [1, 3, 5]
        foreach (string part in trimmed.Split(';'))
            if (int.TryParse(part.Trim(), out int id)) ids.Add(id);

        return ids;
    }
}
