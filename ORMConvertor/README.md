A web-based tool for translating across different .NET ORMs.

# Deployment

## Visual Studio
Open the solution file `ORMConvertor.sln` and set `ORMConvertorAPI` as the startup project, if not selected by default. The app can be launched in `Debug` configuration for development or compiled in `Release` configuration for production. Three launch profiles are defined in `ORMConvertorAPI/Properties/launchSettings.json` (`http`, `https`, `IIS Express`). Both `http` (`http://localhost:5072`) and `https` (`https://localhost:7124`, with `http://localhost:5072` beside it) are configured and were verified on 2026-08-21; `https` additionally expects the ASP.NET development certificate (`dotnet dev-certs https`). `IIS Express` is a leftover of the project template: no documented path uses it and it is not verified. The application can be started with `CTRL + F5` to run without debugging or `F5` to launch with the debugger. A browser window should open automatically.

## .NET CLI
```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```
This approach does not open a browser automatically. Instead, the local URL is printed to the console (typically [http://localhost:5072/orm/](http://localhost:5072/orm/)).

## Docker (application + database)
`docker-compose.yml` in this directory describes the whole environment — the system and the test suite alike — and a profile keeps the two apart, so the command below never starts the test services.

```sh
docker compose up --build
```

starts two containers:

- `ormconvertor` – the application, built by the multi-stage `ORMConvertorAPI/Dockerfile` (compiles the native Advisor library `libadvisor.so` and the .NET app; the static frontend needs no build and enters the image as-is inside `wwwroot`). Exposed on [http://localhost:5072/orm/](http://localhost:5072/orm/). It receives both connection strings — `ConnectionStrings__AdvisorDatabase` for the Advisor's benchmarking and `ConnectionStrings__CatalogDatabase` for the catalog completion phase — so the containerized instance translates with the catalog rather than without it.
- `mssql_db` – Microsoft SQL Server 2022 initialized with the WideWorldImporters sample database (`database.Dockerfile`), exposed on `localhost,1444` (`SA` / `Testingorms123` – development credentials only). That host port is also what the inherited benchmarks bind: `benchmarks/README.md` documents `podman run -p 1444:1433` for their own SQL Server image, so the two cannot be up at the same time. Stop one before starting the other, or change the mapping in `docker-compose.yml`.

Note: the compose file runs the app with `ASPNETCORE_ENVIRONMENT=Development`, so the Swagger UI is available at `/orm/swagger`.

## PM2
`ecosystem.config.js` is a configuration for the [PM2](https://pm2.keymetrics.io/) process manager, used to run the app via `dotnet run` on a server without Docker: `pm2 start ecosystem.config.js` starts it, `pm2 delete orm` stops it. The host needs Node.js with PM2 installed (`npm install -g pm2`) and the .NET SDK, because this path builds from sources; the app then listens on `localhost:5072` only.

## Deploying a real instance
The four paths above are development paths: every one of them runs in the `Development` environment and three of them run from sources. A real instance differs in four ways.

**Publish instead of `dotnet run`.** A published instance needs only the ASP.NET Core runtime, not the SDK:

```sh
dotnet publish ORMConvertorAPI/ORMConvertorAPI.csproj --configuration Release --output /srv/ormconvertor
cd /srv/ormconvertor && dotnet ORMConvertorAPI.dll
```

**The `cd` is not cosmetic.** The content root defaults to the *current working directory*, and the content root is what decides where the app looks for `wwwroot` and for `appsettings.json`. Start the same DLL from anywhere else and it comes up looking healthy — the API answers, the log says `Production` — while `/orm/` returns 404 (`The WebRootPath was not found`) and the published `appsettings.json` is never read. A service manager that cannot set the working directory should name the path instead:

```sh
dotnet /srv/ormconvertor/ORMConvertorAPI.dll --contentRoot /srv/ormconvertor
```

The publish output carries `wwwroot` with it, so the frontend needs no copy step of its own. This is the same publish the Docker image runs (`ORMConvertorAPI/Dockerfile`, stage `dotnet-build`), after which the output runs on the `aspnet:10.0` image rather than the SDK one. The PM2 configuration is the exception: `ecosystem.config.js` runs `dotnet run` from sources and therefore needs the SDK on the server.

**`Production` is the default, and it changes two things.** With `ASPNETCORE_ENVIRONMENT` unset the app runs as `Production`. The Swagger UI is then not mapped at all — `Program.cs` maps it only under `IsDevelopment()` — and user secrets are not read. Whatever a development machine kept in user secrets must therefore arrive as an environment variable; see [Configuration](#configuration).

**Listening address and reverse proxy.** The app serves both the API and the frontend under the path base `/orm` and listens on whatever `ASPNETCORE_URLS` names. `UsePathBase` strips that prefix where it appears but does not require it, so every page and endpoint answers at the root as well — `/required-content` works alongside `/orm/required-content`. A proxy is therefore what decides which of the two the outside world sees. The Docker image sets `http://+:5072`; the launch profiles bind `localhost:5072`, which is loopback only. The app terminates no TLS of its own, so a public instance belongs behind a reverse proxy that terminates HTTPS and forwards to it with the `/orm` prefix intact.

**`AllowedHosts` is `*`.** `appsettings.json` accepts any `Host` header. That is unobjectionable behind a proxy that already filters them, but an instance exposed directly should narrow it to its own host names.

**Verified on 2026-08-22** by publishing and running exactly this way on Windows against the .NET 10 runtime, the four development paths having been verified on 2026-08-21. The instance reported `Hosting environment: Production`; `/orm/swagger` returned 404 while `/orm/` and the endpoints returned 200; a Dapper → EF Core translation returned its artifact together with the run record, tool version `1.0.0` read from the assembly; `ConnectionStrings__CatalogDatabase` supplied as an environment variable moved `CatalogState` from `NotConfigured` to `Reached`; a bogus `Host` header was accepted, as `AllowedHosts: *` promises; and `/advisor-test` answered 400 with the `libadvisor.so` load error rather than a 500. Not covered by that run: a Linux host, an actual reverse proxy in front, and the Advisor with its native library present.

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

# Tests

## In a container — one command, nothing but Docker required

```sh
docker compose --profile test run --rm tests
```

This builds the solution, starts a SQL Server of its own, waits for it, creates the database and runs the whole suite. The first run also pulls the images and builds them, which takes a few minutes; later runs reuse the layer cache. A clean machine needs neither the .NET SDK nor a database of its own (requirement S5). The `test` profile adds three services: `test_db` (a plain SQL Server 2022 — the tests create their own schema in it and never touch WideWorldImporters), `test_db_init` (creates the `ORMConvertorTests` database once the server answers, because the test fixture creates a schema, not a database) and `tests` (the `tests` stage of `ORMConvertorAPI/Dockerfile`).

## On the host

```sh
dotnet test Tests/Tests.csproj --configuration Release
```

Tests can also be run from Visual Studio's Test Explorer window. Database-dependent tests need a connection string, which is never stored in the repository: put it in the test project's user secrets as `ConnectionStrings:TestDatabase`, or pass the environment variable `ConnectionStrings__TestDatabase`, for example

```
Server=(localdb)\MSSQLLocalDB;Database=ORMConvertorTests;Trusted_Connection=True;TrustServerCertificate=True
```

The tests create and drop their own schema (`ormconvertor_test` by default; override with `ORMCONVERTOR_TEST_SCHEMA`), so any reachable SQL Server will do. Without a connection string those tests skip and say why, while everything else still runs. Where the environment states that it does provide a database — `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, set by the compose `test` profile and by CI — a missing database is a failure instead of a skip, so a run that quietly skipped everything database-dependent cannot pass as green.

## Continuous integration
A GitHub Actions pipeline runs the suite on pushes to `main` and on pull requests, whenever something inside `ORMConvertor/**` changes. It starts SQL Server 2022 as a service container, creates the test database and runs with the database required, so the database-dependent tests run there too. Development is solo and on `main`, so no pull request is ever actually opened — that trigger is there for a future in which one is. The pipeline configuration is located in the `.github` folder at the root of the repository.

Two more things it does. It collects **code coverage** through the `XPlat Code Coverage` collector (`coverlet.collector`) and uploads the results as a run artifact; no threshold gates the build — the measurement is there to be read, not to be passed. Locally the same thing is `dotnet test Tests/Tests.csproj --collect:"XPlat Code Coverage"`, which writes `coverage.cobertura.xml` under `Tests/TestResults/`. And a separate `dependencies` job runs `dotnet list package --vulnerable --include-transitive` and fails when a referenced package has a published advisory; because such an advisory appears without anyone pushing, that job also runs weekly on a schedule ([decision 041](../docs/decisions/041-versioning-and-release.md)).

# API
The REST API is the tool's actual product surface; the static frontend is one client of it. Everything is served under the path base `/orm`, so the paths below are relative to `http://<host>/orm`. The Swagger UI at `/orm/swagger` documents the same endpoints, but only in the `Development` environment — this table is what remains available in `Production`.

| Method | Path | What it does | Request → response |
|---|---|---|---|
| `GET` | `/required-content` | What the interface has to collect for each source framework: one unit per input, naming the language its content is written in (decision [025](../docs/decisions/025-query-language-as-content-type.md)). | → `List<RequiredContentDefinition>` |
| `GET` | `/required-content-advisor` | The same list for the Advisor screen. | → `List<RequiredContentDefinition>` |
| `GET` | `/samples` | One sample input per unit above, keyed by the same id. | → `Dictionary<int, string>` |
| `GET` | `/samples-advisor` | The same for the Advisor screen. | → `Dictionary<int, string>` |
| `POST` | `/convert` | The translation itself: parses the sources in the source framework, completes them from the catalog if a connection string is configured, and builds the artifacts for the target framework. | `ConvertRequest` → `ConvertResponse` |
| `POST` | `/archive` | Packs client-named files into a ZIP for the complete-output download (decision [033](../docs/decisions/033-shape-of-the-static-frontend-screens.md)). Translates nothing. | `ArchiveRequest` → `application/zip` |
| `POST` | `/advisor/run` | A full Advisor run: translates the queries into the candidate frameworks, compiles and benchmarks them, and solves the ILP model. Needs both a database and `libadvisor.so`. | `AdvisorRunRequest` → `AdvisorRunResult` |
| `POST` | `/advisor-test` | The bare ILP solver, called with a cost matrix directly — a hook for exercising the model, not a translation path. | `AdvisorSolveRequest` → `AdvisorSolveResponse` |

Every `POST` answers a failure with `400` and the exception message. The request and response types live in `ORMConvertorAPI/Dtos/`. `ConvertResponse` carries the run record required by S6 alongside the artifacts — run id, tool version, both framework versions, the conversion records, and the catalog state — so a caller can tell from the response alone what the translation was and was not able to see.

# Frontend
The frontend is a set of hand-written static pages in `ORMConvertorAPI/wwwroot` — HTML, native ES modules, and CSS with no framework, no npm, and no build step. What is committed is exactly what the browser runs, so there is nothing to compile or copy; the ASP.NET application serves the files directly under `/orm/`. Third-party assets (Pico CSS, highlight.js) are vendored under `wwwroot/vendor/` with their versions and licenses.
