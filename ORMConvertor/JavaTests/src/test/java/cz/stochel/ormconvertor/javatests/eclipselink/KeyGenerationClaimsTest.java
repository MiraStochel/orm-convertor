package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import jakarta.persistence.EntityManagerFactory;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What EclipseLink 5.0.0 makes of a key's generation on SQL Server beyond {@code AUTO}, read
 * from the schema script it writes without a connection (decision 187): {@code IDENTITY} is an
 * identity column, as under Hibernate, which is why the JPA builder writes it for the
 * vocabulary's Auto as well as for its Identity; and the sequence EclipseLink takes where
 * {@code SEQUENCE} names no sequence. {@code AUTO} itself, the counter table, is measured by
 * {@link EclipseLinkClaimsTest} over a script of its own, which has no sequence in it.
 *
 * <p>Unlike that script, this one is written over a connection: EclipseLink chooses between a
 * sequence and an identity column for {@code SEQUENCE} by the version of the server it reads
 * from the connection, and without one it writes an identity column (measured 2026-10-25) -
 * which is not what it does against SQL Server 2022.
 */
@Tag("integration")
class KeyGenerationClaimsTest {

    private static String ddl;

    @BeforeAll
    static void generateTheSchemaScript() throws IOException {
        Path target = Files.createTempFile("ormconvertor-eclipselink-keys-", ".sql");
        Files.delete(target);

        Map<String, Object> scripts = Map.of(
                "jakarta.persistence.schema-generation.scripts.action", "create",
                "jakarta.persistence.schema-generation.scripts.create-target", target.toString());

        // EclipseLink deploys the unit, and writes the script, when the first entity manager opens.
        EntityManagerFactory factory = EclipseLinkBootstrap.build("none", EclipseLinkBootstrap.class.getClassLoader(), null,
                List.of(AutoKeyed.class, IdentityKeyed.class, SequenceKeyed.class, NamedGeneratorKeyed.class, AutoNamedKeyed.class, AutoTableKeyed.class), scripts);
        try {
            factory.createEntityManager().close();
        } finally {
            factory.close();
        }

        ddl = Files.readString(target, StandardCharsets.UTF_8).toUpperCase(Locale.ROOT);
        Files.deleteIfExists(target);

        assertTrue(!ddl.isBlank(), "the provider wrote no schema script at all");
    }

    @Test
    void identityIsAnIdentityColumn() {
        assertTrue(ddl.contains("IDENTITYKEYEDID INTEGER IDENTITY NOT NULL"),
                "the IDENTITY key is not an identity column:" + System.lineSeparator() + ddl);
    }

    /**
     * AUTO is the counter table over a connection as well, the platform's default sequence,
     * not the server's sequence and not an identity column.
     */
    @Test
    void autoIsTheCounterTableOverAConnectionToo() {
        assertTrue(ddl.contains("CREATE TABLE SEQUENCE (SEQ_NAME"),
                "the script has no counter table:" + System.lineSeparator() + ddl);
        assertTrue(ddl.contains("AUTOKEYEDID INTEGER NOT NULL"),
                "the AUTO key is not a plain column:" + System.lineSeparator() + ddl);
    }

    /** SEQUENCE with no generator named takes the implementation's default sequence. */
    @Test
    void sequenceWithoutAGeneratorIsTheDefaultSequence() {
        assertTrue(ddl.contains("CREATE SEQUENCE SEQ_GEN_SEQUENCE INCREMENT BY 50"),
                "the script has no default sequence stepping by 50:" + System.lineSeparator() + ddl);
    }

    /** A generator that names no sequence names it after itself. */
    @Test
    void aGeneratorWithoutASequenceNameNamesTheSequence() {
        assertTrue(ddl.contains("CREATE SEQUENCE KEYED_GEN INCREMENT BY 50"),
                "the script has no sequence keyed_gen stepping by 50:" + System.lineSeparator() + ddl);
    }

    /** AUTO that names a table generator is that generator: its counter table. */
    @Test
    void autoWithANamedTableGeneratorIsThatTable() {
        assertTrue(ddl.contains("CREATE TABLE AUTOCOUNTERS (COUNTERNAME"),
                "the script has no counter table AutoCounters:" + System.lineSeparator() + ddl);
        assertTrue(ddl.contains("AUTOTABLEKEYEDID INTEGER NOT NULL"),
                "the AUTO key with a named table generator is not a plain column:" + System.lineSeparator() + ddl);
    }

    /** AUTO that names a sequence generator is that generator: its sequence, its step. */
    @Test
    void autoWithANamedGeneratorIsThatGenerator() {
        assertTrue(ddl.contains("CREATE SEQUENCE AUTONAMED_SEQ INCREMENT BY 20"),
                "the script has no sequence AutoNamed_Seq stepping by 20:" + System.lineSeparator() + ddl);
        assertTrue(ddl.contains("AUTONAMEDKEYEDID INTEGER NOT NULL"),
                "the AUTO key with a named generator is not a plain column:" + System.lineSeparator() + ddl);
    }
}
