// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Users/UserAreaMap.cs.
// Unchanged apart from this note.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class UserAreaMap : IEntityTypeConfiguration<UserArea>
{
    public void Configure(EntityTypeBuilder<UserArea> builder)
    {
        builder.ToTable(nameof(UserArea), DbConstants.CofoundrySchema);

        builder.HasKey(s => s.UserAreaCode);

        builder.Property(s => s.UserAreaCode)
            .IsRequired()
            .IsCharType(3);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(20);
    }
}
