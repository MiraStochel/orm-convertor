using AbstractWrappers;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using MyBatisWrappers;
using JakartaPersistence;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal class ParserFactory
{
    /// <summary>
    /// Parsers for one source framework, in the reading order that is a stated fact of
    /// the framework, not an accident of the list (decision 017). The order follows the
    /// precedence the framework documents for its own artifacts, strongest first; where
    /// it documents none, the default holds: the input text - the entity class, level
    /// 1a - parses before its auxiliary mapping artifacts - level 1b, for NHibernate
    /// the hbm.xml (decision 068). The fluent configuration parser stands first in the
    /// EF Core list, because EF Core puts the fluent API above annotations (decision 132); the
    /// orm.xml parser stands first in the Hibernate list for the same reason - Jakarta
    /// Persistence puts the descriptor above the annotations (decision 077). The
    /// orchestration runs each parser over all its units before the next one starts, so
    /// a fact of a higher level is already in place when a lower level arrives, and the
    /// builder can keep the first value and report a conflict without the model tracking
    /// the origin of a fact.
    ///
    /// Query parsers appear only when a factory of query builders is there to feed them;
    /// within one framework each query parser claims a different query language, so the
    /// caller can pick by content type instead of by list order (decision 025). A factory
    /// rather than one builder, because a unit may hold several queries and each gets its
    /// own (decision 081); a parser may not make one itself, since a builder belongs to
    /// the target framework and a parser to the source (S1).
    ///
    /// The dialect the source declared for its literal SQL (decision 088) travels the same
    /// way the query builder factory does: it is an input of the parsers' construction, and
    /// only those parsers that read literal SQL take it. It is deliberately not hung on the
    /// entity builder, although every parser already holds one and that would be the
    /// shortest path - a builder belongs to the target framework and a parser to the source,
    /// so a fact about the source travelling through a target object would invert that
    /// boundary (S1).
    /// </summary>
    /// <param name="limits">
    /// The limits the parsers read under (decision 092). Unlike the declared dialect it is no
    /// fact of the request but of the instance, so it is stated once here for every parser
    /// built rather than threaded through twenty constructors - and no wrapper can forget it.
    /// </param>
    public static List<IParser> Create(
        ORMEnum orm,
        AbstractEntityBuilder eb,
        Func<AbstractQueryBuilder>? qb,
        SourceSqlDialect? declaredSourceDialect = null,
        ParseLimits? limits = null)
    {
        var parsers = Build(orm, eb, qb, declaredSourceDialect);

        foreach (var parser in parsers)
        {
            parser.Limits = limits ?? ParseLimits.Default;
        }

        return parsers;
    }

    private static List<IParser> Build(
        ORMEnum orm,
        AbstractEntityBuilder eb,
        Func<AbstractQueryBuilder>? qb,
        SourceSqlDialect? declaredSourceDialect)
    {
        switch (orm)
        {
            // The class states no column, the text of a call that maps rows onto it does: the
            // alias parser is Dapper's auxiliary mapping artifact and reads after the class
            // (decisions 017 and 131), on the entity pass, so the catalog pairs by what it read.
            case ORMEnum.Dapper:
                return qb is null
                    ? [new DapperEntityParser(eb), new DapperColumnAliasParser(eb, declaredSourceDialect)]
                    : [new DapperEntityParser(eb), new DapperColumnAliasParser(eb, declaredSourceDialect), new DapperSqlQueryParser(qb, declaredSourceDialect)];

            // The entity parser stands before the XML mapping parser as a rule, not as a
            // coincidence: swapping the two would invert the source precedence (decision 017).
            // The hbm.xml is claimed twice over, by the mapping parser on the entity pass and
            // by the query parser on the query one: the document is a mapping and a query at
            // once and the pair of parsers is what says so (decision 081).
            case ORMEnum.NHibernate:
                return qb is null
                    ? [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb, declaredSourceDialect)]
                    : [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb, declaredSourceDialect), new NHibernateLinqQueryParser(qb, declaredSourceDialect), new NHibernateHqlQueryParser(qb), new NHibernateXmlQueryParser(qb, declaredSourceDialect)];

            // The fluent configuration before the class: EF Core puts the fluent API above the
            // annotations (decisions 068 and 132), and the conventions the class parser
            // materializes stand below both. What the configuration states but can write only
            // once the class is read - a relationship, the table of a DbSet - waits in a
            // context the two parsers share. The query pass reads through the same context,
            // found through the entity builder as MyBatis's is (decision 185): the entity a
            // DbSet of a given name exposes is what the query parser needs to read a root
            // written by the set's name, and the entity pass is the one that read the context.
            case ORMEnum.EFCore:
            {
                var context = EFCoreReadingContext.For(eb);
                return qb is null
                    ? [new EFCoreFluentConfigurationParser(eb, context, declaredSourceDialect), new EFCoreEntityParser(eb, declaredSourceDialect, context)]
                    : [new EFCoreFluentConfigurationParser(eb, context, declaredSourceDialect), new EFCoreEntityParser(eb, declaredSourceDialect, context), new EFCoreLinqQueryParser(qb, declaredSourceDialect, context)];
            }

            // orm.xml before the class: the specification's precedence (Jakarta Persistence
            // 3.2 §12.1 - XML metadata overrides annotations, and metadata-complete switches
            // them off), read through one context the two parsers share (decision 077).
            case ORMEnum.Hibernate:
            {
                var context = new JpaReadingContext(declaredSourceDialect);
                return qb is null
                    ? [new JpaOrmXmlParser(eb, context), new HibernateEntityParser(eb, context)]
                    : [new JpaOrmXmlParser(eb, context), new HibernateEntityParser(eb, context), new HibernateJpqlQueryParser(qb, declaredSourceDialect)];
            }

            // The same list and the same order for the second implementation of the same
            // specification (decision 080): orm.xml is standard and so is its precedence,
            // and what differs between the two is inside the wrapper's own parsers.
            case ORMEnum.EclipseLink:
            {
                var context = new JpaReadingContext(declaredSourceDialect);
                return qb is null
                    ? [new JpaOrmXmlParser(eb, context), new EclipseLinkEntityParser(eb, context)]
                    : [new JpaOrmXmlParser(eb, context), new EclipseLinkEntityParser(eb, context), new EclipseLinkJpqlQueryParser(qb, declaredSourceDialect)];
            }

            // MyBatis documents no precedence between its two mapping forms - it refuses the
            // concurrence of both outright, which is a Failure and not a Conflict
            // (decision 068) - so the list stands in the default order of decision 017: the
            // framework's input text first (the domain class, level 1a), its auxiliary
            // mapping artifacts after it (the mapper interface and the XML mapper, level 1b).
            // Two of the three units are a mapping and a query at once and are therefore
            // claimed twice over, which is the cleanest case decision 081 has; both passes
            // read through one context, found through the entity builder, because the query
            // pass needs what the entity pass saw - the method signatures above all, which
            // are the only place the type of a MyBatis query parameter lives (decision 084).
            case ORMEnum.MyBatis:
            {
                // The two mapping parsers take the declared dialect as well: a statement whose
                // result type is an entity states columns by its aliases (decision 181), read on
                // the entity pass by the same T-SQL reading the query pass uses.
                var context = MyBatisReadingContext.For(eb);
                return qb is null
                    ? [new MyBatisEntityParser(eb), new MyBatisMapperInterfaceParser(eb, context, declaredSourceDialect), new MyBatisXmlMappingParser(eb, context, declaredSourceDialect)]
                    : [new MyBatisEntityParser(eb), new MyBatisMapperInterfaceParser(eb, context, declaredSourceDialect), new MyBatisXmlMappingParser(eb, context, declaredSourceDialect), new MyBatisAnnotationQueryParser(qb, context, declaredSourceDialect), new MyBatisXmlQueryParser(qb, context, declaredSourceDialect)];
            }

            // Symmetric with the target side, which refuses an unsupported framework rather
            // than returning nothing: an empty parser list produced an empty result and no
            // error at all, so a bad source framework looked like a source with no entities.
            default:
                throw new InvalidOperationException("Source ORM not supported");
        }
    }
}
