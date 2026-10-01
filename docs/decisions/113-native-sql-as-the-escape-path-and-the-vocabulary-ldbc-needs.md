# 113 — Co dotazový jazyk cíle nevysloví, napíše cíl celé nativním SQL svého dialektu; slovník nese rekurzi, seskupení podle výrazu, okenní funkce, agregaci do seznamu a funkce, které potřebuje katalog LDBC

Datum: 2026-10-01
Stav: platí
Požadavky: F7–F11, F13, T1, T2, T3, S1, S2, S6
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [027](027-query-artifact-verification.md), [028](028-assembly-name-is-not-ours-to-invent.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [062](062-hql-read-by-a-hand-written-parser.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [069](069-major-marks-a-milestone-not-a-break.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [086](086-target-database-dialect-declared-by-the-descriptor.md), [088](088-a-declared-foreign-source-dialect-is-not-read.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [103](103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md), [106](106-a-bare-parameter-after-in-is-dappers-collection-parameter.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [109](109-a-code-unit-carries-every-query-it-hands-over.md), [110](110-ldbc-snb-as-a-second-reference-domain.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md) a [112](112-a-query-as-a-row-source-is-a-named-intermediate-result.md); JSS §5.4 („Non-core SQL constructs") a pravidla Q13, Q14 a Q15; specifikace LDBC SNB 2.2.4 a poznámky katalogu `SampleData/LdbcSnbSample.cs`; dokumentace SQL Serveru 2022 k `WITH common_table_expression` (omezení rekurzivního členu); otevřené položky „Rekurzivní `WITH`", „Dotaz předaný v jiném jazyce, než jaký parser jednotky čte, mizí vedle přečteného beze slova", „Seskupení podle výrazu", „Funkce mimo slovník výrazů: datumová aritmetika, zaokrouhlení a převod typu", „Okenní funkce" a „Vnější join s filtrem v `ON` a počet přes celý výsledek EF Core odmítá, ačkoli je LINQ vysloví"

## Kontext

**Cíl z 2026-10-01 je dokončit dotazy, jak daleko to jde, a měřítkem je katalog LDBC** (rozhodnutí 110). Po rozhodnutí 112 v něm stojí 30 ze 41 čtecích dotazů jako přeložené podle specifikace, 5 jako zjednodušené a 6 jako nepřeložené; z těch třiceti ale osm některý cíl odmítá, takže do všech pěti cílů podle specifikace jde dnes 22 dotazů. Poznámky katalogu říkají u každého dotazu, co mu chybí, a dohromady je to krátký seznam:

| Dotaz | Dnes | Co mu chybí |
|---|---|---|
| IC 13 | nepřeložený | rekurze — nejkratší cesta po `knows` |
| BI 15, BI 20 | nepřeložené | rekurze s cenou cesty |
| BI 19 | nepřeložený | rekurze s cenou cesty, `ROUND` a `SQRT` |
| IC 14 | nepřeložený | rekurze, váha hrany jako mezivýsledek, cesta jako text (převod čísla na řetězec) |
| IC 12 | zjednodušený | rekurze — hierarchie tříd tagů, dnes šest vnějších joinů —, jména tagů jako seznam |
| BI 1 | nepřeložený | seskupení podle výrazu |
| IC 7, BI 17 | zjednodušené | datumová aritmetika — latence v minutách, odstup v hodinách |
| BI 14 | zjednodušený | okenní funkce — nejlepší dvojice každého města |
| IC 1 | zjednodušený | výsledek, který je seznamem — e-maily, jazyky, školy, firmy |
| IC 5, IC 10, BI 2, BI 8, BI 12, BI 13 | přeložené, EF Core odmítá | filtr v `ON` vnějšího joinu |
| BI 11 | přeložený, EF Core odmítá | agregát přes celý výsledek bez seskupení |
| BI 4, BI 8, BI 12, BI 13 | přeložené, NHibernate a EclipseLink odmítají | mezivýsledek (rozhodnutí 112) |

**Za tabulkou stojí šest otevřených položek a všechny kladou tutéž otázku.** Rozhodnutí 112 o nativním SQL pro NHibernate a EclipseLink řeklo, že „je otázka položky o rekurzi, kde se týká tří cílů ze šesti, a odpověď musí být pro obě položky táž". Položka o funkcích mimo slovník se ptá, jestli slovník nese funkci, kterou část cílů nevyjádří — „tutéž otázku jako u rekurze, a odpověď by měla být táž". Položka o okenních funkcích se ptá na totéž „s otázkou cílů bez nich jako u rekurze". Položka o dotazu předaném v jiném jazyce je zrcadlem: píše-li nativní SQL cíl, musí ho zdroj umět číst, má-li vlastní výstup nástroje projít zpátky. A položka o EF Core se ptá, jestli dva tvary, které LINQ vysloví, builder píše, nebo odmítá. Rozhodovat šestkrát zvlášť by znamenalo riskovat šest odpovědí na jednu otázku — **co cíl udělá s konstrukcí, kterou jeho dotazový jazyk nemá** —, a proto je rozhodnutí jedno.

**Odpověď na tu otázku přitom už jednou padla, jen se nikdy nenaplnila.** Rozhodnutí 022 zvolilo, že builder píše nativní dotazový tvar svého frameworku a „únikovou cestu na syrové SQL použije jen tam, kde cíl instrukci nativně vyjádřit neumí, a takový případ je vždy záznamem, nikdy tichou náhradou". Únikovou cestu ale nikdo nenapsal. Dokud slovník nesl jen to, co vysloví všechny cíle — měřítko rozhodnutí 107 —, nebylo jí potřeba, a kde se cíle rozešly, odpověděla pravidla 053 a 065 odmítnutím: NHibernate odmítá plný vnější join a množinové operace, NHibernate a EclipseLink mezivýsledek, EF Core agregát bez seskupení a join mimo rovnosti, HQL i JPQL stránkování uvnitř poddotazu. To bylo správné, dokud odmítnutí stála na okrajích. Teď má slovník růst o konstrukce, které polovina cílů nemá vůbec — rekurzi nevysloví LINQ EF Core, HQL NHibernatu ani JPQL EclipseLinku —, a s odmítnutím jako jedinou odpovědí by katalog měřil hlavně naše odmítnutí.

**Článek tu pozici popisuje dvěma větami.** §5.4 řadí *common table expressions*, okenní funkce a „vendor-specific operators" mezi konstrukce mimo základní abstrakci a dodává, že reprezentace je navržená tak, aby se dala rozšířit. Pravidlo Q14 říká, že nepodporuje-li cílový ORM typ instrukce, „translation may rewrite, decompose, or partially evaluate the query". Obojí míří stejným směrem: slovník smí růst o to, co nemají všechny cíle, a cíl, který to nemá, s tím má něco udělat, ne to jen odmítnout.

**Rozhodnout je třeba tři věci.** *Co udělá cíl* s konstrukcí, kterou jeho dotazový jazyk nevysloví. *O co slovník přibude* — rekurze, seskupení podle výrazu, funkce, okenní funkce, agregace do seznamu — a u každé konstrukce, kdo ji vysloví a kdo ji čte. A *co zbude* z obou tvarů EF Core a z dotazu předaného v jiném jazyce.

## Zvažované varianty

### 1 — Rozšířit slovník a v cílech bez konstrukce odmítat

Model by nesl rekurzi, okenní funkce i nové funkce a cíl, jehož jazyk konstrukci nemá, by dotaz odmítl podle 053, jako dnes odmítá mezivýsledek. Je to nejlevnější rozšíření a nic na něm není nepoctivé. Zamítáme. Rekurzivní dotaz by se překládal do dvou nebo tří cílů z pěti a matice T2 i katalog LDBC by v ostatních buňkách měřily mezeru dotazového jazyka cíle, kterou každý uživatel toho frameworku zavírá sám a vždy stejně — nativním dotazem. Rozhodnutí 022 říká, že nástroj má vyrobit „dotaz, jaký by v cílovém frameworku napsal člověk", a člověk, který v NHibernatu potřebuje rekurzivní CTE, napíše `CreateSQLQuery`, ne nic. Odmítnout tam, kde framework odpověď má, je táž vada, kterou rozhodnutí 065 vytklo odmítnutí plného vnějšího joinu v EF Core: vyměnit překlad za žádný tam, kde je správný k mání.

### 2 — Nativní SQL do všech cílů pro všechno, co slovník nově nese

Dotaz s novou konstrukcí by šel do všech cílů jako SQL, ať jejich jazyk konstrukci má, nebo ne. Zamítáme z důvodů, které rozhodnutí 022 vedlo proti syrovému SQL obecně: HQL Hibernate 7.4 rekurzi, okenní funkce i agregaci do seznamu podle dokumentace vysloví a SQL by tam zahodilo strukturovaný tvar, kvůli kterému se ORM vybírá — matice by místo překladu měřila obaly kolem jednoho textu.

### 3 — Úniková cesta po kouscích uvnitř dotazového jazyka

Dotazový jazyk by zůstal a nevyslovitelný kousek by se do něj vložil jako SQL: `FUNCTION('DATEDIFF', …)` nebo `SQL(…)` u EclipseLinku, nezaregistrovaná funkce, kterou HQL NHibernatu propustí do SQL, a podobně. Zamítáme třemi důvody. **Nesahá to na hlavní případy:** rekurzi ani mezivýsledek žádný z těch mechanismů nevyjádří, protože to nejsou výrazy, takže by vedle stála celá úniková cesta stejně. **Každý framework má jiný mechanismus s jiným dosahem:** kousek, který EclipseLink propustí, NHibernate propustit nemusí, a argument, který je v T-SQL klíčovým slovem (`DATEDIFF(minute, …)`), nemá ve `FUNCTION()` tvar vůbec. **A směs se nedá ověřit:** kompilace plánu frameworku (3. stupeň) kousek SQL neposoudí a ověření T-SQL zase neposoudí dotazový jazyk kolem něj. Od toho je třeba odlišit funkci, kterou dotazový jazyk cíle vysloví sám, byť jen pro jeden dialekt: `EF.Functions.DateDiffMinute` je LINQ, který provider SQL Serveru přeloží, tedy totéž co `Math.Abs`, a deklarovaný dialekt (086) je přesně to, co ho opravňuje.

### 4 — Slovník roste o to, co katalog potřebuje; co dotazový jazyk cíle nevysloví, napíše cíl celé nativním SQL svého dialektu, se záznamem

## Rozhodnutí

**Volíme variantu 4 a naplňujeme jí únikovou cestu, kterou rozhodnutí 022 zvolilo a nikdo nenapsal. Cíl píše dotaz svým dotazovým jazykem, kdykoli ho ten jazyk vysloví. Nevysloví-li některou konstrukci dotazu, napíše cíl celý dotaz nativním SQL svého deklarovaného dialektu — týmž sdíleným zapisovačem T-SQL, jakým píše Dapper —, předá ho API svého frameworku pro nativní dotazy a vydá záznam nového druhu `Fallback`, který konstrukci a dialekt jmenuje. Odmítnutí podle 053 zůstává tam, kde dotaz není platný, kde narazí na mez modelu, nebo kde tvar neunese ani nativní API cíle. Na tom slovník roste o pět skupin konstrukcí: rekurzivní definici, seskupení podle výrazu, funkce (datumová aritmetika, zaokrouhlení, odmocnina, převod typu), okenní funkce řazení a agregaci do seznamu. EF Core píše vnější join s filtrem v `ON` a join mimo rovnosti v LINQ, a nativní SQL předané v kódu čtou parsery sdílenou čtečkou T-SQL.**

### Úniková cesta

**Kdy.** O únikové cestě rozhoduje šablona builderu, na jednom místě pro všechny cíle (rozhodnutí 023), po `Normalize()`. Konstrukci dotazu cíl nevysloví ze dvou důvodů a oba se hlásí týmž kanálem. **Deklarací:** kategorie `QuerySupport` vedená jako `NotExpressible`, funkce mimo `Functions` a nové kategorie níž — mechanická kontrola Q14. **V místě emise:** visitor nebo krok builderu narazí na tvar, který jeho jazyk nemá, ač kategorii jako celek vyslovuje — plný vnější join v HQL NHibernatu, stránkování uvnitř poddotazu v HQL i JPQL, agregát bez seskupení a množinová operace nad různými typy prvků v LINQ. Místo emise dnes v takovém případě hlásí `Failure`; nově hlásí, že tvar *jazyk cíle nemá*, a šablona z toho udělá únikovou cestu, má-li ji cíl, a `Failure` jako dosud, nemá-li ji. **Odmítnutí, která o jazyku cíle nejsou, zůstávají odmítnutími:** laterální odkaz, nepojmenovaný sloupec definice, parametr bez skaláru, agregát nad agregátem, pravidla rekurze níž. Tam nestojí dotaz, který by šlo napsat jinak, ale dotaz, který model nenese nebo který by odmítl sám SQL Server.

**Co.** Celý dotaz — definice, tělo, parametry —, nikdy kousek (varianta 3). Text skládá sdílený zapisovač T-SQL (rozhodnutí 082) nad týmiž normalizovanými instrukcemi, nad jakými by psal cíl sám; wrapper ho drží jako druhý builder a jeho výstup obalí API svého frameworku. Je to týž text, jaký z dotazu napíše cíl Dapper, a to je podstatné: zapisovač nejširšího jazyka slovníku je ověřený na všech čtyřech stupních a ve všech směrech, takže úniková cesta nepřidává nový zápis, jen nový obal.

| Cíl | Nativní API | Parametry | Řádek |
|---|---|---|---|
| EF Core 10 | `ctx.Database.SqlQuery<TRow>($"…")`; celá entita `ctx.Set<T>().FromSql($"…")` | interpolace `{x}` | `TRow` je třída vygenerovaná vedle metody: vlastnost za každou projekci, pojmenovaná aliasem, typovaná bránou |
| NHibernate 5.7 | `session.CreateSQLQuery(@"…")` | `:x`, `SetParameter` a `SetParameterList` | `AddScalar` za každou projekci s typem z brány; celá entita `AddEntity` |
| Hibernate 7.4, EclipseLink 5.0 | `em.createNativeQuery("""…""")`; celá entita s třídou entity | poziční `?1` | řádek jako `Object[]`, jednosloupcový jako hodnota; celá entita jako entita |

**Signatura metody se nemění v tom, na čem stojí ověření a Advisor:** EF Core vrací `IQueryable` s `DbContext` jako prvním parametrem, NHibernate `IQuery` (`ISQLQuery` je jeho potomek), JPA dotazový objekt nad `EntityManager` — `Query`, u celé entity `TypedQuery` tam, kde ho `createNativeQuery` s třídou entity vrací (*ověřit proti Jakarta Persistence 3.2; nevrací-li ho, vrací metoda `Query`*). Harness 2. a 3. stupně, diferenční běh i reflexe Advisoru tak vidí týž tvar jako u nativního překladu. Námitka rozhodnutí 022, že úniková cesta EF Core musí vracet sloupce mapované entity, padla s EF Core 8, které `SqlQuery` otevřelo nemapovaným typům; jméno třídy řádku se odvozuje ze jména metody, jako se odvozuje jméno metody samo. *Ověřit při implementaci proti EF Core 10.0.10, že `SqlQuery` přijme text začínající `WITH`, dokud se nad ním nic neskládá, a že ho `ToQueryString()` vydá beze změny.*

**Záznam je nového druhu, `Fallback`.** Rozhodnutí 010 váže každý druh záznamu na jednu definovanou událost a úniková cesta je událost nová: není to `Loss`, protože se nic ze zdroje nezahazuje a řádky jsou tytéž, ani `Convention`, protože nic nedoplnila konvence. Záznam jmenuje konstrukci, kvůli které se ustoupilo, a dialekt, ke kterému se tím artefakt váže, a jde do záznamu běhu (S6).

**Kde úniková cesta není.** Dapper a MyBatis ji nemají, protože jejich dotazový jazyk *je* SQL dialektu; deskriptor to říká vlastností, kterou zbylé čtyři cíle uvádějí kladně. A tam, kde tvar neunese nativní API cíle, je odmítnutí dál `Failure` jmenovitě. Dnes je známý jeden takový tvar, kolekční parametr: NHibernate a Hibernate ho v nativním dotazu rozvinou, interpolace EF Core ne, a u EclipseLinku je to třeba ověřit.

**Proč to není varianta 2 v jiných šatech.** Úniková cesta se spustí jen tam, kde jazyk cíle konstrukci nemá, a měření ji počítá zvlášť: buňka matice T2 i katalogu LDBC má tři hodnoty — přeloženo dotazovým jazykem cíle, přeloženo nativním SQL (záznam `Fallback`), odmítnuto — a druhá se za první nikdy nevydává. Katalog jmenuje u každého dotazu cíle, které ustupují, tak jako dnes jmenuje cíle, které odmítají, a drží to `LdbcCatalogTest`. Osa, kterou rozhodnutí 022 hájilo, tím zůstává a přibývá na ní hodnota.

**Ověření.** 1. a 2. stupeň jako u každého artefaktu. Na 3. stupni frameworky nativní SQL před provedením neposuzují — `CreateSQLQuery` a `createNativeQuery` ho jen uloží, `ToQueryString()` ho jen vypíše —, takže 3. stupněm artefaktu z únikové cesty je ověření T-SQL cíle Dapper (`TSqlAcceptance`: text se rozparsuje a tabulky i sloupce se najdou v mezireprezentaci). Je to tatáž věta, kterou `architecture.md` §6.2 říká o Dapperu: splynutí stupňů je konstatování o frameworku. Rozhoduje 4. stupeň (rozhodnutí 089): artefakt z únikové cesty se spustí a srovná se zdrojem jako kterýkoli jiný.

**Dialekt.** Artefakt z únikové cesty je vázaný na deklarovaný dialekt (086), dnes SQL Server 2022 u všech šesti cílů, a záznam to říká. Druhý dialekt by k tomu potřeboval vlastní zapisovač; položka o něm tím dostává další důvod.

### Rekurzivní definice

**Tvar.** Žádná nová instrukce: rekurzivní definice je definice rozhodnutí 112, jejíž tělo je množinová operace `UNION ALL` kotevního a rekurzivního členu, přičemž rekurzivní člen jmenuje samu definici. Jméno, místo na úrovni dotazu i popis řádku má definice od 112; nové je jen to, že tělo smí jmenovat sebe. **Limit rekurze je fakt dotazu:** `OPTION (MAXRECURSION n)` rozhoduje, jestli dotaz doběhne, nebo skončí chybou po sté úrovni, takže ho dotaz nese jako číslo — nula znamená bez limitu, chybějící hodnota výchozí limit dialektu — a cíl, který ho nevysloví, ustoupí, uvádí-li ho zdroj.

**Pravidla drží brána šablony pro všechny cíle**, protože jsou to pravidla dialektu, ke kterému všechny cíle ukazují (086), a model sám nevaliduje: mezi kotevním a rekurzivním členem jen `UNION ALL`; kotva sebe nejmenuje; rekurzivní člen jmenuje definici právě jednou, ve `FROM` nebo ve vnitřním joinu, ne v poddotazu ani ve vnějším joinu; v rekurzivním členu není `DISTINCT`, seskupení, `HAVING`, agregát, výřez, vnější join ani poddotaz; dvě definice, které se jmenují navzájem, se odmítají; a skalár každého sloupce rekurzivního členu se shoduje se skalárem kotvy — SQL Server odmítá i rozdíl v délce řetězce, a proto se cesta skládaná jako text píše převodem na `NVARCHAR(MAX)` v obou členech. Co brána odmítne, je `Failure` kategorie `IntermediateResult` jmenovitě, ne úniková cesta: takový dotaz nespustí ani SQL Server.

**Ukončení je text dotazu.** Hlídání hloubky je obyčejná podmínka nad sloupcem definice (107), hlídání cyklu podmínka nad cestou složenou konkatenací a převodem typu (níž). Klauzule `search` a `cycle` HQL Hibernatu slovník nenese — SQL Server je nemá a zapisovač T-SQL by je musel emulovat —, takže je čtečka HQL dál odmítá jmenovitě.

**Kdo píše.** T-SQL (Dapper, MyBatis): `WITH x AS (… UNION ALL …)` a s limitem `OPTION (MAXRECURSION n)` na konci příkazu. Hibernate: `with x as (… union all …)` podle dokumentace verze 7.4 — *ověřit sondou při implementaci; neobstojí-li, deskriptor Hibernatu rekurzi neuvede a Hibernate ustoupí*. Rozhodnutí platí v obou případech, protože o tom, kdo rekurzi vysloví, rozhoduje deklarace, ne tenhle text. HQL limit rekurze nemá, takže dotaz s limitem ustoupí i v Hibernatu. EF Core, NHibernate a EclipseLink ustoupí vždy.

**Kdo čte.** Čtečka T-SQL rekurzivní `WITH` a `OPTION (MAXRECURSION n)`; HQL Hibernatu rekurzivní `with` bez `search` a `cycle`. LINQ rekurzi nemá a dostane ji jedině jako nativní SQL předané v kódu (níž).

**Data čtvrtého stupně.** Sdílené testovací schéma (`Tests/Database/TestSchema.sql`, obě sady) dostane hierarchii, která odkazuje sama na sebe a je hlubší než dvě úrovně, aby ji nenahradil pevný počet joinů, a graf s cyklem, aby chybějící hlídání selhalo, místo aby prošlo náhodou.

**Co rekurze v SQL Serveru neumí, za to překlad nemůže.** Rekurzivní člen nesmí řádky slučovat (jen `UNION ALL`, žádné `DISTINCT` ani seskupení), takže hledání v grafu nevyřazuje vrcholy navštívené v dřívější úrovni a vyjmenovává sledy. Nad grafem `knows` scale factoru 1 je proto hledání bez meze hloubky drahé. Jestli dotazy katalogu na cesty ponesou mez hloubky, a budou tedy zjednodušené, nebo ji nést nebudou, je obsah katalogu podle rozhodnutí 110, ne tohle rozhodnutí.

### Seskupení podle výrazu

**`GroupByInstruction` stojí nad operandem**, sloupcem nebo výrazem, tak jako rozhodnutí 107 postavilo nad operandem projekci a klíč řazení; pozice, kterou 107 vědomě vynechalo, tím padá. Shodu na třech místech, kvůli které ji vynechalo, hlídá brána pravidlem samotného SQL: operand projekce, `HAVING` a řazení smí mimo agregát jmenovat sloupce jen uvnitř podstromu, který se strukturně rovná některému klíči seskupení; co pravidlem neprojde, odmítá `Failure` kategorie `Grouping` jmenovitě. Táž strukturní rovnost je zároveň to, co LINQ cíl potřebuje, aby výraz projekce přepsal na `g.Key`.

**Zápis.** T-SQL, HQL a JPQL opakují výraz v `GROUP BY` i v projekci, jak to dělá zdroj. LINQ píše `GroupBy(x => klíč)` a `g.Key` u jednoho klíče a anonymní klíč u víc klíčů; jména členů bere z aliasu projekce, která týž výraz projektuje, u sloupce z vlastnosti. **Výraz klíče, který projekce nepojmenuje, jméno v anonymním typu nemá** a vymyslet ho zakazuje rozhodnutí 028 — LINQ ten tvar bez vymyšleného jména nevysloví, a EF Core u něj proto ustoupí. Standardní JPQL seskupuje jen podle cesty; Hibernate i podle výrazu, u EclipseLinku a HQL NHibernatu je třeba to ověřit — cíl, u kterého to neobstojí, kategorii `ComputedGrouping` neuvede a ustoupí.

**Čtení.** T-SQL `GROUP BY` nad výrazem slovníku, HQL a JPQL totéž. LINQ `GroupBy(m => m.CreationDate.Year)` i anonymní klíč z výrazů; `g.Key` a `g.Key.X` v projekci se čtou zpět na výraz klíče, takže model nese výraz v projekci, jako ho nese zdroj T-SQL.

**Proč ne přepis přes mezivýsledek**, druhá cesta, kterou položka nabízela — definice by výraz projektovala pod aliasem a vnější dotaz seskupoval podle jejího sloupce. Řádky by se nezměnily, ale cíl by dostal dotaz v jiném tvaru, než jaký zdroj napsal a jaký by kdo napsal ručně, a čtení LINQ, kde je klíč z výrazu přirozený, by muselo vyrábět definici: dva tvary jednoho faktu, kterým se rozhodnutí 112 vyhnulo u odvozené tabulky. Argument, že NHibernate a EclipseLink by takový dotaz odmítly, padá s únikovou cestou na obou stranách, a o tvaru tedy rozhoduje to, co zdroj vyslovil.

### Funkce

**Měřítko slovníku se mění.** Rozhodnutí 107 bralo funkci jen s ověřeným zápisem ve všech čtyřech cílových jazycích, protože chybějící zápis znamenal odmítnutí. S únikovou cestou to nutné není: **funkce vstupuje do slovníku, má-li zápis v T-SQL** — zapisovači únikové cesty, takže co nevysloví on, nevysloví nikdo —, **a každý cíl ji buď vysloví ověřeným zápisem, nebo ji jeho deskriptor neuvede a cíl ustoupí.** Neověřený zápis do deskriptoru nevstupuje; je to táž bezpečná strana, kterou 107 popsalo u množiny `Functions`: mlčení deskriptoru znamená ústup, nikdy hádání. Tabulka níž proto uvádí zápisy podle dokumentace připnutých verzí a do deskriptoru vstoupí každý až po sondě při implementaci, jak to vyžadovalo 107.

| Slovník | T-SQL | HQL (NHibernate 5.7) | JPQL (Hibernate 7.4) | JPQL (EclipseLink 5.0) | LINQ (EF Core 10) |
|---|---|---|---|---|---|
| `DateAdd(jednotka, n, d)` | `DATEADD(minute, n, d)` | ověřit | `d + n minute` | — | `d.AddMinutes(n)` … |
| `DateDiff(jednotka, a, b)` | `DATEDIFF(minute, a, b)` | ověřit | `(b - a) by minute` | — | `EF.Functions.DateDiffMinute(a, b)` … |
| `Round(x, n)` | `ROUND(x, n)` | `round(x, n)` | `round(x, n)` | `ROUND(x, n)` | `Math.Round(x, n)` |
| `Sqrt(x)` | `SQRT(x)` | `sqrt(x)` | `sqrt(x)` | `SQRT(x)` | `Math.Sqrt(x)` |
| `Cast(x, skalár)` | `CAST(x AS typ dialektu)` | `cast(x as typ)` | `cast(x as typ)` | ověřit | `x.ToString()`, `(long)x` … |

Poznámky, které z tabulky nejsou vidět:

- **Jednotka datumové aritmetiky je uzavřený výčet `DateUnit`** (rok, měsíc, den, hodina, minuta, sekunda), který volání `DateAdd` a `DateDiff` nese vedle argumentů. V T-SQL je to klíčové slovo, ne hodnota, takže operandem být nemůže; továrna ho u těch dvou funkcí vyžaduje a jinde odmítá.
- **`DateDiff` počítá hranice, ne délku**, jak to dělá T-SQL `DATEDIFF`: mezi 10:00:59 a 10:01:00 je jedna minuta. `EF.Functions.DateDiffMinute` se na `DATEDIFF` mapuje doslova. Rozdíl časových údajů v HQL Hibernatu je délka, a jestli ho dialekt SQL Serveru vypíše jako `DATEDIFF`, se ověří na 4. stupni hodnotami, na kterých se počet hranic a délka liší; neobstojí-li, Hibernate ustoupí. JPQL EclipseLinku datumovou aritmetiku nemá.
- **`Round` má vždy dva argumenty**, protože T-SQL jiný tvar nemá; LINQ `Math.Round(x)` se čte jako `Round(x, 0)`. Zaokrouhluje databáze, ne .NET — provider EF Core `Math.Round` na `ROUND` překládá —, takže platí pravidlo SQL Serveru, ne bankéřské zaokrouhlení .NET; model nese, co se v dotazu stane, a to je `ROUND`.
- **`Cast` převádí do skaláru, ne do databázového typu**, jak model typy nese všude (rozhodnutí 024). Zapisovač T-SQL vybere typ dialektu tabulkou rozhodnutí 086, řetězec jako `NVARCHAR(MAX)`. Čtečka T-SQL proto čte jen převod, jehož cílový typ je ten, který by zapisovač napsal zpět — `NVARCHAR(MAX)` a typy bez délky a přesnosti; převod s délkou nebo do neunicode textu by mohl hodnotu zkrátit nebo změnit, model by to neunesl, a čtečka ho proto odmítá jmenovitě.
- **Co dál zůstává mimo slovník:** `REPLACE` mimo vzorek, `LEFT` a `RIGHT`, `CONVERT` se stylem, `SYSDATETIME` a `DateTime.UtcNow`. Čtečky je odmítají jako dosud.

### Okenní funkce řazení

**Tvar.** `QueryExpression` dostává čtvrtou továrnu, `Window(funkce, oddíly, řazení)`, nad uzavřeným výčtem `RankingFunction` (`RowNumber`, `Rank`, `DenseRank`); oddíly jsou operandy, řazení operandy se směrem. **Stojí jen v projekci**, kde ji připouští SQL; filtr nad ní — nejlepší řádek každé skupiny — jde přes mezivýsledek rozhodnutí 112, jak ho píše každý, kdo v SQL okenní funkci filtruje. Okenní agregát (`SUM(…) OVER`) a rámec okna (`ROWS BETWEEN`) slovník nenese a čtečky je odmítají jmenovitě; katalog LDBC je nepotřebuje.

**Kdo píše a čte.** T-SQL a HQL Hibernatu 7.4 (`row_number() over (partition by … order by …)`, *ověřit*), obojí i čte. EF Core, NHibernate a EclipseLink ustoupí. LINQ obrat `GroupBy(…).Select(g => g.OrderBy(…).First())`, který EF Core sám přeloží na `ROW_NUMBER`, se jako okenní funkce nečte: je to jiný tvar — jeden řádek za skupinu — a jeho přepis by byl čtením stejného druhu jako u rozhodnutí 103; zůstává vyslovenou mezí.

**Proč obecně, a ne jen nejlepší řádek přes `NOT EXISTS`.** BI 14 jde od rozhodnutí 112 napsat i bez okenní funkce — skóre ve `WITH` a nad ním `NOT EXISTS` lepší dvojice téhož města —, ale to je přepis textu katalogu, ne slovník: dotaz, který uživatel napíše s `ROW_NUMBER`, by dál končil odmítnutím. A není to týž dotaz: při shodě skóre vrací `NOT EXISTS` všechny nejlepší řádky, `ROW_NUMBER` jeden z nich.

### Agregace do seznamu

Mezi otevřenými položkami nebyla; katalog LDBC ji jmenuje u IC 1 a IC 12, jejichž specifikace vrací seznamy. **Tvar:** `QueryExpression` dostává pátou továrnu, `ListAggregate(hodnota, oddělovač, řazení)`, kterou brána bere jako agregát — platí pro ni pravidla seskupení i zákaz agregátu nad agregátem. Agregační funkce se jinak nese jako řetězec, protože pět jmen je ve všech cílích totéž slovo (107); tahle se v každém jazyce jmenuje jinak a nese oddělovač a řazení, takže řetězcem být nemůže. **Zápis:** T-SQL `STRING_AGG(x, ',') WITHIN GROUP (ORDER BY …)`; HQL Hibernatu `listagg(x, ',') within group (order by …)` (*ověřit*); LINQ `string.Join(",", g.OrderBy(…).Select(…))`, které EF Core překládá na `STRING_AGG` (*ověřit proti EF Core 10, i v korelovaném poddotazu*); NHibernate a EclipseLink ustoupí. **Čtení:** T-SQL, HQL a LINQ v týchž tvarech. Seznam se tím nese jako text s oddělovačem; jestli to katalog LDBC počítá za výsledek podle specifikace, je jeho obsah.

### EF Core: filtr vnějšího joinu, join mimo rovnosti a agregát bez seskupení

**Podmínka `ON` se dělí na rovnosti klíčů a zbytek.** Konjunkty zbytku, které jmenují jen pravou stranu, píše builder jako `Where` nad vnitřní posloupností — `LeftJoin(ctx.Set<Message>().Where(m => m.ParentMessageId == null), …)` —, což je přesné u vnitřního i vnějšího joinu; všech šest dotazů katalogu, které EF Core dnes odmítá kvůli joinu, má právě tenhle tvar. Zbytek, který jmenuje obě strany a není rovností, píše builder jako korelovaný `SelectMany` nad `Where(…)`, u levého vnějšího joinu s `DefaultIfEmpty()`; provider to překládá na `LEFT JOIN`, případně `OUTER APPLY` (*ověřit `ToQueryString()`*). Tím padá věta rozhodnutí 065, že join bez klíčových rovností je u EF Core `Failure`. Pravý a plný vnější join s takovou podmínkou LINQ nevysloví a EF Core u nich ustoupí.

**Čtení obráceně:** sdílená čtečka LINQ čte filtr uvnitř spojované posloupnosti — `join m in ctx.Messages.Where(…)`, `LeftJoin(ctx.Set<M>().Where(…), …)`, `SelectMany(… .Where(…).DefaultIfEmpty())` — do `ON`, takže identitní směr EF Core → EF Core projde a čtení, které rozhodnutí 103 nechalo stranou, je hotové.

**Agregát přes celý výsledek bez seskupení** (BI 11 a obecně každý `SELECT COUNT(*) FROM …`) vyslovuje LINQ jedině voláním, které řetěz ukončí a dotaz provede. Artefakt je ale dotaz (rozhodnutí 027) a jiný tvar, který by vrátil tytéž řádky, LINQ nemá — seskupení podle konstanty vrátí nad prázdnou tabulkou žádný řádek místo jednoho s nulou. EF Core proto ustoupí: místo emise, které dnes hlásí `Failure`, hlásí, že tvar jazyk cíle nemá.

### Nativní SQL předané v kódu

**Parsery poznávají předání v jiném jazyce, než jaký čtou, a co z něj umějí, čtou.** Rozpoznání je tvrzení wrapperu o API jeho frameworku (S1), jako výčet metod Dapperu v rozhodnutí 109:

- **EF Core:** `Database.SqlQuery<T>` a `SqlQueryRaw<T>`, a `FromSql`, `FromSqlRaw` a `FromSqlInterpolated` jako celý dotaz — tedy tvar, který vydává úniková cesta. `FromSql…`, za kterým řetěz LINQ pokračuje, zůstává odmítnutý jako krok, který mění množinu řádků (070): skládání nad nativním SQL je jiný tvar a vlastní výstup nástroje ho nevydává. `Database.ExecuteSql…` není čtecí dotaz a odmítá se jmenovitě.
- **NHibernate:** `CreateSQLQuery` (SQL) a `CreateQuery` v kódu C# (HQL parserem rozhodnutí 062).
- **JPA:** `createNativeQuery` (SQL).
- **Předání jménem** (`GetNamedQuery`, `createNamedQuery`) je odkaz na dotaz, který se čte tam, kde je definovaný — `<query>` a `<sql-query>` v `hbm.xml` už dnes. Samo artefakt nevydá a dostane záznam, který to říká, místo dnešního mlčení vedle přečteného dotazu.
- **Dotaz skládaný za běhu** (`QueryOver`, `CreateCriteria`) nemá text a dostane `Failure` jmenovitě, jako `createQuery` s argumentem, který není literál.

SQL z kteréhokoli předání čte sdílená čtečka T-SQL (082), jak dnes čte `<sql-query>` NHibernatu. Tvary parametrů jednotlivých API — interpolaci `{x}` a poziční `{0}` EF Core, `:x` NHibernatu a Hibernatu, `?1` JPA — převede wrapper před gramatikou, jako převádí kolekční parametr Dapperu (106); co převést neumí — výraz v interpolaci, který není identifikátor, zástupné symboly NHibernatu `{alias}` —, odmítá jmenovitě. Deklarovaný cizí dialekt zdroje čtení zastaví jako u každého SQL (088). Rozpoznávač role tříd (111) taková předání vidí, takže třída, která předává jen nativní SQL, přestane být čtená jako entita.

Tím do sebe obě odpovědi zapadají: **vlastní výstup únikové cesty se čte zpátky jako týž dotaz**, takže směr cíl → zdroj platí i pro artefakt, který ustoupil.

### Deskriptor a brána

- `QueryFeature` dostává čtyři hodnoty — `Recursion`, `ComputedGrouping`, `WindowFunction` a `ListAggregation` — a všech šest deskriptorů je uvede.
- `QueryFunction` dostává `DateAdd`, `DateDiff`, `Round`, `Sqrt` a `Cast`; přibývají výčty `DateUnit` a `RankingFunction`.
- `TargetFrameworkDescriptor` dostává vlastnost, která říká, že framework má API pro nativní SQL svého dialektu, na které builder smí ustoupit; kladně ji uvádějí EF Core, NHibernate, Hibernate a EclipseLink.
- `ConversionRecordKind` dostává `Fallback`.
- Brána v `Normalize()` přibírá pravidla rekurze a seskupení podle výrazu; o únikové cestě se rozhoduje v šabloně, jednou pro všechny cíle (023).

### Co se nemění

`IQueryVisitor` — nové tvary výrazu vypisuje každý visitor uvnitř operandu, jako u rozhodnutí 074, 083 a 107. Orchestrace. Osm kroků šablony a jejich pořadí. Pravidlo 053 a 065 — dotaz, který by vrátil jiné řádky, se nevydá — platí doslova: úniková cesta vrací tytéž řádky, a kde by je nevrátila, je to dál odmítnutí. Pravidla definice z rozhodnutí 112 platí a rekurze k nim přidává svá.

## Důsledky

**Věty dřívějších rozhodnutí, které přestávají platit; rozhodnutí sama platí dál.** U 065 věta, že NHibernate plný vnější join odmítá, a věta, že join bez klíčových rovností je u EF Core `Failure`. U 112 věty, že NHibernate a EclipseLink mezivýsledek odmítají a že rekurze se odmítá. U 107 věta, že `GroupByInstruction` zůstává sloupec, a měřítko slovníku funkcí „zápis ve všech čtyřech visitorech". Rozhodnutí 022 se nemění, naplňuje se; jen jeho námitka proti `FromSqlRaw` platila pro EF Core před verzí 8. Nový soubor, ne revize, protože všechno jmenované je naimplementované.

**Šest položek `open-items.md` tímhle zaniká a vzniká práce:** rekurzivní `WITH`, dotaz předaný v jiném jazyce, seskupení podle výrazu, funkce mimo slovník, okenní funkce a oba tvary EF Core. Pořadí práce plyne ze závislostí: úniková cesta jde první, protože na ní stojí, co každá další konstrukce udělá v cílech, které ji nevysloví — a sama přeloží BI 4, BI 8, BI 12 a BI 13 do NHibernatu a EclipseLinku a BI 11 do EF Core.

**Katalog LDBC se měří znovu**, a co který dotaz nese, říká katalog a drží `LdbcCatalogTest`, ne tohle rozhodnutí (110). Rozhodnutí otevírá IC 13, BI 15 a BI 20 rekurzí, IC 14 a BI 19 rekurzí s funkcemi a cestou jako textem, BI 1 seskupením podle výrazu, IC 7 a BI 17 datumovou aritmetikou, BI 14 okenní funkcí, IC 12 rekurzí a seznamem a IC 1 seznamem; sedm odmítnutí EF Core a čtyři dvojice odmítnutí NHibernatu a EclipseLinku přecházejí v překlad, nativní nebo únikovou cestou. Horní mezí je všech 41 dotazů ve všech pěti cílech. Katalog k tomu dostává u každého dotazu seznam cílů, které ustupují, se zdůvodněním, vedle seznamu cílů, které odmítají.

**Matice kategorií dostávají řádky** v obou sadách i v diferenční matici, každý z každého zdroje, který tvar vysloví: rekurzivní sestup hierarchií, procházka grafem s hlídáním cyklu, seskupení podle výrazu, datumová aritmetika, zaokrouhlení a odmocnina, převod typu v konkatenaci, číslování řádků v mezivýsledku s filtrem, agregace do seznamu, vnější join s filtrem v `ON`, join mimo rovnosti, agregát bez seskupení a nativní SQL předané v kódu ze zdrojů EF Core, NHibernate a JPA. Každá buňka nese jednu ze tří hodnot únikové cesty a 4. stupeň měří i buňky, které ustoupily.

**§9 a *Guarantees* dostávají nárok i meze.** Nárok: konstrukce, kterou dotazový jazyk cíle nevysloví, se píše nativním SQL deklarovaného dialektu přes API cílového frameworku, vždy se záznamem. Meze, které zůstávají vyslovené: `search` a `cycle` HQL, okenní agregáty a rámce, obrat LINQ pro nejlepší řádek skupiny, `FromSql…` se skládáním, dotaz skládaný za běhu, převod s délkou nebo do neunicode textu, kolekční parametr v nativním SQL EF Core a funkce mimo rozšířený slovník.

**Cena je vyslovená a je ze všech rozšíření dotazové větve největší:** úniková cesta ve čtyřech wrapperech nad sdíleným zapisovačem, čtyři hodnoty `QueryFeature`, pět funkcí a dva výčty, dvě továrny výrazu, operand v klíči seskupení, pravidla rekurze a seskupení v bráně, čtení nativního SQL ve třech wrapperech, dva tvary joinu v EF Core, rozšíření sdíleného testovacího schématu a řádky matic ve dvou sadách. **Pro sedmý framework se nemění nic** (S1): deskriptorem řekne, co jeho dotazový jazyk vysloví a jestli má nativní API, a napíše obal sdíleného textu; `AbstractWrappers` ani orchestrace se kvůli němu nemění. Determinismus (S2) drží, protože úniková cesta je funkcí deskriptoru a dotazu, ničeho dalšího.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR:** vstupy, které končily `Failure`, nově vydají artefakt; veřejná plocha se rozšiřuje o hodnotu `ConversionRecordKind.Fallback` a čtyři hodnoty `QueryFeature`.

**Testy.** Za každou konstrukci čtení do modelu a zápis do všech šesti cílů včetně identitního směru; u každého cíle, který ustupuje, artefakt se záznamem `Fallback`, jehož SQL je textem cíle Dapper, a zpáteční čtení toho artefaktu jako téhož dotazu. Odmítnutí, pokaždé `Failure` a prázdný výstup: každé pravidlo rekurze, výraz projekce mimo klíč seskupení, okenní funkce mimo projekci, převod s délkou, kolekční parametr v nativním SQL EF Core a dotaz skládaný za běhu. A nové řádky matic na 1.–4. stupni v obou sadách, nad hierarchií a grafem s cyklem.
