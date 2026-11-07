namespace Sede;

public class ReviewTaskResults
{
    public int Id { get; set; }

    public int? ReviewTaskId { get; set; }

    public byte? ReviewTaskResultTypeId { get; set; }

    public DateTime? CreationDate { get; set; }

    public byte? RejectionReasonId { get; set; }

    public string? Comment { get; set; }
}
