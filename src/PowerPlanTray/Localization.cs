using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;

namespace PowerPlanTray;

internal static class Localization
{
    private static readonly ResourceLoader Loader = new();
    private static readonly ResourceContext Context = new ResourceManager().CreateResourceContext();

    public static string Get(string key) => Loader.GetString(key);

    public static string Format(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);

    public static FlowDirection FlowDirection
    {
        get
        {
            string language = Context.QualifierValues.TryGetValue("Language", out string? value)
                ? value
                : System.Globalization.CultureInfo.CurrentUICulture.Name;
            string primaryLanguage = language.Split('-')[0];
            return primaryLanguage is "ar" or "ur" or "fa"
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }
    }
}
