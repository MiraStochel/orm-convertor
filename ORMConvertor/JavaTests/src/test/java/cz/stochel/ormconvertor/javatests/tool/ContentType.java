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
