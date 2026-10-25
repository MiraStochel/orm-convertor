// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PageTag.cs.
// Unchanged apart from this note.
namespace Cofoundry.Domain.Data;

public class PageTag : IEntityTag, ICreateAuditable
{
    public int PageId { get; set; }

    public Page Page
    {
        get => field ?? throw NavigationPropertyNotInitializedException.Create<PageTag>(nameof(Page));
        set;
    }

    public int TagId { get; set; }

    public Tag Tag
    {
        get => field ?? throw NavigationPropertyNotInitializedException.Create<PageTag>(nameof(Tag));
        set;
    }

    /// <inheritdoc/>
    public DateTime CreateDate { get; set; }

    /// <inheritdoc/>
    public int CreatorId { get; set; }

    /// <inheritdoc/>
    public User Creator
    {
        get => field ?? throw NavigationPropertyNotInitializedException.Create<PageTag>(nameof(Creator));
        set;
    }
}
