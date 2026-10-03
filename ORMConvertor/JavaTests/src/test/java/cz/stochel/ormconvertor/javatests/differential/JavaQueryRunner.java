package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.eclipselink.EclipseLinkBootstrap;
import cz.stochel.ormconvertor.javatests.hibernate.HibernateBootstrap;
import cz.stochel.ormconvertor.javatests.mybatis.MyBatisBootstrap;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;

/**
 * Runs a generated query against the fixture and renders what came back (decision 089) -
 * the fourth verification level of decision 016 applied to a query, on the three Java
 * frameworks. The .NET three are the other suite's, and the two never meet in a process:
 * they meet over the canonical result, because the decision refused to grow an endpoint
 * that would run foreign code.
 *
 * <p>The artifacts come from a running instance of the tool over HTTP, as everything in
 * this suite does since decision 078.
 *
 * <p>A consumer project compiles its entities once and its queries against them, and so
 * does this runner: the entity artifacts of a direction are compiled once and, for a JPA
 * target, the framework's factory is built once over them; every query of the direction
 * then compiles its method in a project of its own on top ({@code JavaProject.create(name,
 * base)}). With every category stated by up to six sources, the matrix runs a few hundred
 * queries through this class, and a factory per run would cost minutes for nothing the
 * verdict depends on. The cache lives until {@link #closeAll()}, which the test classes call
 * when they are done.
 */
public final class JavaQueryRunner {

    private static final Map<String, Domain> DOMAINS = new LinkedHashMap<>();

    private JavaQueryRunner() {
    }

    /** Whether this suite can run a query of that framework at all. */
    public static boolean owns(int framework) {
        return framework == Orm.HIBERNATE || framework == Orm.ECLIPSELINK || framework == Orm.MYBATIS;
    }

    /**
     * Translates the query from the source into the target and runs it, returning the
     * rendered rows in the order the comparison wants them: as they came for a query that
     * carries an ordering, sorted by the rendered line for one that does not.
     */
    public static List<String> run(DifferentialQuery query, int source, int target) throws Exception {
        return run(query, source, target, null);
    }

    /** The same, with the query artifact replaced by a mutation of it (the negative half). */
    public static List<String> run(DifferentialQuery query, int source, int target, String mutated) throws Exception {
        ConversionResponse response = translate(query, source, target);
        List<List<Object>> rows = execute(query, target, response, mutated);

        List<String> rendered = new ArrayList<>();
        for (List<Object> row : rows) {
            rendered.add(ResultRow.render(row, query.settings()));
        }

        return query.ordered() ? rendered : ResultRow.sorted(rendered);
    }

    /** The conversion behind a run, refusals and all. */
    public static ConversionResponse translate(DifferentialQuery query, int source, int target) {
        ToolResponse answer = ToolApi.convert(source, target, query.units(source));
        assertEquals(200, answer.statusCode(), answer.body());

        ConversionResponse response = answer.required();

        assertTrue(response.recordsOf(RecordKind.FAILURE).isEmpty(),
                query.id() + ": " + Orm.nameOf(source) + " -> " + Orm.nameOf(target)
                        + " was refused, so the pair has nothing to compare:"
                        + System.lineSeparator() + response.describeRecords());

        return response;
    }

    /** Closes every compiled domain and the factories built over them. */
    public static synchronized void closeAll() {
        for (Domain domain : DOMAINS.values()) {
            domain.close();
        }
        DOMAINS.clear();
    }

    private static List<List<Object>> execute(
            DifferentialQuery query, int target, ConversionResponse response, String mutated) throws Exception {

        Domain domain = domain(target, response);

        if (target == Orm.MYBATIS) {
            return runMyBatis(query, response, domain, mutated);
        }

        return runJpa(query, target, response, domain, mutated);
    }

    /**
     * Hibernate and EclipseLink differ in nothing the run cares about: both deploy the
     * generated entity and hand out an EntityManager, and the generated method takes one.
     * Which factory builds it is the whole difference, exactly as decision 080 claimed.
     */
    private static List<List<Object>> runJpa(
            DifferentialQuery query, int target, ConversionResponse response, Domain domain, String mutated) throws Exception {

        try (JavaProject queries = JavaProject.create("diff-query", domain.project)) {
            String wrapper = queries.add(JavaSources.wrapQuery(
                    domain.entityPackage, "GeneratedQueries", queryArtifact(response, mutated)));
            queries.compileAndLoad();

            try (EntityManager manager = domain.factory(target).createEntityManager()) {
                Object returned = invoke(queries.load(wrapper), query, manager);
                List<?> rows = (List<?>) returned.getClass().getMethod("getResultList").invoke(returned);

                // JPQL hands a projection back as an object array per row and an entity as
                // the instance itself, so the shape of the query decides how a row is read.
                return rows(rows, query.id(), query.fields(), query.projection());
            }
        }
    }

