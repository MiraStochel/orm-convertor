package cz.stochel.ormconvertor.javatests;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.sql.Connection;
import java.sql.DatabaseMetaData;
import java.sql.ResultSet;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/**
 * The one test the skeleton of decision 076 was to carry: it connects, creates the shared
 * schema and reads it back - proof that the image stage, the compose service and the CI
 * job run the whole path end to end, before there is a generated artifact to verify.
 * What it asserts about the schema is the .NET fixture's own self-check in miniature.
 */
class TestSchemaTest {

    @BeforeAll
    static void createSchema() throws Exception {
        TestSchema.create();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
    }

    @Test
    void theScriptCreatesEveryTableOfTheSchema() throws Exception {
        Set<String> tables = new TreeSet<>();
        try (Connection connection = TestDatabase.open();
             ResultSet rows = connection.getMetaData()
                     .getTables(null, TestDatabase.schemaName(), "%", new String[] {"TABLE"})) {
            while (rows.next()) {
                tables.add(rows.getString("TABLE_NAME"));
            }
        }

        assertEquals(
                // DifferentialProducts is not part of TestSchema.sql: it comes with the
                // read-only data of the differential verification (decision 089), which
                // brings its own table rather than seeding one that others write to.
                new TreeSet<>(Set.of("Customers", "CustomerProfiles", "Orders", "OrderLines",
                        "OrderLineAllocations", "Products", "Suppliers", "ProductSuppliers",
                        "DifferentialProducts")),
                tables);
    }

    @Test
    void theFourPartKeyKeepsTheOrderOfTheScript() throws Exception {
        TreeMap<Short, String> parts = new TreeMap<>();
        try (Connection connection = TestDatabase.open();
             ResultSet rows = connection.getMetaData()
                     .getPrimaryKeys(null, TestDatabase.schemaName(), "OrderLineAllocations")) {
            while (rows.next()) {
                parts.put(rows.getShort("KEY_SEQ"), rows.getString("COLUMN_NAME"));
            }
        }

        assertEquals(List.of("CompanyId", "OrderId", "LineNo", "AllocationId"), new ArrayList<>(parts.values()));
    }

    @Test
    void theThreeColumnForeignKeyIsOneConstraint() throws Exception {
        TreeMap<Short, String> columns = new TreeMap<>();
        Set<String> constraints = new TreeSet<>();
        try (Connection connection = TestDatabase.open();
             ResultSet rows = connection.getMetaData()
                     .getImportedKeys(null, TestDatabase.schemaName(), "OrderLineAllocations")) {
            while (rows.next()) {
                constraints.add(rows.getString("FK_NAME"));
                columns.put(rows.getShort("KEY_SEQ"),
                        rows.getString("FKCOLUMN_NAME") + " -> " + rows.getString("PKCOLUMN_NAME"));
            }
        }

        assertEquals(Set.of("FK_OrderLineAllocations_OrderLines"), constraints);
        assertEquals(
                List.of("CompanyId -> CompanyId", "OrderId -> OrderId", "LineNo -> LineNo"),
                new ArrayList<>(columns.values()));
    }

    /** {@link DatabaseMetaData} is the reader here; nothing else in the suite reads the catalog. */
    @Test
    void theScriptSplitsIntoOneBatchPerStatementGroup() throws Exception {
        // CREATE SCHEMA, eight CREATE TABLE and one ALTER TABLE of the schema script, and
        // the CREATE TABLE and the INSERT the differential data brings with it
        // (decision 089) - one batch each.
        assertEquals(12, TestSchema.batches().size());
    }
}
