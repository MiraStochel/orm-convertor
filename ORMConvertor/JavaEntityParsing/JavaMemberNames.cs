namespace JavaEntityParsing;

/// <summary>
/// The name a property takes as the field and the accessors of a generated Java class. A
/// property whose getter would be <c>getClass()</c> - a property <c>Class</c>, or <c>class</c> -
/// would redeclare the method <c>java.lang.Object</c> declares final, and javac refuses the
/// whole class; no other member of <c>Object</c> has the shape of an accessor. Such a property
/// takes a trailing underscore, the respelling the JPQL aliases use for a reserved identifier
/// (<c>JpqlNames</c>), and keeps its column, which every Java builder names in the mapping
/// whatever the member is called. The builders of the three Java targets and their query
/// writers ask here, so the entity and the query name the same member.
/// </summary>
public static class JavaMemberNames
{
    /// <summary>The member the property is written as: its own name, or with a trailing underscore where the getter would be getClass().</summary>
    public static string Of(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return string.Equals(JavaClass.Capitalize(property), "Class", StringComparison.Ordinal) ? property + "_" : property;
    }

    /// <summary>The reason of the Convention record a builder writes for a renamed member, one wording for the three targets.</summary>
    public static string RenameReason(string property, string member)
        => $"The property '{property}' is written as the member '{member}': its getter would be getClass(), which java.lang.Object "
            + "declares final; the column keeps its name in the mapping.";

    /// <summary>A member path - <c>b.Class</c>, <c>customer.id</c> - with each segment written as <see cref="Of"/> writes it.</summary>
    public static string PathOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Join('.', path.Split('.').Select(Of));
    }
}
