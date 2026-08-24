using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using CSharpEntityParsing;
using EFCoreWrappers.Convertors;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Parses an EF Core entity class from C# source code: the shared structural reading plus
/// EF Core's attribute mapping and the conventions whose absence would change the meaning.
/// </summary>
public class EFCoreEntityParser : CSharpEntityParser
{
    public EFCoreEntityParser(AbstractEntityBuilder entityBuilder) : base(entityBuilder)
    {
    }

    /// <summary>
    /// Parses class attributes: the table and schema of [Table], the unique constraints of
    /// [Index] (decision 055), and a record for everything else.
    ///
    /// [PrimaryKey] is read by <see cref="GetClassPrimaryKeyNames"/> in the property step,
    /// where the key part order can be matched against the properties, so it is recognized
    /// here only in order not to be reported as dropped.
    ///
    /// The record for the rest is what decision 048 rules for a fact the model has no place
    /// for. It is written here rather than left to the shared dictionary channel, because a
    /// class annotation never reaches that channel at all - this loop used to be the one
    /// place in the parser where an annotation fell out without a trace.
    /// </summary>
    protected override void ParseClassAttributes(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var attr in classDeclaration.AttributeLists.SelectMany(l => l.Attributes))
        {
            var name = TrimAttribute(attr.Name.ToString());

            if (name.Equals("Index", StringComparison.OrdinalIgnoreCase))
            {
                ReadIndexAttribute(attr, classDeclaration);
                continue;
            }

            if (name.Equals("PrimaryKey", StringComparison.OrdinalIgnoreCase))
            {
                continue; // read by GetClassPrimaryKeyNames
            }

            if (name.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                ReadTableAttribute(attr);
                continue;
            }

            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = ORMEnum.EFCore,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = classDeclaration.Identifier.Text,
                Reason = $"The class annotation [{name}] has no counterpart in the intermediate representation and was dropped.",
            });
        }
    }

    /// <summary>
    /// Reads the table and the schema out of [Table("Orders", Schema = "sales")].
    /// </summary>
    private void ReadTableAttribute(AttributeSyntax attribute)
    {
        string? table = null;
        string? schema = null;

        foreach (var arg in attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
        {
            var named = arg.NameEquals?.Name.Identifier.ValueText;

            if (named is null)
            {
                table = GetString(arg.Expression);
            }
            else if (named.Equals("Schema", StringComparison.OrdinalIgnoreCase))
            {
                schema = GetString(arg.Expression);
            }
            else if (named.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                table = GetString(arg.Expression);
            }
        }

        if (!string.IsNullOrEmpty(table))
        {
            entityBuilder.AddTable(table);
        }

        if (!string.IsNullOrEmpty(schema))
        {
            entityBuilder.AddSchema(schema);
        }
    }

    /// <summary>
    /// Reads [Index(nameof(A), nameof(B), IsUnique = true, Name = "…")] - EF Core's only
    /// annotation for a unique constraint (decision 055). Argument order is the order of
    /// the constraint's columns, and both the nameof form and a plain string are accepted,
    /// as in [PrimaryKey].
    ///
    /// Without IsUnique the annotation declares a plain index, which is a performance
    /// artifact rather than a mapping fact and has no place in the model; it is reported
    /// as a loss instead of being read as a constraint it is not.
    /// </summary>
    private void ReadIndexAttribute(AttributeSyntax attribute, ClassDeclarationSyntax classDeclaration)
    {
        var propertyNames = new List<string>();
        string? name = null;
        bool isUnique = false;

        foreach (var arg in attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
        {
            var named = arg.NameEquals?.Name.Identifier.ValueText;

            if (named is null)
            {
                if (ReadPropertyName(arg.Expression) is { Length: > 0 } propertyName)
                {
                    propertyNames.Add(propertyName);
                }

                continue;
            }

            if (named.Equals("IsUnique", StringComparison.OrdinalIgnoreCase))
            {
                isUnique = arg.Expression is LiteralExpressionSyntax { Token.Value: bool value } && value;
            }
            else if (named.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                name = GetString(arg.Expression);
            }
        }

        if (propertyNames.Count == 0)
        {
            return;
        }

        if (!isUnique)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = ORMEnum.EFCore,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = classDeclaration.Identifier.Text,
                Reason = $"The index over ({string.Join(", ", propertyNames)}) is not unique, so it states no "
                    + "mapping fact the intermediate representation carries, and was dropped (decision 055).",
            });
            return;
        }

        entityBuilder.AddUniqueConstraint(name, propertyNames);
    }

    /// <summary>
    /// A property named either through nameof or as a plain string, the two spellings
    /// [Index] and [PrimaryKey] both admit.
    /// </summary>
    private static string? ReadPropertyName(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax inv
            when inv.Expression is IdentifierNameSyntax id
              && id.Identifier.Text == "nameof"
              && inv.ArgumentList.Arguments.Count == 1
              && inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax propName
            => propName.Identifier.Text,
        _ => GetString(expression),
    };

    /// <summary>
    /// Reads the EF Core 7+ class-level [PrimaryKey(nameof(A), nameof(B))] attribute.
    /// Argument order defines the key part order.
    /// </summary>
    private static List<string> GetClassPrimaryKeyNames(ClassDeclarationSyntax classDeclaration)
    {
        var names = new List<string>();

        foreach (var attr in classDeclaration.AttributeLists.SelectMany(l => l.Attributes))
        {
            if (!TrimAttribute(attr.Name.ToString()).Equals("PrimaryKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var arg in attr.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
            {
                var name = ReadPropertyName(arg.Expression);

                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Parses properties from the class declaration.
    /// </summary>
    protected override void ParseProperties(ClassDeclarationSyntax classDeclaration)
    {
        var classKeyNames = GetClassPrimaryKeyNames(classDeclaration);
        var keyPropertyNames = new List<string>();
        var conventionKeyCandidates = new List<string>();

        // Collected per property because the strategy is decided once the whole key is known:
        // the type of the key property carries EF Core's convention, and the annotation, where
        // present, overrides it (decision 011).
        var propertyTypes = new Dictionary<string, string>();
        var generatedOptions = new Dictionary<string, string>();

        foreach (var prop in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            var reading = ReadProperty(prop);
            var name = reading.Name;
            var type = reading.Type;

            var dbProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool isPrimaryKey = false;
            bool requiredAttr = false;
            string? generatedOption = null;
            string? columnTypeName = null;
            List<string>? foreignKeyNames = null;

            foreach (var attribute in prop.AttributeLists.SelectMany(l => l.Attributes))
            {
                var attrName = TrimAttribute(attribute.Name.ToString());

                switch (attrName)
                {
                    case "Key":
                        isPrimaryKey = true;
                        break;
                    case "Column":
                        columnTypeName = HandleColumn(attribute, dbProps) ?? columnTypeName;
                        break;
                    case "MaxLength":
                        dbProps["Length"] = GetInt(attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression).ToString();
                        break;
                    case "Precision":
                        HandlePrecision(attribute, dbProps);
                        break;
                    case "Required":
                        requiredAttr = true;
                        break;
                    case "DatabaseGenerated":
                        generatedOption = ReadGeneratedOption(attribute);
                        break;
                    case "ForeignKey":
                        foreignKeyNames = ReadForeignKeyNames(attribute);
                        break;
                    case "Timestamp":
                        // [Timestamp] is EF Core's mechanism for the version column
                        // (decision 030). The concurrency-token and store-generation
                        // behavior it also implies has no separate fact in the model;
                        // the one flag is the claim. The store type it maps to is the
                        // provider's business, so no type is invented here.
                        dbProps["IsVersion"] = "true";
                        break;

                    // Everything else used to fall out of this switch without a trace. Two of
                    // them change what the artifact means rather than merely impoverishing it:
                    // [NotMapped] says the property is not mapped at all and [Keyless] that the
                    // type has no key, and both went through as if absent (decision 010).
                    default:
                        entityBuilder.Report(new ConversionRecord
                        {
                            Kind = ConversionRecordKind.Loss,
                            Framework = ORMEnum.EFCore,
                            Artifact = ConversionContentType.CSharpEntity,
                            Entity = classDeclaration.Identifier.Text,
                            Property = name,
                            Reason = $"The annotation [{attrName}] has no counterpart in the intermediate representation and was dropped.",
                        });
                        break;
                }
            }

            // The two nullability channels travel apart: the question mark is the language
            // claim and stays on the type, while the column's nullability is what EF Core
            // reads from the type unless [Required] overrides it. Folding them into one
            // value used to lose the question mark of a [Required] property.
            bool databaseNullable = !requiredAttr && reading.IsNullable;

            dbProps["Nullable"] = databaseNullable ? "true" : "false";

            EmitProperty(reading);

            if (dbProps.Count > 0)
            {
                entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
            }

            if (columnTypeName is not null)
            {
                ApplyColumnTypeName(name, columnTypeName);
            }

            propertyTypes[name] = type;

            if (generatedOption is not null)
            {
                generatedOptions[name] = generatedOption;
            }

            if (isPrimaryKey)
            {
                keyPropertyNames.Add(name);
            }

            if (IsCollection(prop.Type, out var target))
            {
                entityBuilder.AddForeignKey(Cardinality.OneToMany, name, target, foreignKeyColumns: foreignKeyNames);
            }
            else if (foreignKeyNames is not null && !IsScalarTypeName(type))
            {
                // [ForeignKey] on the navigation is the claim that the property points at an
                // entity, and it names the key properties in the order of the key they reference
                // (decision 012). The annotation cannot say the relation is 1:1, so N:1 is what
                // survives the reading. The scalar guard keeps out the other legal form of the
                // attribute - [ForeignKey("Navigation")] sitting on the key property itself -
                // whose claim points the opposite way and has no place in the model yet.
                entityBuilder.AddForeignKey(Cardinality.ManyToOne, name, type, RelationRole.Owning, foreignKeyNames);
            }
            else
            {
                if (!IsScalarTypeName(type))
                {
                    // A reference navigation needs no [ForeignKey] in EF Core - the
                    // relationship follows by convention from the type being an entity of
                    // the model. The vocabulary cannot tell such a type from a scalar it
                    // does not know (uint, a key class) until every source is parsed, so
                    // the claim waits and materializes against the entities of the
                    // conversion. The shape is what the annotation would have stated: N:1
                    // with the key on this side; the callback finds the foreign key
                    // property by EF Core's naming convention once the target's key stands.
                    entityBuilder.AddConventionNavigation(
                        Cardinality.ManyToOne, name, type, RelationRole.Owning,
                        (ownerMap, targetMap) => ConventionForeignKeyProperties(ownerMap, targetMap, name));
                }

                // Candidates for the key EF Core derives by convention. Only a scalar can
                // become one, but an unrecognized name may still be a scalar outside the
                // vocabulary (uint), so unknown names stay on the list.
                conventionKeyCandidates.Add(name);
            }
        }

        // The key is defined by a single call for the whole entity.
        // The class-level [PrimaryKey(...)] takes precedence - it defines the explicit part order.
        if (classKeyNames.Count > 0)
        {
            entityBuilder.AddPrimaryKey(
                classKeyNames.Select((n, i) => (n, i + 1, StrategyFor(n, composite: true))).ToList());
        }
        else if (keyPropertyNames.Count > 0)
        {
            bool isComposite = keyPropertyNames.Count > 1;

            entityBuilder.AddPrimaryKey(
                keyPropertyNames.Select((n, i) => (n, i + 1, StrategyFor(n, isComposite))).ToList());
        }
        else if (FindConventionKey(classDeclaration.Identifier.Text, conventionKeyCandidates) is { } conventionKey)
        {
            entityBuilder.AddPrimaryKey(StrategyFor(conventionKey, composite: false), conventionKey);
        }

        // Local because it reads what the loop above collected. The annotation wins where it is
        // present; otherwise EF Core's convention is what the source claims, and that convention
        // depends both on the type of the key and on whether the key is composite.
        PrimaryKeyStrategy StrategyFor(string propertyName, bool composite)
        {
            if (generatedOptions.TryGetValue(propertyName, out var option))
            {
                return option switch
                {
                    "None" => PrimaryKeyStrategy.Assigned,

                    // The annotation says the store produces the value on insert; which
                    // mechanism it uses is the provider's choice, so this is Auto rather than
                    // Identity. Computed adds regeneration on update, a fact about the column
                    // that has no place in the key vocabulary (decision 011).
                    "Identity" or "Computed" => PrimaryKeyStrategy.Auto,
                    _ => PrimaryKeyStrategy.Unspecified,
                };
            }

            // EF Core generates no value for the parts of a composite key.
            if (composite)
            {
                return PrimaryKeyStrategy.Assigned;
            }

            return StrategyFromKeyType(propertyTypes.GetValueOrDefault(propertyName));
        }
    }

    /// <summary>
    /// Reads the names out of [ForeignKey("A,B")] or [ForeignKey(nameof(A))]. The attribute
    /// takes a single string; a composite key arrives comma-separated inside it, in the order
    /// of the key it references, and the order is kept because the pairing relies on it.
    /// </summary>
    private static List<string>? ReadForeignKeyNames(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;

        var value = argument switch
        {
            null => null,
            InvocationExpressionSyntax inv
                when inv.Expression is IdentifierNameSyntax id
                  && id.Identifier.Text == "nameof"
                  && inv.ArgumentList.Arguments.Count == 1
                  && inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax propName
                => propName.Identifier.Text,
            _ => GetString(argument),
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var names = value.Split(',')
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToList();

        return names.Count == 0 ? null : names;
    }

    /// <summary>
    /// Whether the written type is a scalar of the language type model - which a navigation
    /// property can never be.
    /// </summary>
    private static bool IsScalarTypeName(string typeText)
        => CSharpTypeConvertor.FromString(typeText).Category == LangTypeCategory.Scalar;

    /// <summary>
    /// EF Core's discovery of the foreign key property behind a bare reference navigation,
    /// run once the target and its key are known: a property named {Navigation}{KeyName},
    /// {Navigation}Id, {TargetType}{KeyName} or {TargetType}Id - in that order, compared
    /// case-insensitively like the key convention - whose scalar type matches the key
    /// part's. The convention pairs single-part keys only; a composite foreign key exists
    /// in EF Core solely through explicit configuration. Null means EF Core would fall
    /// back to a shadow property, a column no class member carries: the model cannot state
    /// that, so the relation goes without columns and the target reports the omission
    /// where it emits (decision 012).
    /// </summary>
    private static IReadOnlyList<string>? ConventionForeignKeyProperties(
        EntityMap owner, EntityMap target, string navigationName)
    {
        if (target.PrimaryKey is not { Parts: [var keyPart] }
            || keyPart.PropertyMap.Property.Type is not { Category: LangTypeCategory.Scalar } keyType)
        {
            return null;
        }

        string[] candidates =
        [
            navigationName + keyPart.PropertyMap.Property.Name,
            navigationName + "Id",
            target.Entity.Name + keyPart.PropertyMap.Property.Name,
            target.Entity.Name + "Id",
        ];

        foreach (var candidate in candidates)
        {
            var match = owner.Entity.Properties.FirstOrDefault(p =>
                p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                && p.Type is { Category: LangTypeCategory.Scalar } propertyType
                && propertyType.ScalarType == keyType.ScalarType);

            if (match is not null)
            {
                return [match.Name];
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the option out of [DatabaseGenerated(DatabaseGeneratedOption.X)] and returns the X.
    /// The argument is an enum member, so whatever follows the last dot is the value, whether
    /// the source wrote it qualified or not.
    /// </summary>
    private static string? ReadGeneratedOption(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString();

        return string.IsNullOrEmpty(argument)
            ? null
            : argument.Split('.').Last().Trim();
    }

    /// <summary>
    /// The convention EF Core applies to a key that says nothing about generation: integer and
    /// Guid keys get a value on add, everything else does not. Reading the convention belongs to
    /// the parser wherever its absence would change the meaning (decision 015), and it does here
    /// - assuming generation for a string key produces a target mapping the database rejects.
    ///
    /// The list describes EF Core: the unsigned types have no value in the neutral type model
    /// and survive as Unknown, but EF Core's convention treats them as generated all the same,
    /// so they are matched here by name.
    /// </summary>
    private static PrimaryKeyStrategy StrategyFromKeyType(string? typeText)
    {
        var type = typeText?.Trim().Split('.').Last();

        return type switch
        {
            null => PrimaryKeyStrategy.Unspecified,
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                or "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64"
                => PrimaryKeyStrategy.Auto,
            "Guid" => PrimaryKeyStrategy.Uuid,
            _ => PrimaryKeyStrategy.Assigned,
        };
    }

    /// <summary>
    /// EF Core derives a primary key by convention from a property named Id or
    /// {EntityName}Id when none is declared explicitly. Such an entity does have a key, so
    /// the parser records it: leaving it out would make it indistinguishable from an entity
    /// that genuinely has none, and the target builder would mark that one keyless.
    /// Reading the convention of the source framework is the parser's job precisely because
    /// only the parser knows which framework the input came from - see decision 015.
    ///
    /// The comparison is case-insensitive, so CustomerID counts as CustomerId. That matches
    /// what EF Core does, but the documentation page on keys does not spell it out, so the
    /// claim rests on observed behavior rather than on a quotable sentence.
    /// </summary>
    private static string? FindConventionKey(string entityName, List<string> propertyNames)
    {
        // Id takes precedence over {EntityName}Id when both are present.
        var id = propertyNames.FirstOrDefault(n => n.Equals("Id", StringComparison.OrdinalIgnoreCase));
        if (id is not null)
        {
            return id;
        }

        var suffixed = entityName + "Id";
        return propertyNames.FirstOrDefault(n => n.Equals(suffixed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Trims the "Attribute" suffix from the attribute name if it exists.
    /// </summary>
    private static string TrimAttribute(string name)
    {
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    /// <summary>
    /// Checks if the given type is a collection type and extracts the target type if it is.
    /// The recognized names are the ones the shared type vocabulary reads as a collection
    /// (<see cref="Common.Convertors.CSharpTypeConvertor"/>, decision 014); a name this
    /// misses would be read as a scalar candidate and the navigation would fall away in
    /// silence - and the interface spellings matter, because this solution's own
    /// NHibernate output declares collections as IList&lt;T&gt;/ISet&lt;T&gt; (decision 035).
    /// </summary>
    private static bool IsCollection(TypeSyntax type, out string target)
    {
        target = string.Empty;

        if (type is GenericNameSyntax g &&
            (g.Identifier.ValueText is
                "List" or "IList" or "IReadOnlyList"
                or "HashSet" or "ISet" or "IReadOnlySet"
                or "ICollection" or "IEnumerable" or "IReadOnlyCollection") &&
            g.TypeArgumentList.Arguments.Count == 1)
        {
            target = g.TypeArgumentList.Arguments[0].ToString();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts a string value from an expression syntax, if it is a literal expression containing a string.
    /// </summary>
    private static string? GetString(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax lit 
            && lit.Token.Value is string s ? s : null;
    }

    /// <summary>
    /// Extracts an integer value from an expression syntax, if it is a literal expression containing an integer.
    /// </summary>
    private static int GetInt(ExpressionSyntax? expression)
    {
        return expression is LiteralExpressionSyntax lit 
            && lit.Token.Value is int i ? i : 0;
    }

    /// <summary>
    /// Handles the "Column" attribute: the column name goes into the string facts, the
    /// TypeName is returned to travel through the typed channel (decision 019).
    /// </summary>
    private static string? HandleColumn(AttributeSyntax attribute, Dictionary<string, string> dbProps)
    {
        string? typeName = null;

        foreach (var arg in attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
        {
            var named = arg.NameEquals?.Name.Identifier.ValueText;

            if (named is null)
            {
                dbProps["ColumnName"] = GetString(arg.Expression) ?? string.Empty;
            }
            else if (named.Equals("TypeName", StringComparison.OrdinalIgnoreCase))
            {
                typeName = GetString(arg.Expression);
            }
        }

        return string.IsNullOrWhiteSpace(typeName) ? null : typeName;
    }

    /// <summary>
    /// The TypeName of a [Column] read into the neutral vocabulary (decision 019): the
    /// family with the facets the name and its arguments claim, and the literal spelling
    /// on the escape path where the family is coarser or missing. A name outside the
    /// vocabulary is a record, not an exception - the family fact is missing rather than
    /// lost, and the catalog may still supply it (decision 010).
    /// </summary>
    private void ApplyColumnTypeName(string propertyName, string columnTypeName)
    {
        var reading = DatabaseTypeConvertor.FromEfCore(columnTypeName);

        entityBuilder.SetPropertyDatabaseType(
            propertyName,
            reading.Type,
            reading.IsUnicode,
            reading.KeepLiteral || reading.Type is null ? columnTypeName.Trim() : null,
            reading.Length,
            reading.Precision,
            reading.Scale);

        if (reading.Type is null)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = $"The type '{columnTypeName.Trim()}' has no family in the neutral vocabulary; its literal "
                    + "spelling is kept on the escape path and no family is claimed (decision 019).",
            });
        }
    }

    /// <summary>
    /// Handles the "Precision" attribute, extracting precision and scale values if provided.
    /// </summary>
    private static void HandlePrecision(AttributeSyntax attr, IDictionary<string, string> dbProps)
    {
        var args = attr.ArgumentList?.Arguments ?? default;
        if (args.Count > 0)
        {
            dbProps["Precision"] = GetInt(args[0].Expression).ToString();
            if (args.Count > 1)
            {
                dbProps["Scale"] = GetInt(args[1].Expression).ToString();
            }
        }
    }
}
