namespace Sede;

public class TagSynonyms
{
    public int Id { get; set; }

    public string? SourceTagName { get; set; }

    public string? TargetTagName { get; set; }

    public DateTime? CreationDate { get; set; }

    public int? OwnerUserId { get; set; }

    public int? AutoRenameCount { get; set; }

    public DateTime? LastAutoRename { get; set; }

    public int? Score { get; set; }

    public int? ApprovedByUserId { get; set; }

    public DateTime? ApprovalDate { get; set; }
}
