namespace Autodraw;

internal static class UiText
{
    public const string Product = "ColorLayers by Synrik";
    public const string Version = "1.0";
    public const string Title = Product + " " + Version;
    public static bool German { get; set; }

    public static string T(string english, string german) => German ? german : english;

    public static string OutlineWarning => T("Adjust the brush width before drawing!", "Achtung, Breite vorher anpassen!");
}
