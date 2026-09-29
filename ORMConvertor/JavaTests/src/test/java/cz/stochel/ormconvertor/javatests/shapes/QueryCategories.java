package cz.stochel.ormconvertor.javatests.shapes;

import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.QueryFeature;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Reads {@code QueryShapes/categories.txt}, the manifest of the query categories of
 * requirement T2 that both suites read (decisions 076 and 089): which source frameworks
 * state a category and with which units, which target refuses it by its descriptor, and
 * from which source the tool refuses it by a stated rule. Its counterpart is the reading
 * in {@code Tests/Combined/QueryShapeInputs.cs}, and the format is the one of
 * {@code matrix.txt}, poorer than JSON on purpose: two parsers in two languages have to
 * agree about it, and a line of "key = value" cannot be read two ways.
 *
 * <p>The domain the categories are written over - five entities in the languages of all
 * six frameworks under {@code QueryShapes/entities} - is the same for every category and
 * for the deliberately bad query, so it is stated here once for both classes of this
 * package rather than in the manifest.
 */
public final class QueryCategories {

    private static final String DIRECTORY = "/QueryShapes/";
    private static final String MANIFEST = "categories.txt";

    private static List<Category> cached;

    private QueryCategories() {
    }

    /**
     * One category: its id (the section of the manifest, and the directory of its files),
     * the query units of every source that states it, and the two kinds of stated refusal
     * by framework and feature.
     */
    public record Category(
            String id,
            Map<Integer, List<String>> units,
            Map<Integer, Integer> refusedBy,
            Map<Integer, Integer> refusedFrom) {

        /** Whether the source framework can state the category in its language. */
        public boolean statedBy(int source) {
            return units.containsKey(source);
        }

        /** The feature a target's descriptor refuses the category by, or null when it expresses it. */
        public Integer refusalBy(int target) {
            return refusedBy.get(target);
        }

        /** The feature the tool refuses the category from this source by, or null when it reads it. */
        public Integer refusalFrom(int source) {
            return refusedFrom.get(source);
        }

        /** The query units of the source, read from the shared files in the manifest's order. */
        public List<InputUnit> queryUnits(int source) {
            List<String> paths = units.get(source);
            if (paths == null) {
                throw new IllegalArgumentException("The category " + id + " is not stated by " + Orm.nameOf(source) + ".");
            }

            return paths.stream().map(path -> InputUnit.fromShared("QueryShapes/" + id + "/" + path)).toList();
        }

        /** The whole input of a conversion from the source: the domain, then the query. */
        public List<InputUnit> input(int source) {
            List<InputUnit> all = new ArrayList<>(domainUnits(source));
            all.addAll(queryUnits(source));
            return all;
        }

        @Override
        public String toString() {
            return id;
        }
    }

