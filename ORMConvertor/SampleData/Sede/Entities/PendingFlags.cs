namespace Sede;

public class PendingFlags
{
    public int Id { get; set; }

    public int? FlagTypeId { get; set; }

    public int? PostId { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? CloseReasonTypeId { get; set; }

    public int? CloseAsOffTopicReasonTypeId { get; set; }

    public int? DuplicateOfQuestionId { get; set; }

    public string? BelongsOnBaseHostAddress { get; set; }
}
