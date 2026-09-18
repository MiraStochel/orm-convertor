package cz.stochel.ormconvertor.javatests.tool;

import java.io.File;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import javax.tools.Diagnostic;
import javax.tools.DiagnosticCollector;
import javax.tools.JavaCompiler;
import javax.tools.JavaFileObject;
import javax.tools.StandardJavaFileManager;
import javax.tools.ToolProvider;

/**
 * The consumer project a generated artifact is handed to: a directory of sources laid out
 * by Java's rule, {@code javac} over the suite's own classpath, and a class loader of the
 * scenario's own. This is verification level 2 for the Java targets (decision 016) and
 * what level 3 needs before it - a framework is given classes loaded into the JVM it runs
 * in, not text.
 *
 * <p>A project of its own per scenario, in a temporary directory removed on close, so that
 * two scenarios cannot see each other's classes; the loader's parent is this suite's, so
 * {@code jakarta.persistence} and Hibernate are the same classes the test itself holds.
 */
public final class JavaProject implements AutoCloseable {

    private final Path sources;
    private final Path classes;
    private final List<Path> files = new ArrayList<>();
    private final List<String> types = new ArrayList<>();

    private URLClassLoader loader;

    private JavaProject(Path root) throws IOException {
        this.sources = Files.createDirectories(root.resolve("src"));
        this.classes = Files.createDirectories(root.resolve("classes"));
    }

    public static JavaProject create(String name) {
        try {
            return new JavaProject(Files.createTempDirectory("ormconvertor-" + name + "-"));
        } catch (IOException e) {
            throw new UncheckedIOException("A temporary directory for the scenario could not be created.", e);
        }
    }

    /**
     * Writes the artifact into the file its content dictates - the public class's name in
     * the directory of its package - and returns the binary name the loader will know it
     * by.
     */
    public String add(String javaSource) {
        String packageName = JavaSources.packageOf(javaSource);
        String className = JavaSources.publicClassOf(javaSource);

        Path directory = packageName == null
                ? sources
                : sources.resolve(packageName.replace('.', File.separatorChar));

        try {
            Files.createDirectories(directory);
            Path file = directory.resolve(className + ".java");
            Files.writeString(file, javaSource, StandardCharsets.UTF_8);
            files.add(file);
        } catch (IOException e) {
            throw new UncheckedIOException("The artifact " + className + " could not be written.", e);
        }

        String binaryName = packageName == null ? className : packageName + "." + className;
        types.add(binaryName);
        return binaryName;
    }

    /** Every added type, loaded - what a framework is handed at verification level 3. */
    public List<Class<?>> loadAll() {
        return types.stream().map(this::load).collect(Collectors.toList());
    }

    /**
     * Compiles everything added so far. The result carries the diagnostics with the file
     * and the line, so a scenario that fails here says which artifact {@code javac}
     * rejected and where - the failure named at its place (decision 078).
     */
    private Compilation compile() {
        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        if (compiler == null) {
            throw new IllegalStateException(
                    "No Java compiler is available in this runtime; the suite needs a JDK, which is what "
                    + "the Maven image and actions/setup-java provide (decision 076).");
        }

        DiagnosticCollector<JavaFileObject> diagnostics = new DiagnosticCollector<>();
        boolean success;

        try (StandardJavaFileManager fileManager =
                     compiler.getStandardFileManager(diagnostics, null, StandardCharsets.UTF_8)) {
            List<String> options = List.of(
                    "-classpath", classpath(),
                    "-d", classes.toString(),
                    "-encoding", "UTF-8");

            success = compiler.getTask(
                    null, fileManager, diagnostics, options, null,
                    fileManager.getJavaFileObjectsFromPaths(files)).call();
        } catch (IOException e) {
            throw new UncheckedIOException("The compilation of the generated artifacts failed to run.", e);
        }

        String report = diagnostics.getDiagnostics().stream()
                .filter(d -> d.getKind() == Diagnostic.Kind.ERROR)
                .map(JavaProject::describe)
                .collect(Collectors.joining(System.lineSeparator()));

        return new Compilation(success, report);
    }

    /** Compiles and returns the loader over the result; fails with the diagnostics. */
    public ClassLoader compileAndLoad() {
        Compilation compilation = compile();
        if (!compilation.success()) {
            throw new AssertionError("javac rejected the generated artifacts:"
                    + System.lineSeparator() + compilation.errors());
        }
        return loader();
    }

    public ClassLoader loader() {
        if (loader == null) {
            try {
                loader = new URLClassLoader(
                        new URL[] {classes.toUri().toURL()},
                        JavaProject.class.getClassLoader());
            } catch (IOException e) {
                throw new UncheckedIOException("The class loader over the compiled artifacts failed.", e);
            }
        }
        return loader;
    }

    /**
     * Where the compiled classes are, as a URL. A provider bootstrapped without a
     * persistence.xml is told the root of its unit this way (decision 080).
     */
    public URL rootUrl() {
        try {
            return classes.toUri().toURL();
        } catch (IOException e) {
            throw new UncheckedIOException("The compiled artifacts have no usable URL.", e);
        }
    }

    public Class<?> load(String binaryName) {
        try {
            return loader().loadClass(binaryName);
        } catch (ClassNotFoundException e) {
            throw new IllegalStateException("The compiled artifact " + binaryName + " could not be loaded.", e);
        }
    }

    @Override
    public void close() {
        if (loader != null) {
            try {
                loader.close();
            } catch (IOException e) {
                // Nothing to do about it and nothing depends on it; the directory goes next.
            }
        }

        try (Stream<Path> tree = Files.walk(sources.getParent())) {
            tree.sorted(Comparator.reverseOrder()).forEach(path -> {
                try {
                    Files.deleteIfExists(path);
                } catch (IOException e) {
                    // A leftover in the system temporary directory is not worth a failure.
                }
            });
        } catch (IOException e) {
            // Likewise.
        }
    }

    /**
     * The suite's own classpath. Surefire hands the test JVM a manifest-only jar, whose
     * {@code Class-Path} {@code javac} follows, and the two frameworks the artifacts
     * reference are added from where their own classes were loaded from - so the artifact
     * compiles against the very releases this suite runs, not against a repeated list.
     */
    private static String classpath() {
        List<String> entries = new ArrayList<>();
        entries.add(System.getProperty("java.class.path"));
        entries.add(locationOf(jakarta.persistence.Entity.class));
        entries.add(locationOf(org.hibernate.annotations.Nationalized.class));

        return entries.stream().filter(e -> e != null && !e.isBlank())
                .collect(Collectors.joining(File.pathSeparator));
    }

    private static String locationOf(Class<?> type) {
        var source = type.getProtectionDomain().getCodeSource();
        if (source == null || source.getLocation() == null) {
            return null;
        }

        try {
            return Path.of(source.getLocation().toURI()).toString();
        } catch (Exception e) {
            return null;
        }
    }

    private static String describe(Diagnostic<? extends JavaFileObject> diagnostic) {
        JavaFileObject file = diagnostic.getSource();
        String name = file == null ? "<no file>" : Path.of(file.getName()).getFileName().toString();
        return name + ":" + diagnostic.getLineNumber() + ": " + diagnostic.getMessage(null);
    }

    /** Whether {@code javac} accepted the artifacts, and what it said when it did not. */
    private record Compilation(boolean success, String errors) {
    }
}