    /** The category of the manifest with this id, or a failure that says there is none. */
    public static Category byId(String id) {
        return all().stream()
                .filter(category -> category.id().equals(id))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("categories.txt states no category \"" + id + "\"."));
    }

    /** Every category the manifest states, in its order. */
    public static synchronized List<Category> all() {
        if (cached == null) {
            cached = parse();
        }

        return cached;
    }

    /**
     * The shared files the framework reads the five entities from, as units of a
     * conversion; the mapping half of every input of this package.
     */
    public static List<InputUnit> domainUnits(int source) {
        return domainOf(source).stream().map(InputUnit::fromShared).toList();
    }

    /** The paths of those files under {@code QueryShapes}. */
    public static List<String> domainOf(int source) {
        return switch (source) {
            case Orm.DAPPER -> List.of("QueryShapes/entities/dapper/Shop.cs");
            case Orm.EF_CORE -> List.of("QueryShapes/entities/efcore/Shop.cs");
            case Orm.NHIBERNATE -> List.of(
                    "QueryShapes/entities/nhibernate/Shop.cs",
                    "QueryShapes/entities/nhibernate/Shop.hbm.xml");
            case Orm.HIBERNATE, Orm.ECLIPSELINK -> List.of(
                    "QueryShapes/entities/jpa/ShopCustomer.java",
                    "QueryShapes/entities/jpa/ShopOrder.java",
                    "QueryShapes/entities/jpa/ShopOrderLine.java",
                    "QueryShapes/entities/jpa/ShopOrderLineAllocation.java",
                    "QueryShapes/entities/jpa/ShopProduct.java");
            case Orm.MYBATIS -> List.of(
                    "QueryShapes/entities/mybatis/ShopCustomer.java",
                    "QueryShapes/entities/mybatis/ShopOrder.java",
                    "QueryShapes/entities/mybatis/ShopOrderLine.java",
                    "QueryShapes/entities/mybatis/ShopOrderLineAllocation.java",
                    "QueryShapes/entities/mybatis/ShopProduct.java",
                    "QueryShapes/entities/mybatis/ShopMapper.xml");
            default -> throw new IllegalArgumentException(Orm.nameOf(source) + " has no domain under QueryShapes/entities.");
        };
    }

    private static List<Category> parse() {
        List<Category> categories = new ArrayList<>();
        String id = null;
        Map<String, String> values = new LinkedHashMap<>();

        for (String raw : readLines(MANIFEST)) {
            String line = raw.strip();

            if (line.isEmpty() || line.startsWith("#")) {
                continue;
            }

            if (line.startsWith("[") && line.endsWith("]")) {
                if (id != null) {
                    categories.add(build(id, values));
                }

                id = line.substring(1, line.length() - 1).strip();
                values = new LinkedHashMap<>();
                continue;
            }

            int separator = line.indexOf('=');
            if (separator < 0) {
                throw new IllegalStateException(MANIFEST + ": \"" + line + "\" is neither a section nor a key.");
            }

            values.put(line.substring(0, separator).strip(), line.substring(separator + 1).strip());
        }

        if (id != null) {
            categories.add(build(id, values));
        }

        return List.copyOf(categories);
    }

    private static Category build(String id, Map<String, String> values) {
        Map<Integer, List<String>> units = new LinkedHashMap<>();
        Map<Integer, Integer> refusedBy = Map.of();
        Map<Integer, Integer> refusedFrom = Map.of();

        for (Map.Entry<String, String> entry : values.entrySet()) {
            switch (entry.getKey()) {
                case "refusedBy" -> refusedBy = refusals(id, entry.getValue());
                case "refusedFrom" -> refusedFrom = refusals(id, entry.getValue());
                default -> units.put(Orm.forName(entry.getKey()), list(entry.getValue()));
            }
        }

        return new Category(id, Map.copyOf(units), refusedBy, refusedFrom);
    }

    /** A refusal list: {@code Framework:Feature} entries, both spelled as the tool's enums spell them. */
    private static Map<Integer, Integer> refusals(String id, String value) {
        Map<Integer, Integer> refusals = new LinkedHashMap<>();

        for (String entry : list(value)) {
            int colon = entry.indexOf(':');
            if (colon < 0) {
                throw new IllegalStateException(
                        MANIFEST + ": [" + id + "] states the refusal \"" + entry + "\", which is not written as Framework:Feature.");
            }

            refusals.put(
                    Orm.forName(entry.substring(0, colon).strip()),
                    QueryFeature.forName(entry.substring(colon + 1).strip()));
        }

        return Map.copyOf(refusals);
    }

    private static List<String> list(String value) {
        List<String> entries = new ArrayList<>();
        for (String entry : value.split(",")) {
            String trimmed = entry.strip();
            if (!trimmed.isEmpty()) {
                entries.add(trimmed);
            }
        }

        return List.copyOf(entries);
    }

    private static List<String> readLines(String path) {
        String resource = DIRECTORY + path;

        try (InputStream stream = QueryCategories.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException(
                        "Test resource " + resource + " is missing: the pom reads it from ../Tests/Database.");
            }

            String text = new String(stream.readAllBytes(), StandardCharsets.UTF_8).replace("﻿", "");
            return List.of(text.replace("\r\n", "\n").split("\n"));
        } catch (IOException e) {
            throw new UncheckedIOException("Test resource " + resource + " could not be read.", e);
        }
    }
}
