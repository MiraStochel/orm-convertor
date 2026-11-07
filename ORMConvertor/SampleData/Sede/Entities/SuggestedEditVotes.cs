namespace Sede;

public class SuggestedEditVotes
{
    public int Id { get; set; }

    public int? SuggestedEditId { get; set; }

    public int? UserId { get; set; }

    public byte? VoteTypeId { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? TargetUserId { get; set; }

    public int? TargetRepChange { get; set; }
}
