namespace Sede;

public class PostLinks
{
    public int Id { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? PostId { get; set; }

    public int? RelatedPostId { get; set; }

    public int? LinkTypeId { get; set; }
}
