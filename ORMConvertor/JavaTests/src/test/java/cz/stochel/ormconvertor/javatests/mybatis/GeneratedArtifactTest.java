package cz.stochel.ormconvertor.javatests.mybatis;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
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
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.util.Arrays;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

/**
 * Verification levels 2 to 4 over the artifacts the tool generates for MyBatis
 * (decisions 016, 078 and 084) - the third target of this suite, reached through the same
 * product surface as the other two: the suite posts its input files to {@code /convert},
 * compiles the answer with {@code javac}, hands the mapper documents to MyBatis and runs
 * one of the statements against the database.
 *
 * <p>What makes this target different from the two before it is where the halves live. A
 * MyBatis entity becomes a class and a mapper document, and a MyBatis query becomes a
 * method declaration and a second mapper document - and the two halves of a query must
 * <em>not</em> both carry the statement, because MyBatis refuses to build a factory from a
 * project that states one twice. That refusal is the finding decision 068 rests on, and the
 * negative half below is where the framework itself says it.
 */
class GeneratedArtifactTest {

    /** What the level-4 scenario writes; the key is assigned, so the test picks it. */
    private static final int PRODUCT_ID = 990004;
    private static final String PRODUCT_NAME = "Generated mapper widget";
    private static final BigDecimal UNIT_PRICE = new BigDecimal("199.9900");

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

    /** The answer names MyBatis and the release this JVM really loaded (decision 013). */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theAnswerNamesMyBatisAndItsRelease(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        assertEquals(scenario.sourceOrm, response.sourceFramework());
        assertEquals(Orm.MYBATIS, response.targetFramework());
        assertNotNull(response.targetFrameworkVersion());
        assertNotNull(response.runId());
    }

    /**
     * The domain class states nothing about the framework at all - no import, no base
     * class, no annotation - which is the point of MyBatis in the six and the clearest case
     * of the rule that the demands of a target are constraints of the framework rather than
     * facts of the domain.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedDomainClassesCarryNothingOfTheFramework(Scenario scenario) {
        for (ConversionUnit entity : answerFor(scenario).artifactsOf(ContentType.JAVA_ENTITY)) {
            assertFalse(entity.content().contains("import org.apache.ibatis"),
                    "a MyBatis domain class imports nothing from the framework:"
                    + System.lineSeparator() + entity.content());
            assertFalse(entity.content().contains("import jakarta.persistence"),
                    "a MyBatis domain class carries no persistence annotation either:"
                    + System.lineSeparator() + entity.content());
        }
    }

    // ---- level 2 and 3: javac, then the factory ------------------------------------------

    /**
     * Every entity artifact compiles, and MyBatis builds a factory over the mapper
     * documents beside them: the result maps name the compiled classes and the framework
     * resolves them.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedEntitiesCompileAndMyBatisBuildsAFactoryOverTheirMappers(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("mb-entities")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            ClassLoader loader = project.compileAndLoad();
            SqlSessionFactory factory = MyBatisBootstrap.build(loader, entityMappers(response));

            for (Class<?> entity : project.loadAll()) {
                assertNotNull(
                        factory.getConfiguration().getResultMap(entity.getSimpleName()),
                        "MyBatis did not register a result map for " + entity.getSimpleName());
            }
        }
    }

    /**
     * The query branch of the same two levels: the generated method declaration compiles
     * inside the interface its mapper's namespace already names, and MyBatis binds the two
     * together - the statement it builds is the one the document carries.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void theGeneratedQueryCompilesAndItsMapperBindsToIt(Scenario scenario) {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("mb-query")) {
            compileProjectOf(response, project);

            String namespace = MyBatisBootstrap.namespaceOf(queryMapper(response));
            SqlSessionFactory factory = MyBatisBootstrap.build(project.loader(), allMappers(response));

            assertTrue(factory.getConfiguration().hasStatement(namespace + "." + methodNameOf(response)),
                    "MyBatis did not register the statement of " + namespace);
            assertTrue(factory.getConfiguration().hasMapper(project.load(namespace)),
                    "MyBatis did not bind the mapper interface " + namespace);
        }
    }

    // ---- level 4: the generated mapper against the database ------------------------------

    /**
     * The generated pair is used: a row is written through the connection of the session,
     * read back by the generated statement into the generated class, and rolled back. The
     * types exist only at run time, so the test reaches them by reflection.
     */
    @ParameterizedTest
    @EnumSource(Scenario.class)
    void aProductIsReadBackThroughTheGeneratedMapper(Scenario scenario) throws Exception {
        ConversionResponse response = answerFor(scenario);

        try (JavaProject project = JavaProject.create("mb-persistence")) {
            compileProjectOf(response, project);

            SqlSessionFactory factory = MyBatisBootstrap.build(project.loader(), allMappers(response));
            Class<?> mapper = project.load(MyBatisBootstrap.namespaceOf(queryMapper(response)));

            try (SqlSession session = factory.openSession()) {
                insertProduct(session.getConnection());

                Object rows = mapperMethod(mapper).invoke(session.getMapper(mapper));
                List<?> products = (List<?>) rows;

                assertFalse(products.isEmpty(), "the generated statement returned no row");

                Object written = products.stream()
                        .filter(row -> PRODUCT_NAME.equals(read(row, "getProductName")))
                        .findFirst()
                        .orElseThrow(() -> new AssertionError("the written product did not come back"));

                assertEquals(PRODUCT_ID, ((Number) read(written, "getProductId")).intValue());
                assertEquals(0, UNIT_PRICE.compareTo((BigDecimal) read(written, "getUnitPrice")));

                session.rollback(true);
            }
        }
    }

    // ---- the negative half ---------------------------------------------------------------

