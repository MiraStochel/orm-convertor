package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
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
import java.lang.reflect.Method;
import java.math.BigDecimal;
import java.util.Arrays;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

/**
 * Verification levels 2 to 4 over the artifacts the tool generates for EclipseLink
 * (decisions 016, 078 and 080) - the same path the Hibernate scenarios take, through the
 * same product surface: the suite posts its input files to {@code /convert}, compiles the
 * answer with {@code javac}, hands the loaded classes to EclipseLink and runs one of them
 * against the database. It is what lets F9 claim more than a shape, and it is the second
 * implementation that makes the claim worth something: the same representation is written
 * twice and accepted twice, by two providers that disagree about every default behind it.
 *
 * <p>The source frameworks are the three whose input states everything itself, Dapper
 * excepted for the reason decision 078 gives.
 */
class GeneratedArtifactTest {

    /** What the level-4 scenario writes; the key is assigned, so the test picks it. */
    private static final int PRODUCT_ID = 990002;
    private static final String PRODUCT_NAME = "Generated widget";
    private static final BigDecimal UNIT_PRICE = new BigDecimal("12.3400");

    private static final Map<Scenario, ConversionResponse> ANSWERS = new EnumMap<>(Scenario.class);

    /** A source framework, and the input files of this suite written in its languages. */
    enum Scenario {
        HIBERNATE(Orm.HIBERNATE,
                "hibernate/Product.java", "hibernate/CustomerOrder.java", "hibernate/Product.jpql"),
        EF_CORE(Orm.EF_CORE,
                "efcore/Product.cs", "efcore/CustomerOrder.cs", "efcore/Product.query.cs"),
        NHIBERNATE(Orm.NHIBERNATE,
                "nhibernate/Product.cs", "nhibernate/CustomerOrder.cs",
                "nhibernate/Shop.hbm.xml", "nhibernate/Product.query.cs");

        private final int sourceOrm;
        private final List<String> resources;

        Scenario(int sourceOrm, String... resources) {
            this.sourceOrm = sourceOrm;
            this.resources = List.of(resources);
        }

        List<InputUnit> units() {
            return resources.stream().map(InputUnit::fromResource).toList();
        }
    }

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

    // ---- what the answer says, before anything is compiled -------------------------------

    /** The answer names EclipseLink and the release this JVM really loaded (decision 013). */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theAnswerNamesEclipseLinkAndItsRelease(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        assertEquals(scenario.sourceOrm, response.sourceFramework());
        assertEquals(Orm.ECLIPSELINK, response.targetFramework());
        assertNotNull(response.targetFrameworkVersion());
        assertNotNull(response.runId());
    }

    // ---- level 2 and 3: javac, then the factory ------------------------------------------

