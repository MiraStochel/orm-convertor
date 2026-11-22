package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.JoinTable;
import jakarta.persistence.ManyToMany;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.hibernate.resource.jdbc.spi.StatementInspector;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * A table or a column named like a reserved word of T-SQL - {@code User}, {@code Rule},
 * {@code Key}, {@code Order} - under Hibernate 7.4.5 against SQL Server. Written bare in
 * {@code @Table} or {@code @Column}, the name reaches the SQL bare and the server refuses the
 * statement; Hibernate quotes no keyword by itself. Written as the specification's delimited
 * identifier ({@code "\"User\""}, Jakarta Persistence 3.2 section 2.13) it reaches the SQL in
 * the dialect's brackets in every statement - a read, a join along the reference, a join
 * through a join table, an insert -, a join column's {@code referencedColumnName} and a unique
 * constraint's column name in the same spelling match the column, and Hibernate's own backticks
 * read the same. The other half of the measurement is the EclipseLink test of the same name.
 */
@Tag("integration")
class ReservedSqlNameClaimsTest {

    @Entity(name = "RsBareTableUser")
    @Table(name = "User")
    public static class RsBareTableUser {
        @Id
        @Column(name = "\"Key\"")
        private Integer id;

        @Column(name = "Login")
        private String login;
    }

    @Entity(name = "RsBareColumnUser")
    @Table(name = "\"User\"")
    public static class RsBareColumnUser {
        @Id
        @Column(name = "Key")
        private Integer id;

        @Column(name = "Login")
        private String login;
    }

    @Entity(name = "RsUser")
    @Table(name = "\"User\"")
    public static class RsUser {
        @Id
        @Column(name = "\"Key\"")
        private Integer id;

        @Column(name = "Login")
        private String login;

        @ManyToMany
        @JoinTable(name = "\"Plan\"", joinColumns = { @JoinColumn(name = "\"Key\"") }, inverseJoinColumns = { @JoinColumn(name = "\"Rule\"") })
        private List<RsRule> plans;
    }

    @Entity(name = "RsRule")
    @Table(name = "\"Rule\"", uniqueConstraints = @UniqueConstraint(columnNames = {"\"Order\""}))
    public static class RsRule {
        @Id
        @Column(name = "RuleId")
        private Integer id;

        @ManyToOne
        @JoinColumn(name = "\"Order\"", referencedColumnName = "\"Key\"")
        private RsUser user;
    }

    @Entity(name = "RsBacktickedUser")
    @Table(name = "`User`")
    public static class RsBacktickedUser {
        @Id
        @Column(name = "`Key`")
        private Integer id;

        @Column(name = "Login")
        private String login;
    }

    public static final class Seen implements StatementInspector {
        static final List<String> SQL = new ArrayList<>();

        @Override
        public String inspect(String sql) {
            SQL.add(sql);
            return sql;
        }
    }

