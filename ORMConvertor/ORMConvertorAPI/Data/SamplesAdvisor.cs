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
        // Dapper entity
        { 1, CustomerSampleDapper.Entity },

        // NHibernate entity + mapping
        { 2, CustomerSampleNHibernate.Entity },
        { 3, CustomerSampleNHibernate.XmlMapping },

        // EF Core advisor-only samples
        { 4, AdvisorEFCoreSamples.Entity },
        { 5, AdvisorEFCoreSamples.Query1 },
        { 6, AdvisorEFCoreSamples.Query2 },
    };
}

