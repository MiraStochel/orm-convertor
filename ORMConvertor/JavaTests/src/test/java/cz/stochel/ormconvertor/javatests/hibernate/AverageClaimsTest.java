package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;
import java.sql.Connection;
import java.sql.ResultSet;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What avg hands over in Hibernate 7.4.5 over SQL Server (decision 161), the facts the JPQL
 * writer's conversion of an average and the reading of Hibernate's avg rest on: over a whole
 * number avg is the average in floating point - Hibernate writes avg(cast(x as float(53))) -,
 * which does not cut the quotient off as SQL's AVG does, and cast into Integer it is cut off
 * again, toward zero as SQL cuts it; over a decimal avg is SQL's AVG handed over as a Double,
 * and cast into BigDecimal(38,6) SQL's decimal of six places, where BigDecimal alone rounds it
 * to two. The same over a window, inside with, in a subquery and in having. Each value is
 * rendered with its class, so that the type is the claim as much as the value.
 */
@Tag("integration")
class AverageClaimsTest {

    @Entity(name = "HbAverage")
    @Table(name = "HbAverages")
    public static class HbAverage {
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

    private static SessionFactory factory;

    /** Group 1: quantities 1 and 2; group 2: 5; group 3: -1 and -2 - prices alike, 10.1234 in group 2. */
    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".HbAverages (id int PRIMARY KEY, grp int NOT NULL, qty int NOT NULL, price decimal(18,4) NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".HbAverages VALUES (1, 1, 1, 1.0000), (2, 1, 2, 2.5000), (3, 2, 5, 10.1234), "
                    + "(4, 3, -1, -1.0000), (5, 3, -2, -2.0000)");
        }

        factory = HibernateBootstrap.build("none", HbAverage.class);
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** SQL's AVG over the whole number, for comparison: the quotient cut off toward zero. */
    @Test
    void sqlCutsTheQuotientOff() throws Exception {
        assertEquals(List.of("1:Integer", "5:Integer", "-1:Integer"),
                sql("SELECT AVG(qty) FROM " + TestDatabase.schemaName() + ".HbAverages GROUP BY grp ORDER BY grp"));
    }

    @Test
    void avgOverAWholeNumberIsInFloatingPoint() {
        assertEquals(List.of("1.5:Double", "5.0:Double", "-1.5:Double"),
                hql("select avg(p.qty) from HbAverage p group by p.grp order by p.grp"));
    }

    @Test
    void castIntoIntegerCutsItOffAsSqlDoes() {
        assertEquals(List.of("1:Integer", "5:Integer", "-1:Integer"),
                hql("select cast(avg(p.qty) as Integer) from HbAverage p group by p.grp order by p.grp"));
        assertEquals(List.of("1:Long", "5:Long", "-1:Long"),
                hql("select cast(avg(p.qty) as Long) from HbAverage p group by p.grp order by p.grp"));
    }

    @Test
    void avgOverADecimalIsADouble() {
        assertEquals(List.of("1.75:Double", "10.1234:Double", "-1.5:Double"),
                hql("select avg(p.price) from HbAverage p group by p.grp order by p.grp"));
    }

    /** SQL's decimal of six places, exactly what SQL Server's AVG over decimal(18,4) hands over. */
    @Test
    void castIntoBigDecimalOfSqlsScaleIsSqlsAverage() throws Exception {
        List<String> expected = List.of("1.750000:BigDecimal", "10.123400:BigDecimal", "-1.500000:BigDecimal");
        assertEquals(expected, sql("SELECT AVG(price) FROM " + TestDatabase.schemaName() + ".HbAverages GROUP BY grp ORDER BY grp"));
        assertEquals(expected, hql("select cast(avg(p.price) as BigDecimal(38,6)) from HbAverage p group by p.grp order by p.grp"));
    }

    /** Without a precision the decimal is numeric(38,2), which rounds the average. */
    @Test
    void bigDecimalWithoutAPrecisionRounds() {
        assertEquals(List.of("1.75:BigDecimal", "10.12:BigDecimal", "-1.50:BigDecimal"),
                hql("select cast(avg(p.price) as BigDecimal) from HbAverage p group by p.grp order by p.grp"));
    }

    @Test
    void theConversionsHoldOverAWindow() {
        assertEquals(List.of("1,1:Integer,1.750000:BigDecimal", "2,1:Integer,1.750000:BigDecimal", "3,5:Integer,10.123400:BigDecimal",
                        "4,-1:Integer,-1.500000:BigDecimal", "5,-1:Integer,-1.500000:BigDecimal"),
                hql("select p.id, cast(avg(p.qty) over (partition by p.grp) as Integer), cast(avg(p.price) over (partition by p.grp) as BigDecimal(38,6))"
                        + " from HbAverage p order by p.id"));
    }

    /** The shape of FIBEN q237: the converted average over a window inside with, read by the query around it. */
    @Test
    void theConversionHoldsInsideWith() {
        assertEquals(List.of("1,1.750000:BigDecimal", "2,1.750000:BigDecimal", "3,10.123400:BigDecimal", "4,-1.500000:BigDecimal", "5,-1.500000:BigDecimal"),
                hql("with a as (select p.id as id, cast(avg(p.price) over (partition by p.grp) as BigDecimal(38,6)) as ap from HbAverage p)"
                        + " select x.id, x.ap from a x order by x.id"));
    }

    /**
     * In having and in a subquery: the average of all five quantities is exactly 1, so only
     * group 2 lies above it as SQL cuts the averages off - in floating point group 1, at 1.5,
     * would too.
     */
    @Test
    void theConversionHoldsInHavingAndInASubquery() {
        assertEquals(List.of("2:Integer"),
                hql("select p.grp from HbAverage p group by p.grp"
                        + " having cast(avg(p.qty) as Integer) > (select cast(avg(q.qty) as Integer) from HbAverage q) order by p.grp"));
        assertEquals(List.of("1:Integer", "2:Integer"),
                hql("select p.grp from HbAverage p group by p.grp having avg(p.qty) > (select avg(q.qty) from HbAverage q) order by p.grp"));
    }

    private static List<String> hql(String hql) {
        try (Session session = factory.openSession()) {
            List<String> rows = new ArrayList<>();
            for (Object row : session.createSelectionQuery(hql, Object.class).getResultList()) {
                rows.add(row instanceof Object[] values ? render(values) : render(row));
            }
            return rows;
        }
    }

    private static List<String> sql(String sql) throws Exception {
        try (Connection connection = TestDatabase.open();
             Statement statement = connection.createStatement();
             ResultSet result = statement.executeQuery(sql)) {
            List<String> rows = new ArrayList<>();
            while (result.next()) {
                rows.add(render(result.getObject(1)));
            }
            return rows;
        }
    }

    /** The id of a row as it stands, every other value with its class. */
    private static String render(Object[] values) {
        StringBuilder text = new StringBuilder(String.valueOf(values[0]));
        for (int i = 1; i < values.length; i++) {
            text.append(',').append(render(values[i]));
        }
        return text.toString();
    }

    private static String render(Object value) {
        return (value instanceof BigDecimal exact ? exact.toPlainString() : String.valueOf(value)) + ":" + value.getClass().getSimpleName();
    }
}
