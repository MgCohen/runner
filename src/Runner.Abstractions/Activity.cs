namespace Runner.Abstractions;

/// <summary>
/// Receives the lines that the person who watches the runner must see, for example the
/// output of <c>claude -p</c> (CONTRACTS.md, contract B). The connector gives
/// <see cref="ConsoleActivitySink"/> if the application does not register a sink.
/// </summary>
public interface IActivitySink
{
    void Write(string line);
}

/// <summary>Writes each activity line to the console of the current process.</summary>
public sealed class ConsoleActivitySink : IActivitySink
{
    public void Write(string line) => Console.WriteLine(line);
}
