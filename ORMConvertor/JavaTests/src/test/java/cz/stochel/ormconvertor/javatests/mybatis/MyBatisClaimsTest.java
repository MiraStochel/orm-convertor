package cz.stochel.ormconvertor.javatests.mybatis;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.math.BigDecimal;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.util.Arrays;
import java.util.List;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/**
 * The three claims of decision 084 that xUnit cannot verify and the Java suite is to
 * confirm first: that the no-arg constructor MyBatis needs really is enforced, that
 * {@code autoMapping="false"} really closes the mapping, and that a {@code <foreach>} in
 * the form the builder writes really binds a collection. Each of them is a statement about
 * what the framework does with an artifact, so only the framework can answer it.
 *
 * <p>The artifacts are written here by hand in the shape the builder writes them, as the
 * Hibernate and EclipseLink claims tests write theirs: the generated ones reach the suite
 * over HTTP in {@link GeneratedArtifactTest}, and what is under test here is the shape
 * itself rather than one conversion.
 */
class MyBatisClaimsTest {

    private static final int FIRST_ID = 990101;
    private static final int SECOND_ID = 990102;

    /** The domain class in the shape the builder writes it: a POJO that declares no constructor. */
    private static final String GADGET = """
            package Shop;

            import java.math.BigDecimal;

            public class Gadget {

                private Integer ProductId;

                private String ProductName;

                private String Sku;

                private BigDecimal UnitPrice;

                public Integer getProductId() {
                    return ProductId;
                }

                public void setProductId(Integer value) {
                    this.ProductId = value;
                }

                public String getProductName() {
                    return ProductName;
                }

                public void setProductName(String value) {
                    this.ProductName = value;
                }

                public String getSku() {
                    return Sku;
                }

                public void setSku(String value) {
                    this.Sku = value;
                }

                public BigDecimal getUnitPrice() {
                    return UnitPrice;
                }

                public void setUnitPrice(BigDecimal value) {
                    this.UnitPrice = value;
                }
            }
            """;

    private static final String MAPPER_INTERFACE = """
            package Shop;

            import java.util.Collection;
            import java.util.List;
            import org.apache.ibatis.annotations.Param;

            public interface GadgetMapper {

                List<Gadget> findAll();

                List<Gadget> findIn(@Param("ids") Collection<Integer> ids);
            }
            """;

    @BeforeAll
    static void createSchema() throws Exception {
        TestSchema.create();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
    }

    // ---- claim 1: the no-arg constructor is the one enforced member ----------------------

