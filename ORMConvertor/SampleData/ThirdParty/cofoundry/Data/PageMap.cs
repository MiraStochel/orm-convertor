// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PageMap.cs.
// Changed: the relation to an entity outside the excerpt is left out, marked where it stood. See README.md in the folder.
using Cofoundry.Core.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PageMap : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable(nameof(Page), DbConstants.CofoundrySchema, t =>
        {
            t.HasTrigger("Cofoundry.Page_CascadeDelete");
        });

        builder.Property(s => s.UrlPath)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.CustomEntityDefinitionCode)
            .HasMaxLength(6)
            .IsUnicode(false);

        builder.Property(s => s.PublishStatusCode)
            .HasMaxLength(1)
            .IsUnicode(false);

        builder.HasOne(s => s.Locale)
            .WithMany()
            .HasForeignKey(d => d.LocaleId);

        // ORMConvertor: the relation to CustomEntityDefinition is left out with its navigation (see Page.cs).

        builder.HasOne(s => s.PageDirectory)
            .WithMany(s => s.Pages)
            .HasForeignKey(d => d.PageDirectoryId);

        builder.HasOne(s => s.UserAreaForSignInRedirect)
            .WithMany()
            .HasForeignKey(s => s.UserAreaCodeForSignInRedirect);

        builder.Property(s => s.PublishDate).IsUtc();
        builder.Property(s => s.LastPublishDate).IsUtc();

        CreateAuditableMappingHelper.Map(builder);
    }
}
