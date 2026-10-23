package cz.stochel.ormconvertor.javatests.foreign;

import com.sun.source.util.JavacTask;
import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.differential.DifferentialMatrix;
import cz.stochel.ormconvertor.javatests.differential.DifferentialQuery;
import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.differential.ResultRow;
import cz.stochel.ormconvertor.javatests.eclipselink.EclipseLinkBootstrap;
import cz.stochel.ormconvertor.javatests.fiben.FibenDatabase;
import cz.stochel.ormconvertor.javatests.fiben.FibenRun;
import cz.stochel.ormconvertor.javatests.hibernate.HibernateBootstrap;
import cz.stochel.ormconvertor.javatests.ldbc.PreparedJavaQuery;
import cz.stochel.ormconvertor.javatests.mybatis.MyBatisBootstrap;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.JavaProject;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import java.io.StringReader;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Type;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Consumer;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import javax.tools.Diagnostic;
import javax.tools.DiagnosticCollector;
import javax.tools.JavaCompiler;
import javax.tools.JavaFileObject;
import javax.tools.SimpleJavaFileObject;
import javax.tools.ToolProvider;
import javax.xml.parsers.DocumentBuilderFactory;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;
import org.xml.sax.InputSource;

/**
 * The levels of decision 016 over an artifact the tool did not make (decision 172), for the
 * three Java targets - the counterpart of {@code ForeignArtifactRun.cs}. The first level is a
 * reading by the language: {@code javac} parses the Java, the XML parser the mapper. From the
 * second level on the artifact goes through what the tool's own artifact goes through: the
 * consumer project of {@link JavaProject}, the framework's factory, one run and the canonical
 * form, compared with the same measure the tool's artifact is - the canonical result of the
 * matrix query, or the judge of the FIBEN catalog. A query of the LDBC catalog ends at the
 * third level.
 *
 * <p>Nothing here asserts: a red level is a row. What throws is the path itself - a key no
 * measure answers, a target the other suite owns, a measure whose database is not configured.
 */
public final class ForeignArtifactRun {

    private static final Pattern LDBC_KEY = Pattern.compile("(?i)^(is|ic|bi)\\d+$");

    private ForeignArtifactRun() {
    }

    public static boolean owns(int target) {
        return JavaQueryRunner.owns(target);
    }

