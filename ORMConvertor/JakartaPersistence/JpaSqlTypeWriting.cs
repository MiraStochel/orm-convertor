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
///
/// The commonest source of the facet states no database family at all: @Nationalized on a
/// String says unicode and nothing else. The family is then read from the language type,
/// which is where the source did put it - a documented derivation whose inputs are in the
/// read artifact and whose gap would otherwise travel into the target's own default, which
/// is exactly the non-national column the source ruled out (decisions 067 and 080).
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
    /// The national type of the column, or null when neither the database family nor the
    /// language type names a character column.
    /// </summary>
    /// <param name="familyFromLanguageType">Whether the family was read from the language type rather than stated.</param>
    /// <param name="lengthFromDefault">Whether the length in the name is the default rather than the source's.</param>
    public static string? NationalizedColumnDefinition(
        PropertyMap propertyMap, out bool familyFromLanguageType, out bool lengthFromDefault)
    {
        lengthFromDefault = false;
        familyFromLanguageType = false;

        var family = propertyMap.Type;

        if (family is null)
        {
            family = CharacterFamilyOf(propertyMap.Property.Type);
            familyFromLanguageType = family is not null;
        }

        switch (family)
        {
            // No length argument at all: ntext is the deprecated large form and takes none.
            case DatabaseType.Text:
                return "ntext";

            case DatabaseType.Char:
            case DatabaseType.VarChar:
                var name = family == DatabaseType.Char ? "nchar" : "nvarchar";
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

    /// <summary>
    /// The character family a language type implies, and only that: a String is a variable
    /// character column and a char a fixed one-character column. Any other scalar - and any
    /// reference or collection - implies nothing, because a database type is otherwise a
    /// fact of the dialect and is never derived from the language (decision 077).
    /// </summary>
    private static DatabaseType? CharacterFamilyOf(LangType? languageType) => languageType?.ScalarType switch
    {
        ScalarType.String => DatabaseType.VarChar,
        ScalarType.Char => DatabaseType.Char,
        _ => null,
    };
}
