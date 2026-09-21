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
import java.util.List;
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
 */
public final class JavaQueryRunner {

    private JavaQueryRunner() {
    }

    /** Whether this suite can run a query of that framework at all. */
    public static boolean owns(int framework) {
        return framework == Orm.HIBERNATE || framework == Orm.ECLIPSELINK || framework == Orm.MYBATIS;
    }

    /**
     * Translates the query into the target and runs it, returning the rendered rows in the
     * order the comparison wants them: as they came for a query that carries an ordering,
     * sorted by the rendered line for one that does not.
     */
    public static List<String> run(DifferentialQuery query, int target) throws Exception {
        return run(query, target, null);
    }

    /** The same, with the query artifact replaced by a mutation of it (the negative half). */
    public static List<String> run(DifferentialQuery query, int target, String mutated) throws Exception {
        ConversionResponse response = translate(query, target);
        List<List<Object>> rows = execute(query, target, response, mutated);

        List<String> rendered = new ArrayList<>();
        for (List<Object> row : rows) {
            rendered.add(ResultRow.render(row, query.settings()));
        }

        return query.ordered() ? rendered : ResultRow.sorted(rendered);
    }

    /** The conversion behind a run, refusals and all. */
    public static ConversionResponse translate(DifferentialQuery query, int target) {
        ToolResponse answer = ToolApi.convert(query.source(), target, query.units());
        assertEquals(200, answer.statusCode(), answer.body());

        ConversionResponse response = answer.required();

        assertTrue(response.recordsOf(RecordKind.FAILURE).isEmpty(),
                query.id() + ": " + Orm.nameOf(query.source()) + " -> " + Orm.nameOf(target)
                        + " was refused, so the pair has nothing to compare:"
                        + System.lineSeparator() + response.describeRecords());

        return response;
    }

    private static List<List<Object>> execute(
            DifferentialQuery query, int target, ConversionResponse response, String mutated) throws Exception {

        if (target == Orm.MYBATIS) {
            return runMyBatis(query, response, mutated);
        }

        return runJpa(query, target, response, mutated);
    }

    /**
     * Hibernate and EclipseLink differ in nothing the run cares about: both deploy the
     * generated entity and hand out an EntityManager, and the generated method takes one.
     * Which factory builds it is the whole difference, exactly as decision 080 claimed.
     */
    private static List<List<Object>> runJpa(
            DifferentialQuery query, int target, ConversionResponse response, String mutated) throws Exception {

        try (JavaProject project = JavaProject.create("diff-" + Orm.nameOf(target).toLowerCase())) {
            List<String> entityNames = new ArrayList<>();
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                entityNames.add(project.add(entity.content()));
            }

            String packageName = JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content());
            String wrapper = project.add(JavaSources.wrapQuery(
                    packageName, "GeneratedQueries", queryArtifact(response, mutated)));

            ClassLoader loader = project.compileAndLoad();

            List<Class<?>> entities = new ArrayList<>();
            for (String name : entityNames) {
                entities.add(project.load(name));
            }

            EntityManagerFactory factory = target == Orm.HIBERNATE
                    ? HibernateBootstrap.build("none", loader, entities)
                    : EclipseLinkBootstrap.build("none", loader, project.rootUrl(), entities);

            try (factory; EntityManager manager = factory.createEntityManager()) {
                Object returned = invoke(project.load(wrapper), query, manager);
                List<?> rows = (List<?>) returned.getClass().getMethod("getResultList").invoke(returned);

                // JPQL hands a projection back as an object array per row and an entity as
                // the instance itself, so the shape of the query decides how a row is read.
                return rows(rows, query, query.projection());
            }
        }
    }

    private static List<List<Object>> runMyBatis(
            DifferentialQuery query, ConversionResponse response, String mutated) throws Exception {

        List<String> mappers = new ArrayList<>();
        for (ConversionUnit unit : response.artifactsOf(ContentType.XML)) {
            mappers.add(unit.content());
        }

        String original = statementMapper(response, query.id());

        // For MyBatis the query text is in the mapper, not in the method declaration, so a
        // mutation replaces the document rather than the artifact the JPA targets carry.
        String statementMapper = mutated != null ? mutated : original;
        mappers.set(mappers.indexOf(original), statementMapper);

        try (JavaProject project = JavaProject.create("diff-mybatis")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            String packageName = JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content());
            project.add(JavaSources.wrapMapperInterface(
                    packageName,
                    MyBatisBootstrap.interfaceNameOf(statementMapper),
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, mappers);
            Class<?> mapper = project.load(MyBatisBootstrap.namespaceOf(statementMapper));

            try (SqlSession session = factory.openSession()) {
                Method method = mapper.getDeclaredMethods()[0];
                Object returned = method.invoke(session.getMapper(mapper), values(query, method));

                // MyBatis materializes into the domain class, and a projection fills only
                // the properties it selected; both are therefore read by name.
                return rows((List<?>) returned, query, false);
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

    private static List<List<Object>> rows(List<?> returned, DifferentialQuery query, boolean positional) {
        List<List<Object>> rows = new ArrayList<>();

        for (Object item : returned) {
            rows.add(positional ? positional(item, query) : byName(item, query));
        }

        return rows;
    }

    private static List<Object> positional(Object item, DifferentialQuery query) {
        // A single projected field comes back bare rather than as a one-element array.
        List<Object> values = item instanceof Object[] array ? Arrays.asList(array) : List.of(item);

        assertEquals(query.fields().size(), values.size(),
                query.id() + ": a row came back with " + values.size() + " fields and the matrix states "
                        + query.fields().size() + ".");

        return values;
    }

    private static List<Object> byName(Object item, DifferentialQuery query) {
        List<Object> values = new ArrayList<>();

        for (String field : query.fields()) {
            values.add(read(item, field, query));
        }

        return values;
    }

    /**
     * One field off a row. A generated class spells the getter of a boolean {@code isX} and
     * of everything else {@code getX}, so both are tried before the field is given up on.
     */
    private static Object read(Object item, String field, DifferentialQuery query) {
        for (String getter : List.of("get" + field, "is" + field)) {
            try {
                return item.getClass().getMethod(getter).invoke(item);
            } catch (NoSuchMethodException ignored) {
                // The other spelling, then.
            } catch (ReflectiveOperationException e) {
                throw new IllegalStateException(query.id() + ": reading " + field + " failed.", e);
            }
        }

        throw new IllegalStateException(
                query.id() + ": the row type " + item.getClass().getName() + " has neither get" + field
                        + " nor is" + field + "; the matrix states the fields as " + query.fields() + ".");
    }
}
