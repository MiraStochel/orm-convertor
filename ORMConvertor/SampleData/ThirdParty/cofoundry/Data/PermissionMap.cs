// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Roles/PermissionMap.cs.
// Changed: the relation to an entity outside the excerpt is left out, marked where it stood. See README.md in the folder.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PermissionMap : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable(nameof(Permission), DbConstants.CofoundrySchema, t =>
        {
            t.HasTrigger("Cofoundry.Permission_CascadeDelete");
        });

        builder.Property(s => s.PermissionCode)
            .IsRequired()
            .IsCharType(6);

        builder.Property(s => s.EntityDefinitionCode)
            .IsCharType(6);

        // ORMConvertor: the relation to EntityDefinition is left out with its navigation (see Permission.cs).
    }
}
