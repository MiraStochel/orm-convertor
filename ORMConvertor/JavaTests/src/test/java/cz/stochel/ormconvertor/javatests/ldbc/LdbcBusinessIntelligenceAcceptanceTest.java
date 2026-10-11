package cz.stochel.ormconvertor.javatests.ldbc;

import static org.junit.jupiter.api.Assertions.assertNotNull;

import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import java.util.stream.IntStream;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The Business Intelligence queries of the LDBC catalog in the Java targets at the second and
 * third verification level: no validation set judges them (decision 117), so without this
 * class their claim in a Java target would stop at the first level, held by the .NET suite.
 * Every BI query is translated from Dapper by the instance whose catalog is LdbcSnb, compiled
 * with the entities of its conversion, and handed to the framework: a JPA target creates the
 * query, which is where Hibernate and EclipseLink parse JPQL and where a shape the parser
 * refuses fails; MyBatis binds the statement when its factory is built. None of them runs,
 * because BI 15 and BI 19 would not finish over a loaded data set (decision 122) and the
 * rows of a BI query have no judge to be compared with.
 *
 * <p>Needs what the judge needs - the LDBC database and the instance over it - and skips
 * without them with the same reason, or fails where the environment promised them.
 */
class LdbcBusinessIntelligenceAcceptanceTest {

    private static final int BUSINESS_INTELLIGENCE = 30;
    private static final int QUERIES = 20;

    private static LdbcCatalog catalog;

    static Stream<Arguments> cells() {
        return IntStream.rangeClosed(1, QUERIES)
                .mapToObj(number -> "bi" + number)
                .flatMap(key -> Stream.of(Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS).map(framework -> Arguments.of(key, framework)));
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyBusinessIntelligenceQueryIsAcceptedByItsTarget(String key, int framework) throws Exception {
        String notConfigured = LdbcDatabase.notConfigured();
        if (notConfigured != null) {
            LdbcDatabase.unavailable(notConfigured);
        }

        LdbcCatalog.Query query = catalog().query(key);
        if (query.workload() != BUSINESS_INTELLIGENCE) {
            throw new IllegalStateException(key + " is not a query of the BI workload.");
        }

        ConversionResponse response = translate(query, framework);
        try (PreparedJavaQuery prepared = PreparedJavaQuery.prepare(key, framework, response, null, LdbcDatabase.jdbcUrl())) {
            prepared.accept();
        }
    }

    @AfterAll
    static void closeDomains() {
        PreparedJavaQuery.closeAll();
    }

    private static synchronized LdbcCatalog catalog() {
        if (catalog == null) {
            ToolApi.awaitReady(LdbcDatabase.apiUrl());
            catalog = ToolApi.get(LdbcDatabase.apiUrl(), "/ldbc", LdbcCatalog.class);
        }
        return catalog;
    }

    /** The artifact of one target, from Dapper with LdbcSnb as the catalog; a Failure record contradicts the catalog's claim. */
    private static ConversionResponse translate(LdbcCatalog.Query query, int framework) {
        ToolResponse answer = ToolApi.convert(LdbcDatabase.apiUrl(), Orm.DAPPER, framework, catalog.units(query));
        ConversionResponse response = answer.required();

        if (!response.recordsOf(RecordKind.FAILURE).isEmpty()) {
            throw new AssertionError(query.key() + " into " + Orm.nameOf(framework) + " was refused:"
                    + System.lineSeparator() + response.describeRecords());
        }

        assertNotNull(response, query.key());
        return response;
    }
}
