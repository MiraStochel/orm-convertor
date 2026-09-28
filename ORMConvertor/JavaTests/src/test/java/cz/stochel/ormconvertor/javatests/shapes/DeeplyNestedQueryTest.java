package cz.stochel.ormconvertor.javatests.shapes;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.eclipselink.EclipseLinkBootstrap;
import cz.stochel.ormconvertor.javatests.hibernate.HibernateBootstrap;
import cz.stochel.ormconvertor.javatests.mybatis.MyBatisBootstrap;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionRecord;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.apache.ibatis.session.SqlSessionFactory;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

/**
 * The deliberately bad query of the .NET query-shape matrices
 * ({@code Tests/Combined/QueryShapeInputs.cs}), read from the same shared files under
 * {@code Tests/Database/QueryShapes} and sent from every source to each Java target: eight
 * subqueries four levels deep, three joins of which one runs over two columns and one over
 * three, grouping with a HAVING over a parameter, ordering and a bound slice. The .NET
 * suite proves the Java targets' text dry; this class has the Java framework itself judge
 * it, at verification levels 2 and 3 (decisions 016 and 027): {@code javac} over the
 * generated entities and query, Hibernate translating the JPQL against the metamodel,
 * EclipseLink parsing it, MyBatis binding the statement to the interface.
 *
 * <p>The two JPA targets take only the sources that state a key, because a source that
 * does not - Dapper, MyBatis - has its entities refused by a target that requires one
 * (decision 063) and leaves nothing to build a metamodel from; the query still comes out,
 * which the .NET matrices assert. MyBatis requires no key and takes all six.
 */
class DeeplyNestedQueryTest {

    private static final String[] JPA_ENTITIES = {
        "QueryShapes/entities/jpa/Customer.java",
        "QueryShapes/entities/jpa/CustomerOrder.java",
        "QueryShapes/entities/jpa/OrderLine.java",
        "QueryShapes/entities/jpa/OrderLineAllocation.java",
        "QueryShapes/entities/jpa/Product.java",
    };

    private static final String[] MYBATIS_ENTITIES = {
        "QueryShapes/entities/mybatis/Customer.java",
        "QueryShapes/entities/mybatis/CustomerOrder.java",
        "QueryShapes/entities/mybatis/OrderLine.java",
        "QueryShapes/entities/mybatis/OrderLineAllocation.java",
        "QueryShapes/entities/mybatis/Product.java",
        "QueryShapes/entities/mybatis/ShopMapper.xml",
    };

    /** A source framework and the shared files of its row: the domain, then the query. */
    enum Source {
        DAPPER(Orm.DAPPER, new String[] {"QueryShapes/entities/dapper/Shop.cs"},
                "QueryShapes/DeeplyNested/dapper/FindHeavyLines.sql"),
        EF_CORE(Orm.EF_CORE, new String[] {"QueryShapes/entities/efcore/Shop.cs"},
                "QueryShapes/DeeplyNested/efcore/FindHeavyLines.query.cs"),
        NHIBERNATE(Orm.NHIBERNATE,
                new String[] {"QueryShapes/entities/nhibernate/Shop.cs", "QueryShapes/entities/nhibernate/Shop.hbm.xml"},
                "QueryShapes/DeeplyNested/nhibernate/FindHeavyLines.hql"),
        HIBERNATE(Orm.HIBERNATE, JPA_ENTITIES, "QueryShapes/DeeplyNested/hibernate/FindHeavyLines.jpql"),
        ECLIPSELINK(Orm.ECLIPSELINK, JPA_ENTITIES, "QueryShapes/DeeplyNested/eclipselink/FindHeavyLines.jpql"),
        MYBATIS(Orm.MYBATIS, MYBATIS_ENTITIES,
                "QueryShapes/DeeplyNested/mybatis/FindHeavyLinesMapper.query.java",
                "QueryShapes/DeeplyNested/mybatis/FindHeavyLinesMapper.xml");

        private final int orm;
        private final List<String> resources;

        Source(int orm, String[] entities, String... query) {
            this.orm = orm;
            this.resources = concat(entities, query);
        }

        List<InputUnit> units() {
            return resources.stream().map(InputUnit::fromShared).toList();
        }

        private static List<String> concat(String[] first, String[] second) {
            String[] all = new String[first.length + second.length];
            System.arraycopy(first, 0, all, 0, first.length);
            System.arraycopy(second, 0, all, first.length, second.length);
            return List.of(all);
        }
    }

    /**
     * The sources that state a key - the rows the two JPA targets take. An enum of its own
     * rather than a narrowed {@code @EnumSource}, because the suite counts its own cases by
     * the whole enum (decision 087, {@code SuiteSizeTest}) and a narrowing would hide cases
     * from that count.
     */
    enum KeyStatingSource {
        EF_CORE(Source.EF_CORE),
        NHIBERNATE(Source.NHIBERNATE),
        HIBERNATE(Source.HIBERNATE),
        ECLIPSELINK(Source.ECLIPSELINK);

        final Source source;

        KeyStatingSource(Source source) {
            this.source = source;
        }
    }

    private static final Map<String, ConversionResponse> ANSWERS = new ConcurrentHashMap<>();

    @BeforeAll
    static void createSchemaAndAwaitTheInstance() throws Exception {
        TestSchema.create();
        ToolApi.awaitReady();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
        ANSWERS.clear();
    }

