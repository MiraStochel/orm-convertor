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
import jakarta.persistence.IdClass;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.JoinColumns;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.MapsId;
import jakarta.persistence.OneToOne;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.function.Consumer;
import org.eclipse.persistence.sessions.Session;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 takes a primary key that is at the same time the foreign key of a
 * reference - the shared primary key of decision 012, {@code CustomerProfile} keyed by the
 * customer it belongs to - in the shapes the JPA builder can write it in (decision 206). The
 * entities are written by hand in the builder's spelling. The one the builder wrote until the
 * experiment run of 2026-11-05, {@code @MapsId} over a join column marked
 * {@code insertable = false, updatable = false}, is refused at deployment with
 * {@code EclipseLink-46}: under {@code @MapsId} the identifier is derived from the reference
 * and the reference's join column is the one mapping that writes the key, so with it read-only
 * no mapping writes the key at all. The same {@code @MapsId} over a writable join column
 * deploys and writes the key once from the reference, and so does the older spelling without
 * {@code @MapsId}, where the identifier writes the column and the join column is read-only. A
 * composite key with one part shared with a reference, and a composite key shared whole
 * without {@code @MapsId}, deploy in the builder's read-only spelling; {@code @MapsId} over a
 * whole composite key of an {@code @IdClass} is refused with {@code EclipseLink-48}: there the
 * identifier parts stay writable and the writable join columns are a second writer of the same
 * fields, which is why the builder writes {@code @MapsId} over a single-part key only.
 */
@Tag("integration")
class SharedKeyClaimsTest {

    @Entity(name = "SkCustomer")
    @Table(name = "SkCustomers")
    public static class SkCustomer {
        @Id
        @Column(name = "CustomerId")
        private Integer id;

        public SkCustomer() {
        }

        public SkCustomer(Integer id) {
            this.id = id;
        }

        public Integer getId() {
            return id;
        }
    }

    /** The shape the builder wrote until 2026-11-05: @MapsId over a read-only join column. */
    @Entity(name = "SkProfileMapsIdReadOnly")
    @Table(name = "SkProfiles")
    public static class SkProfileMapsIdReadOnly {
        @Id
        @Column(name = "CustomerId")
        private Integer id;

