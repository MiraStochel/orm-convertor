namespace Sede;

public class PendingFlags
{
    public int Id { get; set; }

    public byte? FlagTypeId { get; set; }

    public int? PostId { get; set; }

    public DateTime? CreationDate { get; set; }

    public byte? CloseReasonTypeId { get; set; }

    public short? CloseAsOffTopicReasonTypeId { get; set; }

    public int? DuplicateOfQuestionId { get; set; }

    public string? BelongsOnBaseHostAddress { get; set; }
}
