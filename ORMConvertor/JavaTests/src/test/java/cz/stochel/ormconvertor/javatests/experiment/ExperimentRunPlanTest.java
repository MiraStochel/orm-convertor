package cz.stochel.ormconvertor.javatests.experiment;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.foreign.ForeignArtifact;
import cz.stochel.ormconvertor.javatests.foreign.ForeignVerdict;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/**
 * How the Java half of the limits run continues where it was (decision 195), without an instance
 * or a database: what the plan over the inputs judges and what it keeps as it stands.
 */
class ExperimentRunPlanTest {

    private static final String KEY = "series:joins-2";
    private static final String DOTNET_HEADER = ForeignVerdict.HEADER + "\taxis\tpoint\tcatalog\tstep\ttranslateMs\tallocatedKb\tcatalogMs\tinputBytes";

    @TempDir
    Path directory;

    /** A row read back is the row written: the file of rows is the state the run continues from. */
    @Test
    void aRowReadBackIsTheRowWritten() {
        String line = "series-joins-2-Dapper-Hibernate-fixture\t" + KEY + "\tDapper\tHibernate\ttool\tjava\t4\tran\t\t3\t\t12\t340\t1500\t20\t{\"toolVersion\":\"2.2.0\"}";
        ForeignVerdict verdict = ForeignVerdict.parse(line);

        assertEquals(line, verdict.row());
        assertEquals(ForeignVerdict.LEVEL_RAN, verdict.reached());
        assertEquals(ForeignVerdict.RAN, verdict.outcome());
        assertFalse(verdict.isInProgress());
    }

    /**
     * Over two exported points of one key - the Dapper source with rows of an earlier run, the EF
     * Core source with a .NET row that says its translation into MyBatis was too slow - the plan
     * judges only what nothing settles: a finished row stays and its point is not judged again, a
     * row in progress is the point the earlier run died on, a row of the other walk stays verbatim,
     * and a point the .NET half could not translate is not sent and carries the .NET outcome.
     */
    @Test
    void thePlanKeepsWhatIsSettledAndJudgesTheRest() throws IOException {
        ExperimentInput dapper = input("series-joins-2-Dapper", Orm.DAPPER);
        ExperimentInput efCore = input("series-joins-2-EFCore", Orm.EF_CORE);

        String finished = row("series-joins-2-Dapper-Hibernate-fixture", "Dapper", "Hibernate", 4, ForeignVerdict.RAN);
        String inProgress = row("series-joins-2-Dapper-EclipseLink-fixture", "Dapper", "EclipseLink", 2, ForeignVerdict.IN_PROGRESS);
        String otherWalk = row("series-joins-2-Dapper-MyBatis-none", "Dapper", "MyBatis", 0, ForeignVerdict.REFUSED);
        Path rows = directory.resolve("java-tool-rows.tsv");
        Files.writeString(rows, ForeignVerdict.HEADER + "\n" + finished + "\n" + inProgress + "\n" + otherWalk + "\n", StandardCharsets.UTF_8);

        Path points = directory.resolve("series-points.tsv");
        Files.writeString(points, DOTNET_HEADER + "\n"
                + dotnetRow("series-joins-2-EFCore-MyBatis-fixture", "EFCore", "MyBatis", ForeignVerdict.TOO_SLOW, "35 s") + "\n"
                + dotnetRow("series-joins-2-EFCore-Hibernate-fixture", "EFCore", "Hibernate", "translated", "") + "\n", StandardCharsets.UTF_8);

        ExperimentRunTest.Plan plan = ExperimentRunTest.plan(List.of(dapper, efCore), Set.of(ForeignArtifact.Key.SERIES), false, rows, points, ExperimentRunTest.CATALOG_FIXTURE);

        assertEquals(List.of("series-joins-2-Dapper-MyBatis-fixture", "series-joins-2-EFCore-EclipseLink-fixture", "series-joins-2-EFCore-Hibernate-fixture"),
                plan.cases().stream().map(judged -> ExperimentRunTest.rowIdOf(judged.input(), judged.target(), ExperimentRunTest.CATALOG_FIXTURE)).sorted().toList());

        assertEquals(finished, plan.kept().get("series-joins-2-Dapper-Hibernate-fixture"));
        assertEquals(otherWalk, plan.kept().get("series-joins-2-Dapper-MyBatis-none"));

        ForeignVerdict crashed = ForeignVerdict.parse(plan.kept().get("series-joins-2-Dapper-EclipseLink-fixture"));
        assertEquals(ForeignVerdict.CRASHED, crashed.outcome());
        assertEquals(2, crashed.reached());
        assertTrue(crashed.detail().contains("earlier run"), crashed.detail());

        ForeignVerdict notSent = ForeignVerdict.parse(plan.kept().get("series-joins-2-EFCore-MyBatis-fixture"));
        assertEquals(ForeignVerdict.TOO_SLOW, notSent.outcome());
        assertEquals(0, notSent.reached());
        assertTrue(notSent.detail().contains("not sent"), notSent.detail());
        assertEquals(4, plan.kept().size());
    }

    /** A point of a kind that is not a series is judged whatever the files hold; the resume is the limits run's alone. */
    @Test
    void anInputThatIsNotASeriesPointIsAlwaysJudged() throws IOException {
        ExperimentInput matrix = new ExperimentInput("matrix-q1-Dapper", directory.resolve("matrix-q1-Dapper"), new ForeignArtifact.Key(ForeignArtifact.Key.MATRIX, "q1"), Orm.DAPPER, List.of());
        Path rows = directory.resolve("java-tool-rows.tsv");
        Files.writeString(rows, ForeignVerdict.HEADER + "\n" + row("matrix-q1-Dapper-Hibernate", "Dapper", "Hibernate", 4, ForeignVerdict.AGREES) + "\n", StandardCharsets.UTF_8);

        ExperimentRunTest.Plan plan = ExperimentRunTest.plan(List.of(matrix), Set.of(ForeignArtifact.Key.MATRIX), false, rows, directory.resolve("series-points.tsv"), ExperimentRunTest.CATALOG_FIXTURE);

        assertEquals(3, plan.cases().size());
        assertTrue(plan.kept().isEmpty());
    }

    private ExperimentInput input(String id, int source) {
        return new ExperimentInput(id, directory.resolve(id), ForeignArtifact.Key.parse(KEY), source, List.of());
    }

    private static String row(String artifact, String source, String target, int reached, String outcome) {
        return artifact + "\t" + KEY + "\t" + source + "\t" + target + "\ttool\tjava\t" + reached + "\t" + outcome + "\t\t\t\t0\t0\t0\t0\t{}";
    }

    private static String dotnetRow(String artifact, String source, String target, String outcome, String detail) {
        return artifact + "\t" + KEY + "\t" + source + "\t" + target + "\ttool\trefused\t0\t" + outcome + "\t" + detail + "\t\t\t0\t0\t0\t0\t{}"
                + "\tjoins\t2\tfixture\tfail\t35000\t0\t0\t100";
    }
}
