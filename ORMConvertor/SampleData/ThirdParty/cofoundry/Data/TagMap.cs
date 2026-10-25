// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Tags/TagMap.cs.
// Unchanged apart from this note.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class TagMap : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable(nameof(Tag), DbConstants.CofoundrySchema);

        builder.Property(s => s.TagText)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(s => s.CreateDate).IsUtc();
    }
}
