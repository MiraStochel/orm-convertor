// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Assets/ImageAssetTagMap.cs.
// Unchanged apart from this note.
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cofoundry.Domain.Data;

public class ImageAssetTagMap : IEntityTypeConfiguration<ImageAssetTag>
{
    public void Configure(EntityTypeBuilder<ImageAssetTag> builder)
    {
        builder.ToTable(nameof(ImageAssetTag), DbConstants.CofoundrySchema);
        builder.HasKey(s => new { s.ImageAssetId, s.TagId });

        builder.Property(s => s.ImageAssetId)
            .ValueGeneratedNever();

        builder.Property(s => s.TagId)
            .ValueGeneratedNever();

        builder.HasOne(s => s.ImageAsset)
            .WithMany(s => s.ImageAssetTags)
            .HasForeignKey(d => d.ImageAssetId);

        builder.HasOne(s => s.Tag)
            .WithMany()
            .HasForeignKey(d => d.TagId);

        CreateAuditableMappingHelper.Map(builder);
    }
}
