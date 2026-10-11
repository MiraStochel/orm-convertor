using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Common.Sql;
using CSharpEntityParsing;
using EFCoreWrappers.Convertors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Parses an EF Core entity class from C# source code: the shared structural reading plus
/// EF Core's attribute mapping and the conventions whose absence would change the meaning, under
/// whatever the fluent configuration, read before it, already stated (decision 132).
/// </summary>
public class EFCoreEntityParser : CSharpEntityParser
{
    /// <summary>
    /// What the source declared about the dialect of the literal SQL it spells
    /// (decision 088); null where it declared nothing, which reads as it always did. It is
    /// a fact about the source and therefore arrives at the parser, not at the builder -
    /// the builder belongs to the target framework (S1). The only thing it governs here is
    /// the TypeName of a [Column], the one place an EF Core class spells a database
    /// system's own type.
    /// </summary>
    private readonly SourceSqlDialect? declaredSourceDialect;

    /// <summary>
    /// What the fluent configuration, read before this parser, left for the class to finish
    /// (decision 132): relationships, which wait for the type of their navigations, and the
    /// convention and default that stand below the annotations - the table of a DbSet and the
    /// default schema. A parser constructed without one reads as if no configuration existed.
    /// </summary>
    private readonly EFCoreReadingContext context;

    public EFCoreEntityParser(
        AbstractEntityBuilder entityBuilder,
        SourceSqlDialect? declaredSourceDialect = null,
        EFCoreReadingContext? context = null)
        : base(entityBuilder)
    {
        this.declaredSourceDialect = declaredSourceDialect;
        this.context = context ?? new EFCoreReadingContext();
    }

    /// <summary>
    /// A class whose own members hand a LINQ query to the context is the code around queries,
    /// not an entity (decision 111), answered by the same search the query pass reads the
    /// queries by.
    /// </summary>
    protected override IEnumerable<SyntaxNode> FindHandovers(string source)
        => new EFCoreLinqQueryParser(() => throw new InvalidOperationException("The search for handovers builds no query."))
        {
            Limits = Limits,
        }.FindHandovers(source);

    /// <summary>
    /// The context and an entity type configuration are EF Core's own API and not entity
    /// types (decision 111). Read as classes, they would come out as entities - the context
    /// with a collection relation to every DbSet it declares. What they state is read by the
    /// fluent configuration parser (decision 132); the class itself does not travel, so the
    /// record stays a loss.
    /// </summary>
    protected override string? FrameworkApi(ClassDeclarationSyntax classDeclaration)
    {
        var name = classDeclaration.Identifier.Text;

        if (EFCoreContext.IsContext(classDeclaration))
        {
            return $"The class '{name}' is the EF Core context, not an entity; its DbSet properties and OnModelCreating "
                + "are read as configuration (decision 132), and the class itself is not carried into the output.";
        }

        return EFCoreContext.ConfiguredEntity(classDeclaration) is not null
            ? $"The class '{name}' is an EF Core entity type configuration, not an entity; its Configure method is read "
                + "as configuration (decision 132), and the class itself is not carried into the output."
            : null;
    }

    /// <summary>
    /// Parses class attributes: the table and schema of [Table], the unique constraints of
    /// [Index] (decision 055), the stated keylessness of [Keyless] (decision 063), and a
    /// record for everything else.
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
    /// <summary>
    /// EF Core discovers properties, not fields, so no column is lost with a field - only the
    /// member, and a backing field the class may have pointed a property at.
    /// </summary>
    protected override string FieldNote
        => "EF Core maps no field by convention, so no column is lost with it, only the member.";

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

