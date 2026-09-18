using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// The literal type of a national character column, for the implementation that has no
/// annotation for the unicode facet and can state it only inside a columnDefinition
/// (decision 080). It is the writing direction of <see cref="JpaSqlTypeReading"/> and the
/// same copy of the EF Core wrapper's T-SQL names (decision 077): the pinned target is
/// SQL Server 2022 (decision 013) and the shared T-SQL reading project of the item for F8
/// is the future home of both directions.
///
/// Only the character families have a national variant, which is the whole point of the
/// facet; a unicode claim over any other family has nowhere to go and the caller reports
/// it. The length travels into the name because a columnDefinition overrides the length
/// the column states beside it - "nvarchar" alone would be one character.
/// </summary>
public static class JpaSqlTypeWriting
{
    /// <summary>
    /// The length Jakarta Persistence 3.2 gives @Column#length when nobody states one.
    /// A literal type has to carry a length, so an unstated one becomes this value and the
    /// caller records that the artifact now states what the source did not.
    /// </summary>
    public const int DefaultLength = 255;

    /// <summary>
    /// The national type of the column, or null when the model's family has none.
    /// </summary>
    /// <param name="lengthFromDefault">Whether the length in the name is the default rather than the source's.</param>
    public static string? NationalizedColumnDefinition(PropertyMap propertyMap, out bool lengthFromDefault)
    {
        lengthFromDefault = false;

        switch (propertyMap.Type)
        {
            // No length argument at all: ntext is the deprecated large form and takes none.
            case DatabaseType.Text:
                return "ntext";

            case DatabaseType.Char:
            case DatabaseType.VarChar:
                var name = propertyMap.Type == DatabaseType.Char ? "nchar" : "nvarchar";
                if (propertyMap.Length is { } length)
                {
                    return $"{name}({length})";
                }

                lengthFromDefault = true;
                return $"{name}({DefaultLength})";

            default:
                return null;
        }
    }
}
