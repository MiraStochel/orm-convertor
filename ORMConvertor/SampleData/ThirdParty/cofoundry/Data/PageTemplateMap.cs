// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/PageTemplates/PageTemplateMap.cs.
// Changed: the relation to an entity outside the excerpt is left out, marked where it stood. See README.md in the folder.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PageTemplateMap : IEntityTypeConfiguration<PageTemplate>
{
    public void Configure(EntityTypeBuilder<PageTemplate> builder)
    {
        builder.ToTable(nameof(PageTemplate), DbConstants.CofoundrySchema, t =>
        {
            t.HasTrigger("Cofoundry.PageTemplate_CascadeDelete");
        });

        builder.Property(s => s.FileName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.FullPath)
            .HasMaxLength(400)
            .IsRequired();

        builder.Property(s => s.CustomEntityDefinitionCode).HasMaxLength(6);
        builder.Property(s => s.CustomEntityModelType).HasMaxLength(400);
        builder.Property(s => s.Description).IsNVarCharMaxType();

        builder.Property(s => s.CreateDate).IsUtc();
        builder.Property(s => s.UpdateDate).IsUtc();

        // ORMConvertor: the relation to CustomEntityDefinition is left out with its navigation (see PageTemplate.cs).
    }
}
