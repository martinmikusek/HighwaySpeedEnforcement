namespace HighwaySpeedEnforcement.E2E.Tests.Support;

/// <summary>All whole-system tests share one broker, network and processor image; each test starts its own processor.</summary>
[CollectionDefinition(Name)]
public sealed class PipelineCollection : ICollectionFixture<PipelineStack>
{
    public const string Name = "pipeline";
}
