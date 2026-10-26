using Microsoft.Win32;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

/// <summary>Settings under HKCU\SOFTWARE\ViggiSlideshow, the key the old version used.</summary>
internal sealed class RegistrySettingsStore : ISettingsStore
{
    private const string KeyPath = @"SOFTWARE\ViggiSlideshow";

    public string? GetString(string name) => Read(name)?.ToString();

    public int? GetInt(string name) => Read(name) switch
    {
        int i => i,
        string s when int.TryParse(s, out int parsed) => parsed,
        _ => null,
    };

    public void SetString(string name, string value) => Write(name, value, RegistryValueKind.String);

    public void SetInt(string name, int value) => Write(name, value, RegistryValueKind.DWord);

    private static object? Read(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name);
        }
        catch (Exception e)
        {
            Log.Error($"Reading setting {name}", e);
            return null;
        }
    }

    private static void Write(string name, object value, RegistryValueKind kind)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(name, value, kind);
        }
        catch (Exception e)
        {
            Log.Error($"Saving setting {name}", e);
        }
    }
}
