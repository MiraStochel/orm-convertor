package cz.stochel.ormconvertor.javatests.ldbc;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

/**
 * The conversions of decision 117 that the conformance text holds both suites to - the
 * counterpart of {@code RendererConformance.LdbcRows()} in the .NET suite, with the same
 * bindings and the same JSON. They are code in both languages for the reason the renderer's
 * rows are: a converter is the part that parses JSON, and two libraries in two languages
 * could read it two ways.
 */
public final class LdbcConformance {

    private LdbcConformance() {
    }

    public static List<String> rows() {
        // One object is one row; a moment and a date from milliseconds since the epoch in UTC.
        LdbcCatalog.Validation single = new LdbcCatalog.Validation("IS1", List.of(), List.of(
                LdbcCatalog.Field.of("CreationDate", "creationDate", LdbcCatalog.Field.MOMENT),
                LdbcCatalog.Field.of("Birthday", "birthday", LdbcCatalog.Field.DATE),
                LdbcCatalog.Field.of("CityId", "cityId", LdbcCatalog.Field.VALUE),
                LdbcCatalog.Field.of("FirstName", "firstName", LdbcCatalog.Field.VALUE)), true);

        // A flag as a number; a set in ordinal order, of texts and of objects; a sequence in
        // its own order; a weight written with and without a fraction; an empty list as NULL.
        LdbcCatalog.Validation lists = new LdbcCatalog.Validation("IC0", List.of(), List.of(
                LdbcCatalog.Field.of("IsNew", "isNew", LdbcCatalog.Field.FLAG),
                new LdbcCatalog.Field("Emails", "emails", LdbcCatalog.Field.SET, ";", null, null),
                new LdbcCatalog.Field("Companies", "companies", LdbcCatalog.Field.SET, ";",
                        List.of("organizationName", "year", "placeName"), ","),
                new LdbcCatalog.Field("Path", "path", LdbcCatalog.Field.SEQUENCE, ",", null, null),
                LdbcCatalog.Field.of("Weight", "weight", LdbcCatalog.Field.VALUE)), true);

        // Rows of a query the specification does not order completely are compared sorted.
        LdbcCatalog.Validation unordered = new LdbcCatalog.Validation("IC0", List.of(), List.of(
                LdbcCatalog.Field.of("Name", "name", LdbcCatalog.Field.VALUE)), false);

        // What an artifact hands back: a date as a moment at midnight, a flag as a truth value,
        // a set joined in no particular order.
        LdbcCatalog.Validation actual = new LdbcCatalog.Validation("IC0", List.of(), List.of(
                LdbcCatalog.Field.of("Birthday", "birthday", LdbcCatalog.Field.DATE),
                LdbcCatalog.Field.of("IsNew", "isNew", LdbcCatalog.Field.FLAG),
                new LdbcCatalog.Field("Emails", "emails", LdbcCatalog.Field.SET, ";", null, null)), true);

        List<String> rows = new ArrayList<>();
        rows.addAll(LdbcCanonicalForm.expected(single,
                "{\"firstName\":\"Jun\",\"birthday\":575424000000,\"cityId\":507,\"creationDate\":1331161432355}"));
        rows.addAll(LdbcCanonicalForm.expected(lists,
                "[{\"isNew\":true,\"emails\":[\"b@x.org\",\"a@x.org\"],\"companies\":[{\"organizationName\":\"Zeta\",\"year\":2005,\"placeName\":\"Prague\"},"
                        + "{\"organizationName\":\"Alpha\",\"year\":2001,\"placeName\":\"Brno\"}],\"path\":[3,1,2],\"weight\":17.0},\n"
                        + " {\"isNew\":false,\"emails\":[],\"companies\":[],\"path\":[],\"weight\":18.5}]"));
        rows.addAll(LdbcCanonicalForm.expected(unordered, "[{\"name\":\"b\"},{\"name\":\"a\"}]"));
        rows.addAll(LdbcCanonicalForm.actual(actual,
                LdbcCanonicalForm.rows(new Object[] {LocalDateTime.of(1988, 3, 27, 0, 0), Boolean.TRUE, "c;a;b"})));
        return rows;
    }
}
