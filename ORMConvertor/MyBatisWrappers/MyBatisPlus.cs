using AbstractWrappers.Descriptors;
using JavaEntityParsing;

namespace MyBatisWrappers;

/// <summary>
/// What the MyBatis wrapper knows of MyBatis-Plus, an add-on package over MyBatis that the
/// tool does not read as MyBatis (decision 133): its annotations on a domain class and its
/// BaseMapper, so that a unit using it gets records naming the package rather than a sentence
/// about MyBatis alone, or no record at all. Nothing of the package is read - the SQL it
/// composes from them is no text of the unit.
///
/// The package is told by the unit's imports alone, since the reading compiles nothing: an
/// import of its package or of the class, or a name qualified with the package. Two of its
/// annotation names belong to Jakarta Persistence as well, so those two are told only where
/// the unit does not import their namesake too.
/// </summary>
internal static class MyBatisPlus
{
    private const string AnnotationPackage = "com.baomidou.mybatisplus.annotation";

    private const string MapperPackage = "com.baomidou.mybatisplus.core.mapper";

    /// <summary>The annotations of the package, each with what the record says of its statement and the category of the fact.</summary>
    private static readonly Dictionary<string, (string Dropped, MappingFactCategory? Category)> Annotations = new(StringComparer.Ordinal)
    {
        ["TableName"] = ("the name of the table it states is dropped and left to the catalog", MappingFactCategory.TableName),
        ["TableId"] = ("the key and the generation it states are dropped and left to the catalog", MappingFactCategory.PrimaryKey),
        ["TableField"] = ("the column it states is dropped; the mapper states the columns of its statements", MappingFactCategory.ColumnName),
        ["Version"] = ("the version column it states for the package's own locking is dropped", MappingFactCategory.VersionColumn),
        ["KeySequence"] = ("the sequence it names for the key is dropped", MappingFactCategory.PrimaryKeyStrategy),
        ["TableLogic"] = ("the logical deletion it states, which the package applies to the SQL it composes itself, is dropped", null),
        ["EnumValue"] = ("the value it stores for an enumeration is dropped", null),
        ["OrderBy"] = ("the order it states, which the package applies to the SQL it composes itself, is dropped", null),
    };

    /// <summary>The Jakarta Persistence annotation of the same name, for the two names the package shares with it.</summary>
    private static readonly Dictionary<string, string> Namesakes = new(StringComparer.Ordinal)
    {
        ["Version"] = "jakarta.persistence.Version",
        ["OrderBy"] = "jakarta.persistence.OrderBy",
    };

    /// <summary>
    /// The clause of the record of an annotation of the package and the category of the fact
    /// it states, or null where the annotation is not told as the package's.
    /// </summary>
    internal static (string Why, MappingFactCategory? Category)? Annotation(JavaAnnotation annotation, IReadOnlyList<string> imports)
    {
        var name = annotation.SimpleName;

        if (!Annotations.TryGetValue(name, out var fact))
        {
            return null;
        }

        var told = annotation.Name.Contains('.')
            ? annotation.Name == $"{AnnotationPackage}.{name}"
            : Imports(imports, AnnotationPackage, name)
                && !(Namesakes.TryGetValue(name, out var namesake) && Imports(imports, namesake[..namesake.LastIndexOf('.')], name));

        return told
            ? ($"it is an annotation of MyBatis-Plus, an add-on package over MyBatis that the tool does not read as MyBatis "
                + $"(decision 133), so {fact.Dropped}", fact.Category)
            : null;
    }

    /// <summary>
    /// The reason of the record of a mapper interface that extends the package's BaseMapper, or
    /// null where it extends none the unit tells as the package's: the statements it inherits are
    /// composed by the package at run time, and none of them is declared where a reading could
    /// take it (decision 133).
    /// </summary>
    internal static string? BaseMapper(JavaInterface mapper, IReadOnlyList<string> imports)
    {
        foreach (var extended in mapper.Extends)
        {
            var raw = extended.Split('<')[0].Trim();
            var told = raw.Contains('.')
                ? raw == $"{MapperPackage}.BaseMapper"
                : raw == "BaseMapper" && Imports(imports, MapperPackage, "BaseMapper");

            if (told)
            {
                return $"The mapper interface '{mapper.Name}' extends {extended.Trim()} of MyBatis-Plus, an add-on package over MyBatis "
                    + "that the tool does not read as MyBatis (decision 133): the statements it inherits are composed by the package "
                    + "at run time and none of them is translated; the statements the interface and its XML mapper declare are read.";
            }
        }

        return null;
    }

    /// <summary>Whether the imports bring the type of the package into the unit: the type itself, or the whole package.</summary>
    private static bool Imports(IReadOnlyList<string> imports, string package, string type)
        => imports.Any(import => import == $"{package}.{type}" || import == $"{package}.*");
}
