package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The fact of the profile {@code RefusesBinaryVersion} (decision 179), checked against the
 * release this JVM loads: EclipseLink 5.0.0 refuses a unit whose {@code @Version} attribute
 * is a {@code byte[]} - what EF Core's {@code [Timestamp]} becomes - at deployment with
 * {@code EclipseLink-7168}, the version may be int, Integer, short, Short, long, Long or
 * Timestamp only. The builder therefore writes such a column read-only without
 * {@code @Version}; a version over an integer deploys as always. Found by the experiment run
 * of 2026-10-24 on four examples. Nothing is executed against the tables.
 */
@Tag("integration")
class BinaryVersionClaimsTest {

    @Entity(name = "BvStamped")
    @Table(name = "BvStamped")
    public static class BvStamped {
        @Id
        @Column(name = "id")
        private Integer id;

        @Version
        @Column(name = "rowVersion")
        private byte[] rowVersion;
    }

    @Entity(name = "BvCounted")
    @Table(name = "BvCounted")
    public static class BvCounted {
        @Id
        @Column(name = "id")
        private Integer id;

        @Version
        @Column(name = "revision")
        private Integer revision;
    }

    /** The claim: the unit with a binary version is refused, and the refusal is 7168. */
    @Test
    void aVersionOverAByteArrayIsRefusedAtDeployment() {
        RuntimeException refused = assertThrows(RuntimeException.class, () -> deploy(BvStamped.class));
        assertTrue(mentions(refused, "7168"), String.valueOf(refused.getMessage()));
    }

    /** The same unit over an integer version deploys. */
    @Test
    void aVersionOverAnIntegerDeploys() {
        assertDoesNotThrow(() -> deploy(BvCounted.class));
    }

    private static void deploy(Class<?> entity) {
        EntityManagerFactory factory = EclipseLinkBootstrap.build("none", entity);
        try {
            factory.createEntityManager().close();
        } finally {
            factory.close();
        }
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
