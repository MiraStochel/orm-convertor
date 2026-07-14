namespace linq2dbEntities;

public class Person
{
    public int PersonID { get; set; }
    public required string FullName { get; set; }
    public required string PreferredName { get; set; }
    public string? EmailAddress { get; set; }
    public CustomFields? CustomFields { get; set; }
    public List<string>? OtherLanguages { get; set; }
}

public class CustomFields
{
    public List<string>? OtherLanguages { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Title { get; set; }
}

