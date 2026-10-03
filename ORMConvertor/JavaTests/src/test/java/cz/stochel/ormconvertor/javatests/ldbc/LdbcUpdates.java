package cz.stochel.ormconvertor.javatests.ldbc;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.sql.Statement;
import java.sql.Types;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * The inserts INS 1-8 of the validation set and their compensation (decision 117), run from
 * the scripts in {@code database/ldbc/updates}, which the pom maps in as test resources - the
 * same files the .NET suite embeds. An insert is DML and not a translation: the script knows
 * the operation, and this class binds every field of it to the parameter of the same name -
 * a number as BIGINT, a text as NVARCHAR, a list or an object as its JSON text - and knows
 * nothing else. JDBC has no named parameters, so the names are declared in front of the
 * script and bound by position; the script itself is the one the .NET suite runs.
 */
public final class LdbcUpdates {

    private static final String DIRECTORY = "/ldbc/updates/";
    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";
    private static final int TIMEOUT_SECONDS = 600;
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private LdbcUpdates() {
    }

    /** Runs the script of one insert - INS1 to INS8 - with the fields of the operation as its parameters. */
    public static void insert(Connection connection, String operation, String parameters) throws SQLException {
        JsonNode fields = parse(parameters);

        StringBuilder declarations = new StringBuilder();
        List<Map.Entry<String, JsonNode>> bound = new ArrayList<>();
        for (Map.Entry<String, JsonNode> field : fields.properties()) {
            declarations.append("DECLARE @").append(field.getKey()).append(' ').append(sqlType(field.getValue())).append(" = ?;\n");
            bound.add(field);
        }

        try (PreparedStatement statement = connection.prepareStatement(declarations + script(operation.toLowerCase(Locale.ROOT) + ".sql"))) {
            statement.setQueryTimeout(TIMEOUT_SECONDS);
            for (int index = 0; index < bound.size(); index++) {
                bind(statement, index + 1, bound.get(index).getValue());
            }
            statement.execute();
        }
    }

    /** Deletes every row the inserts of the set write, however many of them ran ({@code undo.sql}). */
    public static void undo(Connection connection) throws SQLException {
        try (Statement statement = connection.createStatement()) {
            statement.setQueryTimeout(TIMEOUT_SECONDS);
            statement.execute(script("undo.sql"));
        }
    }

    /** The text of one script, pointed at the schema LdbcSnb holds the tables in. */
    public static String script(String name) {
        try (InputStream stream = LdbcUpdates.class.getResourceAsStream(DIRECTORY + name)) {
            if (stream == null) {
                throw new IllegalStateException("The script " + DIRECTORY + name + " is missing: the pom reads it from ../database/ldbc/updates.");
            }
            return new String(stream.readAllBytes(), StandardCharsets.UTF_8).replace(SCHEMA_PLACEHOLDER, LdbcDatabase.SCHEMA);
        } catch (IOException e) {
            throw new UncheckedIOException("The script " + name + " could not be read.", e);
        }
    }

    private static String sqlType(JsonNode value) {
        if (value.isIntegralNumber()) {
            return "BIGINT";
        }
        if (value.isNumber()) {
            return "FLOAT";
        }
        if (value.isBoolean()) {
            return "BIT";
        }
        return "NVARCHAR(MAX)";
    }

    private static void bind(PreparedStatement statement, int index, JsonNode value) throws SQLException {
        if (value.isIntegralNumber()) {
            statement.setLong(index, value.longValue());
        } else if (value.isNumber()) {
            statement.setDouble(index, value.doubleValue());
        } else if (value.isBoolean()) {
            statement.setBoolean(index, value.booleanValue());
        } else if (value.isTextual()) {
            statement.setNString(index, value.textValue());
        } else if (value.isArray() || value.isObject()) {
            statement.setNString(index, value.toString());
        } else {
            statement.setNull(index, Types.NVARCHAR);
        }
    }

    private static JsonNode parse(String json) {
        try {
            return MAPPER.readTree(json);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException("The validation set holds JSON that does not parse: " + json, e);
        }
    }
}
