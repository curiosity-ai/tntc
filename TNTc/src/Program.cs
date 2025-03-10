using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Collections.Generic;
using System.Linq;

namespace TNT.CLI;

public partial class Program
{
    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("The .NET Translation Tool");

        rootCommand.AddCommand(CreateExtractCommand());
        rootCommand.AddCommand(CreateUpdateFromTNTCommand());
        rootCommand.AddCommand(CreateJsonTest());

        try
        {
            return rootCommand.Invoke(args);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 5; // failed
        }
    }

    private static Command CreateExtractCommand()
    {
        var command          = new Command("extract", "Extract all strings from all sources and update the translations.");
        var projectFolderArg = new Argument<string>("projectFolder", "Project folder to look for project to translate withing. Any folder with a '.tnt' folder is a folder to be translated.");
        command.AddArgument(projectFolderArg);
        command.SetHandler((projectFolder) => Program.Extract(projectFolder), projectFolderArg);
        return command;
    }
    
    private static Command CreateUpdateFromTNTCommand()
    {
        var command          = new Command("upgrade-from-tnt", "Upgrade the existing TNT translations to the new json format");
        var projectFolderArg = new Argument<string>("projectFolder", "Project folder to look for project to translate withing. Any folder with a '.tnt' folder is a folder to be translated.");
        command.AddArgument(projectFolderArg);
        command.SetHandler((projectFolder) => Program.UpgradeFromTNT(projectFolder), projectFolderArg);
        return command;
    }
    
    private static Command CreateJsonTest()
    {
        var command = new Command("jsonencodertest", "");
        command.SetHandler((projectFolder) => Program.JsonText());
        return command;
    }
}