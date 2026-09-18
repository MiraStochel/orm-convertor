package cz.stochel.ormconvertor.javatests.eclipselink;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.SharedCacheMode;
import jakarta.persistence.ValidationMode;
import jakarta.persistence.spi.ClassTransformer;
import jakarta.persistence.spi.PersistenceUnitInfo;
import jakarta.persistence.spi.PersistenceUnitTransactionType;
import java.net.URL;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import javax.sql.DataSource;
import org.eclipse.persistence.jpa.PersistenceProvider;

/**
 * Building an EclipseLink {@code EntityManagerFactory} over a set of annotated classes -
 * the third verification level for an EclipseLink artifact (decisions 016 and 080): the
 * implementation itself says whether it accepts the mapping, and it says no by throwing.
 *
 * <p>The tutorial bootstraps EclipseLink from a {@code persistence.xml}, which is the
 * usual way and no use here: the classes exist only in a temporary directory of the
 * scenario, under a loader of its own. The specification's other way is this one - the
 * container's: a {@link PersistenceUnitInfo} built in code, handed straight to the
 * provider, naming the classes and the loader. Everything else stays what the tutorial
 * verified: the standard JDBC properties, no platform stated (EclipseLink reads it from
 * the connection), and no weaving, which is exactly the deployment decision 080 warns
 * about and the reason this suite claims nothing about lazy references.
 */
public final class EclipseLinkBootstrap {

    private EclipseLinkBootstrap() {
    }

    /** For classes of this suite, loaded by its own loader. */
    public static EntityManagerFactory build(String schemaAction, Class<?>... entities) {
        return build(schemaAction, EclipseLinkBootstrap.class.getClassLoader(), null, List.of(entities), Map.of());
    }

    /** For classes compiled from generated artifacts, under the loader of their scenario. */
    public static EntityManagerFactory build(String schemaAction, ClassLoader loader, URL root, List<Class<?>> entities) {
        return build(schemaAction, loader, root, entities, Map.of());
    }

    /**
     * The whole call. Extra properties are the caller's own.
     */
    public static EntityManagerFactory build(
            String schemaAction, ClassLoader loader, URL root, List<Class<?>> entities, Map<String, Object> extra) {

        return new PersistenceProvider().createContainerEntityManagerFactory(
                unit(loader, root, entities), properties(schemaAction, loader, extra));
    }

    /**
     * The DDL of these classes as a script, written to the given file, with nothing created
     * in the database. It is the provider's own call for exactly this - schema generation
     * without a factory - so the script does not depend on when a unit happens to deploy,
     * and it is the method the EclipseLink tutorial measured its defaults with.
     */
    public static void generateScript(Path target, List<Class<?>> entities) {
        ClassLoader loader = EclipseLinkBootstrap.class.getClassLoader();

        Map<String, Object> properties = common(loader);
        properties.put("jakarta.persistence.schema-generation.database.action", "none");
        properties.put("jakarta.persistence.schema-generation.scripts.action", "create");
        properties.put("jakarta.persistence.schema-generation.scripts.create-target", target.toString());

        // No connection at all, which is what makes this measurable anywhere: EclipseLink
        // would otherwise try to read the platform from JDBC metadata and fail with 4021.
        // The standard property is what convinces it not to connect - the tutorial's sixth
        // step measured that the vendor's own target-database is not enough by itself.
        properties.put("jakarta.persistence.database-product-name", "Microsoft SQL Server");
        properties.put("eclipselink.target-database", "SQLServer");

        new PersistenceProvider().generateSchema(unit(loader, null, entities), properties);
    }

    /** The unit the provider would otherwise read from a persistence.xml. */
    private static PersistenceUnitInfo unit(ClassLoader loader, URL root, List<Class<?>> entities) {
        List<String> names = new ArrayList<>();
        for (Class<?> entity : entities) {
            names.add(entity.getName());
        }

        return new Unit(loader, rootOrOwn(root), names);
    }

