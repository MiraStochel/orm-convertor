A web-based tool for translating between ORM frameworks. This operating manual says how it is run, deployed, configured and tested, and how each path was verified — the operational half of the deployment view (decision [058](../docs/decisions/058-only-the-operational-half-of-the-deployment-view-moves.md)); the tool itself is described in [`docs/architecture.md`](../docs/architecture.md). **Last verified against the code: 2026-10-18.**

# Deployment

One ASP.NET Core process, `ORMConvertorAPI`, serves the REST API and the static frontend under the path base `/orm`. The connection strings in `appsettings.json` are empty (S4, decision [029](../docs/decisions/029-database-connection-is-the-consumer-projects-fact.md)); see [Configuration](#configuration). **No endpoint requires a login**: a trusted network or an access-controlling proxy is assumed ([`threat-model.md`](../docs/threat-model.md)).

| Path | Environment | Verified by running it, on two machines |
|---|---|---|
| [Visual Studio](#visual-studio), [.NET CLI](#net-cli) | `Development` | profiles `http`, `https`: frontend served, `CatalogState = Reached` |
| [Docker](#docker-application--database) | `Development` | 2026-08-23, 2026-08-24; see below |
| [PM2](#pm2) | `Development` | `CatalogState = Reached`, version in the run record |
| [Real instance](#deploying-a-real-instance) | `Production` | Windows only; see below |

## Visual Studio

Open `ORMConvertor.sln`, startup project `ORMConvertorAPI`. The profiles in `ORMConvertorAPI/Properties/launchSettings.json` set `Development` (Swagger UI, user secrets) and open the relative `orm/` on their `applicationUrl`: `http` on `http://localhost:5072`, `https` on `https://localhost:7124` and `http://localhost:5072` (needs `dotnet dev-certs https`). `IIS Express` is an unverified template leftover. Only Visual Studio opens a browser.

## .NET CLI

```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

The URL is printed to the console (typically [http://localhost:5072/orm/](http://localhost:5072/orm/)).

## Docker (application + database)

`docker-compose.yml` holds the system and, under the profile `test`, the test suites (decision [039](../docs/decisions/039-container-configuration-of-the-environment.md)); `docker compose up --build` starts only `ormconvertor` and `mssql_db`. Arrows are `depends_on` with its condition.

```mermaid
flowchart LR
  subgraph sys["no profile: docker compose up --build"]
    app["ormconvertor<br/>stage runtime, :5072"]
    db[("mssql_db<br/>database.Dockerfile, :1444")]
  end
  subgraph tst["profile test"]
    tdb[("test_db<br/>database.Dockerfile, SF 0.1")]
    init["test_db_init<br/>creates ORMConvertorTests,<br/>waits for ldbc.validation"]
    tests["tests<br/>stage tests"]
    tapp["test_app, alias testapp<br/>stage runtime"]
    tappl["test_app_ldbc, alias testappldbc<br/>catalog LdbcSnb"]
    jt["java_tests<br/>stage java-tests"]
  end
  app -- healthy --> db
  init -- healthy --> tdb
  tests -- completed --> init
  tapp -- completed --> init
  tappl -- completed --> init
  jt -- completed --> init
  jt -- "started, HTTP /orm" --> tapp
  jt -- "started, HTTP /orm" --> tappl
```

`ORMConvertorAPI/Dockerfile`: `advisor-native` builds `libadvisor.so`, `dotnet-build` publishes, `tests` builds on it, `java-tests` on `maven:3.9.11-eclipse-temurin-25-noble`, `runtime` on `aspnet:10.0` with `libglpk40`. **`dotnet-build` copies every `.csproj` by name: a new project needs a line in that `COPY` list.** `java-tests` copies all of `Tests/Database/` and `database/ldbc/updates/`; the pom's `<testResources>` selects from them.

- **`ormconvertor`** — [http://localhost:5072/orm/](http://localhost:5072/orm/), Swagger UI at `/orm/swagger`; gets `ConnectionStrings__AdvisorDatabase` and `ConnectionStrings__CatalogDatabase`.
- **`mssql_db`** — SQL Server 2022 on `localhost,1444`, `SA` / `Testingorms123` (development only), with `WideWorldImporters` and `LdbcSnb`: LDBC SNB Interactive v1 at scale factor 1 (decision [110](../docs/decisions/110-ldbc-snb-as-a-second-reference-domain.md)), downloaded at build (222 MB) and loaded by `database/ldbc/load-ldbc.sh` on the first start in about a minute, with the validation set of the same scale factor, the judge of the LDBC catalog (decision [117](../docs/decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md)), in the table `ValidationOperation`. A database whose extended property `ldbc.dataset` is missing or names another archive is reloaded, one lacking only `ldbc.validation` gets the set alone, and a failed load leaves the server up without it. The loader asks nothing before this start's errorlog says `Recovery is complete` and stops at a question the server does not answer, leaving the database as it was; a new container over a loaded volume (after `down`, a rebuilt image or `--force-recreate`) therefore skips the load.
- **`Fiben`** — the third database of `mssql_db` (decision [149](../docs/decisions/149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md)): the FIBEN benchmark of IBM Research (Apache-2.0), `data.zip` of `IBM/fiben-benchmark` downloaded at build (80 MB; `FIBEN_DATASET` names the commit, `none` leaves it out) and loaded by `database/fiben/load-fiben.sh` on the first start in two to three minutes, with the extended property `fiben.dataset`; beside it the judge of the FIBEN catalog, `database/fiben/judge.tsv`, in the table `Judge` (property `fiben.judge`, the file's checksum; reloaded when the file changes). The loader keeps the rules of the LDBC one; the profile `test` does not carry the database, as the catalog runs on demand against this service ([the FIBEN run](#the-fiben-run)).
- Port 1444 is also bound by the inherited benchmarks: run one at a time.
- **The environment is fixed for measurement** (decision [145](../docs/decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md)): `init-db.sh` sets `max server memory (MB)` 4096 and `max degree of parallelism` 1 on every start; the compose file pins the databases to processors 0–1 and every client to 2–3; the Docker host gives the VM four processors and 8 GB without swap (on MIS3 the WSL2 `.wslconfig`). The iteration policy is `AdvisorBenchmarking/BenchmarkPolicy`, returned by a run as `policy`. The CPU boost of the host is outside the tool: on MIS3 the power plan holds the processor state at 99 % for both minimum and maximum, which turns turbo off, and a record of a measurement says whether that held. The LDBC judge checks correctness, not time, and runs with turbo on.

**Verified** on the same Docker Desktop version: `POST /convert` from Dapper completed `Sales.Customers` (table, schema, types, length, nullability) from the catalog — F6 through the interface; `POST /advisor/run` solved the model through `libadvisor.so` (outside the guarantees, [`architecture.md`](../docs/architecture.md) §9). On a fresh volume (release `2.1.0`) `LdbcSnb` loaded in about 40 s, and an infeasible model sent to `/advisor-test` logged its message with that call.

## PM2

`ecosystem.config.js`, inherited from the original prototype, defines the [PM2](https://pm2.keymetrics.io/) app `orm`, which runs the [.NET CLI](#net-cli) command: `pm2 start ecosystem.config.js`, `pm2 delete orm` to stop. `cwd: __dirname` makes the relative `--project` path work; the `env` block blanks `version` and `Version`, which `dotnet run` would turn into an MSBuild property overriding the assembly version. Needs Node.js with PM2 (`npm install -g pm2`) and the .NET SDK; listens on `localhost:5072` only.

## Deploying a real instance

**1. Publish**; the server needs only the ASP.NET Core runtime:

```sh
dotnet publish ORMConvertorAPI/ORMConvertorAPI.csproj --configuration Release --output /srv/ormconvertor
cd /srv/ormconvertor && dotnet ORMConvertorAPI.dll
```

**The `cd` matters:** the working directory is the content root, where `wwwroot` and `appsettings.json` are looked for. Started elsewhere, the API answers but `/orm/` returns an empty 404; the only trace is `Request reached the end of the middleware pipeline without being handled by application code`. Otherwise name it:

```sh
dotnet /srv/ormconvertor/ORMConvertorAPI.dll --contentRoot /srv/ormconvertor
```

**2. `Production` is the default:** no Swagger UI and no user secrets, so configuration arrives as environment variables.

**3. Proxy.** The app listens on `ASPNETCORE_URLS` without TLS; `UsePathBase` strips `/orm` without requiring it. A public instance belongs behind a reverse proxy that terminates HTTPS and keeps the prefix.

**4. `AllowedHosts` is `*`:** narrow it when exposed directly.

**Verified** on two Windows machines: `/orm/swagger` 404, `/orm/` 200; the catalog variable moved `CatalogState` from `NotConfigured` to `Reached`; `/advisor-test` 400 with the `libadvisor.so` load error; `--contentRoot` cured the content-root failure. **Not covered:** a Linux host, a real reverse proxy, the Advisor with its native library.

# Configuration

No connection string has a value in the repository, except the disposable credentials of servers that compose and CI create ([`threat-model.md`](../docs/threat-model.md)). Keys use `:` in user secrets and `appsettings.json`, `__` in environment variables. The application reads user secrets only in `Development`; the test project always.

| Key | Used by | Purpose; when unset | Set in |
|---|---|---|---|
| `ConnectionStrings:CatalogDatabase` | API | Catalog completion. Unset: conventions only, reported in `CatalogState`; F4 and F6 do not hold through the interface. | secret, env; compose `ormconvertor`, `test_app`, `test_app_ldbc`; CI |
| `ConnectionStrings:AdvisorDatabase` | API | Advisor benchmarking. Unset: the Advisor names the key and stops. | secret, env; compose `ormconvertor` |
| `ConnectionStrings:TestDatabase` | .NET suite | Database tests. Unset: they skip with a reason. | secret, env; compose `tests`; CI |
| `ORMCONVERTOR_REQUIRE_TEST_DATABASE` | .NET suite | `1`: a missing database fails. | env; compose `tests`; CI |
| `ORMCONVERTOR_TEST_SCHEMA` | both suites | Plain SQL identifier. Unset: `ormconvertor_test`, `ormconvertor_java_test`. | env |
| `ORMCONVERTOR_TEST_JDBC_URL` | Java suite | JDBC URL with user and password. | env; compose `java_tests`; CI |
| `ORMCONVERTOR_API_URL` | Java suite | Running instance with `/orm`: `http://testapp:5072/orm`, in CI `http://localhost:5072/orm`. | env; compose `java_tests`; CI |
| `ORMCONVERTOR_RECORD_DIFFERENTIAL` | both suites | Path where a recording run writes canonical results. | env, by hand |
| `ASPNETCORE_ENVIRONMENT` | API | `Development`: Swagger UI, user secrets. Unset: `Production`. | launch profiles, compose, CI |
| `ASPNETCORE_URLS` | API | Listening address. | image `runtime`, CI |
| `Parsing:MaxNestingDepth` | API | Parser nesting cap (decision [092](../docs/decisions/092-input-nesting-depth-capped-before-the-descent.md)); `0` is off. Unset: 128. | `appsettings.json`, env |
| `ORMCONVERTOR_CATALOG_DATABASE` | compose | Catalog of `ormconvertor`. Unset: `WideWorldImporters`; `LdbcSnb` for the LDBC queries, `Fiben` for the FIBEN catalog. | shell |
| `LDBC_SCALE_FACTOR` | compose | `1`, another Interactive v1 scale factor (`0.1`, `0.3`, `3`, `10`, …) or `none` (no `LdbcSnb`). | shell |
| `LDBC_TEST_SCALE_FACTOR` | compose, profile `test` | Scale factor of `test_db`. Unset: `0.1`. | shell |
| `FIBEN_DATASET` | compose, build of the database image | Commit of `IBM/fiben-benchmark` whose `data.zip` the image downloads, or `none` (no `Fiben`; the profile `test` sets it). | shell, `docker-compose.yml` |
| `ConnectionStrings:LdbcDatabase`, `ORMCONVERTOR_REQUIRE_LDBC_DATABASE`, `ORMCONVERTOR_LDBC_VALIDATION_ROWS`, `ORMCONVERTOR_TEST_LDBC_JDBC_URL`, `ORMCONVERTOR_LDBC_API_URL` | both suites | The LDBC judge: [its table](#the-ldbc-judge). Unset: the judge skips with the reason. | secret, env; compose `tests`, `java_tests` |
| `ConnectionStrings:FibenDatabase`, `ORMCONVERTOR_REQUIRE_FIBEN_DATABASE`, `ORMCONVERTOR_FIBEN_QUERIES`, `ORMCONVERTOR_FIBEN_RESULTS`, `ORMCONVERTOR_FIBEN_MEASURE`, `ORMCONVERTOR_TEST_FIBEN_JDBC_URL`, `ORMCONVERTOR_FIBEN_API_URL` | both suites | The FIBEN run: [its table](#the-fiben-run). Unset: every case skips with the reason. | secret, env, by hand |

**The nesting cap is the operator's alone**: read at startup, absent from `/convert`. Deeper input overflows the stack, which .NET cannot catch, and kills the process ([`threat-model.md`](../docs/threat-model.md), threat 2). Each response reports it in `maxNestingDepth`.

# Advisor prerequisites

Translation works anywhere. The Advisor also needs SQL Server with WideWorldImporters (`ConnectionStrings:AdvisorDatabase`) and `libadvisor.so`, built only in the Docker image — elsewhere its endpoints fail. The ILP model: [`architecture.md`](../docs/architecture.md) §8.

# Tests

## In a container

```sh
docker compose --profile test build tests java_tests test_app test_app_ldbc test_db
docker compose --profile test run --rm tests         # the .NET suite
docker compose --profile test run --rm java_tests    # the Java suite
```

S5's "one main command" per suite: the host needs only Docker. Each `run` starts SQL Server, creates the database once it is healthy and runs the whole suite in its own schema (decision [076](../docs/decisions/076-java-wrappers-in-csharp-jvm-in-containers.md)). `test_app` is the tool the Java suite translates against (decision [078](../docs/decisions/078-java-suite-as-a-client-of-a-running-instance.md)), its catalog on the test database, aliased `testapp` because the JDK's `HttpClient` refuses `_` in a host name. The SQL Server of the profile is the image of `mssql_db` at scale factor 0.1 (`LDBC_TEST_SCALE_FACTOR`), so its first start restores WideWorldImporters and loads `LdbcSnb` with its validation set before `test_db_init` lets the suites run; both suites then run [the LDBC judge](#the-ldbc-judge) as well, `test_app_ldbc` serving the Java one.

> **`run` does not rebuild.** Without `build` it measures the tree the images were built from — green, with nothing to say so. **The only tell is the test count** against [the size of the suite](#how-large-the-suite-is-and-what-it-covers).

## On the host

```sh
dotnet test Tests/Tests.csproj --configuration Release
```

Database tests need `ConnectionStrings:TestDatabase`, e.g. `Server=(localdb)\MSSQLLocalDB;Database=ORMConvertorTests;Trusted_Connection=True;TrustServerCertificate=True`.

## The test database

The tests **create their schema themselves** in any reachable SQL Server (decision [016](../docs/decisions/016-generated-artifact-verification-levels.md)), from `Tests/Database/`.

- **Schema** `ormconvertor_test` from `Database/TestSchema.sql` (`{{schema}}` for the name), the expected answer F4 measures against: single-part keys by `IDENTITY` (`Customers`, `Suppliers`) and assigned (`Products`); composite keys of two (`Orders`, `ProductSuppliers`), three (`OrderLines`) and four parts (`OrderLineAllocations`); one- to three-column foreign keys; 1:1 over a shared key (`CustomerProfiles`); a junction table (`ProductSuppliers`); length, precision/scale, nullability. `TestSchemaFixtureTest` guards it.
- **Lifetime.** `TestSchemaFixture` serves the collection `TestDatabaseSchema`: dropped, created once, dropped after. A writing test (level 4: `DapperToNHibernatePersistenceTest`, `DapperToEFCorePersistenceTest`) rolls back its own transaction over `OpenConnection()`.
- **Skip or fail.** `SkipIfUnavailable()` skips with a reason, since the tool must translate without a database; with `ORMCONVERTOR_REQUIRE_TEST_DATABASE` it fails.
- **LDBC:** schema `<schema>_ldbc` from `database/ldbc/schema.sql`, `constraints.sql` and `indexes.sql`, empty tables for `Combined/LdbcCatalogTest`; beside `TestSchema.sql`, the second expected answer of [the F4 share](#the-f4-share).

### The F4 share

`Catalog/CatalogMetadataShareTest` counts the criterion of F4 — at least 95 % of the tracked metadata read correctly against the test database, deviations recorded — over both schemas above, the expected facts read out of their scripts. It prints the share per schema and kind; a deviation the test does not record fails it, even above 95 %. A record names the commit it measured (decision [095](../docs/decisions/095-a-dated-run-record-names-its-commit.md)).

| Date | Tree | `TestSchema.sql` | LDBC | Test database | Deviations |
|---|---|---|---|---|---|
| 2026-10-21 | `223e791` + next commit, host | 163 / 163 | 299 / 299 | 462 / 462 = 100 % | none |

## The LDBC judge

The validation set of LDBC Interactive v1 judges the Interactive queries of the LDBC catalog at level 4 (decision [117](../docs/decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md); [`architecture.md`](../docs/architecture.md) §6.2): both suites replay it in order over `LdbcSnb` - the inserts from `database/ldbc/updates`, every read against the generated artifact of each framework - and return the database to its loaded state before and after.

| Setting | .NET suite | Java suite |
|---|---|---|
| `LdbcSnb` with the data set and the validation set | `ConnectionStrings:LdbcDatabase` (user secret or `ConnectionStrings__LdbcDatabase`) | `ORMCONVERTOR_TEST_LDBC_JDBC_URL` |
| an instance of the tool whose catalog is `LdbcSnb` | — (translates in process) | `ORMCONVERTOR_LDBC_API_URL`, e.g. `http://localhost:5080/orm` |
| a missing judge fails instead of skipping | `ORMCONVERTOR_REQUIRE_LDBC_DATABASE=1` | the same |
| replay only the first n lines | `ORMCONVERTOR_LDBC_VALIDATION_ROWS` | the same |

- **Without the settings** the judge skips with the reason; the rest of the suite does not need `LdbcSnb`.
- **The compose profile** sets everything and replays the first 1000 lines of the SF 0.1 set - a check.
- **The whole set over SF 1 is a run of its own**, several days per suite in the fixed environment ([the projection](#time-by-step)), the two suites one after the other because they share the lock. It runs against `mssql_db` of the system profile, started with `docker compose up -d --build mssql_db`, and for the Java suite an instance over the LDBC catalog, `ORMCONVERTOR_CATALOG_DATABASE=LdbcSnb docker compose up -d --build ormconvertor`. Build the suites first — days spent on a stale image is the trap above at its most expensive — and start each detached, named and without `--rm`, so that `docker logs <name>` keeps the result whatever happens to the shell that started it:

  ```sh
  docker compose --profile test build tests java_tests
  docker compose --profile test run -d -T --no-deps --name ldbc_judge_dotnet \
    -e "ConnectionStrings__LdbcDatabase=Server=mssql_db,1433;Database=LdbcSnb;User ID=sa;Password=Testingorms123;TrustServerCertificate=true;" \
    -e ORMCONVERTOR_LDBC_VALIDATION_ROWS= \
    tests --filter "FullyQualifiedName~LdbcValidationTest" --logger "console;verbosity=detailed"
  docker compose --profile test run -d -T --no-deps --name ldbc_judge_java \
    -e "ORMCONVERTOR_TEST_LDBC_JDBC_URL=jdbc:sqlserver://mssql_db:1433;databaseName=LdbcSnb;user=sa;password=Testingorms123;encrypt=true;trustServerCertificate=true" \
    -e ORMCONVERTOR_LDBC_API_URL=http://ormconvertor:5072/orm -e ORMCONVERTOR_LDBC_VALIDATION_ROWS= \
    java_tests -Dtest=LdbcValidationTest
  ```

  A run stopped in the middle leaves the inserts of the lines it replayed in `LdbcSnb`; the next replay removes them first, and `database/ldbc/updates/undo.sql` does the same by hand.
- **Loading the set elsewhere:** take `validation_params-sf<N>.csv` of the scale factor the data has from `validation_params-interactive-v1.0.0-sf0.1-to-sf10.tar.zst` at `datasets.ldbcouncil.org/interactive-v1/`, run `database/ldbc/validation.sql` with `{{schema}}` replaced by `dbo` and `-v ValidationFile=<path the server can read>`, then add the extended property `ldbc.validation` with the file's name. After a change to `database/ldbc/indexes.sql` (decision [121](../docs/decisions/121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md)) run that script the same way; it is rerunnable, and the container does it by itself on the next start.
- **One replay at a time:** a replay holds the application lock `ldbc.validation` on its connection, so the other suite waits; the lock dies with the connection.
- **Every command of a replay has the same time limit** (decision [146](../docs/decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md)), the reads of the cells included: each suite puts it into the connection string (`Command Timeout`) or the JDBC URL (`queryTimeout`) it hands the frameworks, whatever the configured one said, so the driver applies it to every statement without any framework being told. It is the limit of one execution in the Advisor's policy as well, and `LdbcValidationTest` holds the three equal. A read over it fails its cell.

### Whole-set runs

The verdict of decision [117](../docs/decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md): the whole `validation_params-sf1` (138 747 lines, 7 041 of them inserts) over `mssql_db` on MIS3, the image built by `build` right before. A record names the commit it measured (decision [095](../docs/decisions/095-a-dated-run-record-names-its-commit.md)).

| Started | Tree | Suite | Tests | As specified (19 queries) | IC 13, IC 14 (measured) | Replay |
|---|---|---|---|---|---|---|
| 2026-10-12 | `580ab90` | .NET: Dapper, EF Core, NHibernate | 86 passed, 0 skipped | every read agrees in all three | 6 818 of 6 818 reads (100.0 %) each, in all three | 1226 min, 113 lines a minute |
| 2026-10-14 | `5656945` | Java: Hibernate, EclipseLink, MyBatis | 81 passed, 0 skipped | every read agrees in all three | 6 818 of 6 818 reads (100.0 %) each, in all three | 1518 min, 91 lines a minute |

Both runs also rejected the five mutations of IC 2 in every framework and returned `LdbcSnb` to its loaded state. They judged IC 13 and IC 14 in their texts with a depth bound, not in the texts of decisions [140](../docs/decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) and [141](../docs/decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md).

### Time by step

The compensation test of each suite prints the time of the replay split by step: the steps outside the cells, per framework the translation and compilation of its cells and the select and comparison summed over its reads, and per query the mean select of a read in each framework; the per-query lines are in the test output of a run.

Measured 2026-10-18 over `32f0949`: both suites in their containers (`tests`, and `java_tests` against the rebuilt `ormconvertor` over `LdbcSnb`), one after the other against `mssql_db` in the fixed environment of decision [145](../docs/decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) with turbo on, as the judge runs (the maximum processor state read `0x63` for AC, the minimum `0x05`, the laptop on AC); the first 500 lines of SF 1 (476 reads of all 21 judged queries, 24 inserts), then the first 250 (233 reads, 17 inserts), each suite twice. Every cell agreed with the judge in all four runs. The projection to the whole set multiplies the per-query mean of each framework by the count of that query in the set (6 818 reads of each IC query, 7 162 of IS 1–3, 3 692 of IS 4–7).

| | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| translation of 21 cells, 500-line run / 250-line run | 2.4 s / 2.3 s | 2.3 s / 1.6 s | 1.5 s / 1.3 s | 3.9 s / 1.5 s | 1.6 s / 1.2 s | 1.4 s / 1.1 s |
| compilation of 21 cells, 500 / 250 | 6.2 s / 5.0 s | 12.1 s / 12.1 s | 6.3 s / 6.2 s | 9.8 s (6.4 s bootstrap) / 8.9 s (5.8 s) | 5.8 s (1.9 s) / 4.4 s (1.6 s) | 7.8 s (0.7 s) / 6.1 s (0.6 s) |
| select, 476 reads of lines 1–500 | 316.2 s | 866.9 s | 276.4 s | 284.2 s | 271.2 s | 283.4 s |
| select, mean of a read, lines 1–500 | 664 ms | 1 821 ms | 581 ms | 597 ms | 570 ms | 596 ms |
| select, 233 reads of lines 1–250 | 119.0 s | 131.5 s | 117.2 s | 120.7 s | 117.4 s | 122.2 s |
| select, mean of a read, lines 1–250 | 511 ms | 564 ms | 503 ms | 518 ms | 504 ms | 525 ms |
| whole set of selects projected from lines 1–500 | 23.0 h | 63.2 h | 20.1 h | 20.7 h | 19.8 h | 20.7 h |
| the same projected from lines 1–250 | 18.8 h | 20.7 h | 18.5 h | 19.0 h | 18.5 h | 19.3 h |

The replay took 25.5 min (.NET) and 14.9 min (Java) over 500 lines and 7.2 and 6.7 min over 250, the steps outside the cells under 45 s in all four runs. The projection holds within about 15 % for every cell but IC 14 in EF Core, whose reads of lines 251–500 average about 43 s where Dapper and NHibernate read the same parameters in 1.6–2.0 s, so EF Core projects anywhere between 21 and 63 h. **The whole set in the fixed environment is to be expected at 2.5–5 days for the .NET suite and 2.5–3 for the Java one**; the runs of 2026-10-12 and 2026-10-14, outside it, took 20 h and 25 h.

### Catalog query times

The window of decision [140](../docs/decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md): every query of the catalog runs over `LdbcSnb` on MIS3 between 0.2 s and 300 s, the upper bound met by scale factor 1, the lower one by repetitions in a measurement (decision [141](../docs/decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md)). Measured 2026-10-15 over `86a102b` + the next commit (the five path queries in their new texts, the rest unchanged): each text with its example parameters through Dapper against `mssql_db` (SF 1), four runs, the smallest of the three warm ones; Intel Core i7-1065G7 with turbo on, SQL Server 2022 in Docker Desktop, before the fixed environment of decision [145](../docs/decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), whose numbers are not comparable with these.

| Query | ms | Query | ms | Query | ms | Query | ms |
|---|---|---|---|---|---|---|---|
| IS 1 | 2 | IC 1 | 58 | IC 12 | 1 001 | BI 9 | 341 |
| IS 2 | 2 | IC 2 | 85 | IC 13 | 170 | BI 10 | 1 004 |
| IS 3 | 3 | IC 3 | 1 446 | IC 14 | 2 243 | BI 11 | 5 205 (2.5–14 s between runs) |
| IS 4 | 2 | IC 4 | 61 | BI 1 | 235 | BI 12 | 603 |
| IS 5 | 2 | IC 5 | 210 | BI 2 | 305 | BI 13 | 257 |
| IS 6 | 2 | IC 6 | 266 | BI 3 | 479 | BI 14 | 38 469 |
| IS 7 | 3 | IC 7 | 68 | BI 4 | 412 | BI 15 | 5 259 |
| | | IC 8 | 8 | BI 5 | 2 820 | BI 16 | 3 959 |
| | | IC 9 | 237 | BI 6 | 26 785 | BI 17 | 1 488 |
| | | IC 10 | 173 | BI 7 | 130 | BI 18 | 11 807 |
| | | IC 11 | 19 | BI 8 | 835 | BI 19 | 6 623 |
| | | | | | | BI 20 | 128 |

Below 0.2 s are index lookups and small joins (IS 1–7, IC 1, IC 2, IC 4, IC 7, IC 8, IC 10, IC 11, IC 13, IC 14, BI 7, BI 20), which no scale factor lifts over the bound.

**Noise** (decision [145](../docs/decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md)), measured 2026-10-17 over `6f2c1a2` + the next commit the same way over 21 of the queries, three passes each: before the fixed environment, with turbo on, the medians of IC 5 and IC 9 differed 2–4× between passes (IC 5: 936, 214 and 608 ms); in it, turbo off and on AC, by under 5 % (IC 5: 1 810, 1 790 and 1 782 ms), at absolute times 4–9× higher, about half of that MAXDOP 1. Without the indexes of `indexes.sql` no short query rose over the bound.

**The whole catalog in the fixed environment**, measured 2026-10-17 over `d3d1a9a` the same way from the host (SF 1, MAXDOP 1, 4096 MB, processors 0–1, the VM at four processors and 8 GB; the maximum and minimum processor state both `0x63` for AC, on AC): the median of the warm runs after one cold run, four runs for queries below 5 s, three for the longer ones, two for BI 6. BI 14 was not run: its time put it anywhere between 90 s and over the window.

| Query | ms | Query | ms | Query | ms | Query | ms |
|---|---|---|---|---|---|---|---|
| IS 1 | 3 | IC 1 | 121 | IC 12 | 2 331 | BI 9 | 1 272 |
| IS 2 | 3 | IC 2 | 274 | IC 13 | 269 | BI 10 | 2 199 |
| IS 3 | 4 | IC 3 | 2 804 | IC 14 | 4 927 | BI 11 | 3 141 |
| IS 4 | 3 | IC 4 | 124 | BI 1 | 2 200 | BI 12 | 1 358 |
| IS 5 | 3 | IC 5 | 1 737 | BI 2 | 1 071 | BI 13 | 245 |
| IS 6 | 3 | IC 6 | 625 | BI 3 | 1 035 | BI 14 | not run |
| IS 7 | 4 | IC 7 | 334 | BI 4 | 3 591 | BI 15 | 12 406 |
| | | IC 8 | 16 | BI 5 | 6 702 | BI 16 | 912 |
| | | IC 9 | 1 259 | BI 6 | 60 507 | BI 17 | 3 304 |
| | | IC 10 | 977 | BI 7 | 273 | BI 18 | 36 310 (26.6–36.3 s between runs) |
| | | IC 11 | 35 | BI 8 | 3 320 | BI 19 | 117 773 |
| | | | | | | BI 20 | 290 |

Every query that ran is under the upper bound. Below 0.2 s remain IS 1–7, IC 1, IC 4, IC 8 and IC 11, which the policy of decision 145 repeats up to its 1 000 ms target.

The depths of the path queries, measured from every person over the same data set (`LdbcJudge/LdbcPathQueryTest` holds them):

| Edges | Persons with an edge | Average degree | Components | Depth | Pairs at the depth |
|---|---|---|---|---|---|
| `knows` (IC 13, IC 14) | 9 163 | 39.4 | 1 | 5, the diameter | 2 172 of 83 951 406 |
| `knows` between persons who replied to each other (BI 19) | 8 674 | 32.4 | 1 | 5, the most edges on a cheapest path | 21 056 of 75 229 602 |
| `knows` through a shared university (BI 20) | 3 038 | 4.9 | 443 | 7 | 12 of 70 146 |
| `knows` priced by the forums of the example timeframe (BI 15) | 9 163 | 39.4 | 1 | 11 | 4 of 83 951 406 |

## The FIBEN run

The FIBEN catalog (decision [149](../docs/decisions/149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md); [`architecture.md`](../docs/architecture.md) §6.2) never runs as part of a build. It runs **on demand, for a version or a substantial change**, both suites one after the other against `mssql_db` of the system profile, in the order of the decision: the catalog is complete (held at every build by `Combined/FibenCatalogTest`), every query reaches every target with the catalog and only the entities of the tables it reads, the artifacts compile, every target returns the rows the catalog's own text returns and the text returns the rows the judge holds, and every cell is measured by the policy of decision [145](../docs/decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md).

| Setting | .NET suite | Java suite |
|---|---|---|
| `Fiben` with the benchmark and the judge | `ConnectionStrings:FibenDatabase` (user secret or `ConnectionStrings__FibenDatabase`) | `ORMCONVERTOR_TEST_FIBEN_JDBC_URL` |
| an instance of the tool whose catalog is `Fiben` | — (translates in process) | `ORMCONVERTOR_FIBEN_API_URL` |
| a missing database fails instead of skipping | `ORMCONVERTOR_REQUIRE_FIBEN_DATABASE=1` | the same |
| limit the run to some queries (a check, never a record) | `ORMCONVERTOR_FIBEN_QUERIES=q1,q2,…` | the same |
| write the files of the run into a directory | `ORMCONVERTOR_FIBEN_RESULTS` | the same (`java-` prefixed files) |
| leave the measurement out | `ORMCONVERTOR_FIBEN_MEASURE=0` | the same |

- **Without the settings** every case skips with the reason; nothing else in either suite needs `Fiben`.
- **The record of a run** goes below, beside the LDBC runs: the commit, the environment, the stage each query reached in each framework, the agreement with the source and with the judge, the medians, and a line of notes. The files of a run - `cells.tsv` (one line per cell with its outcome, times, rows and agreement), `judge-candidate.tsv` (the source rows in the form of the judge) and `summary.txt`, and their `java-` counterparts - stay out of git (decision [042](../docs/decisions/042-measured-benchmark-output-out-of-git.md)), except the judge made from a documented run, `database/fiben/judge.tsv`.
- **Start it detached and named**, as the LDBC judge, after rebuilding what it runs - the suites, the database image and, for the Java suite, an instance over the FIBEN catalog:

  ```sh
  docker compose up -d --build mssql_db                                   # loads Fiben on the first start of the new image
  ORMCONVERTOR_CATALOG_DATABASE=Fiben docker compose up -d --build ormconvertor
  docker compose --profile test build tests java_tests
  docker compose --profile test run -d -T --no-deps --name fiben_run_dotnet \
    -e "ConnectionStrings__FibenDatabase=Server=mssql_db,1433;Database=Fiben;User ID=sa;Password=Testingorms123;TrustServerCertificate=true;" \
    -e ORMCONVERTOR_REQUIRE_FIBEN_DATABASE=1 -e ORMCONVERTOR_FIBEN_RESULTS=/app/fiben-results \
    tests --filter "FullyQualifiedName~FibenRunTest" --logger "console;verbosity=normal"
  docker compose --profile test run -d -T --no-deps --name fiben_run_java \
    -e "ORMCONVERTOR_TEST_FIBEN_JDBC_URL=jdbc:sqlserver://mssql_db:1433;databaseName=Fiben;user=sa;password=Testingorms123;encrypt=true;trustServerCertificate=true" \
    -e ORMCONVERTOR_FIBEN_API_URL=http://ormconvertor:5072/orm -e ORMCONVERTOR_REQUIRE_FIBEN_DATABASE=1 \
    -e ORMCONVERTOR_FIBEN_RESULTS=/src/JavaTests/fiben-results \
    java_tests -Dtest=FibenRunTest
  ```

  The two run one after the other, not at once: the measurement of one would be the noise of the other. `docker cp fiben_run_dotnet:/app/fiben-results <dir>` takes the files out before the container is removed. From the host the .NET run works the same against `localhost,1444`. From Git Bash prefix the `run` commands with `MSYS_NO_PATHCONV=1`: without it the shell rewrites the `/app/…` and `/src/…` values of the `-e` arguments into Windows paths and the files land in a directory of that name under the test output.
- **The judge** is the `judge-candidate.tsv` of a documented run that meets the conditions of decision 149 (the first as decision [157](../docs/decisions/157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md) reads it), copied to `database/fiben/judge.tsv` with a header line naming the run and loaded by the next start of the container (the loader reloads it whenever the file's checksum changes); the present one is from the run of 2026-10-20. A new judge is a decision with a date and a reason, never a fix in the comparison.

### Runs

The record names the commit it measured (decision [095](../docs/decisions/095-a-dated-run-record-names-its-commit.md)). *Translated* is the target's own language, *native SQL* the escape path of decision 113, *refused* a `Failure` in the translation; *agree* is the fourth level against the source rows of the same run; *median* is the median of the per-cell medians of the policy of decision 145 and *sum* their sum over the catalog.

| Started | Tree | Suite | Framework | Translated / native SQL / refused | Agree with the source | Measured cells, median / sum of medians | Run |
|---|---|---|---|---|---|---|---|
| 2026-10-18 13:08 UTC | `8e1cdd7` + the next commit | .NET, container `tests` | Dapper (the catalog's text) | 229 / — / 8 | source | 229, 45.0 ms / 327 s | 112 min, 1 401 passed, 5 failed, 261 skipped |
| | | | EF Core | 170 / 59 / 8 | 224 of 229 | 229, 40.9 ms / 329 s | |
| | | | NHibernate | 208 / 21 / 8 | 229 of 229 | 229, 48.4 ms / 323 s | |
| 2026-10-18 15:01 UTC | the same | Java, container `java_tests` against `ormconvertor` over `Fiben` | the text over JDBC | 237 ran | source | 237, 55.7 ms / 347 s | 155 min, 1 364 passed, 40 failed, 261 skipped |
| | | | Hibernate | 204 / 14 / 8, 11 failed to run | 218 of 218 | 218, 62.2 ms / 322 s | |
| | | | EclipseLink | 194 / 14 / 8, 21 failed to run | 203 of 208 | 208, 53.6 ms / 353 s | |
| | | | MyBatis | 226 / — / 8, 3 failed to run | 226 of 226 | 226, 57.5 ms / 334 s | |
| 2026-10-19 22:36 UTC | `9f6082d` + the next commit | .NET, container `tests` | Dapper (the catalog's text) | 229 / — / 8 | source | 229, 60.4 ms / 350 s | 129 min, 1 635 passed, 0 failed, 32 skipped |
| | | | EF Core | 170 / 59 / 8 | 229 of 229 | 229, 57.7 ms / 476 s | |
| | | | NHibernate | 208 / 21 / 8 | 229 of 229 | 229, 58.7 ms / 350 s | |
| 2026-10-20 00:46 UTC | the same | Java, container `java_tests` against `ormconvertor` over `Fiben` | the text over JDBC | 237 ran | source | 237, 53.7 ms / 339 s | 156 min, 1 631 passed, 1 failed (q63 in EclipseLink), 32 skipped |
| | | | Hibernate | 215 / 14 / 8 | 229 of 229 | 229, 64.6 ms / 428 s | |
| | | | EclipseLink | 190 / 39 / 8 | 228 of 229 | 229, 55.2 ms / 353 s | |
| | | | MyBatis | 229 / — / 8 | 229 of 229 | 229, 62.9 ms / 346 s | |

- **2026-10-18**, the first documented run: `Fiben` loaded by the image (`fiben-benchmark-96eaffc`), the environment of decision 145 but with turbo on (the maximum processor state 100 % and the minimum 5 % for AC, on AC). The first judge was made from its .NET run. The five EF Core cells that disagreed with the source (q150, q155, q207, q211, q213) are those of decisions [155](../docs/decisions/155-sql-sum-in-ef-core-linq-is-the-projection-of-one-group-and-ef-core-sum-is-coalesce.md) and [156](../docs/decisions/156-a-linq-predicate-carries-sql-three-valued-logic-in-both-directions.md).
- **2026-10-19 and 2026-10-20**, over `9f6082d` and the change of decision [158](../docs/decisions/158-a-property-declared-dbset-declares-a-set-whatever-supplies-its-value.md): the same database and environment, Windows Update paused. Every cell agreed with the source but q63 in EclipseLink, which trims a `CHAR(5)` ([`subset.md`](../docs/subset.md) 2.2), and the judge was made again from the .NET run (decision [157](../docs/decisions/157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md)); its rows equal those of the first judge. Six EF Core cells ran 7.7–35 s against 0.07–0.1 s from the text (q207, q211, q213, q215, q216, q225; decision [159](../docs/decisions/159-ef-core-takes-the-value-of-one-group-by-max-not-first-or-default.md)).
- **The 8 refused queries** are the catalog's `NotTranslated` ones, refused in every target: q99 and q237 (an aggregate over a window inside a derived table) and q124–126, q200–202 (a scalar subquery inside the `ON` condition of a `LEFT JOIN`), both shapes outside the vocabulary ([`subset.md`](../docs/subset.md) 2.6, 2.9).
- **In both runs** q205 (about 190 s a run) and q233 (about 98 s) set the length; most queries run in under 50 ms.

## The Java test suite

`JavaTests/`, Maven and JUnit outside `ORMConvertor.sln`, the suite of F12 (decision [076](../docs/decisions/076-java-wrappers-in-csharp-jvm-in-containers.md)); what each test verifies is in [`architecture.md`](../docs/architecture.md) §6.2.

- **Verdict** only from the `java-tests` stage and the `java-test` job. A host with JDK 25 and Maven (e.g. those bundled with IntelliJ) can `mvn test-compile`, or run the suite against its own database and an instance whose catalog names that same database, as a check.
- **Versions** of Hibernate, EclipseLink, MyBatis, `mssql-jdbc` and JUnit live only in `JavaTests/pom.xml`; `TargetFrameworkDescriptorTest.JavaSuiteDependenciesMatchTheJavaDescriptors` binds the three framework versions to the descriptors.
- **Inputs:** `ORMCONVERTOR_TEST_JDBC_URL`, e.g. `jdbc:sqlserver://localhost:1433;databaseName=ORMConvertorTests;user=sa;password=...;encrypt=true;trustServerCertificate=true`, the schema built from the same `TestSchema.sql`; `ORMCONVERTOR_API_URL`, polled at `/required-content`.
- **No skip**: where it runs, a database and an instance were started for it - but for [the LDBC judge](#the-ldbc-judge) and [the FIBEN run](#the-fiben-run), which need `LdbcSnb` or `Fiben` and an instance over it and skip without them. `mvn test -Dgroups=integration` runs the integration tests alone (decision [087](../docs/decisions/087-an-integration-test-is-a-run-against-the-database.md)).

Framework behaviour asserted as measured: decisions [078](../docs/decisions/078-java-suite-as-a-client-of-a-running-instance.md), [079](../docs/decisions/079-fractional-second-precision-as-second-precision.md), [080](../docs/decisions/080-eclipselink-as-the-second-profile-over-the-jpa-layer.md).

## Continuous integration

`.github/workflows/ormconvertor-tests.yml`, permissions `contents: read`:

```mermaid
flowchart LR
  push["push to main, pull request<br/>ORMConvertor/**, .github/workflows/**"]
  cron["schedule<br/>Mondays 06:00 UTC"]
  t["test<br/>build, dotnet test, coverage"]
  j["java-test<br/>publish + start app, mvn -B test"]
  d["dependencies<br/>dotnet list package --vulnerable"]
  sql[("SQL Server 2022<br/>service container per job")]
  push --> t
  push --> j
  push --> d
  cron --> d
  t --> sql
  j --> sql
```

| Job | What it does | Uploads |
|---|---|---|
| `test` | build (Release), `dotnet test` with `--collect:"XPlat Code Coverage"`, the database required | `test-results` |
| `java-test` | Temurin 25; starts the published app on `http://localhost:5072` in `Production`, not waited for; `mvn -B test` | `java-test-results`; `java-test-app-log` on failure |
| `dependencies` | `dotnet list ORMConvertor.sln package --vulnerable --include-transitive`, judged by `grep` (it exits 0 either way) under `set -o pipefail` | — |

`dependencies` alone also runs weekly (decision [098](../docs/decisions/098-the-number-is-decided-once-per-release.md)). CI proves the suites against SQL Server of the stated version, compose that they run where nothing is installed. Coverage is never gated; `dotnet test Tests/Tests.csproj --collect:"XPlat Code Coverage"` writes `coverage.cobertura.xml` under `Tests/TestResults/`.

## How large the suite is, and what it covers

The only place that records it; a record names the commit it measured (decision [095](../docs/decisions/095-a-dated-run-record-names-its-commit.md)), and the table keeps the releases and the latest runs. All runs 0 failed. A skip is one of three groups, each with its reason: the differential cells of the LINQ form of NHibernate its provider cannot speak (an `Omitted` record, decision [118](../docs/decisions/118-nhibernate-writes-a-linq-form-beside-its-hql.md); in every environment), [the LDBC judge](#the-ldbc-judge) without `LdbcSnb` and the depths of its path queries over any data set but SF 1, and [the FIBEN run](#the-fiben-run) without `Fiben`. *Pinned* = the `build` + `run` pairs; *host* = `dotnet test` on MIS3 with LocalDB only; integration counted by `-Dgroups=integration`.

| Date | Tree | .NET | Java (integration) | Note |
|---|---|---|---|---|
| 2026-09-21 | `3e8a286` | 1551, pinned | 146 (97), pinned | **release `2.0.0`** |
| 2026-10-03 | `c32948b` + next commit | 7746, pinned | 2082 (1527), pinned | **release `2.1.0`** |
| 2026-10-12 | `fcb4ccb` + next commit ([133](../docs/decisions/133-an-add-on-package-over-a-framework-is-not-that-framework.md)) | 9936, pinned (46 skipped: the `Omitted` LINQ forms) | 2757 (2023), pinned | **release `2.2.0`**; the judge over 1000 lines of SF 0.1 in both |
| 2026-10-15 | `86a102b` + next commit ([140](../docs/decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md), [141](../docs/decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md)) | 9931, host (137 skipped: the 46 `Omitted` LINQ forms, the judge and the path queries without `LdbcSnb`) | 2765 (not recounted), pinned | on the host the judge and `LdbcPathQueryTest` ran separately over `mssql_db` (SF 1) |
| 2026-10-18 | `6ac5690` + next commit ([150](../docs/decisions/150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md)–[152](../docs/decisions/152-an-association-path-that-cannot-be-derived-refuses-the-query-in-every-clause.md)) | 12021, host (1802 skipped: the FIBEN run 1660, the LDBC judge 90, the `Omitted` LINQ forms 52) | not run | the pinned run stopped for want of host memory |

**Coverage** is measured, never a threshold. Over `36d688d`, the same 1551 tests: **80.0 % of lines** (12 650 / 15 801), **69.1 % of branches** (8 047 / 11 642); `Advisor` and `AdvisorBenchmarking` **0.0 %**, the measured basis of area 1 in [`architecture.md`](../docs/architecture.md) §9. No coverage is measured for the Java suite.

**The Java suite counts itself:** `SuiteSizeTest` fails below F12's 60 tests or 20 integration tests and reports what it cannot count as *uncountable* (decision [087](../docs/decisions/087-an-integration-test-is-a-run-against-the-database.md)).

**A count belongs to the tree**, not the machine. A different total means a different tree, usually a stale image; the environment moves only the split into passed and skipped, and skipped tests prove nothing about F4 and F6.

## Translation performance (S3)

Bound: 30 s for 100 entities and 100 queries through `ConversionHandler.Convert`, EF Core → NHibernate; artifact counts are asserted. Release builds, 16 GB RAM:

| Test | Machine | Measured |
|---|---|---|
| `TranslationPerformanceTest`, no catalog | Intel Core i9-9900 (8 cores), Windows 10 Pro | ~0.2 s (2026-08-21) |
| `TranslationPerformanceTest`, no catalog | Intel Core i7-1065G7 (4 cores), Windows 11 Pro | 0.4–0.7 s (2026-08-24) |
| `CatalogTranslationPerformanceTest`, local SQL Server | Intel Core i9-9900 (8 cores), Windows 10 Pro | 0.4–0.5 s, catalog read (`CatalogReadMilliseconds`) 210–330 ms (2026-08-26) |

The margin is at least fortyfold. In the catalog variant three entities match the test schema and `OrderLine` exists only through the key the catalog supplies.

# API

Paths are relative to `http://<host>/orm`; the interface and its decisions are in [`architecture.md`](../docs/architecture.md) §6.5. `/orm/openapi/v1.json` is served in every environment and is authoritative; the committed snapshot [`ORMConvertorAPI/openapi.json`](ORMConvertorAPI/openapi.json) is refreshed from it as below; the Swagger UI at `/orm/swagger` renders it in `Development` only.

```sh
curl http://localhost:5072/orm/openapi/v1.json -o ORMConvertorAPI/openapi.json
```

| Method | Path | Purpose | Request → response |
|---|---|---|---|
| `GET` | `/required-content` | Languages each source reads | → `List<RequiredContentDefinition>` |
| `GET` | `/required-content-advisor` | The same for the Advisor | → `List<AdvisorRequiredContentDefinition>` |
| `GET` | `/samples`, `/samples-advisor` | A sample per unit | → `Dictionary<int, string>` |
| `GET` | `/examples` | Examples as whole inputs | → `List<ExampleDefinition>` |
| `GET` | `/ldbc` | 41 LDBC read queries | → `LdbcCatalogDefinition` |
| `GET` | `/fiben` | 237 FIBEN queries with their questions | → `FibenCatalogDefinition` |
| `POST` | `/convert` | Translation | `ConvertRequest` → `ConvertResponse` |
| `POST` | `/archive` | ZIP of client-named files | `ArchiveRequest` → `application/zip` |
| `POST` | `/advisor/run` | Advisor run with its translations ([059](../docs/decisions/059-advisor-response-carries-the-measured-translations.md)) | `AdvisorRunRequest` → `AdvisorRunResult` |
| `POST` | `/advisor-test` | Bare ILP solver | `AdvisorSolveRequest` → `AdvisorSolveResponse` |

A failing `POST` answers `400` with RFC 9457 `ProblemDetails`. Types live in `ORMConvertorAPI/Dtos/`; `ConvertResponse` carries the S6 run record.

# Frontend

Static pages in `ORMConvertorAPI/wwwroot` — HTML, ES modules, CSS, no build step (decision [032](../docs/decisions/032-frontend-as-static-pages-without-a-build.md)); Pico CSS and highlight.js are vendored in `wwwroot/vendor/`. Seven pages — `index.html`, `translation.html`, `examples.html`, `ldbc.html` and its second version `ldbc-v2.html` (the two kept side by side for comparison), `fiben.html` and `advisor.html`; what each shows is in [`architecture.md`](../docs/architecture.md) §6.3. What the browser runs is what is in git: after editing a page, reload it and check the browser console.
