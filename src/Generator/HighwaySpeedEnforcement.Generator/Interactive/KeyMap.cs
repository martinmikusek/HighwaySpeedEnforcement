namespace HighwaySpeedEnforcement.Generator.Interactive;

/// <summary>What a key of design section 11 does.</summary>
public enum ConsoleCommand
{
    TogglePause,
    ToggleHeartbeats,
    IncreaseVehicles,
    DecreaseVehicles,
    ToggleCamera,
    Quit,
}

/// <summary>A mapped key; <paramref name="Camera"/> is set for <see cref="ConsoleCommand.ToggleCamera"/> only.</summary>
public readonly record struct KeyCommand(ConsoleCommand Command, int Camera = 0);

/// <summary>The key table of design section 11: pure, so it is unit tested without a terminal.</summary>
public static class KeyMap
{
    public static bool TryMap(ConsoleKeyInfo key, out KeyCommand command)
    {
        // The character first (it covers both the main keyboard and the numeric keypad on every layout), the key code as
        // a fallback for keys whose character the terminal does not report.
        switch (char.ToUpperInvariant(key.KeyChar))
        {
            case 'P':
                command = new KeyCommand(ConsoleCommand.TogglePause);
                return true;
            case 'H':
                command = new KeyCommand(ConsoleCommand.ToggleHeartbeats);
                return true;
            case 'Q':
                command = new KeyCommand(ConsoleCommand.Quit);
                return true;
            case '+':
                command = new KeyCommand(ConsoleCommand.IncreaseVehicles);
                return true;
            case '-':
                command = new KeyCommand(ConsoleCommand.DecreaseVehicles);
                return true;
            case >= '0' and <= '9':
                command = new KeyCommand(ConsoleCommand.ToggleCamera, CameraOf(key.KeyChar - '0'));
                return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.P:
                command = new KeyCommand(ConsoleCommand.TogglePause);
                return true;
            case ConsoleKey.H:
                command = new KeyCommand(ConsoleCommand.ToggleHeartbeats);
                return true;
            case ConsoleKey.Q:
                command = new KeyCommand(ConsoleCommand.Quit);
                return true;
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                command = new KeyCommand(ConsoleCommand.IncreaseVehicles);
                return true;
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                command = new KeyCommand(ConsoleCommand.DecreaseVehicles);
                return true;
            case >= ConsoleKey.D0 and <= ConsoleKey.D9:
                command = new KeyCommand(ConsoleCommand.ToggleCamera, CameraOf(key.Key - ConsoleKey.D0));
                return true;
            case >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9:
                command = new KeyCommand(ConsoleCommand.ToggleCamera, CameraOf(key.Key - ConsoleKey.NumPad0));
                return true;
        }

        command = default;
        return false;
    }

    /// <summary>Digit keys 1 to 9 are cameras 1 to 9; 0 is camera 10.</summary>
    private static int CameraOf(int digit)
    {
        return digit == 0 ? 10 : digit;
    }
}
