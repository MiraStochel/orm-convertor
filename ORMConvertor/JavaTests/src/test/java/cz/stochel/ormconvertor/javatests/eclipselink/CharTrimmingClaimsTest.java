package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.List;
import java.util.Map;
import org.eclipse.persistence.sessions.Session;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 reads a fixed-width character column, a stated limit of the
 * EclipseLink target in the subset: the trailing spaces SQL Server pads a {@code char(n)}
 * value with to its width are trimmed on the way in - through JPQL and through a native
 * query alike -, while JDBC and every other framework hand the padded value over. It is a
 * setting of the database platform, {@code shouldTrimStrings}, on by default, so a consumer
 * project that switches it off in its session gets the padding back; no artifact of the
 * tool states the session. A variable-width value that ends in spaces is kept, and so is a
 * value the database computes from the column, because the padding is then part of a text
 * of another type. Measured first by the FIBEN catalog (decision 149, query 63:
 * {@code "YES"} for the {@code "YES  "} of the other five frameworks).
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
            statement.execute("CREATE TABLE " + schema + ".ChFlags (id int PRIMARY KEY, code char(5) NOT NULL, note nvarchar(10) NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".ChFlags VALUES (1, 'YES', N'NO  ')");
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
             ResultSet result = statement.executeQuery("SELECT code, note FROM " + TestDatabase.schemaName() + ".ChFlags WHERE id = 1")) {
            result.next();
            assertEquals("YES  ", result.getString(1));
            assertEquals("NO  ", result.getString(2));
        }
    }

    /** The claim: EclipseLink trims the padding by default, in JPQL and in a native query alike. */
    @Test
    void eclipseLinkTrimsAFixedWidthValueByDefault() {
        assertEquals("YES", value(SuiteSchema.class, CODE));
        assertEquals("YES", nativeValue(SuiteSchema.class, "SELECT code FROM " + TestDatabase.schemaName() + ".ChFlags WHERE id = 1"));
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

    /** The switch is the platform's, set by the consumer project in its session - not by the artifact. */
    @Test
    void thePlatformKeepsThePaddingWhenToldTo() {
        assertEquals("YES  ", value(SuiteSchemaKeepingThePadding.class, CODE));
    }

    private static String value(Class<? extends SessionCustomizer> customizer, String jpql) {
        EntityManagerFactory factory = factory(customizer);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                return (String) manager.createQuery(jpql).getSingleResult();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static String nativeValue(Class<? extends SessionCustomizer> customizer, String sql) {
        EntityManagerFactory factory = factory(customizer);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                return (String) manager.createNativeQuery(sql).getSingleResult();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static EntityManagerFactory factory(Class<? extends SessionCustomizer> customizer) {
        return EclipseLinkBootstrap.build(
                "none",
                CharTrimmingClaimsTest.class.getClassLoader(),
                null,
                List.of(ChFlag.class),
                Map.of("eclipselink.session.customizer", customizer.getName()));
    }
}
