using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TNTc;

namespace TNT.CLI;

public partial class Program
{
    private const string PROJECT_FOLDER_DESCRIPTION = "Project folder to look for project to translate within. Any folder with a '.tnt' folder is a folder to be translated.";

    private static int _exitCode;

    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("The .NET Translation Tool");

        rootCommand.AddCommand(CreateExtractCommand());
        rootCommand.AddCommand(CreateSyncCommand());
        rootCommand.AddCommand(CreateMissingCommand());
        rootCommand.AddCommand(CreateApplyCommand());
        rootCommand.AddCommand(CreateVerifyCommand());
        rootCommand.AddCommand(CreateInstallSkillCommand());
        rootCommand.AddCommand(CreateUpdateFromTNTCommand());
        rootCommand.AddCommand(CreateJsonTest());

        var invocationResult = rootCommand.Invoke(args);

        return _exitCode != 0 ? _exitCode : invocationResult;
    }

    /// <summary>Runs a command, reporting a failure as a message plus an exit code rather than as a stack trace. System.CommandLine handles exceptions thrown out of a handler itself, so the exit code has to be carried out of the invocation instead of thrown.</summary>
    private static void Run(Action command)
    {
        try
        {
            command();
        }
        catch (CommandFailedException e)
        {
            Console.Error.WriteLine(e.Message);
            _exitCode = e.ExitCode;
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(e.Message);
            _exitCode = 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            _exitCode = 5; // failed
        }
    }

    private static Argument<string> ProjectFolderArgument() => new Argument<string>("projectFolder", PROJECT_FOLDER_DESCRIPTION);

    private static Option<string> LanguagesOption() => new Option<string>("--languages", $"Comma-separated language codes to work on. Defaults to all of them: {LanguageHelper.AllLanguageCodes}");

    private static Option<bool> IncludePackagesOption() => new Option<bool>("--include-packages", "Import the translation tables shipped by the packages this project restored to, so a string a package already translated is never queued here and reaches the application through '.tnt-content'. Off by default.");

    private static Option<bool> ScanAssembliesOption() => new Option<bool>("--scan-assemblies", "Read the referenced assemblies for strings they will ask TNT to translate, so a package that draws UI but ships no tables still gets its strings queued here. Off by default.");

    private static Command CreateExtractCommand()
    {
        var command               = new Command("extract", "Extract all strings from all sources, refresh their source locations, and queue the ones that have no translation yet.");
        var projectFolderArg      = ProjectFolderArgument();
        var languagesOption       = LanguagesOption();
        var includePackagesOption = IncludePackagesOption();
        var scanAssembliesOption  = ScanAssembliesOption();

        command.AddArgument(projectFolderArg);
        command.AddOption(languagesOption);
        command.AddOption(includePackagesOption);
        command.AddOption(scanAssembliesOption);
        command.SetHandler((projectFolder, languages, includePackages, scanAssemblies) => Run(() => { Program.Extract(projectFolder, languages, includePackages, scanAssemblies); SkillInstaller.RefreshIfInstalled(projectFolder); }),
                           projectFolderArg, languagesOption, includePackagesOption, scanAssembliesOption);

        return command;
    }

    private static Command CreateSyncCommand()
    {
        var command              = new Command("sync", "Re-import what the referenced packages contribute and rewrite '.tnt-content' from what is on file. Run it after a package moved; queuing new work is what 'extract' is for.");
        var projectFolderArg     = ProjectFolderArgument();
        var languagesOption      = LanguagesOption();
        var scanAssembliesOption = ScanAssembliesOption();

        command.AddArgument(projectFolderArg);
        command.AddOption(languagesOption);
        command.AddOption(scanAssembliesOption);
        command.SetHandler((projectFolder, languages, scanAssemblies) => Run(() => { Program.Sync(projectFolder, languages, scanAssemblies); SkillInstaller.RefreshIfInstalled(projectFolder); }),
                           projectFolderArg, languagesOption, scanAssembliesOption);

        return command;
    }

    private static Command CreateMissingCommand()
    {
        var command            = new Command("missing", "Write the strings that still need a translation to a batch file, for a translator to fill in and hand to 'apply'.");
        var projectFolderArg   = ProjectFolderArgument();
        var languagesOption    = LanguagesOption();
        var limitOption        = new Option<int>("--limit", () => 0, "Maximum number of strings to write out. 0 (the default) writes all of them.");
        var outputOption       = new Option<string>("--output", "File to write the batch to. Writes to standard output when omitted.");
        var includeUnusedOption = new Option<bool>("--include-unused", "Also include strings no source file references any more. Off by default.");
        var packageCoveredOption = new Option<bool>("--include-package-covered", "Also include strings a referenced package already translated, so this project can word one differently. A translation of our own then shadows the package's. Off by default.");
        var retranslateOption  = new Option<string>("--retranslate", $"Comma-separated states whose translations should be redone even though they carry text, e.g. 'New,NeedsReview'. Known states: {string.Join(", ", Enum.GetNames<TranslationRecordState>())}");

        command.AddArgument(projectFolderArg);
        command.AddOption(languagesOption);
        command.AddOption(limitOption);
        command.AddOption(outputOption);
        command.AddOption(includeUnusedOption);
        command.AddOption(retranslateOption);
        command.AddOption(packageCoveredOption);
        command.SetHandler((context) => Run(() =>
                           {
                               Program.Missing(context.ParseResult.GetValueForArgument(projectFolderArg),
                                               context.ParseResult.GetValueForOption(languagesOption),
                                               context.ParseResult.GetValueForOption(limitOption),
                                               context.ParseResult.GetValueForOption(outputOption),
                                               context.ParseResult.GetValueForOption(includeUnusedOption),
                                               context.ParseResult.GetValueForOption(retranslateOption),
                                               context.ParseResult.GetValueForOption(packageCoveredOption));
                               SkillInstaller.RefreshIfInstalled(context.ParseResult.GetValueForArgument(projectFolderArg));
                           }));

        return command;
    }

    private static Command CreateApplyCommand()
    {
        var command             = new Command("apply", "Merge a filled-in batch file back into the translation files, rejecting any translation that changed a placeholder, a tag, a link or the surrounding whitespace.");
        var projectFolderArg    = ProjectFolderArgument();
        var batchArg            = new Argument<string>("batchFile", "The batch file written by 'missing', with the translations filled in.");
        var modelOption         = new Option<string>("--model", "The Claude model id that produced the translations, recorded on each record as 'GeneratedBy'.") { IsRequired = true };
        var stateOption         = new Option<string>("--state", () => nameof(TranslationRecordState.ClaudeSkillGenerated), $"The state to record the translations under. Known states: {string.Join(", ", Enum.GetNames<TranslationRecordState>())}");
        var allowWarningsOption = new Option<bool>("--allow-warnings", "Accept translations that only raise warnings (whitespace and tag-attribute differences). Off by default.");
        var forceOption         = new Option<bool>("--force", $"Overwrite translations that were reviewed ({nameof(TranslationRecordState.Final)} / {nameof(TranslationRecordState.Translated)}). Off by default.");

        command.AddArgument(projectFolderArg);
        command.AddArgument(batchArg);
        command.AddOption(modelOption);
        command.AddOption(stateOption);
        command.AddOption(allowWarningsOption);
        command.AddOption(forceOption);
        command.SetHandler((projectFolder, batchFile, model, state, allowWarnings, force) => Run(() => { Program.Apply(projectFolder, batchFile, model, state, allowWarnings, force); SkillInstaller.RefreshIfInstalled(projectFolder); }),
                           projectFolderArg, batchArg, modelOption, stateOption, allowWarningsOption, forceOption);

        return command;
    }

    private static Command CreateVerifyCommand()
    {
        var command          = new Command("verify", "Check that every translation file parses and that every translation on file kept the original's placeholders, markup, links and whitespace.");
        var projectFolderArg = ProjectFolderArgument();
        var languagesOption  = LanguagesOption();
        var strictOption     = new Option<bool>("--strict", "Treat warnings (whitespace and tag-attribute differences) as errors too.");

        command.AddArgument(projectFolderArg);
        command.AddOption(languagesOption);
        command.AddOption(strictOption);
        command.SetHandler((projectFolder, languages, strict) => Run(() => { Program.Verify(projectFolder, languages, strict); SkillInstaller.RefreshIfInstalled(projectFolder); }), projectFolderArg, languagesOption, strictOption);

        return command;
    }

    private static Command CreateInstallSkillCommand()
    {
        var command       = new Command("install-skill", "Install the tntc-translate Claude Code skill into a repository's .claude/skills/ folder. Once installed, the other commands keep it up to date with the tool.");
        var repoRootArg   = new Argument<string>("repositoryRoot", () => ".", "The repository root - where the .claude folder lives (usually not the folder with the .tnt folder, which sits deeper in the tree).");

        command.AddArgument(repoRootArg);
        command.SetHandler((repositoryRoot) => Run(() => SkillInstaller.Install(repositoryRoot)), repoRootArg);

        return command;
    }

    private static Command CreateUpdateFromTNTCommand()
    {
        var command          = new Command("upgrade-from-tnt", "Upgrade the existing TNT translations to the new json format");
        var projectFolderArg = ProjectFolderArgument();

        command.AddArgument(projectFolderArg);
        command.SetHandler((projectFolder) => Run(() => Program.UpgradeFromTNT(projectFolder)), projectFolderArg);

        return command;
    }

    private static Command CreateJsonTest()
    {
        var command = new Command("jsonencodertest", "");
        command.SetHandler((projectFolder) => Program.JsonText());
        return command;
    }
}
