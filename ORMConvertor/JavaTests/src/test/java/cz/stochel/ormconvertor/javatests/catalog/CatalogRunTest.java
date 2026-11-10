package cz.stochel.ormconvertor.javatests.catalog;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.ldbc.PreparedJavaQuery;
import cz.stochel.ormconvertor.javatests.tool.ConversionRecord;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.QueryFeature;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import java.time.LocalDateTime;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/**
 * What holds of the on-demand run of a catalog without a database (decisions 149 and 208) - the
 * counterpart of {@code CatalogRunTest.cs}: how the run reads its configuration, when it
 * measures, how it converts the values of a catalog into the arguments of a generated method,
 * and that the digest of a result is the one the .NET suite writes. The runs themselves are
 * {@code fiben/FibenRunTest} and {@code sede/SedeRunTest}.
 */
class CatalogRunTest {

    @Test
    void aMissingDatabaseSkipsUnlessTheEnvironmentPromisedIt() {
        for (CatalogDatabase database : List.of(CatalogDatabase.FIBEN, CatalogDatabase.SEDE)) {
            assertNull(database.failureOf("no database", false));

            String failure = database.failureOf("no database", true);
            assertNotNull(failure);
            assertTrue(failure.contains(database.requireVariable()) && failure.contains(database.databaseName()) && failure.contains("no database"), failure);
        }
    }

    /** The settings of the two runs differ by the prefix alone, so that the commands of one are the commands of the other. */
    @Test
    void theTwoRunsAreConfiguredAlike() {
        assertEquals("ORMCONVERTOR_TEST_FIBEN_JDBC_URL", CatalogDatabase.FIBEN.jdbcUrlVariable());
        assertEquals("ORMCONVERTOR_FIBEN_API_URL", CatalogDatabase.FIBEN.apiUrlVariable());
        assertEquals("ORMCONVERTOR_TEST_SEDE_JDBC_URL", CatalogDatabase.SEDE.jdbcUrlVariable());
        assertEquals("ORMCONVERTOR_SEDE_API_URL", CatalogDatabase.SEDE.apiUrlVariable());
        assertEquals("ORMCONVERTOR_REQUIRE_SEDE_DATABASE", CatalogDatabase.SEDE.requireVariable());
        assertEquals("ORMCONVERTOR_SEDE_QUERIES", CatalogDatabase.SEDE.queriesVariable());
        assertEquals("ORMCONVERTOR_SEDE_RESULTS", CatalogDatabase.SEDE.resultsVariable());
        assertEquals("ORMCONVERTOR_SEDE_MEASURE", CatalogDatabase.SEDE.measureVariable());
    }

    @Test
    void theLimitIsAListOfKeys() {
        assertNull(CatalogDatabase.resolveKeys(null));
        assertNull(CatalogDatabase.resolveKeys(" "));
        assertEquals(java.util.Set.of("q1", "q2", "q237"), CatalogDatabase.resolveKeys("q1, Q2 ,q237"));
        assertEquals(java.util.Set.of("s1233", "s2304"), CatalogDatabase.resolveKeys("s1233,S2304"));
    }

    @Test
    void theUrlHandedToACellCarriesTheTimeLimit() {
        String base = "jdbc:sqlserver://db:1433;databaseName=Sede;user=sa;password=p;encrypt=true;trustServerCertificate=true";

        assertEquals(base + ";queryTimeout=" + CatalogRun.QUERY_TIMEOUT_SECONDS, CatalogDatabase.withQueryTimeout(base));
        assertEquals(base + ";queryTimeout=" + CatalogRun.QUERY_TIMEOUT_SECONDS,
                CatalogDatabase.withQueryTimeout("jdbc:sqlserver://db:1433;databaseName=Sede;queryTimeout=30;user=sa;password=p;encrypt=true;trustServerCertificate=true"));
    }

    /** The run measures only over a server in the measurement mode (decisions 164 and 166), as the .NET run does. */
    @Test
    void theRunMeasuresOnlyOverAServerInTheMeasurementMode() {
        assertNull(CatalogRun.Server.measurementLeftOut(true, 1, 2));
        assertTrue(CatalogRun.Server.measurementLeftOut(true, 0, 2).contains("MAXDOP 0"));
        assertTrue(CatalogRun.Server.measurementLeftOut(true, 4, 4).contains("MAXDOP 4"));
        assertTrue(CatalogRun.Server.measurementLeftOut(true, 1, 4).contains("4 processors"));
        assertTrue(CatalogRun.Server.measurementLeftOut(true, 1, null).contains("unknown number"));
        assertEquals("left out", CatalogRun.Server.measurementLeftOut(false, 1, 2));
    }

