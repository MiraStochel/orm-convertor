using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Common.Reading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using TransactSql;

namespace DapperWrappers;

/// <summary>
/// Reads the one mapping Dapper has: a column renamed in the text of a call that maps its rows
/// onto an entity of the conversion (decision 131). Dapper puts each column of the result into
/// the member of the same name, so <c>connection.Query&lt;Author&gt;("SELECT AuthorId AS Id
/// FROM Authors")</c> states that the property Id of Author holds the column AuthorId - the
/// source's own statement, which the catalog completes and compares but never overwrites.
///
/// An auxiliary mapping artifact, level 1b of decision 017: the class is level 1a and states no
/// column, the text of the call is where Dapper's mapping lives. It reads on the entity pass,
/// after every class of the conversion and before the catalog, so the completion pairs by the
/// stated column; the query pass reads the same unit for its queries (decision 081).
///
/// What the wrapper knows is which call maps onto which type; what the text says about its
/// columns is read by <see cref="SqlRenamedColumns"/>. Everything else is decision 131's
/// conditions: the type is an entity of the conversion, the column belongs to its table, and
/// the name is one of its scalar properties as Dapper matches them. A column renamed onto a
/// property an earlier call mapped to another column is the conflict of decision 017, kept
/// first; a type map registered in code is a loss. Reading the text says nothing else,
/// because the query pass says it.
/// </summary>
public class DapperColumnAliasParser(
    AbstractEntityBuilder entityBuilder,
    SourceSqlDialect? declaredSourceDialect = null) : IEntityParser
{
    /// <summary>The limits this parser reads its input under (decision 092).</summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType) => contentType == ConversionContentType.CSharp;

    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        var read = new List<EntityMap>();

        // One reading of the unit for both halves, under the nesting cap (decision 092); a text
        // too deep or one that does not read is the entity pass's to report, and it has.
        if (NestingDepthGuard.FirstBeyond(DapperSqlQueryParser.Tracked(source), Limits) is not null
            || CSharpSource.Read(source) is not { Tree: not null } unit)
        {
            return read;
        }

        foreach (var call in DapperSqlQueryParser.RowMappings(unit))
        {
            if (EntityOf(call.Row) is not { } entityMap)
            {
                continue;
            }

            // The text as the query pass reads it, refusals left to that pass to report.
            var text = DapperCollectionParameters.PeelOff(call.Sql, out _);
            if (SqlText.Selects(text, (_, _, _) => { }, declaredSourceDialect, Limits) is not [var select])
            {
                continue;
            }

            var tables = entityMap.Table is { } stated ? [stated] : EntityTableNaming.TableCandidatesFor(entityMap.Entity.Name);

            foreach (var renamed in SqlRenamedColumns.Of(select))
            {
                if (!tables.Contains(renamed.Table, StringComparer.OrdinalIgnoreCase)
                    || PropertyOf(entityMap, renamed.Name) is not { } property)
                {
                    continue;
                }

                entityBuilder.EntityMap = entityMap;

                using (entityBuilder.At(call.Range))
                {
                    entityBuilder.SetPropertyDatabaseMapping(property.Name, new() { ["ColumnName"] = renamed.Column });
                }

                if (!read.Contains(entityMap))
                {
                    read.Add(entityMap);
                }
            }
        }

        ReportTypeMaps(unit.Tree);

        return read;
    }

    /// <summary>
    /// Dapper's other way to map a column onto a member of another name (decision 131): a type
    /// map registered in code that runs apart from the queries, or the process-wide switch that
    /// lets a column match a member with its underscores dropped. Neither is a statement the
    /// reading can take - the map is a function, the switch a setting of every type at once -,
    /// so each occurrence is a loss that says what it drops (decision 048).
    /// </summary>
    private void ReportTypeMaps(SyntaxTree tree)
    {
        foreach (var node in tree.GetCompilationUnitRoot().DescendantNodes())
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "SetTypeMap" } } invocation)
            {
                var type = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf
                    ? TypeName(typeOf.Type)
                    : null;

                Report(
                    type,
                    $"The type map registered by SqlMapper.SetTypeMap{(type is null ? string.Empty : $" for '{type}'")} maps columns to "
                        + "members in code that runs apart from the queries it serves; it is not read, so the column mapping it states "
                        + "is dropped and the properties are paired with columns by their names.");
            }
            else if (node is AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax { Name.Identifier.Text: "MatchNamesWithUnderscores" } })
            {
                Report(
                    null,
                    "DefaultTypeMap.MatchNamesWithUnderscores is a setting of the process that lets every type match a column with its "
                        + "underscores dropped; it is not read, so the column mapping it implies is dropped and the properties are paired "
                        + "with columns by their names as written.");
            }
        }

        void Report(string? entity, string reason) => entityBuilder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = entityBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.CSharp,
            Entity = entity,
            Category = MappingFactCategory.ColumnName,
            Reason = reason,
        });
    }

    private static string? TypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        QualifiedNameSyntax { Right: IdentifierNameSyntax right } => right.Identifier.Text,
        AliasQualifiedNameSyntax { Name: IdentifierNameSyntax right } => right.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// The entity a type argument names: by its simple name, and by the namespace where the
    /// argument states one. Two entities of the name and no namespace to tell them apart name
    /// neither; a type that is no entity of the conversion - int, dynamic, a tuple - names none.
    /// </summary>
    private EntityMap? EntityOf(TypeSyntax row)
    {
        var name = TypeName(row);
        var qualifier = row is QualifiedNameSyntax qualified ? Unaliased(qualified.Left) : null;

        if (name is null)
        {
            return null;
        }

        var named = entityBuilder.EntityMaps.Where(em => em.Entity.Name == name).ToList();
        var placed = qualifier is null
            ? named
            : named.Where(em => em.Entity.Namespace is null || em.Entity.Namespace == qualifier).ToList();

        return placed.Count == 1 ? placed[0] : null;
    }

    private static string Unaliased(NameSyntax name)
        => name is AliasQualifiedNameSyntax alias ? alias.Name.ToString() : name.ToString().Replace("global::", string.Empty);

    /// <summary>
    /// The property Dapper puts a column of the given name into: the same name first, then the
    /// name regardless of case (DefaultTypeMap.GetMember, Dapper 2.1.79). A navigation takes no
    /// column of a row, so only a property of a scalar or an unknown type is one.
    /// </summary>
    private static Property? PropertyOf(EntityMap entityMap, string name)
    {
        var property = entityMap.Entity.Properties.FirstOrDefault(p => p.Name == name)
            ?? entityMap.Entity.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        return property?.Type?.Category is LangTypeCategory.Reference or LangTypeCategory.Collection ? null : property;
    }
}
