using System.Reflection;
using System.Text;

namespace TNT.CLI;

/// <summary>Installs the tntc-translate Claude Code skill into a repository's <c>.claude/skills/</c> folder. The skill files are embedded in the tool (a dotnet tool package cannot be PackageReference'd, so there is no MSBuild hook to copy them from the package), and a <c>.skills-version</c> marker keyed on the tool version keeps re-installs cheap and makes upgrades show up as a reviewable diff.</summary>
public static class SkillInstaller
{
    public const string SKILL_NAME = "tntc-translate";

    private const string RESOURCE_PREFIX = "skills/tntc-translate/";
    private const string VERSION_MARKER  = ".skills-version";

    public static string ToolVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrEmpty(informational)) return "0.0.0";

            var plus = informational.IndexOf('+'); // strip the SourceLink commit suffix

            return plus > 0 ? informational.Substring(0, plus) : informational;
        }
    }

    /// <summary>Writes the skill into <paramref name="repositoryRoot"/><c>/.claude/skills/tntc-translate/</c>, replacing whatever version is there.</summary>
    public static void Install(string repositoryRoot)
    {
        if (!Directory.Exists(repositoryRoot)) throw new CommandFailedException($"{repositoryRoot} does not exist.");

        var skillFolder = Path.Combine(repositoryRoot, ".claude", "skills", SKILL_NAME);

        WriteSkill(skillFolder);
        Console.WriteLine($"Installed the {SKILL_NAME} skill (version {ToolVersion}) into {Path.GetFullPath(skillFolder)}.");
        Console.WriteLine("Commit the folder - it is how the skill reaches everyone working in the repository.");
    }

    /// <summary>Re-writes an already-installed skill when its <c>.skills-version</c> differs from the tool's. A repository that never ran <c>install-skill</c> is left alone.</summary>
    public static void RefreshIfInstalled(string projectFolder)
    {
        var skillFolder = FindInstalledSkillFolder(projectFolder);

        if (skillFolder is null) return;

        var markerPath = Path.Combine(skillFolder, VERSION_MARKER);
        var installed  = File.Exists(markerPath) ? File.ReadAllText(markerPath, Encoding.UTF8).Trim() : "";

        if (installed == ToolVersion) return;

        WriteSkill(skillFolder);

        // stderr, so 'tntc missing' without --output keeps its stdout parseable
        Console.Error.WriteLine($"Updated the {SKILL_NAME} skill in {Path.GetFullPath(skillFolder)} ({(installed.Length == 0 ? "unknown" : installed)} -> {ToolVersion}). Commit the diff.");
    }

    private static void WriteSkill(string skillFolder)
    {
        Directory.CreateDirectory(skillFolder);

        var assembly = Assembly.GetExecutingAssembly();
        var written  = 0;

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(RESOURCE_PREFIX, StringComparison.Ordinal)) continue;

            var relativePath = resourceName.Substring(RESOURCE_PREFIX.Length).Replace('/', Path.DirectorySeparatorChar);
            var targetPath   = Path.Combine(skillFolder, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using var resource = assembly.GetManifestResourceStream(resourceName)!;
            using var file     = File.Create(targetPath);
            resource.CopyTo(file);
            written++;
        }

        if (written == 0) throw new CommandFailedException("The tool carries no embedded skill files - this is a packaging bug in TNTC itself.");

        File.WriteAllText(Path.Combine(skillFolder, VERSION_MARKER), ToolVersion, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Walks up from the project folder looking for an existing <c>.claude/skills/tntc-translate/</c>, stopping at the filesystem root. The project folder is usually nested inside the repository (the repository root is where <c>.claude</c> lives).</summary>
    private static string? FindInstalledSkillFolder(string projectFolder)
    {
        var current = new DirectoryInfo(Path.GetFullPath(projectFolder));

        while (current is object)
        {
            var candidate = Path.Combine(current.FullName, ".claude", "skills", SKILL_NAME);

            if (Directory.Exists(candidate)) return candidate;

            current = current.Parent;
        }

        return null;
    }
}