    /**
     * The finding the MyBatis tutorial produced as an exception rather than as
     * documentation, now over generated artifacts: one statement declared in the annotation
     * and in the XML is a project MyBatis refuses to build a factory from. It is why the
     * builder writes the SQL into the document alone and leaves the method declaration bare
     * (decisions 068 and 084) - and why the tool reports the concurrence as a Failure and
     * not as a Conflict: there is no precedence to apply, because the framework accepts
     * neither.
     */
    @Test
    void aStatementDeclaredTwiceIsRefusedByTheFramework() {
        ConversionResponse response = answerFor(Scenario.HIBERNATE);
        String document = queryMapper(response);
        String namespace = MyBatisBootstrap.namespaceOf(document);

        try (JavaProject project = JavaProject.create("mb-twice")) {
            for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                project.add(entity.content());
            }

            project.add(annotatedMapperInterface(response));
            ClassLoader loader = project.compileAndLoad();

            Throwable refusal = assertThrows(
                    RuntimeException.class,
                    () -> MyBatisBootstrap.build(loader, allMappers(response)),
                    "MyBatis accepted the same statement declared in both forms");

            assertTrue(chain(refusal).contains(namespace),
                    "the refusal does not name the statement: " + chain(refusal));
        }
    }

    // ---- the exchange, and the pieces a scenario is assembled from ------------------------

    private static ConversionResponse answerFor(Scenario scenario) {
        return ANSWERS.computeIfAbsent(scenario, s -> {
            ToolResponse answer = ToolApi.convert(s.sourceOrm, Orm.MYBATIS, s.units());

            assertEquals(200, answer.statusCode(), answer.body());

            ConversionResponse conversion = answer.required();
            assertTrue(conversion.recordsOf(RecordKind.FAILURE).isEmpty(),
                    "The tool refused an artifact of the " + Orm.nameOf(s.sourceOrm)
                    + " scenario:" + System.lineSeparator() + conversion.describeRecords());

            return conversion;
        });
    }

    /** The classes of a whole scenario: the entities, and the interface of the query mapper. */
    private static void compileProjectOf(ConversionResponse response, JavaProject project) {
        for (ConversionUnit entity : response.artifactsOf(ContentType.JAVA_ENTITY)) {
            project.add(entity.content());
        }

        String document = queryMapper(response);
        project.add(JavaSources.wrapMapperInterface(
                JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content()),
                MyBatisBootstrap.interfaceNameOf(document),
                response.artifactOf(ContentType.JAVA_QUERY).content()));

        project.compileAndLoad();
    }

    /** The mapper document carrying the statement - the only one of the answer that does. */
    private static String queryMapper(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream()
                .map(ConversionUnit::content)
                .filter(content -> content.contains("<select"))
                .findFirst()
                .orElseThrow(() -> new AssertionError("The answer carries no mapper with a statement"));
    }

    /** The mapper documents of the entities: every one that carries no statement. */
    private static List<String> entityMappers(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream()
                .map(ConversionUnit::content)
                .filter(content -> !content.contains("<select"))
                .toList();
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

    /**
     * The interface of the negative half: the same declaration with the statement of the
     * document on it as well, which is the project the reading side of the tool refuses.
     */
    private static String annotatedMapperInterface(ConversionResponse response) {
        String declaration = "@org.apache.ibatis.annotations.Select(\"" + statementOf(queryMapper(response)) + "\")"
                + System.lineSeparator() + "    " + response.artifactOf(ContentType.JAVA_QUERY).content();

        return JavaSources.wrapMapperInterface(
                JavaSources.packageOf(response.artifactsOf(ContentType.JAVA_ENTITY).get(0).content()),
                MyBatisBootstrap.interfaceNameOf(queryMapper(response)),
                declaration);
    }

    /**
     * The SQL the document's one statement carries, on a single line and unescaped: the
     * tool writes the text as element content, where the markup characters are entities.
     */
    private static String statementOf(String document) {
        int open = document.indexOf('>', document.indexOf("<select")) + 1;
        int close = document.indexOf("</select>");

        return String.join(" ", document.substring(open, close).strip().split("\\R"))
                .replace("&lt;", "<")
                .replace("&gt;", ">")
                .replace("&amp;", "&")
                .replace("\"", "'");
    }

    /** The one declared method of a generated mapper interface. */
    private static Method mapperMethod(Class<?> mapper) {
        return Arrays.stream(mapper.getDeclaredMethods())
                .findFirst()
                .orElseThrow(() -> new AssertionError("The generated mapper declares no method"));
    }

    private static Object read(Object target, String getter) {
        try {
            return target.getClass().getMethod(getter).invoke(target);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError("The generated class has no " + getter, e);
        }
    }

    /**
     * The row the statement is to find, written on the session's own connection so that the
     * rollback at the end of the session takes it away again.
     */
    private static void insertProduct(Connection connection) throws Exception {
        String sql = "INSERT INTO [" + TestDatabase.schemaName() + "].[Products] "
                + "(ProductId, ProductName, Sku, UnitPrice, IsDiscontinued) VALUES (?, ?, ?, ?, 0)";

        try (PreparedStatement statement = connection.prepareStatement(sql)) {
            statement.setInt(1, PRODUCT_ID);
            statement.setString(2, PRODUCT_NAME);
            statement.setString(3, "GEN-0004");
            statement.setBigDecimal(4, UNIT_PRICE);
            statement.executeUpdate();
        }
    }

    /** Every message of the cause chain - a refusal names the statement deep inside it. */
    private static String chain(Throwable throwable) {
        StringBuilder text = new StringBuilder();
        for (Throwable current = throwable; current != null; current = current.getCause()) {
            text.append(current).append(System.lineSeparator());
        }
        return text.toString();
    }
}
