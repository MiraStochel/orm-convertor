package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.hibernate.query.SyntaxException;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The words the profile of Hibernate 7.4.5 lists as entity names its parser refuses
 * ({@code EntityNamesRefused}: the three literals) and the alias the shared layer writes for
 * a reserved identifier, checked against the release this JVM loads. A refused name fails
 * as a syntax error when the query is created; the other names here are read, and the query
 * then names an entity the unit does not declare - a different exception. Nothing is
 * executed.
 */
@Tag("integration")
class KeywordNameClaimsTest {

    @Entity(name = "KwCustomer")
    @Table(name = "KwCustomers")
    public static class KwCustomer {
        @Id
        private Integer id;
    }

    @Entity(name = "KwOrder")
    @Table(name = "KwOrders")
    public static class KwOrder {
        @Id
        private Integer id;

        @ManyToOne
        private KwCustomer customer;
    }

    private static SessionFactory factory;
    private static Session session;

    @BeforeAll
    static void open() {
        factory = HibernateBootstrap.build("none", KwCustomer.class, KwOrder.class);
        session = factory.openSession();
    }

    @AfterAll
    static void close() {
        session.close();
        factory.close();
    }

    @Test
    void aLiteralIsRefusedAsAnEntityName() {
        assertThrows(SyntaxException.class, () -> session.createSelectionQuery("select n from Null n", Object.class));
        assertThrows(SyntaxException.class,
                () -> session.createSelectionQuery("select c from KwCustomer c join True t on t.customer = c", Object.class));
    }

    @Test
    void theWordsEclipseLinkRefusesAreReadAsEntityNames() {
        for (String name : new String[] {"Table", "Member", "Select", "Order", "Where"}) {
            RuntimeException unknown = assertThrows(RuntimeException.class,
                    () -> session.createSelectionQuery("select c from KwCustomer c join " + name + " x on x.customer = c", Object.class));
            if (unknown instanceof SyntaxException) {
                throw new AssertionError(name + " was refused as an entity name: " + unknown.getMessage());
            }
        }
    }

    @Test
    void aReservedIdentifierIsReadAsAnAliasWithATrailingUnderscore() {
        assertThrows(SyntaxException.class,
                () -> session.createSelectionQuery("select left from KwOrder left where left.id = 1", Object.class));
        assertDoesNotThrow(() -> session.createSelectionQuery("select left_ from KwOrder left_ where left_.id = 1", Object.class));
    }

    /** Hibernate reads a reserved identifier as a result variable either way; the shared layer writes the underscore for EclipseLink's sake. */
    @Test
    void aResultVariableIsReadWithOrWithoutATrailingUnderscore() {
        assertDoesNotThrow(() -> session.createSelectionQuery("select count(o) as count from KwOrder o", Object.class));
        assertDoesNotThrow(() -> session.createSelectionQuery("select o.id as value from KwOrder o order by value desc", Object.class));
        assertDoesNotThrow(() -> session.createSelectionQuery("select count(o) as count_ from KwOrder o", Object.class));
        assertDoesNotThrow(() -> session.createSelectionQuery("select o.id as value_ from KwOrder o order by value_ desc", Object.class));
    }
}
