namespace Sede;

public class ReviewTaskResults
{
    public int Id { get; set; }

    public int? ReviewTaskId { get; set; }

    public int? ReviewTaskResultTypeId { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? RejectionReasonId { get; set; }

    public string? Comment { get; set; }
}