    /** Runs one artifact through the levels; the callback sees the verdict after every level. */
    public static ForeignVerdict run(ForeignArtifact artifact, Consumer<ForeignVerdict> progress) throws Exception {
        if (!owns(artifact.target())) {
            throw new IllegalStateException(artifact.id() + ": " + Orm.nameOf(artifact.target()) + " is not a framework this suite runs; the artifact is the .NET suite's.");
        }

        Measure measure = Measure.of(artifact);
        ForeignVerdict verdict = new ForeignVerdict(
                artifact.id(), artifact.key().toString(), Orm.nameOf(artifact.source()), Orm.nameOf(artifact.target()),
                artifact.producer(), artifact.metadata());

        double[] times = new double[4];
        Runnable report = () -> {
            verdict.times(times[0], times[1], times[2], times[3]);
            if (progress != null) {
                progress.accept(verdict);
            }
        };

        // Level 1: the language reads every unit.
        long started = System.nanoTime();
        String unreadable = unreadable(artifact);
        times[0] = (System.nanoTime() - started) / 1e6;
        if (unreadable != null) {
            verdict.stop(ForeignVerdict.UNREADABLE, unreadable);
            report.run();
            return verdict;
        }
        verdict.reached(ForeignVerdict.LEVEL_READ);
        report.run();

        JavaProject entities = JavaProject.create("foreign-" + sanitize(artifact.id()));
        JavaProject queries = null;
        EntityManagerFactory jpa = null;
        SqlSessionFactory myBatis = null;
        try {
            // Level 2: the units compile under the classpath of the consumer project.
            started = System.nanoTime();
            Compiled compiled;
            try {
                compiled = compile(artifact, entities);
            } catch (AssertionError | IllegalArgumentException | IllegalStateException e) {
                times[1] = (System.nanoTime() - started) / 1e6;
                verdict.stop(ForeignVerdict.NOT_COMPILED, e.getMessage());
                report.run();
                return verdict;
            }
            queries = compiled.queries();
            times[1] = (System.nanoTime() - started) / 1e6;
            verdict.reached(ForeignVerdict.LEVEL_COMPILED);
            report.run();

            // Level 3: the framework accepts the artifact.
            started = System.nanoTime();
            try {
                List<String> mappers = artifact.mappings().stream().map(ForeignArtifact.Unit::content).toList();
                if (artifact.target() == Orm.MYBATIS) {
                    // The mapper interface lives in the query project, so its loader - a child
                    // of the entities' - is the one MyBatis resolves the namespace of a document by.
                    ClassLoader mapperLoader = compiled.queries() != null ? compiled.queries().loader() : compiled.loader();
                    myBatis = measure.jdbcUrl() == null
                            ? MyBatisBootstrap.build(mapperLoader, mappers)
                            : MyBatisBootstrap.build(mapperLoader, mappers, measure.jdbcUrl());
                } else if (artifact.target() == Orm.HIBERNATE) {
                    jpa = measure.jdbcUrl() == null
                            ? HibernateBootstrap.build("none", compiled.loader(), compiled.mapped())
                            : HibernateBootstrap.build("none", compiled.loader(), compiled.mapped(), measure.jdbcUrl(), measure.schema());
                } else {
                    jpa = measure.jdbcUrl() == null
                            ? EclipseLinkBootstrap.build("none", compiled.loader(), entities.rootUrl(), compiled.mapped())
                            : EclipseLinkBootstrap.build("none", compiled.loader(), entities.rootUrl(), compiled.mapped(), connectionOf(measure.jdbcUrl()));
                }

                if (jpa != null && compiled.method() != null) {
                    accept(jpa, compiled.method());
                }
            } catch (Throwable e) {
                times[2] = (System.nanoTime() - started) / 1e6;
                verdict.stop(ForeignVerdict.NOT_ACCEPTED, String.valueOf(innermost(e).getMessage()));
                report.run();
                return verdict;
            }
            times[2] = (System.nanoTime() - started) / 1e6;
            verdict.reached(ForeignVerdict.LEVEL_ACCEPTED);
            report.run();

            if (compiled.method() == null || measure.fields() == null) {
                verdict.stop(ForeignVerdict.ACCEPTED, compiled.method() == null
                        ? "the artifact carries no query, so there is nothing to run"
                        : measure.noFourthLevel());
                report.run();
                return verdict;
            }

            // Level 4: the query runs and its rows are compared with the measure.
            started = System.nanoTime();
            List<String> rows;
            try {
                List<List<Object>> raw = artifact.target() == Orm.MYBATIS
                        ? runMyBatis(myBatis, compiled.mapper(), compiled.method(), measure, artifact.id())
                        : runJpa(jpa, compiled.method(), measure, artifact.id());
                rows = ResultRow.canonical(raw, measure.settings(), measure.fields(), measure.orderingKey());
            } catch (Throwable e) {
                times[3] = (System.nanoTime() - started) / 1e6;
                verdict.stop(ForeignVerdict.FAILED, String.valueOf(innermost(e).getMessage()));
                report.run();
                return verdict;
            }
            times[3] = (System.nanoTime() - started) / 1e6;

            String difference = measure.compare(rows);
            verdict.ran(rows.size(), difference == null, difference);
            report.run();
            return verdict;
        } finally {
            if (jpa != null) {
                jpa.close();
            }
            if (queries != null) {
                queries.close();
            }
            entities.close();
        }
    }

