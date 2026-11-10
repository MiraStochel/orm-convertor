package cz.stochel.ormconvertor.javatests.mybatis;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How MyBatis 3.5.19 maps a row onto a class - the facts decisions 210 and 212 rest on,
 * found by the runs of the SEDE catalog. It has no type handler for {@code java.util.UUID}:
 * a {@code <result>} over such a property refuses the mapper, and automatic mapping under a
 * {@code resultType} leaves it null without a word, while a {@code String} property reads
 * the {@code uniqueidentifier} as the text the driver hands over. Under a
 * {@code resultType} a column fills only a property of its own name, so a member named
 * otherwise than its column stays null; a statement that points at the {@code <resultMap>}
 * of another document is mapped by it whichever document is read first. Nothing here reads
 * a table: the rows are literals.
 */
@Tag("integration")
class ResultMappingClaimsTest {

    /**
     * A property of the type the JPA targets keep, beside one that maps by its name: where no
     * column of a row maps under a resultType, MyBatis returns no instance at all.
     */
    public static class UuidRow {
        private UUID Ref;

        private Integer Other;

        public UUID getRef() {
            return Ref;
        }

        public void setRef(UUID value) {
            this.Ref = value;
        }

        public Integer getOther() {
            return Other;
        }

        public void setOther(Integer value) {
            this.Other = value;
        }
    }

    /** The same property as the MyBatis builder writes it. */
    public static class TextRow {
        private String Ref;

        private Integer Other;

        public String getRef() {
            return Ref;
        }

        public void setRef(String value) {
            this.Ref = value;
        }

        public Integer getOther() {
            return Other;
        }

        public void setOther(Integer value) {
            this.Other = value;
        }
    }

    private static final String UUID_ROW = ResultMappingClaimsTest.class.getName() + "$UuidRow";
    private static final String TEXT_ROW = ResultMappingClaimsTest.class.getName() + "$TextRow";

    private static final String VALUE = "6F9619FF-8B86-D011-B42D-00C04FC964FF";
    private static final String GUID = "CONVERT(uniqueidentifier, '" + VALUE + "')";

    private static String mapper(String namespace, String body) {
        return """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
                        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
                <mapper namespace="%s">
                %s
                </mapper>
                """.formatted(namespace, body);
    }

    private static String closedMap(String type, String column) {
        return "<resultMap id=\"Row\" type=\"" + type + "\" autoMapping=\"false\"><result column=\"" + column + "\" property=\"Ref\"/></resultMap>";
    }

    private static Object first(List<String> documents, String statement, Object parameter) {
        SqlSessionFactory factory = MyBatisBootstrap.build(ResultMappingClaimsTest.class.getClassLoader(), documents);
        try (SqlSession session = factory.openSession()) {
            List<Object> rows = session.selectList(statement, parameter);
            assertEquals(1, rows.size(), statement);
            return rows.get(0);
        }
    }

    // ---- decision 210: a uniqueidentifier ------------------------------------------------

    @Test
    void aUuidPropertyInAResultMapRefusesTheMapper() {
        String document = mapper("claims.UuidMap",
                closedMap(UUID_ROW, "Ref") + "<select id=\"s\" resultMap=\"Row\">SELECT " + GUID + " AS Ref</select>");

        RuntimeException refused = assertThrows(RuntimeException.class, () -> first(List.of(document), "claims.UuidMap.s", null));
        assertTrue(innermost(refused).contains("No typehandler found for property Ref"), innermost(refused));
    }

    @Test
    void aUuidPropertyUnderAResultTypeStaysNull() {
        String document = mapper("claims.UuidType",
                "<select id=\"s\" resultType=\"" + UUID_ROW + "\">SELECT " + GUID + " AS Ref, 5 AS Other</select>");

        UuidRow row = (UuidRow) first(List.of(document), "claims.UuidType.s", null);
        assertEquals(5, row.getOther());
        assertNull(row.getRef());
    }

    /** With nothing else in the row, there is no row: the instance itself comes back null. */
    @Test
    void aRowOfNothingButAUuidIsNoInstance() {
        String document = mapper("claims.UuidAlone", "<select id=\"s\" resultType=\"" + UUID_ROW + "\">SELECT " + GUID + " AS Ref</select>");

        assertNull(first(List.of(document), "claims.UuidAlone.s", null));
    }

    /** The builder's form: the text of the value, under the entity's result map and under a resultType alike. */
    @Test
    void aStringPropertyReadsTheValueAsItsText() {
        String byMap = mapper("claims.TextMap",
                closedMap(TEXT_ROW, "Ref") + "<select id=\"s\" resultMap=\"Row\">SELECT " + GUID + " AS Ref</select>");
        String byType = mapper("claims.TextType", "<select id=\"s\" resultType=\"" + TEXT_ROW + "\">SELECT " + GUID + " AS Ref</select>");
        String byRow = mapper("claims.TextRow", "<select id=\"s\" resultType=\"map\">SELECT " + GUID + " AS Ref</select>");

        assertEquals(VALUE, ((TextRow) first(List.of(byMap), "claims.TextMap.s", null)).getRef());
        assertEquals(VALUE, ((TextRow) first(List.of(byType), "claims.TextType.s", null)).getRef());
        assertEquals(VALUE, ((Map<?, ?>) first(List.of(byRow), "claims.TextRow.s", null)).get("Ref"));
    }

    /** A parameter needs no handler of its own: an unknown type is bound as an object, which the driver takes. */
    @Test
    void aUuidParameterBinds() {
        String document = mapper("claims.UuidParameter",
                "<select id=\"s\" resultType=\"map\">SELECT CASE WHEN " + GUID + " = #{x} THEN 1 ELSE 0 END AS Same</select>");

        Object row = first(List.of(document), "claims.UuidParameter.s", Map.of("x", UUID.fromString(VALUE)));
        assertEquals(1, ((Map<?, ?>) row).get("Same"));
    }

    // ---- decision 212: a member named otherwise than its column ---------------------------

    private static final String ROWS = "SELECT * FROM (SELECT 'abc' AS Guid, 5 AS Other) AS t";

    @Test
    void aRenamedColumnUnderAResultTypeStaysNull() {
        String document = mapper("claims.RenamedType", "<select id=\"s\" resultType=\"" + TEXT_ROW + "\">" + ROWS + "</select>");

        TextRow row = (TextRow) first(List.of(document), "claims.RenamedType.s", null);
        assertEquals(5, row.getOther());
        assertNull(row.getRef());
    }

    /** The builder's form: the statement names the result map of the entity's own mapper, in either order of the documents. */
    @Test
    void aStatementTakesTheResultMapOfAnotherDocument() {
        String entity = mapper("claims.EntityMapper", closedMap(TEXT_ROW, "Guid"));
        String query = mapper("claims.QueryMapper", "<select id=\"s\" resultMap=\"claims.EntityMapper.Row\">" + ROWS + "</select>");

        assertEquals("abc", ((TextRow) first(List.of(entity, query), "claims.QueryMapper.s", null)).getRef());
        assertEquals("abc", ((TextRow) first(List.of(query, entity), "claims.QueryMapper.s", null)).getRef());

        RuntimeException missing = assertThrows(RuntimeException.class, () -> first(List.of(query), "claims.QueryMapper.s", null));
        assertTrue(innermost(missing).contains("does not contain value for claims.EntityMapper.Row"), innermost(missing));
    }

    private static String innermost(Throwable thrown) {
        Throwable root = thrown;
        while (root.getCause() != null) {
            root = root.getCause();
        }
        return String.valueOf(root.getMessage());
    }
}
