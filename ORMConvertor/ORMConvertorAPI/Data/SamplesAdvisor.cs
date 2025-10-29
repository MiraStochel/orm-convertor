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
        { 1, CustomerSampleDapper.Entity }, //+ "\n" + SharedSampleClasses.CustomerTransaction },

        // NHibernate entity + mapping (append complete CustomerTransaction for completeness)
        { 2, CustomerSampleNHibernate.Entity }, //+ "\n" + SharedSampleClasses.CustomerTransaction },
        { 3, CustomerSampleNHibernate.XmlMapping },

        // EF Core advisor-only samples
        { 4, AdvisorEfCoreSamples.Entity },
        { 5, AdvisorEfCoreSamples.Query },
    };
}
