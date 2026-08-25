using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// One sample per unit declared in <see cref="RequiredContent"/>, keyed by the same id. Ids
/// 6 and 7 used to point at the EF Core query as well, though only 5 had a unit to fill -
/// two of the three were unreachable and the third was the only real one.
/// </summary>
public static class Samples
{
    public static Dictionary<int, string> GetSamples => new()
    {
        { 1, CustomerSampleDapper.Entity },
        { 2, CustomerSampleNHibernate.Entity },
        { 3, CustomerSampleNHibernate.XmlMapping },
        { 4, CustomerSampleEFCore.Entity },
        { 5, CustomerSampleEFCore.Query },
        { 8, CustomerSampleDapper.Query },
        { 9, CustomerSampleNHibernate.Query },
        { 10, CustomerSampleNHibernate.HqlQuery },
    };
}
