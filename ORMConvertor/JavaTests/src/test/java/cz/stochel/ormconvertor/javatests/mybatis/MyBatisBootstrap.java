package cz.stochel.ormconvertor.javatests.mybatis;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import java.io.ByteArrayInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import javax.sql.DataSource;
import org.apache.ibatis.builder.xml.XMLMapperBuilder;
import org.apache.ibatis.datasource.unpooled.UnpooledDataSource;
import org.apache.ibatis.mapping.Environment;
import org.apache.ibatis.session.Configuration;
import org.apache.ibatis.session.SqlSessionFactory;
import org.apache.ibatis.session.SqlSessionFactoryBuilder;
import org.apache.ibatis.transaction.jdbc.JdbcTransactionFactory;

/**
 * Building a MyBatis {@code SqlSessionFactory} over generated artifacts - the third
 * verification level for a MyBatis artifact (decisions 016 and 084): the framework itself
 * says whether it accepts the mapper, and it says no by throwing while the factory is
 * built. That is the moment the tutorial measured the one finding decision 068 rests on,
 * the same statement declared twice.
 *
 * <p>The bootstrap is programmatic, as EclipseLink's is and for the same reason: the tool
 * does not emit a {@code mybatis-config.xml} - the configuration is a fact of the consumer
 * project (decision 040) and would carry its credentials - so the suite builds the
 * {@link Configuration} in code, adds each mapper document as a source and lets the
 * namespace bind the interface. Nothing of what a configuration would state is assumed:
 * {@code mapUnderscoreToCamelCase} is left off, which is MyBatis's own default and which
 * the generated artifact is independent of anyway, because every column is written out
 * under a closed mapping.
 */
public final class MyBatisBootstrap {

    private static final Pattern NAMESPACE = Pattern.compile("<mapper\\s[^>]*namespace=\"([^\"]+)\"");

    private MyBatisBootstrap() {
    }

    /**
     * A factory over the mapper documents, with the classes they name loaded by the
     * scenario's own loader.
     *
     * <p>The loader reaches MyBatis through the thread's context loader rather than through
     * {@code Resources.setDefaultClassLoader}: the artifacts of a scenario live in a
     * temporary directory under a loader of their own, and the static default would outlive
     * the scenario that set it.
     */
    public static SqlSessionFactory build(ClassLoader loader, List<String> mapperDocuments) {
        ClassLoader previous = Thread.currentThread().getContextClassLoader();
        Thread.currentThread().setContextClassLoader(loader);

        try {
            Configuration configuration = new Configuration(environment());
            configuration.setMapUnderscoreToCamelCase(false);

            for (String document : mapperDocuments) {
                addMapper(configuration, document);
            }

            return new SqlSessionFactoryBuilder().build(configuration);
        } finally {
            Thread.currentThread().setContextClassLoader(previous);
        }
    }

    /**
     * One mapper document. The resource name is the namespace, which is what MyBatis would
     * have called the file on the classpath and what it reports in the message when two
     * documents state the same statement.
     */
    private static void addMapper(Configuration configuration, String document) {
        String namespace = namespaceOf(document);

        try (InputStream stream = new ByteArrayInputStream(document.getBytes(StandardCharsets.UTF_8))) {
            new XMLMapperBuilder(stream, configuration, namespace, configuration.getSqlFragments()).parse();
        } catch (java.io.IOException e) {
            throw new IllegalStateException("The mapper document " + namespace + " could not be read.", e);
        }
    }

    /** The namespace a mapper document declares - the name of the interface it belongs to. */
    public static String namespaceOf(String document) {
        Matcher matcher = NAMESPACE.matcher(document);
        if (!matcher.find()) {
            throw new IllegalArgumentException(
                    "The mapper document declares no namespace:" + System.lineSeparator() + document);
        }
        return matcher.group(1);
    }

    /** The last segment of a namespace: the simple name of the mapper interface. */
    public static String interfaceNameOf(String document) {
        String namespace = namespaceOf(document);
        int lastDot = namespace.lastIndexOf('.');
        return lastDot < 0 ? namespace : namespace.substring(lastDot + 1);
    }

    /**
     * The connection, built from the same JDBC URL every other part of the suite uses.
     * MyBatis's own unpooled data source wants the driver class by name - a property of its
     * built-in source rather than of the framework, which the tutorial's sixth step noted.
     */
    private static Environment environment() {
        DataSource dataSource = new UnpooledDataSource(
                "com.microsoft.sqlserver.jdbc.SQLServerDriver",
                TestDatabase.jdbcUrl(),
                TestDatabase.jdbcUser(),
                TestDatabase.jdbcPassword());

        return new Environment("ormconvertor-generated", new JdbcTransactionFactory(), dataSource);
    }
}
