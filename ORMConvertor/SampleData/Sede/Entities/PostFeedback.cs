namespace Sede;

public class PostFeedback
{
    public int Id { get; set; }

    public int? PostId { get; set; }

    public bool? IsAnonymous { get; set; }

    public byte? VoteTypeId { get; set; }

    public DateTime? CreationDate { get; set; }
}
