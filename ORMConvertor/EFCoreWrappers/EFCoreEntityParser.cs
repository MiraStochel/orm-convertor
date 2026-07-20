using AbstractWrappers;
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

            if (isPrimaryKey)
            {
                keyPropertyNames.Add(name);
            }

            if (IsCollection(prop.Type, out var target))
            {
                entityBuilder.AddForeignKey(Cardinality.OneToMany, name, target);
            }
        }

        // The key is defined by a single call for the whole entity (design doc 001, §3.3).
        // The class-level [PrimaryKey(...)] takes precedence - it defines the explicit part order.
        if (classKeyNames.Count > 0)
        {
            entityBuilder.AddPrimaryKey(
                classKeyNames.Select((n, i) => (n, i + 1, PrimaryKeyStrategy.None)).ToList());
        }
        else if (keyPropertyNames.Count > 0)
        {
            entityBuilder.AddPrimaryKey(
                keyPropertyNames.Select((n, i) => (n, i + 1, PrimaryKeyStrategy.Identity)).ToList());
        }
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
