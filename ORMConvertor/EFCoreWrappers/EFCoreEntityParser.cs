using AbstractWrappers;
using Common.Convertors;
using EFCoreWrappers.Convertors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Parses a C# class definition (optionally within a namespace) from the provided source code string.
/// </summary>
public class EFCoreEntityParser(AbstractEntityBuilder entityBuilder) : IParser
{
    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.CSharpEntity;
    }

    /// <summary>
    /// Parses a C# class definition (optionally within a namespace) from the provided source code string.
    /// </summary>
    /// <param name="source">C# source code containing a single class, optionally wrapped in a namespace.</param>
    public void Parse(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        var classes = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToList();

        foreach (var cls in classes)
        {
            entityBuilder.BeginEntity();

            var ns = GetNamespace(cls);
            if (!string.IsNullOrEmpty(ns))
            {
                entityBuilder.AddNamespace(ns);
            }

            ParseClassAttributes(cls);
            ParseClassHeader(cls);
            ParseProperties(cls);
        }
    }

    private static string? GetNamespace(ClassDeclarationSyntax classDeclaration)
    {
        var namespaces = classDeclaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(ns => ns.Name.ToString())
            .Reverse()
            .ToList();

        return namespaces.Count == 0 ? null : string.Join(".", namespaces);
    }

    /// <summary>
    /// Parses the class header, including modifiers and class name.
    /// </summary>
    private void ParseClassHeader(ClassDeclarationSyntax classDeclaration)
    {
        var modifiers = string.Join(" ", classDeclaration.Modifiers.Select(m => m.Text));

        entityBuilder.AddClassHeader(
            modifiers,
            classDeclaration.Identifier.Text
        );
    }

    /// <summary>
    /// Parses class attributes, specifically looking for EF Core table and schema attributes.
    /// </summary>
    private void ParseClassAttributes(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var attr in classDeclaration.AttributeLists.SelectMany(l => l.Attributes))
        {
            var name = TrimAttribute(attr.Name.ToString());

            if (name.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                string? table = null;
                string? schema = null;

                foreach (var arg in attr.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
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
        }
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
                string? name = arg.Expression switch
                {
                    // nameof(CustomerID)
                    InvocationExpressionSyntax inv
                        when inv.Expression is IdentifierNameSyntax id
                          && id.Identifier.Text == "nameof"
                          && inv.ArgumentList.Arguments.Count == 1
                          && inv.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax propName
                        => propName.Identifier.Text,
                    // "CustomerID"
                    _ => GetString(arg.Expression),
                };

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
    private void ParseProperties(ClassDeclarationSyntax classDeclaration)
    {
        var classKeyNames = GetClassPrimaryKeyNames(classDeclaration);
        var keyPropertyNames = new List<string>();
        var scalarPropertyNames = new List<string>();

        // Collected per property because the strategy is decided once the whole key is known:
        // the type of the key property carries EF Core's convention, and the annotation, where
        // present, overrides it (decision 011).
        var propertyTypes = new Dictionary<string, string>();
        var generatedOptions = new Dictionary<string, string>();

        foreach (var prop in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            var name = prop.Identifier.Text;
            var accessTokens = prop.Modifiers
                .Where(m =>
                    m.IsKind(SyntaxKind.PublicKeyword) ||
                    m.IsKind(SyntaxKind.PrivateKeyword) ||
                    m.IsKind(SyntaxKind.InternalKeyword) ||
                    m.IsKind(SyntaxKind.ProtectedKeyword))
                .Select(t => t.Text)
                .ToList();
            var accessModifiers = string.Join(" ", accessTokens);

            var otherModifiers = prop.Modifiers
                        .Where(m => !accessTokens.Contains(m.Text))
                        .Select(m => m.Text)
                        .ToList();

            bool hasGetter = prop.ExpressionBody != null
                    || prop.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;

            bool hasSetter = prop.AccessorList?.Accessors
                        .Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) == true;

            bool nullableSyntax = prop.Type is NullableTypeSyntax;
            string type = ((prop.Type as NullableTypeSyntax)?.ElementType ?? prop.Type).ToString();
            
            var defaultValue = prop.Initializer?.Value?.ToString();

            var dbProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool isPrimaryKey = false;
            bool requiredAttr = false;
            string? generatedOption = null;
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
                        HandleColumn(attribute, dbProps);
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
                }
            }

            bool isNullable = !requiredAttr && nullableSyntax;

            dbProps["Nullable"] = isNullable ? "true" :"false";

            entityBuilder.AddProperty(
                type, 
                name,
                accessModifier: accessModifiers,
                OtherModifiers: otherModifiers,
                hasGetter: hasGetter,
                hasSetter: hasSetter,
                defaultValue: defaultValue,
                isNullable: isNullable
            );

            if (dbProps.Count > 0)
            {
                entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
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
                // Only a scalar property can become the key EF Core derives by convention.
                scalarPropertyNames.Add(name);
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
        else if (FindConventionKey(classDeclaration.Identifier.Text, scalarPropertyNames) is { } conventionKey)
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
    /// the parser wherever its absence would change the meaning (decision 008), and it does here
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
    /// only the parser knows which framework the input came from - see decision 008.
    ///
    /// The comparison is case-insensitive, so CustomerID counts as CustomerId. That matches
    /// what EF Core does, but the documentation page on keys does not spell it out, so the
    /// claim rests on observed behaviour rather than on a quotable sentence.
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
    /// </summary>
    private static bool IsCollection(TypeSyntax type, out string target)
    {
        target = string.Empty;

        if (type is GenericNameSyntax g &&
            (g.Identifier.ValueText is "List" or "ICollection" or "IEnumerable" or "HashSet") &&
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
    /// Handles the "Column" attribute, extracting properties like ColumnName and TypeName.
    /// </summary>
    private static void HandleColumn(AttributeSyntax attribute, Dictionary<string, string> dbProps)
    {
        foreach (var arg in attribute.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>())
        {
            var named = arg.NameEquals?.Name.Identifier.ValueText;

            if (named is null)
            {
                dbProps["ColumnName"] = GetString(arg.Expression) ?? string.Empty;
            }
            else if (named.Equals("TypeName", StringComparison.OrdinalIgnoreCase))
            {
                dbProps["Type"] = ((int)DatabaseTypeConvertor.FromEfCore(GetString(arg.Expression))).ToString();
            }
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