    /**
     * Every entity artifact compiles in the file and package Java demands of it, and
     * EclipseLink builds a factory over the loaded classes and maps both entities - the
     * two-part key with its {@code @IdClass} among them.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedEntitiesCompileAndEclipseLinkMapsThem(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("el-entities")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            ClassLoader loader = project.compileAndLoad();

            try (EntityManagerFactory factory =
                         EclipseLinkBootstrap.build("none", loader, project.rootUrl(), project.loadAll())) {
                Set<String> mapped = factory.getMetamodel().getEntities().stream()
                        .map(jakarta.persistence.metamodel.EntityType::getName)
                        .collect(Collectors.toSet());

                assertEquals(Set.of("Product", "CustomerOrder"), mapped);
            }
        }
    }

    /**
     * The query branch of the same two levels (decision 027): the generated method compiles
     * beside the entities, and the bare JPQL is handed to EclipseLink, which parses it
     * against the mapped model without running it.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedQueryCompilesAndTheJpqlIsParsed(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);
        String entityPackage = packageOf(response);

        try (JavaProject project = JavaProject.create("el-query")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            ConversionUnit method = response.artifactOf(ContentType.JAVA_QUERY);
            project.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries", method.content()));

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

    // ---- level 4: the generated entity against the database ------------------------------

    /**
     * The generated entity is used: a product is stored through it and read back with the
     * same identity, in a transaction rolled back at the end. The entity types exist only
     * at run time, so the test reaches them by reflection.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void aProductIsStoredAndReadBackThroughTheGeneratedEntity(Scenario scenario) throws Exception {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("el-persistence")) {
            String product = project.add(entityArtifact(response, "Product"));
            ClassLoader loader = project.compileAndLoad();
            Class<?> productClass = project.load(product);

            try (EntityManagerFactory factory =
                         EclipseLinkBootstrap.build("none", loader, project.rootUrl(), List.of(productClass));
                 EntityManager manager = factory.createEntityManager()) {
                manager.getTransaction().begin();
                try {
                    Object row = productClass.getConstructor().newInstance();
                    set(row, "ProductId", PRODUCT_ID);
                    set(row, "ProductName", PRODUCT_NAME);
                    set(row, "Sku", "GEN-0003");
                    set(row, "UnitPrice", UNIT_PRICE);
                    set(row, "IsDiscontinued", false);

                    manager.persist(row);
                    manager.flush();
                    manager.clear();

                    Object reloaded = manager.find(productClass, PRODUCT_ID);

                    assertNotNull(reloaded, "the stored product was not found by its assigned key");
                    assertEquals(PRODUCT_NAME, get(reloaded, "ProductName"));
                    assertEquals(0, UNIT_PRICE.compareTo((BigDecimal) get(reloaded, "UnitPrice")));
                } finally {
                    manager.getTransaction().rollback();
                }
            }
        }
    }

    // ---- the negative half ---------------------------------------------------------------

    /**
     * The identifier is the one mapping fact the Jakarta Persistence descriptor marks as
     * required, because an entity without one is refused (§2.4) - so the tool never emits
     * such an artifact and the completeness gate reports a {@code Failure} instead. Taken
     * out of a valid artifact, this is what level 3 saying no looks like for EclipseLink,
     * and it is measured here rather than assumed: the two implementations refuse
     * different things at different moments, which is the whole reason F9 has a suite.
     */
    @Test
    void anEntityWithoutItsIdentifierIsRefused() {
        ConversionResponse response = answerFor(Scenario.HIBERNATE);
        String artifact = JavaSources.withoutAnnotation(entityArtifact(response, "Product"), "@Id");

        try (JavaProject project = JavaProject.create("el-no-id")) {
            String product = project.add(artifact);
            ClassLoader loader = project.compileAndLoad();
            List<Class<?>> entities = List.of(project.load(product));

            Throwable refusal = assertThrows(
                    RuntimeException.class,
                    () -> EclipseLinkBootstrap.build("none", loader, project.rootUrl(), entities).close(),
                    "EclipseLink accepted an entity with no identifier");

            assertTrue(chain(refusal).contains("Product"),
                    "the refusal does not name the entity: " + chain(refusal));
        }
    }

    // ---- the exchange, and reaching a class that exists only at run time -----------------

    private static ConversionResponse answerFor(Scenario scenario) {
        return ANSWERS.computeIfAbsent(scenario, s -> {
            ToolResponse answer = ToolApi.convert(s.sourceOrm, Orm.ECLIPSELINK, s.units());

            assertEquals(200, answer.statusCode(), answer.body());

            ConversionResponse conversion = answer.required();
            assertTrue(conversion.recordsOf(RecordKind.FAILURE).isEmpty(),
                    "The tool refused an artifact of the " + Orm.nameOf(s.sourceOrm)
                    + " scenario:" + System.lineSeparator() + conversion.describeRecords());

            return conversion;
        });
    }

    /** The entity artifact whose public class has this name. */
    private static String entityArtifact(ConversionResponse response, String className) {
        return response.artifactsOf(ContentType.JAVA_ENTITY).stream()
                .map(ConversionUnit::content)
                .filter(content -> JavaSources.publicClassOf(content).equals(className))
                .findFirst()
                .orElseThrow(() -> new AssertionError("The answer carries no entity artifact " + className));
    }

    /** The package the entity artifacts declare - where the query class belongs too. */
    private static String packageOf(ConversionResponse response) {
        return JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content());
    }

    private static void set(Object target, String property, Object value) throws Exception {
        accessor(target, "set" + property, 1).invoke(target, value);
    }

    private static Object get(Object target, String property) throws Exception {
        return accessor(target, "get" + property, 0).invoke(target);
    }

    private static Method accessor(Object target, String name, int parameters) {
        return Arrays.stream(target.getClass().getMethods())
                .filter(m -> m.getName().equals(name) && m.getParameterCount() == parameters)
                .findFirst()
                .orElseThrow(() -> new AssertionError(
                        "The generated class " + target.getClass().getName() + " has no " + name));
    }

    /** Every message of the cause chain - a refusal names the class deep inside it. */
    private static String chain(Throwable throwable) {
        StringBuilder text = new StringBuilder();
        for (Throwable current = throwable; current != null; current = current.getCause()) {
            text.append(current).append(System.lineSeparator());
        }
        return text.toString();
    }
}
