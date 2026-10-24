package cz.stochel.ormconvertor.javatests.tool;

/**
 * The values of the tool's {@code ConversionContentType} on the wire, and the file
 * extension each of them is written with. Every unit names the language its content is
 * written in (decision 025), and an input unit of this suite takes that from the name of
 * the resource file it came from (decision 078).
 *
 * <p>The table is the frontend's ({@code js/api.js}, {@code js/translation.js}): one
 * extension, one language (decision 111). A unit declares no role - a {@code .cs} or
 * {@code .java} file is a whole file with whatever it holds, and the source framework finds
 * the entities and the queries in it - so a {@code .query.cs} is C# like any other
 * {@code .cs}, and the infix only tells a reader of the tree what the file holds. The four
 * values that name a role besides the language are the artifacts': the tool writes them and
 * this suite picks its artifacts out of a response by them.
 */
public final class ContentType {

    public static final int CSHARP_ENTITY = 10;
    public static final int CSHARP_QUERY = 20;
    public static final int XML = 30;
    public static final int SQL_QUERY = 40;
    public static final int HQL_QUERY = 50;
    public static final int JAVA_ENTITY = 60;
    public static final int JAVA_QUERY = 70;
    public static final int JPQL_QUERY = 80;
    public static final int CSHARP = 90;
    public static final int JAVA = 100;

    /** The LINQ form NHibernate writes beside its HQL method (decision 118); an artifact's value, never a unit's. */
    public static final int CSHARP_LINQ_QUERY = 110;

    private ContentType() {
    }

    /**
     * The content type a file of this name holds. Unknown extensions are rejected rather
     * than guessed: a unit sent under the wrong language would be a finding about the
     * suite dressed as a finding about the tool.
     */
    public static int forFileName(String fileName) {
        if (fileName.endsWith(".cs")) {
            return CSHARP;
        }
        if (fileName.endsWith(".java")) {
            return JAVA;
        }
        if (fileName.endsWith(".xml")) {
            return XML;
        }
        if (fileName.endsWith(".sql")) {
            return SQL_QUERY;
        }
        if (fileName.endsWith(".hql")) {
            return HQL_QUERY;
        }
        if (fileName.endsWith(".jpql")) {
            return JPQL_QUERY;
        }

        throw new IllegalArgumentException(
                "No content type is defined for the extension of \"" + fileName + "\".");
    }

    /**
     * The value a name stands for, spelled as {@code ConversionContentType} spells it - what
     * the manifest of a foreign artifact states for every unit (decision 172). A name nobody
     * knows stops the run rather than picking a type by accident.
     */
    public static int forName(String name) {
        for (int contentType : new int[] {
                CSHARP_ENTITY, CSHARP_QUERY, XML, SQL_QUERY, HQL_QUERY, JAVA_ENTITY, JAVA_QUERY, JPQL_QUERY,
                CSHARP, JAVA, CSHARP_LINQ_QUERY}) {
            if (nameOf(contentType).equalsIgnoreCase(name.strip())) {
                return contentType;
            }
        }

        throw new IllegalArgumentException("No content type is named \"" + name + "\"; the names are those of ConversionContentType.");
    }

    /** Whether the value names a query, in any of the languages - the predicate {@code IsQuery} of the tool's {@code ConversionContentType}. */
    public static boolean isQuery(int contentType) {
        return contentType == CSHARP_QUERY || contentType == CSHARP_LINQ_QUERY || contentType == SQL_QUERY
                || contentType == HQL_QUERY || contentType == JAVA_QUERY || contentType == JPQL_QUERY;
    }

    public static String nameOf(int contentType) {
        return switch (contentType) {
            case CSHARP_ENTITY -> "CSharpEntity";
            case CSHARP_QUERY -> "CSharpQuery";
            case XML -> "XML";
            case SQL_QUERY -> "SqlQuery";
            case HQL_QUERY -> "HqlQuery";
            case JAVA_ENTITY -> "JavaEntity";
            case JAVA_QUERY -> "JavaQuery";
            case JPQL_QUERY -> "JpqlQuery";
            case CSHARP -> "CSharp";
            case JAVA -> "Java";
            case CSHARP_LINQ_QUERY -> "CSharpLinqQuery";
            default -> "content type " + contentType;
        };
    }
}
