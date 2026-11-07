namespace Sede;

public class CloseAsOffTopicReasonTypes
{
    public short Id { get; set; }

    public bool? IsUniversal { get; set; }

    public string? InputTitle { get; set; }

    public string? MarkdownInputGuidance { get; set; }

    public string? MarkdownPostOwnerGuidance { get; set; }

    public string? MarkdownPrivilegedUserGuidance { get; set; }

    public string? MarkdownConcensusDescription { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? CreationModeratorId { get; set; }

    public DateTime? ApprovalDate { get; set; }

    public int? ApprovalModeratorId { get; set; }

    public DateTime? DeactivationDate { get; set; }

    public int? DeactivationModeratorId { get; set; }
}
