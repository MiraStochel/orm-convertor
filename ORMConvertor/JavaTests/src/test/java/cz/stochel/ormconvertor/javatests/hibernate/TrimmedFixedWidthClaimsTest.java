package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.AttributeConverter;
import jakarta.persistence.Column;
import jakarta.persistence.Convert;
import jakarta.persistence.Converter;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.hibernate.annotations.ColumnTransformer;
import org.hibernate.resource.jdbc.spi.StatementInspector;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How Hibernate 7.4.5 reads a fixed-width value the source's framework - EclipseLink - handed
 * its program trimmed (decision 169). Hibernate hands it over padded as JDBC does; the artifact
 * nests a converter that cuts the trailing characters up to U+0020 as EclipseLink does, and
 * Hibernate applies it - without the unit listing it - wherever the attribute is read: the
 * entity, the attribute as a path, a maximum over it. It leaves the conditions to the database.
 * The alternative the decision rejects, {@code @ColumnTransformer(read = "rtrim(…)")}, trims as
 * well but rewrites the conditions into another predicate.
 */
@Tag("integration")
class TrimmedFixedWidthClaimsTest {

    @Entity(name = "TfTrimmed")
    @Table(name = "TfFlags")
    public static class TfTrimmed {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "code", length = 5, columnDefinition = "char(5)")
        @Convert(converter = TfTrimmed.TrimmedFixedWidth.class)
        private String code;

        @Converter
        public static class TrimmedFixedWidth implements AttributeConverter<String, String> {
            @Override
            public String convertToDatabaseColumn(String value) {
                return value;
            }

            @Override
            public String convertToEntityAttribute(String value) {
                if (value == null) {
                    return null;
                }

                int end = value.length();
                while (end > 0 && value.charAt(end - 1) <= ' ') {
                    end--;
                }

                return value.substring(0, end);
            }
        }
    }

    @Entity(name = "TfTransformed")
    @Table(name = "TfFlags")
    public static class TfTransformed {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "code")
        @ColumnTransformer(read = "rtrim(code)")
        private String code;
    }

    public static final class Seen implements StatementInspector {
        static final List<String> SQL = new ArrayList<>();

        @Override
        public String inspect(String sql) {
            SQL.add(sql);
            return sql;
        }
    }

    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".TfFlags (id int PRIMARY KEY, code char(5) NULL)");
            statement.execute("INSERT INTO " + schema + ".TfFlags VALUES (1, 'YES'), (2, NULL), (3, 'A' + CHAR(9))");
        }
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        TestSchema.drop();
    }

    @Test
    void theNestedConverterTrimsAsEclipseLinkDoes() {
        try (SessionFactory factory = HibernateBootstrap.build("none", Map.of(), TfTrimmed.class);
             Session session = factory.openSession()) {
            assertEquals("YES", session.find(TfTrimmed.class, 1).code);
            assertEquals("A", session.find(TfTrimmed.class, 3).code, "the tab goes with the spaces, as in EclipseLink");
            session.clear();
            assertEquals("YES", session.createQuery("select f from TfTrimmed f where f.id = 1", TfTrimmed.class).getSingleResult().code);
            assertEquals("YES", session.createQuery("select f.code from TfTrimmed f where f.id = 1", Object.class).getSingleResult());
            assertEquals("YES", session.createQuery("select max(f.code) from TfTrimmed f where f.id in (1, 2)", Object.class).getSingleResult());
        }
    }

    @Test
    void theConverterLeavesTheConditionToTheDatabase() {
        Map<String, String> settings = Map.of("hibernate.session_factory.statement_inspector", Seen.class.getName());
        try (SessionFactory factory = HibernateBootstrap.build("none", settings, TfTrimmed.class);
             Session session = factory.openSession()) {
            Seen.SQL.clear();
            assertEquals(1L, session.createQuery("select count(f) from TfTrimmed f where f.code = :code", Long.class).setParameter("code", "YES").getSingleResult());
            assertTrue(Seen.SQL.get(0).matches(".* where \\w+\\.code=\\?$"), Seen.SQL.get(0));
        }
    }

    /** The rejected alternative: it trims, but the condition becomes rtrim(code) = ?. */
    @Test
    void aColumnTransformerRewritesTheCondition() {
        Map<String, String> settings = Map.of("hibernate.session_factory.statement_inspector", Seen.class.getName());
        try (SessionFactory factory = HibernateBootstrap.build("none", settings, TfTransformed.class);
             Session session = factory.openSession()) {
            assertEquals("YES", session.find(TfTransformed.class, 1).code);
            Seen.SQL.clear();
            session.createQuery("select count(f) from TfTransformed f where f.code = :code", Long.class).setParameter("code", "YES").getSingleResult();
            assertTrue(Seen.SQL.get(0).matches(".* where rtrim\\(\\w+\\.code\\)=\\?$"), Seen.SQL.get(0));
        }
    }

    /** The spelling of TrimEnd in JPQL is the database's rtrim. */
    @Test
    void aTrailingTrimIsRtrim() {
        Map<String, String> settings = Map.of("hibernate.session_factory.statement_inspector", Seen.class.getName());
        try (SessionFactory factory = HibernateBootstrap.build("none", settings, TfTrimmed.class);
             Session session = factory.openSession()) {
            Seen.SQL.clear();
            assertEquals("YES", session.createQuery("select trim(trailing from f.code) from TfTrimmed f where f.id = 1", Object.class).getSingleResult());
            assertTrue(Seen.SQL.get(0).matches("^select rtrim\\(\\w+\\.code\\) .*"), Seen.SQL.get(0));
        }
    }
}
