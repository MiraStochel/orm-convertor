package cz.stochel.ormconvertor.javatests.hibernate;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import java.util.List;
import java.util.Map;
import org.hibernate.SessionFactory;
import org.hibernate.boot.MetadataSources;
import org.hibernate.boot.registry.BootstrapServiceRegistryBuilder;
import org.hibernate.boot.registry.StandardServiceRegistry;
import org.hibernate.boot.registry.StandardServiceRegistryBuilder;

/**
 * Building a Hibernate {@code SessionFactory} over a set of annotated classes - the third
 * verification level for a Hibernate artifact (decision 016): the framework itself says
 * whether it accepts the mapping, and it says no by throwing.
 *
 * <p>The bootstrap is the one from the tutorial, verified against the pinned release: the
 * standard JPA property names, no dialect (Hibernate reads it from JDBC metadata), the
 * suite's schema as the default one. Hand-written entities and generated ones go through
 * the same call; the only difference is the class loader, which for a generated artifact
 * is the scenario's own (decision 078).
 */
public final class HibernateBootstrap {

    private HibernateBootstrap() {
    }

    /** For classes of this suite, loaded by its own loader. */
    public static SessionFactory build(String schemaAction, Class<?>... entities) {
        return build(schemaAction, null, List.of(entities));
    }

    /**
     * For a claim about a setting of the consumer's configuration: the same bootstrap with
     * the given settings applied over the standard ones.
     */
    public static SessionFactory build(String schemaAction, Map<String, String> settings, Class<?>... entities) {
        return build(schemaAction, null, List.of(entities), TestDatabase.jdbcUrl(), TestDatabase.schemaName(), settings);
    }

    /**
     * For classes compiled from generated artifacts: the loader of the scenario is applied
     * to the bootstrap registry, so everything Hibernate resolves by name inside the
     * mapping is looked up where those classes live.
     */
    public static SessionFactory build(String schemaAction, ClassLoader loader, List<Class<?>> entities) {
        return build(schemaAction, loader, entities, TestDatabase.jdbcUrl(), TestDatabase.schemaName());
    }

    /**
     * The same over another database: LdbcSnb, whose tables the judge of the LDBC catalog
     * reads in dbo (decision 117).
     */
    public static SessionFactory build(
            String schemaAction, ClassLoader loader, List<Class<?>> entities, String jdbcUrl, String defaultSchema) {
        return build(schemaAction, loader, entities, jdbcUrl, defaultSchema, Map.of());
    }

    private static SessionFactory build(
            String schemaAction, ClassLoader loader, List<Class<?>> entities, String jdbcUrl, String defaultSchema,
            Map<String, String> settings) {
        BootstrapServiceRegistryBuilder bootstrap = new BootstrapServiceRegistryBuilder();
        if (loader != null) {
            bootstrap.applyClassLoader(loader);
        }

        StandardServiceRegistryBuilder builder = new StandardServiceRegistryBuilder(bootstrap.build())
                .applySetting("jakarta.persistence.jdbc.url", jdbcUrl)
                .applySetting("hibernate.default_schema", defaultSchema)
                .applySetting("jakarta.persistence.schema-generation.database.action", schemaAction);
        settings.forEach(builder::applySetting);
        StandardServiceRegistry registry = builder.build();
        try {
            MetadataSources sources = new MetadataSources(registry);
            for (Class<?> entity : entities) {
                sources.addAnnotatedClass(entity);
            }
            return sources.buildMetadata().buildSessionFactory();
        } catch (RuntimeException e) {
            StandardServiceRegistryBuilder.destroy(registry);
            throw e;
        }
    }
}
