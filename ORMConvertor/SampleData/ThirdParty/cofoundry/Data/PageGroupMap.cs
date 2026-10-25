// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PageGroupMap.cs.
// Changed: the column of ParentGroupId is named, an upstream mistake corrected where it stands.
// See README.md in the folder.
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

[Obsolete("The page grouping system will be revised in an upcomming release.")]
public class PageGroupMap : IEntityTypeConfiguration<PageGroup>
{
    public void Configure(EntityTypeBuilder<PageGroup> builder)
    {
        builder.ToTable(nameof(PageGroup), DbConstants.CofoundrySchema);

        builder.Property(s => s.GroupName)
            .IsRequired()
            .HasMaxLength(64);

        // ORMConvertor: added - the project maps ParentGroupId by convention to a column ParentGroupId, but its
        // installation script creates the column ParentPageGroupId (00001.sql), so no query over page groups
        // runs against the project's own schema; the column is named here.
        builder.Property(s => s.ParentGroupId).HasColumnName("ParentPageGroupId");

        builder.HasOne(s => s.ParentPageGroup)
            .WithMany(s => s.ChildPageGroups)
            .HasForeignKey(d => d.ParentGroupId);

        CreateAuditableMappingHelper.Map(builder);
    }
}