        @OneToOne(optional = false)
        @MapsId
        @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId", nullable = false, unique = true,
                insertable = false, updatable = false)
        private SkCustomer customer;
    }

    /** The shape the builder writes since decision 206: @MapsId over a writable join column. */
    @Entity(name = "SkProfileMapsId")
    @Table(name = "SkProfiles")
    public static class SkProfileMapsId {
        @Id
        @Column(name = "CustomerId")
        private Integer id;

        @OneToOne(optional = false)
        @MapsId
        @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId", nullable = false, unique = true)
        private SkCustomer customer;

        public SkProfileMapsId() {
        }

        public SkProfileMapsId(SkCustomer customer) {
            this.customer = customer;
        }

        public Integer getId() {
            return id;
        }

        public SkCustomer getCustomer() {
            return customer;
        }
    }

    /** The spelling without @MapsId: the identifier writes the column, the reference is read-only over it. */
    @Entity(name = "SkProfileReadOnlyReference")
    @Table(name = "SkProfiles")
    public static class SkProfileReadOnlyReference {
        @Id
        @Column(name = "CustomerId")
        private Integer id;

        @OneToOne(optional = false)
        @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId", nullable = false, unique = true,
                insertable = false, updatable = false)
        private SkCustomer customer;

        public SkProfileReadOnlyReference() {
        }

        public SkProfileReadOnlyReference(Integer id, SkCustomer customer) {
            this.id = id;
            this.customer = customer;
        }

        public Integer getId() {
            return id;
        }

        public SkCustomer getCustomer() {
            return customer;
        }
    }

    @Entity(name = "SkOrder")
    @Table(name = "SkOrders")
    public static class SkOrder {
        @Id
        @Column(name = "OrderId")
        private Integer id;

        public SkOrder() {
        }

        public SkOrder(Integer id) {
            this.id = id;
        }
    }

    /** A composite key with one part that is the foreign key of a reference: the builder's read-only spelling. */
    @Entity(name = "SkLine")
    @Table(name = "SkLines")
    @IdClass(SkLine.Key.class)
    public static class SkLine {
        @Id
        @Column(name = "OrderId")
        private Integer orderId;

        @Id
        @Column(name = "LineNumber")
        private Integer lineNo;

        @ManyToOne(optional = false)
        @JoinColumn(name = "OrderId", referencedColumnName = "OrderId", nullable = false, insertable = false, updatable = false)
        private SkOrder order;

        public SkLine() {
        }

        public SkLine(Integer orderId, Integer lineNo, SkOrder order) {
            this.orderId = orderId;
            this.lineNo = lineNo;
            this.order = order;
        }

        public Integer getLineNo() {
            return lineNo;
        }

        public static class Key implements Serializable {
            private Integer orderId;
            private Integer lineNo;

            public Key() {
            }

            public Key(Integer orderId, Integer lineNo) {
                this.orderId = orderId;
                this.lineNo = lineNo;
            }

            @Override
            public boolean equals(Object other) {
                return other instanceof Key key && Objects.equals(orderId, key.orderId) && Objects.equals(lineNo, key.lineNo);
            }

            @Override
            public int hashCode() {
                return Objects.hash(orderId, lineNo);
            }
        }
    }

    @Entity(name = "SkPair")
    @Table(name = "SkPairs")
    @IdClass(SkPair.Key.class)
    public static class SkPair {
        @Id
        @Column(name = "A")
        private Integer a;

        @Id
        @Column(name = "B")
        private Integer b;

        public SkPair() {
        }

        public SkPair(Integer a, Integer b) {
            this.a = a;
            this.b = b;
        }

        public static class Key implements Serializable {
            private Integer a;
            private Integer b;

            public Key() {
            }

            public Key(Integer a, Integer b) {
                this.a = a;
                this.b = b;
            }

            @Override
            public boolean equals(Object other) {
                return other instanceof Key key && Objects.equals(a, key.a) && Objects.equals(b, key.b);
            }

            @Override
            public int hashCode() {
                return Objects.hash(a, b);
            }
        }
    }

    /** A composite key shared whole with a reference, @MapsId over an @IdClass. */
    @Entity(name = "SkPairDetailMapsId")
    @Table(name = "SkPairDetails")
    @IdClass(SkPair.Key.class)
    public static class SkPairDetailMapsId {
        @Id
        @Column(name = "A")
        private Integer a;

        @Id
        @Column(name = "B")
        private Integer b;

        @OneToOne(optional = false)
        @MapsId
        @JoinColumns({
            @JoinColumn(name = "A", referencedColumnName = "A", nullable = false),
            @JoinColumn(name = "B", referencedColumnName = "B", nullable = false)
        })
        private SkPair pair;
    }

    /** The same key without @MapsId: the identifier writes both columns, the reference is read-only over them. */
    @Entity(name = "SkPairDetailReadOnlyReference")
    @Table(name = "SkPairDetails")
    @IdClass(SkPair.Key.class)
    public static class SkPairDetailReadOnlyReference {
        @Id
        @Column(name = "A")
        private Integer a;

        @Id
        @Column(name = "B")
        private Integer b;

        @OneToOne(optional = false)
        @JoinColumns({
            @JoinColumn(name = "A", referencedColumnName = "A", nullable = false, insertable = false, updatable = false),
            @JoinColumn(name = "B", referencedColumnName = "B", nullable = false, insertable = false, updatable = false)
        })
        private SkPair pair;

        public SkPairDetailReadOnlyReference() {
        }

        public SkPairDetailReadOnlyReference(Integer a, Integer b, SkPair pair) {
            this.a = a;
            this.b = b;
            this.pair = pair;
        }

        public Integer getB() {
            return b;
        }
    }

    /** The hand-written entities name no schema, so the session qualifies their tables with the suite's. */
    public static final class SuiteSchema implements SessionCustomizer {
        @Override
        public void customize(Session session) {
            session.getLogin().setTableQualifier(TestDatabase.schemaName());
        }
    }

    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".SkCustomers (CustomerId int PRIMARY KEY)");
            statement.execute("CREATE TABLE " + schema + ".SkProfiles (CustomerId int PRIMARY KEY "
                    + "REFERENCES " + schema + ".SkCustomers (CustomerId))");
            statement.execute("CREATE TABLE " + schema + ".SkOrders (OrderId int PRIMARY KEY)");
            statement.execute("CREATE TABLE " + schema + ".SkLines (OrderId int NOT NULL REFERENCES " + schema + ".SkOrders (OrderId), "
                    + "LineNumber int NOT NULL, PRIMARY KEY (OrderId, LineNumber))");
            statement.execute("CREATE TABLE " + schema + ".SkPairs (A int NOT NULL, B int NOT NULL, PRIMARY KEY (A, B))");
            statement.execute("CREATE TABLE " + schema + ".SkPairDetails (A int NOT NULL, B int NOT NULL, PRIMARY KEY (A, B), "
                    + "FOREIGN KEY (A, B) REFERENCES " + schema + ".SkPairs (A, B))");
        }
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        TestSchema.drop();
    }

    /** The claim behind decision 206: @MapsId over a read-only join column leaves no mapping that writes the key. */
    @Test
    void mapsIdOverAReadOnlyJoinColumnIsRefusedAtDeployment() {
        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> deploy(SkCustomer.class, SkProfileMapsIdReadOnly.class));
        assertTrue(mentions(refused, "EclipseLink-46") || mentions(refused, "non-read-only"), String.valueOf(refused.getMessage()));
    }

    /** The shape the builder writes: the key is derived from the reference and written once, by it. */
    @Test
    void mapsIdOverAWritableJoinColumnDeploysAndWritesTheKeyFromTheReference() {
        List<String> rows = new ArrayList<>();
        run(manager -> {
            SkCustomer customer = new SkCustomer(1);
            manager.persist(customer);
            manager.persist(new SkProfileMapsId(customer));
        }, SkCustomer.class, SkProfileMapsId.class);
        run(manager -> {
            SkProfileMapsId profile = manager.find(SkProfileMapsId.class, 1);
            rows.add(profile.getId() + "," + profile.getCustomer().getId());
        }, SkCustomer.class, SkProfileMapsId.class);
        assertEquals(List.of("1,1"), rows);
        assertTrue(keys("SkProfiles", "CustomerId").contains("1"));
    }

    /** The spelling without @MapsId deploys too; the identifier writes the column and the reference reads it. */
    @Test
    void aReadOnlyReferenceOverTheIdentifierDeploysAndTheIdentifierWritesTheKey() {
        List<String> rows = new ArrayList<>();
        run(manager -> {
            SkCustomer customer = new SkCustomer(2);
            manager.persist(customer);
            manager.persist(new SkProfileReadOnlyReference(2, customer));
        }, SkCustomer.class, SkProfileReadOnlyReference.class);
        run(manager -> {
            SkProfileReadOnlyReference profile = manager.find(SkProfileReadOnlyReference.class, 2);
            rows.add(profile.getId() + "," + profile.getCustomer().getId());
        }, SkCustomer.class, SkProfileReadOnlyReference.class);
        assertEquals(List.of("2,2"), rows);
    }

    /** A composite key with one shared part keeps the builder's read-only spelling of the reference. */
    @Test
    void aCompositeKeyWithOneSharedPartDeploysWithTheReferenceReadOnly() {
        List<String> rows = new ArrayList<>();
        run(manager -> {
            SkOrder order = new SkOrder(10);
            manager.persist(order);
            manager.persist(new SkLine(10, 1, order));
            manager.persist(new SkLine(10, 2, order));
        }, SkOrder.class, SkLine.class);
        run(manager -> {
            SkLine line = manager.find(SkLine.class, new SkLine.Key(10, 2));
            rows.add(String.valueOf(line.getLineNo()));
        }, SkOrder.class, SkLine.class);
        assertEquals(List.of("2"), rows);
        assertEquals(List.of("1", "2"), keys("SkLines", "LineNumber"));
    }

    /** @MapsId over the whole key of an @IdClass is refused (48, two writable mappings of one field); the builder writes @MapsId over a single-part key only. */
    @Test
    void mapsIdOverAWholeCompositeKeyOfAnIdClassIsRefusedAtDeployment() {
        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> deploy(SkPair.class, SkPairDetailMapsId.class));
        assertTrue(mentions(refused, "EclipseLink-48"), String.valueOf(refused.getMessage()));
    }

    /** A composite key shared whole deploys in the spelling without @MapsId. */
    @Test
    void aCompositeKeySharedWholeDeploysWithTheReferenceReadOnly() {
        List<String> rows = new ArrayList<>();
        run(manager -> {
            SkPair pair = new SkPair(5, 6);
            manager.persist(pair);
            manager.persist(new SkPairDetailReadOnlyReference(5, 6, pair));
        }, SkPair.class, SkPairDetailReadOnlyReference.class);
        run(manager -> {
            SkPairDetailReadOnlyReference detail = manager.find(SkPairDetailReadOnlyReference.class, new SkPair.Key(5, 6));
            rows.add(String.valueOf(detail.getB()));
        }, SkPair.class, SkPairDetailReadOnlyReference.class);
        assertEquals(List.of("6"), rows);
    }

    private static void deploy(Class<?>... entities) {
        EntityManagerFactory factory = build(entities);
        try {
            factory.createEntityManager().close();
        } finally {
            factory.close();
        }
    }

    private static void run(Consumer<EntityManager> work, Class<?>... entities) {
        EntityManagerFactory factory = build(entities);
        try {
            EntityManager manager = factory.createEntityManager();
            try {
                manager.getTransaction().begin();
                work.accept(manager);
                manager.getTransaction().commit();
            } finally {
                manager.close();
            }
        } finally {
            factory.close();
        }
    }

    private static EntityManagerFactory build(Class<?>... entities) {
        return EclipseLinkBootstrap.build(
                "none",
                SharedKeyClaimsTest.class.getClassLoader(),
                null,
                List.of(entities),
                Map.of("eclipselink.session.customizer", SuiteSchema.class.getName()));
    }

    /** What the table holds after the write, read over plain JDBC so the claim is about the rows and not the cache. */
    private static List<String> keys(String table, String column) {
        List<String> values = new ArrayList<>();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement();
             var result = statement.executeQuery("SELECT " + column + " FROM " + TestDatabase.schemaName() + "." + table + " ORDER BY " + column)) {
            while (result.next()) {
                values.add(result.getString(1));
            }
        } catch (java.sql.SQLException e) {
            throw new IllegalStateException(e);
        }
        return values;
    }

    /** Whether the exception or any cause of it names the text: the provider wraps its validation exception on the way out. */
    private static boolean mentions(Throwable thrown, String text) {
        for (Throwable cause = thrown; cause != null; cause = cause.getCause()) {
            if (String.valueOf(cause.getMessage()).contains(text)) {
                return true;
            }
        }
        return false;
    }
}
