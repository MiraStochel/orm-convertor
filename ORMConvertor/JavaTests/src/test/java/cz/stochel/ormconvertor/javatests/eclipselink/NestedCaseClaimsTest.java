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
import java.sql.Connection;
import java.sql.Statement;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What EclipseLink 5.0.0 does with a CASE whose ELSE holds another CASE. Measured on
 * 2026-11-01 after the Java half of the limits run of 2026-10-30 (series {@code case}), where
 * every point beyond the first failed at run time: the SQL it writes moves the outer WHEN
 * branches into the inner CASE - {@code CASE ELSE CASE WHEN … WHEN … ELSE ? END END} - and
 * SQL Server refuses the outer CASE without a branch ("Incorrect syntax near the keyword
 * 'ELSE'"), while the JPQL itself is accepted. Parentheses around the inner CASE change
 * nothing, and the same happens wherever the inner CASE stands under the ELSE - in a filter,
 * under coalesce, inside a subquery. A CASE in a THEN, as the operand of a WHEN or beside
 * another in an operation is written as it stands, and so is one CASE with several branches
 * and a subquery without a CASE in the ELSE. The builder therefore writes a CASE directly in
 * the ELSE as one CASE with the inner branches appended, and a CASE deeper under the ELSE
 * goes out in native SQL (profile {@code MovesTheOuterBranchesIntoACaseInElse}, decisions 201
 * and 113). The other half of the measurement is the Hibernate test of the same name.
 *
 * <p>The rows: prices 1 to 4 under the keys 1 to 4, so that every spelling below has one
 * expected answer per row.
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

    private static EntityManagerFactory factory;

    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".NcValues (id int PRIMARY KEY, price int NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".NcValues VALUES (1, 1), (2, 2), (3, 3), (4, 4)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                NestedCaseClaimsTest.class.getClassLoader(),
                null,
                List.of(NcValue.class),
                Map.of("eclipselink.session.customizer", JoinedHavingClaimsTest.SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** One CASE with several branches - the spelling the builder gives a CASE nested in an ELSE - runs and answers by the first branch that holds. */
    @Test
    void oneCaseWithSeveralBranchesRuns() {
        assertEquals(List.of(0, 1, 2, 3), rows("select case when v.price > 3 then 3 when v.price > 2 then 2 when v.price > 1 then 1 else 0 end" + FROM));
    }

    /** A CASE directly in the ELSE is accepted and fails at run time, with or without parentheses, at any depth, in a filter too. */
    @Test
    void aCaseInTheElseLosesTheOuterBranches() {
        assertThrows(PersistenceException.class, () -> rows("select case when v.price > 3 then 3 else case when v.price > 2 then 2 else 0 end end" + FROM));
        assertThrows(PersistenceException.class, () -> rows("select case when v.price > 3 then 3 else (case when v.price > 2 then 2 else 0 end) end" + FROM));
        assertThrows(PersistenceException.class, () -> rows("select case when v.price > 3 then 3 else case when v.price > 2 then 2 else case when v.price > 1 then 1 else 0 end end end" + FROM));
        assertThrows(PersistenceException.class, () -> rows("select v.id from NcValue v where case when v.price > 3 then 3 else case when v.price > 2 then 2 else 0 end end > 1 order by v.id"));
    }

    /** The same under an expression or inside a subquery of the ELSE: the inner CASE need not be the ELSE itself. */
    @Test
    void aCaseDeeperUnderTheElseLosesTheOuterBranchesToo() {
        assertThrows(PersistenceException.class, () -> rows("select case when v.price > 3 then 3 else coalesce(case when v.price > 2 then 2 else 0 end, 7) end" + FROM));
        assertThrows(PersistenceException.class, () -> rows("select case when v.price > 3 then 3 else (select max(case when w.price > 2 then 2 else 0 end) from NcValue w) end" + FROM));
    }

    /** A CASE anywhere else - in a THEN, as the operand of a WHEN, beside another in an operation - is written as it stands, and so is a subquery without a CASE in the ELSE. */
    @Test
    void aCaseOutsideTheElseIsWrittenAsItStands() {
        assertEquals(List.of(0, 1, 2, 2), rows("select case when v.price > 1 then case when v.price > 2 then 2 else 1 end else 0 end" + FROM));
        assertEquals(List.of(0, 0, 3, 3), rows("select case when (case when v.price > 2 then 1 else 0 end) = 1 then 3 else 0 end" + FROM));
        assertEquals(List.of(0, 0, 2, 5), rows("select case when v.price > 3 then 3 else 0 end + case when v.price > 2 then 2 else 0 end" + FROM));
        assertEquals(List.of(4, 4, 4, 3), rows("select case when v.price > 3 then 3 else (select max(w.price) from NcValue w) end" + FROM));
    }

    private static List<Integer> rows(String jpql) {
        EntityManager manager = factory.createEntityManager();
        try {
            return manager.createQuery(jpql).getResultList().stream().map(o -> ((Number) o).intValue()).toList();
        } finally {
            manager.close();
        }
    }
}
