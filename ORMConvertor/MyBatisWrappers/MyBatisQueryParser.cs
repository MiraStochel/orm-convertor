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
