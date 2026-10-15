using HighwaySpeedEnforcement.Generator.Interactive;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class KeyMapTests
{
    public static TheoryData<char, ConsoleKey, ConsoleCommand, int> MappedKeys => new()
    {
        { 'p', ConsoleKey.P, ConsoleCommand.TogglePause, 0 },
        { 'P', ConsoleKey.P, ConsoleCommand.TogglePause, 0 },
        { 'h', ConsoleKey.H, ConsoleCommand.ToggleHeartbeats, 0 },
        { 'H', ConsoleKey.H, ConsoleCommand.ToggleHeartbeats, 0 },
        { 'q', ConsoleKey.Q, ConsoleCommand.Quit, 0 },
        { 'Q', ConsoleKey.Q, ConsoleCommand.Quit, 0 },
        { '+', ConsoleKey.OemPlus, ConsoleCommand.IncreaseVehicles, 0 },
        { '+', ConsoleKey.Add, ConsoleCommand.IncreaseVehicles, 0 },
        { '-', ConsoleKey.OemMinus, ConsoleCommand.DecreaseVehicles, 0 },
        { '-', ConsoleKey.Subtract, ConsoleCommand.DecreaseVehicles, 0 },
        { '1', ConsoleKey.D1, ConsoleCommand.ToggleCamera, 1 },
        { '5', ConsoleKey.D5, ConsoleCommand.ToggleCamera, 5 },
        { '9', ConsoleKey.D9, ConsoleCommand.ToggleCamera, 9 },
        { '0', ConsoleKey.D0, ConsoleCommand.ToggleCamera, 10 },
        { '1', ConsoleKey.NumPad1, ConsoleCommand.ToggleCamera, 1 },
        { '0', ConsoleKey.NumPad0, ConsoleCommand.ToggleCamera, 10 },
    };

    [Theory]
    [MemberData(nameof(MappedKeys))]
    public void TryMap_maps_every_documented_key(char keyChar, ConsoleKey key, ConsoleCommand expected, int camera)
    {
        var info = new ConsoleKeyInfo(keyChar, key, shift: false, alt: false, control: false);

        Assert.True(KeyMap.TryMap(info, out var command));
        Assert.Equal(new KeyCommand(expected, camera), command);
    }

    [Theory]
    [InlineData(ConsoleKey.Add, ConsoleCommand.IncreaseVehicles, 0)]
    [InlineData(ConsoleKey.Subtract, ConsoleCommand.DecreaseVehicles, 0)]
    [InlineData(ConsoleKey.NumPad7, ConsoleCommand.ToggleCamera, 7)]
    [InlineData(ConsoleKey.NumPad0, ConsoleCommand.ToggleCamera, 10)]
    [InlineData(ConsoleKey.D3, ConsoleCommand.ToggleCamera, 3)]
    [InlineData(ConsoleKey.P, ConsoleCommand.TogglePause, 0)]
    [InlineData(ConsoleKey.Q, ConsoleCommand.Quit, 0)]
    public void TryMap_falls_back_to_the_key_code_when_no_character_is_reported(ConsoleKey key, ConsoleCommand expected, int camera)
    {
        var info = new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false);

        Assert.True(KeyMap.TryMap(info, out var command));
        Assert.Equal(new KeyCommand(expected, camera), command);
    }

    [Theory]
    [InlineData('x', ConsoleKey.X)]
    [InlineData(' ', ConsoleKey.Spacebar)]
    [InlineData('\r', ConsoleKey.Enter)]
    [InlineData('\u001b', ConsoleKey.Escape)]
    [InlineData('\0', ConsoleKey.F1)]
    [InlineData('\0', ConsoleKey.UpArrow)]
    [InlineData('*', ConsoleKey.Multiply)]
    public void TryMap_ignores_unknown_keys(char keyChar, ConsoleKey key)
    {
        var info = new ConsoleKeyInfo(keyChar, key, shift: false, alt: false, control: false);

        Assert.False(KeyMap.TryMap(info, out var command));
        Assert.Equal(default, command);
    }
}
