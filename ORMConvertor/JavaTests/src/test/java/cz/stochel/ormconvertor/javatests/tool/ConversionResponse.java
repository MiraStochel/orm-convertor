package cz.stochel.ormconvertor.javatests.tool;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import java.util.List;
import java.util.stream.Collectors;

/**
 * The body of a {@code /convert} answer: the run record requirement S6 asks for, the
 * generated artifacts, and the diagnostic records. Read as the contract puts it on the
 * wire - camelCase names, enums as numbers (decision 043) - because this suite is an
 * ordinary client of the running instance and shares no type with the server.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ConversionResponse(
        String runId,
        String toolVersion,
        int sourceFramework,
        String sourceFrameworkVersion,
        int targetFramework,
        String targetFrameworkVersion,
        List<ConversionUnit> sources,
        List<ConversionRecord> records,
        int catalogState,
        Double catalogReadMilliseconds) {

    /** The artifacts written in one language, in the order the answer lists them. */
    public List<ConversionUnit> artifactsOf(int contentType) {
        return sources.stream().filter(s -> s.contentType() == contentType).toList();
    }

    /** The one artifact written in this language; fails when there is none or several. */
    public ConversionUnit artifactOf(int contentType) {
        List<ConversionUnit> matching = artifactsOf(contentType);
        if (matching.size() != 1) {
            throw new IllegalStateException("The answer carries " + matching.size() + " artifacts of "
                    + ContentType.nameOf(contentType) + ", not one: " + summary());
        }
        return matching.get(0);
    }

    public List<ConversionRecord> recordsOf(int kind) {
        return records.stream().filter(r -> r.kind() == kind).toList();
    }

    /** Every record of the answer, one per line - what a failing scenario prints. */
    public String describeRecords() {
        return records.stream().map(ConversionRecord::describe).collect(Collectors.joining(System.lineSeparator()));
    }

    private String summary() {
        return sources.stream()
                .map(s -> ContentType.nameOf(s.contentType()))
                .collect(Collectors.joining(", ", "[", "]"));
    }
}
