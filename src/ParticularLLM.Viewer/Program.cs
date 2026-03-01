using System.Diagnostics;
using ParticularLLM.Viewer;

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
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        break;
}

void RunHtmlExport(string[] args)
{
    string outputPath = "viewer.html";

    // Check for explicit output path
    for (int i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
        {
            outputPath = args[i];
            break;
        }
    }

    // Create temp capture directory
    var captureDir = Path.Combine(Path.GetTempPath(), $"particularllm-capture-{Guid.NewGuid():N}");
    Directory.CreateDirectory(captureDir);

    try
    {
        var testProject = Path.Combine(repoRoot!, "tests", "ParticularLLM.Tests");

        Console.WriteLine("Building test project...");
        var buildPsi = new ProcessStartInfo("dotnet", $"build \"{testProject}\" -c Release -v quiet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using (var buildProc = Process.Start(buildPsi)!)
        {
            buildProc.WaitForExit();
            if (buildProc.ExitCode != 0)
            {
                var errors = buildProc.StandardError.ReadToEnd();
                Console.Error.WriteLine($"Build failed:\n{errors}");
                return;
            }
        }

        Console.WriteLine($"Running tests with frame capture...");
        var testPsi = new ProcessStartInfo("dotnet", $"test \"{testProject}\" --no-build -c Release -v quiet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        testPsi.Environment["PARTICULARLLM_CAPTURE"] = captureDir;

        using (var testProc = Process.Start(testPsi)!)
        {
            var stdout = testProc.StandardOutput.ReadToEnd();
            var stderr = testProc.StandardError.ReadToEnd();
            testProc.WaitForExit();

            Console.Write(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.Write(stderr);

            if (testProc.ExitCode != 0)
                Console.Error.WriteLine("Warning: some tests failed, but continuing with capture export.");
        }

        // Read captured files
        var capturedFiles = Directory.GetFiles(captureDir, "*.bin");
        Console.WriteLine($"Found {capturedFiles.Length} captured test scenarios.");

        if (capturedFiles.Length == 0)
        {
            Console.Error.WriteLine("No capture files produced. Tests may not use SimulationFixture.");
            return;
        }

        Console.Write("Reading capture files...");
        var captured = CaptureReader.ReadCaptureDirectory(captureDir);
        Console.WriteLine($" {captured.Count} scenarios loaded.");

        long totalBytes = captured.Sum(s => (long)s.CompressedBase64.Length);
        Console.WriteLine($"Total compressed data: {totalBytes / 1024:N0} KB (base64)");

        var reviewState = queue.LoadState();

        // Auto-import any review JSON files in repo root
        var currentHash = queue.GetCurrentHash();
        foreach (var reviewFile in Directory.GetFiles(repoRoot!, "review-*.json")
            .Where(f => !Path.GetFileName(f).Equals("review-state.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f))
        {
            try
            {
                var (p, f, s) = queue.ImportReview(reviewFile, reviewState, currentHash);
                Console.WriteLine($"Auto-imported {Path.GetFileName(reviewFile)}: {p} pass, {f} fail, {s} skipped.");
                File.Delete(reviewFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping {Path.GetFileName(reviewFile)}: {ex.Message}");
            }
        }

        int invalidated = queue.InvalidateStaleReviews(reviewState, captured);
        if (invalidated > 0)
            Console.WriteLine($"Re-queued {invalidated} scenarios (code changed since last review).");

        // Update baseline hash so next export compares from here
        reviewState.LastExportHash = queue.GetCurrentHash();
        queue.SaveState(reviewState);

        Console.Write("Generating HTML...");
        HtmlExporter.Export(captured, outputPath, reviewState);
        Console.WriteLine(" done.");

        var fileInfo = new FileInfo(Path.GetFullPath(outputPath));
        Console.WriteLine($"Output: {fileInfo.FullName} ({fileInfo.Length / (1024 * 1024.0):F1} MB)");
    }
    finally
    {
        try { Directory.Delete(captureDir, recursive: true); }
        catch { /* best effort */ }
    }
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

    Console.WriteLine($"Imported: {passed} pass, {failed} fail, {skipped} skipped.");
}

void PrintUsage()
{
    Console.WriteLine("ParticularLLM Viewer");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --html              Run tests, capture frames, export viewer.html");
    Console.WriteLine("  dotnet run -- --html out.html     Export to specific file");
    Console.WriteLine("  dotnet run -- --import review.json  Import review results from browser");
}
