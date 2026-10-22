package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.PersistenceException;
import jakarta.persistence.Table;
import java.math.BigDecimal;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What AVG hands over in EclipseLink 5.0.0 over SQL Server (decision 161), the facts the JPQL
 * writer's conversion of an average for EclipseLink rests on: AVG(x) is SQL's AVG - over a
 * whole number the quotient cut off - handed over as a Double whatever it averages. The type
 * name of a cast reaches SQL Server as written, so the names SQL Server knows convert it back -
 * INTEGER and BIGINT into Integer and Long, DECIMAL(38,6) into SQL's decimal of six places -,
 * in a subquery and in having as well; LONG, the name Jakarta Persistence 3.2 gives a long, it
 * refuses. Written as the JPQL writer writes them, the function names in lower case.
 */
@Tag("integration")
class AverageClaimsTest {

    @Entity(name = "ElAverage")
    @Table(name = "ElAverages")
    public static class ElAverage {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "grp")
        private Integer grp;

        @Column(name = "qty")
        private Integer qty;

        @Column(name = "price", precision = 18, scale = 4)
        private BigDecimal price;
    }

    private static EntityManagerFactory factory;

    /** Group 1: quantities 1 and 2; group 2: 5; group 3: -1 and -2 - prices alike, 10.1234 in group 2. */
    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".ElAverages (id int PRIMARY KEY, grp int NOT NULL, qty int NOT NULL, price decimal(18,4) NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".ElAverages VALUES (1, 1, 1, 1.0000), (2, 1, 2, 2.5000), (3, 2, 5, 10.1234), "
                    + "(4, 3, -1, -1.0000), (5, 3, -2, -2.0000)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                AverageClaimsTest.class.getClassLoader(),
                null,
                List.of(ElAverage.class),
                Map.of("eclipselink.session.customizer", JoinedHavingClaimsTest.SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    @Test
    void avgIsSqlsAverageAsADouble() {
        assertEquals(List.of("1.0:Double", "5.0:Double", "-1.0:Double"),
                jpql("select avg(p.qty) from ElAverage p group by p.grp order by p.grp"));
        assertEquals(List.of("1.75:Double", "10.1234:Double", "-1.5:Double"),
                jpql("select avg(p.price) from ElAverage p group by p.grp order by p.grp"));
    }

    @Test
    void theNamesSqlServerKnowsConvertItBack() {
        assertEquals(List.of("1:Integer", "5:Integer", "-1:Integer"),
                jpql("select cast(avg(p.qty) as INTEGER) from ElAverage p group by p.grp order by p.grp"));
        assertEquals(List.of("1:Long", "5:Long", "-1:Long"),
                jpql("select cast(avg(p.qty) as BIGINT) from ElAverage p group by p.grp order by p.grp"));
        assertEquals(List.of("1.750000:BigDecimal", "10.123400:BigDecimal", "-1.500000:BigDecimal"),
                jpql("select cast(avg(p.price) as DECIMAL(38,6)) from ElAverage p group by p.grp order by p.grp"));
    }

    /** The name Jakarta Persistence gives a long reaches SQL Server, which knows no type LONG. */
    @Test
    void theNameOfTheSpecificationIsRefused() {
        assertThrows(PersistenceException.class,
                () -> jpql("select cast(avg(p.qty) as LONG) from ElAverage p group by p.grp order by p.grp"));
    }

    /** In having and in a subquery: the average of all five quantities is 1, and group 2 alone lies above it. */
    @Test
    void theConversionsHoldInHavingAndInASubquery() {
        assertEquals(List.of("2:Integer"),
                jpql("select p.grp from ElAverage p group by p.grp"
                        + " having cast(avg(p.qty) as INTEGER) > (select cast(avg(q.qty) as INTEGER) from ElAverage q) order by p.grp"));
        assertEquals(List.of("2:Integer"),
                jpql("select p.grp from ElAverage p group by p.grp"
                        + " having cast(avg(p.price) as DECIMAL(38,6)) > (select cast(avg(q.price) as DECIMAL(38,6)) from ElAverage q) order by p.grp"));
    }

    private static List<String> jpql(String jpql) {
        EntityManager manager = factory.createEntityManager();
        try {
            List<String> rows = new ArrayList<>();
            for (Object value : manager.createQuery(jpql).getResultList()) {
                rows.add((value instanceof BigDecimal exact ? exact.toPlainString() : String.valueOf(value)) + ":" + value.getClass().getSimpleName());
            }
            return rows;
        } finally {
            manager.close();
        }
    }
}
