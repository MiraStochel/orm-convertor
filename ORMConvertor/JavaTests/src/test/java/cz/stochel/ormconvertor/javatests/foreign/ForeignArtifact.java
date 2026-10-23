package cz.stochel.ormconvertor.javatests.foreign;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.stream.Stream;

/**
 * An artifact the tool did not make (decision 172) - the counterpart of
 * {@code ForeignArtifact.cs}, and the two read the same directories. A directory with the
 * units of the target, every file a consumer would get from {@code /convert} for the same
 * input, and a manifest, {@code artifact.json}, that says what the artifact answers, from which
 * source into which target, who made it, which file holds which unit, and whatever the producer
 * wants the record to carry. The suite reads the four fields and copies the fifth.
 *
 * <p>The files may carry the {@code {{schema}}} placeholder of the fixture scripts and inputs;
 * it is substituted with the suite's own schema when the artifact is read, as the inputs are.
 *
 * @param id        the name of the artifact's directory
 * @param directory where it was read from
 * @param key       what it answers
 * @param source    the source framework the manifest states
 * @param target    the target framework, which decides the suite that takes the artifact
 * @param producer  who made it: llm, human, tool
 * @param units     the files, with the language and role the manifest states
 * @param metadata  the manifest's metadata object as compact JSON, copied into the row
 */
