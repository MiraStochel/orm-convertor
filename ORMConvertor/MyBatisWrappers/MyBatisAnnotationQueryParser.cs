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
/// side of the line decision 040 draws. @Insert, @Update and @Delete are refused by name as
/// well, as their elements are in the XML form: the interface hands them over as much as it
/// hands over a select (decision 109). @Options and @Lang on a select are its options,
/// answered by the rule the XML form's attributes share.
/// </summary>
public sealed class MyBatisAnnotationQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    MyBatisReadingContext context,
    SourceSqlDialect? declaredSourceDialect = null)
    : MyBatisQueryParser(queryBuilders, context)
{
    protected override ConversionContentType Artifact => ConversionContentType.JavaQuery;

    public override bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.Java;

    /// <summary>
    /// The statements of the interfaces of a Java unit, which is a whole file or a fragment
    /// of one (decision 111): a file holding the domain class alone has none, and yields no
    /// query. A text the reader cannot read yields nothing either, and says nothing: the
    /// entity pass, which reads the same text first, has reported it.
    /// </summary>
    public override IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null)
    {
        JavaCompilationUnit unit;
        try
        {
            unit = JavaClassReader.Read(source, Limits);
        }
        catch (Exception exception) when (exception is JavaSyntaxError or JavaInputTooDeep)
        {
            return [];
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

                if (method.Annotations.FirstOrDefault(a => a.SimpleName is "Insert" or "Update" or "Delete") is { } writing)
                {
                    filled.Add(RefuseWritingStatement(method.Name, $"@{writing.SimpleName}"));
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

        foreach (var dropped in method.Annotations.Where(a => a.SimpleName is "ResultMap" or "ResultType" or "MapKey"))
        {
            Report(builder, ConversionRecordKind.Loss,
                $"The method carries @{dropped.SimpleName}, which says how the result is mapped; the query representation does not "
                + "carry it, so it was dropped and the result type is derived from the table.");
        }

        if (!ReadOptions(builder, Options(method)))
        {
            return builder;
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
            text.Parameters,
            Limits)
            .Read(sql);

        return builder;
    }

    /// <summary>
    /// The options a method states for its select: every element of @Options, and the
    /// driver @Lang names. A value is normalized to what the XML form would write -
    /// StatementType.CALLABLE to CALLABLE, RawLanguageDriver.class to RawLanguageDriver -,
    /// so that one rule answers both forms.
    /// </summary>
    private static IEnumerable<StatementOption> Options(JavaMethod method)
    {
        foreach (var annotation in method.Annotations)
        {
            if (annotation.SimpleName == "Options")
            {
                foreach (var argument in annotation.Arguments)
                {
                    var name = argument.Name ?? "value";
                    yield return new StatementOption(name, Normalized(argument.Value), $"@Options({name} = {argument.Value.Text})");
                }
            }
            else if (annotation.SimpleName == "Lang" && annotation["value"] is { } driver)
            {
                yield return new StatementOption("lang", Normalized(driver), $"@Lang({driver.Text}.class)");
            }
        }
    }

    private static string Normalized(JavaAnnotationValue value) => value.Kind switch
    {
        JavaAnnotationValueKind.Name or JavaAnnotationValueKind.ClassLiteral => value.SimpleName,
        _ => value.Text,
    };

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
