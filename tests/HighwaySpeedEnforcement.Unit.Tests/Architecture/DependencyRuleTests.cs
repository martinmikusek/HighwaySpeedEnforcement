using System.Xml.Linq;

namespace HighwaySpeedEnforcement.Unit.Tests.Architecture;

/// <summary>
/// Executable form of the dependency rule of ADR-0004, checked on the declared references of every production project:
/// dependencies point inward (host, infrastructure, application, domain) and wire contracts never reach domain or application.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string Prefix = "HighwaySpeedEnforcement.";

    private static readonly string[] NoProjects = [];

    private static readonly Dictionary<string, ProjectRule> Rules = new()
    {
        ["Processor.Contracts"] = new(NoProjects, NoPackage),
        ["Processor.Domain"] = new(NoProjects, NoPackage),
        ["Processor.Application"] = new(["Processor.Domain"], AbstractionsOnly),
        ["Processor.Infrastructure"] = new(["Processor.Application", "Processor.Contracts"], AnyPackage),
        ["Processor.Api"] = new(["Processor.Application", "Processor.Infrastructure", "Processor.Contracts"], AnyPackage),
        ["Generator.Domain"] = new(NoProjects, NoPackage),
        ["Generator.Infrastructure"] = new(["Generator.Domain", "Processor.Contracts"], AnyPackage),
        ["Generator"] = new(["Generator.Domain", "Generator.Infrastructure", "Processor.Contracts"], AnyPackage),
        ["Client.Application"] = new(NoProjects, AbstractionsOnly),
        ["Client.Infrastructure"] = new(["Client.Application", "Processor.Contracts"], AnyPackage),
        ["Client.ViewModels"] = new(["Client.Application"], AbstractionsOrMvvm),
        ["Client.Wpf"] = new(["Client.ViewModels", "Client.Application", "Client.Infrastructure"], AnyPackage),
    };

    public static TheoryData<string> ProjectNames => new(LoadProjects().Keys);

    [Fact]
    public void Every_production_project_has_a_dependency_rule()
    {
        var projects = LoadProjects().Keys.Order().ToArray();

        Assert.Equal(Rules.Keys.Order().ToArray(), projects);
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void Project_references_only_allowed_projects(string project)
    {
        var references = DeclaredIncludes(LoadProjects()[project], "ProjectReference")
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))[Prefix.Length..]);

        var unexpected = references.Except(Rules[project].Projects).ToArray();

        Assert.True(unexpected.Length == 0, $"{project} must not reference: {string.Join(", ", unexpected)}");
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void Project_uses_only_allowed_packages(string project)
    {
        var packages = DeclaredIncludes(LoadProjects()[project], "PackageReference");

        var unexpected = packages.Where(package => !Rules[project].PackageAllowed(package)).ToArray();

        Assert.True(unexpected.Length == 0, $"{project} must not use packages: {string.Join(", ", unexpected)}");
    }

    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void Only_the_wpf_host_targets_windows(string project)
    {
        var document = LoadProjects()[project];
        var targetsWindows = document.Descendants("UseWPF").Any() ||
            document.Descendants("TargetFramework").Any(element => element.Value.EndsWith("-windows", StringComparison.Ordinal));

        Assert.Equal(project == "Client.Wpf", targetsWindows);
    }

    private static bool NoPackage(string package)
    {
        return false;
    }

    private static bool AnyPackage(string package)
    {
        return true;
    }

    private static bool AbstractionsOnly(string package)
    {
        return package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) &&
            package.EndsWith(".Abstractions", StringComparison.Ordinal);
    }

    private static bool AbstractionsOrMvvm(string package)
    {
        return AbstractionsOnly(package) || package == "CommunityToolkit.Mvvm";
    }

    private static IEnumerable<string> DeclaredIncludes(XDocument document, string elementName)
    {
        return document.Descendants(elementName).Select(element => (string)element.Attribute("Include")!);
    }

    private static Dictionary<string, XDocument> LoadProjects()
    {
        var source = Path.Combine(FindRepositoryRoot(), "src");

        return Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)[Prefix.Length..], path => XDocument.Load(path));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && directory.GetFiles("*.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (the folder with the .slnx file) not found.");
    }

    private sealed record ProjectRule(string[] Projects, Func<string, bool> PackageAllowed);
}
