# Běh mezí, javová půlka

- **Začátek:** 2026-10-30 19:41 UTC; procházka s fixturou jako katalogem 19:49 UTC – 2026-10-31 15:46 UTC (z toho 02:09–07:46 UTC nad mrtvým klientem sady a 07:46–07:53 UTC zastavená, níž), nasucho 15:47 UTC – 2026-11-01 03:45 UTC, souhrn do 03:46 UTC
- **Strom:** obrazy ze stromu `fd27497` záznamu [2026-10-29](2026-10-29-limits.md), doplněného o čtyři soubory javové sady z `6d5d5a5` (`ExperimentRunPlanTest.java`, `ExperimentRunTest.java`, `ForeignVerdict.java`, `ToolApi.java`; rozh. [195](../decisions/195-the-java-half-of-the-limits-run-continues-where-it-was-skips-what-the-dotnet-half-could-not-translate-and-translates-by-an-instance-with-a-memory-limit.md)), o `docker-compose.yml` pracovní kopie (rozh. 195, [200](../decisions/200-the-instances-of-the-java-half-of-the-limits-run-translate-with-the-nesting-cap-off-by-a-compose-variable-of-that-run.md)) a od 07:50 UTC 2026-10-31 o `JavaTests/pom.xml` s `argLine` Surefire (níž); překladač tedy týž, nad kterým stojí obě .NET půlky
- **Stroj a prostředí:** MIS3; virtuální stroj se čtyřmi procesory, 12 GB a bez swapu (rozh. [193](../decisions/193-the-docker-vm-has-twelve-gigabytes-so-that-the-limits-inside-it-add-up-to-less-than-it.md)); `test_db` (SF 0,1) s `max server memory` 2048 MB, `MAXDOP` 1, procesory 0–1; kontejner sady 6 GB na procesorech 2–3; jediná instance s mezí 2 GB a se stropem zanoření vypnutým (`Parsing__MaxNestingDepth=0`, rozh. 200) — `test_app` pro procházku s fixturou, `test_app_dry` nasucho; nejvyšší i nejnižší stav procesoru 99 % do 07:50 UTC 2026-10-31, od restartu 100 % a 5 % s turbem (javová půlka čas neměří, rozh. 182); Windows Update pozastavené
- **Co běželo:** javová půlka běhu rozh. [182](../decisions/182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md) podle rozh. 195 nad body, které obě .NET půlky vypsaly 2026-10-29 do svazku `ormconvertor_limits`: obě procházky bod po bodu, pak souhrn nad oběma půlkami v kontejneru (`ExperimentSummaryTest`). Řádky javové půlky z 2026-10-29 (řady `case` až `joins` po bod 256) stály; před během z nich šlo pryč 165 řádků, které instance odmítla stropem zanoření 128, a řádek `joins-256` Dapper → MyBatis zapsaný nad umírajícím serverem (1077 → 911), aby se jejich body soudily znovu

## Výsledek

4776 řádků javové půlky, 2388 na procházku: každý bod .NET půlky do tří javových cílů; bod, který .NET půlka do cíle nepřeložila, nese její výsledek (`too slow`: 512 joinů ze zdroje EF Core). Nasucho odmítl instance už první bod zdrojů Dapper a MyBatis do tří javových cílů (bez katalogu nemají klíč, `refused`), takže nasucho má každá řada 12 směrů místo 18 a Hibernate i EclipseLink tam stojí jen nad zdroji EF Core, EclipseLink, Hibernate a NHibernate. Instance se během nerestartovala ani jednou a server byl zdravý celý běh.

