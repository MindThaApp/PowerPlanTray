using Microsoft.Windows.ApplicationModel.Resources;

namespace PowerPlanTray;

internal static class Localization
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);

    public static string Format(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
