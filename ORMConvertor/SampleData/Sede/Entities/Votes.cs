namespace Sede;

public class Votes
{
    public int Id { get; set; }

    public int? PostId { get; set; }

    public byte? VoteTypeId { get; set; }

    public int? UserId { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? BountyAmount { get; set; }
}
