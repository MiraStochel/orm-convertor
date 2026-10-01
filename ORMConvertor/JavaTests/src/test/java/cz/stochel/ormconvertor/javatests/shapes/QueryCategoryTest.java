package cz.stochel.ormconvertor.javatests.shapes;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.eclipselink.EclipseLinkBootstrap;
import cz.stochel.ormconvertor.javatests.hibernate.HibernateBootstrap;
import cz.stochel.ormconvertor.javatests.mybatis.MyBatisBootstrap;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionRecord;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.QueryFeature;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.apache.ibatis.session.SqlSessionFactory;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The query categories of requirement T2, read from the shared manifest
 * ({@code QueryShapes/categories.txt}, the one {@code Tests/Combined/QueryShapeInputs.cs}
 * reads) and sent from every source that states them to each Java target - the second
 * dimension of what {@link DeeplyNestedQueryTest} does for the one deliberately bad query.
 * The .NET matrices prove the Java targets' text dry over the same files; here the Java
 * framework itself judges every category, at verification levels 2 and 3 (decisions 016
 * and 027): {@code javac} over the generated entities and the query method, Hibernate
 * translating the JPQL against the metamodel, EclipseLink parsing it, MyBatis binding the
 * statement to the interface.
 *
 * <p>A consumer project compiles its entities once and its queries against them, and so
 * does this class: per direction the entity artifacts are compiled once and, for a JPA
 * target, the framework's factory is built once over them; each category then compiles
 * its query method in a project of its own on top and is judged alone, so a failure names
 * the category. The entity artifacts of every category's answer are compared with the
 * shared ones first - the query unit must not change what the entities come out as.
 *
 * <p>Which sources state a category and what is refused where comes from the manifest,
 * not from a list of this class. A target that refuses by its descriptor has its refusal
 * asserted, a record naming the feature and no query artifact (decision 053), so that a
 * refusal which stops arriving is a failure rather than a case that quietly passes. A
 * source the manifest lists as refused <em>without a catalog</em> - a Dapper parameter,
 * whose scalar takes a mapping the source does not state (decision 083) - is not refused
 * here: this suite converts through an instance whose catalog holds the domain the schema
 * of this class creates, and the query asks the catalog for the binding itself, whichever
 * the target (decision 105), so the artifact is expected and judged like any other. The two
 * JPA targets take only the sources that state a key, for the reason
 * {@link DeeplyNestedQueryTest} gives; MyBatis takes all six.
 */
class QueryCategoryTest {

    /** The sources that state a key - the rows the two JPA targets take (decision 063). */
    private static final int[] KEY_STATING_SOURCES = {Orm.EF_CORE, Orm.NHIBERNATE, Orm.HIBERNATE, Orm.ECLIPSELINK};

    /** One case: a category from one source, into the target the test names. */
    record Case(QueryCategories.Category category, int source) {

        @Override
        public String toString() {
            return category.id() + " from " + Orm.nameOf(source);
        }
    }

    /** Every category from every key-stating source that states it. */
    static List<Case> jpaCases() {
        return cases(KEY_STATING_SOURCES);
    }

    /** Every category from every source that states it. */
    static List<Case> allCases() {
        return cases(Orm.ALL);
    }

    private static List<Case> cases(int[] sources) {
        List<Case> cases = new ArrayList<>();
        for (QueryCategories.Category category : QueryCategories.all()) {
            for (int source : sources) {
                if (category.statedBy(source)) {
                    cases.add(new Case(category, source));
                }
            }
        }

        return cases;
    }

    private static final Map<String, ConversionResponse> ANSWERS = new ConcurrentHashMap<>();
    private static final Map<String, Domain> DOMAINS = new ConcurrentHashMap<>();

    @BeforeAll
    static void createSchemaAndAwaitTheInstance() throws Exception {
        TestSchema.create();
        ToolApi.awaitReady();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        for (Domain domain : DOMAINS.values()) {
            domain.close();
        }
        DOMAINS.clear();
        ANSWERS.clear();
        TestSchema.drop();
    }

    /**
     * Levels 2 and 3 for Hibernate: the query method compiles over the compiled entities,
     * and Hibernate translates the JPQL against the metamodel built over them - entity
     * names, paths, the join condition, the parameters, the aggregate and the literal,
     * all judged by the framework rather than by a hallmark.
     */
    @ParameterizedTest
    @MethodSource("jpaCases")
    void hibernateTranslatesTheGeneratedJpql(Case row) {
        ConversionResponse response = answer(row, Orm.HIBERNATE);
        if (endsInAStatedRefusal(row, Orm.HIBERNATE, response)) {
            return;
        }

        Domain domain = domain(row, Orm.HIBERNATE, response);
        if (endsInAStatedFallback(row, Orm.HIBERNATE, response, domain)) {
            return;
        }

        try (JavaProject queries = domain.compileQuery(response)) {
            String jpql = response.artifactOf(ContentType.JPQL_QUERY).content();

            try (Session session = domain.hibernate().openSession()) {
                // A row array takes any selection, an entity as well as a projection; the
                // translation is what is asked for, nothing is executed.
                assertNotNull(session.createSelectionQuery(jpql, Object[].class), jpql);
            }
        }
    }

