package cz.stochel.ormconvertor.javatests.fiben;

import cz.stochel.ormconvertor.javatests.catalog.CatalogDatabase;
import cz.stochel.ormconvertor.javatests.catalog.CatalogQuery;
import cz.stochel.ormconvertor.javatests.catalog.CatalogRun;
import java.util.List;

/**
 * The FIBEN catalog as the on-demand run reads it (decision 149): the catalog's queries, taken
 * from the instance at {@code /fiben}, in the shape of {@link CatalogRun} - grouped by Kim's
 * type, each with the entities of the tables it reads and its text as one more unit, without
 * parameters, because the benchmark writes every value into the text. The run itself is
 * {@link CatalogRun}, the same for FIBEN and SEDE.
 */
public final class FibenRun {

    /** The keys of the catalog, stated without the instance so that the cases can be counted where nothing is configured. */
    public static final int QUERIES = 237;

    public static final CatalogRun.Definition<FibenCatalog> DEFINITION = new CatalogRun.Definition<>(
            CatalogDatabase.FIBEN,
            "fiben.dataset",
            "/fiben",
            FibenCatalog.class,
            FibenCatalog::dataSet,
            FibenRun::queriesOf);

    private FibenRun() {
    }

    /** The run of this test run over the FIBEN catalog, made by the first test that asks for it. */
    public static CatalogRun current() throws Exception {
        return CatalogRun.of(DEFINITION);
    }

    private static List<CatalogQuery> queriesOf(FibenCatalog catalog) {
        return catalog.queries().stream()
                .map(query -> new CatalogQuery(
                        query.key(),
                        nestingName(query.nesting()),
                        query.columns(),
                        query.orderingKey(),
                        catalog.units(query),
                        List.of(),
                        query.sql(),
                        query.translation() != FibenCatalog.NOT_TRANSLATED))
                .toList();
    }

    private static String nestingName(int nesting) {
        switch (nesting) {
            case 10: return "NonNested";
            case 20: return "TypeN";
            case 30: return "TypeA";
            case 40: return "TypeJ";
            case 50: return "TypeJA";
            default: return String.valueOf(nesting);
        }
    }
}
