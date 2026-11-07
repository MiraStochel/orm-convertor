namespace Sede;

public class PostLinks
{
    public long Id { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? PostId { get; set; }

    public int? RelatedPostId { get; set; }

    public byte? LinkTypeId { get; set; }
}
