namespace Sede;

public class PostNoticeTypes
{
    public int Id { get; set; }

    public int? ClassId { get; set; }

    public string? Name { get; set; }

    public string? Body { get; set; }

    public bool? IsHidden { get; set; }

    public bool? Predefined { get; set; }

    public int? PostNoticeDurationId { get; set; }
}
