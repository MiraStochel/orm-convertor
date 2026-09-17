package cz.stochel.ormconvertor.javatests.tool;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;

/**
 * One unit of a {@code /convert} request, in the shape the wire expects: the client-given
 * name, the language the content is written in, and the content itself.
 *
 * <p>The suite sends files of its own, from {@code src/test/resources/inputs} (decision
 * 078): written in the languages of the source frameworks and describing the tables of
 * the shared fixture schema, because verification level 4 has nowhere to write a row
 * otherwise (decision 016). The schema name is not fixed, so the resources carry the same
 * {@code {{schema}}} placeholder the DDL script does and it is substituted here - which is
 * at the same time how the suite points the catalog at its own schema rather than at the
 * .NET suite's, when both stand in the database at once.
 */
public record InputUnit(String name, int contentType, String content) {

    private static final String RESOURCE_DIRECTORY = "/inputs/";
    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";

    /**
     * Reads {@code src/test/resources/inputs/<path>}. The unit's name is the file name,
     * so a record of the answer points back at the file it came from (decision 066), and
     * the content type follows the extension.
     */
    public static InputUnit fromResource(String path) {
        String resource = RESOURCE_DIRECTORY + path;
        try (InputStream stream = InputUnit.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException("The input resource " + resource + " is missing.");
            }

            String content = new String(stream.readAllBytes(), StandardCharsets.UTF_8)
                    .replace("﻿", "")
                    .replace(SCHEMA_PLACEHOLDER, TestDatabase.schemaName());

            String fileName = path.substring(path.lastIndexOf('/') + 1);
            return new InputUnit(fileName, ContentType.forFileName(fileName), content);
        } catch (IOException e) {
            throw new UncheckedIOException("The input resource " + resource + " could not be read.", e);
        }
    }
}
