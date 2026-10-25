package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import jakarta.persistence.EntityManagerFactory;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/**
 * What EclipseLink 5.0.0 makes of the nullability of a key column on SQL Server, read from the
 * schema script it writes over a connection (decision 188): a column {@code @Id} declares - a
 * simple key or a part of an {@code @IdClass} key - is NOT NULL though nothing states it, over
 * a wrapper type and a String alike, while a column of the same type outside the key is not.
 * It is the fact the measure of mapping completeness counts a key column's NOT NULL by; the
 * .NET half is {@code Experiment/KeyColumnNullabilityProviderTest}.
 */
@Tag("integration")
class KeyColumnNullabilityClaimsTest {

    private static String ddl;

    @BeforeAll
    static void generateTheSchemaScript() throws IOException {
        Path target = Files.createTempFile("ormconvertor-eclipselink-nullability-", ".sql");
        Files.delete(target);

        Map<String, Object> scripts = Map.of(
                "jakarta.persistence.schema-generation.scripts.action", "create",
                "jakarta.persistence.schema-generation.scripts.create-target", target.toString());

        // EclipseLink deploys the unit, and writes the script, when the first entity manager opens.
        EntityManagerFactory factory = EclipseLinkBootstrap.build("none", EclipseLinkBootstrap.class.getClassLoader(), null,
                List.of(AssignedKeyed.class, TextKeyed.class, PairKeyed.class), scripts);
        try {
            factory.createEntityManager().close();
        } finally {
            factory.close();
        }

        ddl = Files.readString(target, StandardCharsets.UTF_8).toUpperCase(Locale.ROOT);
        Files.deleteIfExists(target);

        assertTrue(!ddl.isBlank(), "the provider wrote no schema script at all");
    }

    @ParameterizedTest
    @CsvSource({"AssignedKeyeds, AssignedKeyedId", "TextKeyeds, Code", "PairKeyeds, A", "PairKeyeds, B"})
    void aKeyColumnIsNotNull(String table, String column) {
        String definition = columnDefinition(table, column);
        assertTrue(definition.endsWith(" NOT NULL"),
                "the key column " + column + " is not NOT NULL: " + definition + System.lineSeparator() + ddl);
    }

    @ParameterizedTest
    @CsvSource({"AssignedKeyeds", "TextKeyeds", "PairKeyeds"})
    void theSameTypeOutsideTheKeyIsNullable(String table) {
        String definition = columnDefinition(table, "Note");
        assertFalse(definition.contains("NOT NULL"),
                "the column Note outside the key is NOT NULL: " + definition + System.lineSeparator() + ddl);
    }

    /** The definition of one column in the create table statement of the given table, upper cased. */
    private static String columnDefinition(String table, String column) {
        var matcher = Pattern.compile("CREATE TABLE (\\S+\\.)?" + table.toUpperCase(Locale.ROOT) + " \\(").matcher(ddl);
        assertTrue(matcher.find(), "the script has no table " + table + ":" + System.lineSeparator() + ddl);

        int end = ddl.indexOf(';', matcher.end());
        String body = ddl.substring(matcher.end(), end < 0 ? ddl.length() : end);

        String name = column.toUpperCase(Locale.ROOT) + " ";
        return Arrays.stream(body.split(","))
                .map(String::strip)
                .filter(part -> part.startsWith(name))
                .findFirst()
                .orElseThrow(() -> new AssertionError("the table " + table + " has no column " + column + ":" + System.lineSeparator() + ddl));
    }
}
