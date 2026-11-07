package cz.stochel.ormconvertor.javatests.catalog;

import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import java.util.List;

/**
 * One query of a catalog as the run reads it, whatever the catalog - the counterpart of the
 * .NET {@code CatalogRunQuery}: its key, the group the summary sorts it under (Kim's type for
 * FIBEN, the part of the set for SEDE), the columns of its result and the key of its ordering
 * (decision 170), the units of its conversion, the values of its parameters by name, the text
 * the source runs over JDBC, and whether the catalog claims it translated at all.
 */
public record CatalogQuery(
        String key,
        String group,
        List<String> columns,
        List<String> orderingKey,
        List<InputUnit> units,
        List<Argument> arguments,
        String sql,
        boolean translated) {

    /** The value of one parameter: its name without the @, the SQL Server type of its value, and the value as the catalog writes it. */
    public record Argument(String name, String sqlType, String value) {

        /** The value as a T-SQL literal, for the declaration the source text runs with. */
        public String literal() {
            String base = sqlType.toUpperCase(java.util.Locale.ROOT);
            int parenthesis = base.indexOf('(');
            if (parenthesis > 0) {
                base = base.substring(0, parenthesis);
            }
            return switch (base) {
                case "INT", "BIGINT", "SMALLINT", "TINYINT", "FLOAT", "REAL", "DECIMAL", "NUMERIC", "BIT" -> value;
                default -> "N'" + value.replace("'", "''") + "'";
            };
        }
    }
}
