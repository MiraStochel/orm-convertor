// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Pages/PageDirectoryLocaleMap.cs.
// Unchanged apart from this note.
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class PageDirectoryLocaleMap : IEntityTypeConfiguration<PageDirectoryLocale>
{
    public void Configure(EntityTypeBuilder<PageDirectoryLocale> builder)
    {
        builder.ToTable("PageDirectoryLocale", DbConstants.CofoundrySchema);
        builder.HasKey(s => s.PageDirectoryLocaleId);

        builder.Property(s => s.UrlPath)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasOne(s => s.Locale)
            .WithMany()
            .HasForeignKey(d => d.LocaleId);

        builder.HasOne(s => s.PageDirectory)
            .WithMany(s => s.PageDirectoryLocales)
            .HasForeignKey(d => d.PageDirectoryId);

        CreateAuditableMappingHelper.Map(builder);
    }
}
