// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PageDirectoryPathMap.cs.
// Unchanged apart from this note.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PageDirectoryPathMap : IEntityTypeConfiguration<PageDirectoryPath>
{
    public void Configure(EntityTypeBuilder<PageDirectoryPath> builder)
    {
        builder.ToTable(nameof(PageDirectoryPath), DbConstants.CofoundrySchema);
        builder.HasKey(s => s.PageDirectoryId);

        builder.Property(s => s.FullUrlPath)
            .IsRequired()
            .IsNVarCharMaxType();
    }
}