    /**
     * Why the language does not read the artifact, or null where it reads every unit: the
     * parser of {@code javac} over a Java unit - the query inside the class the consumer puts
     * around it -, the XML parser over a mapping, without fetching the DTD a mapper names.
     * JPQL has no reader apart from the framework's, which the third level is.
     */
    static String unreadable(ForeignArtifact artifact) {
        String entityPackage = artifact.entities().isEmpty() ? null : JavaSources.packageOf(artifact.entities().get(0).content());

        for (ForeignArtifact.Unit unit : artifact.units()) {
            String problem = switch (unit.contentType()) {
                case ContentType.JAVA, ContentType.JAVA_ENTITY -> javaSyntax(unit.content(), unit.path());
                case ContentType.JAVA_QUERY -> javaSyntax(
                        artifact.target() == Orm.MYBATIS
                                ? JavaSources.wrapMapperInterface(entityPackage, "GeneratedMapper", unit.content())
                                : JavaSources.wrapQuery(entityPackage, "GeneratedQueries", unit.content()),
                        unit.path());
                case ContentType.XML -> xmlSyntax(unit.content());
                default -> null;
            };

            if (problem != null) {
                return unit.path() + ": " + problem;
            }
        }

        return null;
    }

    private static String javaSyntax(String source, String path) {
        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        if (compiler == null) {
            throw new IllegalStateException("No Java compiler is available in this runtime; the suite needs a JDK (decision 076).");
        }

        String fileName = path.substring(path.lastIndexOf('/') + 1);
        if (!fileName.endsWith(".java")) {
            fileName = fileName + ".java";
        }

        JavaFileObject file = new SimpleJavaFileObject(URI.create("string:///" + fileName), JavaFileObject.Kind.SOURCE) {
            @Override
            public CharSequence getCharContent(boolean ignoreEncodingErrors) {
                return source;
            }
        };

        DiagnosticCollector<JavaFileObject> diagnostics = new DiagnosticCollector<>();
        try {
            JavacTask task = (JavacTask) compiler.getTask(null, null, diagnostics, List.of("-proc:none"), null, List.of(file));
            task.parse();
        } catch (Exception e) {
            return e.getMessage();
        }

        String errors = diagnostics.getDiagnostics().stream()
                .filter(d -> d.getKind() == Diagnostic.Kind.ERROR)
                .map(d -> d.getMessage(null) + " (" + d.getLineNumber() + ")")
                .collect(Collectors.joining("; "));
        return errors.isEmpty() ? null : errors;
    }

