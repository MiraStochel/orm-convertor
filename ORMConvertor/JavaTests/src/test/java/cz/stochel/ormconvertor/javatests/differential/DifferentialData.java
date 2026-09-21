package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * The files of the differential verification, read from where both suites read them
 * (decision 089): beside the schema script in {@code ../Tests/Database/Differential},
 * which the pom maps in as a test resource - the mechanism decision 076 already set up for
 * the schema itself, so nothing new is shared here.
 */
public final class DifferentialData {

    private static final String DIRECTORY = "/Differential/";
    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";

    private DifferentialData() {
    }

    /** Text of one file, with the schema placeholder substituted. */
    public static String read(String path) {
        return readRaw(path).replace(SCHEMA_PLACEHOLDER, TestDatabase.schemaName());
    }

    /**
     * Text of one file as it stands. Canonical results and the conformance text are read
     * this way: they state values, not schema names, and a substitution in them would be a
     * way for a run to move its own target.
     */
    public static String readRaw(String path) {
        String resource = DIRECTORY + path;

        try (InputStream stream = DifferentialData.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException(
                        "Test resource " + resource + " is missing: the pom reads it from ../Tests/Database.");
            }

            return new String(stream.readAllBytes(), StandardCharsets.UTF_8).replace("﻿", "");
        } catch (IOException e) {
            throw new UncheckedIOException("Test resource " + resource + " could not be read.", e);
        }
    }

    /**
     * Lines of one file, with the line ending of the checkout taken out of the verdict.
     * The canonical results are text files under the repository's CRLF rule, so comparing
     * whole files byte for byte would compare the checkout and not the query.
     */
    public static List<String> readLines(String path) {
        List<String> lines = new ArrayList<>(Arrays.asList(readRaw(path).replace("\r\n", "\n").split("\n", -1)));

        // A text file ends with a line break, so the split leaves one empty entry behind.
        // Only the last one goes: an empty line in the middle of a file is a row of the
        // result and dropping it would be a comparison of something else.
        if (!lines.isEmpty() && lines.get(lines.size() - 1).isEmpty()) {
            lines.remove(lines.size() - 1);
        }

        return lines;
    }
}
