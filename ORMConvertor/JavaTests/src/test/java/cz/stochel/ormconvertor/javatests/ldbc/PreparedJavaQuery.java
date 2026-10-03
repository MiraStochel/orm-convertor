package cz.stochel.ormconvertor.javatests.ldbc;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.eclipselink.EclipseLinkBootstrap;
import cz.stochel.ormconvertor.javatests.hibernate.HibernateBootstrap;
import cz.stochel.ormconvertor.javatests.mybatis.MyBatisBootstrap;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Parameter;
import java.sql.Timestamp;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.apache.ibatis.annotations.Param;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;

/**
 * A generated query of a Java target, compiled once and run as often as the replay of the
 * validation set needs (decision 117) - one artifact for every read of its operation. The
 * entity artifacts of a target come out the same for every query of the catalog, so they are
 * compiled once per target and the framework's factory is built over them once, as
 * {@code JavaQueryRunner} does for the differential matrix; every query compiles its method in
 * a project of its own on top. A run takes an EntityManager or a session of its own and closes
 * it, so nothing a run does stays open after it.
 */
final class PreparedJavaQuery implements AutoCloseable {

    private static final Map<String, Domain> DOMAINS = new LinkedHashMap<>();

    private final int target;
    private final JavaProject project;
    private final Method method;
    private final EntityManagerFactory jpa;
    private final SqlSessionFactory myBatis;
    private final Class<?> mapper;

    private PreparedJavaQuery(int target, JavaProject project, Method method, EntityManagerFactory jpa, SqlSessionFactory myBatis, Class<?> mapper) {
        this.target = target;
        this.project = project;
        this.method = method;
        this.jpa = jpa;
        this.myBatis = myBatis;
        this.mapper = mapper;
    }

    /**
     * Compiles the query of the answer - or the mutation of its query-bearing artifact a
     * negative case supplies ({@link JavaQueryRunner#mutableArtifact}) - over the database
     * the JDBC URL names.
     */
    static PreparedJavaQuery prepare(String name, int target, ConversionResponse response, String mutated, String jdbcUrl) {
        Domain domain = domain(target, response, jdbcUrl);

        if (target == Orm.MYBATIS) {
            List<String> mappers = new ArrayList<>();
            for (ConversionUnit unit : response.artifactsOf(ContentType.XML)) {
                mappers.add(unit.content());
            }

            String original = JavaQueryRunner.mutableArtifact(response, target, name);
            String statementMapper = mutated != null ? mutated : original;
            mappers.set(mappers.indexOf(original), statementMapper);

            JavaProject project = JavaProject.create("ldbc-mybatis", domain.project);
            project.add(JavaSources.wrapMapperInterface(
                    domain.entityPackage,
                    MyBatisBootstrap.interfaceNameOf(statementMapper),
                    response.artifactOf(ContentType.JAVA_QUERY).content()));
            ClassLoader loader = project.compileAndLoad();

            SqlSessionFactory factory = MyBatisBootstrap.build(loader, mappers, jdbcUrl);
            Class<?> mapper = project.load(MyBatisBootstrap.namespaceOf(statementMapper));

            return new PreparedJavaQuery(target, project, mapper.getDeclaredMethods()[0], null, factory, mapper);
        }

        String artifact = mutated != null ? mutated : response.artifactOf(ContentType.JAVA_QUERY).content();

        JavaProject project = JavaProject.create("ldbc-query", domain.project);
        String wrapper = project.add(JavaSources.wrapQuery(domain.entityPackage, "GeneratedQueries", artifact));
        project.compileAndLoad();

        Method method = Arrays.stream(project.load(wrapper).getDeclaredMethods())
                .findFirst()
                .orElseThrow(() -> new AssertionError(name + ": the generated class declares no method"));

        return new PreparedJavaQuery(target, project, method, domain.factory(target), null, null);
    }

    /** The names of the query's parameters, after the framework's handle where there is one (decision 083). */
    List<String> parameterNames() {
        List<String> names = new ArrayList<>();
        Parameter[] parameters = method.getParameters();

        for (int index = target == Orm.MYBATIS ? 0 : 1; index < parameters.length; index++) {
            Param param = parameters[index].getAnnotation(Param.class);
            names.add(param != null ? param.value() : parameters[index].getName());
        }

        return names;
    }

    /**
     * Runs the query with the arguments in the order of its parameters. JPQL and native JPA
     * queries hand a projection back as an object array per row, MyBatis as a map keyed by the
     * projected columns (decision 104).
     */
    List<List<Object>> run(String id, Object[] arguments, List<String> fields) throws Exception {
        try {
            if (target == Orm.MYBATIS) {
                try (SqlSession session = myBatis.openSession()) {
                    Object returned = method.invoke(session.getMapper(mapper), values(arguments, 0));
                    return JavaQueryRunner.rows(withEveryColumn((List<?>) returned, fields), id, fields, false);
                }
            }

            try (EntityManager manager = jpa.createEntityManager()) {
                Object[] values = values(arguments, 1);
                values[0] = manager;

                Object query = method.invoke(null, values);
                List<?> rows = (List<?>) query.getClass().getMethod("getResultList").invoke(query);
                return JavaQueryRunner.rows(rows, id, fields, true);
            }
        } catch (InvocationTargetException e) {
            throw e.getCause() instanceof Exception cause ? cause : e;
        }
    }

