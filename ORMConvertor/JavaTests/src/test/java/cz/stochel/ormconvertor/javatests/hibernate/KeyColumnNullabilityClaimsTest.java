package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/**
 * What Hibernate 7.4.5 makes of the nullability of a key column on SQL Server, read from the
 * schema script it writes (decision 188): a column {@code @Id} declares - a simple key or a
 * part of an {@code @IdClass} key - is NOT NULL though nothing states it, over a wrapper type
 * and a String alike, while a column of the same type outside the key is not. It is the fact
 * the measure of mapping completeness counts a key column's NOT NULL by; the .NET half is
 * {@code Experiment/KeyColumnNullabilityProviderTest}.
 *
 * <p>The script is generated with no database action, so nothing is created; the bootstrap
 * still connects once to learn the dialect, which is why the class is an integration test.
 */
@Tag("integration")
class KeyColumnNullabilityClaimsTest {

    private static String ddl;

    @BeforeAll
    static void generateTheSchemaScript() throws IOException {
        Path target = Files.createTempFile("ormconvertor-hibernate-nullability-", ".sql");
        Files.delete(target);

        Map<String, String> settings = Map.of(
                "jakarta.persistence.schema-generation.scripts.action", "create",
                "jakarta.persistence.schema-generation.scripts.create-target", target.toString());

        try (SessionFactory ignored = HibernateBootstrap.build("none", settings, AssignedKeyed.class, TextKeyed.class, PairKeyed.class)) {
            // nothing to run - the script is the point
        }

        ddl = Files.readString(target, StandardCharsets.UTF_8);
        Files.deleteIfExists(target);

        assertTrue(!ddl.isBlank(), "the provider wrote no schema script at all");
    }

    @ParameterizedTest
    @CsvSource({"AssignedKeyeds, AssignedKeyedId", "TextKeyeds, Code", "PairKeyeds, A", "PairKeyeds, B"})
    void aKeyColumnIsNotNull(String table, String column) {
        String definition = columnDefinition(table, column);
        assertTrue(definition.endsWith(" not null"),
                "the key column " + column + " is not NOT NULL: " + definition + System.lineSeparator() + ddl);
    }

    @ParameterizedTest
    @CsvSource({"AssignedKeyeds", "TextKeyeds", "PairKeyeds"})
    void theSameTypeOutsideTheKeyIsNullable(String table) {
        String definition = columnDefinition(table, "Note");
        assertFalse(definition.contains("not null"),
                "the column Note outside the key is NOT NULL: " + definition + System.lineSeparator() + ddl);
    }

    /** The definition of one column in the create table statement of the given table, lower cased. */
    private static String columnDefinition(String table, String column) {
        String lower = ddl.toLowerCase(Locale.ROOT);
        var matcher = Pattern.compile("create table (\\S+\\.)?" + table.toLowerCase(Locale.ROOT) + " \\(").matcher(lower);
        assertTrue(matcher.find(), "the script has no table " + table + ":" + System.lineSeparator() + ddl);

        int end = lower.indexOf(';', matcher.end());
        String body = lower.substring(matcher.end(), end < 0 ? lower.length() : end);

        String name = column.toLowerCase(Locale.ROOT) + " ";
        return Arrays.stream(body.split(","))
                .map(String::strip)
                .filter(part -> part.startsWith(name))
                .findFirst()
                .orElseThrow(() -> new AssertionError("the table " + table + " has no column " + column + ":" + System.lineSeparator() + ddl));
    }
}
