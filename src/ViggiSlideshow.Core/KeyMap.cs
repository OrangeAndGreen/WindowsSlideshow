namespace ViggiSlideshow.Core;

public enum Command
{
    None,
    Next,
    Previous,
    Pause,
    ToggleFileName,
    ToggleInfo,
    ToggleDebug,
    ToggleHelp,
    ToggleSettings,
    ToggleMute,
    ToggleRandom,
    CycleVideos,
    ToggleVideosOnly,
    SetInterval,
    OpenFolder,
    HideFile,
    Quit,
}

public readonly record struct KeyCommand(Command Command, int Digit = 0);

/// <summary>
/// Maps key names (WPF's <c>Key.ToString()</c>) to commands. The help text is built from the
/// same table, so it can't drift from what the keys do.
/// </summary>
public static class KeyMap
{
    private static readonly (string Keys, string Description, Command Command, string[] Names)[] Table =
    [
        ("Right / Down / N", "Next", Command.Next, ["Right", "Down", "N"]),
        ("Left / Up / B", "Previous", Command.Previous, ["Left", "Up", "B"]),
        ("Space / P", "Pause or resume", Command.Pause, ["Space", "P"]),
        ("F", "Show or hide the file name", Command.ToggleFileName, ["F"]),
        ("I", "Show or hide file info", Command.ToggleInfo, ["I"]),
        ("V", "Photos and videos / photos only / videos only", Command.CycleVideos, ["V"]),
        ("W", "Videos only on or off", Command.ToggleVideosOnly, ["W"]),
        ("M", "Sound on or off", Command.ToggleMute, ["M"]),
        ("R", "Random or folder order", Command.ToggleRandom, ["R"]),
        ("1-9, 0", "Seconds per photo: 1-5, 10, 20, 30, 60; 0 for the default", Command.SetInterval, []),
        ("S", "Show or hide settings", Command.ToggleSettings, ["S"]),
        ("D", "Show or hide debug info", Command.ToggleDebug, ["D"]),
        ("H", "Show or hide this help", Command.ToggleHelp, ["H"]),
        ("X", "Hide this file from the show for good", Command.HideFile, ["X"]),
        ("Enter / O", "Open the file's folder and exit", Command.OpenFolder, ["Return", "Enter", "O"]),
        ("Esc / Q", "Exit", Command.Quit, ["Escape", "Q"]),
    ];

    public static KeyCommand Map(string keyName)
    {
        // D0-D9 on the main keyboard, NumPad0-NumPad9 on the keypad
        if (keyName.Length == 2 && keyName[0] == 'D' && char.IsAsciiDigit(keyName[1]))
            return new KeyCommand(Command.SetInterval, keyName[1] - '0');
        if (keyName.Length == 7 && keyName.StartsWith("NumPad", StringComparison.Ordinal) && char.IsAsciiDigit(keyName[6]))
            return new KeyCommand(Command.SetInterval, keyName[6] - '0');

        foreach (var row in Table)
        {
            if (row.Names.Contains(keyName, StringComparer.Ordinal))
                return new KeyCommand(row.Command);
        }
        return new KeyCommand(Command.None);
    }

    public static IReadOnlyList<(string Keys, string Description)> Help =>
        Table.Select(row => (row.Keys, row.Description)).ToList();
}
