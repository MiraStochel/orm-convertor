// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Tags/Tag.cs.
// Unchanged apart from this note.
namespace Cofoundry.Domain.Data;

public class Tag
{
    public int TagId { get; set; }

    public string TagText { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; }
}
