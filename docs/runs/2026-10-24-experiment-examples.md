# Kontrola experimentálního běhu nad příklady a úprava jejich vstupů

- **Začátek:** 2026-10-24, tři kontroly během jednoho dne (před úpravou, po první a po druhé vlně úprav)
- **Strom:** `0b6c875` před úpravou; po úpravě týž strom s neodevzdanými změnami, které tento záznam popisuje (odevzdány v následujícím commitu)
- **Stroj a prostředí:** MIS2, hostitel mimo zafixované prostředí (SQL Server 2025 Express, `dotnet test` z hosta); jen .NET půlka, druh vstupu `example`, bez katalogu (rozh. [099](../decisions/099-examples-are-content-not-a-choice.md)); čas se neměřil, stav procesoru proto nezaznamenán
- **Co běželo:** `Experiment/ExperimentRunTest` s `ORMCONVERTOR_EXPERIMENT_KINDS=example` (rozh. [173](../decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md)): každý příklad `GET /examples` do tří cílů .NET, artefakt stupni cesty cizího artefaktu do třetího stupně

Je to kontrola, ne záznam verze; zapisuje se podle výjimky v [přehledu](README.md#záznam), protože její zjištění změnilo vstupy experimentu — příklady výkladové stránky a výňatky převzatých projektů —, a záznam verze by jinak stál nad jinými vstupy, než nad kterými vznikla první čísla.

## Výsledek před úpravou

63 řádků (21 příkladů × 3 cíle), z nich 44 do třetího stupně, 12 nezkompilovaných, 1 nepřijatý a 6 odmítnutých. Červené řádky, které nejsou odmítnutím podle rozhodnutí:

| Příklad | Cíl | Stupeň | Co kontrola našla |
|---|---|---|---|
| `dapper-to-efcore`, `efcore-to-nhibernate` | Dapper, EF Core, NHibernate (5 řádků; Dapper do NHibernate je odmítnutí) | 1 | entita `Customer` nese `List<CustomerTransaction>`, třídu `CustomerTransaction` příklad nenese; generované třídy jmenují typ, který nikdo nedeklaruje (`CS0246`) |
| `mybatis-to-dapper` | Dapper, EF Core (2 řádky) | 1 | totéž, navíc statement `findTransactions` vrací `CustomerTransaction` |
| `keycloak-identity` | Dapper, NHibernate (2 řádky) | 1 | typy mimo výňatek `OrganizationEntity`, `ProtocolMapperEntity`; javové typy `Map<,>` a `Integer` ze dvou `@ElementCollection Map<String, …>` opsané do C# |
| `ambari-alerts` | Dapper, EF Core, NHibernate (3 řádky) | 1 | šest výčtů mimo výňatek (`AlertState`, `NotificationState`, `MaintenanceState`, `AlertFirmness`, `Scope`, `SourceType`) a `ClusterEntity` opsané do C# jako typy vlastností |
| `chinook-dapper` | EF Core (1 řádek) | 2 | model EF Core 10.0.10 odmítne: „Unable to determine the relationship represented by navigation 'Album.Artist' of type 'Artist'“ — entity bez klíče (Dapper ho nevysloví) s navigací |

Odmítnutí na stupni 0 podle rozhodnutí, která úprava vstupů neřeší: `dapper-to-efcore`, `mybatis-to-dapper`, `chinook-dapper` a `jpetstore-mybatis` do NHibernate (klíč, který má dodat katalog), `jpetstore-mybatis` do EF Core (holé sloupce joinu bez typu, rozh. [105](../decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md)), `keycloak-identity` do EF Core (kolekční parametr v nativním SQL).

## Proč se vstupy upravily, a jak

Tři příčiny, každá s jinou odpovědí; žádná není chybou nástroje a žádnou neřeší změna nástroje.

**Odkaz na typ mimo vstup** (vzorky, `OrganizationEntity`, `ProtocolMapperEntity`, `ClusterEntity`). Nástroj jméno nezařazeného typu opíše a hlásí `Incompleteness` (rozh. [075](../decisions/075-unknown-language-type-is-a-reported-incompleteness.md)); katalog třídu, kterou nikdo nenahrál, nevyrobí. Odpověď je úplnější vstup: vzorky dostaly entitu `CustomerTransaction` jako další jednotku (`SampleData`, `Examples.cs`), výňatek Keycloaku `ProtocolMapperEntity` z téhož commitu, protože neodkazuje na nic mimo výňatek. `OrganizationEntity` by stáhla domény, poskytovatele identit a realm, a `ClusterEntity` většinu modelu Ambari; asociace na ně se proto čtou jako sloupec, na kterém stojí (`GroupEntity.organization` → `@Column(name = "ORG_ID") String organizationId`, pravidlo 2 rozh. [138](../decisions/138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md)), nebo se vypustí, kde jen zdvojují sloupec (`AlertDefinitionEntity.clusterEntity` nad `cluster_id`, které entita čte jako `clusterId`; pravidlo 1).

**Konstrukce mimo podmnožinu** (výčty, mapy). Nástroj výčet nečte (`@Enumerated` je `Loss`, [`subset.md`](../subset.md)) a `@ElementCollection` je vyňatá oblast (VO 2), přesto vlastnost vyjde do cílové třídy s typem opsaným ze zdroje, který druhý ekosystém nezkompiluje. Rozhodli jsme o vstupech, ne o nástroji: šest výčtových sloupců Ambari je `@Enumerated(EnumType.STRING)`, sloupec tedy nese jméno konstanty a `String` vrací tytéž řádky — přepis podle pravidla 2 rozh. 138, s původním textem nad každým; s tím se vrátily čtyři dotazy, které tyto sloupce srovnávají s parametrem a byly do té doby vypuštěné (parametr teď dostane typ `String`). Kolekce prvků (`Map<String, String>`, `Map<String, Integer>` v Keycloaku, `Set<AlertState>` v Ambari) jsou kolekční tabulky, ne sloupce entity, a nástroj je nepřekládá — vypuštěné podle pravidla 1, každá s poznámkou na svém místě. Zda má nástroj vlastnost vyňaté konstrukce vypisovat vůbec, zůstává položkou v [`open-items.md`](../open-items.md).

**Klíč, který zdroj nevysloví** (Chinook do EF Core; odmítnutí do NHibernate). Dapper a MyBatis klíč nenesou a bez katalogu entity vyjdou `[Keyless]`; navigaci mezi dvěma takovými entitami model EF Core odmítne. Je to chování podle UC1 ([`use-cases.md`](../use-cases.md)) a odpovědí je katalog (rozh. [015](../decisions/015-mapping-fact-completion-from-the-catalog.md)): položka *Schémata převzatých projektů v katalogu instance*. Vstup se nemění a řádek zůstává červený.

## Výsledek po úpravě

Tatáž kontrola nad upravenými vstupy: 63 řádků, 56 do třetího stupně, 6 odmítnutí na stupni 0 (tytéž jako před úpravou) a 1 řádek na druhém stupni (`chinook-dapper` do EF Core, viz výš). Keycloak dává 22 entit a 47 dotazů, Ambari 6 entit a 39 dotazů; všechny tři vzorky projdou do třetího stupně ve všech cílech, které je neodmítnou kvůli klíči. Mezi první a druhou vlnou úprav zůstal jeden červený řádek Ambari ve všech třech cílech: `Set<AlertState>` v `AlertTargetEntity`, který první kontrola nejmenovala, protože kompilátor hlásil typ jen jednou za soubor.

## Poznámky

- Řádky obou kontrol i vygenerované artefakty zůstaly mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); co se změnilo ve vstupech, vypisují README složek `SampleData/ThirdParty/keycloak` a `ambari` a poznámky v souborech.
- Javová půlka tu neběžela (kontrola z hosta bez instance); javové cíle téže změny ověří záznam verze na MIS3.
- `ExampleCatalogTest` a `ThirdPartySourcesTest` prošly před úpravou i po ní; první stupeň červené řádky nikdy neukázal, což je důvod, proč experiment existuje vedle testů (rozh. 173).
