package cz.stochel.ormconvertor.javatests.tool;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * One diagnostic record of the answer (decision 010). The suite reads the kind, the unit
 * and the reason; the rest is here so that a message can name what the record was about.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ConversionRecord(
        int kind,
        int framework,
        Integer artifact,
        String entity,
        String property,
        Integer category,
        Integer feature,
        String unit,
        String reason) {

    /** One line for a failure message: kind, where it happened, and why. */
    public String describe() {
        StringBuilder text = new StringBuilder(RecordKind.nameOf(kind));

        if (unit != null) {
            text.append(" [").append(unit).append(']');
        }
        if (entity != null) {
            text.append(' ').append(entity);
            if (property != null) {
                text.append('.').append(property);
            }
        }

        return text.append(": ").append(reason).toString();
    }
}
