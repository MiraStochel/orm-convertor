// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PagePublishStatusQueryMap.cs.
// Unchanged apart from this note.
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PagePublishStatusQueryMap : IEntityTypeConfiguration<PagePublishStatusQuery>
{
    public void Configure(EntityTypeBuilder<PagePublishStatusQuery> builder)
    {
        builder.ToTable(nameof(PagePublishStatusQuery), DbConstants.CofoundrySchema);
        builder.HasKey(s => new { s.PageId, s.PublishStatusQueryId });

        builder.HasOne(s => s.Page)
            .WithMany(s => s.PagePublishStatusQueries)
            .HasForeignKey(d => d.PageId);

        builder.HasOne(s => s.PageVersion)
            .WithMany(s => s.PagePublishStatusQueries)
            .HasForeignKey(d => d.PageVersionId);
    }
}