    /** The value of a parameter in the catalog is a text; the run converts it to the type the generated method declares. */
    @Test
    void aValueOfTheCatalogBecomesTheArgumentTheMethodDeclares() {
        assertEquals(1192, CatalogRun.argument(int.class, "1192"));
        assertEquals(7, CatalogRun.argument(Integer.class, "7"));
        assertEquals(4398046513938L, CatalogRun.argument(long.class, "4398046513938"));
        assertEquals("sql-server", CatalogRun.argument(String.class, "sql-server"));
        assertEquals(0.5, CatalogRun.argument(double.class, "0.5"));
        assertEquals(Boolean.TRUE, CatalogRun.argument(boolean.class, "1"));
        assertEquals(LocalDateTime.of(2015, 1, 1, 0, 0), CatalogRun.argument(LocalDateTime.class, "2015-01-01"));
    }

    /** A value is declared in front of the source text as a T-SQL literal: a number bare, a text in quotes with a quote doubled. */
    @Test
    void aValueIsDeclaredAsTheLiteralOfItsType() {
        assertEquals("1192", new CatalogQuery.Argument("UserId", "INT", "1192").literal());
        assertEquals("N'O''Brien'", new CatalogQuery.Argument("Name", "NVARCHAR(MAX)", "O'Brien").literal());
        assertEquals("N'2015-01-01'", new CatalogQuery.Argument("Date1", "NVARCHAR(MAX)", "2015-01-01").literal());
    }

    /** The digest of a result is the same in both suites; this value is held by the .NET suite over the same rows. */
    @Test
    void theDigestIsTheOneTheDotNetSuiteWrites() {
        assertEquals("rows=0 sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", CatalogRun.digest(List.of()));
        assertEquals("rows=2 sha256=" + sha256("1\t\"a\"\n2\tNULL"), CatalogRun.digest(List.of("1\t\"a\"", "2\tNULL")));
    }

    /**
     * A cell states what its translation lost, as the .NET run's cell does, and after a loss
     * a column no map of a MyBatis result carries is a column the artifact does not project,
     * not a NULL: the read fails on it instead of returning rows that only differ from the
     * source's (the SEDE run of 2026-11-08, s31062).
     */
    @Test
    void aColumnTheTranslationLostIsMissingRatherThanNull() {
        ConversionRecord loss = new ConversionRecord(RecordKind.LOSS, Orm.MYBATIS, null, null, null, null, QueryFeature.EXPRESSION, null, "The projected expression '-bountyamount' was dropped.");
        // A loss of the entity branch names no query feature and changes no column of a result.
        ConversionRecord mapping = new ConversionRecord(RecordKind.LOSS, Orm.MYBATIS, null, "Votes", "Id", 1, null, null, "The source states PrimaryKeyStrategy and the target has no way to record it.");
        ConversionRecord fallback = new ConversionRecord(RecordKind.FALLBACK, Orm.MYBATIS, null, null, null, null, null, null, "native SQL");
        ConversionResponse lossy = new ConversionResponse("r", "v", Orm.DAPPER, "", Orm.MYBATIS, "", List.of(), List.of(mapping, loss, fallback, loss), 0, null);
        ConversionResponse whole = new ConversionResponse("r", "v", Orm.DAPPER, "", Orm.MYBATIS, "", List.of(), List.of(mapping, fallback), 0, null);

        assertEquals("The projected expression '-bountyamount' was dropped.", CatalogRun.lost(lossy));
        assertNull(CatalogRun.lost(whole));
        assertEquals("boom (the translation lost: it)", CatalogRun.withLoss("boom", "it"));
        assertEquals("boom", CatalogRun.withLoss("boom", null));

        List<String> fields = List.of("owneruserid", "bountyamount");
        List<Map<String, Object>> returned = List.of(new LinkedHashMap<>(Map.of("owneruserid", 1)));
        assertEquals(Arrays.asList(1, null), JavaQueryRunner.rows(PreparedJavaQuery.withEveryColumn(returned, fields), "s31062", fields, false).get(0));
        IllegalStateException missing = assertThrows(IllegalStateException.class, () -> JavaQueryRunner.rows(returned, "s31062", fields, false));
        assertTrue(missing.getMessage().contains("\"bountyamount\""), missing.getMessage());
    }

    private static String sha256(String text) {
        try {
            byte[] hash = java.security.MessageDigest.getInstance("SHA-256").digest(text.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder hex = new StringBuilder();
            for (byte b : hash) {
                hex.append(String.format(java.util.Locale.ROOT, "%02x", b));
            }
            return hex.toString();
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
