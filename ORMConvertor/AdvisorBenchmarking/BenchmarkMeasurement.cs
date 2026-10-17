namespace AdvisorBenchmarking;

/// <summary>
/// One cell of a measurement (decision 145): the median time of one execution, the mean
/// bytes one execution allocated, and how many warm-up and measured executions produced
/// them under <see cref="BenchmarkPolicy"/>.
/// </summary>
public sealed record BenchmarkMeasurement(
    double MedianDurationMilliseconds,
    long AllocatedBytes,
    int WarmupIterations,
    int Iterations);
