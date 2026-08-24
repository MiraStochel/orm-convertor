using DatabaseCatalog;
using Model;

namespace AdvisorBenchmarking;

public interface IBenchmarkExecutor
{
    /// <summary>
    /// Builds, compiles and measures the harness for one (query, framework) pair. The
    /// catalog reader qualifies unqualified table names; the caller passes one for the
    /// whole run, so the same entity set is not re-read per pair.
    /// </summary>
    BenchmarkMeasurement Execute(
        ORMEnum framework,
        IReadOnlyList<ConversionSource> sources,
        string connectionString,
        ICatalogReader catalogReader);
}
