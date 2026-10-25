package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What Hibernate 7.4.5 makes of a key's generation on SQL Server, read from the schema
 * script it writes (decision 187). The facts the JPA layer rests on: {@code AUTO} - the bare
 * annotation - is a sequence named after the table, not the entity, stepping by 50, which
 * is what the parser reads a Hibernate source's {@code AUTO} as; and {@code IDENTITY} is an
 * identity column, which is what the builder writes for the vocabulary's Auto and Identity.
 *
 * <p>The script is generated with no database action, so nothing is created; the bootstrap
 * still connects once to learn the dialect, which is why the class is an integration test.
 */
@Tag("integration")
class KeyGenerationClaimsTest {

    private static String ddl;

    @BeforeAll
    static void generateTheSchemaScript() throws IOException {
        Path target = Files.createTempFile("ormconvertor-hibernate-", ".sql");
        Files.delete(target);

        Map<String, String> settings = Map.of(
                "jakarta.persistence.schema-generation.scripts.action", "create",
                "jakarta.persistence.schema-generation.scripts.create-target", target.toString());

        try (SessionFactory ignored = HibernateBootstrap.build("none", settings, AutoKeyed.class, IdentityKeyed.class, SequenceKeyed.class, NamedGeneratorKeyed.class, AutoNamedKeyed.class, AutoTableKeyed.class)) {
            // nothing to run - the script is the point
        }

        ddl = Files.readString(target, StandardCharsets.UTF_8);
        Files.deleteIfExists(target);

        assertTrue(!ddl.isBlank(), "the provider wrote no schema script at all");
    }

    /**
     * The sequence takes the name of the table, not of the entity: {@code AutoKeyeds_SEQ} for
     * the entity {@code AutoKeyed} over the table {@code AutoKeyeds}. Decision 077 had written
     * the entity's name down; the two agree only where the table is named like the entity.
     */
    @Test
    void autoIsASequenceNamedAfterTheTableWithAStepOfFifty() {
        assertSequence("AutoKeyeds_SEQ");
        assertFalse(tableDefinition("AutoKeyeds").contains(" identity"),
                "the AUTO key is an identity column:" + System.lineSeparator() + ddl);
    }

    /** SEQUENCE with no generator named is the same default as AUTO. */
    @Test
    void sequenceWithoutAGeneratorIsTheSameSequence() {
        assertSequence("SequenceKeyeds_SEQ");
    }

    /** A generator that names no sequence names it after itself. */
    @Test
    void aGeneratorWithoutASequenceNameNamesTheSequence() {
        assertSequence("keyed_gen");
    }

    /** AUTO that names a sequence generator is that generator: its sequence, its step. */
    @Test
    void autoWithANamedGeneratorIsThatGenerator() {
        assertSequence("AutoNamed_Seq", 20);
        assertFalse(ddl.toLowerCase(Locale.ROOT).contains("autonamedkeyeds_seq"),
                "AUTO with a named generator got the default sequence too:" + System.lineSeparator() + ddl);
    }

    /** AUTO that names a table generator is that generator: its counter table, no sequence. */
    @Test
    void autoWithANamedTableGeneratorIsThatTable() {
        assertTrue(tableDefinition("AutoCounters").contains("countername"),
                "the script has no counter table AutoCounters:" + System.lineSeparator() + ddl);
        assertFalse(ddl.toLowerCase(Locale.ROOT).contains("autotablekeyeds_seq"),
                "AUTO with a named table generator got the default sequence:" + System.lineSeparator() + ddl);
    }

    private static void assertSequence(String name) {
        assertSequence(name, 50);
    }

    private static void assertSequence(String name, int step) {
        assertTrue(Pattern.compile("create sequence (\\S+\\.)?" + name + " start with 1 increment by " + step, Pattern.CASE_INSENSITIVE)
                        .matcher(ddl).find(),
                "the script has no sequence " + name + " stepping by " + step + ":" + System.lineSeparator() + ddl);
    }

    @Test
    void identityIsAnIdentityColumn() {
        assertTrue(tableDefinition("IdentityKeyeds").contains("identitykeyedid int identity not null"),
                "the IDENTITY key is not an identity column:" + System.lineSeparator() + ddl);
        assertFalse(ddl.toLowerCase(Locale.ROOT).contains("identitykeyed_seq"),
                "an IDENTITY key got a sequence:" + System.lineSeparator() + ddl);
    }

    /** The create table statement of the given table, lower cased. */
    private static String tableDefinition(String table) {
        String lower = ddl.toLowerCase(Locale.ROOT);
        var matcher = Pattern.compile("create table (\\S+\\.)?" + table.toLowerCase(Locale.ROOT) + " \\(").matcher(lower);
        assertTrue(matcher.find(), "the script has no table " + table + ":" + System.lineSeparator() + ddl);

        int end = lower.indexOf(';', matcher.start());
        return lower.substring(matcher.start(), end < 0 ? lower.length() : end);
    }
}
