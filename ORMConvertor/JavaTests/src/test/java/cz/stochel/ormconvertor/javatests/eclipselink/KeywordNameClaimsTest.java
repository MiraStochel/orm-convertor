package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The words the profile of EclipseLink 5.0.0 lists as entity names its JPQL parser refuses
 * ({@code EntityNamesRefused}, {@code EntityNamesRefusedAsJoinTarget}) and the alias the
 * shared layer writes for a reserved identifier, checked against the release this JVM loads.
 * A name is refused when creating the query fails as a syntax error; an entity the unit does
 * not declare fails too, but as an unknown entity, which is how the two are told apart, so no
 * entity of those names has to exist. Only the queries are created - nothing is executed. The
 * word "set" is left out: the parser does not return on it but runs out of memory.
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

    private static EntityManagerFactory factory;
    private static EntityManager manager;

    @BeforeAll
    static void open() {
        factory = EclipseLinkBootstrap.build("none", KwCustomer.class, KwOrder.class);
        manager = factory.createEntityManager();
    }

    @AfterAll
    static void close() {
        manager.close();
        factory.close();
    }

    @Test
    void aWordOfTheGrammarIsRefusedAsAnEntityNameWhereverItStands() {
        assertSyntaxError("select t from Table t");
        assertSyntaxError("select c from KwCustomer c join Table t on t.customer = c");
        assertSyntaxError("select c from KwCustomer c where exists (select w from Where w where w.customer = c)");
    }

    @Test
    void anotherIsRefusedOnlyAsTheTargetOfAnEntityJoin() {
        assertSyntaxError("select c from KwCustomer c join Member m on m.customer = c");
        assertUnknownEntity("select m from Member m");
    }

    @Test
    void orderIsReadAsAnEntityName() {
        assertUnknownEntity("select c from KwCustomer c join Order o on o.customer = c");
    }

    @Test
    void aReservedIdentifierIsReadAsAnAliasOnlyWithATrailingUnderscore() {
        assertThrows(IllegalArgumentException.class, () -> manager.createQuery("select value from KwOrder value where value.id = 1"));
        assertDoesNotThrow(() -> manager.createQuery("select value_ from KwOrder value_ where value_.id = 1"));
    }

    @Test
    void aReservedIdentifierIsReadAsAResultVariableOnlyWithATrailingUnderscore() {
        assertSyntaxError("select count(o) as count from KwOrder o");
        assertSyntaxError("select o.id as value from KwOrder o order by value desc");
        assertDoesNotThrow(() -> manager.createQuery("select count(o) as count_ from KwOrder o"));
        assertDoesNotThrow(() -> manager.createQuery("select o.id as value_ from KwOrder o order by value_ desc"));
    }

    private static void assertSyntaxError(String jpql) {
        IllegalArgumentException refused = assertThrows(IllegalArgumentException.class, () -> manager.createQuery(jpql));
        assertTrue(String.valueOf(refused.getMessage()).contains("Syntax error parsing"),
                "expected a syntax error for " + jpql + ", got: " + refused.getMessage());
    }

    private static void assertUnknownEntity(String jpql) {
        IllegalArgumentException refused = assertThrows(IllegalArgumentException.class, () -> manager.createQuery(jpql));
        assertTrue(!String.valueOf(refused.getMessage()).contains("Syntax error parsing"),
                "expected the name to be read and the entity to be unknown for " + jpql + ", got: " + refused.getMessage());
    }
}
