// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Locales/LocaleMap.cs.
// Unchanged apart from this note.
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class LocaleMap : IEntityTypeConfiguration<Locale>
{
    public void Configure(EntityTypeBuilder<Locale> builder)
    {
        builder.ToTable(nameof(Locale), DbConstants.CofoundrySchema);
        builder.HasKey(s => s.LocaleId);

        builder.Property(s => s.IETFLanguageTag)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(s => s.LocaleName)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasOne(s => s.ParentLocale)
            .WithMany(s => s.ChildLocales)
            .HasForeignKey(d => d.ParentLocaleId);
    }
}