    /** Users 1 and 2; rules 10 and 11 over them, rule 12 over none; plans of user 1 rules 10 and 11, of user 2 rule 12. */
    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".[User] ([Key] int PRIMARY KEY, Login nvarchar(20) NOT NULL)");
            statement.execute("CREATE TABLE " + schema + ".[Rule] (RuleId int PRIMARY KEY, [Order] int NULL "
                    + "REFERENCES " + schema + ".[User] ([Key]))");
            statement.execute("CREATE TABLE " + schema + ".[Plan] ([Key] int NOT NULL REFERENCES " + schema + ".[User] ([Key]), "
                    + "[Rule] int NOT NULL REFERENCES " + schema + ".[Rule] (RuleId))");
            statement.execute("INSERT INTO " + schema + ".[User] VALUES (1, 'ann'), (2, 'bob')");
            statement.execute("INSERT INTO " + schema + ".[Rule] VALUES (10, 1), (11, 2), (12, NULL)");
            statement.execute("INSERT INTO " + schema + ".[Plan] VALUES (1, 10), (1, 11), (2, 12)");
        }
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        TestSchema.drop();
    }

    @Test
    void aBareTableNameThatIsAReservedWordIsRefusedByTheServer() {
        try (SessionFactory factory = HibernateBootstrap.build("none", RsBareTableUser.class);
             Session session = factory.openSession()) {
            RuntimeException refused = assertThrows(RuntimeException.class,
                    () -> session.createSelectionQuery("select u.login from RsBareTableUser u", Object.class).getResultList());
            assertTrue(messages(refused).contains("keyword 'User'"), messages(refused));
        }
    }

    @Test
    void aBareColumnNameThatIsAReservedWordIsRefusedByTheServer() {
        try (SessionFactory factory = HibernateBootstrap.build("none", RsBareColumnUser.class);
             Session session = factory.openSession()) {
            RuntimeException refused = assertThrows(RuntimeException.class,
                    () -> session.createSelectionQuery("select u.id from RsBareColumnUser u", Object.class).getResultList());
            assertTrue(messages(refused).contains("keyword 'Key'"), messages(refused));
        }
    }

    @Test
    void delimitedNamesReachTheSqlInBrackets() {
        Map<String, String> settings = Map.of("hibernate.session_factory.statement_inspector", Seen.class.getName());
        try (SessionFactory factory = HibernateBootstrap.build("none", settings, RsUser.class, RsRule.class);
             Session session = factory.openSession()) {
            Seen.SQL.clear();
            assertEquals(List.of("1,ann", "2,bob"),
                    rows(session, "select u.id, u.login from RsUser u order by u.id"));
            assertTrue(Seen.SQL.get(0).contains(".[User] "), Seen.SQL.get(0));
            assertTrue(Seen.SQL.get(0).contains(".[Key]"), Seen.SQL.get(0));
        }
    }

    @Test
    void aJoinColumnAndItsReferencedColumnDelimitedMatchTheColumn() {
        try (SessionFactory factory = HibernateBootstrap.build("none", RsUser.class, RsRule.class);
             Session session = factory.openSession()) {
            assertEquals(List.of("10,ann", "11,bob", "12,null"),
                    rows(session, "select r.id, u.login from RsRule r left join r.user u order by r.id"));
        }
    }

    @Test
    void aJoinTableAndItsColumnsDelimitedAreRead() {
        try (SessionFactory factory = HibernateBootstrap.build("none", RsUser.class, RsRule.class);
             Session session = factory.openSession()) {
            assertEquals(List.of("ann,10", "ann,11", "bob,12"),
                    rows(session, "select u.login, r.id from RsUser u join u.plans r order by r.id"));
        }
    }

    @Test
    void anInsertWritesTheDelimitedNames() {
        Map<String, String> settings = Map.of("hibernate.session_factory.statement_inspector", Seen.class.getName());
        try (SessionFactory factory = HibernateBootstrap.build("none", settings, RsUser.class, RsRule.class);
             Session session = factory.openSession()) {
            var transaction = session.beginTransaction();
            try {
                RsRule rule = new RsRule();
                rule.id = 13;
                rule.user = session.find(RsUser.class, 2);
                Seen.SQL.clear();
                session.persist(rule);
                session.flush();
                String insert = Seen.SQL.stream().filter(s -> s.startsWith("insert")).findFirst().orElseThrow();
                assertTrue(insert.contains(".[Rule] ") && insert.contains("[Order]"), insert);
                assertEquals(List.of("11,bob", "13,bob"),
                        rows(session, "select r.id, u.login from RsRule r join r.user u where u.id = 2 order by r.id"));
            } finally {
                transaction.rollback();
            }
        }
    }

    @Test
    void backtickedNamesAreReadAsWell() {
        try (SessionFactory factory = HibernateBootstrap.build("none", RsBacktickedUser.class);
             Session session = factory.openSession()) {
            assertEquals(List.of("1,ann", "2,bob"),
                    rows(session, "select u.id, u.login from RsBacktickedUser u order by u.id"));
        }
    }

    private static List<String> rows(Session session, String hql) {
        List<String> rows = new ArrayList<>();
        for (Object row : session.createSelectionQuery(hql, Object[].class).getResultList()) {
            Object[] values = (Object[]) row;
            rows.add(values[0] + "," + values[1]);
        }
        return rows;
    }

    /** The messages of the exception and its causes, which is where the server's own words stand. */
    private static String messages(Throwable thrown) {
        StringBuilder text = new StringBuilder();
        for (Throwable each = thrown; each != null; each = each.getCause()) {
            text.append(each.getMessage()).append(" | ");
        }
        return text.toString();
    }
}
