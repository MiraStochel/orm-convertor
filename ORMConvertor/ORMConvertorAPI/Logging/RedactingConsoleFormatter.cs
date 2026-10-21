using System.Text;
using DatabaseCatalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ORMConvertorAPI.Logging;

/// <summary>
/// The output of the log (decision 160). The application logs to the console and nowhere
/// else - in the container that is <c>docker logs</c> -, and every line written there, the
/// message as well as the text and stack of an exception, passes through the one redaction of
/// the instance's credentials. The rule sits at the output rather than at each call, so a
/// line added later is covered without anyone having to remember it. The layout is that of
/// the default simple formatter: the level, the category with the event id, the indented
/// message, the exception.
/// </summary>
public sealed class RedactingConsoleFormatter(CredentialRedaction redaction) : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "redacting";

    private const string Indent = "      ";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(Label(logEntry.LogLevel)).Append(": ")
            .Append(logEntry.Category).Append('[').Append(logEntry.EventId.Id).Append(']')
            .Append(Environment.NewLine);

        if (!string.IsNullOrEmpty(message))
        {
            line.Append(Indent)
                .Append(message.ReplaceLineEndings(Environment.NewLine + Indent))
                .Append(Environment.NewLine);
        }

        if (logEntry.Exception is not null)
        {
            line.Append(logEntry.Exception).Append(Environment.NewLine);
        }

        textWriter.Write(redaction.Redact(line.ToString()));
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none",
    };
}
