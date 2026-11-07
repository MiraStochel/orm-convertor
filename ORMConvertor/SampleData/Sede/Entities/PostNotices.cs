namespace Sede;

public class PostNotices
{
    public int Id { get; set; }

    public int? PostId { get; set; }

    public int? PostNoticeTypeId { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? DeletionDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string? Body { get; set; }

    public int? OwnerUserId { get; set; }

    public int? DeletionUserId { get; set; }
}
