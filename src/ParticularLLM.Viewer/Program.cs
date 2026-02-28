using ParticularLLM.Viewer;
using ParticularLLM.Viewer.Scenarios;

// Collect all scenarios
var providers = new IScenarioProvider[]
{
    new CoreScenarios(),
    new StructureScenarios(),
    new InteractionScenarios(),
};

var scenarios = new List<ScenarioDef>();
foreach (var provider in providers)
    scenarios.AddRange(provider.GetScenarios());

// Find repo root (where .git lives)
var repoRoot = Directory.GetCurrentDirectory();
while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot, ".git")))
    repoRoot = Directory.GetParent(repoRoot)?.FullName;
repoRoot ??= Directory.GetCurrentDirectory();

var queue = new ReviewQueue(repoRoot);

// Parse args
if (args.Length == 0)
{
    PrintUsage();
    return;
}

switch (args[0])
{
    case "--html":
        RunHtmlExport(args);
        break;
    case "--import":
        RunImport(args);
        break;
    case "--status":
        RunStatus();
        break;
    case "--list":
        RunList();
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        break;
}

void RunHtmlExport(string[] args)
{
    bool exportAll = args.Contains("--all");
    string outputPath = "viewer.html";

    // Check for explicit output path (--html output.html or --html --all output.html)
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] != "--all" && !args[i].StartsWith("--"))
        {
            outputPath = args[i];
            break;
        }
    }

    List<ScenarioDef> toExport;
    if (exportAll)
    {
        toExport = scenarios;
        Console.WriteLine($"Exporting ALL {toExport.Count} scenarios...");
    }
    else
    {
        var state = queue.LoadState();
        toExport = queue.ComputeQueue(scenarios, state);

        if (toExport.Count == 0)
        {
            Console.WriteLine("Queue is empty — all scenarios have been reviewed and no code has changed.");
            Console.WriteLine("Use --html --all to export all scenarios anyway.");
            return;
        }

        // Shuffle for variety
        var rng = new Random();
        toExport = toExport.OrderBy(_ => rng.Next()).ToList();

        Console.WriteLine($"Exporting {toExport.Count} queued scenarios...");
        queue.SaveState(state); // Save any re-queue removals from ComputeQueue
    }

    HtmlExporter.Export(toExport, outputPath);

    // Update lastExportHash
    var exportState = queue.LoadState();
    exportState.LastExportHash = queue.GetCurrentHash();
    queue.SaveState(exportState);

    Console.WriteLine($"Done! Open {Path.GetFullPath(outputPath)} in a browser.");
}

void RunImport(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: dotnet run -- --import <review.json>");
        return;
    }

    var jsonPath = args[1];
    if (!File.Exists(jsonPath))
    {
        Console.Error.WriteLine($"File not found: {jsonPath}");
        return;
    }

    var state = queue.LoadState();
    var currentHash = queue.GetCurrentHash();
    var (passed, failed, skipped) = queue.ImportReview(jsonPath, state, currentHash);
    queue.SaveState(state);

    // Compute remaining queue
    var remaining = queue.ComputeQueue(scenarios, state);
    Console.WriteLine($"Imported: {passed} pass, {failed} fail, {skipped} skipped. Queue: {remaining.Count} remaining.");
}

void RunStatus()
{
    var state = queue.LoadState();
    queue.PrintStatus(scenarios, state);
}

void RunList()
{
    Console.WriteLine($"Available scenarios ({scenarios.Count}):");
    Console.WriteLine();
    string? lastCategory = null;
    for (int i = 0; i < scenarios.Count; i++)
    {
        var s = scenarios[i];
        if (s.Category != lastCategory)
        {
            if (lastCategory != null) Console.WriteLine();
            Console.WriteLine($"  [{s.Category}]");
            lastCategory = s.Category;
        }
        Console.WriteLine($"    {i + 1,2}. {s.Name,-30} [{string.Join(", ", s.Tags)}]  {s.Width}x{s.Height}  {s.SuggestedFrames} frames");
    }
}

void PrintUsage()
{
    Console.WriteLine("ParticularLLM Viewer");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --html              Export queued scenarios to viewer.html");
    Console.WriteLine("  dotnet run -- --html --all        Export ALL scenarios (ignore queue)");
    Console.WriteLine("  dotnet run -- --html out.html     Export to specific file");
    Console.WriteLine("  dotnet run -- --import review.json  Import review results from browser");
    Console.WriteLine("  dotnet run -- --status            Show review queue status");
    Console.WriteLine("  dotnet run -- --list              List all scenarios with tags");
}
