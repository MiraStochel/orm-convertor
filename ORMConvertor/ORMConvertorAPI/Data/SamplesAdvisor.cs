using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// Samples dedicated to the Advisor page to avoid coupling with other pages/tests.
/// IDs are aligned with RequiredContentAdvisor definitions.
/// </summary>
public static class SamplesAdvisor
{
    public static Dictionary<int, string> GetSamples => new()
    {
        // Dapper entity (append complete CustomerTransaction so benchmarks compile)
        { 1, CustomerSampleDapper.Entity + "\n" + SharedSampleClasses.CustomerTransaction },

        // NHibernate entity + mapping (append complete CustomerTransaction for completeness)
        { 2, CustomerSampleNHibernate.Entity + "\n" + SharedSampleClasses.CustomerTransaction },
        { 3, CustomerSampleNHibernate.XmlMapping },

        // EF Core entity + queries (append complete CustomerTransaction so Dapper benchmark compiles)
        { 4, CustomerSampleEFCore.Entity + "\n" + SharedSampleClasses.CustomerTransaction },
        { 5, CustomerSampleEFCore.Query },
        { 6, CustomerSampleEFCore.Query },
        { 7, CustomerSampleEFCore.Query },
    };
}
