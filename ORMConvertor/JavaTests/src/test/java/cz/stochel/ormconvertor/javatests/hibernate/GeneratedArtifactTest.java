package cz.stochel.ormconvertor.javatests.hibernate;

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
import java.lang.reflect.Method;
import java.math.BigDecimal;
import java.util.Arrays;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.hibernate.Version;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

/**
 * Verification levels 2 to 4 over the artifacts the tool really generates (decisions 016
 * and 078). The suite is an ordinary client of a running instance: it posts its own input
 * files to {@code /convert}, derives files and packages from the answer by the rule of the
 * Java language, compiles them with {@code javac}, hands the loaded classes to Hibernate
 * and runs one of them against the database. That is the whole path F7 and F12 ask about,
 * through the product surface the tool has, in the same run.
 *
 * <p>The three source frameworks here are the ones whose input states everything itself,
 * so the answer does not depend on the catalog having been reached. Dapper waits: its
 * entity cannot state a table, let alone a schema, and while both suites' schemas stand
 * in one database a lookup without a stated schema is ambiguous (decision 078).
 */
class GeneratedArtifactTest {

    /** What the level-4 scenario writes; the key is assigned, so the test picks it. */
    private static final int PRODUCT_ID = 990001;
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
        // The schema first: the instance reads the catalog during the completion phase,
        // and the tables the input names have to be there when it does.
        TestSchema.create();
        ToolApi.awaitReady();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
        ANSWERS.clear();
    }

    // ---- what the answer says, before anything is compiled -------------------------------

    /**
     * The release the artifacts are valid against is the release this JVM really loaded.
     * The .NET suite binds the descriptor to the text of {@code pom.xml} without a JVM;
     * this is the same binding from the other side, made at run time - the Java
     * counterpart of {@code DeclaredVersionsMatchTheVerificationPackages}.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theAnswerNamesHibernateAndTheReleaseTheJvmLoaded(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        assertEquals(scenario.sourceOrm, response.sourceFramework());
        assertEquals(Orm.HIBERNATE, response.targetFramework());
        assertEquals(Version.getVersionString(), response.targetFrameworkVersion());
        assertNotNull(response.runId());
    }

    // ---- level 2 and 3: javac, then the factory ------------------------------------------

    /**
     * Every entity artifact of the answer compiles in the file and package Java demands of
     * it, and Hibernate builds a factory over the loaded classes and maps both entities -
     * the two-part key with its {@code @IdClass} among them.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedEntitiesCompileAndHibernateMapsThem(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("entities")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            ClassLoader loader = project.compileAndLoad();

            try (SessionFactory factory = HibernateBootstrap.build("none", loader, project.loadAll())) {
                Set<String> mapped = factory.getMetamodel().getEntities().stream()
                        .map(jakarta.persistence.metamodel.EntityType::getName)
                        .collect(Collectors.toSet());

                assertEquals(Set.of("Product", "CustomerOrder"), mapped);
            }
        }
    }

    /**
     * The query branch of the same two levels (decision 027): the generated method compiles
     * beside the entities once the harness has put the class and the imports around it, and
     * the bare JPQL is translated against the metamodel without being run.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedQueryCompilesAndTheJpqlIsTranslated(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);
        String entityPackage = packageOf(response);

        try (JavaProject project = JavaProject.create("query")) {
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

            try (SessionFactory factory = HibernateBootstrap.build("none", loader, mapped);
                 Session session = factory.openSession()) {
                assertNotNull(session.createSelectionQuery(jpql, Object.class), jpql);
            }
        }
    }

    // ---- level 4: the generated entity against the database ------------------------------

    /**
     * The generated entity is used: a product is stored through it and read back with the
     * same identity, in a transaction rolled back at the end - the fixture's rule for a
     * writing test. The entity types exist only at run time, so the test reaches them by
     * reflection, which stands for the reference a consumer project would have at compile
     * time (the {@code dynamic} of the .NET scenarios).
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void aProductIsStoredAndReadBackThroughTheGeneratedEntity(Scenario scenario) throws Exception {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("persistence")) {
            String product = project.add(entityArtifact(response, "Product"));
            ClassLoader loader = project.compileAndLoad();
            Class<?> productClass = project.load(product);

            try (SessionFactory factory = HibernateBootstrap.build("none", loader, List.of(productClass));
                 Session session = factory.openSession()) {
                session.beginTransaction();
                try {
                    Object row = productClass.getConstructor().newInstance();
                    set(row, "ProductId", PRODUCT_ID);
                    set(row, "ProductName", PRODUCT_NAME);
                    set(row, "Sku", "GEN-0001");
                    set(row, "UnitPrice", UNIT_PRICE);
                    set(row, "IsDiscontinued", false);

                    session.persist(row);
                    session.flush();
                    session.clear();

                    Object reloaded = session.find(productClass, PRODUCT_ID);

                    assertNotNull(reloaded, "the stored product was not found by its assigned key");
                    assertEquals(PRODUCT_NAME, get(reloaded, "ProductName"));
                    assertEquals(0, UNIT_PRICE.compareTo((BigDecimal) get(reloaded, "UnitPrice")));
                } finally {
                    session.getTransaction().rollback();
                }
            }
        }
    }

    // ---- the negative half: a level that never says no would prove nothing ---------------

    /**
     * The identifier is the one mapping fact the Jakarta Persistence descriptor marks as
     * required, because an entity without one is refused at bootstrap (§2.4) - so the tool
     * never emits such an artifact and the completeness gate reports a {@code Failure}
     * instead. Taken out of a valid artifact, this is what level 3 saying no looks like;
     * it is the counterpart of the keyless EF Core entity on the .NET side.
     */
    @Test
    void anEntityWithoutItsIdentifierIsRefused() {
        ConversionResponse response = answerFor(Scenario.HIBERNATE);
        String artifact = JavaSources.withoutAnnotation(entityArtifact(response, "Product"), "@Id");

        try (JavaProject project = JavaProject.create("no-id")) {
            String product = project.add(artifact);
            ClassLoader loader = project.compileAndLoad();
            List<Class<?>> entities = List.of(project.load(product));

            Throwable refusal = assertThrows(
                    RuntimeException.class,
                    () -> HibernateBootstrap.build("none", loader, entities).close(),
                    "Hibernate accepted an entity with no identifier");

            assertTrue(chain(refusal).contains("Product"),
                    "the refusal does not name the entity: " + chain(refusal));
        }
    }

    /**
     * The artifact declares no constructor, which is how it keeps the implicit no-arg one
     * Jakarta Persistence 3.2 §2.1 requires (decision 037). Hibernate 7.4.5 does not check
     * that while it builds the factory - measured here, 2026-09-18, against the assumption
     * of decision 078 that the factory would refuse it. What it does do is fail the moment
     * it has to instantiate the entity, which is any read: so the enforced member is real,
     * and the level that catches its absence is the fourth, not the third.
     */
    @Test
    void anEntityThatLostItsNoArgConstructorFailsOnTheReadThatInstantiatesIt() throws Exception {
        ConversionResponse response = answerFor(Scenario.HIBERNATE);
        String artifact = JavaSources.withDeclaredConstructor(entityArtifact(response, "Product"));

        try (JavaProject project = JavaProject.create("no-constructor")) {
            String product = project.add(artifact);
            ClassLoader loader = project.compileAndLoad();
            Class<?> productClass = project.load(product);

            try (SessionFactory factory = HibernateBootstrap.build("none", loader, List.of(productClass));
                 Session session = factory.openSession()) {
                session.beginTransaction();
                try {
                    Object row = productClass.getConstructor(int.class).newInstance(0);
                    set(row, "ProductId", PRODUCT_ID);
                    set(row, "ProductName", PRODUCT_NAME);
                    set(row, "Sku", "GEN-0002");
                    set(row, "UnitPrice", UNIT_PRICE);
                    set(row, "IsDiscontinued", false);

                    session.persist(row);
                    session.flush();
                    session.clear();

                    Throwable refusal = assertThrows(
                            RuntimeException.class,
                            () -> session.find(productClass, PRODUCT_ID),
                            "Hibernate read an entity it cannot instantiate");

                    assertTrue(chain(refusal).contains("Product"),
                            "the failure does not name the entity: " + chain(refusal));
                } finally {
                    session.getTransaction().rollback();
                }
            }
        }
    }

    /**
     * The key class must define {@code equals} (§2.4, decision 006) - identity of a
     * composite key is decided by value - and the artifact writes it. Hibernate 7.4.5 does
     * not require it: measured here, 2026-09-18, the factory is built over a key class
     * without it. The member is therefore in the artifact because the specification asks
     * for it and because a consumer's own code compares such keys, not because the
     * implementation would notice. Stated as measured behaviour, so a release that starts
     * checking shows up here rather than nowhere.
     */
    @Test
    void equalsOnTheKeyClassIsNotRequiredAtBootstrap() {
        assertAcceptedAtBootstrap(
                "no-equals",
                JavaSources.withoutMember(
                        entityArtifact(answerFor(Scenario.HIBERNATE), "CustomerOrder"),
                        "public boolean equals(Object"));
    }

    /**
     * And neither is {@code @IdClass} itself: Hibernate 7.4.5 accepts two {@code @Id}
     * attributes without a key class named at all (measured 2026-09-18). The annotation is
     * in the artifact because the specification requires it of a portable mapping, which
     * is what decision 040 hands over - not because this implementation would object.
     */
    @Test
    void theIdClassIsNotRequiredAtBootstrap() {
        assertAcceptedAtBootstrap(
                "no-idclass",
                JavaSources.withoutAnnotation(
                        entityArtifact(answerFor(Scenario.HIBERNATE), "CustomerOrder"), "@IdClass("));
    }

    private static void assertAcceptedAtBootstrap(String name, String artifact) {
        try (JavaProject project = JavaProject.create(name)) {
            String type = project.add(artifact);
            ClassLoader loader = project.compileAndLoad();

            try (SessionFactory factory =
                         HibernateBootstrap.build("none", loader, List.of(project.load(type)))) {
                assertNotNull(factory, artifact);
            }
        }
    }

    // ---- the exchange, and reaching a class that exists only at run time -----------------

    /**
     * The answer of one scenario, asked for once. Two things are settled here rather than
     * in every test: the status code, and a {@code Failure} record - the tool refused the
     * artifact (decision 070), and compiling a refused artifact would claim nothing.
     */
    private static ConversionResponse answerFor(Scenario scenario) {
        return ANSWERS.computeIfAbsent(scenario, s -> {
            ToolResponse answer = ToolApi.convert(s.sourceOrm, Orm.HIBERNATE, s.units());

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