    private static String xmlSyntax(String text) {
        try {
            DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
            factory.setFeature("http://apache.org/xml/features/nonvalidating/load-external-dtd", false);
            factory.setFeature("http://xml.org/sax/features/external-general-entities", false);
            factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false);
            factory.setExpandEntityReferences(false);
            factory.newDocumentBuilder().parse(new InputSource(new StringReader(text)));
            return null;
        } catch (Exception e) {
            return e.getMessage();
        }
    }

    /** The compiled artifact: the entity classes under the scenario's loader and, where there is a query, its method or its mapper interface. */
    private record Compiled(ClassLoader loader, List<Class<?>> mapped, JavaProject queries, Method method, Class<?> mapper) {
    }

    private static Compiled compile(ForeignArtifact artifact, JavaProject entities) {
        if (artifact.entities().isEmpty()) {
            throw new IllegalStateException("the artifact carries no entity class");
        }

        for (ForeignArtifact.Unit entity : artifact.entities()) {
            entities.add(entity.content());
        }
        ClassLoader loader = entities.compileAndLoad();
        List<Class<?>> mapped = entities.loadAll();
        String entityPackage = JavaSources.packageOf(artifact.entities().get(0).content());

        ForeignArtifact.Unit query = artifact.queryMethod();
        if (query == null) {
            return new Compiled(loader, mapped, null, null, null);
        }

        JavaProject queries = JavaProject.create("foreign-query", entities);
        if (artifact.target() == Orm.MYBATIS) {
            String statementMapper = artifact.mappings().stream()
                    .map(ForeignArtifact.Unit::content)
                    .filter(content -> content.contains("<select"))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException("the artifact carries no mapper document with a statement"));

            String wrapper = queries.add(JavaSources.wrapMapperInterface(entityPackage, MyBatisBootstrap.interfaceNameOf(statementMapper), query.content()));
            queries.compileAndLoad();
            Class<?> mapper = queries.load(wrapper);
            return new Compiled(loader, mapped, queries, mapper.getDeclaredMethods()[0], mapper);
        }

        String wrapper = queries.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries", query.content()));
        queries.compileAndLoad();
        Method method = Arrays.stream(queries.load(wrapper).getDeclaredMethods())
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("the generated class declares no method"));
        return new Compiled(loader, mapped, queries, method, null);
    }

    /** The third level of a JPA query: created with a value of the declared type for every parameter, nothing executed - as {@link PreparedJavaQuery#accept()} does. */
    private static void accept(EntityManagerFactory jpa, Method method) throws Exception {
        try (EntityManager manager = jpa.createEntityManager()) {
            Type[] types = method.getGenericParameterTypes();
            Object[] values = new Object[types.length];
            values[0] = manager;
            for (int index = 1; index < types.length; index++) {
                values[index] = PreparedJavaQuery.sample(types[index]);
            }

            if (method.invoke(null, values) == null) {
                throw new IllegalStateException(method.getName() + " returned no query.");
            }
        } catch (InvocationTargetException e) {
            throw e.getCause() instanceof Exception cause ? cause : e;
        }
    }

    private static List<List<Object>> runJpa(EntityManagerFactory jpa, Method method, Measure measure, String id) throws Exception {
        try (EntityManager manager = jpa.createEntityManager()) {
            Object[] arguments = measure.arguments();
            Object[] values = new Object[arguments.length + 1];
            values[0] = manager;
            System.arraycopy(arguments, 0, values, 1, arguments.length);

            if (method.getParameterCount() != values.length) {
                throw new IllegalStateException("the generated method takes " + method.getParameterCount() + " parameters, and the measure binds " + arguments.length + " beside the framework's handle");
            }

            Object query = method.invoke(null, values);
            List<?> rows = (List<?>) query.getClass().getMethod("getResultList").invoke(query);
            return JavaQueryRunner.rows(rows, id, measure.fields(), measure.projection());
        } catch (InvocationTargetException e) {
            throw e.getCause() instanceof Exception cause ? cause : e;
        }
    }

    private static List<List<Object>> runMyBatis(SqlSessionFactory myBatis, Class<?> mapper, Method method, Measure measure, String id) throws Exception {
        try (SqlSession session = myBatis.openSession()) {
            Object[] arguments = measure.arguments();
            if (method.getParameterCount() != arguments.length) {
                throw new IllegalStateException("the generated mapper method takes " + method.getParameterCount() + " parameters and the measure binds " + arguments.length);
            }

            Object returned = method.invoke(session.getMapper(mapper), arguments);
            return JavaQueryRunner.rows(PreparedJavaQuery.withEveryColumn((List<?>) returned, measure.fields()), id, measure.fields(), false);
        } catch (InvocationTargetException e) {
            throw e.getCause() instanceof Exception cause ? cause : e;
        }
    }

    private static Map<String, Object> connectionOf(String jdbcUrl) {
        Map<String, Object> connection = new HashMap<>();
        connection.put("jakarta.persistence.jdbc.url", jdbcUrl);
        String user = TestDatabase.property(jdbcUrl, "user");
        String password = TestDatabase.property(jdbcUrl, "password");
        if (user != null) {
            connection.put("jakarta.persistence.jdbc.user", user);
        }
        if (password != null) {
            connection.put("jakarta.persistence.jdbc.password", password);
        }
        return connection;
    }

    private static String sanitize(String id) {
        return id.replaceAll("[^A-Za-z0-9]", "-").toLowerCase(Locale.ROOT);
    }

    private static Throwable innermost(Throwable e) {
        Throwable current = e;
        while (current instanceof InvocationTargetException && current.getCause() != null) {
            current = current.getCause();
        }
        return current;
    }

    /**
     * What an artifact is measured against: the database its query runs in - null for the
     * suite's own -, the fields of a row, the key of the ordering and the settings of the
     * canonical form, the arguments of the query, and what the rows are compared with.
     */
    private record Measure(
            String jdbcUrl,
            String schema,
            List<String> fields,
            boolean projection,
            List<String> orderingKey,
            ResultRow.Settings settings,
            Object[] arguments,
            java.util.function.Function<List<String>, String> comparison,
            String noFourthLevel) {

        /** The difference from the measure, or null where the rows agree. */
        String compare(List<String> rows) {
            return comparison.apply(rows);
        }

        static Measure of(ForeignArtifact artifact) throws SQLException {
            ForeignArtifact.Key key = artifact.key();
            switch (key.kind()) {
                case ForeignArtifact.Key.MATRIX -> {
                    DifferentialQuery query;
                    try {
                        query = DifferentialMatrix.query(key.id());
                    } catch (RuntimeException e) {
                        throw new IllegalStateException(artifact.id() + ": the matrix has no query " + key.id() + "; the sections of Tests/Database/Differential/matrix.txt are the keys.");
                    }

                    List<String> canonical = query.canonicalResult();
                    Object[] arguments = new Object[query.arguments().size()];
                    for (int index = 0; index < arguments.length; index++) {
                        arguments[index] = query.arguments().get(index).materialize();
                    }

                    return new Measure(null, null, query.fields(), query.projection(), query.orderingKey(), query.settings(), arguments,
                            rows -> rows.equals(canonical) ? null : FibenRun.difference(canonical, rows), null);
                }

                case ForeignArtifact.Key.FIBEN -> {
                    ForeignFibenCatalog.Query query = ForeignFibenCatalog.query(key.id());
                    if (query == null) {
                        throw new IllegalStateException(artifact.id() + ": the FIBEN catalog has no query " + key.id() + ".");
                    }

                    String jdbcUrl = FibenDatabase.jdbcUrl();
                    if (jdbcUrl == null) {
                        throw new IllegalStateException(artifact.id() + ": a FIBEN query runs over the Fiben database, and none is configured; set " + FibenDatabase.JDBC_URL_VARIABLE + ".");
                    }

                    Map<String, ForeignFibenCatalog.Entry> judge = ForeignFibenCatalog.judge(jdbcUrl);
                    if (judge == null) {
                        throw new IllegalStateException(artifact.id() + ": the Fiben database holds no judge, so a FIBEN query has no measure (decision 149).");
                    }

                    String judged = query.key().toLowerCase(Locale.ROOT);
                    return new Measure(jdbcUrl, FibenDatabase.SCHEMA, query.columns(), true, query.orderingKey(), ResultRow.Settings.defaults(), new Object[0],
                            rows -> {
                                ForeignFibenCatalog.Entry entry = judge.get(judged);
                                if (entry == null) {
                                    return "the judge holds no entry for the query - the run it was made from did not run it";
                                }
                                boolean agrees = FibenRun.digest(rows).equals(entry.digest()) && (entry.rows().isEmpty() || entry.rows().equals(rows));
                                if (agrees) {
                                    return null;
                                }
                                return !entry.rows().isEmpty() || rows.size() <= FibenRun.ROWS_KEPT_BY_THE_JUDGE
                                        ? FibenRun.difference(entry.rows(), rows)
                                        : "the judge holds " + entry.digest() + ", the artifact returned " + FibenRun.digest(rows);
                            },
                            null);
                }

                case ForeignArtifact.Key.LDBC -> {
                    if (!LDBC_KEY.matcher(key.id()).matches()) {
                        throw new IllegalStateException(artifact.id() + ": " + key.id() + " is not a key of the LDBC catalog (is1, ic2, bi3, ...).");
                    }

                    return new Measure(null, null, null, false, List.of(), ResultRow.Settings.defaults(), new Object[0], rows -> null,
                            "a query of the LDBC catalog is judged by a replay of the validation set, not by one reading, so a foreign artifact of it ends at the third level (decision 172)");
                }

                default -> throw new IllegalStateException(artifact.id() + ": the kind " + key.kind() + " has no measure.");
            }
        }
    }
}
