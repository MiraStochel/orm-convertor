package cz.stochel.ormconvertor.javatests.tool;

import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * What the language requires of a generated Java artifact before {@code javac} will look
 * at it, and what a consumer project has to add around it.
 *
 * <p>An artifact of the answer carries no file name, so the name is derived from the
 * content: a public top-level type must live in a file of its own name inside a directory
 * matching its package (JLS §7.6). Nothing is invented by that - it is the only layout
 * Java admits, {@code javac} enforces it itself, and it would hold for any consumer. The
 * open item about naming artifacts on the wire is untouched: its question is which entity
 * and which units an artifact came from, and the answer to it would not change the file.
 *
 * <p>A query artifact is a bare method, so the class around it and its imports are added
 * here - the contribution of the consumer project, not of the artifact, exactly as
 * {@code GeneratedQueryCompiler} adds them on the .NET side (decisions 027 and 040).
 */
public final class JavaSources {

    private static final Pattern PACKAGE = Pattern.compile("(?m)^\\s*package\\s+([\\w.]+)\\s*;");

    // Anchored at the start of a line: a nested key class is indented and declared static,
    // so the top-level type of the file is the one this matches. An interface counts as
    // well since decision 084, because a MyBatis mapper is one and Java demands the same
    // file name of it.
    private static final Pattern PUBLIC_CLASS = Pattern.compile("(?m)^public\\s+(?:class|interface)\\s+(\\w+)\\b");

    private JavaSources() {
    }

    /** The package the artifact declares, or null for the default package. */
    public static String packageOf(String source) {
        Matcher matcher = PACKAGE.matcher(source);
        return matcher.find() ? matcher.group(1) : null;
    }

    /** The name of the public top-level class - the name its file must have. */
    public static String publicClassOf(String source) {
        Matcher matcher = PUBLIC_CLASS.matcher(source);
        if (!matcher.find()) {
            throw new IllegalArgumentException(
                    "The artifact declares no public top-level class, so it has no file name:"
                    + System.lineSeparator() + source);
        }
        return matcher.group(1);
    }

    /**
     * The query method inside the class and the imports a consumer project would give it.
     * The class goes into the package of the entities so that the entity the method names
     * is in scope without an import of its own.
     *
     * <p>The types a parameter of the generated method can be declared with are imported
     * too (decision 083): the scalar vocabulary maps onto {@code java.math} and
     * {@code java.time} beside the primitives, and a collection parameter is declared as a
     * {@code Collection}. Unused imports are legal Java, and leaving them out would make a
     * parameterized query fail to compile for a reason that is the consumer project's, not
     * the artifact's.
     */
    public static String wrapQuery(String packageName, String className, String method) {
        StringBuilder source = new StringBuilder();
        if (packageName != null) {
            source.append("package ").append(packageName).append(";").append(System.lineSeparator())
                    .append(System.lineSeparator());
        }

        return source
                .append("import jakarta.persistence.EntityManager;").append(System.lineSeparator())
                .append("import jakarta.persistence.Query;").append(System.lineSeparator())
                .append("import jakarta.persistence.TypedQuery;").append(System.lineSeparator())
                .append("import java.math.BigDecimal;").append(System.lineSeparator())
                .append("import java.time.Duration;").append(System.lineSeparator())
                .append("import java.time.LocalDate;").append(System.lineSeparator())
                .append("import java.time.LocalDateTime;").append(System.lineSeparator())
                .append("import java.time.LocalTime;").append(System.lineSeparator())
                .append("import java.time.OffsetDateTime;").append(System.lineSeparator())
                .append("import java.util.Collection;").append(System.lineSeparator())
                .append("import java.util.UUID;").append(System.lineSeparator())
                .append(System.lineSeparator())
                .append("public class ").append(className).append(" {").append(System.lineSeparator())
                .append(method).append(System.lineSeparator())
                .append("}").append(System.lineSeparator())
                .toString();
    }

