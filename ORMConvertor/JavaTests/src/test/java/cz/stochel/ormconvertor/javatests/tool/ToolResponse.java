package cz.stochel.ormconvertor.javatests.tool;

/**
 * What came back from the instance: the status code, the raw body, and - for an answer
 * the contract describes as an answer - the parsed conversion. A scenario asserts the
 * status code first and prints the body when it is not 200, because a failing endpoint
 * answers with {@code ProblemDetails} (decision 044) whose {@code detail} says why.
 */
public record ToolResponse(int statusCode, String body, ConversionResponse conversion) {

    public ConversionResponse required() {
        if (conversion == null) {
            throw new IllegalStateException("The instance answered " + statusCode + ": " + body);
        }
        return conversion;
    }
}
