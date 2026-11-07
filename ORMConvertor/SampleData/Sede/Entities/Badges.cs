namespace Sede;

public class Badges
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public string? Name { get; set; }

    public DateTime? Date { get; set; }

    public int? Class { get; set; }

    public bool? TagBased { get; set; }
}