    private static List<List<Object>> runMyBatis(
            DifferentialQuery query, ConversionResponse response, Domain domain, String mutated) throws Exception {

        List<String> mappers = new ArrayList<>();
        for (ConversionUnit unit : response.artifactsOf(ContentType.XML)) {
            mappers.add(unit.content());
        }

        String original = statementMapper(response, query.id());

        // For MyBatis the query text is in the mapper, not in the method declaration, so a
        // mutation replaces the document rather than the artifact the JPA targets carry.
        String statementMapper = mutated != null ? mutated : original;
        mappers.set(mappers.indexOf(original), statementMapper);

        try (JavaProject project = JavaProject.create("diff-mybatis", domain.project)) {
            project.add(JavaSources.wrapMapperInterface(
                    domain.entityPackage,
                    MyBatisBootstrap.interfaceNameOf(statementMapper),
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, mappers);
            Class<?> mapper = project.load(MyBatisBootstrap.namespaceOf(statementMapper));

            try (SqlSession session = factory.openSession()) {
                Method method = mapper.getDeclaredMethods()[0];
                Object returned = method.invoke(session.getMapper(mapper), values(query, method));

                // MyBatis materializes a whole-entity query into the domain class and a
                // projection into a Map per row keyed by the projected columns (decision
                // 104); both are therefore read by name.
                return rows((List<?>) returned, query.id(), query.fields(), false);
            }
        }
    }

    private static String queryArtifact(ConversionResponse response, String mutated) {
        return mutated != null ? mutated : response.artifactOf(ContentType.JAVA_QUERY).content();
    }

    /**
     * The artifact that carries the query text, which is where a mutation of decision 089
     * has to be applied. For the JPA profiles it is the generated method; for MyBatis the
     * method declaration says nothing about the query and the mapper document says it all.
     */
    public static String mutableArtifact(ConversionResponse response, int target, String queryId) {
        return target == Orm.MYBATIS
                ? statementMapper(response, queryId)
                : response.artifactOf(ContentType.JAVA_QUERY).content();
    }

    private static String statementMapper(ConversionResponse response, String queryId) {
        return response.artifactsOf(ContentType.XML).stream()
                .map(ConversionUnit::content)
                .filter(content -> content.contains("<select"))
                .findFirst()
                .orElseThrow(() -> new AssertionError(queryId + ": the answer carries no mapper with a statement"));
    }

    /**
     * Calls the one generated method. Its first parameter is the framework's handle and the
     * parameters of the query follow it (decision 083); the matrix states their values.
     */
    private static Object invoke(Class<?> queries, DifferentialQuery query, Object handle) throws Exception {
        Method method = Arrays.stream(queries.getDeclaredMethods())
                .findFirst()
                .orElseThrow(() -> new AssertionError(query.id() + ": the generated class declares no method"));

        Object[] arguments = new Object[query.arguments().size() + 1];
        arguments[0] = handle;
        for (int index = 0; index < query.arguments().size(); index++) {
            arguments[index + 1] = query.arguments().get(index).materialize();
        }

        assertEquals(arguments.length, method.getParameterCount(),
                query.id() + ": the generated method takes " + method.getParameterCount()
                        + " parameters, and the matrix binds " + query.arguments().size()
                        + " beside the framework's handle.");

        return method.invoke(null, arguments);
    }

    /** The arguments of a mapper method, which has no handle in front of them. */
    private static Object[] values(DifferentialQuery query, Method method) {
        assertEquals(query.arguments().size(), method.getParameterCount(),
                query.id() + ": the generated mapper method takes " + method.getParameterCount()
                        + " parameters and the matrix binds " + query.arguments().size() + ".");

        Object[] arguments = new Object[query.arguments().size()];
        for (int index = 0; index < arguments.length; index++) {
            arguments[index] = query.arguments().get(index).materialize();
        }

        return arguments;
    }

    /**
     * The rows a generated method returned, each with the named fields in the order given -
     * by position for a JPA projection, by name otherwise. Public because the judge of the
     * LDBC catalog reads the rows of its artifacts the same way (decision 117).
     */
    public static List<List<Object>> rows(List<?> returned, String id, List<String> fields, boolean positional) {
        List<List<Object>> rows = new ArrayList<>();

        // The columns the rows of a projection carry between them. MyBatis puts no entry for
        // a column whose value is NULL into the map of a row - callSettersOnNulls is off by
        // default, and the suite assumes nothing a configuration would state
        // (MyBatisBootstrap) -, so a column one row lacks and another carries is a NULL there.
        Set<Object> columns = new HashSet<>();
        for (Object item : returned) {
            if (item instanceof Map<?, ?> map) {
                columns.addAll(map.keySet());
            }
        }

        for (Object item : returned) {
            rows.add(positional ? positional(item, id, fields) : byName(item, id, fields, columns));
        }

        return rows;
    }

    private static List<Object> positional(Object item, String id, List<String> fields) {
        // A single projected field comes back bare rather than as a one-element array.
        List<Object> values = item instanceof Object[] array ? Arrays.asList(array) : List.of(item);

        assertEquals(fields.size(), values.size(),
                id + ": a row came back with " + values.size() + " fields and the caller states "
                        + fields.size() + ".");

        return values;
    }

    /**
     * The fields of a row read by the names the matrix states: off the map MyBatis returns
     * for a projection (decision 104), off the accessors of an entity otherwise. A field the
     * map of this row lacks is a NULL where another row of the result carries it; a field no
     * row carries is a column the projection does not have, which is what the check is for.
     */
    private static List<Object> byName(Object item, String id, List<String> fields, Set<Object> columnsOfTheResult) {
        List<Object> values = new ArrayList<>();

        if (item instanceof Map<?, ?> columns) {
            for (String field : fields) {
                if (!columns.containsKey(field) && !columnsOfTheResult.contains(field)) {
                    throw new IllegalStateException(
                            id + ": the row has no column \"" + field + "\"; it has " + columns.keySet()
                                    + " and the caller states the fields as " + fields + ".");
                }
                values.add(columns.get(field));
            }

            return values;
        }

        for (String field : fields) {
            values.add(read(item, field, id, fields));
        }

        return values;
    }

    /**
     * One field off a row. A generated class spells the getter of a boolean {@code isX} and
     * of everything else {@code getX}, so both are tried before the field is given up on.
     */
    private static Object read(Object item, String field, String id, List<String> fields) {
        for (String getter : List.of("get" + field, "is" + field)) {
            try {
                return item.getClass().getMethod(getter).invoke(item);
            } catch (NoSuchMethodException ignored) {
                // The other spelling, then.
            } catch (ReflectiveOperationException e) {
                throw new IllegalStateException(id + ": reading " + field + " failed.", e);
            }
        }

        throw new IllegalStateException(
                id + ": the row type " + item.getClass().getName() + " has neither get" + field
                        + " nor is" + field + "; the caller states the fields as " + fields + ".");
    }

    /**
     * The compiled entities of a direction, built from the first answer that reaches them.
     * Keyed by the target and the entity artifacts themselves rather than by the direction,
     * so that two queries whose entities come out the same share one compilation and two
     * whose entities differ - the six queries of the matrix's own against the domain of the
     * categories - never share by accident.
     */
    private static synchronized Domain domain(int target, ConversionResponse response) {
        List<String> entities = response.artifactsOf(ContentType.JAVA_ENTITY).stream().map(ConversionUnit::content).toList();
        String key = target + "|" + String.join("\u0000", entities);

        return DOMAINS.computeIfAbsent(key, k -> new Domain("diff-" + Orm.nameOf(target).toLowerCase(), entities));
    }

    /**
     * What one direction shares over every query: the entity artifacts compiled once, the
     * package they declare, and - for a JPA target - the framework's factory built over
     * them once.
     */
    private static final class Domain implements AutoCloseable {

        private final String entityPackage;
        private final JavaProject project;
        private final ClassLoader loader;
        private final List<Class<?>> mapped;

        private EntityManagerFactory hibernate;
        private EntityManagerFactory eclipseLink;

        Domain(String name, List<String> entities) {
            entityPackage = JavaSources.packageOf(entities.get(0));
            project = JavaProject.create(name);
            for (String entity : entities) {
                project.add(entity);
            }

            loader = project.compileAndLoad();
            mapped = project.loadAll();
        }

        synchronized EntityManagerFactory factory(int target) {
            if (target == Orm.HIBERNATE) {
                if (hibernate == null) {
                    hibernate = HibernateBootstrap.build("none", loader, mapped);
                }
                return hibernate;
            }

            if (eclipseLink == null) {
                eclipseLink = EclipseLinkBootstrap.build("none", loader, project.rootUrl(), mapped);
            }
            return eclipseLink;
        }

        @Override
        public void close() {
            if (hibernate != null) {
                hibernate.close();
            }
            if (eclipseLink != null) {
                eclipseLink.close();
            }
            project.close();
        }
    }
}
