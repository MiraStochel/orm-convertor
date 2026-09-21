package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.util.List;
import org.junit.jupiter.api.Test;

/**
 * What the matrix claims about itself (decision 089), in the shape decision 087 gave the
 * same question at F12: a number written into a document goes stale with the first query
 * anybody adds, and a number the suite checks against its own data cannot.
 *
 * <p>The criterion of F13 is split over two claims, because neither suite sees into the
 * other: the matrix states that there are at least thirty pairs, and each suite states that
 * it ran every pair the matrix assigns to it. Together they are the criterion, and neither
 * half can be met by leaving the other out.
 *
 * <p>This class is the Java half of it and reads the same file as the .NET half, so the two
 * cannot be counting different matrices.
 */
class DifferentialMatrixTest {

    @Test
    void theMatrixStatesAtLeastThePairsTheCriterionAsks() {
        int pairs = DifferentialMatrix.pairs().size();

        assertTrue(pairs >= DifferentialMatrix.REQUIRED_PAIRS,
                "F13 asks for at least " + DifferentialMatrix.REQUIRED_PAIRS + " pairs of queries and the matrix "
                        + "states " + pairs + ". A query added to matrix.txt brings five of them, one per framework "
                        + "other than its own source.");
    }

    /**
     * Every framework is the source of some query. Without it the matrix could be thirty
     * pairs that all translate out of one ecosystem, which would measure the easy half of
     * what T2 asks for and call it the whole.
     */
    @Test
    void everyFrameworkIsTheSourceOfAQuery() {
        List<Integer> sources = DifferentialMatrix.queries().stream().map(DifferentialQuery::source).distinct().toList();

        for (int orm : Orm.ALL) {
            assertTrue(sources.contains(orm),
                    Orm.nameOf(orm) + " is the source of no query, so the matrix never translates out of it.");
        }
    }

    /**
     * Every query has a canonical result, and it is not empty. An empty file would make
     * every direction agree with every other about nothing at all.
     */
    @Test
    void everyQueryHasACanonicalResult() {
        for (DifferentialQuery query : DifferentialMatrix.queries()) {
            List<String> rows = query.canonicalResult();

            assertFalse(rows.isEmpty(), query.id() + ": the canonical result is empty.");

            for (String row : rows) {
                assertEquals(query.fields().size(), row.split("\t", -1).length,
                        query.id() + ": the row \"" + row + "\" has not as many fields as the matrix states.");
            }
        }
    }
}
