using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using TransactSql;

namespace MyBatisWrappers;

/// <summary>
/// Reads the statements a MyBatis mapper interface carries in annotations (decision 084).
/// Requirement F8 names both forms of the input in so many words - "XML or annotated
/// mapping" - so both are read, while only the XML form is written; the asymmetry has its
/// precedent in decision 013, which made hbm.xml to annotations a one-way road for the same
/// reason: there are more source forms than forms worth emitting.
///
/// @SelectProvider and its siblings are refused by name. SQL assembled by Java code is a
/// program rather than an artifact, and running it to find out what it says is on the far
/// side of the line decision 040 draws.
/// </summary>
public sealed class MyBatisAnnotationQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    MyBatisReadingContext context,
    SourceSqlDialect? declaredSourceDialect = null)
    : MyBatisQueryParser(queryBuilders, context)
{
    protected override ConversionContentType Artifact => ConversionContentType.JavaQuery;

    public override bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.JavaQuery;

    public override IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null)
    {
        JavaCompilationUnit unit;
        try
        {
            unit = JavaClassReader.Read(source);
        }
        catch (JavaSyntaxError error)
        {
            var refused = Named(null);
            Report(refused, ConversionRecordKind.Failure,
                $"The Java source could not be read at line {error.Line}, column {error.Column}: {error.Message}.");
            return [refused];
        }

        var filled = new List<AbstractQueryBuilder>();

        foreach (var mapper in unit.Interfaces)
        {
            var namespaceName = string.IsNullOrEmpty(unit.Package) ? mapper.Name : $"{unit.Package}.{mapper.Name}";

            foreach (var method in mapper.Methods)
            {
                if (method.Annotations.FirstOrDefault(a => a.SimpleName.EndsWith("Provider", StringComparison.Ordinal)) is { } provider)
                {
                    filled.Add(RefuseProvider(method, provider));
                    continue;
                }

                if (method.Annotations.FirstOrDefault(a => a.SimpleName == "Select") is { } select)
                {
                    filled.Add(ReadSelect(mapper, method, select, namespaceName, entityMaps));
                }
            }
        }

        return filled;
    }

    private AbstractQueryBuilder ReadSelect(
        JavaInterface mapper,
        JavaMethod method,
        JavaAnnotation select,
        string namespaceName,
        IReadOnlyList<EntityMap>? entityMaps)
    {
        var builder = Named(method.Name);

        if (RefuseIfDeclaredTwice(builder, namespaceName, method.Name))
        {
            return builder;
        }

        foreach (var dropped in method.Annotations.Where(a => a.SimpleName is "ResultMap" or "ResultType"))
        {
            Report(builder, ConversionRecordKind.Loss,
                $"The method carries @{dropped.SimpleName}, which says how the result is mapped; the query representation does not "
                + "carry it, so it was dropped and the result type is derived from the table.");
        }

        // @Select takes one string or an array of them, which MyBatis joins with a space.
        var statement = string.Join(
            " ",
            (select["value"]?.AsList ?? [])
                .Where(v => v.Kind == JavaAnnotationValueKind.String)
                .Select(v => v.Text));

        if (string.IsNullOrWhiteSpace(statement))
        {
            Report(builder, ConversionRecordKind.Failure,
                $"The @Select on '{mapper.Name}.{method.Name}' carries no statement text; no artifact was generated.");
            return builder;
        }

        var text = new MyBatisStatementText(context, namespaceName, (kind, reason, feature) => Report(builder, kind, reason, feature));

        foreach (var (name, facts) in StatedParameters(namespaceName, method.Name, parameterType: null, entityMaps))
        {
            text.Parameters[name] = facts;
        }

        if (text.FromAnnotationText(statement) is not { } sql)
        {
            return builder;
        }

        new SqlQueryReader(
            builder,
            (kind, reason, feature) => ReportSql(builder, kind, reason, feature),
            declaredSourceDialect,
            text.Parameters)
            .Read(sql);

        return builder;
    }

    private AbstractQueryBuilder RefuseProvider(JavaMethod method, JavaAnnotation provider)
    {
        var builder = Named(method.Name);

        Report(builder, ConversionRecordKind.Failure,
            $"The method carries @{provider.SimpleName}, whose SQL is assembled by Java code rather than written in the mapper. That is "
            + "a program and not an artifact, and running it to find out what it says is outside the boundary of what the tool reads "
            + "(decision 040); no artifact was generated.");

        return builder;
    }
}
