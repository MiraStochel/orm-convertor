namespace Sede;

public class ReviewTasks
{
    public int Id { get; set; }

    public int? ReviewTaskTypeId { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? DeletionDate { get; set; }

    public int? ReviewTaskStateId { get; set; }

    public int? PostId { get; set; }

    public int? SuggestedEditId { get; set; }

    public int? CompletedByReviewTaskId { get; set; }
}
