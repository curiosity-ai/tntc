using System.Text.Json;

namespace TNTc;

/// <summary>One NuGet package a scanned project resolves to, located in the restored package folder so its shipped translation tables and its assemblies can be read.</summary>
public sealed class ReferencedPackage
{
    public string   Id         { get; init; } = "";
    public string   Version    { get; init; } = "";
    public string   Folder     { get; init; } = "";

    /// <summary>The package's implementation assemblies - its runtime assets where it has them, its reference assemblies otherwise. A reference assembly carries no method bodies, so scanning one finds nothing rather than something wrong.</summary>
    public string[] Assemblies { get; init; } = Array.Empty<string>();

    /// <summary>How a package is recorded on a translation it provided, and what <c>verify</c> reports drift against.</summary>
    public string Coordinates => $"{Id}/{Version}";
}

/// <summary>
/// Resolves the packages a project actually restored to, by reading the <c>obj/project.assets.json</c>
/// NuGet writes - the resolved graph, after version unification, with the folder each package was
/// extracted into. Nothing here restores or downloads: a project that was never restored simply
/// contributes no packages, and the caller says so.
/// </summary>
public static class PackageGraph
{
    public const string ASSETS_FILE = "project.assets.json";

    /// <summary>The resolved packages of every given project folder, unified by id (the highest version wins, as NuGet's own unification does) and ordered by id.</summary>
    public static IReadOnlyList<ReferencedPackage> Resolve(IEnumerable<string> projectFolders, out IReadOnlyList<string> foldersWithoutAssets)
    {
        var packages = new Dictionary<string, ReferencedPackage>(StringComparer.OrdinalIgnoreCase);
        var missing  = new List<string>();

        foreach (var projectFolder in projectFolders)
        {
            var assetsPath = Path.Combine(projectFolder, "obj", ASSETS_FILE);

            if (!File.Exists(assetsPath))
            {
                missing.Add(projectFolder);
                continue;
            }

            foreach (var package in ReadAssetsFile(assetsPath))
            {
                if (packages.TryGetValue(package.Id, out var existing) && CompareVersions(existing.Version, package.Version) >= 0) continue;

                packages[package.Id] = package;
            }
        }

        foldersWithoutAssets = missing;

        return packages.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<ReferencedPackage> ReadAssetsFile(string assetsPath)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{assetsPath} is not valid JSON: {e.Message}", e);
        }

        using (document)
        {
            var root           = document.RootElement;
            var packageFolders = root.TryGetProperty("packageFolders", out var folders)
                ? folders.EnumerateObject().Select(f => f.Name).ToArray()
                : Array.Empty<string>();

            if (!root.TryGetProperty("libraries", out var libraries)) yield break;

            var runtimeAssets = ReadAssets(root, "runtime");
            var compileAssets = ReadAssets(root, "compile");

            foreach (var library in libraries.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package") continue;

                var separator = library.Name.LastIndexOf('/');

                if (separator <= 0) continue;

                var relativePath = library.Value.TryGetProperty("path", out var path) ? path.GetString() : null;
                var folder       = relativePath is null ? null : packageFolders.Select(f => Path.Combine(f, relativePath)).FirstOrDefault(Directory.Exists);

                if (folder is null) continue;

                yield return new ReferencedPackage()
                {
                    Id                = library.Name.Substring(0, separator),
                    Version           = library.Name.Substring(separator + 1),
                    Folder     = Path.GetFullPath(folder),
                    Assemblies = ResolveAssemblies(folder, runtimeAssets, compileAssets, library.Name)
                };
            }
        }
    }

    private static string[] ResolveAssemblies(string folder, Dictionary<string, List<string>> runtimeAssets, Dictionary<string, List<string>> compileAssets, string library)
    {
        var assets = runtimeAssets.TryGetValue(library, out var runtime) ? runtime : compileAssets.TryGetValue(library, out var compile) ? compile : null;

        if (assets is null) return Array.Empty<string>();

        return assets.Select(a => Path.GetFullPath(Path.Combine(folder, a))).Where(File.Exists).Distinct().ToArray();
    }

    /// <summary>One kind of asset of every library, unioned across the file's targets - a project multi-targeting picks a different lib folder per framework, and either one carries the same strings.</summary>
    private static Dictionary<string, List<string>> ReadAssets(JsonElement root, string assetKind)
    {
        var assets = new Dictionary<string, List<string>>();

        if (!root.TryGetProperty("targets", out var targets)) return assets;

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty(assetKind, out var kind)) continue;

                foreach (var asset in kind.EnumerateObject())
                {
                    if (asset.Name.EndsWith("_._", StringComparison.Ordinal)) continue;
                    if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!assets.TryGetValue(library.Name, out var paths))
                    {
                        paths                 = new List<string>();
                        assets[library.Name] = paths;
                    }

                    var normalized = asset.Name.Replace('/', Path.DirectorySeparatorChar);

                    if (!paths.Contains(normalized)) paths.Add(normalized);
                }
            }
        }

        return assets;
    }

    /// <summary>Orders two NuGet versions by their numeric parts, treating a prerelease suffix as lower than the release it labels. Enough to pick between two resolutions of the same package; not a full SemVer implementation.</summary>
    private static int CompareVersions(string left, string right)
    {
        var leftRelease  = left.Split('-', 2);
        var rightRelease = right.Split('-', 2);
        var leftParts    = leftRelease[0].Split('.');
        var rightParts   = rightRelease[0].Split('.');

        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var leftPart  = index < leftParts.Length && int.TryParse(leftParts[index], out var l) ? l : 0;
            var rightPart = index < rightParts.Length && int.TryParse(rightParts[index], out var r) ? r : 0;

            if (leftPart != rightPart) return leftPart.CompareTo(rightPart);
        }

        if (leftRelease.Length == rightRelease.Length) return string.CompareOrdinal(left, right);

        return leftRelease.Length == 1 ? 1 : -1;
    }
}