    /**
     * The rows of a MyBatis projection with every column the binding names. MyBatis puts no
     * entry into the map of a row for a column whose value is NULL (callSettersOnNulls is off
     * by default, MyBatisBootstrap), and a column NULL in every row of a result - the
     * companies of persons who work nowhere - is then in no map at all. The binding names only
     * columns the text projects, which the catalog's own test holds, so such a column is NULL
     * here rather than missing.
     */
    private static List<?> withEveryColumn(List<?> rows, List<String> fields) {
        List<Object> completed = new ArrayList<>(rows.size());
        for (Object row : rows) {
            if (row instanceof Map<?, ?> map) {
                Map<Object, Object> copy = new LinkedHashMap<>(map);
                for (String field : fields) {
                    copy.putIfAbsent(field, null);
                }
                completed.add(copy);
            } else {
                completed.add(row);
            }
        }
        return completed;
    }

    @Override
    public void close() {
        project.close();
    }

    /** Closes every compiled domain and the factories built over them. */
    static synchronized void closeAll() {
        for (Domain domain : DOMAINS.values()) {
            domain.close();
        }
        DOMAINS.clear();
    }

    /**
     * The arguments as the parameters want them, from the offset on: an integer of the width
     * the parameter declares, a moment as the temporal type it declares - a date where the
     * moment is a midnight and the parameter a date.
     */
    private Object[] values(Object[] arguments, int offset) {
        Class<?>[] types = method.getParameterTypes();
        Object[] values = new Object[types.length];

        for (int index = 0; index < arguments.length; index++) {
            values[index + offset] = convert(arguments[index], types[index + offset]);
        }

        return values;
    }

    private static Object convert(Object value, Class<?> type) {
        if (value == null || type.isInstance(value)) {
            return value;
        }

        if (value instanceof Number number) {
            if (type == long.class || type == Long.class) {
                return number.longValue();
            }
            if (type == int.class || type == Integer.class) {
                return number.intValue();
            }
            if (type == short.class || type == Short.class) {
                return number.shortValue();
            }
        }

        if (value instanceof LocalDateTime moment) {
            if (type == LocalDate.class && moment.toLocalTime().equals(LocalTime.MIDNIGHT)) {
                return moment.toLocalDate();
            }
            if (type == Timestamp.class || type == java.util.Date.class) {
                return Timestamp.valueOf(moment);
            }
        }

        throw new IllegalArgumentException("No conversion of " + value.getClass().getName() + " into the parameter type " + type.getName() + ".");
    }

    private static synchronized Domain domain(int target, ConversionResponse response, String jdbcUrl) {
        List<String> entities = response.artifactsOf(ContentType.JAVA_ENTITY).stream().map(ConversionUnit::content).toList();
        String key = target + "|" + jdbcUrl + "|" + String.join("\u0000", entities);

        return DOMAINS.computeIfAbsent(key, k -> new Domain("ldbc-" + Orm.nameOf(target).toLowerCase(), entities, jdbcUrl));
    }

    /** The entity artifacts of a target, compiled once, and the JPA factory built over them once. */
    private static final class Domain implements AutoCloseable {

        private final String entityPackage;
        private final JavaProject project;
        private final ClassLoader loader;
        private final List<Class<?>> mapped;
        private final String jdbcUrl;

        private EntityManagerFactory hibernate;
        private EntityManagerFactory eclipseLink;

        Domain(String name, List<String> entities, String jdbcUrl) {
            this.jdbcUrl = jdbcUrl;
            entityPackage = JavaSources.packageOf(entities.get(0));
            project = JavaProject.create(name);
            for (String entity : entities) {
                project.add(entity);
            }

            loader = project.compileAndLoad();
            mapped = project.loadAll();
        }

        synchronized EntityManagerFactory factory(int target) {
            if (target == Orm.HIBERNATE) {
                if (hibernate == null) {
                    hibernate = HibernateBootstrap.build("none", loader, mapped, jdbcUrl, LdbcDatabase.SCHEMA);
                }
                return hibernate;
            }

            if (eclipseLink == null) {
                Map<String, Object> connection = new HashMap<>();
                connection.put("jakarta.persistence.jdbc.url", jdbcUrl);
                putIfPresent(connection, "jakarta.persistence.jdbc.user", TestDatabase.property(jdbcUrl, "user"));
                putIfPresent(connection, "jakarta.persistence.jdbc.password", TestDatabase.property(jdbcUrl, "password"));
                eclipseLink = EclipseLinkBootstrap.build("none", loader, project.rootUrl(), mapped, connection);
            }
            return eclipseLink;
        }

        private static void putIfPresent(Map<String, Object> properties, String key, String value) {
            if (value != null) {
                properties.put(key, value);
            }
        }

        @Override
        public void close() {
            if (hibernate != null) {
                hibernate.close();
            }
            if (eclipseLink != null) {
                eclipseLink.close();
            }
            project.close();
        }
    }
}
