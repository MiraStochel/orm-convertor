package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.List;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What Hibernate 7.4.5 does with a CASE whose ELSE holds another CASE - the other half of the
 * measurement of 2026-11-01 behind the EclipseLink test of the same name: Hibernate writes the
 * nested CASE as it stands ({@code case when … else case when … end end} in its SQL) and
 * answers the rows the nesting means, directly in the ELSE, three levels deep and under
 * coalesce alike, so the builder keeps the nesting for Hibernate (profile
 * {@code MovesTheOuterBranchesIntoACaseInElse} off; decision 201).
 */
@Tag("integration")
class NestedCaseClaimsTest {

    @Entity(name = "NcValue")
    @Table(name = "NcValues")
    public static class NcValue {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "price")
        private Integer price;
    }

    static final String FROM = " from NcValue v order by v.id";

    private static SessionFactory factory;
    private static Session session;

    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".NcValues (id int PRIMARY KEY, price int NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".NcValues VALUES (1, 1), (2, 2), (3, 3), (4, 4)");
        }

        factory = HibernateBootstrap.build("none", NcValue.class);
        session = factory.openSession();
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        if (session != null) {
            session.close();
        }
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** A CASE in the ELSE keeps the outer branches: the rows are those of the nesting. */
    @Test
    void aCaseInTheElseIsWrittenNested() {
        assertEquals(List.of(0, 0, 2, 3), rows("select case when v.price > 3 then 3 else case when v.price > 2 then 2 else 0 end end" + FROM));
        assertEquals(List.of(0, 1, 2, 3), rows("select case when v.price > 3 then 3 else case when v.price > 2 then 2 else case when v.price > 1 then 1 else 0 end end end" + FROM));
        assertEquals(List.of(0, 0, 2, 3), rows("select case when v.price > 3 then 3 else coalesce(case when v.price > 2 then 2 else 0 end, 7) end" + FROM));
    }

    private static List<Integer> rows(String hql) {
        return session.createQuery(hql, Object.class).getResultList().stream().map(o -> ((Number) o).intValue()).toList();
    }
}
