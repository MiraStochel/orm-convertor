using System.Globalization;
using System.Text;

namespace Tests.Foreign;

/// <summary>The verification levels of decision 016 as the row of a foreign artifact counts them (decision 172).</summary>
internal static class ForeignLevel
{
    /// <summary>Nothing passed: the language of the target does not read a unit.</summary>
    public const int None = 0;

    /// <summary>The language of the target reads every unit (T3: parsable).</summary>
    public const int Read = 1;

    /// <summary>The units compile under the consumer references of the target (T3: compilable).</summary>
    public const int Compiled = 2;

    /// <summary>The framework's factory accepts the artifact; HQL and JPQL compile against the model.</summary>
    public const int Accepted = 3;

    /// <summary>The query ran and returned rows (T3: runnable); whether they agree with the measure is the outcome.</summary>
    public const int Ran = 4;
}

/// <summary>
/// What became of one foreign artifact (decision 172): the highest level it reached, the
/// outcome at the level it stopped at, the diagnostic of that level, and the provenance the
/// manifest stated, copied. One line of the file of rows; both suites write the same columns,
/// which <c>ForeignArtifactPathTest</c> holds equal to the Java suite's.
/// </summary>
internal sealed class ForeignVerdict
{
    public const string Header = "artifact\tanswers\tsource\ttarget\tproducer\tlanguage\treached\toutcome\tdetail\trows\tagrees\treadMs\tcompileMs\tacceptMs\trunMs\tmetadata";

    public const string InProgress = "in progress";

    /// <summary>The outcome of a translation the tool refused with a Failure record, so that there is no artifact to judge (decision 173); the row of the experiment run alone.</summary>
    public const string Refused = "refused";
    public const string Unreadable = "unreadable";
    public const string NotCompiled = "not compiled";
    public const string NotAccepted = "not accepted";
    public const string Failed = "failed";
    public const string Differs = "differs";
    public const string Agrees = "agrees";

    /// <summary>The outcome of an artifact that has no fourth level: no query to run, or a key whose measure is a replay (LDBC).</summary>
    public const string Accepted = "accepted";

    public required string Artifact { get; init; }

    public required string Answers { get; init; }

    public required string Source { get; init; }

    public required string Target { get; init; }

    public required string Producer { get; init; }

    /// <summary>The language of the artifact's query (<see cref="ForeignArtifact.Language"/>), or <c>refused</c> where the tool made none.</summary>
    public required string Language { get; init; }

    public required string Metadata { get; init; }

    public int Reached { get; set; } = ForeignLevel.None;

    public string Outcome { get; set; } = InProgress;

    public string Detail { get; set; } = string.Empty;

    public int? Rows { get; set; }

    public bool? AgreesWithMeasure { get; set; }

    public double ReadMs { get; set; }

    public double CompileMs { get; set; }

    public double AcceptMs { get; set; }

    public double RunMs { get; set; }

    public string Row()
        => string.Join('\t',
            Artifact, Answers, Source, Target, Producer, Language,
            Reached.ToString(CultureInfo.InvariantCulture),
            Outcome,
            Clean(Detail),
            Rows?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            AgreesWithMeasure switch { true => "1", false => "0", null => string.Empty },
            ReadMs.ToString("F0", CultureInfo.InvariantCulture),
            CompileMs.ToString("F0", CultureInfo.InvariantCulture),
            AcceptMs.ToString("F0", CultureInfo.InvariantCulture),
            RunMs.ToString("F0", CultureInfo.InvariantCulture),
            Clean(Metadata));

    public string Describe()
        => $"{Artifact} ({Answers}, {Source} -> {Target}, {Producer}, {Language}): level {Reached}, {Outcome}"
           + (Rows is { } rows ? $", {rows} rows" : string.Empty)
           + (Detail.Length > 0 ? ": " + Detail : string.Empty);

    private static string Clean(string text)
        => text.Replace('\t', ' ').Replace("\r", string.Empty).Replace('\n', ' ');
}

/// <summary>The file of rows: one line per artifact under the header, rewritten whole on every change so that a run stopped from outside leaves the rows it had.</summary>
internal static class ForeignRows
{
    public const string FileName = "foreign-rows.tsv";

    public static void Write(string directory, string fileName, IEnumerable<ForeignVerdict> verdicts)
    {
        Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(ForeignVerdict.Header).Append('\n');
        foreach (var verdict in verdicts.OrderBy(verdict => verdict.Artifact, StringComparer.Ordinal))
        {
            text.Append(verdict.Row()).Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, fileName), text.ToString(), new UTF8Encoding(false));
    }
}
