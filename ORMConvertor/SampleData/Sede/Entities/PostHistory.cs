namespace Sede;

public class PostHistory
{
    public int Id { get; set; }

    public byte? PostHistoryTypeId { get; set; }

    public int? PostId { get; set; }

    public Guid? RevisionGUID { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? UserId { get; set; }

    public string? UserDisplayName { get; set; }

    public string? Comment { get; set; }

    public string? Text { get; set; }

    public string? ContentLicense { get; set; }
}
