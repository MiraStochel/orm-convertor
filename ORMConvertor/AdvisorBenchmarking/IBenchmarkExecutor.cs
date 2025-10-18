using Model;

namespace AdvisorBenchmarking;

public interface IBenchmarkExecutor
{
    BenchmarkMeasurement Execute(
        ORMEnum framework,
        IReadOnlyList<ConversionSource> sources,
        string connectionString);
}
