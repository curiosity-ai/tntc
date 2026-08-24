namespace TNT.CLI;

/// <summary>A command that failed in an expected way, carrying the exit code the process should report.</summary>
public sealed class CommandFailedException : Exception
{
    public int ExitCode { get; }

    public CommandFailedException(string message, int exitCode = 1) : base(message) => ExitCode = exitCode;
}
