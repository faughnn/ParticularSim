using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ParticularLLM.Viewer.HtmlExporter;

namespace ParticularLLM.Viewer;

public class ReviewQueue
{
    private const string StateFileName = "review-state.json";

    // Source file → tags mapping: when a source file changes, scenarios with these tags are invalidated
    private static readonly Dictionary<string, string[]> SourceFileTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SimulateChunksLogic.cs"] = ["powder", "liquid", "gas", "density", "heat"],
        ["BeltManager.cs"] = ["belt"],
        ["LiftManager.cs"] = ["lift"],
        ["WallManager.cs"] = ["wall"],
        ["FurnaceBlockManager.cs"] = ["furnace", "heat"],
        ["PistonManager.cs"] = ["piston"],
        ["ClusterManager.cs"] = ["cluster"],
        ["ClusterFactory.cs"] = ["cluster"],
        ["CellSimulator.cs"] = ["powder", "liquid", "gas", "density", "heat", "belt", "lift", "wall", "furnace", "piston", "cluster"],
        ["Materials.cs"] = ["powder", "liquid", "gas"],
        ["HeatTransferSystem.cs"] = ["heat"],
        ["PhysicsSettings.cs"] = ["powder", "liquid", "gas", "lift"],
    };

    private readonly string _repoRoot;
    private readonly string _stateFilePath;

    public ReviewQueue(string repoRoot)
    {
        _repoRoot = repoRoot;
        _stateFilePath = Path.Combine(repoRoot, StateFileName);
    }

    public ReviewState LoadState()
    {
        if (!File.Exists(_stateFilePath))
            return new ReviewState();

        var json = File.ReadAllText(_stateFilePath);
        return JsonSerializer.Deserialize<ReviewState>(json, JsonOpts) ?? new ReviewState();
    }

    public void SaveState(ReviewState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOpts);
        File.WriteAllText(_stateFilePath, json);
    }

    public string GetCurrentHash()
    {
        var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
        {
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var hash = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return hash;
    }

    /// <summary>
    /// Marks scenarios for retest when relevant code has changed.
    /// For reviewed scenarios: compares against reviewedAtHash.
    /// For unreviewed scenarios: compares against lastExportHash.
    /// Returns the number of scenarios marked for retest.
    /// </summary>
    public int InvalidateStaleReviews(ReviewState state, List<ScenarioData> scenarios)
    {
        int invalidated = 0;

        // --- Pass 1: reviewed scenarios (compare against each scenario's reviewedAtHash) ---
        var byHash = new Dictionary<string, List<(string name, ScenarioData scenario)>>();
        foreach (var scenario in scenarios)
        {
            if (!state.Scenarios.TryGetValue(scenario.Name, out var review))
                continue;
            if (review.ReviewedAtHash == null)
                continue;

            if (!byHash.TryGetValue(review.ReviewedAtHash, out var list))
            {
                list = new List<(string, ScenarioData)>();
                byHash[review.ReviewedAtHash] = list;
            }
            list.Add((scenario.Name, scenario));
        }

        foreach (var (hash, reviewedScenarios) in byHash)
        {
            var changedFiles = GetChangedFiles(hash);
            if (changedFiles.Count == 0)
                continue;

            var affectedTags = BuildAffectedTags(changedFiles);

            foreach (var (name, scenario) in reviewedScenarios)
            {
                if (IsAffected(scenario, changedFiles, affectedTags))
                {
                    state.Scenarios[name].Status = "retest";
                    invalidated++;
                }
            }
        }


        return invalidated;
    }

    private static HashSet<string> BuildAffectedTags(HashSet<string> changedFiles)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in changedFiles)
        {
            if (SourceFileTags.TryGetValue(file, out var fileTags))
            {
                foreach (var tag in fileTags)
                    tags.Add(tag);
            }
        }
        return tags;
    }

    private static bool IsAffected(ScenarioData scenario, HashSet<string> changedFiles, HashSet<string> affectedTags)
    {
        // Check tag overlap with changed sim source files
        if (scenario.Tags.Any(t => affectedTags.Contains(t)))
            return true;

        // Check if the test file itself changed
        string classFile = scenario.Category + "Tests.cs";
        return changedFiles.Contains(classFile);
    }

    private HashSet<string> GetChangedFiles(string sinceHash)
    {
        var psi = new ProcessStartInfo("git", $"diff --name-only {sinceHash}..HEAD")
        {
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            files.Add(Path.GetFileName(line.Trim()));
        return files;
    }

    /// <summary>
    /// Imports review results from a browser-exported JSON file.
    /// </summary>
    public (int passed, int failed, int skipped) ImportReview(string jsonPath, ReviewState state, string currentHash)
    {
        var json = File.ReadAllText(jsonPath);
        var items = JsonSerializer.Deserialize<List<ReviewImportItem>>(json, JsonOpts)
            ?? throw new InvalidOperationException("Could not parse review JSON");

        int passed = 0, failed = 0, skipped = 0;
        foreach (var item in items)
        {
            if (item.Status == "pass")
            {
                state.Scenarios[item.Name] = new ScenarioReview
                {
                    Status = "pass",
                    Notes = item.Notes ?? "",
                    ReviewedAtHash = currentHash,
                };
                passed++;
            }
            else if (item.Status == "fail")
            {
                state.Scenarios[item.Name] = new ScenarioReview
                {
                    Status = "fail",
                    Notes = item.Notes ?? "",
                    ReviewedAtHash = currentHash,
                };
                failed++;
            }
            else
            {
                skipped++;
            }
        }

        return (passed, failed, skipped);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public class ReviewState
{
    public Dictionary<string, ScenarioReview> Scenarios { get; set; } = new();
    public string? LastExportHash { get; set; }
}

public class ScenarioReview
{
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public string? ReviewedAtHash { get; set; }
}

public class ReviewImportItem
{
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string Status { get; set; } = "unreviewed";
    public string? Notes { get; set; }
    public int? FailFrame { get; set; }
}