            if (name.Equals("Keyless", StringComparison.OrdinalIgnoreCase))
            {
                if (entityBuilder.EntityMap.PrimaryKey is { } configured)
                {
                    // Only the fluent configuration, read before the class, states a key by
                    // now; HasKey overrides the annotation in EF Core, so the key stands
                    // (decision 132) - unlike [PrimaryKey] beside [Keyless] in one class,
                    // which EF Core refuses and the completeness gate refuses (decision 063).
                    entityBuilder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Conflict,
                        Framework = ORMEnum.EFCore,
                        Artifact = ConversionContentType.CSharpEntity,
                        Entity = classDeclaration.Identifier.Text,
                        Category = MappingFactCategory.PrimaryKey,
                        Reason = $"The configuration states the key ({string.Join(", ", configured.Parts.Select(p => p.PropertyMap.Property.Name))}) "
                            + "and the class carries [Keyless]; the fluent configuration overrides the annotation in EF Core, "
                            + "so the key is kept (decision 132).",
                    });
                    continue;
                }

                entityBuilder.MarkNoKey();
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

        // Below the configuration and the annotations, both of which are read by now: the
        // table a DbSet property gives is EF Core's convention and materializes as a claim of
        // the source (decision 067, applied by decision 132), the default schema of the model
        // is EF Core's default for every entity without its own. Both fill an empty fact only,
        // and silently: where a stronger level spoke, EF Core takes its word without an event.
        if (context.DbSetOf(classDeclaration.Identifier.Text) is { } dbSet && entityBuilder.EntityMap.Table is null)
        {
            entityBuilder.AddTable(dbSet);
        }

        if (context.DefaultSchema is { } schema && entityBuilder.EntityMap.Schema is null)
        {
            entityBuilder.AddSchema(schema);
        }
    }

    /// <summary>
    /// Reads the table and the schema out of [Table("Orders", Schema = "sales")].
    /// </summary>
    private void ReadTableAttribute(AttributeSyntax attribute)
    {
        string? table = null;
        string? schema = null;
        AttributeArgumentSyntax? tableArgument = null;
        AttributeArgumentSyntax? schemaArgument = null;

        foreach (var arg in attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
        {
            var named = arg.NameEquals?.Name.Identifier.ValueText;

            if (named is null)
            {
                table = GetString(arg.Expression);
                tableArgument = arg;
            }
            else if (named.Equals("Schema", StringComparison.OrdinalIgnoreCase))
            {
                schema = GetString(arg.Expression);
                schemaArgument = arg;
            }
            else if (named.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                table = GetString(arg.Expression);
                tableArgument = arg;
            }
        }

        // The attribute is where both facts stand; each argument is where its own does
        // (decision 125).
        using var scope = entityBuilder.At(
            Range(attribute),
            new FactRefinement(MappingFactCategory.TableName, Range((SyntaxNode?)tableArgument ?? attribute)),
            new FactRefinement(MappingFactCategory.SchemaName, Range((SyntaxNode?)schemaArgument ?? attribute)));

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

        using var scope = entityBuilder.At(Range(attribute));

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

    private bool IsConcurrencyToken(PropertyDeclarationSyntax prop, string entityName)
        => HasAttribute(prop, "ConcurrencyCheck") || context.IsConcurrencyToken(entityName, prop.Identifier.Text);

    private static bool HasAttribute(PropertyDeclarationSyntax prop, string name)
        => prop.AttributeLists.SelectMany(l => l.Attributes)
            .Any(a => TrimAttribute(a.Name.ToString()).Equals(name, StringComparison.Ordinal));

    /// <summary>
    /// The three conditions of decision 116 under which [ConcurrencyCheck] is the version
    /// column the application keeps, as the reason the failing one gives, or null when all
    /// hold: one token in the class, no [Timestamp] beside it, and a type the target
    /// frameworks lead a version over - integral or DateTime, nullable or not.
    /// </summary>
    private static string? ConcurrencyTokenIsNotTheVersion(PropertyReading reading, int tokensInClass, bool classHasTimestamp)
    {
        if (tokensInClass > 1)
        {
            return $"the class carries {tokensInClass} tokens";
        }

        if (classHasTimestamp)
        {
            return "[Timestamp] already names the version of the class";
        }

        return CSharpTypeConvertor.FromString(reading.Type) is
            { Category: LangTypeCategory.Scalar, ScalarType: ScalarType.Short or ScalarType.Int or ScalarType.Long or ScalarType.DateTime }
            ? null
            : $"the property is a '{reading.Type}'";
    }

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

        // [ConcurrencyCheck] is read per class, not per property (decision 116): it is the
        // version the application keeps only where it is the one token of the class and no
        // [Timestamp] already names the version.
        // The fluent IsConcurrencyToken and IsRowVersion count the same as the annotations
        // (decision 132).
        var entityName = classDeclaration.Identifier.Text;
        var properties = classDeclaration.Members.OfType<PropertyDeclarationSyntax>().ToList();
        var concurrencyTokens = properties.Count(p => IsConcurrencyToken(p, entityName));
        var classHasTimestamp = properties.Any(p => HasAttribute(p, "Timestamp")) || context.HasRowVersion(entityName);

        // A key the configuration stated before the class was read (decision 132): the
        // convention does not look for another one, and the class supplies the strategy the
        // configuration left to the convention of the key's type.
        bool keyConfigured = entityBuilder.EntityMap.PrimaryKey is not null;

        // Where each property, its [Key] and its [DatabaseGenerated] stand, for the key the
        // loop below states once the whole class is read (decision 125).
        var propertyRanges = new Dictionary<string, TextRange>();
        var keyRanges = new Dictionary<string, TextRange>();
        var strategyRanges = new Dictionary<string, TextRange>();

        foreach (var prop in properties)
        {
            var reading = ReadProperty(prop);
            var name = reading.Name;
            var type = reading.Type;

            var dbProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool isPrimaryKey = false;
            bool isTransient = false;
            bool requiredAttr = false;
            string? generatedOption = null;
            string? columnTypeName = null;
            bool? unicodeAttr = null;
            string? inverseNavigation = null;
            List<string>? foreignKeyNames = null;

            // Which annotation states which fact, so that the facts of this property are placed
            // at the annotation rather than at the whole declaration (decision 125).
            var refinements = new List<FactRefinement>();
            propertyRanges[name] = Range(prop);

            // A concurrency token the application keeps: EF Core compares the value on write
            // and never produces a new one. It is the version column only on the one integral
            // or DateTime property of a class without [Timestamp] (decision 116); anywhere else
            // it protects a column and is not a version, which the record says. Beside
            // [Timestamp] or IsRowVersion on the same property it states nothing new.
            void ReadConcurrencyToken(string spelling, TextRange? range)
            {
                if (HasAttribute(prop, "Timestamp") || context.IsRowVersion(entityName, name))
                {
                    return;
                }

                if (ConcurrencyTokenIsNotTheVersion(reading, concurrencyTokens, classHasTimestamp) is { } why)
                {
                    entityBuilder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Loss,
                        Framework = ORMEnum.EFCore,
                        Artifact = ConversionContentType.CSharpEntity,
                        Entity = entityName,
                        Property = name,
                        Category = MappingFactCategory.VersionColumn,
                        Reason = $"{spelling} is read as the version column the application keeps only on the "
                            + $"one short, int, long or DateTime property of a class without [Timestamp]; {why}, "
                            + "so the token protects this column and is dropped (decision 116).",
                    });
                    return;
                }

                dbProps["IsVersion"] = "true";
                dbProps["ApplicationManagedVersion"] = "true";

                if (range is { } at)
                {
                    refinements.Add(new(MappingFactCategory.VersionColumn, name, at));
                }
            }

            foreach (var attribute in prop.AttributeLists.SelectMany(l => l.Attributes))
            {
                var attrName = TrimAttribute(attribute.Name.ToString());

                switch (attrName)
                {
                    case "Key":
                        isPrimaryKey = true;
                        keyRanges[name] = Range(attribute);
                        break;
                    case "Column":
                        columnTypeName = HandleColumn(attribute, dbProps) ?? columnTypeName;
                        refinements.Add(new(MappingFactCategory.ColumnName, name, Range(attribute)));
                        refinements.Add(new(MappingFactCategory.DatabaseType, name, Range(attribute)));
                        break;
                    case "MaxLength":
                    case "StringLength":
                        // Two spellings of one mapping fact: EF Core reads the maximum length
                        // out of either. What [StringLength] says beyond it - MinimumLength and
                        // the message - is validation, which EF Core's own model does not carry
                        // and this one therefore loses nothing by not carrying either. Stated
                        // twice with different numbers, the first reading stands and the
                        // difference is a record (decision 017); the dictionary alone would let
                        // the later attribute overwrite the earlier one in silence.
                        HandleLength(
                            GetInt(attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression),
                            attrName, dbProps, classDeclaration.Identifier.Text, name);
                        refinements.Add(new(MappingFactCategory.Length, name, Range(attribute)));
                        break;
                    case "Unicode":
                        // The bare annotation claims unicode, [Unicode(false)] the opposite. It
                        // is a facet of the type claim, so it travels through the typed channel
                        // (decision 019) and is applied after [Column(TypeName)]: EF Core lets an
                        // explicit store type decide and ignores the facet beside it, so the type
                        // name's claim stands and a disagreeing [Unicode] becomes a conflict.
                        unicodeAttr = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is { } unicodeArgument
                            ? GetBool(unicodeArgument)
                            : true;
                        refinements.Add(new(MappingFactCategory.DatabaseType, name, Range(attribute)));
                        break;
                    case "InverseProperty":
                        // The navigation on the far side that is the other end of this one -
                        // what EF Core needs where two pairs of navigations run between the same
                        // two entities and the convention cannot tell which belongs to which.
                        // Relation.InverseRelationName is that fact's place in the model.
                        inverseNavigation = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is { } inverseArgument
                            ? ReadPropertyName(inverseArgument)
                            : null;
                        break;
                    case "Precision":
                        HandlePrecision(attribute, dbProps);
                        refinements.Add(new(MappingFactCategory.PrecisionAndScale, name, Range(attribute)));
                        break;
                    case "Required":
                        requiredAttr = true;
                        refinements.Add(new(MappingFactCategory.Nullability, name, Range(attribute)));
                        break;
                    case "DatabaseGenerated":
                        generatedOption = ReadGeneratedOption(attribute);
                        strategyRanges[name] = Range(attribute);
                        break;
                    case "ForeignKey":
                        foreignKeyNames = ReadForeignKeyNames(attribute);
                        refinements.Add(new(MappingFactCategory.ForeignKeyColumns, name, Range(attribute)));
                        break;
                    case "Timestamp":
                        // [Timestamp] is EF Core's mechanism for the version column
                        // (decision 030). The concurrency-token and store-generation
                        // behavior it also implies has no separate fact in the model;
                        // the one flag is the claim. The store type it maps to is the
                        // provider's business, so no type is invented here.
                        dbProps["IsVersion"] = "true";
                        refinements.Add(new(MappingFactCategory.VersionColumn, name, Range(attribute)));
                        break;
                    case "ConcurrencyCheck":
                        ReadConcurrencyToken("[ConcurrencyCheck]", Range(attribute));
                        break;
                    case "NotMapped":
                        // The property stays on the class and leaves EF Core's model
                        // (decision 072); what that does to the rest of the reading is
                        // decided once all annotations are known, below.
                        isTransient = true;
                        refinements.Add(new(MappingFactCategory.TransientProperty, name, Range(attribute)));
                        break;

                    // Everything else used to fall out of this switch without a trace, and
                    // some of it - [NotMapped] before decision 072 - changed what the artifact
                    // means rather than merely impoverishing it (decision 010).
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

            // The fluent IsConcurrencyToken is read by the same rule as the annotation, here,
            // because the rule asks about the class (decisions 116 and 132); beside the
            // annotation it says nothing new.
            if (context.IsConcurrencyToken(entityName, name) && !HasAttribute(prop, "ConcurrencyCheck"))
            {
                ReadConcurrencyToken("IsConcurrencyToken()", null);
            }

            // A property the configuration took out of the model with Ignore is read the way
            // [NotMapped] is (decision 132).
            isTransient |= entityBuilder.EntityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == name)?.IsTransient == true;

            // The fluent ValueGeneratedNever or ValueGeneratedOnAdd outranks [DatabaseGenerated]
            // as the configuration outranks every annotation (decision 132).
            if (context.GeneratedOptionOf(entityName, name) is { } configuredOption)
            {
                if (generatedOption is not null && generatedOption != configuredOption)
                {
                    entityBuilder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Conflict,
                        Framework = ORMEnum.EFCore,
                        Artifact = ConversionContentType.CSharpEntity,
                        Entity = entityName,
                        Property = name,
                        Category = MappingFactCategory.PrimaryKeyStrategy,
                        Reason = $"The configuration states the value generation '{configuredOption}' and [DatabaseGenerated] "
                            + $"states '{generatedOption}'; the fluent configuration overrides the annotation in EF Core, so the "
                            + "configuration is kept (decision 132).",
                    });
                }

                generatedOption = configuredOption;
            }

            // Everything the reading of this property leaves in the representation is placed at
            // the declaration, or at the annotation that stated it (decision 125). The column's
            // nullability without [Required] is read from the type, so the type is where it stands.
            if (!requiredAttr)
            {
                refinements.Add(new(MappingFactCategory.Nullability, name, Range(prop.Type)));
            }

            using var scope = entityBuilder.At(Range(prop), [.. refinements]);

            if (isTransient)
            {
                // [NotMapped] takes the property out of EF Core's model altogether: the
                // other mapping annotations on it are not read by EF Core, the key
                // convention does not consider it and a navigation so marked founds no
                // relationship. Only the language facts and the statement itself travel
                // (decision 072); the dead annotations produce no record, because they
                // state nothing in the source's model and so nothing is lost. [Key] is the
                // one claim that survives beside the flag: the two contradict each other,
                // and both enter the model so that the completeness gate refuses the entity
                // the way decision 063 refuses [Keyless] beside a class-level [PrimaryKey].
                EmitProperty(reading);
                entityBuilder.MarkTransient(name);
                propertyTypes[name] = type;

                if (isPrimaryKey)
                {
                    keyPropertyNames.Add(name);
                }

                continue;
            }

            // The two nullability channels travel apart: the question mark is the language
            // claim and stays on the type, while the column's nullability is what EF Core
            // reads from the type unless [Required] overrides it. Folding them into one
            // value used to lose the question mark of a [Required] property.
            // [Required] is an annotation and is compared with the configuration; the reading
            // from the type is the convention, which stands below both and so speaks only where
            // neither did - IsRequired(false) over a string is no conflict, EF Core takes the
            // configuration without an event (decisions 068 and 132).
            if (requiredAttr)
            {
                dbProps["Nullable"] = "false";
            }
            else if (entityBuilder.EntityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == name)?.IsNullable is null)
            {
                dbProps["Nullable"] = reading.IsNullable ? "true" : "false";
            }

            EmitProperty(reading);

            if (dbProps.Count > 0)
            {
                entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
            }

            if (columnTypeName is not null)
            {
                EFCoreStoreType.Apply(
                    entityBuilder, name, columnTypeName, declaredSourceDialect,
                    $"[Column(TypeName = \"{columnTypeName.Trim()}\")]");
            }

            if (unicodeAttr is not null)
            {
                // After the type name on purpose - see the [Unicode] branch above.
                entityBuilder.SetPropertyDatabaseType(name, type: null, isUnicode: unicodeAttr);
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

            var isCollection = IsCollection(prop.Type, out var target);

            if (isCollection && IsPrimitiveElement(target))
            {
                // A collection of primitive values is not a navigation: EF Core maps it as a
                // primitive collection, one column holding the values as JSON. Unlike an
                // unknown element, a scalar of the vocabulary or a C# keyword type can never
                // name an entity, so the answer does not wait for the entity set. The language
                // fact travels - a collection of the scalar, with no relation behind it - and
                // the storage in one column, which the model has no place for, is a loss
                // (decision 048).
                entityBuilder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = ORMEnum.EFCore,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = classDeclaration.Identifier.Text,
                    Property = name,
                    Reason = $"The collection of '{target}' is EF Core's primitive collection - one column holding the "
                        + "values as JSON - and not a navigation; the intermediate representation has no place for a "
                        + "collection stored in a column, so the property travels as a collection with no relation "
                        + "behind it and how it is stored is dropped.",
                });

                if (foreignKeyNames is not null)
                {
                    entityBuilder.Report(RelationshipAnnotationDropped(
                        classDeclaration.Identifier.Text, name, $"[ForeignKey(\"{string.Join(",", foreignKeyNames)}\")]"));
                }

                if (inverseNavigation is not null)
                {
                    entityBuilder.Report(RelationshipAnnotationDropped(
                        classDeclaration.Identifier.Text, name, $"[InverseProperty(\"{inverseNavigation}\")]"));
                }
            }
            else if ((isCollection || !IsScalarTypeName(type))
                && context.RelationshipFor(entityName, name, SimpleName(isCollection ? target : type)) is var (configured, mirrored))
            {
                ApplyConfiguredRelationship(
                    configured, mirrored, entityName, name, isCollection ? target : type, foreignKeyNames, inverseNavigation);
            }
            else if (isCollection)
            {
                if (foreignKeyNames is not null)
                {
                    // [ForeignKey] on a collection names the key properties of the entity on
                    // the far side, which is the annotation stating a one-to-many outright:
                    // the dependent holds the columns, so nothing waits for the far side.
                    entityBuilder.AddForeignKey(
                        Cardinality.OneToMany, name, target,
                        foreignKeyColumns: foreignKeyNames, inverseNavigation: inverseNavigation);
                }
                else
                {
                    // Without the annotation the collection is EF Core's convention, and
                    // which convention it is depends on what the far entity declares back
                    // (decision 067): the claim waits like a reference navigation does and
                    // CollectionCardinality answers once the entities of the conversion are
                    // known. Only the cardinality is left open - the role follows from it,
                    // and neither shape puts a column on this side.
                    entityBuilder.AddConventionNavigation(
                        Cardinality.OneToMany, name, target,
                        inverseNavigation: inverseNavigation,
                        shape: (owner, targetMap) => CollectionCardinality(owner, targetMap, name, inverseNavigation));
                }
            }
            else if (foreignKeyNames is not null && !IsScalarTypeName(type))
            {
                // [ForeignKey] on the navigation is the claim that the property points at an
                // entity, and it names the key properties in the order of the key they reference
                // (decision 012). The annotation cannot say the relation is 1:1, so N:1 is what
                // survives the reading. The scalar guard keeps out the other legal form of the
                // attribute - [ForeignKey("Navigation")] sitting on the key property itself -
                // whose claim points the opposite way and has no place in the model yet.
                entityBuilder.AddForeignKey(
                    Cardinality.ManyToOne, name, type, RelationRole.Owning, foreignKeyNames,
                    inverseNavigation: inverseNavigation);
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
                        (ownerMap, targetMap) => ConventionForeignKeyProperties(ownerMap, targetMap, name),
                        inverseNavigation);
                }
                else if (inverseNavigation is not null)
                {
                    // [InverseProperty] pairs two navigations, and a scalar is neither.
                    entityBuilder.Report(RelationshipAnnotationDropped(
                        classDeclaration.Identifier.Text, name, $"[InverseProperty(\"{inverseNavigation}\")]"));
                }

                if (foreignKeyNames is not null && IsScalarTypeName(type)
                    && !(foreignKeyNames.Count == 1 && name.Equals(foreignKeyNames[0] + "Id", StringComparison.OrdinalIgnoreCase)))
                {
                    // The other legal form of the attribute: [ForeignKey("Navigation")] on the
                    // key property, naming the navigation whose column this is. The reading
                    // pairs a column with its navigation from the navigation's side only, so
                    // the claim has no place to land and is dropped (decision 048) - unless
                    // the property is named as the convention would find it anyway
                    // ({Navigation}Id), where the annotation states nothing the convention
                    // does not derive.
                    entityBuilder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Loss,
                        Framework = ORMEnum.EFCore,
                        Artifact = ConversionContentType.CSharpEntity,
                        Entity = classDeclaration.Identifier.Text,
                        Property = name,
                        Reason = $"[ForeignKey(\"{string.Join(",", foreignKeyNames)}\")] on the scalar property '{name}' names the "
                            + "navigation whose foreign key column the property is; the reading pairs a column with its navigation "
                            + "from the navigation's side only, so the claim is dropped and the column of the navigation "
                            + $"'{string.Join(",", foreignKeyNames)}' is found by naming convention alone, or supplied by the database catalog.",
                    });
                }

                // Candidates for the key EF Core derives by convention. Only a scalar can
                // become one, but an unrecognized name may still be a scalar outside the
                // vocabulary (uint), so unknown names stay on the list.
                conventionKeyCandidates.Add(name);
            }
        }

        // The key is defined by a single call for the whole entity.
        // The class-level [PrimaryKey(...)] takes precedence - it defines the explicit part order.
        // A stated [Keyless] plays out the way EF Core itself plays it (decision 063): beside
        // the class-level [PrimaryKey] both claims enter the model and the completeness gate
        // refuses the entity, because EF Core builds no model from that input either.
        bool statesNoKey = entityBuilder.EntityMap.HasNoKey;

        // Where the key stands (decision 125): at the class-level [PrimaryKey], at each part's
        // [Key], or at the declaration of the property the convention picks; the strategy of a
        // part at its [DatabaseGenerated], where the source wrote one.
        FactRefinement[] KeyRefinements(IEnumerable<string> names, Func<string, TextRange> partRange)
            => [.. names.SelectMany(n => new FactRefinement[]
            {
                new(MappingFactCategory.PrimaryKey, n, partRange(n)),
                new(MappingFactCategory.PrimaryKeyStrategy, n, strategyRanges.GetValueOrDefault(n, partRange(n))),
            })];

        if (classKeyNames.Count > 0)
        {
            var primaryKeyAttribute = classDeclaration.AttributeLists
                .SelectMany(l => l.Attributes)
                .FirstOrDefault(a => TrimAttribute(a.Name.ToString()).Equals("PrimaryKey", StringComparison.OrdinalIgnoreCase));

            using var scope = entityBuilder.At(
                primaryKeyAttribute is null ? null : Range(primaryKeyAttribute),
                KeyRefinements(classKeyNames, n => primaryKeyAttribute is null ? propertyRanges.GetValueOrDefault(n) : Range(primaryKeyAttribute)));

            entityBuilder.AddPrimaryKey(
                classKeyNames.Select((n, i) => (n, i + 1, StrategyFor(n, composite: true))).ToList());
        }
        else if (keyPropertyNames.Count > 0)
        {
            using var scope = entityBuilder.At(
                keyRanges.GetValueOrDefault(keyPropertyNames[0], propertyRanges.GetValueOrDefault(keyPropertyNames[0])),
                KeyRefinements(keyPropertyNames, n => keyRanges.GetValueOrDefault(n, propertyRanges.GetValueOrDefault(n))));

            if (statesNoKey)
            {
                // EF Core ignores [Key] on a keyless type with a warning and the model
                // builds keyless; the key claim is dropped the same way, out loud.
                entityBuilder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Conflict,
                    Framework = ORMEnum.EFCore,
                    Artifact = ConversionContentType.CSharpEntity,
                    Entity = classDeclaration.Identifier.Text,
                    Property = keyPropertyNames.Count == 1 ? keyPropertyNames[0] : null,
                    Category = MappingFactCategory.PrimaryKey,
                    Reason = $"The entity is stated keyless ([Keyless] or HasNoKey) and [Key] stands on ({string.Join(", ", keyPropertyNames)}); "
                        + "EF Core reads such a type as keyless and only warns about the KeyAttribute, "
                        + "so the key claim is dropped and the entity stays keyless (decision 063).",
                });
            }
            else
            {
                bool isComposite = keyPropertyNames.Count > 1;

                entityBuilder.AddPrimaryKey(
                    keyPropertyNames.Select((n, i) => (n, i + 1, StrategyFor(n, isComposite))).ToList());
            }
        }
        else if (!statesNoKey
            && !keyConfigured
            && FindConventionKey(classDeclaration.Identifier.Text, conventionKeyCandidates) is { } conventionKey)
        {
            using var scope = entityBuilder.At(
                propertyRanges.GetValueOrDefault(conventionKey),
                KeyRefinements([conventionKey], n => propertyRanges.GetValueOrDefault(n)));

            entityBuilder.AddPrimaryKey(StrategyFor(conventionKey, composite: false), conventionKey);
        }

        // The configuration's HasKey names the parts and leaves their strategy to EF Core's
        // convention over the key's type, which only the class declares: the same claim over
        // the same parts fills the Unspecified strategy and nothing else (decisions 036, 132).
        if (keyConfigured && entityBuilder.EntityMap.PrimaryKey is { } standing
            && standing.Parts.Any(p => p.Strategy == PrimaryKeyStrategy.Unspecified))
        {
            var parts = standing.Parts.OrderBy(p => p.Order).Select(p => p.PropertyMap.Property.Name).ToList();

            entityBuilder.AddPrimaryKey(
                parts.Select((n, i) => (n, i + 1, StrategyFor(n, composite: parts.Count > 1))).ToList());
        }

        // A relationship the configuration states on a navigation the class does not declare
        // has nothing to stand on; EF Core refuses such a model, and the claim is dropped out loud.
        foreach (var relationship in context.RelationshipsOf(entityName)
            .Where(r => properties.All(p => p.Identifier.Text != r.Navigation)))
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = ORMEnum.EFCore,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = entityName,
                Property = relationship.Navigation,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The configuration configures the navigation '{relationship.Navigation}', which the class "
                    + $"'{entityName}' does not declare; the relationship has no navigation to stand on and is dropped (decision 132).",
            });
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
    /// Writes the relationship the fluent configuration states for a navigation, now that the
    /// class names the navigation's type (decision 132) - before the annotations on the same
    /// navigation, which are then compared with it rather than read on their own: the
    /// configuration outranks them in EF Core. Seen from the configured navigation the
    /// relationship is what HasOne or HasMany and WithOne or WithMany say; seen from the far
    /// end that WithOne or WithMany named, it is the mirror image. The side holding the key is
    /// the dependent: the many side, or for a one-to-one the entity HasForeignKey&lt;T&gt; names.
    /// Without HasForeignKey the columns of a many-to-one are EF Core's convention, derived
    /// once the target's key is known, exactly as for an unconfigured navigation.
    /// </summary>
    private void ApplyConfiguredRelationship(
        FluentRelationship relationship,
        bool mirrored,
        string entityName,
        string navigation,
        string target,
        List<string>? annotatedForeignKey,
        string? annotatedInverse)
    {
        var cardinality = !mirrored
            ? relationship.Cardinality
            : relationship.Cardinality switch
            {
                Cardinality.ManyToOne => Cardinality.OneToMany,
                Cardinality.OneToMany => Cardinality.ManyToOne,
                var same => same,
            };

        RelationRole? role = cardinality switch
        {
            Cardinality.ManyToOne => RelationRole.Owning,
            Cardinality.OneToOne => relationship.Dependent == entityName ? RelationRole.Owning : RelationRole.Inverse,
            _ => null,
        };

        var inverse = mirrored ? relationship.Navigation : relationship.Inverse;

        entityBuilder.AddForeignKey(
            cardinality, navigation, target, role, relationship.ForeignKey,
            inverseNavigation: inverse,
            conventionalColumns: cardinality == Cardinality.ManyToOne && relationship.ForeignKey is null
                ? (owner, targetMap) => ConventionForeignKeyProperties(owner, targetMap, navigation)
                : null);

        if (annotatedForeignKey is not null || annotatedInverse is not null)
        {
            entityBuilder.AddForeignKey(
                cardinality, navigation, target, role, annotatedForeignKey, inverseNavigation: annotatedInverse);
        }
    }

    /// <summary>A written type name without its namespace, as the configuration names entities.</summary>
    private static string SimpleName(string typeName)
    {
        var trimmed = typeName.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot < 0 ? trimmed : trimmed[(lastDot + 1)..];
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
    /// Whether the written element type of a collection makes it EF Core's primitive
    /// collection rather than a navigation: a scalar of the language type model, or a C#
    /// keyword type outside it (uint, ulong, decision 071) - a keyword can never name an
    /// entity. Any other name may be an entity in another unit and stays a navigation
    /// candidate, an enum among them, since the parser cannot tell the two apart.
    /// </summary>
    private static bool IsPrimitiveElement(string elementText)
    {
        var bare = elementText.TrimEnd('?').Trim();

        return IsScalarTypeName(bare)
            || SyntaxFacts.IsPredefinedType(SyntaxFacts.GetKeywordKind(bare));
    }

    /// <summary>
    /// The record of an annotation that belongs to a relationship, found on a property that
    /// founds none - a scalar or a collection of scalars. The claim has no relation to sit on
    /// and therefore no place in the model.
    /// </summary>
    private static ConversionRecord RelationshipAnnotationDropped(string entity, string property, string annotation) => new()
    {
        Kind = ConversionRecordKind.Loss,
        Framework = ORMEnum.EFCore,
        Artifact = ConversionContentType.CSharpEntity,
        Entity = entity,
        Property = property,
        Reason = $"The annotation {annotation} belongs to a relationship and the property it sits on is not a "
            + "navigation; the claim was dropped.",
    };

    /// <summary>
    /// What EF Core's convention makes of a collection navigation once the far entity is
    /// known: collection navigations on both sides pointing at each other are a many-to-many
    /// over an implicit junction table (EF Core 5 and later), a collection whose far side
    /// answers with a reference or with nothing at all is a one-to-many. The question cannot
    /// be answered while the far class is still unparsed, which is why the claim waits
    /// (decision 067); read as two one-to-many relations it used to claim a foreign key on
    /// both sides at once.
    ///
    /// [InverseProperty] settles the pairing itself - that is what the annotation is for -
    /// and the far end it names decides the shape. Without it the pairing is by convention
    /// and EF Core only pairs where a single candidate stands on each side; where several
    /// do, EF Core builds no model at all, so no shape is derived, the claim is dropped and
    /// the state is recorded. The collection then carries no relation, which is exactly what
    /// leaves the catalog free to supply one (decision 015).
    ///
    /// A far side outside the conversion is not ambiguity but absence: one artifact at a
    /// time is the ordinary unit of conversion, so the far class is usually just in another
    /// file. EF Core reads the collection as its one-to-many either way, and that is what
    /// the claim becomes; the resolution phase records the unresolved target.
    /// </summary>
    private Cardinality? CollectionCardinality(
        EntityMap owner, EntityMap? target, string navigationName, string? inverseNavigation)
    {
        if (target is null)
        {
            return Cardinality.OneToMany;
        }

        if (inverseNavigation is not null)
        {
            return NavigationsTowards(target, owner.Entity.Name)
                .FirstOrDefault(p => p.Name.Equals(inverseNavigation, StringComparison.Ordinal))
                is { Type.Category: LangTypeCategory.Collection }
                ? Cardinality.ManyToMany
                : Cardinality.OneToMany;
        }

        var farSide = NavigationsTowards(target, owner.Entity.Name);

        if (farSide.Count == 0)
        {
            // Nothing answers back: a unidirectional one-to-many whose foreign key EF Core
            // puts on the far entity as a shadow property.
            return Cardinality.OneToMany;
        }

        if (farSide.Count > 1 || NavigationsTowards(owner, target.Entity.Name).Count > 1)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = ORMEnum.EFCore,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = owner.Entity.Name,
                Property = navigationName,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"Several navigations pair '{owner.Entity.Name}' with '{target.Entity.Name}', so EF Core's "
                    + "convention cannot tell which of them belongs to this collection - it builds no model from "
                    + "such a source either, and [InverseProperty] is what would name the far end. No relation is "
                    + "derived for the collection; the database catalog may still supply one (decision 015).",
            });

            return null;
        }

        // A collection answering a collection is EF Core's implicit junction table; the
        // table itself is a fact of the schema, so it stays for the catalog to name
        // (decision 005) and the junction entity is synthesized once it does.
        return farSide[0].Type is { Category: LangTypeCategory.Collection }
            ? Cardinality.ManyToMany
            : Cardinality.OneToMany;
    }

    /// <summary>
    /// The properties of an entity whose written type names the given entity - a reference
    /// navigation or a collection of one - which is the set EF Core looks through for the far
    /// end of a navigation. A property the source took out of the model with [NotMapped]
    /// founds no relationship in EF Core (decision 072) and is not among them. The element
    /// may still be an unresolved name: the pairing runs before the resolution phase, which
    /// is the whole point of it running after parsing.
    /// </summary>
    private static List<Property> NavigationsTowards(EntityMap entity, string entityName)
        => entity.Entity.Properties
            .Where(p => TypeNames(p.Type, entityName)
                && entity.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == p.Name)?.IsTransient != true)
            .ToList();

    private static bool TypeNames(LangType? type, string entityName) => type switch
    {
        { Category: LangTypeCategory.Reference } => type.TargetEntity == entityName,
        { Category: LangTypeCategory.Unknown } => type.SourceName == entityName,
        { Category: LangTypeCategory.Collection } => TypeNames(type.ElementType, entityName),
        _ => false,
    };

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
    /// A class carrying [Keyless] never gets here: the convention may only speak where the
    /// source is silent, and there the source spoke (decision 063).
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
    /// A boolean written out as a literal; null where the argument is anything else, which
    /// claims nothing rather than claiming the default.
    /// </summary>
    private static bool? GetBool(ExpressionSyntax? expression)
    {
        return expression is LiteralExpressionSyntax lit
            && lit.Token.Value is bool b ? b : null;
    }

    /// <summary>
    /// The maximum length both [MaxLength] and [StringLength] state. The first reading
    /// stands and a second, differing one is a conflict record (decision 017): the two
    /// annotations may sit on one property, and the fact dictionary carries a single key,
    /// so without this the later attribute would overwrite the earlier one in silence.
    /// </summary>
    private void HandleLength(
        int length, string attributeName, Dictionary<string, string> dbProps, string entityName, string propertyName)
    {
        var stated = length.ToString();

        if (dbProps.TryAdd("Length", stated) || string.Equals(dbProps["Length"], stated, StringComparison.Ordinal))
        {
            return;
        }

        entityBuilder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Conflict,
            Framework = ORMEnum.EFCore,
            Artifact = ConversionContentType.CSharpEntity,
            Entity = entityName,
            Property = propertyName,
            Category = MappingFactCategory.Length,
            Reason = $"The property states length {dbProps["Length"]} and [{attributeName}] states {stated}; "
                + "a fact read earlier is never overwritten by a later one (decision 017), so the first value is kept.",
        });
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
