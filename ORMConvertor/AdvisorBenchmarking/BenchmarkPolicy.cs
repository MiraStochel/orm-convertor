namespace AdvisorBenchmarking;

/// <summary>
/// The iteration policy of a measurement (decision 145): the values are part of the
/// methodology, not of the request, so that numbers of different runs are comparable, and
/// a run reports them with its measurements. Warm-up continues until two consecutive
/// executions agree within <see cref="WarmupTolerance"/> of the slower one, at most
/// <see cref="MaxWarmupIterations"/> times; the first cell of a query thereby also warms
/// the buffer pool for the cells of the other frameworks, which read the same data. The
/// measured loop runs until the accumulated time reaches <see cref="TargetMilliseconds"/>
/// and at least <see cref="MinIterations"/> times, the smallest count a median exists for;
/// a short query is thus repeated and a long one runs three times. The reported time is the
/// median of one execution, the reported memory the mean of the bytes one execution
/// allocated on the measuring thread. One execution has a time limit (decision 146),
/// set on the driver connection of the harness rather than through any framework's own
/// API, so that every framework gets the same one and the artifact stays as translated;
/// the Java branch takes these values from the request and has no constants of its own.
/// </summary>
public static class BenchmarkPolicy
{
    public const double WarmupTolerance = 0.10;
    public const int MaxWarmupIterations = 5;
    public const int MinIterations = 3;
    public const double TargetMilliseconds = 1000;

    /// <summary>
    /// The limit of one execution in seconds: the value the LDBC judge gives every command of
    /// a replay, twice the upper bound of the window of decision 140. The connection string of
    /// a harness carries it as <c>Command Timeout</c>, a JDBC URL as <c>queryTimeout</c>.
    /// </summary>
    public const int ExecutionTimeoutSeconds = 600;
}