    private static Map<String, Object> properties(String schemaAction, ClassLoader loader, Map<String, Object> extra) {
        Map<String, Object> properties = common(loader);
        properties.put("jakarta.persistence.jdbc.url", TestDatabase.jdbcUrl());
        properties.put("jakarta.persistence.schema-generation.database.action", schemaAction);

        // Measured on the first run (2026-09-18): EclipseLink does not hand the URL to the
        // driver as it stands the way Hibernate does - it builds its own connection
        // properties and sends an empty user, which SQL Server refuses with 18456. The
        // credentials the URL carries therefore have to be given as properties as well.
        if (TestDatabase.jdbcUser() != null) {
            properties.put("jakarta.persistence.jdbc.user", TestDatabase.jdbcUser());
        }
        if (TestDatabase.jdbcPassword() != null) {
            properties.put("jakarta.persistence.jdbc.password", TestDatabase.jdbcPassword());
        }

        properties.putAll(extra);

        return properties;
    }

    /** What every call sets, connection or no connection. */
    private static Map<String, Object> common(ClassLoader loader) {
        Map<String, Object> properties = new HashMap<>();

        // The provider looks classes up by name; without this it would use the loader that
        // loaded EclipseLink itself, which has never seen the scenario's temporary directory.
        properties.put("eclipselink.classloader", loader);
        properties.put("eclipselink.logging.level", "OFF");
        properties.put("eclipselink.weaving", "false");

        return properties;
    }

    /**
     * The root of the unit. It may look optional - nothing is scanned from it, because the
     * classes are listed - but EclipseLink builds the unit's name out of it and fails with
     * a {@code NullPointerException} on a null one, measured on the first run of this suite
     * (2026-09-18). A scenario passes the directory its classes were compiled into; a caller
     * with no directory of its own gets the one this class itself was loaded from.
     */
    private static URL rootOrOwn(URL root) {
        if (root != null) {
            return root;
        }

        var source = EclipseLinkBootstrap.class.getProtectionDomain().getCodeSource();
        if (source == null || source.getLocation() == null) {
            throw new IllegalStateException(
                    "The persistence unit has no root URL and this class has no code source to borrow one from.");
        }

        return source.getLocation();
    }

    /**
     * The persistence unit the provider would otherwise read from a persistence.xml. Every
     * method answers what this suite means: resource-local transactions, no data source,
     * no mapping file, and exactly the classes handed in - nothing is scanned.
     */
    private record Unit(ClassLoader loader, URL root, List<String> classNames) implements PersistenceUnitInfo {

        @Override
        public String getPersistenceUnitName() {
            return "ormconvertor-generated";
        }

        @Override
        public String getPersistenceProviderClassName() {
            return PersistenceProvider.class.getName();
        }

        // The interface still declares the spi enum that 3.2 deprecated for removal, so the
        // implementation has to name it; the warning is the specification's, not ours.
        @Override
        @SuppressWarnings("removal")
        public PersistenceUnitTransactionType getTransactionType() {
            return PersistenceUnitTransactionType.RESOURCE_LOCAL;
        }

        @Override
        public DataSource getJtaDataSource() {
            return null;
        }

        @Override
        public DataSource getNonJtaDataSource() {
            return null;
        }

        @Override
        public List<String> getMappingFileNames() {
            return List.of();
        }

        /** Jakarta Persistence 3.2 asks every unit for its CDI scope and qualifiers; this one has neither. */
        @Override
        public String getScopeAnnotationName() {
            return null;
        }

        @Override
        public List<String> getQualifierAnnotationNames() {
            return List.of();
        }

        @Override
        public List<URL> getJarFileUrls() {
            return List.of();
        }

        @Override
        public URL getPersistenceUnitRootUrl() {
            return root;
        }

        @Override
        public List<String> getManagedClassNames() {
            return classNames;
        }

        @Override
        public boolean excludeUnlistedClasses() {
            return true;
        }

        @Override
        public SharedCacheMode getSharedCacheMode() {
            return SharedCacheMode.UNSPECIFIED;
        }

        @Override
        public ValidationMode getValidationMode() {
            return ValidationMode.NONE;
        }

        @Override
        public Properties getProperties() {
            return new Properties();
        }

        @Override
        public String getPersistenceXMLSchemaVersion() {
            return "3.2";
        }

        @Override
        public ClassLoader getClassLoader() {
            return loader;
        }

        @Override
        public void addTransformer(ClassTransformer transformer) {
            // No weaving: the transformer is what an agent would apply, and there is none
            // here on purpose (decision 080).
        }

        @Override
        public ClassLoader getNewTempClassLoader() {
            return loader;
        }
    }
}
