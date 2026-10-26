using System.Security.Cryptography;
using System.Text.Json;

namespace Sim.Editor;

/// <summary>What <see cref="ProjectTemplate.Sync"/> did to each file (relative paths).</summary>
public sealed record TemplateSyncResult(IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> KeptEdited);

/// <summary>
/// Keeps a team project in workspace/ in step with the TeamCode it was copied from (tests/compat/&lt;robot&gt;): new
/// template files are added, files the team never changed follow template updates, and files the team edited are
/// never touched. A manifest stores the SHA-256 of each file as it was copied; it lives with the simulator's own data
/// for the project (workspace/.ftcsim/&lt;project&gt;), never inside the team's repository.
/// </summary>
public static class ProjectTemplate
{
    public static TemplateSyncResult Sync(string templateDir, string projectDir, string manifestPath)
    {
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath)) ?? new()
            : new Dictionary<string, string>();
        var added = new List<string>();
        var updated = new List<string>();
        var kept = new List<string>();
        foreach (var file in Directory.GetFiles(templateDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(templateDir, file).Replace('\\', '/');
            var dest = Path.Combine(projectDir, relative);
            var templateHash = Hash(file);
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
                manifest[relative] = templateHash;
                added.Add(relative);
                continue;
            }
            var current = Hash(dest);
            if (current == templateHash)
            {
                manifest[relative] = templateHash;   // already the newest template version
            }
            else if (manifest.TryGetValue(relative, out var copied) && copied == current)
            {
                File.Copy(file, dest, overwrite: true);   // unchanged since it was copied: take the update
                manifest[relative] = templateHash;
                updated.Add(relative);
            }
            else
            {
                kept.Add(relative);   // the team edited it (or it predates the manifest): never overwrite
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new SortedDictionary<string, string>(manifest, StringComparer.Ordinal),
            new JsonSerializerOptions { WriteIndented = true }));
        return new TemplateSyncResult(added, updated, kept);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