    /** The same for EclipseLink, which parses the JPQL against its mapped model. */
    @ParameterizedTest
    @MethodSource("jpaCases")
    void eclipseLinkParsesTheGeneratedJpql(Case row) {
        ConversionResponse response = answer(row, Orm.ECLIPSELINK);
        if (endsInAStatedRefusal(row, Orm.ECLIPSELINK, response)) {
            return;
        }

        Domain domain = domain(row, Orm.ECLIPSELINK, response);
        if (endsInAStatedFallback(row, Orm.ECLIPSELINK, response, domain)) {
            return;
        }

        try (JavaProject queries = domain.compileQuery(response)) {
            String jpql = response.artifactOf(ContentType.JPQL_QUERY).content();

            try (EntityManager manager = domain.eclipseLink().createEntityManager()) {
                assertNotNull(manager.createQuery(jpql), jpql);
            }
        }
    }

    /**
     * Levels 2 and 3 for MyBatis, from all six sources: the generated interface compiles
     * over the compiled domain classes, and MyBatis registers the statement of the mapper
     * document under the interface's method and binds the interface - placeholders and a
     * {@code <foreach>} included, typed from the signature.
     */
    @ParameterizedTest
    @MethodSource("allCases")
    void myBatisBindsTheGeneratedStatement(Case row) {
        ConversionResponse response = answer(row, Orm.MYBATIS);
        if (endsInAStatedRefusal(row, Orm.MYBATIS, response)) {
            return;
        }

        Domain domain = domain(row, Orm.MYBATIS, response);

        // MyBatis's statement is the dialect's SQL already, so it has nothing to fall back
        // from (decision 113); the call asserts that no record says otherwise.
        endsInAStatedFallback(row, Orm.MYBATIS, response, domain);
        try (JavaProject project = JavaProject.create("category-mybatis", domain.project())) {
            String document = MyBatisAnswer.queryMapper(response);
            project.add(JavaSources.wrapMapperInterface(
                    domain.entityPackage(),
                    MyBatisBootstrap.interfaceNameOf(document),
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            project.compileAndLoad();

            String namespace = MyBatisBootstrap.namespaceOf(document);
            SqlSessionFactory factory = MyBatisBootstrap.build(project.loader(), MyBatisAnswer.allMappers(response));

            assertTrue(factory.getConfiguration().hasStatement(namespace + "." + MyBatisAnswer.methodNameOf(response)),
                    "MyBatis did not register the statement of " + namespace + " for " + row);
            assertTrue(factory.getConfiguration().hasMapper(project.load(namespace)),
                    "MyBatis did not bind the mapper interface " + namespace + " for " + row);
        }
    }

    // ---- the exchange, and the pieces a case is assembled from ----------------------------

    /** The tool's answer for one case and target, asked once. */
    private static ConversionResponse answer(Case row, int target) {
        return ANSWERS.computeIfAbsent(row + "->" + target, key -> {
            ToolResponse answer = ToolApi.convert(row.source(), target, row.category().input(row.source()));

            assertEquals(200, answer.statusCode(), answer.body());

            return answer.required();
        });
    }

    /**
     * Whether the manifest states a refusal of this case by the target's descriptor and,
     * when it does, that the tool refused as stated: no query artifact and a record naming
     * the feature. When it states none, the answer may refuse nothing about the query; what
     * it may refuse is an entity of a source that states no key (decision 063), which names
     * an entity and a mapping category. A refusal the manifest states for a run without a
     * catalog is no refusal of this run (decision 105): the instance has the catalog and
     * the catalog has the domain, so the answer is judged as any other - and a refusal that
     * arrived all the same would say that the instance's catalog does not see the schema of
     * this class, which is what the message names.
     */
    private static boolean endsInAStatedRefusal(Case row, int target, ConversionResponse response) {
        Integer refusedBy = row.category().refusalBy(target);

        if (refusedBy == null) {
            List<ConversionRecord> refusals = response.records().stream()
                    .filter(record -> record.kind() == RecordKind.FAILURE && record.entity() == null && record.category() == null)
                    .toList();
            String why = row.category().refusalWithoutCatalog(row.source()) == null
                    ? ""
                    : " The manifest refuses this source without a catalog only; the instance the suite converts through"
                    + " has to see the domain of the schema " + TestDatabase.schemaName() + " in its catalog (decision 105).";
            assertTrue(refusals.isEmpty(),
                    "The tool refused the " + row + " query for " + Orm.nameOf(target) + ":"
                    + System.lineSeparator() + response.describeRecords() + why);
            return false;
        }

        assertTrue(response.artifactsOf(ContentType.JAVA_QUERY).isEmpty()
                        && response.artifactsOf(ContentType.JPQL_QUERY).isEmpty(),
                "The manifest states a refusal for " + row + " into " + Orm.nameOf(target)
                + ", but a query artifact came out:" + System.lineSeparator() + response.describeRecords());

        // A refusal of the target's descriptor is a record naming the feature, worded by
        // the builder (decision 053).
        int feature = refusedBy;
        assertTrue(response.records().stream().anyMatch(record ->
                        record.feature() != null && record.feature() == feature),
                "The manifest states that " + row + " into " + Orm.nameOf(target) + " is refused by "
                + QueryFeature.nameOf(refusedBy) + ", and no record says so:"
                + System.lineSeparator() + response.describeRecords());
        return true;
    }

    /**
     * Whether the manifest states that the target writes this case in native SQL (decision
     * 113) and, when it does, that the tool did: a record of kind Fallback naming the
     * feature, the bare SQL beside the method and no JPQL, and the method - createNativeQuery
     * over the EntityManager - compiling over the compiled entities, which is the second
     * level. A framework does not judge native SQL before running it, so the third level of
     * such an artifact is the .NET suite's T-SQL parse of the bare SQL, and the fourth the
     * differential matrix. When the manifest states none, no record may say that the target
     * fell back: a translation in the target's own language is never the other value.
     */
    private static boolean endsInAStatedFallback(Case row, int target, ConversionResponse response, Domain domain) {
        Integer fallbackBy = row.category().fallbackBy(target);
        List<ConversionRecord> fallbacks = response.recordsOf(RecordKind.FALLBACK);

        if (fallbackBy == null) {
            assertTrue(fallbacks.isEmpty(),
                    "The tool wrote the " + row + " query for " + Orm.nameOf(target)
                    + " in native SQL, which the manifest does not state:"
                    + System.lineSeparator() + response.describeRecords());
            return false;
        }

        int feature = fallbackBy;
        assertTrue(fallbacks.stream().anyMatch(record -> record.feature() != null && record.feature() == feature),
                "The manifest states that " + row + " into " + Orm.nameOf(target) + " falls back for "
                + QueryFeature.nameOf(feature) + ", and no Fallback record says so:"
                + System.lineSeparator() + response.describeRecords());
        assertFalse(response.artifactsOf(ContentType.SQL_QUERY).isEmpty(),
                "No native SQL came out beside the method of " + row + " into " + Orm.nameOf(target) + ".");
        assertTrue(response.artifactsOf(ContentType.JPQL_QUERY).isEmpty(),
                "JPQL came out for " + row + " into " + Orm.nameOf(target) + ", which the manifest states as falling back.");

        try (JavaProject queries = domain.compileQuery(response)) {
            assertTrue(response.artifactOf(ContentType.JAVA_QUERY).content().contains("em.createNativeQuery("),
                    "The method of " + row + " into " + Orm.nameOf(target) + " does not call createNativeQuery.");
        }

        return true;
    }

    /**
     * The shared part of one direction, built from the first answer that reaches it; every
     * later answer of the direction has to carry the same entity artifacts.
     */
    private static Domain domain(Case row, int target, ConversionResponse response) {
        String direction = Orm.nameOf(row.source()) + " -> " + Orm.nameOf(target);
        Domain domain = DOMAINS.computeIfAbsent(direction,
                key -> new Domain("category-" + Orm.nameOf(target).toLowerCase(), response));

        assertEquals(domain.entities, entitiesOf(response),
                "The entity artifacts of " + direction + " differ between categories, so the query unit of "
                + row + " changed what the entities come out as.");
        return domain;
    }

    private static List<String> entitiesOf(ConversionResponse response) {
        return response.artifactsOf(ContentType.JAVA_ENTITY).stream().map(ConversionUnit::content).toList();
    }

    /**
     * What one direction shares over every category: the entity artifacts compiled once,
     * the package they declare, and - for a JPA target - the framework's factory built over
     * them once. Everything a consumer project does once for all of its queries.
     */
    private static final class Domain implements AutoCloseable {

        private final List<String> entities;
        private final String entityPackage;
        private final JavaProject project;
        private final ClassLoader loader;
        private final List<Class<?>> mapped;

        private SessionFactory hibernate;
        private EntityManagerFactory eclipseLink;

        Domain(String name, ConversionResponse response) {
            entities = entitiesOf(response);
            entityPackage = JavaSources.packageOf(entities.get(0));
            project = JavaProject.create(name);
            for (String entity : entities) {
                project.add(entity);
            }

            loader = project.compileAndLoad();
            mapped = project.loadAll();
        }

        JavaProject project() {
            return project;
        }

        String entityPackage() {
            return entityPackage;
        }

        /**
         * Level 2 for the query alone: its method compiles in a project of its own over the
         * compiled entities, so what {@code javac} rejects is attributed to the category.
         */
        JavaProject compileQuery(ConversionResponse response) {
            JavaProject queries = JavaProject.create("category-query", project);
            queries.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries",
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            try {
                queries.compileAndLoad();
            } catch (RuntimeException | AssertionError e) {
                queries.close();
                throw e;
            }

            return queries;
        }

        synchronized SessionFactory hibernate() {
            if (hibernate == null) {
                hibernate = HibernateBootstrap.build("none", loader, mapped);
            }
            return hibernate;
        }

        synchronized EntityManagerFactory eclipseLink() {
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
