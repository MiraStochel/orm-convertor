A web-based tool for translating across different .NET ORMs.

This file is the operating manual: how the tool is run, deployed, configured and tested, why each path is built the way it is, and what on it has actually been checked by running it. It holds the operational half of the deployment view (decision [058](../docs/decisions/058-only-the-operational-half-of-the-deployment-view-moves.md)); what describes the tool rather than its operation — the verification levels of generated artifacts, the frontend, the REST interface, and the whole intermediate representation — is in Czech in [`docs/architecture.md`](../docs/architecture.md). **Last verified against the code: 2026-08-26.**

| Section | What it covers |
|---|---|
| [Deployment](#deployment) | The four development paths, the container, and a real published instance. |
| [Configuration](#configuration) | The six variables that decide what an instance can do. |
| [Advisor prerequisites](#advisor-prerequisites) | What the Advisor needs beyond the translation features. |
| [Tests](#tests) | Running the suite on the host or in a container, the test database, CI, size and coverage, translation performance. |
| [API](#api) | The eight endpoints, their request and response shapes, and the OpenAPI document. |
| [Frontend](#frontend) | Where the static pages live and what they are built from. |

# Deployment

The application is a single ASP.NET Core process (`ORMConvertorAPI`) serving both the REST API and the static frontend from `wwwroot` under the path base `/orm`. The connection strings in `appsettings.json` are deliberately empty (requirement S4, decision [029](../docs/decisions/029-database-connection-is-the-consumer-projects-fact.md)): `ConnectionStrings:CatalogDatabase` is supplied on every machine from outside the repository — in the `Development` environment through the project's user secrets (the `UserSecretsId` is in `ORMConvertorAPI.csproj`, and the key is spelled there with a colon), anywhere else through the environment variable `ConnectionStrings__CatalogDatabase`. While the key is empty the catalog completion phase never runs and criteria F4 and F6 do not hold through the interface; the `/convert` response reports that state in `CatalogState`. Filling the key in is therefore part of bringing up any instance — the container configuration fills it in itself.

**No endpoint requires a login.** The application has neither an authentication scheme nor an authorization policy, and assumes deployment on a trusted network or behind a proxy that controls access; `AllowedHosts` is `*`. Until 2026-08-22 `Program.cs` registered the ASP.NET template's authorization services (`AddAuthorization`, `UseAuthorization`), which protected nothing — no endpoint asked for a policy — so they looked like protection where there was none. They were removed, and the assumption is now stated here and in [`threat-model.md`](../docs/threat-model.md), which also lists what an exposed instance is exposed to.

## Visual Studio
Open the solution file `ORMConvertor.sln` and set `ORMConvertorAPI` as the startup project, if not selected by default. The app can be launched in `Debug` configuration for development or compiled in `Release` configuration for production. Three launch profiles are defined in `ORMConvertorAPI/Properties/launchSettings.json` (`http`, `https`, `IIS Express`). Both `http` (`http://localhost:5072`) and `https` (`https://localhost:7124`, with `http://localhost:5072` beside it) are configured and have been verified on two machines by running `dotnet run --launch-profile`: the app came up, served the frontend, and `/convert` with the catalog key filled in reported `CatalogState = Reached`. `https` additionally expects the ASP.NET development certificate (`dotnet dev-certs https`). Both profiles set `ASPNETCORE_ENVIRONMENT=Development`, which is what turns on the Swagger UI and the reading of user secrets. `IIS Express` is a leftover of the project template: no documented path uses it and it is not verified. The application can be started with `CTRL + F5` to run without debugging or `F5` to launch with the debugger. A browser window should open automatically — checked in Visual Studio, where `F5` opened the page the profile names. That last part is Visual Studio's doing, not the profile's: `dotnet run --launch-profile` honours the same `launchSettings.json` but opens no browser at all, so the section below describes a path that ends at the console.

**The address a profile opens is the relative `orm/`, not an absolute URL.** The absolute form carried two faults inherited from client-side routing: it pointed at `/orm/home`, which was the Angular route and has returned 404 since decision [032](../docs/decisions/032-frontend-as-static-pages-without-a-build.md) (verified by running it on both machines), and under the `https` profile it wrote the `https` scheme against port 5072, on which that same profile listens unencrypted. The relative form cancels both by taking host and scheme from the profile's own `applicationUrl` — `http://localhost:5072/orm/` and `https://localhost:7124/orm/` respectively.

## .NET CLI
```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```
This approach does not open a browser automatically. Instead, the local URL is printed to the console (typically [http://localhost:5072/orm/](http://localhost:5072/orm/)).

## Docker (application + database)
`docker-compose.yml` in this directory describes the whole environment — the system and the test suite alike (decision [039](../docs/decisions/039-container-configuration-of-the-environment.md)) — and a profile keeps the two apart, so the command below never starts the test services.

```sh
docker compose up --build
```

starts two containers:

- `ormconvertor` – the application, built by the multi-stage `ORMConvertorAPI/Dockerfile`. Stage `advisor-native` compiles the native Advisor library `libadvisor.so` (gcc + `libglpk-dev`), stage `dotnet-build` publishes the .NET application, and stage `runtime` turns that into a runtime image on `aspnet:10.0` with `libglpk40` added; the static frontend has no build step and enters the image as-is inside `wwwroot`. Exposed on [http://localhost:5072/orm/](http://localhost:5072/orm/). It waits for the database through a health check and receives both connection strings — `ConnectionStrings__AdvisorDatabase` for the Advisor's benchmarking and `ConnectionStrings__CatalogDatabase` for the catalog completion phase — so the containerized instance translates with the catalog rather than without it.
- `mssql_db` – Microsoft SQL Server 2022 initialized with the WideWorldImporters sample database (`database.Dockerfile` and the script `database/init-db.sh`), exposed on `localhost,1444` (`SA` / `Testingorms123` – development credentials only). That host port is also what the inherited benchmarks bind: `benchmarks/README.md` documents `podman run -p 1444:1433` for their own SQL Server image, so the two cannot be up at the same time. Stop one before starting the other, or change the mapping in `docker-compose.yml`.

Note: the compose file runs the app with `ASPNETCORE_ENVIRONMENT=Development`, so the Swagger UI is available at `/orm/swagger`.

**Verified by running it on two machines**, on 2026-08-23 and on 2026-08-24. Both are Windows hosts, and they ran the *same* Docker Desktop version — worth saying, because it bounds what the repetition proves: the second run adds a second machine, not a second engine. All four frontend pages, the vendored Pico CSS and highlight.js, and the Swagger UI are served (`/orm/`, `/orm/translation.html`, `/orm/advisor.html`, `/orm/examples.html`, `/orm/swagger`; `/orm/swagger` redirects once, to `/orm/swagger/index.html`). `POST /convert` with a Dapper source returned `CatalogState = Reached` together with a `CatalogReadMilliseconds` value, and completed the table `Sales.Customers`, the schema, the database types, the length and the nullability from the catalog — criterion F6 to the letter, demonstrated through the interface rather than only in tests. **`ConnectionStrings__AdvisorDatabase` thereby stopped being an unverified declaration:** `POST /advisor/run` over two queries translated, compiled and measured both variants against WideWorldImporters, and the ILP model called through `libadvisor.so` returned a framework selection. The whole Advisor path therefore works inside the image, though it stays outside the guarantees ([`architecture.md`](../docs/architecture.md) §9).

## PM2
`ecosystem.config.js` is a configuration for the [PM2](https://pm2.keymetrics.io/) process manager, inherited from the original prototype where a public instance ran this way. It defines the application `orm` and runs the same `dotnet run` with the `http` profile as the development path above: `pm2 start ecosystem.config.js` starts it, `pm2 delete orm` stops it.

Two details in that file are load-bearing. `cwd: __dirname` anchors the process to the solution directory regardless of where `pm2 start` is called from — without it the process failed on the relative `--project` path, and it was added while verifying this. And the `env` block blanks the `Version`/`version` variable, which the build inside `dotnet run` would otherwise promote to an MSBuild property and overwrite the assembly version with.

The host needs Node.js with PM2 installed (`npm install -g pm2`) and the .NET SDK, not just the runtime: `dotnet run` restores and builds from sources, so this is not a published artifact. The app listens on `localhost:5072` only, so public reachability requires a reverse proxy on the host; the `http` profile also means the `Development` environment, hence the Swagger UI and user secrets under PM2 as well. Verified on two machines: the app came up under PM2 and `/convert` ran with `CatalogState = Reached`, its run record carrying the version from `Directory.Build.props` — the blanking of `Version`/`version` does reach the process, as `pm2 jlist` shows both spellings empty in the app's environment.

## Deploying a real instance
The four paths above are development paths: every one of them runs in the `Development` environment and three of them run from sources. A real instance differs in four ways.

**Publish instead of `dotnet run`.** A published instance needs only the ASP.NET Core runtime, not the SDK:

```sh
dotnet publish ORMConvertorAPI/ORMConvertorAPI.csproj --configuration Release --output /srv/ormconvertor
cd /srv/ormconvertor && dotnet ORMConvertorAPI.dll
```

**The `cd` is not cosmetic.** The content root defaults to the *current working directory*, and the content root is what decides where the app looks for `wwwroot` and for `appsettings.json`. Start the same DLL from anywhere else and it comes up looking healthy — the API answers, the log says `Production` — while `/orm/` returns 404 and the published `appsettings.json` is never read. **Nothing announces the cause.** The startup log carries no warning about the missing `wwwroot`, the 404 has an empty body, and the only trace is the routing line `Request reached the end of the middleware pipeline without being handled by application code`. A deployment that fails this way therefore looks like a routing mistake, not a working-directory one; the give-away is that the API endpoints answer while every static page does not. A service manager that cannot set the working directory should name the path instead:

```sh
dotnet /srv/ormconvertor/ORMConvertorAPI.dll --contentRoot /srv/ormconvertor
```

The publish output carries `wwwroot` with it, so the frontend needs no copy step of its own. This is the same publish the Docker image runs (`ORMConvertorAPI/Dockerfile`, stage `dotnet-build`), after which the output runs on the `aspnet:10.0` image rather than the SDK one. The PM2 configuration is the exception: `ecosystem.config.js` runs `dotnet run` from sources and therefore needs the SDK on the server.

**`Production` is the default, and it changes two things.** With `ASPNETCORE_ENVIRONMENT` unset the app runs as `Production`. The Swagger **user interface** is then not mapped at all — `Program.cs` maps it only under `IsDevelopment()` — and user secrets are not read. Whatever a development machine kept in user secrets must therefore arrive as an environment variable; see [Configuration](#configuration). The OpenAPI **document** itself is mapped in every environment; see [API](#api).

**Listening address and reverse proxy.** The app serves both the API and the frontend under the path base `/orm` and listens on whatever `ASPNETCORE_URLS` names. `UsePathBase` strips that prefix where it appears but does not require it, so every page and endpoint answers at the root as well — `/required-content` works alongside `/orm/required-content`. A proxy is therefore what decides which of the two the outside world sees. The Docker image sets `http://+:5072`; the launch profiles bind `localhost:5072`, which is loopback only. The app terminates no TLS of its own, so a public instance belongs behind a reverse proxy that terminates HTTPS and forwards to it with the `/orm` prefix intact.

**`AllowedHosts` is `*`.** `appsettings.json` accepts any `Host` header. That is unobjectionable behind a proxy that already filters them, but an instance exposed directly should narrow it to its own host names.

**Verified on two machines** by publishing and running exactly this way, the four development paths having been verified on both as well. The instance reported `Hosting environment: Production`; `/orm/swagger` returned 404 while `/orm/` and the endpoints returned 200; a Dapper → EF Core translation returned its artifact together with the run record, carrying the tool version read from the assembly (the number that day was whatever `Directory.Build.props` held; it is not repeated here, so that this note does not go stale at every release); `ConnectionStrings__CatalogDatabase` supplied as an environment variable moved `CatalogState` from `NotConfigured` to `Reached`; a bogus `Host` header was accepted, as `AllowedHosts: *` promises; and `/advisor-test` answered 400 with the `libadvisor.so` load error rather than a 500. The content-root failure mode above was exercised too: started from a different working directory the same DLL served the endpoints and returned 404 for `/orm/`, and `--contentRoot` pointed at the publish directory restored it. Not covered by those runs: a Linux host, an actual reverse proxy in front, and the Advisor with its native library present — both machines were Windows, so the first of the three is as open as it was.

Two further facts a fresh instance inherits rather than configures. The Advisor needs the native `libadvisor.so`, which is built only inside the Docker image, so anywhere else the Advisor endpoints fail while translation keeps working. And with `ConnectionStrings__CatalogDatabase` left empty the catalog completion phase never runs: translation falls back on conventions and every `/convert` response reports it in `CatalogState`.

# Configuration
Six variables decide what an instance can do. None of them carries a value in the repository — `appsettings.json` ships both connection strings deliberately empty (requirement S4, decision [029](../docs/decisions/029-database-connection-is-the-consumer-projects-fact.md)), so each environment supplies its own. In `Development` the application and the tests also read user secrets, where the key is spelled with a colon (`ConnectionStrings:CatalogDatabase`); everywhere else it arrives as an environment variable, where the separator is a double underscore.

| Variable | What it enables | Without it |
|---|---|---|
| `ConnectionStrings__CatalogDatabase` | The catalog completion phase — filling mapping facts from database metadata. | The phase never runs. Translation proceeds on conventions, `/convert` reports the state in `CatalogState`, and criteria F4 and F6 do not hold through the interface. |
| `ConnectionStrings__AdvisorDatabase` | The Advisor's benchmarking runs. Also readable as `Advisor:ConnectionString`. | The Advisor does not run, and says which key to set. |
| `ConnectionStrings__TestDatabase` | Database-dependent tests. | They skip and say why; everything else still runs. |
| `ORMCONVERTOR_REQUIRE_TEST_DATABASE` | Turns that skip into a failure. Set it wherever the environment does provide a database. | A run that quietly skipped everything database-dependent passes as green. |
| `ORMCONVERTOR_TEST_SCHEMA` | The schema the tests create and drop. | Defaults to `ormconvertor_test`. |
| `ASPNETCORE_ENVIRONMENT` | `Development` maps the Swagger UI and reads user secrets. | Defaults to `Production`: neither. |

`docker-compose.yml` sets the environment and both application connection strings for the app service, and the test connection string plus the requirement flag for the test service; CI sets those last two. A host deployment normally has to think only about the first two.

# Advisor prerequisites
The translation features work in any environment. The Advisor additionally needs:

- A reachable SQL Server with the WideWorldImporters database. The connection string is read from `ConnectionStrings:AdvisorDatabase` or `Advisor:ConnectionString` (via `appsettings.json` or environment variables, e.g. `ConnectionStrings__AdvisorDatabase`). Inside docker-compose this resolves to the `mssql_db` service automatically.
- The native GLPK wrapper `libadvisor.so`, which is currently built only inside the Docker image. Outside Linux/Docker the Advisor endpoints will fail, while the rest of the application works normally.

How the ILP model itself is built and called is in [`architecture.md`](../docs/architecture.md) §8, including what happens when the model admits no solution.

# Tests

## In a container — one command, nothing but Docker required

```sh
docker compose --profile test run --rm tests
```

This is the "one main command" of requirement S5: the host needs nothing but Docker — no .NET SDK, no SQL Server of its own. It builds the solution, starts a SQL Server, waits for it, creates the database and runs the whole suite. The first run also pulls the images and builds them, which takes a few minutes; later runs reuse the layer cache.

The `test` profile adds three services:

- `test_db` — a plain SQL Server 2022 with no content at all. The fixture creates its own schema in it (see [The test database](#the-test-database)), so the tests are not tied to WideWorldImporters, whose purpose is the data volume for benchmarking.
- `test_db_init` — creates the `ORMConvertorTests` database, because the fixture creates a schema, not a database. It starts only once the health check confirms the server answers, so the suite is not skipped merely because the server was still starting.
- `tests` — the `tests` stage of `ORMConvertorAPI/Dockerfile`. It builds on the `dotnet-build` stage, so building the image is itself a check that the solution compiles on Linux too, and packages are baked into it rather than into a cache mount, so that `dotnet test --no-build` needs no network when the container starts. It receives `ConnectionStrings__TestDatabase` and, with it, `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, by which the environment declares that it does provide a database.

**`docker compose run` does not rebuild the image**, and for this service that is not a detail. The `tests` stage carries the sources and the compiled test assembly inside it — its entry point is `dotnet test --no-build` — so an existing image runs the suite of the revision it was built from, green and with nothing in the output to show that it describes a different tree. Evidence for the S5 claim is therefore produced only by the pair:

```sh
docker compose --profile test build tests
docker compose --profile test run --rm tests
```

and the only tell that the image was not stale is **the test count compared against [the size of the suite](#how-large-the-suite-is-and-what-it-covers)**. That is exactly how it was noticed on 2026-08-23: a run over a non-rebuilt image returned a count fifty-eight tests lower, i.e. the suite of 22 August, without `ConsumerProjectFactsTest` and without the REST contract tests.

**Verified by running it on two machines**, on 2026-08-23 and on 2026-08-24, and again on 2026-08-25, each time over the whole of the suite as it stood that day. The run finished from a clean state **with no failure and no skip**; the zero skips are the significant number, because that is what separates a run which demonstrated criteria F4 and F6 from one which merely did not contradict them. Both enforcement branches were tried as well: with the variable set and the database unreachable the suite fails with the stated reason, and without the variable and without a connection string it skips and stays green.

**The stale image caught a second victim on 2026-08-25, and that run is the sharpest evidence the check works.** A bare `run` against an image built the previous day reported **507 tests, green and with no skip** — the suite of 24 August — while the host of the very same checkout reported 602. Nothing in the container's output said so; only the count did. `build` followed by `run` then returned **602, equal to the host, with no skip**, which is the staleness check passing rather than merely being described. Trusting a container run whose image was not rebuilt in the same command is therefore not a theoretical risk: it has now silently reported the wrong tree twice.

## On the host

```sh
dotnet test Tests/Tests.csproj --configuration Release
```

Tests can also be run from Visual Studio's Test Explorer window. Database-dependent tests need a connection string, which is never stored in the repository: put it in the test project's user secrets as `ConnectionStrings:TestDatabase`, or pass the environment variable `ConnectionStrings__TestDatabase`, for example

```
Server=(localdb)\MSSQLLocalDB;Database=ORMConvertorTests;Trusted_Connection=True;TrustServerCertificate=True
```

The tests create and drop their own schema (`ormconvertor_test` by default; override with `ORMCONVERTOR_TEST_SCHEMA`), so any reachable SQL Server will do. Without a connection string those tests skip and say why, while everything else still runs. Where the environment states that it does provide a database — `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, set by the compose `test` profile and by CI — a missing database is a failure instead of a skip, so a run that quietly skipped everything database-dependent cannot pass as green.

## The test database

Per decision [016](../docs/decisions/016-generated-artifact-verification-levels.md), database-dependent tests connect to a **local SQL Server instance and create their schema themselves**. Everything needed for that is in `Tests/Database/`.

**The connection is configuration, not code.** `TestDatabase` composes its configuration from two sources — the test project's user secrets (the `UserSecretsId` is in `Tests.csproj`) and environment variables — and reads the connection string from them under the name `TestDatabase`, i.e. the key `ConnectionStrings:TestDatabase` in user secrets or the variable `ConnectionStrings__TestDatabase`. **No connection string to a foreign database is in the repository** (S4); the naming matches `ConnectionStrings__AdvisorDatabase` in `docker-compose.yml`. Since decision [039](../docs/decisions/039-container-configuration-of-the-environment.md) `docker-compose.yml` does contain connection strings — but aimed at servers that the same file creates and throws away, with the development password of a disposable instance. S4 does not forbid that: it forbids credentials in generated artifacts and in the log, not in the description of an environment the repository builds for itself (see [`threat-model.md`](../docs/threat-model.md)). The schema name is hard-wired to `ormconvertor_test` so a run can be inspected by hand; `ORMCONVERTOR_TEST_SCHEMA` overrides it (it must be a plain SQL identifier, otherwise it is rejected — it is substituted straight into DDL).

**The schema is also the expected answer.** It is described by the SQL script `Database/TestSchema.sql`, embedded into the assembly as a resource, with the placeholder `{{schema}}` in place of the schema name. A script rather than code, because it can also be run by hand against a local instance and is a readable statement of what F4 measures the share of correctly obtained metadata against. It contains exactly the cases at issue: a single-part key generated by `IDENTITY` (`Customers`, `Suppliers`) and one assigned by the application (`Products`), composite keys of two (`Orders`, `ProductSuppliers`), three (`OrderLines`) and four parts (`OrderLineAllocations`), one-, two- and three-column foreign keys, a 1:1 relation over a shared primary key (`CustomerProfiles`), a junction table (`ProductSuppliers`) and a set of types with length, precision/scale and nullability.

**Lifetime and the transaction boundary.** `TestSchemaFixture` is a collection fixture: the schema is created once for the whole `TestDatabaseSchema` collection and dropped after it finishes. Once per collection, because the catalog reader reads metadata, and metadata is written once and read many times. Before creation the schema is dropped first, so leftovers from a crashed run do not block the next one; the cleanup is written against the system catalog (drop all foreign keys, then all tables, then the schema), so adding a table to the script does not break it. The fixture itself holds no transaction — the DDL is shared and, for verification levels 2 and 3, read-only. Writing data needs a boundary, and the fixture offers `OpenConnection()` for it; the rule for a writing test is its own transaction over `OpenConnection()`, rolled back at the end, so that no test depends on what another left behind. **Verification level 4 writes through exactly that boundary**: `DapperToNHibernatePersistenceTest` and `DapperToEFCorePersistenceTest` store rows using the generated artifacts and load them back with the same identity (see [`architecture.md`](../docs/architecture.md) §6.2). The tests in `TestSchemaFixtureTest` use the same connection read-only, to guard the fixture's own schema.

**A missing database makes a test skip, not fail — unless the environment promised one.** `SkipIfUnavailable()` on the fixture calls the dynamic skip of xUnit v3 with a stated reason. Two are distinguished: either no connection string is configured (the reason says where to put it), or one is configured but the schema could not be prepared (the reason carries the connection error). Neither case fails the tests, and that is intent rather than tolerance: the suite is not to claim the opposite of what the tool claims, and the tool must not fail translation without a connected database. A skip is, however, itself a statement about coverage, and so the environment decides: where `ORMCONVERTOR_REQUIRE_TEST_DATABASE` is set (decision [039](../docs/decisions/039-container-configuration-of-the-environment.md)), `SkipIfUnavailable()` reports the same reason as a failure. Both places where a database is created by configuration set it — the `test` profile in `docker-compose.yml` and the workflow in `.github` — so a run in which everything database-dependent was silently skipped cannot pass there as green.

**What the fixture checks about itself.** `TestSchemaFixtureTest` verifies that the schema really contains what it promises — the existence of the tables, the order of the parts for all four key sizes, the column pairs of multi-column foreign keys, the junction table (whose whole key is composed of foreign keys) and, for selected columns, the type, nullability, length, precision/scale and the `IDENTITY` flag. Without it the fixture could silently stop containing the case at issue, and every later verdict about the catalog reader would lose its meaning.

The test project therefore references `Microsoft.Data.SqlClient` and the configuration packages; **this does not violate S1** — the wrappers stay free of any dependency on the framework they generate for.

## Continuous integration
A GitHub Actions pipeline runs the suite on pushes to `main` and on pull requests, whenever something inside `ORMConvertor/**` **or `.github/workflows/**`** changes; changes under `docs/`, `benchmarks/` and the like do not trigger it. The second half of that filter is there because without it the workflow did not verify itself: a faulty edit showed up only at the next change to the solution, or on Monday according to the schedule. It starts SQL Server 2022 as a service container, creates the `ORMConvertorTests` database itself and passes the string in `ConnectionStrings__TestDatabase` with the database required, so the database-dependent tests run there too. The workflow takes read permissions only (`permissions: contents: read`) — neither of its jobs writes anywhere. Development is solo and on `main`, so no pull request is ever actually opened — that trigger is there for a future in which one is. The pipeline configuration is located in the `.github` folder at the root of the repository.

Two more things it does. It collects **code coverage** through the `XPlat Code Coverage` collector (`coverlet.collector`) and uploads the results as a run artifact; no threshold gates the build — the measurement is there to be read, not to be passed. Locally the same thing is `dotnet test Tests/Tests.csproj --collect:"XPlat Code Coverage"`, which writes `coverage.cobertura.xml` under `Tests/TestResults/`. And a separate `dependencies` job runs `dotnet list package --vulnerable --include-transitive` and fails when a referenced package has a published advisory; because such an advisory appears without anyone pushing, that job also runs weekly on a schedule ([decision 041](../docs/decisions/041-versioning-and-release.md)). `dotnet list package --vulnerable` exits zero even where it found an advisory, so the verdict is made by a `grep` over the captured output; the block therefore runs with `set -o pipefail`, so that the pipe status is decided by `dotnet` and not by `tee`, for which writing the file is enough. Without that the job would pass at precisely the moment it stopped checking — and a gate that cannot fail is not a gate.

## How large the suite is, and what it covers

**Coverage is measured, but guards nothing.** `Tests.csproj` references `coverlet.collector`, so the `XPlat Code Coverage` collector emits `coverage.cobertura.xml` into `Tests/TestResults/`; CI collects the same on every run and attaches the result as a run artifact. **No coverage threshold fails the build**, and that is intent: the verification levels of decision [016](../docs/decisions/016-generated-artifact-verification-levels.md) state *what* is verified and how strongly, whereas coverage says only how far a run got — a threshold over such a number would push towards tests that raise the percentage and prove nothing. What the measurement is for: to substantiate claims about untested areas ([`architecture.md`](../docs/architecture.md) §9 excludes the Advisor and the benchmarking on the grounds that they have not a single test, which until then was a claim from reading the code) and to give the thesis text a figure of the family requirement T3 asks for. **This is the only place that says how large the suite is.** Everything else refers here and does not repeat the number — two figures in two places drifted apart once already, and the only defence against that is not to write them twice.

**Measured on 2026-08-26** (Release, host with a local SQL Server, connection string in user secrets), with `dotnet test Tests/Tests.csproj --configuration Release --collect:"XPlat Code Coverage"`: **610 passed, 0 failed, 0 skipped**. Coverage **75.5 % of lines and 63.5 % of branches** overall; of the individual projects the highest are `SampleData` and `CSharpEntityParsing` (100 %), `OrmConvertor` (95.0 %) and `Model` (93.1 %), the lowest **`Advisor` and `AdvisorBenchmarking`, both at 0.0 %** — which makes the exclusion of area 1 in [`architecture.md`](../docs/architecture.md) §9 a matter of a measured figure rather than of reading — and after them `ORMConvertorAPI` (41.1 %) and `LinqParsing` (63.6 %). The `ORMConvertorAPI` figure is not old: before the tests over HTTP (decision [043](../docs/decisions/043-rest-contract-guarded-over-http.md), [`architecture.md`](../docs/architecture.md) §6.5) it was **6.0 %**, because the endpoint handlers were exercised only through the orchestration. Taken on its own it is also lower than the project's own code looks: `Program` is at 100 %, `Endpoints` at 78.3 %, and all the DTOs and the data in `Data/` at 100 %. Two things pull the average down — types generated into the assembly by the `Microsoft.AspNetCore.OpenApi` source generator (XML comment support, not our code) and the Advisor handlers with their DTOs at 0 %, i.e. excluded area 1.

**The total is a property of the tree, not of the machine.** Nothing in the suite generates tests from its surroundings: the count is exactly the `[Fact]` methods plus the enumerated `[Theory]` cases under `Tests/`, and a static tally of the sources comes out at the same 610 the run reports. A total that disagrees therefore always means a *different tree* was measured — most often a container image that was not rebuilt (see [Tests](#tests)), or a working copy part-way through a change that had not been committed. What the environment does change is only the split of that fixed total between passed and skipped: without a reachable database the database-dependent tests skip, and where `ORMCONVERTOR_REQUIRE_TEST_DATABASE` is set they fail instead (decision [039](../docs/decisions/039-container-configuration-of-the-environment.md)). Reading a differing count as a difference between machines is thus the wrong diagnosis; the tree is what to check first.

**Zero skipped matters for this suite as much as the count does.** Without a configured database the database-dependent tests skip, so a run with the same number of passes and a non-zero number of skips would demonstrate nothing about criteria F4 and F6. That is exactly why the count is always given together with the conditions under which it arose.

Until 2026-08-23 two "whole suite" figures for the same day stood in two places, 419 and 392, differing by the twenty-seven tests that `ConsumerProjectFactsTest` had just added; both were correct and each was about a different suite. That is where the rule in the previous paragraph comes from. Since decision [057](../docs/decisions/057-deployment-view-in-the-operating-manual.md) put the whole deployment view in one document, the two places that could drift no longer exist — but the rule stays, because the container run and the host run are still two runs.

## Translation performance (S3)

`TranslationPerformanceTest` composes a project of 100 entities and 100 queries and measures a pass through `ConversionHandler.Convert` against a ceiling of 30 seconds. The direction is EF Core → NHibernate, because it employs the most machinery: Roslyn reads both attributed entities and LINQ chains on the way in, and every entity leaves as a class plus an hbm.xml and every query as a method plus bare HQL; the artifact counts are part of the assertion, so that the bound cannot hold over a quietly smaller scenario. This is the one figure here that is inherently machine-bound, so it is stated as two measurements rather than one. Intel Core i9-9900 (8 cores), 16 GB RAM, Windows 10 Pro: **~0.2 s** (2026-08-21, Release). Intel Core i7-1065G7 (4 cores), 16 GB RAM, Windows 11 Pro: **0.4–0.7 s** over three runs (2026-08-24, Release). The slower machine is roughly three times slower, and the margin against the 30 s bound falls from about a hundredfold to about fiftyfold — which is the point of quoting both: S3 holds on hardware differing by a factor of three, and the bound is nowhere near being a close call. Both numbers are the test's own stopwatch around `ConversionHandler.Convert`; the test's total duration is longer, because it also composes the 200 input sources. This scenario is deliberately dry — no connection is passed — so its number is the price of parsing and generation alone.

`CatalogTranslationPerformanceTest` measures the same bound **with a connected database**: a project of the same size, EF Core → NHibernate again, translated with the completion phase reading the catalog of the schema the test suite owns ([Tests](#tests)). Three of the hundred entities match the schema's tables, so the phase does everything it does in a real conversion — the batched image read over all hundred entities, fact completion, foreign keys and the junction probe — and one entity (`OrderLine`) even generates only because the catalog supplies its key; the rest of the assertion mirrors the dry test, plus the reached catalog state and the presence of supplied facts, so the bound cannot hold over a run that quietly stayed dry. The reader is created for the one conversion and disposed with it, which is exactly the cost `/convert` pays per request. Intel Core i9-9900 (8 cores), 16 GB RAM, Windows 10 Pro, local SQL Server: **0.4–0.5 s** total over three runs, of which the catalog read — the separately reported `CatalogReadTime` / `CatalogReadMilliseconds` of the same conversion ([`architecture.md`](../docs/architecture.md) §5.2) — is **210–330 ms** (2026-08-26, Release). Both halves of S3 thus carry a measured number: the 30 s bound holds with the catalog phase included, with a roughly sixtyfold margin on this machine, and the difference against the dry ~0.2 s is the catalog phase's price. The test writes both numbers into its output on every run, so re-measuring on another machine is a matter of running it.

# API
The REST API is the tool's actual product surface; the static frontend is one client of it. Everything is served under the path base `/orm`, so the paths below are relative to `http://<host>/orm`.

**The machine-readable contract is at `/orm/openapi/v1.json`, in every environment.** It is generated by `Microsoft.AspNetCore.OpenApi` and carries the tool version read from the assembly, so the document cannot claim a version the build did not produce. A generated snapshot of it is committed as [`ORMConvertorAPI/openapi.json`](ORMConvertorAPI/openapi.json) so the contract can be read, and diffed, without running anything — the running endpoint is the authoritative one, and the file is refreshed from it:

```sh
curl http://localhost:5072/orm/openapi/v1.json -o ORMConvertorAPI/openapi.json
```

The **Swagger UI** at `/orm/swagger` renders that same document, but only in the `Development` environment; Swashbuckle no longer generates a document of its own, so the two cannot drift apart.

| Method | Path | What it does | Request → response |
|---|---|---|---|
| `GET` | `/required-content` | What the interface has to collect for each source framework: one unit per input, naming the language its content is written in (decision [025](../docs/decisions/025-query-language-as-content-type.md)). | → `List<RequiredContentDefinition>` |
| `GET` | `/required-content-advisor` | The same list for the Advisor screen. | → `List<RequiredContentDefinition>` |
| `GET` | `/samples` | One sample input per unit above, keyed by the same id. | → `Dictionary<int, string>` |
| `GET` | `/samples-advisor` | The same for the Advisor screen. | → `Dictionary<int, string>` |
| `POST` | `/convert` | The translation itself: parses the sources in the source framework, completes them from the catalog if a connection string is configured, and builds the artifacts for the target framework. | `ConvertRequest` → `ConvertResponse` |
| `POST` | `/archive` | Packs client-named files into a ZIP for the complete-output download (decision [033](../docs/decisions/033-shape-of-the-static-frontend-screens.md)). Translates nothing. | `ArchiveRequest` → `application/zip` |
| `POST` | `/advisor/run` | A full Advisor run: translates the queries into the candidate frameworks, compiles and benchmarks them, and solves the ILP model. The response carries the translated artifacts the run measured next to the numbers (decision [059](../docs/decisions/059-advisor-response-carries-the-measured-translations.md)). Needs both a database and `libadvisor.so`. | `AdvisorRunRequest` → `AdvisorRunResult` |
| `POST` | `/advisor-test` | The bare ILP solver, called with a cost matrix directly — a hook for exercising the model, not a translation path. | `AdvisorSolveRequest` → `AdvisorSolveResponse` |

Every `POST` answers a failure with `400` and a `ProblemDetails` body per RFC 9457 (`application/problem+json`), the exception message in `detail` ([decision 044](../docs/decisions/044-error-response-as-problem-details.md)). The request and response types live in `ORMConvertorAPI/Dtos/`. `ConvertResponse` carries the run record required by S6 alongside the artifacts — run id, tool version, both framework versions, the conversion records, and the catalog state — so a caller can tell from the response alone what the translation was and was not able to see.

# Frontend
The frontend is a set of hand-written static pages in `ORMConvertorAPI/wwwroot` — HTML, native ES modules, and CSS with no framework, no npm, and no build step. What is committed is exactly what the browser runs, so there is nothing to compile or copy; the ASP.NET application serves the files directly under `/orm/`. Third-party assets (Pico CSS, highlight.js) are vendored under `wwwroot/vendor/` with their versions and licenses.
