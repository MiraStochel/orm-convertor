using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;

namespace JavaEntityParsing;

/// <summary>
/// Reads a Java entity class into the entity IR: package, class header and the language
/// facts of every attribute. Nothing here belongs to any ORM - the class structure means
/// the same thing under every Java framework, the way the shared C# reading owns the C#
/// structure (decisions 026 and 076). What a framework reads on top - annotations,
/// conventions - is what a subclass adds through the hooks.
/// </summary>
public abstract class JavaEntityParser(AbstractEntityBuilder entityBuilder) : IEntityParser
{
    protected readonly AbstractEntityBuilder entityBuilder = entityBuilder;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.JavaEntity;

    /// <summary>
    /// Parses the Java classes of one source into entities. Every class declaration in the
    /// text becomes an entity of its own, nested classes included - a nested static key
    /// class is a class of the conversion like a separate one, and the dissolution phase
    /// of decision 031 takes it out again once the key names it. A source the reader
    /// cannot read is a failure record with a line and a column, and yields nothing.
    /// </summary>
    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        JavaCompilationUnit unit;
        try
        {
            unit = JavaClassReader.Read(source, Limits);
        }
        catch (JavaSyntaxError error)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = entityBuilder.Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Reason = $"The Java source could not be read at line {error.Line}, column {error.Column}: {error.Message}.",
            });
            return [];
        }
        catch (JavaInputTooDeep tooDeep)
        {
            // Not a malformed source and not reported as one (decision 092): the sentence is
            // the one all five languages refuse with, and the unit yields nothing.
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = entityBuilder.Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Reason = NestingDepthGuard.Reason(tooDeep.Token, Limits),
            });
            return [];
        }

        var read = new List<EntityMap>();

        foreach (var (cls, declaringType) in Flatten(unit.Classes))
        {
            // Find-or-create over the pair of package and name (decision 094): a mapping
            // descriptor read before the class - the orm.xml of decision 068 - or another
            // unit declaring the same class has founded the entity already, and this
            // declaration enriches it rather than standing beside it as a second one. A map
            // another unit founded with a package of its own is a different class, and so is
            // a nested class of another container.
            var entityMap = entityBuilder.DeclareEntity(cls.Name, unit.Package, declaringType);

            entityBuilder.AddClassHeader(AccessOf(cls.Modifiers), cls.Name);
            ParseClassBody(cls);

            if (!read.Contains(entityMap))
            {
                read.Add(entityMap);
            }
        }

        return read;
    }

    /// <summary>
    /// The class after its header is recorded. The default emits the language facts of
    /// every attribute with field access; a framework reading annotations overrides it.
    /// </summary>
    protected virtual void ParseClassBody(JavaClass cls)
    {
        foreach (var reading in ReadAttributes(cls, JavaMemberAccess.Field))
        {
            EmitProperty(reading);
        }
    }

    /// <summary>
    /// The attributes of a class with their language facts: the non-static fields, and
    /// under property access the getters that have no field. Annotations come from the
    /// member the access type names - the field, or the getter of the same name.
    /// </summary>
    protected static List<JavaAttributeReading> ReadAttributes(JavaClass cls, JavaMemberAccess access)
    {
        var readings = new List<JavaAttributeReading>();

        foreach (var field in cls.Fields.Where(f => !f.Modifiers.Contains("static", StringComparer.Ordinal)))
        {
            readings.Add(ReadField(cls, field, access));
        }

        if (access == JavaMemberAccess.Property)
        {
            foreach (var getter in cls.Methods.Where(m =>
                m.ParameterCount == 0
                && !m.Modifiers.Contains("static", StringComparer.Ordinal)
                && m.ReturnType != "void"
                && JavaClass.PropertyOfAccessor(m.Name) is { } property
                && !m.Name.StartsWith("set", StringComparison.Ordinal)
                && readings.All(r => r.Name != property)))
            {
                var name = JavaClass.PropertyOfAccessor(getter.Name)!;
                readings.Add(new JavaAttributeReading(
                    name,
                    getter.ReturnType,
                    AccessOf(getter.Modifiers),
                    HasGetter: true,
                    HasSetter: cls.SetterOf(name) is not null,
                    DefaultValue: null,
                    DroppedInitializer: null,
                    IsNullable: !JavaTypeConvertor.IsPrimitive(getter.ReturnType),
                    getter.Annotations,
                    HasTransientModifier: false,
                    DroppedModifiers: [],
                    getter.Line));
            }
        }

        return readings;
    }

    private static JavaAttributeReading ReadField(JavaClass cls, JavaField field, JavaMemberAccess access)
    {
        var getter = cls.GetterOf(field.Name);
        var setter = cls.SetterOf(field.Name);

        // The property's visibility is the accessor's: a private field behind a public
        // getter is a public property to every caller (decision 077). A field with no
        // accessor at all is, under field access, the whole property - and a private
        // property without accessors would be no property in the target language - so it
        // reads as a public property with both.
        var bare = getter is null && setter is null;
        var accessModifier = bare ? "public" : getter is null ? AccessOf(field.Modifiers) : AccessOf(getter.Modifiers);

        var annotations = access == JavaMemberAccess.Property && getter is not null
            ? getter.Annotations
            : field.Annotations;

        var (literal, dropped) = ReadInitializer(field.Initializer);

        return new JavaAttributeReading(
            field.Name,
            field.Type,
            accessModifier,
            HasGetter: bare || getter is not null,
            HasSetter: bare || setter is not null,
            literal,
            dropped,
            IsNullable: !JavaTypeConvertor.IsPrimitive(field.Type),
            annotations,
            HasTransientModifier: field.Modifiers.Contains("transient", StringComparer.Ordinal),
            DroppedModifiers: [.. field.Modifiers.Where(m => m is "final" or "volatile")],
            field.Line);
    }

    /// <summary>
    /// An initializer travels only as a literal both languages spell alike (decision 077):
    /// an integer, a decimal fraction, a double-quoted string, true, false or null. A Java
    /// suffix is stripped where the value survives it (5L, 1.5d), a BigDecimal constructor
    /// over a literal is read as the literal; anything else is kept for the record.
    /// </summary>
    private static (string? Literal, string? Dropped) ReadInitializer(string? initializer)
    {
        if (string.IsNullOrWhiteSpace(initializer))
        {
            return (null, null);
        }

        var text = initializer.Trim();

        if (text is "true" or "false" or "null")
        {
            return (text, null);
        }

        if (text.StartsWith("new BigDecimal(\"", StringComparison.Ordinal) && text.EndsWith("\")", StringComparison.Ordinal))
        {
            text = text["new BigDecimal(\"".Length..^2];
        }
        else if (text == "BigDecimal.ZERO")
        {
            text = "0";
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return (text, null);
        }

        var number = text.TrimEnd('L', 'l', 'd', 'D', 'f', 'F');
        if (number.Length > 0 && decimal.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            && !number.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return (number, null);
        }

        return (null, initializer.Trim());
    }

    /// <summary>
    /// Writes the language facts of one attribute into the builder, and the records for
    /// what the model has no place for: an initializer that is not a shared literal, and
    /// the final and volatile modifiers, which have no meaning in a C# property.
    /// </summary>
    protected void EmitProperty(JavaAttributeReading reading, bool? isNullable = null)
    {
        entityBuilder.AddProperty(
            reading.Type,
            reading.Name,
            reading.AccessModifier,
            [],
            hasGetter: reading.HasGetter,
            hasSetter: reading.HasSetter,
            defaultValue: reading.DefaultValue,
            isNullable: isNullable ?? reading.IsNullable);

        // The type read by the builder's C# convertor is wrong for Java; the Java reading
        // replaces it in place, keeping the nullability decided above.
        var property = entityBuilder.EntityMap.Entity.Properties.First(p => p.Name == reading.Name);
        property.Type = JavaTypeConvertor.FromString(reading.Type, isNullable ?? reading.IsNullable);

        if (reading.DroppedInitializer is not null)
        {
            ReportLoss(reading.Name,
                $"The initializer '{reading.DroppedInitializer}' is not a literal both languages spell alike; it was dropped.");
        }

        foreach (var modifier in reading.DroppedModifiers)
        {
            ReportLoss(reading.Name,
                $"The modifier '{modifier}' has no counterpart in the intermediate representation and was dropped.");
        }
    }

    protected void ReportLoss(string? property, string reason) => entityBuilder.Report(new ConversionRecord
    {
        Kind = ConversionRecordKind.Loss,
        Framework = entityBuilder.Descriptor.Framework,
        Artifact = ConversionContentType.JavaEntity,
        Entity = entityBuilder.EntityMap.Entity.Name,
        Property = property,
        Reason = reason,
    });

    /// <summary>
    /// The access token of a member: Java has three, and none means package-private,
    /// which the model records as internal - the nearest of its values.
    /// </summary>
    protected static string AccessOf(IReadOnlyList<string> modifiers)
        => modifiers.FirstOrDefault(m => m is "public" or "private" or "protected") ?? string.Empty;

    /// <summary>
    /// Every class of the unit with the container it is nested in, outermost first and null
    /// for a top-level one: an entity beside its container in the model, and a type of its
    /// own for the identity rule of decision 094.
    /// </summary>
    private static IEnumerable<(JavaClass Class, string? DeclaringType)> Flatten(
        IReadOnlyList<JavaClass> classes,
        string? declaringType = null)
    {
        foreach (var cls in classes)
        {
            yield return (cls, declaringType);

            var container = declaringType is null ? cls.Name : $"{declaringType}.{cls.Name}";

            foreach (var nested in Flatten(cls.NestedClasses, container))
            {
                yield return nested;
            }
        }
    }
}
