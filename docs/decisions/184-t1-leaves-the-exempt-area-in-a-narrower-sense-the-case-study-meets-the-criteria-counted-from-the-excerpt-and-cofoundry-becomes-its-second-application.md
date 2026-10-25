# 184 — T1 vystupuje z vyňaté oblasti 6 v užším rozsahu: nárokuje se vstup experimentu, ne výsledek nad ním — aplikace případové studie splní čísla zadání, jak ji přečte převod, a drží to test, který je z výňatku spočítá; druhou aplikací bude výňatek Cofoundry nad EF Core

Datum: 2026-10-25
Stav: revidováno
Požadavky: T1, T2, T3
Podklad: zadání ([`requirements.md`](../requirements.md), T1: „Alespoň jedna, lépe dvě open-source aplikace s netriviálním ORM přístupem k datům; každá ≥ 15 entit, vztahy, složený klíč, ≥ 20 read-only dotazů"); schválená [specifikace](../specifikace.tex) (T1: „Projekt připraví alespoň jednu reprezentativní případovou studii nebo testovací sadu s netriviálními entitami, vztahy a read-only dotazy. Rozšíření na více reálných open-source aplikací je předmětem navazující diplomové práce."); rozhodnutí [030](030-scope-of-version-1-0.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [098](098-the-number-is-decided-once-per-release.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [132](132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md), [138](138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [174](174-the-reference-databases-of-the-experiments-are-three-by-role-and-the-case-study-of-t1-is-the-keycloak-excerpt-judged-to-the-third-level.md), [176](176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md) a [183](183-t2-and-t3-leave-the-exempt-area-in-a-narrower-sense-the-build-holds-the-category-matrix-and-the-run-measures-the-metrics-without-promising-numbers.md); [záznam experimentálního běhu z 2026-10-24](../runs/2026-10-24-experiment.md); [`analysis/real-world-sources.md`](../analysis/real-world-sources.md) (katalog kandidátů, doplněný 2026-10-25); [`traceability.md`](../traceability.md), řádek T1; otevřená položka *Nárok T1 po prvním záznamu experimentálního běhu*

## Kontext

**T1 zůstal po rozhodnutí 183 jediným experimentálním požadavkem projektu, o jehož nároku se nerozhodlo.** Rozhodnutí 174 přečetlo T1 v obou zněních — testovací sadou jsou katalogy LDBC a FIBEN, případovou studií výňatek Keycloaku, druhotnými studiemi výňatky Ambari, Chinooku a JPetStore — a nárok odložilo, „dokud záznam běhu v zafixovaném prostředí nedá čísla". Záznam z 2026-10-24 je dal a rozhodnutí 183 z nich vyvodilo vzor: nárokuje se, co sestavení udrží bez rozporu — u T2 matice kategorií, u T3 měřidlo —, čísla záznamu jsou míra verze. Na T1 se vzor nepřenáší přímo, protože T1 netvrdí nic o nástroji. Zadání i specifikace v něm mluví o **vstupech** experimentu: projekt připraví případovou studii, jejíž aplikace má určitou velikost a tvar. Otevřená položka se proto ptala na tři věci: co se u T1 vůbec nárokuje, zda „lépe dvě" aplikace zadání splní druhotné studie, nebo jen Keycloak, a zda odmítnutí Keycloaku v EF Core a čtvrtý stupeň bez ekvivalence nárok zužují, nebo jsou jen hodnotou buňky.

**Čísla, o která T1 jde, dnes stojí jen v textech, a ty se už rozešly s výňatkem.** Rozhodnutí 174 píše o Keycloaku „21 entit … deset složených klíčů, 47 čtecích dotazů", katalog reálných zdrojů „22 entit, 10 složených klíčů", titulek příkladu „22 entities and 47 named queries". Spočítali jsme je z výňatku samého (2026-10-25, způsobem, který níže volíme): 22 entit, 11 složených klíčů, 19 vztahů mezi entitami výňatku a 47 čtecích dotazů; prostý grep souborů dává totéž (22 `@Entity`, 11 `@IdClass`, 47 `@NamedQuery`). Rozdíl nic nemění na tom, že Keycloak kritéria splní, ale ukazuje, co se stane s nárokem, který drží jen text: rozejde se s tím, o čem mluví, a nikdo si toho nevšimne. Týmž způsobem spočítané všechny výňatky:

| Výňatek | Zdroj | Entity | Složené klíče | Vztahy | Čtecí dotazy |
|---|---|---|---|---|---|
| `keycloak-identity` | Hibernate | 22 | 11 | 19 | 47 |
| `chinook-efcore` | EF Core | 11 | 1 | 11 | 35 |
| `ambari-alerts` | EclipseLink | 6 | 0 | 5 | 39 |
| `chinook-dapper` | Dapper | 11 | — | — | 38 |
| `jpetstore-mybatis` | MyBatis | 7 | — | — | 14 |

Dapper a MyBatis klíč ani vztah nevysloví (F6), u nich je dá teprve katalog studie. Čísla zadání tedy splní jen Keycloak; druhotné studie existují kvůli pokrytí zdrojových frameworků (rozh. 174: zdroj v pěti ze šesti), ne kvůli číslům.

**Jediná aplikace, která čísla splní, má zdroj na javové straně.** Případová studie ve smyslu zadání tak měří reálný kód jen ve směrech Java → .NET a Java → Java; směr .NET → Java, který T2 jmenuje jako první a na kterém stojí cíl 2 specifikace, dostává reálný kód jen z Chinooku, ukázkové aplikace o jedenácti tabulkách. Proto jsme 2026-10-25 znovu prošli veřejné projekty s otázkou, zda existuje druhá aplikace, která čísla splní, je pod permisivní licencí (rozh. 138), zveřejňuje schéma pro SQL Server nebo převoditelné do T-SQL a nese dotazy v tvaru, který nástroj čte. Úplný katalog s počty a metodou je v [`analysis/real-world-sources.md`](../analysis/real-world-sources.md); vážní kandidáti:

| Kandidát | Licence | Framework | Výňatek | Entity · složené klíče · čtecí dotazy | Čím by se výňatek upravil | Schéma |
|---|---|---|---|---|---|---|
| [Cofoundry](https://github.com/cofoundry-cms/cofoundry) `d3b30b1` (2026-02-13) | MIT | EF Core 10 | stránky, adresáře, šablony, uživatelé, role, štítky, jazyky, obrázky | 31 · 7 · 60 přečtených bez `Failure`, z nich 19 s vlastním filtrem projektu (**změřeno převodem**) | vlastní filtr projektu (`FilterById`, `FilterActive` …) za `Where`, který obaluje; konstanta schématu v `ToTable` za literál; kontext zúžený na výňatek; zápis ven | instalační skripty T-SQL, jen SQL Server |
| [Apache Ambari](https://github.com/apache/ambari) `a01b6745`, výňatek rozšířený o topologii a stav clusteru | Apache-2.0 | EclipseLink | výstrahy, cluster, služby, komponenty, hostitelé | 19 · 2 · 44 nových (grep) | 16 výčtových polí na `String`; 8 asociací mimo výňatek ven; 7 dotazů po cestě přes N:M, kterou nástroj odmítá; 2 vazby na unikátní klíč | `Ambari-DDL-SQLServer-CREATE.sql` |
| [HAPI FHIR](https://github.com/hapifhir/hapi-fhir) `2970ec4` | Apache-2.0 | Hibernate 7 | terminologie | 13 (15 jen s `ResourceTable`) · 13 · 61 `@Query` | Spring Data `@Query` u všech 61 do `createQuery`; část klíče z `@MappedSuperclass` do každé entity | jen javový migrátor, žádné DDL |
| [allReady](https://github.com/HTBox/allReady) `b3d35a1` (archiv, 2019) | MIT | EF Core 2.0 | celá aplikace | 27 · 9 · ~75 | konfigurace z pomocných metod do konfiguračních tříd; osm navigací na `IdentityUser` | jen migrace EF Core |

Ostatní prověření kandidáti padli na licenci (ThingsBoard je dnes pod BSL), na chybějícím složeném klíči (Dataverse, OpenMeetings, Syncope, jBPM, Oqtane, Raytha) nebo na tom, že dotazy skládá kód za běhu či dědičnost prochází celým modelem (DSpace, SimplCommerce, Piranha, Flowable, Camunda 7, OpenOLAT). **Aplikace nad NHibernatem, která by čísla splnila, neexistuje pod permisivní licencí**: jediná nalezená s `hbm.xml` (Cuyahoga, BSD-3) nemá složený klíč; zdroj NHibernate tedy v případové studii chybí dál, jak říkalo rozh. 138.

**Je třeba rozhodnout**, co T1 nárokuje, čím se to drží, zda případová studie dostane druhou aplikaci a co z výsledků nad studií nárok zužuje.

## Zvažované varianty

### Co se nárokuje

**1 — T1 zůstane vyňatý: je to vlastnost vstupů, ne nástroje, a nárok se týká nástroje.** Zamítáme. Vyňaté podle rozh. 030 znamená, že verze za tvrzení neručí — u T1 by to říkalo, že projekt případovou studii nedodává, ač ji repozitář nese, experimentální běh ji měří a záznam ji vykazuje. Vlastnost vstupu se přitom dá udržet bez běhu a bez databáze líp než většina vlastností nástroje: stačí ji spočítat. Že ji dnes nikdo nepočítá, má měřitelný následek — dvě různá čísla ve dvou textech, obě jiná než výňatek.

**2 — Nárokovat výsledek nad případovou studií**, například „studie dojde do čtvrtého stupně v každém cíli, který ji neodmítne podle rozhodnutí". Zamítáme z důvodů variant 2 a 3 rozh. 183. Je to míra běhu na vyžádání nad databází, kterou sestavení nespouští; výčet výjimek „podle rozhodnutí" by se měnil s každou verzí (Keycloak do EF Core, JPetStore do čtyř cílů); a reálný kód je v experimentu právě proto, aby našel, co matice nenajde (rozh. 174: „čísla … vznikají o aplikacích") — nárok nad jeho výsledky by z každého nálezu udělal červené sestavení.

**3 — Nárokovat čísla zadání nad všemi pěti výňatky.** Zamítáme. Dnes to neplatí (tabulka výš) a platit by to začalo jen rozšířením čtyř výňatků daleko za účel, kvůli kterému v repozitáři jsou: druhotné studie nesou zdrojové frameworky, ne velikost.

**4 — Nárokovat vstup: aplikace případové studie splní čísla zadání, jak výňatek přečte převod, a drží to test, který je z výňatku spočítá; testovací sadou jsou oba katalogy; co nástroj nad studií dokáže, je míra záznamu jako u katalogů.** Volíme, níž.

### Druhá aplikace

**a — Žádná: „lépe" je přání a specifikace víc reálných aplikací odsouvá do navazující práce.** Zamítáme, i když je to přípustné čtení. Specifikace odsouvá *rozšíření na více reálných aplikací* — studii nad řadou projektů —, ne druhou aplikaci, kterou zadání výslovně jmenuje. Hlavní důvod je ale věcný: s jedinou aplikací na javové straně měří případová studie reálný kód jen ze zdroje v Javě, a tvrzení práce o použitelnosti mezireprezentace napříč ekosystémy by pro směr .NET → Java stálo na ukázkové aplikaci o jedenácti tabulkách.

**b — Rozšířit výňatek Ambari na devatenáct entit.** Zamítáme jako druhou aplikaci T1. Splnil by čísla, ale přidal by druhou aplikaci na tutéž stranu jako Keycloak a směr .NET → Java by zůstal bez ní. Úpravy jsou navíc jiného druhu než u ostatních výňatků: šestnáct výčtových polí přepsaných na řetězce a osm asociací vypuštěných, protože vedou mimo výňatek, a sedm z 44 nových dotazů jde po cestě přes N:M, kterou nástroj odmítá — výňatek by víc ukazoval meze nástroje než aplikaci. Počty máme jen z grepu, převodem změřené nejsou. Varianta zůstává v katalogu jako cesta, kdyby Cofoundry neobstálo.

**c — HAPI FHIR nebo allReady.** Zamítáme. HAPI FHIR by potřeboval přepis všech 61 dotazů ze Spring Data `@Query`, část klíče z mapované nadtřídy v každé entitě a schéma, které vzniká jen spuštěním javového migrátoru. allReady má nejčistší dotazy, ale je archivovaný od roku 2019 nad EF Core 2.0 a schéma nese jen v migracích; zůstává zálohou za Cofoundry.

**d — Cofoundry nad EF Core.** Volíme, níž.

## Rozhodnutí

**Volíme variantu 4 a d. T1 vystupuje z vyňaté oblasti 6 se stavem *nárokované v užším rozsahu*. Nárokuje se vstup experimentu: každá aplikace případové studie — výňatek open-source aplikace pod permisivní licencí se schématem svých tabulek (rozh. 138, 176) — má nejméně 15 entit, vztahy mezi nimi, složený klíč a nejméně 20 čtecích dotazů, jak výňatek přečte převod ze zdrojového frameworku, a drží to test sestavení, který čísla spočítá z výňatku samého; testovací sadou ve smyslu specifikace jsou katalogy LDBC a FIBEN nad svými databázemi se soudci (rozh. 174). Případovou studii měří experimentální běh týmž měřidlem jako všechno ostatní (T3, rozh. 183) a co nástroj nad ní dokáže, je míra záznamu verze, ne nárok. Aplikacemi jsou výňatek Keycloaku (Hibernate) a výňatek Cofoundry (EF Core), po jedné na každé straně hranice ekosystémů. Druhotné studie čísla nesplní a nárok o nich nemluví.**

### Co se tvrdí a čím se to drží

| Co se tvrdí | Čím se to drží |
|---|---|
| aplikace případové studie má ≥ 15 entit, vztahy, složený klíč a ≥ 20 čtecích dotazů, jak ji přečte převod | `Combined/CaseStudyCriteriaTest` (nový) |
| je to open-source projekt pod permisivní licencí, každý soubor jmenuje původ a změnu | `Combined/ThirdPartySourcesTest` |
| nese schéma svých tabulek, které obě sady vytvoří v databázi případových studií | `Combined/CaseStudySchemaTest`; `Experiment/ExperimentPathTest` |
| vstupuje do experimentálního běhu jako případová studie a projekt studie má čtvrtý stupeň | `Experiment/ExperimentPathTest` |
| testovací sada: katalogy nesou celý benchmark — dotazy LDBC Interactive a BI, dotazy a tabulky FIBEN | `Combined/LdbcCatalogTest`, `Combined/FibenCatalogTest` |

**Proč počítat tak, jak výňatek přečte převod.** Kritérium má platit pro to, co experiment měří, a experiment čte výňatek parserem zdrojového frameworku: třída, kterou parser za entitu nevezme, nebo dotaz, který nepřečte, do studie nevstoupí, ať o něm text projektu říká cokoli. Test proto projde kroky `ConversionHandler.Convert` ze zdrojového frameworku do něj samého bez katalogu — parsery entit, fázi katalogu bez čtečky (rozpuštění klíčových tříd, vyřazení konstrukcí vyňaté oblasti 2), sestavení, které konvencí doplní, co dotazy potřebují, a parsery dotazů — a počítá: entitu jako mapu, která po fázi katalogu zbude; vztah jako vlastnící stranu mezi dvěma entitami výňatku; čtecí dotaz jako dotaz přečtený bez záznamu `Failure`, takže se nezapočte ani příkaz, který zapisuje (rozh. 053), ani dotaz, který nástroj odmítne přečíst. Počítat grepem anotací by znamenalo šest pravidel pro šest frameworků, která opakují parsery a počítají text místo vstupu; počítat schéma by bylo neutrální, ale měřilo by databázi, ne vrstvu ORM, o které zadání mluví, a dotazy by nespočítalo vůbec. Že se počet převodem a grepem u Keycloaku shoduje, je změřená shoda z 2026-10-25, ne předpoklad.

**Test tvrdí prahy zadání, ne počty.** Počet entit nebo dotazů výňatku je přesně ten údaj, který by každé rozšíření výňatku muselo opravit; práh se nemění a počty jde vypsat do výstupu testu. Číslo zadání tak drží sestavení a žádný text je neopakuje.

### Cofoundry jako druhá aplikace

Cofoundry je CMS nad ASP.NET Core a EF Core 10 — týž hlavní verzí, jakou nástroj zafixoval —, pod licencí MIT, jen pro SQL Server, se schématem v instalačních skriptech T-SQL. Entity nemají bázové třídy, jen rozhraní; konfiguraci nesou třídy `IEntityTypeConfiguration<T>`, převážně v podmnožině fluent API, kterou nástroj čte (rozh. 132) — co je mimo ni (`IsUtc`, `IsCharType` …), vypustí se záznamem —, a dotazy píší obslužné třídy přímo nad `DbSet` kontextu. Převod nezměněných souborů oblasti stránek, adresářů, šablon, uživatelů, rolí, štítků, jazyků a obrázků (2026-10-25, zdroj EF Core, bez katalogu) dal 31 entit s třídou ve výňatku, 7 složených klíčů (`PageTag`, `PageGroupItem`, `PageDirectoryClosure`, `PagePublishStatusQuery`, `RolePermission`, `ImageAssetTag`, `ImageAssetGroupItem`) a 82 přečtených dotazů, z nich 60 bez `Failure`. Ze šedesáti jich 19 volá vlastní filtr projektu — rozšiřující metodu nad `IQueryable`, jejíž tělo je jeden `Where` —, který parser jako neznámý krok vypustí se záznamem `Loss` (vyslovená mez, [`subset.md`](../subset.md) 2.12, rozh. 070), takže by vydal dotaz bez filtru; i bez nich zbývá kolem čtyřiceti dotazů bez vlastního filtru, dvojnásobek prahu. Zbylých 22 odmítne podle vyslovených mezí — koncový `Any` a `Count`, dotaz skládaný za běhu, cesta přes dvě asociace —; výňatek je podle rozh. 138 vynechá s poznámkou, jako Chinook vynechal své otázky ano/ne, protože příklad výkladové stránky se ve směru, který ukazuje, převede bez `Failure` (`Combined/ExampleCatalogTest`).

Výňatek se řídí rozh. 138 a 176 jako ostatní: složka `cofoundry/` s licencí, README se seznamem změn a poznámkou na začátku každého souboru, příklad s klíčem `cofoundry-pages`, schéma tabulek výňatku vzaté z databáze, kterou postaví instalační skripty projektu. Žádná úprava nemění význam: vlastní filtr projektu se nahradí `Where`, který obaluje, protože nástroj tělo cizí metody nevidí; kontext ponechá jen `DbSet` entit výňatku; navigace k entitám mimo výňatek a výčtový typ mimo něj odpadnou; zápis odpadne. Konstanta schématu v `ToTable` zůstane: nástroj z `nameof` přečte jméno tabulky a schéma vypustí se záznamem, a to je, co databáze případových studií se schématem na projekt potřebuje.

### Zúžení T1

- **Výňatek, ne celá aplikace.** Nárok mluví o oblasti datové vrstvy, převzaté z jednoho commitu se změnami vyznačenými podle rozh. 138 (zápis ven, srovnání s celou entitou na srovnání klíče, vlastní filtr na `Where` …); o zbytku aplikace neříká nic.
- **Co nástroj nad studií dokáže, je míra.** Odmítnutí Keycloaku do EF Core (kolekční parametr v nativním SQL EF Core, vyslovená mez rozh. 113) je hodnota buňky, stejně jako odmítnutí JPetStore; nárok T1 nevyvrací. Odmítnutí, které by bylo vadou, je vadou F-požadavku a opravuje se přednostně jako každá jiná.
- **Bez ekvivalence.** Čtvrtý stupeň studie je jen *spustitelný* nad prázdnými tabulkami jejího schématu (rozh. 176); funkční ekvivalence je měřená nad testovací sadou (matice, FIBEN). To je zúžení, ne hodnota buňky, protože je to vlastnost měření, která se s verzí nemění: data projekty nezveřejňují.
- **Druhotné studie nejsou aplikacemi T1.** Ambari, oba výňatky Chinooku a JPetStore jsou v tabulce T2 × T3 mírou jako katalogy; čísla zadání nesplní a nárok o nich nemluví.
- **Bez NHibernatu.** Žádná aplikace pod permisivní licencí, která by čísla splnila, zdroj NHibernate nemá; případová studie má zdroj v pěti ze šesti frameworků.

## Důsledky

- **§9 a sekce *Guarantees* kořenového README:** řádek T1 se odděluje od T4–T7 se stavem *nárokované v užším rozsahu*, s tím, co se tvrdí, a se zúženími výš; oblast 6 drží T4–T7. Podle rozh. 098 je oblast vstupující do záruk schopnost, tedy MINOR; nese ji příští vydání spolu s T2 a T3 (rozh. 183).
- **[`traceability.md`](../traceability.md):** řádek T1 dostává stav, důkaz a zúžení; úkol záměru 5 přestává jmenovat T1 mezi vyňatými.
- **Kód:** test `Combined/CaseStudyCriteriaTest` s aplikacemi `keycloak-identity` a `cofoundry-pages`; výňatek Cofoundry v `SampleData/ThirdParty/cofoundry/` s příkladem `cofoundry-pages`, projektem v `CASE_STUDIES` javové sady a schématem v databázi případových studií.
- **[`open-items.md`](../open-items.md):** položka rozhodnutí *Nárok T1 po prvním záznamu experimentálního běhu* mizí; co výňatek Cofoundry našel v nástroji, se třídí jako u ostatních výňatků — kořen EF Core jménem sady, které není jménem entity s „s", je položka vady.
- **[`analysis/real-world-sources.md`](../analysis/real-world-sources.md):** katalog dostává kandidáty z 2026-10-25 a opravené počty Keycloaku a Ambari.
- **Co toto rozhodnutí neurčuje:** kterým směrem výkladová stránka příklad Cofoundry ukáže; zda se výňatek Ambari rozšíří jako obsah příkladu (jako aplikace T1 ne); T4–T6 (navazující práce, rozh. 175) a T7 (Advisor, rozh. 171).

## Historie

**2026-10-25 — revidováno při převzetí výňatku Cofoundry.** Volba se nemění; opravena jsou dvě tvrzení o výňatku, která převzetí vyvrátilo, a text mluví o dvou aplikacích místo o stavu, kdy výňatek ještě nestál. Konstanta schématu v `ToTable` zůstává, jak ji projekt napsal: nástroj z `nameof` přečte jméno tabulky a vypustí jen schéma, což databáze případových studií se schématem na projekt potřebuje — původní znění chtělo konstantu nahradit literálem. Dotazy, které nástroj odmítne podle vyslovených mezí, výňatek vynechá s poznámkou, protože příklad se ve směru, který ukazuje, převede bez `Failure` — původní znění je chtělo ve výňatku nechat. Z důsledků vypadla práce *Výňatek Cofoundry jako druhá aplikace případové studie*, kterou převzetí uzavřelo, a přibyla položka vady, kterou výňatek našel.
