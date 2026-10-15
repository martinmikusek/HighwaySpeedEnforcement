namespace HighwaySpeedEnforcement.Unit.Tests.Contracts;

/// <summary>Reads the approved example files of <c>docs/contracts/examples</c>, linked into the test output.</summary>
internal static class ContractExamples
{
    public static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Contracts", "examples", fileName);
        return File.ReadAllText(path);
    }
}
