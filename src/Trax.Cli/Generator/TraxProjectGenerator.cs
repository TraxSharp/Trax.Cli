using System.Diagnostics;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

public class TraxProjectGenerator
{
    private readonly CodeRenderer _renderer = new();

    public void Generate(ApiSchema schema, string outputDir, string projectName, bool force)
    {
        // Before the output directory is touched, so a refused schema deletes nothing.
        SchemaNames.Validate(schema, projectName);

        if (Directory.Exists(outputDir))
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Output directory already exists: {outputDir}. Use --force to overwrite."
                );
            Directory.Delete(outputDir, recursive: true);
        }

        Directory.CreateDirectory(outputDir);

        // 1. Scaffold the hub project via dotnet new
        var hubProjectName = $"{projectName}.Hub";
        var hubDir = Path.Combine(outputDir, hubProjectName);
        RunDotnetNew(hubProjectName, hubDir);

        // 2. Create the trains library
        var trainsProjectName = $"{projectName}.Trains";
        var trainsDir = Path.Combine(outputDir, trainsProjectName);
        GenerateTrainsLibrary(schema, trainsDir, projectName);

        // 3. Add ProjectReference from hub to trains library
        AddProjectReference(hubDir, hubProjectName, trainsProjectName);

        // 4. Patch hub Program.cs to scan the trains assembly
        PatchProgramCs(hubDir, projectName);
    }

    internal void GenerateTrainsLibrary(ApiSchema schema, string trainsDir, string projectName)
    {
        SchemaNames.Validate(schema, projectName);

        var trainsProjectName = $"{projectName}.Trains";
        Directory.CreateDirectory(trainsDir);

        // If the schema has shared types or enums, set the models namespace
        // so generated code includes the correct using directive
        var hasSharedTypes =
            schema.Types.Any(t => !t.IsBuiltIn && t.Fields.Count > 0) || schema.Enums.Count > 0;
        if (hasSharedTypes)
            _renderer.SetModelsNamespace($"{projectName}.Trains.Models");

        // Write trains csproj
        WriteFile(
            Path.Combine(trainsDir, $"{trainsProjectName}.csproj"),
            _renderer.RenderTrainsCsproj()
        );

        // Write ManifestNames.cs
        WriteFile(
            Path.Combine(trainsDir, "ManifestNames.cs"),
            _renderer.RenderManifestNames(schema.Operations, projectName)
        );

        // Write GraphQLNamespaces.cs
        var groups = schema
            .Operations.Select(o => o.Group)
            .Where(g => g != null)
            .Cast<string>()
            .Distinct()
            .OrderBy(g => g)
            .ToList();

        if (groups.Count > 0)
        {
            WriteFile(
                Path.Combine(trainsDir, "GraphQLNamespaces.cs"),
                _renderer.RenderGraphQLNamespaces(groups, projectName)
            );
        }

        // Write shared types in Models/
        foreach (var apiType in schema.Types)
        {
            if (apiType.IsBuiltIn || apiType.Name == "Unit" || apiType.Fields.Count == 0)
                continue;

            var modelsDir = Path.Combine(trainsDir, "Models");
            Directory.CreateDirectory(modelsDir);
            WriteFile(
                Path.Combine(modelsDir, $"{apiType.Name}.cs"),
                _renderer.RenderTypeRecord(apiType, projectName, null)
            );
        }

        // Write enums in Models/
        foreach (var apiEnum in schema.Enums)
        {
            var modelsDir = Path.Combine(trainsDir, "Models");
            Directory.CreateDirectory(modelsDir);
            WriteFile(
                Path.Combine(modelsDir, $"{apiEnum.Name}.cs"),
                _renderer.RenderEnum(apiEnum, projectName)
            );
        }

        // Write trains
        foreach (var operation in schema.Operations)
        {
            var group = operation.Group ?? "General";
            var trainDir = Path.Combine(trainsDir, "Trains", group, operation.Name);
            var junctionDir = Path.Combine(trainDir, "Junctions");
            Directory.CreateDirectory(junctionDir);

            WriteFile(
                Path.Combine(trainDir, $"I{operation.Name}Train.cs"),
                _renderer.RenderTrainInterface(operation, projectName)
            );

            WriteFile(
                Path.Combine(trainDir, $"{operation.Name}Train.cs"),
                _renderer.RenderTrainImplementation(operation, projectName)
            );

            WriteFile(
                Path.Combine(trainDir, $"{operation.InputType.Name}.cs"),
                _renderer.RenderInput(operation, projectName)
            );

            if (!operation.OutputType.IsBuiltIn && operation.OutputType.Fields.Count > 0)
            {
                WriteFile(
                    Path.Combine(trainDir, $"{operation.OutputType.Name}.cs"),
                    _renderer.RenderOutput(operation, projectName)
                );
            }

            WriteFile(
                Path.Combine(junctionDir, $"{operation.Name}Junction.cs"),
                _renderer.RenderJunction(operation, projectName)
            );
        }
    }

    internal static void AddProjectReference(
        string apiDir,
        string apiProjectName,
        string trainsProjectName
    )
    {
        var csprojPath = Path.Combine(apiDir, $"{apiProjectName}.csproj");
        var content = File.ReadAllText(csprojPath);

        content = content.Replace(
            "</Project>",
            $"""

              <ItemGroup>
                <ProjectReference Include="..\{trainsProjectName}\{trainsProjectName}.csproj" />
              </ItemGroup>
            </Project>
            """
        );

        File.WriteAllText(csprojPath, content);
    }

    /// <summary>
    /// Adds the trains assembly to the hub's <c>AddMediator(typeof(Program).Assembly)</c> scan. The name is
    /// written fully qualified, so no using directive is inserted: finding where one may go needs a parse, and
    /// the template's last <c>using</c> is a statement inside a block. A template that no longer has the scan
    /// is refused, since the hub would otherwise build and register none of the generated trains.
    /// </summary>
    internal static void PatchProgramCs(string hubDir, string projectName)
    {
        const string scan = "typeof(Program).Assembly";
        var programPath = Path.Combine(hubDir, "Program.cs");
        if (!File.Exists(programPath))
            throw new InvalidOperationException(
                $"The hub template produced no Program.cs at {programPath}, so the generated trains cannot be "
                    + "registered. The installed trax-hub template does not match this version of trax."
            );

        var content = File.ReadAllText(programPath);
        if (!content.Contains(scan, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The hub's Program.cs ({programPath}) has no '{scan}' to add the trains assembly to, so the "
                    + "generated trains would not be registered. The installed trax-hub template does not match "
                    + "this version of trax."
            );

        content = content.Replace(
            scan,
            $"{scan}, typeof({projectName}.Trains.ManifestNames).Assembly",
            StringComparison.Ordinal
        );

        File.WriteAllText(programPath, content);
    }

    private static void RunDotnetNew(string name, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "new", "trax-hub", "-n", name, "-o", outputDir },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var message = stderr.Contains(
                "No templates or subcommands found",
                StringComparison.Ordinal
            )
                ? "The 'trax-hub' template is not installed. Run: dotnet new install Trax.Samples"
                : $"dotnet new failed (exit code {process.ExitCode}): {stderr}";
            throw new InvalidOperationException(message);
        }
    }

    public static bool IsDotnetAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null)
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
    }
}
