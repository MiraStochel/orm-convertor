package cz.stochel.ormconvertor.javatests.shapes;

import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import java.util.List;

/**
 * The pieces of a MyBatis answer the two classes of this package bind: the mapper
 * document that carries the statement, every document the answer holds, and the name of
 * the method the statement is registered under (decision 084).
 */
final class MyBatisAnswer {

    private MyBatisAnswer() {
    }

    /** The mapper document carrying the statement - the only one of the answer that does. */
    static String queryMapper(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream()
                .map(ConversionUnit::content)
                .filter(content -> content.contains("<select"))
                .findFirst()
                .orElseThrow(() -> new AssertionError("The answer carries no mapper with a statement"));
    }

    static List<String> allMappers(ConversionResponse response) {
        return response.artifactsOf(ContentType.XML).stream().map(ConversionUnit::content).toList();
    }

    /** The id of the statement, which is the name of the method the declaration carries. */
    static String methodNameOf(ConversionResponse response) {
        String declaration = response.artifactOf(ContentType.JAVA_QUERY).content();
        int parenthesis = declaration.indexOf('(');
        int space = declaration.lastIndexOf(' ', parenthesis);
        return declaration.substring(space + 1, parenthesis);
    }
}
