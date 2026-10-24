package cz.stochel.ormconvertor.javatests.foreign;

import com.sun.source.util.JavacTask;
import cz.stochel.ormconvertor.javatests.CaseStudyDatabase;
import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
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
import jakarta.persistence.Query;
import java.io.StringReader;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Parameter;
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
import org.apache.ibatis.annotations.Param;
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
 * third level; a project of a case study runs every query over the empty tables of its
 * schema (decision 176).
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
                artifact.producer(), artifact.language(), artifact.metadata());

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

                // Every query of the artifact is created against the model - the one of a query
                // artifact, each of a project's -, nothing executed.
                if (jpa != null) {
                    for (Method method : compiled.projectMethods()) {
                        accept(jpa, method);
                    }
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

            // Level 4 of a project whose tables stand in the fixture (decision 176): every query
            // runs over the empty tables of the project's schema. Runnable is what is measured;
            // there is no measure of equivalence, so the outcome is "ran" and agrees stays empty.
            if (compiled.method() == null && artifact.isProject() && measure.runsProject()) {
                started = System.nanoTime();
                runProject(artifact, compiled, jpa, myBatis, verdict);
                times[3] = (System.nanoTime() - started) / 1e6;
                report.run();
                return verdict;
            }

            if (compiled.method() == null || measure.fields() == null) {
                verdict.stop(ForeignVerdict.ACCEPTED, compiled.method() != null
                        ? measure.noFourthLevel()
                        : artifact.isProject()
                                ? "the artifact is a project of " + artifact.queryMethods().size() + " queries, judged to the third level as one; "
                                        + (measure.noFourthLevel() != null ? measure.noFourthLevel() : "a project of this key has no measure at the fourth")
                                : "the artifact carries no query, so there is nothing to run");
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

    /**
     * The compiled artifact: the entity classes under the scenario's loader and, where there is a
     * query, its method or its mapper interface; for a project, every query's method, each in a
     * class of its own, with the path of the unit it came from beside it, and no single method.
     */
    private record Compiled(ClassLoader loader, List<Class<?>> mapped, JavaProject queries, Method method, Class<?> mapper, List<Method> projectMethods, List<String> projectPaths) {
    }

    /**
     * The fourth level of a project (decision 176): every query of the project run with a value of
     * the declared type for every parameter - as the third level creates it -, a JPA query through
     * {@code getResultList}, a MyBatis statement by its id in the document that declares it. The
     * first query that fails stops the project at {@code failed} and the detail names it; otherwise
     * the project {@code ran}, the row carries the rows of all its queries - over empty tables that
     * is the aggregates' one row each, and a row from anywhere else would say the catalog bound an
     * entity to a table that holds data.
     */
    private static void runProject(ForeignArtifact artifact, Compiled compiled, EntityManagerFactory jpa, SqlSessionFactory myBatis, ForeignVerdict verdict) {
        int rows = 0;
        List<String> returning = new ArrayList<>();
        List<String> mappers = artifact.mappings().stream().map(ForeignArtifact.Unit::content).toList();

        for (int index = 0; index < compiled.projectMethods().size(); index++) {
            Method method = compiled.projectMethods().get(index);
            String path = compiled.projectPaths().get(index);
            try {
                int count = artifact.target() == Orm.MYBATIS
                        ? countMyBatis(myBatis, mappers, method)
                        : countJpa(jpa, method);
                rows += count;
                if (count > 0) {
                    returning.add(path + " (" + count + ")");
                }
            } catch (Throwable e) {
                verdict.stop(ForeignVerdict.FAILED, path + ": " + innermost(e).getMessage());
                return;
            }
        }

        verdict.ran(rows, compiled.projectMethods().size() + " queries ran over the empty tables of the project's schema"
                + (returning.isEmpty() ? "" : "; rows came back from " + String.join(", ", returning)));
    }

    private static int countJpa(EntityManagerFactory jpa, Method method) throws Exception {
        try (EntityManager manager = jpa.createEntityManager()) {
            Type[] types = method.getGenericParameterTypes();
            Object[] values = new Object[types.length];
            values[0] = manager;
            for (int index = 1; index < types.length; index++) {
                values[index] = PreparedJavaQuery.sample(types[index]);
            }

            Object query = method.invoke(null, values);
            if (!(query instanceof Query typed)) {
                throw new IllegalStateException(method.getName() + " returned no query.");
            }
            return typed.getResultList().size();
        } catch (InvocationTargetException e) {
            throw e.getCause() instanceof Exception cause ? cause : e;
        }
    }

    /**
     * A statement of a project's mapper, run by its id: the document whose select carries the
     * method's name gives the namespace (the tool writes one document per statement), and the
     * parameter is what MyBatis would bind for the method - the one value of a single unnamed
     * parameter, otherwise a map by the names of {@code @Param}.
     */
    private static int countMyBatis(SqlSessionFactory myBatis, List<String> mappers, Method method) {
        String id = method.getName();
        String document = mappers.stream()
                .filter(mapper -> mapper.contains("id=\"" + id + "\""))
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("no mapper document declares a statement " + id));
        String statement = MyBatisBootstrap.namespaceOf(document) + "." + id;

        Parameter[] parameters = method.getParameters();
        Object argument;
        if (parameters.length == 0) {
            argument = null;
        } else if (parameters.length == 1 && parameters[0].getAnnotation(Param.class) == null) {
            argument = PreparedJavaQuery.sample(parameters[0].getParameterizedType());
        } else {
            Map<String, Object> named = new HashMap<>();
            for (int index = 0; index < parameters.length; index++) {
                Object value = PreparedJavaQuery.sample(parameters[index].getParameterizedType());
                Param param = parameters[index].getAnnotation(Param.class);
                if (param != null) {
                    named.put(param.value(), value);
                }
                named.put("param" + (index + 1), value);
                named.put("arg" + index, value);
            }
            argument = named;
        }

        try (SqlSession session = myBatis.openSession()) {
            return session.selectList(statement, argument).size();
        }
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

        if (artifact.isProject()) {
            // A project: every query in a class or interface of its own, compiled together over
            // the entities; the compiler names the file of a query that does not compile.
            JavaProject queries = JavaProject.create("foreign-project", entities);
            List<String> wrappers = new ArrayList<>();
            int index = 0;
            for (ForeignArtifact.Unit query : artifact.queryMethods()) {
                index++;
                wrappers.add(queries.add(artifact.target() == Orm.MYBATIS
                        ? JavaSources.wrapMapperInterface(entityPackage, "GeneratedMapper" + index, query.content())
                        : JavaSources.wrapQuery(entityPackage, "GeneratedQueries" + index, query.content())));
            }
            queries.compileAndLoad();

            List<Method> methods = new ArrayList<>();
            List<String> paths = new ArrayList<>();
            for (int position = 0; position < wrappers.size(); position++) {
                for (Method method : queries.load(wrappers.get(position)).getDeclaredMethods()) {
                    methods.add(method);
                    paths.add(artifact.queryMethods().get(position).path());
                }
            }
            return new Compiled(loader, mapped, queries, null, null, methods, paths);
        }

        ForeignArtifact.Unit query = artifact.queryMethod();
        if (query == null) {
            return new Compiled(loader, mapped, null, null, null, List.of(), List.of());
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
            return new Compiled(loader, mapped, queries, mapper.getDeclaredMethods()[0], mapper, List.of(mapper.getDeclaredMethods()[0]), List.of(query.path()));
        }

        String wrapper = queries.add(JavaSources.wrapQuery(entityPackage, "GeneratedQueries", query.content()));
        queries.compileAndLoad();
        Method method = Arrays.stream(queries.load(wrapper).getDeclaredMethods())
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("the generated class declares no method"));
        return new Compiled(loader, mapped, queries, method, null, List.of(method), List.of(query.path()));
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
     * canonical form, the arguments of the query, and what the rows are compared with; or,
     * for the project of a case study, that it runs over the empty tables of its schema in
     * the suite's own database (decision 176).
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
            String noFourthLevel,
            boolean runsProject) {

        Measure(String jdbcUrl, String schema, List<String> fields, boolean projection, List<String> orderingKey,
                ResultRow.Settings settings, Object[] arguments, java.util.function.Function<List<String>, String> comparison, String noFourthLevel) {
            this(jdbcUrl, schema, fields, projection, orderingKey, settings, arguments, comparison, noFourthLevel, false);
        }

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

                case ForeignArtifact.Key.CROSS -> {
                    try {
                        Orm.forName(key.id());
                    } catch (IllegalArgumentException e) {
                        throw new IllegalStateException(artifact.id() + ": the scenario key names the source \"" + key.id() + "\", which is no framework of the tool; the scenarios of CrossFrameworkInputs are one per source framework.");
                    }

                    return new Measure(null, null, null, false, List.of(), ResultRow.Settings.defaults(), new Object[0], rows -> null,
                            "the scenario of CrossFrameworkInputs has no measure at the fourth level - its entity stands over no table of the fixture and its row is stored and read by a scenario written per entity -, so a foreign artifact of it ends at the third level (decision 172)");
                }

                case ForeignArtifact.Key.EXAMPLE -> {
                    // The keys are those of GET /examples; this suite has no instance at hand
                    // when it judges a foreign artifact, so the key is taken as written and the
                    // .NET suite holds it against the catalog. The excerpt of a real project -
                    // the case study of T1 - has its tables in the case-study database (decision
                    // 176), empty, so its project runs there at the fourth level - in the targets
                    // whose mapping carries the schema the catalog supplied. MyBatis writes the
                    // table as the source named it, and a source that names no schema gives SQL
                    // that runs in the default schema of its connection, which is not the
                    // project's; its project ends at the third level and says so.
                    if (TestSchema.isCaseStudy(key.id())) {
                        if (!CaseStudyDatabase.isConfigured()) {
                            throw new IllegalStateException(artifact.id() + ": a case study runs over the tables of its project in the case-study database, and none is configured. " + CaseStudyDatabase.notConfigured());
                        }

                        // The mapping carries the schema the catalog supplied, so the default
                        // schema of the connection is nobody's concern; dbo is what the FIBEN run
                        // passes, and a null setting is what Hibernate's bootstrap refuses.
                        String jdbcUrl = CaseStudyDatabase.jdbcUrl();
                        return artifact.target() == Orm.MYBATIS
                                ? new Measure(jdbcUrl, "dbo", null, false, List.of(), ResultRow.Settings.defaults(), new Object[0], rows -> null,
                                        "the SQL of a MyBatis artifact names the tables as the source did, without a schema, and the tables of the project stand in a schema of their own in the case-study database (decision 176); the project is not run")
                                : new Measure(jdbcUrl, "dbo", null, false, List.of(), ResultRow.Settings.defaults(), new Object[0], rows -> null, null, true);
                    }

                    return new Measure(null, null, null, false, List.of(), ResultRow.Settings.defaults(), new Object[0], rows -> null,
                            "an example written for the tool runs over no database of it (decision 099), so an artifact of it ends at the third level (decision 174)");
                }

                default -> throw new IllegalStateException(artifact.id() + ": the kind " + key.kind() + " has no measure.");
            }
        }
    }
}
