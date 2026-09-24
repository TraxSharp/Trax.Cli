using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Every train the generator scaffolds declares its chain in <c>Junctions()</c>, and nothing it
/// writes uses <c>RunInternal</c> or <c>Activate(</c>, the API the template used before.
///
/// <para><c>ScaffoldCompilationTests</c> cannot catch a return to the old template: it compiles
/// against the Trax.Core the tests deploy, which a transitive pin holds at a version where
/// <c>RunInternal</c> and <c>Activate</c> still exist, so old-shape code still compiles there. A
/// scaffold only breaks against the Trax a new project floats to. This reads the generated
/// source instead, for every fixture schema, which covers query, mutation and Unit-output
/// trains.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ScaffoldTrainShapeTests
{
    private string _outputDir = null!;

    [SetUp]
    public void SetUp() =>
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-shape-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static IEnumerable<string> FixtureSchemas() =>
        Directory
            .EnumerateFiles(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas")
            )
            .Select(Path.GetFileName)
            .Cast<string>()
            .Order();

    [TestCaseSource(nameof(FixtureSchemas))]
    public void Every_scaffolded_train_declares_its_chain_in_Junctions(string fixture)
    {
        var schema = Parse(fixture);
        new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Shape");

        var sources = Directory
            .EnumerateFiles(_outputDir, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_outputDir, f), File.ReadAllText);

        // Where the generator writes each operation's train implementation. Named per operation
        // rather than matched on "*Train.cs", which would also match the I*Train.cs interfaces.
        var trains = schema
            .Operations.Select(o =>
                Path.Combine("Trains", o.Group ?? "General", o.Name, $"{o.Name}Train.cs")
            )
            .Select(path =>
                (
                    path,
                    source: sources.GetValueOrDefault(path)
                        ?? throw new AssertionException($"no train was written at {path}")
                )
            )
            .ToList();

        trains.Should().NotBeEmpty("every fixture schema has at least one operation");

        foreach (var (path, source) in trains)
            source
                .Should()
                .Contain(
                    "protected override Task<Either<Exception, ",
                    $"{path} must override the chain declaration"
                )
                .And.Contain(
                    "Junctions() =>",
                    $"{path} must declare its chain in Junctions(), which current Trax requires"
                );

        foreach (var (path, source) in sources)
            source
                .Should()
                .NotContainAny(
                    ["RunInternal", "Activate("],
                    $"{path} would not compile against the Trax a new scaffold references"
                );
    }

    [Test]
    public void The_fixtures_include_a_Unit_output_train()
    {
        FixtureSchemas()
            .SelectMany(f => Parse(f).Operations)
            .Should()
            .Contain(
                o => o.OutputType.Name == "Unit",
                "the Unit branch of the train and junction templates is otherwise unexercised here"
            );
    }

    private static ApiSchema Parse(string fixture)
    {
        var path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "Schemas",
            fixture
        );
        return fixture.EndsWith(".graphql", StringComparison.Ordinal)
            ? new GraphQLSchemaParser().Parse(path)
            : new OpenApiSchemaParser().Parse(path);
    }
}
