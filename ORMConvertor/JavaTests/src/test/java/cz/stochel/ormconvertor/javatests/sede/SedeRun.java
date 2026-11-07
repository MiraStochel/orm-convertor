package cz.stochel.ormconvertor.javatests.sede;

import cz.stochel.ormconvertor.javatests.catalog.CatalogDatabase;
import cz.stochel.ormconvertor.javatests.catalog.CatalogQuery;
import cz.stochel.ormconvertor.javatests.catalog.CatalogRun;
import java.util.List;

/**
 * The SEDE catalog as the on-demand run reads it (decision 208): the catalog's queries, taken
 * from the instance at {@code /sede}, in the shape of {@link CatalogRun} - grouped by the part
 * of the set, each with the entities of the tables it reads, its text as one more unit and the
 * values of its parameters. The run itself is {@link CatalogRun}, the same for FIBEN and SEDE.
 */
public final class SedeRun {

    public static final CatalogRun.Definition<SedeCatalog> DEFINITION = new CatalogRun.Definition<>(
            CatalogDatabase.SEDE,
            "sede.dataset",
            "/sede",
            SedeCatalog.class,
            SedeCatalog::dataSet,
            SedeRun::queriesOf);

    private SedeRun() {
    }

    /** The run of this test run over the SEDE catalog, made by the first test that asks for it. */
    public static CatalogRun current() throws Exception {
        return CatalogRun.of(DEFINITION);
    }

    private static List<CatalogQuery> queriesOf(SedeCatalog catalog) {
        return catalog.queries().stream()
                .map(query -> new CatalogQuery(
                        query.key(),
                        query.set() == SedeCatalog.VALIDATION ? "Validation" : query.set() == SedeCatalog.TEST ? "Test" : String.valueOf(query.set()),
                        query.columns(),
                        query.orderingKey(),
                        catalog.units(query),
                        query.parameters().stream().map(parameter -> new CatalogQuery.Argument(parameter.name(), parameter.sqlType(), parameter.value())).toList(),
                        query.sql(),
                        query.translation() != SedeCatalog.NOT_TRANSLATED))
                .toList();
    }
}