    /**
     * MyBatis demands no base class, no interface and no annotation of a domain class, and
     * the comparison and the tutorial both say so. What it does need is a no-arg
     * constructor, because its {@code ObjectFactory} builds the result row with one - and
     * the artifact keeps it by declaring no constructor at all. Taking that away is what
     * the descriptor's forbidden marker guards against, and this is the framework saying
     * the marker guards against something real.
     */
    @Test
    void aClassWithoutANoArgConstructorCannotMaterializeAResult() throws Exception {
        try (JavaProject project = JavaProject.create("mb-ctor")) {
            project.add(JavaSources.withDeclaredConstructor(GADGET));
            project.add(MAPPER_INTERFACE);
            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, List.of(mapper(closedResultMap())));

            try (SqlSession session = factory.openSession()) {
                try {
                    insert(session.getConnection(), FIRST_ID, "Gadget one", "CLM-0001", "10.0000");

                    Throwable refusal = assertThrows(
                            RuntimeException.class,
                            () -> findAll(session, project),
                            "MyBatis materialized a row into a class with no no-arg constructor");

                    assertTrue(chain(refusal).contains("Gadget"),
                            "the refusal does not name the class: " + chain(refusal));
                } finally {
                    // Always, and forced: a session rolls back by itself only where the write
                    // went through its own update(), and closing one hands the connection back
                    // with autocommit on, which commits whatever is open.
                    session.rollback(true);
                }
            }
        }
    }

    /** The same class as the builder writes it - no constructor - materializes the row. */
    @Test
    void theClassTheBuilderWritesMaterializesTheRow() throws Exception {
        try (JavaProject project = JavaProject.create("mb-ctor-ok")) {
            project.add(GADGET);
            project.add(MAPPER_INTERFACE);
            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, List.of(mapper(closedResultMap())));

            try (SqlSession session = factory.openSession()) {
                try {
                    insert(session.getConnection(), FIRST_ID, "Gadget one", "CLM-0002", "10.0000");

                    List<?> rows = findAll(session, project);

                    assertEquals(1, rows.size());
                    assertEquals("Gadget one", read(rows.get(0), "getProductName"));
                } finally {
                    session.rollback(true);
                }
            }
        }
    }

    // ---- claim 2: autoMapping="false" closes the mapping ---------------------------------

    /**
     * The attribute every generated result map carries, and the reason it does: with
     * automatic mapping on, a column nobody named would still fill a property of the same
     * name, so the meaning of the artifact would depend on a coincidence of names and on a
     * {@code mapUnderscoreToCamelCase} in a configuration the tool never sees. Switched
     * off, the mapping is closed - which is at the same time what makes a property with no
     * column expressible (decision 072). Measured on both sides here: the same select, the
     * same column, one mapping closed and one open.
     */
    @Test
    void aClosedMappingLeavesAColumnItDoesNotNameUnread() throws Exception {
        assertNull(productNameUnder(closedResultMapWithoutName()),
                "autoMapping=\"false\" did not close the mapping");
    }

    @Test
    void anOpenMappingFillsTheSameColumnByItsName() throws Exception {
        assertNotNull(productNameUnder(openResultMapWithoutName()),
                "automatic mapping did not fill the property of the same name");
    }

    private static Object productNameUnder(String resultMap) throws Exception {
        try (JavaProject project = JavaProject.create("mb-automapping")) {
            project.add(GADGET);
            project.add(MAPPER_INTERFACE);
            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, List.of(mapper(resultMap)));

            try (SqlSession session = factory.openSession()) {
                try {
                    insert(session.getConnection(), FIRST_ID, "Gadget one", "CLM-0003", "10.0000");

                    List<?> rows = findAll(session, project);
                    assertEquals(1, rows.size());

                    return read(rows.get(0), "getProductName");
                } finally {
                    session.rollback(true);
                }
            }
        }
    }

    // ---- claim 3: the emitted foreach binds the collection --------------------------------

    /**
     * The one dynamic tag the builder ever writes, in the one form it writes it: MyBatis
     * does not expand a list behind {@code #{}}, so a collection parameter has no other
     * spelling, and the parser reads back exactly this shape (decisions 074, 083 and 084).
     * The claim is that the tag binds the whole list - as many placeholders as there are
     * elements - which no shape of the text alone can show.
     */
    @Test
    void theEmittedForEachBindsEveryElementOfTheCollection() throws Exception {
        try (JavaProject project = JavaProject.create("mb-foreach")) {
            project.add(GADGET);
            project.add(MAPPER_INTERFACE);
            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, List.of(mapper(closedResultMap())));

            try (SqlSession session = factory.openSession()) {
                try {
                    insert(session.getConnection(), FIRST_ID, "Gadget one", "CLM-0004", "10.0000");
                    insert(session.getConnection(), SECOND_ID, "Gadget two", "CLM-0005", "20.0000");

                    Class<?> mapper = project.load("Shop.GadgetMapper");
                    Object instance = session.getMapper(mapper);
                    Method findIn = method(mapper, "findIn");

                    assertEquals(2, ((List<?>) call(findIn, instance, List.of(FIRST_ID, SECOND_ID))).size());
                    assertEquals(1, ((List<?>) call(findIn, instance, List.of(SECOND_ID))).size());
                } finally {
                    session.rollback(true);
                }
            }
        }
    }

    // ---- the artifacts, in the shape the builder writes them -----------------------------

    /** Every property named, which is what a generated mapper carries. */
    private static String closedResultMap() {
        return """
                    <resultMap id="Gadget" type="Shop.Gadget" autoMapping="false">
                        <id column="ProductId" property="ProductId" />
                        <result column="ProductName" property="ProductName" jdbcType="NVARCHAR" />
                        <result column="Sku" property="Sku" jdbcType="VARCHAR" />
                        <result column="UnitPrice" property="UnitPrice" jdbcType="DECIMAL" />
                    </resultMap>
                """;
    }

    /** The same mapping with one property left out - what a transient property looks like. */
    private static String closedResultMapWithoutName() {
        return closedResultMap().replaceAll("(?m)^.*property=\"ProductName\".*\\R", "");
    }

    /** And the same again with the attribute taken away, which is what it guards against. */
    private static String openResultMapWithoutName() {
        return closedResultMapWithoutName().replace(" autoMapping=\"false\"", "");
    }

    /**
     * The document around a result map: the prolog and the DOCTYPE the builder writes, the
     * namespace of the interface, and the two statements this test needs - which the
     * builder writes into a document of their own, because a mapper of an entity carries no
     * statement.
     */
    private static String mapper(String resultMap) {
        String table = "[" + TestDatabase.schemaName() + "].[Products]";

        return """
                <?xml version="1.0" encoding="utf-8" ?>
                <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
                <mapper namespace="Shop.GadgetMapper">
                """
                + resultMap
                + "    <select id=\"findAll\" resultMap=\"Gadget\">" + System.lineSeparator()
                + "        SELECT p.ProductId, p.ProductName, p.Sku, p.UnitPrice FROM " + table + " AS p"
                + System.lineSeparator()
                + "    </select>" + System.lineSeparator()
                + "    <select id=\"findIn\" resultMap=\"Gadget\">" + System.lineSeparator()
                + "        SELECT p.ProductId, p.ProductName, p.Sku, p.UnitPrice FROM " + table + " AS p"
                + System.lineSeparator()
                + "        WHERE p.ProductId IN "
                + "<foreach item=\"item\" collection=\"ids\" open=\"(\" separator=\",\" close=\")\">#{item}</foreach>"
                + System.lineSeparator()
                + "    </select>" + System.lineSeparator()
                + "</mapper>" + System.lineSeparator();
    }

    // ---- reaching classes that exist only at run time -------------------------------------

    private static List<?> findAll(SqlSession session, JavaProject project) {
        Class<?> mapper = project.load("Shop.GadgetMapper");
        return (List<?>) call(method(mapper, "findAll"), session.getMapper(mapper));
    }

    /**
     * Calls a method of a type that exists only at run time, with the wrapper reflection
     * puts around a failure taken off: what a claim is about is what the framework threw,
     * not that it was reached through {@code Method.invoke}.
     */
    private static Object call(Method method, Object target, Object... arguments) {
        try {
            return method.invoke(target, arguments);
        } catch (InvocationTargetException e) {
            Throwable cause = e.getCause();
            throw cause instanceof RuntimeException runtime ? runtime : new IllegalStateException(cause);
        } catch (IllegalAccessException e) {
            throw new AssertionError("The generated mapper is not accessible.", e);
        }
    }

    private static Method method(Class<?> mapper, String name) {
        return Arrays.stream(mapper.getDeclaredMethods())
                .filter(m -> m.getName().equals(name))
                .findFirst()
                .orElseThrow(() -> new AssertionError("The mapper declares no " + name));
    }

    private static Object read(Object target, String getter) {
        try {
            return target.getClass().getMethod(getter).invoke(target);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError("The class has no " + getter, e);
        }
    }

    private static void insert(Connection connection, int id, String name, String sku, String price)
            throws Exception {
        String sql = "INSERT INTO [" + TestDatabase.schemaName() + "].[Products] "
                + "(ProductId, ProductName, Sku, UnitPrice, IsDiscontinued) VALUES (?, ?, ?, ?, 0)";

        try (PreparedStatement statement = connection.prepareStatement(sql)) {
            statement.setInt(1, id);
            statement.setString(2, name);
            statement.setString(3, sku);
            statement.setBigDecimal(4, new BigDecimal(price));
            statement.executeUpdate();
        }
    }

    private static String chain(Throwable throwable) {
        StringBuilder text = new StringBuilder();
        for (Throwable current = throwable; current != null; current = current.getCause()) {
            text.append(current).append(System.lineSeparator());
        }
        return text.toString();
    }
}
