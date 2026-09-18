package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Locale;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/**
 * The claims decision 080 took from the EclipseLink tutorial and wrote into the profile,
 * checked against the release this JVM loads. They are values the C# side cannot verify -
 * xUnit can only assert that the builder writes them - and each of them is a fact that
 * shows up nowhere in the artifact: the counter table behind {@code AUTO}, and the plain
 * {@code String} that is not national character data.
 *
 * <p>The DDL is generated as a script rather than against the database, the same way the
 * tutorial measured it: nothing is created, and the script is the whole evidence. The
 * third fact of the profile - a lazy reference being inert without weaving - stays
 * measured by the tutorial's ninth step: it needs a JVM started with an agent, which is a
 * deployment and not a test, and the boundary of the guarantees says so instead.
 */
class EclipseLinkClaimsTest {

    private static String ddl;

    @BeforeAll
    static void generateTheSchemaScript() throws IOException {
        Path target = Files.createTempFile("ormconvertor-eclipselink-", ".sql");

        EclipseLinkBootstrap.generateScript(target, List.of(Widget.class));

        ddl = Files.readString(target, StandardCharsets.UTF_8);
        Files.deleteIfExists(target);

        assertTrue(!ddl.isBlank(), "the provider wrote no schema script at all");
    }

    /**
     * The profile says {@code AUTO} is a counter table named SEQUENCE with the row SEQ_GEN
     * (decision 080), and that is why the builder writes a @TableGenerator with those
     * values instead of the annotation the source had. If a release ever changed them, the
     * artifacts would keep counting from a table nobody creates - so it is measured here.
     */
    @Test
    void autoIsTheCounterTableTheProfileNames() {
        assertContains("CREATE TABLE SEQUENCE");
        assertContains("SEQ_NAME");
        assertContains("SEQ_COUNT");
        assertContains("SEQ_GEN");
    }

    /**
     * And the other half of the same claim: this really is a table, not a sequence. The
     * same annotation gives Hibernate {@code CREATE SEQUENCE Widget_SEQ}, which is the
     * swap decision 076 refused to let happen silently.
     */
    @Test
    void autoIsNotASequenceHere() {
        assertTrue(!ddl.toUpperCase(Locale.ROOT).contains("CREATE SEQUENCE"),
                "EclipseLink generated a sequence for @GeneratedValue:" + System.lineSeparator() + ddl);
    }

    /**
     * A plain String is VARCHAR and the literal type is the only way to say otherwise -
     * the reason the second hook of decision 076 writes a columnDefinition here where the
     * Hibernate builder writes @Nationalized.
     */
    @Test
    void aStringIsNotNationalUnlessTheLiteralTypeSaysSo() {
        String widgets = tableDefinition();

        assertTrue(widgets.contains("LABEL NVARCHAR(200)"),
                "the column with the literal type is not nvarchar:" + System.lineSeparator() + widgets);
        assertTrue(widgets.contains("NOTE VARCHAR(200)"),
                "the plain String column is not varchar:" + System.lineSeparator() + widgets);
    }

    /** The CREATE TABLE statement of the entity, upper cased - EclipseLink writes its DDL that way. */
    private static String tableDefinition() {
        String upper = ddl.toUpperCase(Locale.ROOT);
        int start = upper.indexOf("CREATE TABLE WIDGETS");
        assertTrue(start >= 0, "the script has no Widgets table:" + System.lineSeparator() + ddl);

        int end = upper.indexOf("CREATE TABLE", start + 1);
        return ddl.substring(start, end < 0 ? ddl.length() : end).toUpperCase(Locale.ROOT);
    }

    private static void assertContains(String fragment) {
        assertTrue(ddl.toUpperCase(Locale.ROOT).contains(fragment.toUpperCase(Locale.ROOT)),
                "the generated script does not contain \"" + fragment + "\":" + System.lineSeparator() + ddl);
    }
}
