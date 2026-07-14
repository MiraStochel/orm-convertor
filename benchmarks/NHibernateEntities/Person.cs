using System.Text.Json;

namespace NHibernateEntities;

public class Person
{
    public virtual int PersonID { get; set; }
    public virtual required string FullName { get; set; }
    public virtual required string PreferredName { get; set; }
    public virtual string? EmailAddress { get; set; }
    public virtual string? CustomFields { get; set; }
    public virtual string? OtherLanguages { get; set; }

    public virtual CustomFields? GetCustomFields()
    {
        return string.IsNullOrEmpty(CustomFields) ? null : JsonSerializer.Deserialize<CustomFields>(CustomFields);
    }

    public virtual List<string>? GetOtherLanguages()
    {
        return string.IsNullOrEmpty(OtherLanguages) ? null : JsonSerializer.Deserialize<List<string>>(OtherLanguages);
    }
}

public class CustomFields
{
    public List<string>? OtherLanguages { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Title { get; set; }
}
