package cz.stochel.ormconvertor.javatests.tool;

/**
 * The values of the tool's {@code ConversionContentType} on the wire, and the file
 * extension each of them is written with. Every unit names the language its content is
 * written in (decision 025), and an input unit of this suite takes that from the name of
 * the resource file it came from (decision 078).
 *
 * <p>The table is the frontend's ({@code js/api.js}, {@code js/translation.js}) wherever
 * the frontend's is unambiguous. Two extensions there stand for two languages each -
 * {@code .cs} for an entity and for a LINQ query, {@code .java} for an entity and for a
 * query method - because the frontend has a picker to tell them apart and a file name has
 * not. Here the longer, more specific extension decides, exactly as {@code .hbm.xml} does
 * for XML: {@code .query.cs} is the LINQ query, {@code .query.java} the query method.
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

    private ContentType() {
    }

    /**
     * The content type a file of this name holds. Unknown extensions are rejected rather
     * than guessed: a unit sent under the wrong language would be a finding about the
     * suite dressed as a finding about the tool.
     */
    public static int forFileName(String fileName) {
        if (fileName.endsWith(".query.cs")) {
            return CSHARP_QUERY;
        }
        if (fileName.endsWith(".query.java")) {
            return JAVA_QUERY;
        }
        if (fileName.endsWith(".cs")) {
            return CSHARP_ENTITY;
        }
        if (fileName.endsWith(".java")) {
            return JAVA_ENTITY;
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
            default -> "content type " + contentType;
        };
    }
}
