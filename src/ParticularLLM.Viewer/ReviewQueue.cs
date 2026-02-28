using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParticularLLM.Viewer.Scenarios;

namespace ParticularLLM.Viewer;

public class ReviewQueue
{
    private const string StateFileName = "review-state.json";

    // Source file → tags mapping
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
    /// Gets the set of tags affected by code changes since the given commit hash.
    /// </summary>
    public HashSet<string> GetChangedTags(string sinceHash)
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

        var changedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fileName = Path.GetFileName(line.Trim());
            if (SourceFileTags.TryGetValue(fileName, out var tags))
            {
                foreach (var tag in tags)
                    changedTags.Add(tag);
            }
        }
        return changedTags;
    }

    /// <summary>
    /// Computes which scenarios need review. Re-queues reviewed scenarios whose
    /// relevant source code has changed since their review.
    /// </summary>
    public List<ScenarioDef> ComputeQueue(List<ScenarioDef> allScenarios, ReviewState state)
    {
        // Find the oldest reviewedAtHash to minimize git diff calls
        string? oldestHash = null;
        foreach (var entry in state.Scenarios.Values)
        {
            if (entry.ReviewedAtHash != null)
            {
                // Use the first one we find; we'll do one diff from it
                if (oldestHash == null)
                    oldestHash = entry.ReviewedAtHash;
            }
        }

        HashSet<string>? changedTags = null;
        if (oldestHash != null)
        {
            changedTags = GetChangedTags(oldestHash);
        }

        var queue = new List<ScenarioDef>();
        foreach (var scenario in allScenarios)
        {
            if (!state.Scenarios.TryGetValue(scenario.Name, out var entry))
            {
                // Never reviewed
                queue.Add(scenario);
                continue;
            }

            // Check if code changed for this scenario's tags
            if (changedTags != null && scenario.Tags.Any(t => changedTags.Contains(t)))
            {
                // Re-queue: remove the stale review
                state.Scenarios.Remove(scenario.Name);
                queue.Add(scenario);
                continue;
            }

            // Reviewed and no relevant code changed — skip (it's out of queue)
        }

        return queue;
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

    /// <summary>
    /// Prints queue status summary.
    /// </summary>
    public void PrintStatus(List<ScenarioDef> allScenarios, ReviewState state)
    {
        var queue = ComputeQueue(allScenarios, state);
        int passed = state.Scenarios.Values.Count(s => s.Status == "pass");
        int failed = state.Scenarios.Values.Count(s => s.Status == "fail");
        int unreviewed = queue.Count;

        Console.WriteLine($"Queue: {unreviewed} need review, {passed} passed, {failed} failed (of {allScenarios.Count} total)");

        if (queue.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Queued scenarios:");
            foreach (var s in queue)
                Console.WriteLine($"  - {s.Name} [{string.Join(", ", s.Tags)}]");
        }
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
