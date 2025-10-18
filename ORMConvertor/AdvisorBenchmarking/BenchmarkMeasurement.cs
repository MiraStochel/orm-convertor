namespace AdvisorBenchmarking;

public sealed record BenchmarkMeasurement(double MeanDurationMilliseconds, long AllocatedBytes);
