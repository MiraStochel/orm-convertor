
using System.Text.Json;

namespace DapperEntities;
public class Person
{
    public int PersonID { get; set; }
    public required string FullName { get; set; }
    public required string PreferredName { get; set; }
    public string? EmailAddress { get; set; }
    public string? CustomFields { get; set; }
    public string? OtherLanguages { get; set; }

    public CustomFields? GetCustomFields()
    {
        return string.IsNullOrEmpty(CustomFields) ? null : JsonSerializer.Deserialize<CustomFields>(CustomFields);
    }

    public List<string>? GetOtherLanguages()
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

