package cz.stochel.ormconvertor.javatests.hibernate;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import java.util.List;
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
     * For classes compiled from generated artifacts: the loader of the scenario is applied
     * to the bootstrap registry, so everything Hibernate resolves by name inside the
     * mapping is looked up where those classes live.
     */
    public static SessionFactory build(String schemaAction, ClassLoader loader, List<Class<?>> entities) {
        BootstrapServiceRegistryBuilder bootstrap = new BootstrapServiceRegistryBuilder();
        if (loader != null) {
            bootstrap.applyClassLoader(loader);
        }

        StandardServiceRegistry registry = new StandardServiceRegistryBuilder(bootstrap.build())
                .applySetting("jakarta.persistence.jdbc.url", TestDatabase.jdbcUrl())
                .applySetting("hibernate.default_schema", TestDatabase.schemaName())
                .applySetting("jakarta.persistence.schema-generation.database.action", schemaAction)
                .build();
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
