package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The other half of the profile fact {@code RefusesBinaryVersion} (decision 179), checked
 * against the release this JVM loads: Hibernate 7.4.5 deploys a unit whose {@code @Version}
 * attribute is a {@code byte[]}, where EclipseLink 5.0.0 refuses it - so the builder keeps
 * {@code @Version} in this profile. Measured by the experiment run of 2026-10-24, which
 * accepted the same artifacts here. Nothing is executed against the tables.
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

    @Test
    void aVersionOverAByteArrayDeploys() {
        assertDoesNotThrow(() -> {
            SessionFactory factory = HibernateBootstrap.build("none", BvStamped.class);
            factory.close();
        });
    }
}
