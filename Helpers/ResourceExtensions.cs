using Microsoft.Windows.ApplicationModel.Resources;

namespace RyTuneX.Helpers;

public static class ResourceExtensions
{
    private static readonly ResourceLoader _resourceLoader = new();

    public static string GetLocalized(this string resourceKey)
    {
        // WinAppSDK ResourceLoader uses '/' as the path delimiter, not '.'
        // Keys like "PageName_Control.Text" must be looked up as "PageName_Control/Text".
        var lookupKey = resourceKey.Contains('.') ? resourceKey.Replace('.', '/') : resourceKey;
        return _resourceLoader.GetString(lookupKey);
    }

    // Tries to get a localized string, returning null if the resource is not found.
    // Handles both dot format (Feature.Header) and slash format (Feature/Header).

    public static string? TryGetLocalized(this string resourceKey)
    {
        try
        {
            var value = _resourceLoader.GetString(resourceKey);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // Resource not found with original key
        }

        // Try converting slash to dot or vice versa
        try
        {
            string altKey;
            if (resourceKey.Contains('/'))
            {
                altKey = resourceKey.Replace('/', '.');
            }
            else if (resourceKey.Contains('.'))
            {
                altKey = resourceKey.Replace('.', '/');
            }
            else
            {
                return null;
            }

            var value = _resourceLoader.GetString(altKey);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }
}