Poslední přijatý a poslední běžící bod každé osy do javových cílů (shodně v obou procházkách, kde není řečeno jinak; „strop" je bod, ke kterému došla .NET půlka):

| Osa (strop) | Hibernate | EclipseLink | MyBatis | Co zastavilo cestu |
|---|---|---|---|---|
| `joins` (512) | přijal 512, běžel 512 | 512, 512 | 512, 512 | SQL Server dotaz 512 joinů v textu javových cílů **spustil**, 27 min na směr (`runMs` 1,6–1,8 mil.); Dapper → Hibernate s fixturou `hung` po 30 min; zdroj EF Core končí na 256 (.NET půlka `too slow`) |
| `conditions` (4096) | 2048, 2048 | přijal 1024 nebo 2048 podle směru, běžel 512 | 4096, 4096 | javac: text přes 64 KB (4096); EclipseLink 2048 výjimka bez zprávy; běh: EclipseLink řetěz uzávorkuje a SQL Server ho od 1024 odmítne (`nested too deeply`, chyba 191) |
| `nesting` (4096, strop zanoření vypnutý) | 4096, 4096 | 4096, 4096 | 4096, 4096 | nic — se stropem instance vypnutým došly javové cíle ke stropu řady; řádek „javové instance: strop 128 nasazené instance" záznamu 2026-10-29 je tím uzavřený |
| `subqueries` (2048) | 512, 32 | 512, 32 | 2048, 32 | javac 64 KB (1024); běh: od 64 úrovní SQL Server plán nesestaví (chyba 8623) |
| `exists` (512) | 512, 16 | 512, 16 | 512, 16 | běh: od 32 `EXISTS` plán nevznikne (8623) |
| `ctes` (256) | 256, 256 | — (řada EclipseLink vynechává, mezivýsledek nemá) | 256, 256 | zdroj EF Core končí na 16 (.NET půlka `too slow` na 32) |
| `unions` (256) | 256, 256 | **přijal 1, od 2 `not accepted`** | 256, 256 | parser EclipseLinku nevezme dotaz, který začíná závorkou, a builder JPA píše řetěz jako `(a union b) union c` — vada nástroje, otevřená položka |
| `expression` (4096) | přijal 4096, běžel 512 | přijal 512 nebo 1024, běžel 256 | přijal 4096, běžel 2048 | EclipseLink od 1024 výjimka bez zprávy; běh: členy uzávorkované, SQL Server `nested too deeply` v Hibernatu od 1024 a v EclipseLinku od 512; 4096 členů v MyBatisu `server stack limit` |
| `case` (4096) | přijal 512 nebo 1024, běžel 8 | přijal 512 nebo 1024, **běžel 1** | 4096, 8 | Hibernate 1024 výjimka bez zprávy, 2048 javac; běh: CASE vnořený nejvýš desetkrát (Msg 125); **EclipseLink pošle CASE vnořený v ELSE jako `CASE ELSE CASE WHEN … END END`**, bez větví WHEN vnějšího CASE (`Incorrect syntax near the keyword 'ELSE'`) — otevřená položka; ze zdroje EF Core, jehož řetěz `?:` se čte jako jeden CASE, běžel 1024 i v EclipseLinku |
| `not` (4096) | **přijal 128; 256 a 512 `hung`**; 1024 a 2048 výjimka bez zprávy nebo vyčerpaná halda sady, 4096 vždy halda | přijal 256 nebo 512, běžel 256 | 4096, 256 | Hibernate přijímá řetěz negací v čase rostoucím s délkou (16: 5 s, 32: 3,2 min, 64: 2 min, 128: 8,5 min, 256 přes 30 min) — 17 bodů `hung` za 8,5 h — a od 1024 při startu vyčerpá 4,5 GB haldy sady (níž); běh: SQL Server `nested too deeply` od 512 v MyBatisu i v uzávorkovaném řetězu EclipseLinku |
| `properties` (4096) | **přijal 1024, 2048 `not accepted`** | 4096 | 4096 | Hibernate 7.4.5: `Could not instantiate persister org.hibernate.persister.entity.SingleTableEntityPersister (Shop.Wide)`, příčinu zpráva nenese |
| `relations` (256) | 256 | 256 | 256 | nic |
| `key-parts` (64) | 64 | 64 | 64 | nic |
| `unit-size` (64 MB) | přijal 16 MB, 64 MB `refused` | 16 MB, 64 MB `refused` nebo `crashed` | 16 MB, 64 MB `refused` nebo `crashed` | jednotka 64 MB přes HTTP: instance odpoví 413, nebo spojení zavře dřív, než klient tělo doposlal (`header parser received no bytes`) — výchozí mez těla požadavku Kestrelu (30 000 000 bajtů), kterou .NET půlka v procesu neplatí |
| `entities` (800) | 800 | 800 | 800 | nic (ze zdrojů EF Core a Hibernate, které řadu vyslovují) |

Co javové cíle zastavilo dřív než strop řady, jsou tedy meze cílů (javac, přijetí Hibernatem a EclipseLinkem), meze SQL Serveru (plán, zanoření, CASE) a mez těla požadavku — a dvě vady nástroje do EclipseLinku. Překlad sám nespadl na žádném bodě ani tady.

**Sada sama** selhala dvakrát způsobem, který záznam nese:

- V prvním pokusu procházky s fixturou měl forkovaný JVM sady výchozí haldu, čtvrtinu kontejneru (1,5 GB). V 02:08 UTC 2026-10-31 ji Hibernate při startu nad `not-1024` (Dapper → Hibernate) vyčerpal; `OutOfMemoryError` zabil i vlákno selektoru klienta HTTP, a od té chvíle každý bod skončil na straně klienta (`IOException: selector manager closed`), zapsaný jako `crashed` („instance neodpověděla") a následovaný třemi minutami čekání na instanci, která byla celou dobu zdravá (229 MB, odpověď za 6 ms). 91 takových řádků za 5,6 h, všechny v řadě `not`. V 07:46 UTC jsme běh zastavili, ze souboru ve svazku odstranili všech 95 řádků řady `not` (1581 → 1486) a v 07:53 UTC běh spustili znovu od téhož místa.
- Oprava: forkovaný JVM dostal `-XX:MaxRAMPercentage=75 -XX:+ExitOnOutOfMemoryError` (`JavaTests/pom.xml`), takže vyčerpaná halda JVM ukončí, a skript běhu procházku spustí znovu v novém kontejneru; bod, na kterém JVM skončil, je `crashed` (rozh. 195). Tak proběhlo 17 restartů (8 s fixturou, 9 nasucho), každý za šest minut: Hibernate při startu nad řetězem 4096 negací — nasucho i 2048, s fixturou dvakrát 512 — haldu 4,5 GB vyčerpá pokaždé.

**Doby:** procházka s fixturou 20 h včetně 5,6 h nad mrtvým klientem, nasucho 12 h. V obou šla většina času na dvě věci: 512 joinů, které SQL Server počítal 27 min na směr (15 směrů s fixturou, 9 nasucho; 11 h), a body `hung` řady `not` do Hibernatu (17 × 30 min). Řady `case` až `joins` po 256 trvaly nasucho hodinu a osm minut; osy `key-parts`, `nesting`, `properties`, `relations`, `subqueries`, `unions` a `unit-size` dohromady pod hodinu na procházku.

## Třídění

| Zjištění | Kam |
|---|---|
| řetěz dvou a více množinových operací do EclipseLinku: builder JPA uzávorkuje levý operand a parser EclipseLinku dotaz závorkou nezačne | vada, otevřená položka |
| CASE vnořený v ELSE do EclipseLinku: SQL bez WHEN vnějšího CASE | otevřená položka — změřit, zda jde o tvar JPQL nástroje nebo o chybu EclipseLinku 5.0.0, pak rozhodnout |
| Hibernate nad řetězem negací (nadlineární přijetí, od 256 do 30 minut nepřijme, od 1024 halda), entita s 2048 vlastnostmi bez persisteru, řetězy kolem 1024 článků, které Hibernate a EclipseLink odmítnou výjimkou bez zprávy | vyslovené meze cíle v [`subset.md`](../subset.md) |
| uzávorkované řetězy Hibernatu a EclipseLinku a `NOT` v MyBatisu: SQL Server `nested too deeply` od 512–1024 | vyslovená mez v [`subset.md`](../subset.md) |
| jednotka nad výchozí mezí těla požadavku Kestrelu | hrozba 2 v [`threat-model.md`](../threat-model.md); položka o počtu jednotek |
| 512 joinů, 32 `EXISTS`, 64 úrovní poddotazů, CASE nad deset úrovní: SQL Server plán nesestaví nebo dotaz odmítne | mez platformy, ne nástroje; věta textu práce |
| javové cíle se stropem instance vypnutým došly ke stropu každé řady vnoření | řádek „javové instance" záznamu 2026-10-29 je uzavřený |
| halda forkovaného JVM sady a restart procházky po `OutOfMemoryError` | `JavaTests/pom.xml`, [`ORMConvertor/README.md`](../../ORMConvertor/README.md), `architecture.md` §6.2 — hotovo |

## Poznámky

- 725 a 731 testů v posledním kontejneru každé procházky jsou body, které ten kontejner soudil; velikost sady se zde nezapisuje.
- Výstup (`java-tool-rows.tsv`, `series-points.tsv`, `summary.md`) do gitu nejde (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); svazek `ormconvertor_limits` s vypsanými body zůstává pro protokol rozh. [175](../decisions/175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md).
- Souhrn nad oběma půlkami tentokrát vznikl v běhu, ne na hostiteli jako 2026-10-29.
