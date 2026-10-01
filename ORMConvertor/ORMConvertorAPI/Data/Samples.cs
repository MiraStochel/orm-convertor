using SampleData;

namespace ORMConvertorAPI.Data;

/// <summary>
/// One sample per unit declared in <see cref="RequiredContent"/>, keyed by the same id. The
/// C# and Java samples are whole files holding the entity and the code that queries it,
/// because that is what one unit is since decision 111; the ids of the role-bound units that
/// went with it (5, 9, 13, 17, 20) are not used again.
/// </summary>
public static class Samples
{
    public static Dictionary<int, string> GetSamples => new()
    {
        { 1, CustomerSampleDapper.Source },
        { 2, CustomerSampleNHibernate.Source },
        { 3, CustomerSampleNHibernate.XmlMapping },
        { 4, CustomerSampleEFCore.Source },
        { 8, CustomerSampleDapper.Query },
        { 10, CustomerSampleNHibernate.HqlQuery },
        { 11, CustomerSampleHibernate.Source },
        { 12, CustomerSampleHibernate.OrmXml },
        { 14, CustomerSampleHibernate.JpqlQuery },
        { 15, CustomerSampleEclipseLink.Source },
        { 16, CustomerSampleEclipseLink.OrmXml },
        { 18, CustomerSampleEclipseLink.JpqlQuery },
        { 19, CustomerSampleMyBatis.Source },
        { 21, CustomerSampleMyBatis.XmlMapper },
    };
}
