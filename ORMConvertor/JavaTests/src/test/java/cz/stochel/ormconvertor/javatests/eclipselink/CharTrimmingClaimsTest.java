package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.AttributeConverter;
import jakarta.persistence.Column;
import jakarta.persistence.Convert;
import jakarta.persistence.Converter;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.Query;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.List;
import java.util.Map;
import org.eclipse.persistence.jpa.JpaQuery;
import org.eclipse.persistence.sessions.Session;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 reads a fixed-width character column - the facts decision 169 rests
 * on. Under its default session (the platform's {@code shouldTrimStrings}) it cuts the
 * trailing characters up to U+0020 off every result column of type {@code CHAR} or
 * {@code NCHAR} - a column, a MIN or MAX, a COALESCE or a CASE over one -, through JPQL and a
 * native query alike, while JDBC and every other framework hand the padded value over. A
 * variable-width value is kept, and so is a value the database computes from the column, a
 * concatenation with empty text among them: the builder writes the select list of an
 * EclipseLink query that way, in JPQL and in native SQL. The standard {@code cast(x as String)}
 * is no way out, SQL Server refuses it. The entity side is a converter nested in the entity
 * that pads the value back; EclipseLink finds it only among the classes of the persistence
 * unit. Measured first by the FIBEN catalog (decision 149, query 63: {@code "YES"} for the
 * {@code "YES  "} of the other five frameworks).
 */
@Tag("integration")
class CharTrimmingClaimsTest {

    @Entity(name = "ChFlag")
    @Table(name = "ChFlags")
    public static class ChFlag {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "code", columnDefinition = "char(5)")
        private String code;

        @Column(name = "note", columnDefinition = "nvarchar(10)")
        private String note;

        @Column(name = "wide", columnDefinition = "nchar(5)")
        private String wide;
    }

    /** The entity as the artifact writes it into EclipseLink: the converter nested, padding the value back to its width. */
    @Entity(name = "ChPadded")
    @Table(name = "ChFlags")
    public static class ChPadded {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "code", length = 5, columnDefinition = "char(5)")
        @Convert(converter = ChPadded.FixedWidth5.class)
        private String code;

        @Converter
        public static class FixedWidth5 implements AttributeConverter<String, String> {
            @Override
            public String convertToDatabaseColumn(String value) {
                return value;
            }

            @Override
            public String convertToEntityAttribute(String value) {
                return value == null ? null : String.format("%-5s", value);
            }
        }
    }

    /** A converter the entity does not nest, so the unit does not list it. */
    @Converter
    public static class Unlisted implements AttributeConverter<String, String> {
        @Override
        public String convertToDatabaseColumn(String value) {
            return value;
        }

        @Override
        public String convertToEntityAttribute(String value) {
            return value;
        }
    }

    @Entity(name = "ChUnlisted")
    @Table(name = "ChFlags")
    public static class ChUnlisted {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "code")
        @Convert(converter = Unlisted.class)
        private String code;
    }

    /** The hand-written entity names no schema, so the session qualifies its table with the suite's. */
    public static class SuiteSchema implements SessionCustomizer {
        @Override
        public void customize(Session session) {
            session.getLogin().setTableQualifier(TestDatabase.schemaName());
        }
    }

    /** The same, with the platform told to keep the padding - what a consumer project would set. */
    public static final class SuiteSchemaKeepingThePadding extends SuiteSchema {
        @Override
        public void customize(Session session) {
            super.customize(session);
            session.getLogin().getPlatform().setShouldTrimStrings(false);
        }
    }

    private static final String CODE = "select f.code from ChFlag f where f.id = 1";

    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".ChFlags (id int PRIMARY KEY, code char(5) NULL, note nvarchar(10) NOT NULL, wide nchar(5) NULL)");
            statement.execute("INSERT INTO " + schema + ".ChFlags VALUES "
                    + "(1, 'YES', N'NO  ', N'ANO'), "
                    + "(2, NULL, N'none', NULL), "
                    + "(3, 'A' + CHAR(9), N'tab', N'A' + NCHAR(9)), "
                    + "(4, 'A' + CHAR(160), N'nbsp', N'A' + NCHAR(12288))");
        }
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        TestSchema.drop();
    }

    /** What the database holds and JDBC returns: the fixed-width value padded to its width, the other as written. */
    @Test
    void jdbcReturnsThePaddedValue() throws Exception {
        try (Connection connection = TestDatabase.open();
             Statement statement = connection.createStatement();
             ResultSet result = statement.executeQuery("SELECT code, note, RTRIM(code) FROM " + TestDatabase.schemaName() + ".ChFlags WHERE id IN (1, 3) ORDER BY id")) {
            result.next();
            assertEquals("YES  ", result.getString(1));
            assertEquals("NO  ", result.getString(2));
            result.next();
            assertEquals("A\t   ", result.getString(1));
            assertEquals("A\t", result.getString(3), "RTRIM cuts spaces only");
        }
    }

    /** The claim: EclipseLink trims the padding by default, in JPQL and in a native query alike. */
    @Test
    void eclipseLinkTrimsAFixedWidthValueByDefault() {
        assertEquals("YES", value(SuiteSchema.class, CODE));
        assertEquals("YES", nativeValue(SuiteSchema.class, "SELECT code FROM " + TestDatabase.schemaName() + ".ChFlags WHERE id = 1"));
    }

    /** It cuts every character up to U+0020, the tab too, and leaves the spaces above it. */
    @Test
    void eclipseLinkTrimsEveryCharacterUpToTheSpace() {
        assertEquals("A", value(SuiteSchema.class, "select f.code from ChFlag f where f.id = 3"));
        assertEquals("A\u00A0", value(SuiteSchema.class, "select f.code from ChFlag f where f.id = 4"));
        assertEquals("A", value(SuiteSchema.class, "select f.wide from ChFlag f where f.id = 3"));
        assertEquals("A\u3000", value(SuiteSchema.class, "select f.wide from ChFlag f where f.id = 4"));
    }

    /** It goes by the type of the result column: whatever comes back as CHAR or NCHAR is trimmed. */
    @Test
    void eclipseLinkTrimsEveryFixedWidthResult() {
        assertEquals("YES", value(SuiteSchema.class, "select max(f.code) from ChFlag f where f.id in (1, 2)"));
        assertEquals("YES", value(SuiteSchema.class, "select min(f.code) from ChFlag f where f.id = 1"));
        assertEquals("YES", value(SuiteSchema.class, "select coalesce(f.code, f.code) from ChFlag f where f.id = 1"));
        assertEquals("YES", value(SuiteSchema.class, "select case when f.id = 1 then f.code else f.code end from ChFlag f where f.id = 1"));
        assertEquals("ANO", value(SuiteSchema.class, "select f.wide from ChFlag f where f.id = 1"));
    }

    /** A variable-width value that ends in spaces is not trimmed. */
    @Test
    void aVariableWidthValueKeepsItsTrailingSpaces() {
        assertEquals("NO  ", value(SuiteSchema.class, "select f.note from ChFlag f where f.id = 1"));
    }

    /** A text the database computes from the fixed-width value keeps the padding inside it. */
    @Test
    void aValueComputedInTheDatabaseKeepsThePadding() {
        assertEquals("YES  |", value(SuiteSchema.class, "select concat(f.code, '|') from ChFlag f where f.id = 1"));
    }

    /**
     * The builder's form (decision 169): a concatenation with empty text is the value SQL
     * returns - the padding, the tab, a NULL -, over a maximum, beside a grouping by the
     * column, under DISTINCT and in a member of a union, for a national column too, and in
     * native SQL as the plus of T-SQL. EclipseLink binds the empty text as a parameter.
     */
    @Test
    void aConcatenationWithEmptyTextKeepsThePadding() {
        assertEquals("YES  ", value(SuiteSchema.class, "select concat(f.code, '') as code from ChFlag f where f.id = 1"));
        assertEquals("A\t   ", value(SuiteSchema.class, "select concat(f.code, '') from ChFlag f where f.id = 3"));
        assertNull(value(SuiteSchema.class, "select concat(f.code, '') from ChFlag f where f.id = 2"));
        assertEquals("YES  ", value(SuiteSchema.class, "select concat(max(f.code), '') from ChFlag f where f.id in (1, 2)"));
        assertEquals("ANO  ", value(SuiteSchema.class, "select concat(f.wide, '') from ChFlag f where f.id = 1"));
        assertEquals("YES  ", value(SuiteSchema.class, "select distinct concat(f.code, '') from ChFlag f where f.id = 1"));
        assertEquals("YES  ", value(SuiteSchema.class, "select concat(f.code, '') from ChFlag f where f.id = 1 union select concat(f.code, '') from ChFlag f where f.id = 1"));
        assertEquals("(code + ?)", sqlOf(SuiteSchema.class, "select concat(f.code, '') from ChFlag f where f.id = 1").replaceAll("^SELECT (\\(code \\+ \\?\\)).*$", "$1"));

        List<?> grouped = rows(SuiteSchema.class, "select concat(f.code, ''), count(f) from ChFlag f where f.id = 1 group by f.code");
        assertEquals("YES  ", ((Object[]) grouped.get(0))[0]);

        String schema = TestDatabase.schemaName();
        assertEquals("YES  ", nativeValue(SuiteSchema.class, "SELECT code + '' FROM " + schema + ".ChFlags WHERE id = 1"));
        assertEquals("ANO  ", nativeValue(SuiteSchema.class, "SELECT wide + '' FROM " + schema + ".ChFlags WHERE id = 1"));
        assertNull(nativeValue(SuiteSchema.class, "SELECT code + '' FROM " + schema + ".ChFlags WHERE id = 2"));
    }

    /**
     * Why a projection under DISTINCT that is also the ordering stays as written (decision 169):
     * SQL Server pairs a key of the ordering with the select list by its text, and neither the
     * column beside the concatenation nor the concatenation with its own bound empty text is
     * that text, so the database refuses both, in JPQL and in native SQL alike.
     */
    @Test
    void aDistinctConcatenationOrderedByTheColumnIsRefused() {
        assertThrows(RuntimeException.class, () -> rows(SuiteSchema.class, "select distinct concat(f.code, '') from ChFlag f order by f.code"));
        assertThrows(RuntimeException.class, () -> rows(SuiteSchema.class, "select distinct concat(f.code, '') from ChFlag f order by concat(f.code, '')"));
        assertThrows(RuntimeException.class, () -> nativeValue(SuiteSchema.class, "SELECT DISTINCT f.code + '' FROM " + TestDatabase.schemaName() + ".ChFlags f ORDER BY f.code"));
    }

    /** The standard conversion into text is no way out: EclipseLink hands the type name String to SQL Server, which refuses it. */
    @Test
    void theStandardConversionIntoTextIsRefused() {
        assertThrows(RuntimeException.class, () -> value(SuiteSchema.class, "select cast(f.code as String) from ChFlag f where f.id = 1"));
    }

    /** The spelling of TrimEnd in JPQL is the database's RTRIM, which cuts spaces only. */
    @Test
    void aTrailingTrimIsRtrim() {
        assertEquals("SELECT RTRIM(code) FROM " + TestDatabase.schemaName() + ".ChFlags WHERE (id = ?)",
                sqlOf(SuiteSchema.class, "select trim(trailing from f.code) from ChFlag f where f.id = 1"));
        assertEquals("A\t", value(SuiteSchema.class, "select trim(trailing from f.code) from ChFlag f where f.id = 3"));
    }

    /**
     * The converter the artifact nests pads the value back wherever the entity's attribute is
     * read - the entity, its attribute as a path, a maximum over it -, restores spaces only,
     * leaves the condition to the database, and does not reach a concatenation, which is exact.
     */
    @Test
    void theNestedConverterPadsTheValueBack() {
        EntityManagerFactory factory = factory(SuiteSchema.class, ChPadded.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                assertEquals("YES  ", manager.find(ChPadded.class, 1).code);
                assertEquals("A    ", manager.find(ChPadded.class, 3).code, "the tab is lost, a space comes in its place");
                manager.clear();
                assertEquals("YES  ", ((ChPadded) manager.createQuery("select f from ChPadded f where f.id = 1").getSingleResult()).code);
                assertEquals("YES  ", manager.createQuery("select f.code from ChPadded f where f.id = 1").getSingleResult());
                assertEquals("YES  ", manager.createQuery("select max(f.code) from ChPadded f where f.id in (1, 2)").getSingleResult());
                assertEquals("A\t   ", manager.createQuery("select concat(f.code, '') from ChPadded f where f.id = 3").getSingleResult());
                assertEquals(1L, manager.createQuery("select count(f) from ChPadded f where f.code = :code").setParameter("code", "YES").getSingleResult());
                assertEquals(1L, manager.createQuery("select count(f) from ChPadded f where f.code = :code").setParameter("code", "YES  ").getSingleResult());
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    /** EclipseLink finds a converter only among the classes of the unit, which is why the consumer lists the nested one. */
    @Test
    void aConverterOutsideTheUnitIsNotFound() {
        RuntimeException refused = assertThrows(RuntimeException.class, () -> factory(SuiteSchema.class, ChUnlisted.class).createEntityManager().close());
        assertTrue(String.valueOf(refused.getMessage()).contains("was not found"), refused.getMessage());
    }

    /** The switch is the platform's, set by the consumer project in its session - not by the artifact. */
    @Test
    void thePlatformKeepsThePaddingWhenToldTo() {
        assertEquals("YES  ", value(SuiteSchemaKeepingThePadding.class, CODE));
    }

    /** The same switch as a property of the persistence unit, the one the record of the converter names. */
    @Test
    void theUnitPropertyKeepsThePadding() {
        EntityManagerFactory factory = EclipseLinkBootstrap.build(
                "none",
                CharTrimmingClaimsTest.class.getClassLoader(),
                null,
                List.of(ChFlag.class),
                Map.of("eclipselink.session.customizer", SuiteSchema.class.getName(),
                        "eclipselink.target-database-properties", "shouldTrimStrings=false"));
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                assertEquals("YES  ", manager.createQuery(CODE).getSingleResult());
                assertEquals("YES  ", manager.createNativeQuery("SELECT code FROM " + TestDatabase.schemaName() + ".ChFlags WHERE id = 1").getSingleResult());
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static Object value(Class<? extends SessionCustomizer> customizer, String jpql) {
        List<?> rows = rows(customizer, jpql);
        return rows.isEmpty() ? null : rows.get(0);
    }

    private static List<?> rows(Class<? extends SessionCustomizer> customizer, String jpql) {
        EntityManagerFactory factory = factory(customizer, ChFlag.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                return manager.createQuery(jpql).getResultList();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    /** The SQL EclipseLink writes for the query, read off the query once it has run. */
    private static String sqlOf(Class<? extends SessionCustomizer> customizer, String jpql) {
        EntityManagerFactory factory = factory(customizer, ChFlag.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                Query query = manager.createQuery(jpql);
                query.getResultList();
                return query.unwrap(JpaQuery.class).getDatabaseQuery().getSQLString();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static Object nativeValue(Class<? extends SessionCustomizer> customizer, String sql) {
        EntityManagerFactory factory = factory(customizer, ChFlag.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                return manager.createNativeQuery(sql).getSingleResult();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static EntityManagerFactory factory(Class<? extends SessionCustomizer> customizer, Class<?> entity) {
        return EclipseLinkBootstrap.build(
                "none",
                CharTrimmingClaimsTest.class.getClassLoader(),
                null,
                List.of(entity),
                Map.of("eclipselink.session.customizer", customizer.getName()));
    }
}
