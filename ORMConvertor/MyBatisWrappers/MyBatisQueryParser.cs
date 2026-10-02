using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using TransactSql;

namespace MyBatisWrappers;

/// <summary>
/// What the two query parsers of MyBatis share (decision 084): making a builder that
/// carries the statement's own name, recognizing the one id declared in both forms, and
/// gathering the scalars the source stated about the statement's parameters.
///
/// The statement's text is not read here and never will be: SQL is a language three
/// frameworks write, so it is read by the shared <see cref="SqlQueryReader"/>
/// (decision 082). What belongs to MyBatis is only getting hold of plain T-SQL, which is
/// <see cref="MyBatisStatementText"/>.
/// </summary>
public abstract class MyBatisQueryParser(Func<AbstractQueryBuilder> queryBuilders, MyBatisReadingContext context)
    : IQueryParser
{
    protected readonly MyBatisReadingContext context = context;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public abstract bool CanParse(ConversionContentType contentType);

    public abstract IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null);

    /// <summary>The artifact the records of this parser name, which its subclass states.</summary>
    protected abstract ConversionContentType Artifact { get; }

    /// <summary>
    /// A builder carrying the statement's id as the query's name (decision 081): the
    /// generated method is named after it, and so is every record of this query, which is
    /// the only thing telling two failed statements of one mapper apart.
    /// </summary>
    protected AbstractQueryBuilder Named(string? id)
    {
        var builder = queryBuilders();
        builder.QueryName = id;
        return builder;
    }

    /// <summary>
    /// Whether the same &lt;namespace, id&gt; was stated in an annotation and in XML. MyBatis
    /// 3.5.19 then refuses to build the SqlSessionFactory at all, so there is no precedence
    /// to apply and no conflict to resolve: it is a Failure, in the shape decision 063 gave
    /// one, naming both places (decision 068).
    /// </summary>
    protected bool RefuseIfDeclaredTwice(AbstractQueryBuilder builder, string namespaceName, string id)
    {
        if (!context.IsDeclaredTwice(namespaceName, id))
        {
            return false;
        }

        Report(
            builder,
            ConversionRecordKind.Failure,
            $"The statement '{namespaceName}.{id}' is declared both by an annotation on the mapper interface and by an element of the "
            + "XML mapper. MyBatis documents no precedence between the two forms - it refuses to build the SqlSessionFactory from such "
            + "a project - so there is nothing to decide between; no artifact was generated.");

        return true;
    }

    /// <summary>
    /// A statement that changes rows - &lt;insert&gt;, &lt;update&gt; and &lt;delete&gt; in the
    /// document, @Insert, @Update and @Delete on the interface. It is outside what the tool
    /// translates for any framework, the same refusal a Dapper unit carrying an INSERT meets in
    /// the shared reader, but the unit hands it over all the same, so it is named and refused
    /// rather than dropped in silence (decisions 048 and 109). <paramref name="spelled"/> is
    /// the statement as the source wrote it.
    /// </summary>
    protected AbstractQueryBuilder RefuseWritingStatement(string? id, string spelled)
    {
        var builder = Named(id);

        Report(builder, ConversionRecordKind.Failure,
            $"The mapper declares {spelled}, a statement that changes rows rather than returning them; the tool translates "
            + "entities, mappings and queries, so no artifact was generated for it.");

        return builder;
    }

    /// <summary>One option a statement states about itself, as the source spelled it and with its value normalized.</summary>
    protected readonly record struct StatementOption(string Name, string Value, string Spelled);

    /// <summary>
    /// The value an option takes when nobody states it, for a select. An option stated with
    /// it states nothing the absent one would not, so it is no loss; the annotated form
    /// spells some of them its own way (FlushCachePolicy.DEFAULT, -1).
    /// </summary>
    private static readonly Dictionary<string, string[]> Defaults = new(StringComparer.Ordinal)
    {
        ["statementType"] = ["PREPARED"],
        ["useCache"] = ["true"],
        ["flushCache"] = ["false", "DEFAULT"],
        ["resultSetType"] = ["DEFAULT"],
        ["resultOrdered"] = ["false"],
        ["affectData"] = ["false"],
        ["fetchSize"] = ["-1"],
        ["timeout"] = ["-1"],
        ["useGeneratedKeys"] = ["false"],
        ["keyProperty"] = [""],
        ["keyColumn"] = [""],
        ["resultSets"] = [""],
        ["databaseId"] = [""],
    };

    /// <summary>
    /// The language drivers that read a statement the way the tool does: XML, MyBatis's
    /// default, and raw, which is XML without the dynamic tags. Named by alias or by class.
    /// </summary>
    private static readonly HashSet<string> XmlLanguageDrivers = new(StringComparer.OrdinalIgnoreCase)
    {
        "xml", "raw", "XMLLanguageDriver", "RawLanguageDriver",
    };

    /// <summary>
    /// The options a select states beside the four attributes the reading takes - the other
    /// attributes of &lt;select&gt;, and @Options and @Lang on a mapper method. Most say how
    /// MyBatis executes the statement rather than which rows it reads (fetchSize, timeout,
    /// useCache, flushCache, resultSetType, resultOrdered, resultSets, affectData,
    /// parameterMap, statementType STATEMENT): the representation has no place for them and
    /// each is a loss (decision 048), the answer the reading of a JPA or NHibernate query
    /// object gives its setters.
    ///
    /// Three get an answer of their own. statementType CALLABLE has MyBatis call the
    /// statement as a stored procedure: the text is read for what it states, and a call
    /// written in it - EXEC, or JDBC's {call …} - is refused by the reading as no query, so
    /// what is lost is the statement type alone. databaseId has MyBatis load the statement
    /// only under the database the configuration names so, which the tool cannot know
    /// (decision 040); the text is read as SQL of the declared dialect whatever the name. And
    /// a language driver other than XML reads the text in a language of its own - a
    /// template whose directives may sit in what T-SQL takes for a comment -, so the text
    /// the tool would read could be another statement than the one MyBatis runs: that is a
    /// refusal (decision 053), and the method returns false.
    /// </summary>
    protected bool ReadOptions(AbstractQueryBuilder builder, IEnumerable<StatementOption> options)
    {
        var read = true;

        foreach (var option in options)
        {
            if (Defaults.TryGetValue(option.Name, out var defaults)
                && defaults.Contains(option.Value, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (option.Name)
            {
                case "lang" when XmlLanguageDrivers.Contains(option.Value):
                    break;

                case "lang":
                    Report(builder, ConversionRecordKind.Failure,
                        $"The statement states {option.Spelled}, a language driver that reads its text in a language of its own "
                        + "rather than in MyBatis's XML, which is the one the tool reads; read as XML, the text could be another "
                        + "statement than the one MyBatis runs, so no artifact was generated.");
                    read = false;
                    break;

                case "statementType" when option.Value.Equals("CALLABLE", StringComparison.OrdinalIgnoreCase):
                    Report(builder, ConversionRecordKind.Loss,
                        $"The statement states {option.Spelled}, which has MyBatis call it as a stored procedure; the text is read "
                        + "as the query it states - a call itself, EXEC or JDBC's {call …}, is refused by the reading - and the "
                        + "statement type was dropped.");
                    break;

                case "databaseId":
                    Report(builder, ConversionRecordKind.Loss,
                        $"The statement states {option.Spelled}, which has MyBatis load it only under the database the "
                        + "configuration names so; the tool reads it as SQL of the declared dialect whatever the name, and the "
                        + "condition was dropped.");
                    break;

                default:
                    Report(builder, ConversionRecordKind.Loss,
                        $"The statement states {option.Spelled}, which says how MyBatis executes it rather than which rows it "
                        + "reads; the query representation has no place for it and it was dropped.");
                    break;
            }
        }

        return read;
    }

    /// <summary>
    /// The scalars the source stated about the statement's parameters, in the order
    /// decision 084 gives them: the signature of the mapper method first, because that is
    /// where a MyBatis parameter's type lives at all, and a parameterType naming an entity of
    /// the conversion after it. The third source, a javaType inside #{}, is read with the
    /// text and overwrites both, being the most local claim.
    ///
    /// Only the scalar travels from here. Whether a parameter binds a list is stated by a
    /// &lt;foreach&gt; and by nothing else: a method declaring a Collection whose statement
    /// writes #{ids} binds the list as one value, which is what MyBatis does with it.
    /// </summary>
    protected Dictionary<string, SqlParameterFacts> StatedParameters(
        string namespaceName,
        string id,
        string? parameterType,
        IReadOnlyList<EntityMap>? entityMaps)
    {
        var stated = new Dictionary<string, SqlParameterFacts>(StringComparer.Ordinal);

        if (context.MethodOf(namespaceName, id) is { } signature)
        {
            foreach (var parameter in signature.Parameters.Where(p => p.Name is not null))
            {
                if (ScalarOf(parameter.Type) is { } scalar)
                {
                    stated[parameter.Name!] = new SqlParameterFacts(scalar);
                }
            }
        }

        if (parameterType is not null && entityMaps is not null)
        {
            var simple = MyBatisTypeNames.SimpleName(parameterType);
            var entityMap = entityMaps.FirstOrDefault(em => string.Equals(em.Entity.Name, simple, StringComparison.Ordinal));

            foreach (var property in entityMap?.Entity.Properties ?? [])
            {
                if (property.Type is { Category: LangTypeCategory.Scalar } type && !stated.ContainsKey(property.Name))
                {
                    stated[property.Name] = new SqlParameterFacts(type.ScalarType);
                }
            }
        }

        return stated;
    }

    /// <summary>
    /// The scalar a declared Java type states - of the value itself, or of the elements
    /// where the declaration is a collection, which is what a collection parameter's scalar
    /// means (decision 083).
    /// </summary>
    private static ScalarType? ScalarOf(string javaType)
    {
        var langType = JavaTypeConvertor.FromString(javaType);

        return langType.Category switch
        {
            LangTypeCategory.Scalar => langType.ScalarType,
            LangTypeCategory.Collection when langType.ElementType is { Category: LangTypeCategory.Scalar } element
                => element.ScalarType,
            _ => null,
        };
    }

    /// <summary>The channel the tag-level reading reports over; the artifact is the unit's own language.</summary>
    protected void Report(AbstractQueryBuilder builder, ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = builder.Descriptor.Framework,
            Artifact = Artifact,
            Feature = feature,
            Reason = reason,
        });

    /// <summary>
    /// The channel the shared T-SQL reading reports over. The record names the language that
    /// was read rather than the unit that carried it, which is the shape every query parser
    /// of the solution uses.
    /// </summary>
    protected static void ReportSql(AbstractQueryBuilder builder, ConversionRecordKind kind, string reason, QueryFeature? feature)
        => builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = builder.Descriptor.Framework,
            Artifact = ConversionContentType.SqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
