using System.Globalization;
using System.Resources;

namespace EasySave.Resources;

public static class Translator
{
    private static readonly ResourceManager Manager =
        new("EasySave.Resources.Strings", typeof(Translator).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static void SetLanguage(string cultureName) =>
        CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
}
