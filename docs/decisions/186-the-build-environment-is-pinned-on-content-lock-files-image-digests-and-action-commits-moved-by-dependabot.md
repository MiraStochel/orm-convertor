# 186 — Prostředí sestavení je připnuté na obsah: zámek balíčků u každého projektu, základní obrazy digestem, akce CI commitem, posun navrhuje Dependabot; nárok S2 na sestavení nevstupuje a vynucení stylu je mimo rozsah

Datum: 2026-10-25
Stav: platí
Požadavky: S5, S2, S4
Podklad: audit [2026-08-23](../audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.4; rozhodnutí [013](013-target-framework-versions.md), [034](034-central-version-management.md), [039](039-container-configuration-of-the-environment.md), [041](041-versioning-and-release.md), [095](095-a-dated-run-record-names-its-commit.md) a [098](098-the-number-is-decided-once-per-release.md); `ORMConvertor/Directory.Build.props`, `Directory.Packages.props`, `ORMConvertorAPI/Dockerfile`, `database.Dockerfile`, `docker-compose.yml` a `.github/workflows/ormconvertor-tests.yml` ke dni rozhodnutí; registry MCR, Docker Hub, NuGet a značky repozitářů `actions/*` dotázané 2026-10-25; otevřená položka *Vynucení stylu a reprodukovatelnost sestavení*

## Kontext

Požadavek S5 chce, aby čisté prostředí reprodukovalo testy jedním hlavním příkazem, a rozhodnutí [039](039-container-configuration-of-the-environment.md) to splnilo: `docker compose --profile test build` a `run` postaví i spustí obě sady na stroji, kde není nic než Docker. Audit z 2026-08-23 k tomu dodal, že „reprodukovatelné" tu znamená *jedním příkazem*, ne *totéž*: ten příkaz staví proti tomu, kam onoho dne ukazují pohyblivé značky, a dvě sestavení téhož commitu v různých dnech tak mohou stát na různých vstupech. Otevřená položka z toho udělala otázku, zda se nárok S2 (determinismus) rozšíří z výstupu překladu i na sestavení nástroje, nebo zda jde o údržbu.

Co bylo ke dni rozhodnutí pohyblivé, jsme změřili proti stromu a proti registrům, ne z paměti:

| Místo | Stav ke dni rozhodnutí | Na co značka toho dne ukazovala |
|---|---|---|
| balíčky NuGet | přímé verze centrálně v `Directory.Packages.props` (rozh. 034), **žádný `packages.lock.json`** u žádného z 23 projektů; tranzitivní graf rozhoduje každé `restore` znovu | — |
| `ORMConvertorAPI/Dockerfile` | `mcr.microsoft.com/dotnet/sdk:10.0` (dvakrát), `mcr.microsoft.com/dotnet/aspnet:10.0`, `maven:3.9.11-eclipse-temurin-25-noble` | `sdk:10.0` = `10.0.402`, `aspnet:10.0` = `10.0.13`; obraz Mavenu má značku přesnou, digest ne |
| `database.Dockerfile`, `docker-compose.yml` (`test_db_init`), workflow (služby obou jobů) | `mcr.microsoft.com/mssql/server:2022-latest` | `2022-CU27-GDR1-ubuntu-22.04` |
| workflow | `actions/checkout@v5`, `actions/setup-dotnet@v5`, `actions/setup-java@v6`, `actions/upload-artifact@v7` | značka hlavní verze se posouvá s každým vydáním v ní |
| `.github/dependabot.yml` | neexistuje; úloha `dependencies` hlídá jen bezpečnostní hlášení NuGetu | — |

Proč na tom záleží, i když nárok S2 na sestavení nestojí. **Záznam běhu jmenuje commit** (rozh. [095](095-a-dated-run-record-names-its-commit.md)) a opírá o něj verdikt nad celou sadou, běh FIBEN a experimentální běh; commit ale dnes neurčuje obraz SQL Serveru, SDK ani tranzitivní balíčky, nad nimiž ten verdikt vznikl, takže „prostředí verze" v záznamu je prostředí dne, ne commitu. **Úloha `dependencies`** hodnotí tranzitivní graf dnešního `restore`, který se od grafu zaznamenaného běhu může lišit, aniž by to kdo viděl. A **značka registru je plocha, kterou nikdo z projektu nehlídá**: kdo může psát pod `2022-latest` nebo pod `v5`, může změnit, co se tu staví a spouští, bez commitu v tomto repozitáři — S4 mluví o tajemstvích, ale tohle je totéž téma důvěry v prostředí.

Druhá polovina položky — **vynucení stylu** (`EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`, `dotnet format` v CI) — s reprodukovatelností sousedí jen v auditu, kde vzešla z jednoho nálezu o `Directory.Build.props`. Věcně je to jiná práce: nic nereprodukuje, mění, co build přijme.

## Zvažované varianty

**Nechat být jako údržbu, která počká.** Nejlevnější dnes, nejdražší při prvním záznamu, který nebude možné zopakovat. Všechno, co níž zavádíme, stojí jednu vlastnost MSBuildu, čtyři řetězce s digestem a jeden soubor; odkládat to nemá co ušetřit.

**Připnout vstupy sestavení a nárok nerozšiřovat** — doporučená varianta, níž.

**Rozšířit S2 i na sestavení**: `ContinuousIntegrationBuild`, bajtově shodné assembly, srovnání dvou nezávislých sestavení. Odmítáme. Nárok, který projekt vysloví, potřebuje důkaz (`traceability.md` má řádek *důkaz* právě proto) a důkazem bajtové shody je druhé sestavení v jiném prostředí a jeho záznam v `docs/runs/`; nic z toho nemáme a nikdo z konzumentů nástroje bajty sestavení nebere — artefaktem nástroje je výstup překladu, na který S2 míří dnes. Jediné, co by bajtovou shodu potřebovalo, je položka *Trvalý identifikátor vydání* (haš balíčku vydání, který má vyjít i z nového sestavení); ta se rozhoduje až na konci vývoje a může toto rozhodnutí v té části nahradit. Připnutí vstupů, které zavádíme, je její nutná a dnes chybějící podmínka, ne její splnění.

**Dependabot i pro NuGet a Maven.** Odmítáme. Rozhodnutí [041](041-versioning-and-release.md) a [098](098-the-number-is-decided-once-per-release.md) říkají, že zafixovaná verze cizího balíčku se posouvá ze dvou důvodů — bezpečnostní hlášení a potřeba rozdělané práce — a že „vyšla novější" mezi nimi není, protože na verzích stojí platnost tvrzení v `docs/analysis/` a verze cílového frameworku cestuje deskriptorem do záznamu běhu. Týdenní pull requesty s posuny by tomu pravidlu odporovaly po jednom; hlášení hlídá úloha `dependencies`, a hlídá je nyní i nad zamčeným grafem.

**Přesná verze SDK na hostiteli** (`global.json` bez `rollForward`, přesná verze v `setup-dotnet`). Odmítáme. Verdikt vzniká v obraze (rozh. 039) a ten je nyní připnutý digestem; CI i hostitel jsou kontroly, ne prostředí verze, a hostitelský `latestFeature` je pohodlí dvou strojů, které stejně nejsou totožné (viz [`architecture.md`](../architecture.md) §1).

**Zámek pro Maven.** Maven zámek první strany nemá; `pom.xml` fixuje přímé verze (rozh. 013) a obraz stupně `java-tests` je připnutý digestem, takže vstupy javové sady jsou určené pom a obrazem; tranzitivní mediaci Mavenu nad týmž pom a týmž obrazem bereme jako vyslovenou mez, ne jako problém k řešení.

## Rozhodnutí

Vstupy sestavení jsou připnuté na **obsah**, ne na značku, všude, kde je repozitář jmenuje. Nárok S2 se **nemění**: dál mluví o výstupu překladu. Nárok S5 se nemění ve znění, jen se posouvá to, co „reprodukuje" znamená — čisté prostředí staví a testuje nad týmiž vstupy, nad kterými stál commit.

### Balíčky: zámek u každého projektu

`Directory.Build.props` nastavuje `RestorePackagesWithLockFile`, takže každý projekt v adresáři řešení — všech 22 v `ORMConvertor.sln` i `FibenImport`, který props sdílí — má vedle `.csproj` soubor `packages.lock.json`, a ten je commitovaný. Přímé verze dál píše jediné místo (rozh. 034); zámek je strojový zápis grafu, do kterého se ty verze rozvinou, a je **tím, čím se od nynějška dokládá tranzitivní závislost** — `obj/project.assets.json` je totéž po posledním `restore`, ale není v gitu. CI (`dotnet restore --locked-mode`, `dotnet publish -p:RestoreLockedMode=true`) i obraz (`--locked-mode` v obou `restore` stupně `dotnet-build` a ve stupni `tests`) obnovují v zamčeném režimu: zámek, který s připnutými verzemi nesouhlasí, tam sestavení **shodí**, místo aby se přepsal. Na hostiteli ho prosté `dotnet restore` při posunu verze přepíše — a posun se commituje i se zámkem. Stupeň `dotnet-build` kopíruje zámek vedle každého `.csproj`, aby vrstva `restore` zůstala v cache.

### Obrazy: digest vedle značky přesné verze

Každý `FROM` a každé `image:` nese `<značka přesné verze>@sha256:<digest>`: `sdk:10.0.402`, `aspnet:10.0.13`, `maven:3.9.11-eclipse-temurin-25-noble` a `mssql/server:2022-CU27-GDR1-ubuntu-22.04`, s digesty, na které značky `10.0`, `10.0`, tato a `2022-latest` ukazovaly 2026-10-25. Značka je pro čtenáře, digest je to, co build vezme; posouvají se spolu. Odkaz na obraz SQL Serveru je ve všech čtyřech souborech — `database.Dockerfile`, `docker-compose.yml`, obě služby workflow — **jeden řetězec**, a drží to `Combined/PinnedEnvironmentTest`: každý `FROM` mimo stupně téhož souboru, každé `image:` a každé `uses:` nese digest či commit, a odkazů na obraz SQL Serveru je napříč soubory právě jeden. V kontejneru, kde workflow není součástí kontextu sestavení, test ověří jen to, že běží v kontejneru (`DOTNET_RUNNING_IN_CONTAINER`), a kontrolu workflow dělá běh na hostiteli a CI; nepřeskakuje, aby počet přeskočených testů běhu zůstal tím, co říkají rozhodnutí.

Digest obrazu SQL Serveru je digest jediného manifestu pro `amd64`, protože MCR pod tou značkou víc architektur nenabízí; oba stroje projektu i běžec CI jsou `amd64`, hostitel jiné architektury by potřeboval jiný pin — vyslovená mez.

### Akce CI: commit vedle verze

Každé `uses:` jmenuje commit a za ním v komentáři verzi, kterou ten commit nese (`actions/checkout@fbc6f399… # v5.1.0` atd.), vždy nejnovější vydání **téže hlavní verze**, kterou workflow používalo; hlavní verzi neposouváme, protože „vyšla novější" není důvod ani zde.

### Posun navrhuje Dependabot

`.github/dependabot.yml` sleduje ekosystémy `docker` (oba adresáře s Dockerfile), `docker-compose` a `github-actions`, týdně v pondělí jako úloha `dependencies`. Pull request Dependabota je jediná větev, která v repozitáři vzniká: „žádné větve" rozhodnutí 098 mluví o vývoji, a návrh robota, který Míra slije po zeleném CI, vývoj není — trigger `pull_request` workflow, který 098 označilo za „pro budoucnost", tím dostává první použití. Služby workflow Dependabot nepokrývá a `database.Dockerfile` podle názvu může minout; slitý návrh, který je nechá pozadu, shodí `PinnedEnvironmentTest`, a doplní se rukou. NuGet ani Maven v souboru nejsou, viz varianty.

### Verze balíčků se dnes neposouvají

Při zavedení zámku jsme zvážili, zda s ním některý balíček posunout, a odpověď je ne — podle pravidla rozhodnutí 041, které jsme nad každým kandidátem ověřili:

| Balíček | Připnuto | Novější 2026-10-25 | Proč zůstává |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore(.SqlServer)`, `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*` | 10.0.10 | 10.0.13 | EF Core je cílový framework: verze cestuje deskriptorem do záznamu běhu a posun není nikdy PATCH (rozh. 013, 098); balíčky ASP.NET a Extensions se drží s ním |
| `Microsoft.Data.SqlClient` | 7.0.2 | 7.1.1 | i 7.1.1 závisí na `System.Security.Cryptography.Pkcs` 8.0.1, takže přímá reference na opravenou verzi (rozh. 034) by zůstala; posun by nic nezjednodušil |
| `System.Security.Cryptography.Pkcs` | 9.0.21 | 10.0.13 | referencovaná pro hlášení, ne pro sebe; 9.0.21 hlášení řeší |
| `Microsoft.SqlServer.TransactSql.ScriptDom` | 180.78.1 | 180.140.0 | gramatika, proti které platí čtení i zápis T-SQL (rozh. 082) |
| `Microsoft.CodeAnalysis.CSharp` | 5.6.0 | 5.9.0 | kompilace generovaného kódu stojí na něm (ověřovací úroveň rozh. 016) |
| `xunit.v3`, `xunit.runner.visualstudio` | 3.2.2, 3.1.5 | 4.0.2, 4.0.1 | hlavní verze nástroje sady bez potřeby rozdělané práce |
| `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `Swashbuckle.AspNetCore`, `Dapper` | 18.8.1, 10.0.1, 10.2.3, 2.1.79 | 18.11.0, 10.1.0, 10.3.1, 2.1.89 | žádné hlášení, žádná rozdělaná práce; Dapper je cílový framework |
| `NHibernate` | 5.7.0 | 5.7.0 | — |

Javové verze (`pom.xml`) se neposouvají z téhož pravidla; vyhledávací index Maven Central odpovídal 2026-10-25 staršími verzemi, než jsou připnuté, takže o „novější" na javové straně nic netvrdíme.

### Vynucení stylu je mimo rozsah

Pravidla `.editorconfig` zůstávají věcí editoru. `EnforceCodeStyleInBuild`, `AnalysisMode`, `TreatWarningsAsErrors` ani `dotnet format --verify-no-changes` v CI nezavádíme: první zapnutí by obarvilo zděděný kód prototypu i generované entity FIBEN a LDBC, jejichž jména jsou jména sloupců benchmarku, a `TreatWarningsAsErrors` by shazovalo sestavení s každou novou sadou analyzátorů v dalším pásmu SDK — tedy přesně nereprodukovatelnost, kterou toto rozhodnutí odstraňuje. Druhý stroj bez shellu by styl před commitem ověřit neuměl. Položka s tímto zněním jde do oddílu *Mimo rozsah* v `open-items.md`.

## Důsledky

- **Posun verze má o krok víc.** Změna v `Directory.Packages.props` přepíše zámky (`dotnet restore` na hostiteli), a commit nese obojí; zámek, který se zapomene, shodí CI i obraz. Nový projekt dostane zámek prvním `restore`, commituje se, a řádek `COPY` ve stupni `dotnet-build` ho kopíruje vedle `.csproj`.
- **Záplaty obrazů nepřicházejí samy.** `aspnet:10.0.13` zůstane 10.0.13, dokud Míra neslije návrh Dependabota; to je cena za to, že commit určuje obraz. Totéž pro kumulativní aktualizace SQL Serveru a vydání akcí.
- **Hostitelské SDK a obraz se mohou lišit v pásmu.** `global.json` dál dovoluje `latestFeature`; co platí, je obraz.
- **Tranzitivní závislost se dokládá zámkem**, ne `obj/project.assets.json`.
- `architecture.md` §1 popisuje zámek, digesty a Dependabot; `traceability.md` S5 jmenuje `PinnedEnvironmentTest`; kořenový `README.md` říká, že piny prostředí posouvá Dependabot a že reprodukovatelné sestavení už mezi „užitečné, ne nutné" nepatří; `ORMConvertor/README.md` nese zámek a `--locked-mode` u sestavení a CI. Položka *Vynucení stylu a reprodukovatelnost sestavení* mizí z `open-items.md`; položka *Trvalý identifikátor vydání* na toto rozhodnutí odkazuje místo na ni.
