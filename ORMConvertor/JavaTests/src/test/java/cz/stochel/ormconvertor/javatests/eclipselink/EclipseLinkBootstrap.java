package cz.stochel.ormconvertor.javatests.eclipselink;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.SharedCacheMode;
import jakarta.persistence.ValidationMode;
import jakarta.persistence.spi.ClassTransformer;
import jakarta.persistence.spi.PersistenceUnitInfo;
import jakarta.persistence.spi.PersistenceUnitTransactionType;
import java.net.URL;
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
     * The whole call. Extra properties are the caller's own - a scenario that wants the
     * DDL as a script rather than against the database says so there.
     */
    public static EntityManagerFactory build(
            String schemaAction, ClassLoader loader, URL root, List<Class<?>> entities, Map<String, Object> extra) {

        Map<String, Object> properties = new HashMap<>();
        properties.put("jakarta.persistence.jdbc.url", TestDatabase.jdbcUrl());
        properties.put("jakarta.persistence.schema-generation.database.action", schemaAction);

        // The provider looks classes up by name; without this it would use the loader that
        // loaded EclipseLink itself, which has never seen the scenario's temporary directory.
        properties.put("eclipselink.classloader", loader);
        properties.put("eclipselink.logging.level", "OFF");
        properties.put("eclipselink.weaving", "false");
        properties.putAll(extra);

        List<String> names = new ArrayList<>();
        for (Class<?> entity : entities) {
            names.add(entity.getName());
        }

        return new PersistenceProvider()
                .createContainerEntityManagerFactory(new Unit(loader, root, names), properties);
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

        @Override
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
