using System.IO.Compression;
using static ParticularLLM.Viewer.HtmlExporter;

namespace ParticularLLM.Viewer;

public static class CaptureReader
{
    private static readonly Dictionary<string, string[]> TagKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["powder"] = ["powder", "sand"],
        ["liquid"] = ["liquid", "water"],
        ["gas"] = ["gas", "steam"],
        ["density"] = ["density"],
        ["belt"] = ["belt"],
        ["lift"] = ["lift"],
        ["wall"] = ["wall"],
        ["furnace"] = ["furnace"],
        ["heat"] = ["furnace", "heat", "reaction"],
        ["piston"] = ["piston"],
        ["cluster"] = ["cluster"],
    };

    public static List<ScenarioData> ReadCaptureDirectory(string captureDir)
    {
        var results = new List<ScenarioData>();

        if (!Directory.Exists(captureDir))
            return results;

        foreach (var filePath in Directory.GetFiles(captureDir, "*.bin").OrderBy(f => f))
        {
            var data = ReadCaptureFile(filePath);
            if (data != null)
                results.Add(data);
        }

        // Sort by category then name
        results.Sort((a, b) =>
        {
            int cmp = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private static readonly byte[] V2Magic = { 0x50, 0x4C, 0x76, 0x32 }; // "PLv2"

    private static ScenarioData? ReadCaptureFile(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);

            // Read first 4 bytes to detect format
            var first4 = new byte[4];
            if (fs.Read(first4, 0, 4) < 4)
                return null;

            bool isV2 = first4[0] == V2Magic[0]
                     && first4[1] == V2Magic[1]
                     && first4[2] == V2Magic[2]
                     && first4[3] == V2Magic[3];

            int width, height, frameCount;

            if (isV2)
            {
                // v2: magic already consumed, read width/height/frameCount
                var header = new byte[12];
                if (fs.Read(header, 0, 12) < 12)
                    return null;
                width = BitConverter.ToInt32(header, 0);
                height = BitConverter.ToInt32(header, 4);
                frameCount = BitConverter.ToInt32(header, 8);
            }
            else
            {
                // v1: first 4 bytes are width, read remaining 8 bytes for height/frameCount
                width = BitConverter.ToInt32(first4, 0);
                var rest = new byte[8];
                if (fs.Read(rest, 0, 8) < 8)
                    return null;
                height = BitConverter.ToInt32(rest, 0);
                frameCount = BitConverter.ToInt32(rest, 4);
            }

            if (width <= 0 || height <= 0 || frameCount <= 0)
                return null;

            // Read description (length-prefixed UTF-8 string)
            string description = "";
            var descLenBytes = new byte[4];
            if (fs.Read(descLenBytes, 0, 4) == 4)
            {
                int descLen = BitConverter.ToInt32(descLenBytes, 0);
                if (descLen > 0 && descLen < 100_000)
                {
                    var descBytes = new byte[descLen];
                    if (fs.Read(descBytes, 0, descLen) == descLen)
                        description = System.Text.Encoding.UTF8.GetString(descBytes);
                }
            }

            // Read furnace metadata (v2 only)
            FurnaceBlockInfo[]? furnaceBlocks = null;
            if (isV2)
            {
                var countBytes = new byte[4];
                if (fs.Read(countBytes, 0, 4) == 4)
                {
                    int furnaceBlockCount = BitConverter.ToInt32(countBytes, 0);
                    if (furnaceBlockCount < 0 || furnaceBlockCount > 10_000)
                        return null; // corrupt data guard

                    furnaceBlocks = new FurnaceBlockInfo[furnaceBlockCount];
                    for (int i = 0; i < furnaceBlockCount; i++)
                    {
                        var blockData = new byte[5]; // int16 + int16 + byte
                        if (fs.Read(blockData, 0, 5) < 5)
                            return null;
                        int gridX = BitConverter.ToInt16(blockData, 0);
                        int gridY = BitConverter.ToInt16(blockData, 2);
                        byte direction = blockData[4];
                        furnaceBlocks[i] = new FurnaceBlockInfo(gridX, gridY, direction);
                    }
                }
            }

            // Read gzip body and base64 encode it
            using var bodyMs = new MemoryStream();
            fs.CopyTo(bodyMs);
            string compressedBase64 = Convert.ToBase64String(bodyMs.ToArray());

            // Derive metadata from filename
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var (category, name) = ParseFileName(fileName);
            var tags = InferTags(fileName);
            if (string.IsNullOrEmpty(description))
                description = $"Test: {fileName}";

            return new ScenarioData(name, category, description, width, height, frameCount, compressedBase64, tags,
                HasTemperature: isV2,
                FurnaceBlocks: furnaceBlocks);
        }
        catch
        {
            return null;
        }
    }

    private static (string category, string name) ParseFileName(string fileName)
    {
        // Format: ClassName.MethodName or ClassName.MethodName_2
        int dotIndex = fileName.IndexOf('.');
        if (dotIndex < 0)
            return ("Tests", fileName.Replace('_', ' '));

        string className = fileName[..dotIndex];
        string methodName = fileName[(dotIndex + 1)..];

        // Strip trailing _N suffix (dedup counter)
        int lastUnderscore = methodName.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(methodName[(lastUnderscore + 1)..], out _))
            methodName = methodName[..lastUnderscore];

        // Category: strip "Tests" suffix from class name
        string category = className;
        if (category.EndsWith("Tests", StringComparison.Ordinal))
            category = category[..^5];

        // Name: replace underscores with spaces
        string name = methodName.Replace('_', ' ');

        return (category, name);
    }

    private static string[] InferTags(string fileName)
    {
        string lower = fileName.ToLowerInvariant();

        // Integration/interaction/combo tests get all tags
        if (lower.Contains("integration") || lower.Contains("interaction") || lower.Contains("combo"))
        {
            return ["powder", "liquid", "gas", "density", "heat", "belt", "lift", "wall", "furnace", "piston", "cluster"];
        }

        var tags = new HashSet<string>();
        foreach (var (tag, keywords) in TagKeywords)
        {
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw))
                {
                    tags.Add(tag);
                    break;
                }
            }
        }

        // Density tests also imply powder+liquid
        if (tags.Contains("density"))
        {
            tags.Add("powder");
            tags.Add("liquid");
        }

        return tags.Count > 0 ? tags.ToArray() : ["general"];
    }
}