    /**
     * Level 3 for Hibernate: the generated entities and the query method compile, and
     * Hibernate translates the JPQL against the metamodel built over them - the entity joins
     * over two and three columns, the nine scopes and their correlations, the grouping and
     * the HAVING with its parameter, all judged by the framework rather than by a hallmark.
     */
    @ParameterizedTest
    @EnumSource(KeyStatingSource.class)
    void hibernateTranslatesTheGeneratedJpql(KeyStatingSource row) {
        ConversionResponse response = answer(row.source, Orm.HIBERNATE);
        String entityPackage = packageOf(response);

        try (JavaProject project = JavaProject.create("shape-hibernate")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            project.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries",
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            ClassLoader loader = project.compileAndLoad();
            List<Class<?>> mapped = project.loadAll().stream()
                    .filter(type -> !type.getSimpleName().equals("GeneratedQueries"))
                    .toList();

            String jpql = response.artifactOf(ContentType.JPQL_QUERY).content();

            try (SessionFactory factory = HibernateBootstrap.build("none", loader, mapped);
                 Session session = factory.openSession()) {
                // A projection of three items comes back as a row array; the translation is
                // what is asked for, nothing is executed.
                assertNotNull(session.createSelectionQuery(jpql, Object[].class), jpql);
            }
        }
    }

    /** Level 3 for EclipseLink: the same, with EclipseLink parsing the JPQL against its mapped model. */
    @ParameterizedTest
    @EnumSource(KeyStatingSource.class)
    void eclipseLinkParsesTheGeneratedJpql(KeyStatingSource row) {
        ConversionResponse response = answer(row.source, Orm.ECLIPSELINK);
        String entityPackage = packageOf(response);

        try (JavaProject project = JavaProject.create("shape-eclipselink")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            project.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries",
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            ClassLoader loader = project.compileAndLoad();
            List<Class<?>> mapped = project.loadAll().stream()
                    .filter(type -> !type.getSimpleName().equals("GeneratedQueries"))
                    .toList();

            String jpql = response.artifactOf(ContentType.JPQL_QUERY).content();

            try (EntityManagerFactory factory =
                         EclipseLinkBootstrap.build("none", loader, project.rootUrl(), mapped);
                 EntityManager manager = factory.createEntityManager()) {
                assertNotNull(manager.createQuery(jpql), jpql);
            }
        }
    }

    /**
     * Level 3 for MyBatis, from all six sources: the generated domain classes and the
     * interface compile, and MyBatis registers the statement of the mapper document under
     * the interface's method and binds the interface - the four placeholders of the
     * statement included, typed from the signature.
     */
    @ParameterizedTest
    @EnumSource(Source.class)
    void myBatisBindsTheGeneratedStatement(Source source) {
        ConversionResponse response = answer(source, Orm.MYBATIS);

        try (JavaProject project = JavaProject.create("shape-mybatis")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            String document = queryMapper(response);
            project.add(JavaSources.wrapMapperInterface(
                    packageOf(response),
                    MyBatisBootstrap.interfaceNameOf(document),
                    response.artifactOf(ContentType.JAVA_QUERY).content()));

            project.compileAndLoad();

            String namespace = MyBatisBootstrap.namespaceOf(document);
            SqlSessionFactory factory = MyBatisBootstrap.build(project.loader(), allMappers(response));

            assertTrue(factory.getConfiguration().hasStatement(namespace + "." + methodNameOf(response)),
                    "MyBatis did not register the statement of " + namespace);
            assertTrue(factory.getConfiguration().hasMapper(project.load(namespace)),
                    "MyBatis did not bind the mapper interface " + namespace);
        }
    }

    // ---- the exchange, and the pieces a case is assembled from ----------------------------

    /**
     * The tool's answer for one direction, asked once per direction. What is refused may be
     * an entity of a source that states no key (decision 063), never the query: a refusal of
     * the query names neither an entity nor a mapping category.
     */
    private static ConversionResponse answer(Source source, int targetOrm) {
        return ANSWERS.computeIfAbsent(source + "->" + targetOrm, key -> {
            ToolResponse answer = ToolApi.convert(source.orm, targetOrm, source.units());

            assertEquals(200, answer.statusCode(), answer.body());

            ConversionResponse conversion = answer.required();
            List<ConversionRecord> refusals = conversion.recordsOf(RecordKind.FAILURE).stream()
                    .filter(record -> record.entity() == null && record.category() == null)
                    .toList();
            assertTrue(refusals.isEmpty(),
                    "The tool refused the query of the " + source + " row for " + Orm.nameOf(targetOrm) + ":"
                    + System.lineSeparator() + conversion.describeRecords());

            return conversion;
        });
    }

    private static String packageOf(ConversionResponse response) {
        return JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content());
    }

    /** The mapper document carrying the statement - the only one of the answer that does. */
    private static String queryMapper(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream()
                .map(ConversionUnit::content)
                .filter(content -> content.contains("<select"))
                .findFirst()
                .orElseThrow(() -> new AssertionError("The answer carries no mapper with a statement"));
    }

    private static List<String> allMappers(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream().map(ConversionUnit::content).toList();
    }

    /** The id of the statement, which is the name of the method the declaration carries. */
    private static String methodNameOf(ConversionResponse response) {
        String declaration = response.artifactOf(ContentType.JAVA_QUERY).content();
        int parenthesis = declaration.indexOf('(');
        int space = declaration.lastIndexOf(' ', parenthesis);
        return declaration.substring(space + 1, parenthesis);
    }
}
