package cz.stochel.ormconvertor.javatests.tool;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * One generated artifact of the answer: the language it is written in and its text.
 * Nothing else - an artifact carries no name, and pairing output with input is an open
 * item of the interface, which is why the suite derives file and package from the content
 * by the rule of the target language (decision 078).
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ConversionUnit(int contentType, String content, String name) {
}