public record ForeignArtifact(
        String id,
        Path directory,
        Key key,
        int source,
        int target,
        String producer,
        List<Unit> units,
        String metadata) {

    public static final String MANIFEST_FILE_NAME = "artifact.json";

    /** The directory of foreign artifacts a run judges: one artifact, or a directory of them. */
    public static final String ARTIFACTS_VARIABLE = "ORMCONVERTOR_FOREIGN_ARTIFACTS";

    /** The directory the rows of a run are written into; without it the rows go to the output alone. */
    public static final String RESULTS_VARIABLE = "ORMCONVERTOR_FOREIGN_RESULTS";

    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";
    private static final ObjectMapper MAPPER = new ObjectMapper();

    /** One unit: its path inside the artifact's directory, the language and role the manifest states, and its text. */
    public record Unit(String path, int contentType, String content) {
    }

    /**
     * The input an artifact answers: the kind of measure - a query of the differential matrix,
     * of the FIBEN catalog, of the LDBC catalog - and the identifier within it.
     */
    public record Key(String kind, String id) {

        public static final String MATRIX = "matrix";
        public static final String FIBEN = "fiben";
        public static final String LDBC = "ldbc";

        public static Key parse(String answers) {
            int colon = answers.indexOf(':');
            if (colon <= 0 || colon == answers.length() - 1) {
                throw new IllegalStateException("The key \"" + answers + "\" is not written as kind:id (matrix:<query>, fiben:<key>, ldbc:<key>).");
            }

            String kind = answers.substring(0, colon).strip().toLowerCase(Locale.ROOT);
            if (!kind.equals(MATRIX) && !kind.equals(FIBEN) && !kind.equals(LDBC)) {
                throw new IllegalStateException("The key \"" + answers + "\" names the kind \"" + kind + "\", which no suite measures; the kinds are matrix, fiben and ldbc.");
            }

            return new Key(kind, answers.substring(colon + 1).strip());
        }

        @Override
        public String toString() {
            return kind + ":" + id;
        }
    }

    /** The entity classes of the artifact. */
    public List<Unit> entities() {
        return units.stream().filter(unit -> unit.contentType() == ContentType.JAVA_ENTITY || unit.contentType() == ContentType.JAVA).toList();
    }

    /** The mapping documents: the mapper of MyBatis, an orm.xml. */
    public List<Unit> mappings() {
        return units.stream().filter(unit -> unit.contentType() == ContentType.XML).toList();
    }

    /** The unit that carries the query as the consumer calls it - the method, or a mapper's declaration -, or null for an artifact of entities alone. */
    public Unit queryMethod() {
        List<Unit> matching = units.stream().filter(unit -> unit.contentType() == ContentType.JAVA_QUERY).toList();
        if (matching.size() > 1) {
            throw new IllegalStateException(id + ": the manifest states " + matching.size() + " units of JavaQuery, and a query artifact has one.");
        }
        return matching.isEmpty() ? null : matching.get(0);
    }

    /**
     * The artifacts under a root: the root itself where it holds a manifest, otherwise every
     * directory directly under it that holds one, in the order of their names.
     */
    public static List<ForeignArtifact> discover(Path root, String schema) {
        if (Files.exists(root.resolve(MANIFEST_FILE_NAME))) {
            return List.of(read(root, schema));
        }

        if (!Files.isDirectory(root)) {
            throw new IllegalStateException(ARTIFACTS_VARIABLE + " names " + root + ", which is not a directory.");
        }

        List<ForeignArtifact> artifacts = new ArrayList<>();
        try (Stream<Path> children = Files.list(root)) {
            children.filter(child -> Files.exists(child.resolve(MANIFEST_FILE_NAME)))
                    .sorted()
                    .forEach(child -> artifacts.add(read(child, schema)));
        } catch (IOException e) {
            throw new UncheckedIOException("The directory " + root + " could not be listed.", e);
        }

        if (artifacts.isEmpty()) {
            throw new IllegalStateException(root + " holds no artifact: neither it nor a directory under it has an " + MANIFEST_FILE_NAME + ".");
        }

        return artifacts;
    }

    /** Reads one artifact: the manifest, then every unit it names, with the schema placeholder substituted. */
    public static ForeignArtifact read(Path directory, String schema) {
        String id = directory.getFileName().toString();
        Path manifestPath = directory.resolve(MANIFEST_FILE_NAME);
        if (!Files.exists(manifestPath)) {
            throw new IllegalStateException(id + ": " + directory + " holds no " + MANIFEST_FILE_NAME + ".");
        }

        JsonNode manifest;
        try {
            manifest = MAPPER.readTree(Files.readString(manifestPath, StandardCharsets.UTF_8));
        } catch (IOException e) {
            throw new IllegalStateException(id + ": " + MANIFEST_FILE_NAME + " is not JSON: " + e.getMessage());
        }

        Key key = Key.parse(required(manifest, id, "answers"));
        int source = framework(id, "source", required(manifest, id, "source"));
        int target = framework(id, "target", required(manifest, id, "target"));
        String producer = required(manifest, id, "producer");

        JsonNode unitsNode = manifest.get("units");
        if (unitsNode == null || !unitsNode.isArray() || unitsNode.isEmpty()) {
            throw new IllegalStateException(id + ": " + MANIFEST_FILE_NAME + " states no \"units\" - the files of the artifact with their content types.");
        }

        List<Unit> units = new ArrayList<>();
        for (JsonNode element : unitsNode) {
            JsonNode pathNode = element.get("path");
            if (pathNode == null || !pathNode.isTextual()) {
                throw new IllegalStateException(id + ": a unit of " + MANIFEST_FILE_NAME + " states no \"path\".");
            }
            String path = pathNode.asText().strip();

            JsonNode typeNode = element.get("contentType");
            int contentType;
            if (typeNode != null && typeNode.isTextual()) {
                try {
                    contentType = ContentType.forName(typeNode.asText());
                } catch (IllegalArgumentException e) {
                    throw new IllegalStateException(id + ": the unit " + path + " states the content type \"" + typeNode.asText() + "\", which the tool does not have; the names are those of ConversionContentType.");
                }
            } else {
                Integer byExtension = defaultContentType(path);
                if (byExtension == null) {
                    throw new IllegalStateException(id + ": the unit " + path + " states no \"contentType\", and its extension names only a language; a .cs or .java file says whether it is an entity or a query.");
                }
                contentType = byExtension;
            }

            Path file = directory.resolve(path.replace('/', java.io.File.separatorChar));
            if (!Files.exists(file)) {
                throw new IllegalStateException(id + ": the manifest names " + path + ", which is not in the artifact's directory.");
            }

            try {
                String content = Files.readString(file, StandardCharsets.UTF_8).replace("﻿", "").replace(SCHEMA_PLACEHOLDER, schema);
                units.add(new Unit(path, contentType, content));
            } catch (IOException e) {
                throw new UncheckedIOException(id + ": the unit " + path + " could not be read.", e);
            }
        }

        JsonNode metadataNode = manifest.get("metadata");
        String metadata = metadataNode != null && metadataNode.isObject() ? metadataNode.toString() : "{}";

        return new ForeignArtifact(id, directory, key, source, target, producer, units, metadata);
    }

    /** Writes an artifact in the shape the reader takes: the files and the manifest. */
    public static void write(Path directory, Key key, int source, int target, String producer, List<Unit> units, String metadataJson) {
        try {
            Files.createDirectories(directory);

            StringBuilder manifest = new StringBuilder();
            manifest.append("{\n");
            manifest.append("  \"answers\": ").append(MAPPER.writeValueAsString(key.toString())).append(",\n");
            manifest.append("  \"source\": \"").append(Orm.nameOf(source)).append("\",\n");
            manifest.append("  \"target\": \"").append(Orm.nameOf(target)).append("\",\n");
            manifest.append("  \"producer\": ").append(MAPPER.writeValueAsString(producer)).append(",\n");
            manifest.append("  \"units\": [\n");

            for (int index = 0; index < units.size(); index++) {
                Unit unit = units.get(index);
                Path file = directory.resolve(unit.path().replace('/', java.io.File.separatorChar));
                Files.createDirectories(file.getParent());
                Files.writeString(file, unit.content(), StandardCharsets.UTF_8);

                manifest.append("    { \"path\": ").append(MAPPER.writeValueAsString(unit.path()))
                        .append(", \"contentType\": \"").append(ContentType.nameOf(unit.contentType())).append("\" }")
                        .append(index < units.size() - 1 ? "," : "").append('\n');
            }

            manifest.append("  ],\n");
            manifest.append("  \"metadata\": ").append(metadataJson == null ? "{}" : metadataJson).append('\n');
            manifest.append("}\n");

            Files.writeString(directory.resolve(MANIFEST_FILE_NAME), manifest.toString(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException("The artifact could not be written into " + directory + ".", e);
        }
    }

    /** The content type an extension names by itself: the query languages and XML; a .java or .cs file names a language only. */
    static Integer defaultContentType(String path) {
        String lower = path.toLowerCase(Locale.ROOT);
        if (lower.endsWith(".sql")) {
            return ContentType.SQL_QUERY;
        }
        if (lower.endsWith(".hql")) {
            return ContentType.HQL_QUERY;
        }
        if (lower.endsWith(".jpql")) {
            return ContentType.JPQL_QUERY;
        }
        if (lower.endsWith(".xml")) {
            return ContentType.XML;
        }
        return null;
    }

    private static String required(JsonNode manifest, String id, String name) {
        JsonNode value = manifest.get(name);
        if (value == null || !value.isTextual() || value.asText().isBlank()) {
            throw new IllegalStateException(id + ": " + MANIFEST_FILE_NAME + " states no \"" + name + "\".");
        }
        return value.asText().strip();
    }

    private static int framework(String id, String field, String name) {
        for (int orm : Orm.ALL) {
            if (Orm.nameOf(orm).equalsIgnoreCase(name)) {
                return orm;
            }
        }
        throw new IllegalStateException(id + ": the manifest states the " + field + " \"" + name + "\", which is no framework of the tool.");
    }
}
