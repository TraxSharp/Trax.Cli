using System.Diagnostics;
using Trax.Cli.Models;

namespace Trax.Cli.Generator;

public class TraxProjectGenerator
{
    private readonly CodeRenderer _renderer = new();
    private readonly Action<string, string> _scaffoldHub;

    public TraxProjectGenerator()
        : this(RunDotnetNew) { }

    /// <summary>Takes the step that scaffolds the hub (name, directory), so a test can make it fail.</summary>
    internal TraxProjectGenerator(Action<string, string> scaffoldHub) => _scaffoldHub = scaffoldHub;

    public void Generate(ApiSchema schema, string outputDir, string projectName, bool force)
    {
        // Before the output directory is touched, so a refused schema deletes nothing.
        SchemaNames.Validate(schema, projectName);

        outputDir = Path.GetFullPath(outputDir);
        var replacing = Directory.Exists(outputDir);
        if (replacing)
        {
            if (!force)
                throw new InvalidOperationException(
                    $"Output directory already exists: {outputDir}. Use --force to overwrite."
                );
            EnsureReplaceable(outputDir, Directory.GetCurrentDirectory());
        }

        // Build the project in a sibling directory and swap it in only when every step has succeeded, so a
        // failure (most often `dotnet new`) leaves the existing directory as it was. A sibling keeps the swap
        // a rename on one volume.
        var parent = Path.GetDirectoryName(outputDir)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            $".{Path.GetFileName(outputDir)}.trax-generate-{Guid.NewGuid():N}"
        );
        try
        {
            Directory.CreateDirectory(staging);
            GenerateInto(schema, staging, projectName);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        if (!replacing)
        {
            Directory.Move(staging, outputDir);
            return;
        }

        var previous = Path.Combine(
            parent,
            $".{Path.GetFileName(outputDir)}.trax-previous-{Guid.NewGuid():N}"
        );
        Directory.Move(outputDir, previous);
        try
        {
            Directory.Move(staging, outputDir);
        }
        catch
        {
            Directory.Move(previous, outputDir);
            TryDeleteDirectory(staging);
            throw;
        }
        TryDeleteDirectory(previous);
    }

    private void GenerateInto(ApiSchema schema, string outputDir, string projectName)
    {
        // 1. Scaffold the hub project via dotnet new
        var hubProjectName = $"{projectName}.Hub";
        var hubDir = Path.Combine(outputDir, hubProjectName);
        _scaffoldHub(hubProjectName, hubDir);

        // 2. Create the trains library
        var trainsProjectName = $"{projectName}.Trains";
        var trainsDir = Path.Combine(outputDir, trainsProjectName);
        GenerateTrainsLibrary(schema, trainsDir, projectName);

        // 3. Add ProjectReference from hub to trains library
        AddProjectReference(hubDir, hubProjectName, trainsProjectName);

        // 4. Patch hub Program.cs to scan the trains assembly
        PatchProgramCs(hubDir, projectName);
    }

    /// <summary>
    /// Refuses a <c>--force</c> target that replacing would do real damage to: the current directory or one of
    /// its parents (<c>--output .</c>), and any directory holding a git repository or worktree.
    /// </summary>
    internal static void EnsureReplaceable(string outputDir, string currentDirectory)
    {
        var target = TrimSeparators(Path.GetFullPath(outputDir));
        var current = TrimSeparators(Path.GetFullPath(currentDirectory));
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (
            string.Equals(target, current, comparison)
            || current.StartsWith(target + Path.DirectorySeparatorChar, comparison)
            || Path.GetPathRoot(target) == target + Path.DirectorySeparatorChar
            || Path.GetPathRoot(target) == target
        )
            throw new InvalidOperationException(
                $"Refusing to replace {outputDir}: it is the current directory or one of its parents. "
                    + "Choose a new output directory."
            );

        var git = Path.Combine(target, ".git");
        if (Directory.Exists(git) || File.Exists(git))
            throw new InvalidOperationException(
                $"Refusing to replace {outputDir}: it holds a git repository (.git). "
                    + "Choose a new output directory."
            );
    }

    private static string TrimSeparators(string path) =>
        path.Length > 1
            ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : path;

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover staging directory is visible, named, and harmless.
        }
        catch (UnauthorizedAccessException) { }
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

    internal static void PatchProgramCs(string hubDir, string projectName)
    {
        var programPath = Path.Combine(hubDir, "Program.cs");
        if (!File.Exists(programPath))
            return;

        var content = File.ReadAllText(programPath);

        // Add the trains assembly alongside Program's assembly so both get scanned
        content = content.Replace(
            "typeof(Program).Assembly",
            $"typeof(Program).Assembly, typeof({projectName}.Trains.ManifestNames).Assembly"
        );

        // Add using for the trains namespace if not already present
        var trainsUsing = $"using {projectName}.Trains;";
        if (!content.Contains(trainsUsing))
        {
            var lastUsingIndex = content.LastIndexOf("using ", StringComparison.Ordinal);
            if (lastUsingIndex >= 0)
            {
                var endOfLine = content.IndexOf('\n', lastUsingIndex);
                if (endOfLine >= 0)
                {
                    content = content.Insert(endOfLine + 1, trainsUsing + "\n");
                }
            }
        }

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
