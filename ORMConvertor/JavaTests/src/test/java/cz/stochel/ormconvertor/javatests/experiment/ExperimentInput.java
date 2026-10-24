package cz.stochel.ormconvertor.javatests.experiment;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import cz.stochel.ormconvertor.javatests.foreign.ForeignArtifact;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;

/**
 * One input of the experiment run (decision 173) - the counterpart of {@code ExperimentInput.cs}:
 * a directory the inputs export of the .NET suite wrote (decision 172), read back as the key it
 * answers, the source framework and the units, with the schema of this suite substituted. The
 * Java suite has no inputs of its own for the scenarios and the examples; it takes every input
 * from the export, which is what makes the two halves of a run translate the same files.
 *
 * @param id        the name of the input's directory, which identifies the input in every row
 * @param directory where it was read from
 * @param key       what it answers
 * @param source    the source framework
 * @param units     the units of the conversion, in the order the manifest lists them
 */
public record ExperimentInput(String id, Path directory, ForeignArtifact.Key key, int source, List<InputUnit> units) {

    public static final String MANIFEST_FILE_NAME = "input.json";

    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";
    private static final ObjectMapper MAPPER = new ObjectMapper();

    /** The identifier of the row of one translation of the input. */
    public String rowId(int target) {
        return id + "-" + Orm.nameOf(target);
    }

    /** Every input under the root: every directory directly under it that holds an {@code input.json}, in the order of their names. */
    public static List<ExperimentInput> discover(Path root, String schema) {
        if (!Files.isDirectory(root)) {
            throw new IllegalStateException(root + " is not a directory; the inputs export of the .NET suite writes one (decision 172).");
        }

        List<ExperimentInput> inputs = new ArrayList<>();
        try (Stream<Path> children = Files.list(root)) {
            children.filter(child -> Files.exists(child.resolve(MANIFEST_FILE_NAME)))
                    .sorted()
                    .forEach(child -> inputs.add(read(child, schema)));
        } catch (IOException e) {
            throw new UncheckedIOException("The directory " + root + " could not be listed.", e);
        }

        if (inputs.isEmpty()) {
            throw new IllegalStateException(root + " holds no input: no directory under it has an " + MANIFEST_FILE_NAME + ".");
        }

        return inputs;
    }

    public static ExperimentInput read(Path directory, String schema) {
        String id = directory.getFileName().toString();
        try {
            JsonNode manifest = MAPPER.readTree(Files.readString(directory.resolve(MANIFEST_FILE_NAME), StandardCharsets.UTF_8));
            ForeignArtifact.Key key = ForeignArtifact.Key.parse(manifest.get("answers").asText());
            int source = Orm.forName(manifest.get("source").asText());

            List<InputUnit> units = new ArrayList<>();
            for (JsonNode unit : manifest.get("units")) {
                String path = unit.get("path").asText();
                int contentType = ContentType.forName(unit.get("contentType").asText());
                String content = Files.readString(directory.resolve(path), StandardCharsets.UTF_8)
                        .replace("﻿", "")
                        .replace(SCHEMA_PLACEHOLDER, schema);
                units.add(new InputUnit(path, contentType, content));
            }

            return new ExperimentInput(id, directory, key, source, units);
        } catch (IOException e) {
            throw new UncheckedIOException(id + ": the input could not be read.", e);
        }
    }
}
