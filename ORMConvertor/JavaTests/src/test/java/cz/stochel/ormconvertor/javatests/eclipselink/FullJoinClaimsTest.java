package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The fact of the profile {@code RefusesFullJoin} (decision 180), checked against the release
 * this JVM loads: Jakarta Persistence has the inner and the left join only, and the JPQL
 * parser of EclipseLink 5.0.0 refuses {@code full join} when the query is created, where HQL
 * reads it. The builder therefore sends a query with a full outer join to native SQL
 * (decision 113). Found by the experiment run of 2026-10-24 on a query from Hibernate.
 * Nothing is executed.
 */
@Tag("integration")
class FullJoinClaimsTest {

    @Entity(name = "FjCustomer")
    @Table(name = "FjCustomers")
    public static class FjCustomer {
        @Id
        @Column(name = "id")
        private Integer id;
    }

    @Entity(name = "FjOrder")
    @Table(name = "FjOrders")
    public static class FjOrder {
        @Id
        @Column(name = "id")
        private Integer id;

        @ManyToOne
        @JoinColumn(name = "customerId")
        private FjCustomer customer;
    }

    private static EntityManagerFactory factory;
    private static EntityManager manager;

    @BeforeAll
    static void open() {
        factory = EclipseLinkBootstrap.build("none", FjCustomer.class, FjOrder.class);
        manager = factory.createEntityManager();
    }

    @AfterAll
    static void close() {
        manager.close();
        factory.close();
    }

    /** The claim: a full join is refused as a syntax error when the query is created. */
    @Test
    void aFullJoinIsRefusedWhenTheQueryIsCreated() {
        assertThrows(IllegalArgumentException.class,
                () -> manager.createQuery("select c from FjCustomer c full join FjOrder o on o.customer = c"));
        assertThrows(IllegalArgumentException.class,
                () -> manager.createQuery("select c from FjCustomer c full outer join FjOrder o on o.customer = c"));
    }

    /** The joins the specification has are read, with the same condition. */
    @Test
    void anInnerAndALeftJoinAreRead() {
        assertDoesNotThrow(() -> manager.createQuery("select c from FjCustomer c join FjOrder o on o.customer = c"));
        assertDoesNotThrow(() -> manager.createQuery("select c from FjCustomer c left join FjOrder o on o.customer = c"));
    }
}
