using System.Text.RegularExpressions;

namespace EasySave.Views;

public static class CliParser
{
    public static List<int> Parse(string input)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(input)) return ids;

        string trimmed = input.Trim();

        var rangeMatch = Regex.Match(trimmed, @"^(\d+)-(\d+)$");
        if (rangeMatch.Success)
        {
            int start = int.Parse(rangeMatch.Groups[1].Value);
            int end   = int.Parse(rangeMatch.Groups[2].Value);
            for (int i = start; i <= end; i++) ids.Add(i);
            return ids;
        }

        foreach (string part in trimmed.Split(';'))
            if (int.TryParse(part.Trim(), out int id)) ids.Add(id);

        return ids;
    }
}
