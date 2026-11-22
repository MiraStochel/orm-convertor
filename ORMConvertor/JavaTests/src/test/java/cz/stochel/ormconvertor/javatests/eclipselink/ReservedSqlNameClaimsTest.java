package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.JoinTable;
import jakarta.persistence.ManyToMany;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Query;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.eclipse.persistence.jpa.JpaQuery;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * A table or a column named like a reserved word of T-SQL - {@code User}, {@code Rule},
 * {@code Key}, {@code Order} - under EclipseLink 5.0.0 against SQL Server. Written bare in
 * {@code @Table} or {@code @Column}, the name reaches the SQL bare and the server refuses the
 * statement; EclipseLink quotes no keyword by itself. Written as the specification's delimited
 * identifier ({@code "\"User\""}, Jakarta Persistence 3.2 section 2.13) it reaches the SQL
 * in its double quotes in every statement - a read, a join along the reference, a join through
 * a join table, an insert -, with the session's qualifier delimited beside it, and a join
 * column's {@code referencedColumnName} and a unique constraint's column name in the same
 * spelling match the column. Hibernate's backticks are not the specification's, and EclipseLink
 * takes them for characters of the name. The other half of the measurement is the Hibernate
 * test of the same name.
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
        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> rows("select u.id, u.login from RsBareTableUser u", RsBareTableUser.class));
        assertTrue(messages(refused).contains("keyword 'User'"), messages(refused));
    }

    @Test
    void aBareColumnNameThatIsAReservedWordIsRefusedByTheServer() {
        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> rows("select u.id, u.login from RsBareColumnUser u", RsBareColumnUser.class));
        assertTrue(messages(refused).contains("keyword 'Key'"), messages(refused));
    }

    @Test
    void delimitedNamesReachTheSqlDelimited() {
        EntityManagerFactory factory = factory(RsUser.class, RsRule.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                Query query = manager.createQuery("select u.id, u.login from RsUser u order by u.id");
                assertEquals(List.of("1,ann", "2,bob"), pairs(query.getResultList()));
                String sql = query.unwrap(JpaQuery.class).getDatabaseQuery().getSQLString();
                // The double quote of the specification, and the qualifier of the session delimited with the table.
                assertEquals("SELECT \"Key\", Login FROM \"" + TestDatabase.schemaName() + "\".\"User\" ORDER BY \"Key\"", sql);
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    @Test
    void aJoinColumnAndItsReferencedColumnDelimitedMatchTheColumn() {
        assertEquals(List.of("10,ann", "11,bob", "12,null"),
                rows("select r.id, u.login from RsRule r left join r.user u order by r.id", RsUser.class, RsRule.class));
    }

    @Test
    void aJoinTableAndItsColumnsDelimitedAreRead() {
        assertEquals(List.of("ann,10", "ann,11", "bob,12"),
                rows("select u.login, r.id from RsUser u join u.plans r order by r.id", RsUser.class, RsRule.class));
    }

    @Test
    void anInsertWritesTheDelimitedNames() {
        EntityManagerFactory factory = factory(RsUser.class, RsRule.class);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                manager.getTransaction().begin();
                try {
                    RsRule rule = new RsRule();
                    rule.id = 13;
                    rule.user = manager.find(RsUser.class, 2);
                    manager.persist(rule);
                    manager.flush();
                    assertEquals(List.of("11,bob", "13,bob"), pairs(manager.createQuery(
                            "select r.id, u.login from RsRule r join r.user u where u.id = 2 order by r.id").getResultList()));
                } finally {
                    manager.getTransaction().rollback();
                }
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    @Test
    void backticksAreCharactersOfTheName() {
        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> rows("select u.id, u.login from RsBacktickedUser u", RsBacktickedUser.class));
        assertTrue(messages(refused).contains("`"), messages(refused));
    }

    private static EntityManagerFactory factory(Class<?>... entities) {
        return EclipseLinkBootstrap.build(
                "none",
                ReservedSqlNameClaimsTest.class.getClassLoader(),
                null,
                List.of(entities),
                Map.of("eclipselink.session.customizer", JoinedHavingClaimsTest.SuiteSchema.class.getName()));
    }

    private static List<String> rows(String jpql, Class<?>... entities) {
        EntityManagerFactory factory = factory(entities);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                return pairs(manager.createQuery(jpql).getResultList());
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static List<String> pairs(List<?> result) {
        List<String> rows = new ArrayList<>();
        for (Object row : result) {
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
