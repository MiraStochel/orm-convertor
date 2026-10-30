namespace AbstractWrappers.Diagnostics;

/// <summary>
/// A failure of the tool itself during one translation (decision 197): an exception that is
/// not a refusal of the input - refusals are Failure records written on purpose - but a defect
/// met while reading one unit, generating one entity or one query. The orchestration turns
/// it into a Failure record for the caller and keeps the exception here for whoever runs the
/// instance: the record says what was not translated, the defect says why, with the stack,
/// and it leaves the process only through the log.
/// </summary>
/// <param name="Exception">What was thrown.</param>
/// <param name="Unit">The input unit being read or whose query was being generated, as records reference units (decision 066); null for the entity generation, which belongs to no one unit.</param>
/// <param name="Entity">The entity being generated, when the defect was met in the build of one entity.</param>
/// <param name="Query">The query being generated, when the defect was met in the build of one query.</param>
public sealed record TranslationDefect(Exception Exception, string? Unit = null, string? Entity = null, string? Query = null)
{
    /// <summary>
    /// The sentence of the Failure record written for a defect met while doing
    /// <paramref name="activity"/>: it names the exception, so that two runs of one input
    /// are comparable, and it says whose fault it is, so that the user does not go looking
    /// for an error in the input.
    /// </summary>
    public static string Reason(string activity, Exception exception)
        => $"The tool failed while {activity} ({exception.GetType().Name}: {exception.Message.Trim()}); "
            + "this is a defect of the tool, not of the input. Nothing was generated from it; "
            + "the log of the instance carries the details.";
}
