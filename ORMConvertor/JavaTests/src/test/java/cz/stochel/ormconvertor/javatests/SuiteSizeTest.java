package cz.stochel.ormconvertor.javatests;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.net.URISyntaxException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;

/**
 * What this suite claims about itself (decision 087). Requirement F12 is verified by three
 * clauses - one command, at least 60 Java tests, at least 20 of them integration tests -
 * and the first two are answered by running the suite at all. The third would otherwise be
 * answered by somebody counting methods by hand, and such a number goes stale with the
 * first test anybody adds. It is counted here instead, so the claim either holds or the
 * build says it does not.
 *
 * <p>An integration test is the fourth verification level of decision 016: a test whose
 * verdict depends on what actually happened in the database, through an artifact the tool
 * emits or through the shape it emits. It carries {@code @Tag("integration")} on the
 * method or on its class, which also makes the set runnable on its own with
 * {@code mvn test -Dgroups=integration}.
 *
 * <p>The count comes from the compiled test classes rather than from the JUnit Platform's
 * discovery: discovery yields the template of a {@code @ParameterizedTest}, not its
 * invocations, which appear only while the suite runs - counting templates would report a
 * fraction of the suite and the claim would quietly fall below the line.
 */
class SuiteSizeTest {

    /** The clauses of the verification criterion of F12, as it is written. */
    private static final int REQUIRED_TESTS = 60;
    private static final int REQUIRED_INTEGRATION_TESTS = 20;

    static final String INTEGRATION_TAG = "integration";

    @Test
    void theSuiteIsAsLargeAsTheRequirementAsks() {
        Tally tally = count();

        assertTrue(tally.total() >= REQUIRED_TESTS,
                "F12 asks for at least " + REQUIRED_TESTS + " Java tests and the suite runs "
                + tally.total() + ":" + System.lineSeparator() + tally.describe());
    }

    @Test
    void enoughOfThemReachTheDatabase() {
        Tally tally = count();

        assertTrue(tally.integration() >= REQUIRED_INTEGRATION_TESTS,
                "F12 asks for at least " + REQUIRED_INTEGRATION_TESTS + " integration tests and the suite runs "
                + tally.integration() + ":" + System.lineSeparator() + tally.describe());
    }

    /** How many invocations the suite runs, and how many of them reach the database. */
    private record Tally(int total, int integration, List<String> lines) {

        String describe() {
            return String.join(System.lineSeparator(), lines);
        }
    }

    private static Tally count() {
        int total = 0;
        int integration = 0;
        List<String> lines = new ArrayList<>();

        for (Class<?> testClass : testClasses()) {
            int classTotal = 0;
            int classIntegration = 0;
            boolean taggedClass = isTagged(testClass.getAnnotationsByType(Tag.class));

            for (Method method : testClass.getDeclaredMethods()) {
                int invocations = invocationsOf(testClass, method);
                if (invocations == 0) {
                    continue;
                }

                classTotal += invocations;
                if (taggedClass || isTagged(method.getAnnotationsByType(Tag.class))) {
                    classIntegration += invocations;
                }
            }

            if (classTotal > 0) {
                lines.add("    %-58s %3d, of them %2d integration"
                        .formatted(testClass.getName(), classTotal, classIntegration));
            }

            total += classTotal;
            integration += classIntegration;
        }

        lines.add("    %-58s %3d, of them %2d integration".formatted("the whole suite", total, integration));

        return new Tally(total, integration, lines);
    }

    /**
     * How many times JUnit runs one method: once for a {@code @Test}, once per constant for
     * a {@code @ParameterizedTest} over an enum, and never for anything else.
     *
     * <p>A parameterization this does not know is a failure rather than a one, on purpose:
     * counting it as a single invocation would understate the suite by however many cases
     * it really has, and the claim of F12 would fall below its line without a word.
     */
    private static int invocationsOf(Class<?> testClass, Method method) {
        if (method.isAnnotationPresent(Test.class)) {
            return 1;
        }

        if (!method.isAnnotationPresent(ParameterizedTest.class)) {
            return 0;
        }

        EnumSource source = method.getAnnotation(EnumSource.class);
        if (source == null) {
            throw new AssertionError(
                    "The parameterized test " + testClass.getName() + "#" + method.getName()
                    + " takes its cases from a source this counter does not know. Teach it that source, "
                    + "or the size the suite claims (decision 087) stops being the size it has.");
        }

        Class<?> constants = source.value();
        if (source.names().length > 0 || !constants.isEnum()) {
            throw new AssertionError(
                    "The parameterized test " + testClass.getName() + "#" + method.getName()
                    + " narrows its enum source, which this counter does not follow (decision 087).");
        }

        return constants.getEnumConstants().length;
    }

    private static boolean isTagged(Tag[] tags) {
        return Stream.of(tags).anyMatch(tag -> INTEGRATION_TAG.equals(tag.value()));
    }

    /** Every compiled class of this suite that declares at least one test method. */
    private static List<Class<?>> testClasses() {
        Path root = compiledClasses();

        try (Stream<Path> files = Files.walk(root)) {
            return files.filter(path -> path.toString().endsWith(".class"))
                    .map(path -> className(root, path))
                    .filter(name -> !name.contains("$"))
                    .map(SuiteSizeTest::load)
                    .filter(type -> !Modifier.isAbstract(type.getModifiers()))
                    .filter(SuiteSizeTest::declaresATest)
                    .sorted(Comparator.comparing(Class::getName))
                    .toList();
        } catch (IOException e) {
            throw new UncheckedIOException("The compiled test classes under " + root + " could not be walked.", e);
        }
    }

    private static boolean declaresATest(Class<?> type) {
        return Stream.of(type.getDeclaredMethods())
                .anyMatch(m -> m.isAnnotationPresent(Test.class) || m.isAnnotationPresent(ParameterizedTest.class));
    }

    private static Path compiledClasses() {
        try {
            return Path.of(SuiteSizeTest.class.getProtectionDomain().getCodeSource().getLocation().toURI());
        } catch (URISyntaxException e) {
            throw new IllegalStateException("The suite cannot locate its own compiled classes.", e);
        }
    }

    private static String className(Path root, Path file) {
        String relative = root.relativize(file).toString();
        return relative.substring(0, relative.length() - ".class".length())
                .replace(java.io.File.separatorChar, '.')
                .replace('/', '.');
    }

    private static Class<?> load(String name) {
        try {
            return Class.forName(name, false, SuiteSizeTest.class.getClassLoader());
        } catch (ClassNotFoundException e) {
            throw new IllegalStateException("The compiled class " + name + " could not be loaded.", e);
        }
    }
}