    /**
     * The declaration of a mapper method inside the interface a MyBatis consumer project
     * would declare it in (decision 084). The generated artifact is the declaration alone -
     * a fragment, as every query artifact of the tool is - and the interface it belongs to
     * is the consumer's, so the suite writes it the way a consumer would: in the package of
     * the entities, under the name the mapper document's namespace already states, because
     * that is what binds the two halves together.
     */
    public static String wrapMapperInterface(String packageName, String interfaceName, String method) {
        StringBuilder source = new StringBuilder();
        if (packageName != null) {
            source.append("package ").append(packageName).append(";").append(System.lineSeparator())
                    .append(System.lineSeparator());
        }

        return source
                .append("import java.math.BigDecimal;").append(System.lineSeparator())
                .append("import java.time.Duration;").append(System.lineSeparator())
                .append("import java.time.LocalDate;").append(System.lineSeparator())
                .append("import java.time.LocalDateTime;").append(System.lineSeparator())
                .append("import java.time.LocalTime;").append(System.lineSeparator())
                .append("import java.time.OffsetDateTime;").append(System.lineSeparator())
                .append("import java.util.Collection;").append(System.lineSeparator())
                .append("import java.util.List;").append(System.lineSeparator())
                .append("import java.util.UUID;").append(System.lineSeparator())
                .append("import org.apache.ibatis.annotations.Param;").append(System.lineSeparator())
                .append(System.lineSeparator())
                .append("public interface ").append(interfaceName).append(" {").append(System.lineSeparator())
                .append("    ").append(method).append(System.lineSeparator())
                .append("}").append(System.lineSeparator())
                .toString();
    }

    /**
     * The artifact with a constructor of one parameter added to its top-level class: any
     * declared constructor removes the implicit no-arg one, which is the enforced member
     * Jakarta Persistence 3.2 §2.1 requires and which the artifact keeps by declaring no
     * constructor at all (decision 037). Used by the negative half of level 3.
     */
    public static String withDeclaredConstructor(String source) {
        String className = publicClassOf(source);
        Matcher matcher = Pattern.compile("(?m)^public\\s+class\\s+" + className + "\\b[^{]*\\{").matcher(source);
        if (!matcher.find()) {
            throw new IllegalArgumentException("The class header of " + className + " was not found.");
        }

        String constructor = System.lineSeparator()
                + System.lineSeparator()
                + "    public " + className + "(int only) {" + System.lineSeparator()
                + "    }" + System.lineSeparator();

        return source.substring(0, matcher.end()) + constructor + source.substring(matcher.end());
    }

    /**
     * The artifact without the line carrying this annotation. Line-based on purpose: the
     * builder writes one annotation per line, and taking the whole line away leaves a
     * class a human could have written.
     */
    public static String withoutAnnotation(String source, String annotation) {
        int start = source.indexOf(annotation);
        if (start < 0) {
            throw new IllegalArgumentException(
                    "The artifact carries no " + annotation + ":" + System.lineSeparator() + source);
        }

        int lineStart = source.lastIndexOf('\n', start) + 1;
        int lineEnd = source.indexOf('\n', start);
        return lineEnd < 0 ? source.substring(0, lineStart) : source.substring(0, lineStart) + source.substring(lineEnd + 1);
    }

    /**
     * The artifact without the member whose declaration starts with the given text - the
     * declaration, its body and the annotations directly above it. Used to take an
     * enforced member away from the generated key class and see what the framework does
     * about it.
     */
    public static String withoutMember(String source, String declaration) {
        int start = source.indexOf(declaration);
        if (start < 0) {
            throw new IllegalArgumentException(
                    "The artifact declares no member starting with \"" + declaration + "\":"
                    + System.lineSeparator() + source);
        }

        int bodyStart = source.indexOf('{', start);
        if (bodyStart < 0) {
            throw new IllegalArgumentException("The member \"" + declaration + "\" has no body.");
        }

        int end = matchingBrace(source, bodyStart);

        // Take the annotations and the indentation above the declaration with it, so that
        // what is left is a class body a human would have written that way.
        int lineStart = source.lastIndexOf('\n', start) + 1;
        while (lineStart > 0) {
            int previousStart = source.lastIndexOf('\n', lineStart - 2) + 1;
            if (!source.substring(previousStart, lineStart).strip().startsWith("@")) {
                break;
            }
            lineStart = previousStart;
        }

        // ... and the line break after the closing brace, whichever of the two the
        // instance that generated the artifact happened to write.
        int tail = end + 1;
        if (tail < source.length() && source.charAt(tail) == '\r') {
            tail++;
        }
        if (tail < source.length() && source.charAt(tail) == '\n') {
            tail++;
        }

        return source.substring(0, lineStart) + source.substring(tail);
    }

    private static int matchingBrace(String source, int openingBrace) {
        int depth = 0;
        for (int i = openingBrace; i < source.length(); i++) {
            char c = source.charAt(i);
            if (c == '{') {
                depth++;
            } else if (c == '}') {
                depth--;
                if (depth == 0) {
                    return i;
                }
            }
        }

        throw new IllegalArgumentException("The member's body is not closed.");
    }
}
