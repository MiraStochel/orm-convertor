# 112 — Dotaz jako zdroj řádků je pojmenovaný mezivýsledek dotazu: odvozená tabulka i `WITH` se čtou do téže definice

Datum: 2026-10-01
Stav: platí
Požadavky: F7–F11, F13, T1, T2, S1, S2
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [028](028-assembly-name-is-not-ours-to-invent.md), [041](041-versioning-and-release.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md), [061](061-subquery-as-a-condition-operand.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md), [083](083-parameter-as-the-fifth-operand-shape.md), [085](085-a-row-count-is-a-number-or-a-parameter.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [103](103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md), [104](104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md), [105](105-a-query-formulates-its-own-demand-on-the-catalog.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [109](109-a-code-unit-carries-every-query-it-hands-over.md) a [110](110-ldbc-snb-as-a-second-reference-domain.md); JSS §5.4 („Non-core SQL constructs") a pravidla Q2, Q11, Q13, Q14 a Q15; otevřená položka „Dotaz jako zdroj řádků: odvozená tabulka a `WITH`"; sondy proti připnutým verzím z 2026-10-01 (níž, oddíl *Co který cíl vysloví*)

## Kontext

Mezireprezentace zná poddotaz jen jako operand: vnořený rozsah, který vrací hodnotu nebo množinu hodnot k porovnání (rozhodnutí 061), a od rozhodnutí 107 i list výrazu. Jako zdroj řádků — to, nad čím stojí `FROM` nebo join — zná jen tabulku entity. Rozhodnutí 061 to vyslovilo jako pravidlo: odvozená tabulka ve `FROM` „se do modelu nepřijímá", protože HQL NHibernate tu pozici nemá a pravidlo Q13 by nemělo odvozenou tabulku k čemu rozresolvovat. Rozhodnutí 070 pak přidalo `WITH` ke stejnému odmítnutí: sdílená čtečka T-SQL hlásí obojí záznamem `Failure`, protože dotaz přečtený bez něj by stál nad tabulkou, kterou nikdo nedeklaroval.

Obojí přitom vyslovuje to, co jeden plochý `SELECT` napsat neumí. **Agregát nad agregátem** — maximum přes počty, histogram počtů — není SQL v jedné úrovni; rozhodnutí 107 ho jmenovalo jako mez výrazu. **Druhý krok nad výsledkem prvního** — osoby podle počtu zpráv, členové nejoblíbenějších fór — potřebuje, aby výsledek prvního kroku byl řádky, nad kterými se dá filtrovat, spojovat a seskupovat znovu. V katalogu LDBC (rozhodnutí 110) kvůli tomu nejsou přeložené BI 4 (dva `WITH`, druhý čte první a hlavní dotaz čte druhý dvakrát) a BI 12 (odvozená tabulka) a BI 8 a BI 13 jsou zjednodušené právě o krok, který čte výsledek jiného kroku. Cíl z 2026-10-01 je dotazy dokončit, jak daleko to jde, a `WITH` s rekurzí do slovníku patří určitě; rekurzivní `WITH` je další položka a stojí na téhle.

Článek tu pozici vyslovuje jako vědomou mez: §5.4 počítá *common table expressions* mezi konstrukce, které „nejsou součástí základní abstrakce", a dodává, že reprezentace je navržená tak, aby se dala rozšířit. Rozšíření slovníku nad normalizovanou sadu článku už máme třikrát — stránkování (060), `DISTINCT` (073), výraz (107) — a pokaždé za stejných podmínek: tvar má protějšek ve většině cílů, kde ho nemá, cíl odmítá podle 053, a čte se ve všech jazycích, které ho vyslovují.

Rozhodnout je třeba tři věci. **Tvar:** jestli model nese odvozenou tabulku, pojmenovanou definici, nebo obojí. **Co definice smí:** kde stojí, co smí číst, odkud mají její sloupce jméno a typ, když nejsou vlastnostmi žádné entity. **Rozsah po jazycích:** který cíl tvar vysloví a který jazyk ho čte — u každého ověřené proti připnuté verzi, ne převzaté z dokumentace.

## Zvažované varianty

### 1 — Ponechat mez a zapsat ji do katalogu

Dnešní stav. Zamítáme: cíl z 2026-10-01 tu pozici výslovně otevírá, katalog LDBC by dál nesl dva nepřeložené a dva zjednodušené dotazy z důvodu, který není v cílech, ale v našem slovníku, a rekurzivní `WITH` — většina toho, co katalog nepřekládá — by neměl na čem stát.

### 2 — Jen odvozená tabulka; čtečka `WITH` rozvine na místo každého odkazu

Zdroj řádků by vedle tabulky směl být vnořený rozsah (`FromInstruction` a `JoinInstruction` s tělem). `WITH` by čtečka rozvinula do odvozené tabulky na každém místě, kde se na něj dotaz odkazuje — nerekurzivní `WITH` znamená totéž co odvozená tabulka vložená na každé místo odkazu a SQL Server ho tak i provádí, takže řádky by se nezměnily. Je to menší zásah: jedno pole na dvou instrukcích. Zamítáme ze tří důvodů. Za prvé **rekurze jméno potřebuje**: rekurzivní člen se odkazuje sám na sebe a rozvinout ho nejde, takže následující položka by musela přidat pojmenovanou definici stejně a model by nesl dva tvary jednoho faktu. Za druhé **cíl, který `WITH` píše, by dostal zpátky vnoření** a definice čtená dvakrát (BI 4 čte `TopForum` ve `FROM` i v poddotazu) by vyšla jako dvě kopie těla — výstup, ve kterém uživatel nepozná, že jde o jeden mezivýsledek, a při úpravě jedné kopie rozejde dvě místa, která zdroj držel jako jedno. Za třetí model by tím zapomněl fakt, který zdroj vyslovil — že dvě místa čtou totéž —, a 048 ani 004 nedávají takovému zapomenutí jméno ztráty, protože řádky se nemění; je to jen chudší model, než jaký zdroj napsal.

### 3 — Dva tvary: odvozená tabulka s tělem a vedle ní pojmenovaná definice pro `WITH`

Každá syntaxe by měla svůj tvar a obě by se psaly tak, jak je zdroj napsal. Zamítáme: odvozená tabulka *je* definice čtená jednou na místě, kde stojí — standard SQL obojí definuje týmž *query expression* se jménem, liší se jen tím, kam text definici klade. Dva tvary by znamenaly dvě větve v každém ze šesti builderů, dvě větve v každé bráně šablony (typování, parametry, poptávka po katalogu, výrazy, časové literály) a dva způsoby, jak se tytéž sloupce dostanou do vnějšího rozsahu, a cíl, který jeden z tvarů nemá — JPQL EclipseLinku `WITH` nemá vůbec —, by musel ten druhý stejně umět převést. Je to rozlišení, které nemění, které řádky se vrátí, a model ho nést nemá z téhož důvodu, z jakého nenese `BETWEEN` (pravidlo Q14 ho přepisuje na dvojici porovnání) ani dotazový výraz LINQ (rozhodnutí 103 ho čte jako jeho přepis na řetěz).

### 4 — Pojmenovaný mezivýsledek na úrovni dotazu, ke kterému vede `WITH` i odvozená tabulka

## Rozhodnutí

**Volíme variantu 4. Dotaz nese vedle svého těla uspořádaný seznam definic — `WithInstruction(Name, Body)` —, každá je pojmenovaný mezivýsledek celého dotazu, a zdroj řádků (`FROM` i pravá strana joinu) na něj ukazuje jménem tak, jako ukazuje na tabulku. `WITH` se čte do definice přímo, odvozená tabulka do definice pojmenované svým aliasem. Sloupce definice jsou její projekce a typ jim dává šablona z mapovací mezireprezentace. Tvar píší Dapper, MyBatis, Hibernate a EF Core; NHibernate a EclipseLink ho odmítají podle rozhodnutí 053.**

### Tvar

Definice stojí **na úrovni dotazu**, ne na úrovni rozsahu, ve kterém ji zdroj napsal — tak, jak T-SQL dovoluje `WITH` jen před příkazem. Odvozenou tabulku čtečka vyzdvihne na tuto úroveň, ať stojí kdekoli: ve `FROM` hlavního dotazu, v těle jiné definice, v poddotazu operandu, v operandu množinové operace. Je to přesné, protože definice nevidí dotaz kolem sebe (níž) — nezávislý dotaz znamená totéž, ať je napsaný na místě, nebo pojmenovaný dřív —, a za tu cenu žádný builder nemusí umět vnořenou definici: definice je jedna úroveň, jedno místo, jeden seznam. Pořadí seznamu je pořadí závislostí: definice smí číst definice před sebou, a protože čtečka definuje vnitřní odvozenou tabulku dřív než tu, v jejímž těle stojí, vychází pořadí samo.

**Jméno je jedno na dotaz.** Definici najde zdroj řádků podle jména, takže jméno nesmí znamenat dvě věci: druhá definice téhož jména, i ve dvou různých poddotazech, a odvozená tabulka, jejíž alias je zároveň jménem tabulky, kterou příkaz čte, se odmítají záznamem `Failure` — vyzdvižená definice by jinak zastínila tabulku na místě, kde zdroj tabulku myslel. Jméno si nevymýšlíme (rozhodnutí 028): `WITH` ho dává, odvozená tabulka ho má v aliasu, který T-SQL vyžaduje, a v LINQ je jménem proměnná, ve které kód mezivýsledek drží, nebo parametr lambdy, kterým řetěz pojmenovává řádky kroku, jenž mezivýsledek čte.

**Seznam sloupců `WITH x(a, b)`** se čte jako aliasy projekcí těla — přesný přepis, protože seznam nedělá nic jiného, než že přejmenuje sloupce. HQL seznam sloupců nemá (ověřeno), a model, který by ho nesl, by jeden cíl musel stejně přepisovat.

**Vykreslení je práce builderu, `IQueryVisitor` se nemění** — táž věta jako u poddotazu (061) a ze stejného důvodu: složit tělo definice znamená normalizaci, osm kroků a pořadí textu cíle. `WithInstruction.Accept` vrací prázdný řetězec jako `SubQueryInstruction.Accept`.

### Co definice smí

- **Nevidí dotaz kolem sebe.** Definice, jejíž tělo jmenuje alias, který samo nedeklaruje, je *laterální* odkaz — `CROSS APPLY` T-SQL, `lateral` HQL — a ten slovník nemá: CTE ho v T-SQL neumí vůbec a Hibernate ho bez slova `lateral` odmítá (ověřeno). Šablona ho odmítá záznamem `Failure` kategorie nového rysu (níž). Proto také smí čtečka vyzdvihovat.
- **Tělo je `SELECT`, nebo množinová operace** — kterou potřebuje rekurze, a nerekurzivní definice ji nese stejně; jména sloupců pak dává levý operand, jak to dělá SQL.
- **Tělo vyslovuje své sloupce, a každý má jméno**: alias, nebo holý sloupec, který se jmenuje sám. Agregát nebo výraz bez aliasu, dva sloupce téhož jména a projekce celé entity se odmítají. První dva by T-SQL sám odmítl („No column name was specified", „specified multiple times"), třetí odmítá Hibernate („aliases are required in CTEs", ověřeno) a vlastní řádek entity by z definice udělal druhou entitu, ne mezivýsledek. Šablona dá každé projekci těla jméno výslovně, takže cíl, který alias v definici vyžaduje, ho dostane i tam, kde ho zdroj nechal sloupci.
- **Řazení v těle bez stránkování se vypouští se záznamem `Loss`** — T-SQL ho v CTE ani v odvozené tabulce bez `TOP`/`OFFSET` nepřipouští a pořadí řádků mezivýsledku nemá vliv na to, které řádky vrátí dotaz nad ním; je to tatáž věta, kterou 061 řeklo o poddotazu. **Stránkování v těle se nese** — `TOP (100) … ORDER BY` je jádro BI 4 — v každém cíli, který tvar píše: T-SQL `TOP` a `OFFSET`, HQL `offset … rows` a `fetch first … rows only` v textu, LINQ `Take` a `Skip` v řetězu proměnné.
- **Rekurze ne.** Definice, jejíž tělo jmenuje samo sebe, se odmítá; je to následující položka a toto rozhodnutí jí připravuje jméno, ne odpověď.

### Typ a jméno sloupců

Vnější dotaz jmenuje sloupce definice tak, jak jmenuje sloupce tabulky: alias a jméno (`oc.Orders`). Žádná entita je nemá, a přitom na nich stojí čtyři mechanismy šablony — brána parametrů (083) typuje parametr ze sloupce na druhé straně porovnání, typování časových literálů (024) a brána výrazů (107) potřebují skalár sloupce a poptávka po katalogu (105) se ptá, kterou tabulku dotaz potřebuje svázat — a tři buildery, které jmenují vlastnosti místo sloupců (HQL, JPQL, LINQ), mapují zpět přes `EntityMaps`. **Šablona proto popíše řádek každé definice jako entitu platnou jen uvnitř dotazu**: vlastnost a sloupec téhož jména za každou projekci těla, skalár odvozený bránou z rozsahu těla — `COUNT` počtem, ostatní agregáty skalárem svého argumentu, výraz tabulkou rozhodnutí 107, sloupec z mapování. Všechny mechanismy, které umějí sloupec entity, pak umějí i sloupec definice, bez jediné větve navíc, a pravidlo Q13 — dotaz je úplný, když se jeho zdroj a atributy rozresolvují přes mapování — platí doslova: rozresolvují se přes mapování mezivýsledku, které šablona z těla odvodí. Ta entita nikdy neopustí builder: není to fakt mapování, nevzniká z ní artefakt a orchestrace ji nevidí.

Popis vzniká dvakrát, jako rozlišení jmen ve dvou dosavadních průchodech šablony: brána parametrů typuje jen přes vyslovené mapování a vykreslování i přes konvenci rozhodnutí 050, takže sloupec definice, jehož podkladový sloupec zná jen konvence, nedá parametru skalár, ale vykreslí se pod svou vlastností — týž rozdíl, jaký mají sloupce tabulek.

Poptávka po katalogu (105) jde do těl definic jako do poddotazů a parametr porovnaný se sloupcem definice se ptá po tabulce za sloupcem, ze kterého definice ten sloupec vzala — ne po jménu definice, které v katalogu není.

### Co který cíl vysloví

Ověřeno sondami proti připnutým verzím 2026-10-01: Hibernate 7.4.5 a EclipseLink 5.0.0 přes `createQuery` a spuštění nad SQL Serverem, EF Core 10 přes `ToQueryString()` a spuštění.

- **Dapper a MyBatis (T-SQL)** píší definice jako `WITH a AS (…), b AS (…)` před příkazem — i ty, které zdroj napsal jako odvozenou tabulku. Model nedrží, kterou syntaxí zdroj mezivýsledek napsal, a řádky jsou tytéž; pravidlo Q15 zachovává pořadí instrukcí a strukturu podmínek, ne syntaxi. Dotaz s projekcí celé řádky nad definicí materializuje jako netypovaný řádek (rozhodnutí 104), protože definice žádnou třídu nemá.
- **Hibernate (HQL 7.4)** píše `with a as (…)` stejně. Ověřeno: odvozená tabulka ve `from` i za `join` a `left join`, `with` čtený z `from`, z `join`, z poddotazu i dvakrát, definice nad definicí, agregát nad agregátem a `limit` i `fetch first` v těle. Tři věci Hibernate odmítá a builder je proto nepíše: odkaz na CTE bez aliasu (`from d` a pak `d.x`), projekci celé odvozené řádky (`select d` — „a derived model part does not have identifying parts"; builder vypíše její sloupce) a seznam sloupců `d(a, b)`.
- **EF Core (LINQ)** píše každou definici jako lokální proměnnou generované metody, `var a = …;`, a odkaz jako tu proměnnou: kořen řetězu, vnitřní posloupnost `Join` a `LeftJoin`, kořen vnořeného řetězu pod `Contains`, `Any` nebo agregátem. Ověřeno: EF Core 10 skládá proměnnou do odvozené tabulky na každém místě odkazu, včetně agregátu nad seskupeným výsledkem, joinu na něj, dvojího odkazu a `Take` uvnitř. Metoda zůstává `IQueryable` bez typového argumentu, protože prvkem je anonymní typ.
- **NHibernate (HQL 5.7)** nemá ani jedno a cíl tvar odmítá záznamem `Failure` — zdroj řádků vynechaný z dotazu by vrátil jiné řádky (053). Jestli místo toho napsat nativní SQL přes `CreateSQLQuery`, je otázka položky o rekurzi, kde se týká tří cílů ze šesti, a odpověď musí být pro obě položky táž.
- **EclipseLink (JPQL 5.0)**: JPQL 3.2 nemá ani jedno a rozšíření EclipseLinku nese poddotaz ve `FROM` jen na jednom místě ze čtyř — jako deklaraci, která není první a stojí za čárkou, tedy jako kartézský součin s filtrem. Ověřeno: odvozená tabulka jako první deklarace, za `join`, vnořená do jiné a uvnitř poddotazu se odmítá a `with` parser nezná. Cíl, který by mezivýsledek psal jen jako druhou deklaraci vnitřního joinu, by odmítal po jednom tvaru většinu případů a ten zbytek by přepisoval; **EclipseLink tvar odmítá jako celek**, deskriptorem, a deskriptory Hibernatu a EclipseLinku se tím v dotazových schopnostech poprvé rozcházejí — jako implementační profily, které rozhodnutí 080 předpokládalo.

### Čtení po jazycích

- **T-SQL** (Dapper, MyBatis a `<sql-query>` NHibernatu přes sdílenou čtečku, rozhodnutí 082) čte `WITH` včetně seznamu sloupců a odvozenou tabulku ve `FROM` i za joinem. Dál odmítá `WITH XMLNAMESPACES`, rekurzivní `WITH`, `APPLY` (laterální join) a odvozenou tabulku z `VALUES`, protože nic z toho slovník nenese.
- **HQL Hibernatu** čte `with` a odvozenou tabulku ve `from` i za joinem háčkem dialektu (rozhodnutí 077, čtvrtý háček), stejně jako čte `limit`. Nápovědu `materialized` čte jako ztrátu: mění plán, ne řádky (048). **JPQL EclipseLinku** čte, co jeho jazyk píše, a nic z toho nečte: deklarace za čárkou je kartézský součin, který slovník nenese a parser ho odmítá jako dosud.
- **HQL NHibernatu** nemá co číst.
- **LINQ EF Core** vyslovuje odvozenou tabulku skládáním a `WITH` proměnnou. Sdílený parser čte jako definici řetěz, na který navazuje krok nad výsledkem dosavadního rozsahu, a to jen po projekci, která vyslovila sloupce — nad celou entitou se kroky čtou jako dosud: po projekci filtr, seskupení, join, další projekce, koncová agregace a řazení podle něčeho jiného než projektovaného sloupce (řazení podle projektovaného sloupce je řazení podle aliasu, rozhodnutí 073), po výřezu krok, který s ním nekomutuje, a po `Distinct()` krok, který se před ním čte jinak. Proměnná, která drží dotaz, je definicí pojmenovanou po sobě, kde ji dotaz čte jako hotové řádky: jako vnitřní posloupnost joinu, nebo tam, kde řetěz přes ni pokračuje takovým krokem — i uvnitř poddotazu, takže proměnná spojená joinem a agregovaná v poddotazu je jedna definice čtená dvakrát. **LINQ NHibernatu** tvar nečte: provider překládá do HQL, které ho nemá, a háček zůstává vypnutý stejně jako pro jiné kroky, které jeden provider přeloží a druhý ne.

### Brána a deskriptor

Nová kategorie dotazového rysu **`IntermediateResult`**. Pravidla definice — jedinečné jméno, žádný laterální odkaz, žádná rekurze ani dopředný odkaz, pojmenované a jedinečné sloupce, žádná celá entita, řazení bez stránkování jako ztráta — drží jedno místo šablony pro všechny cíle (rozhodnutí 023). Cíl, jehož deskriptor rys vede jako nevyjádřitelný, dotaz odmítá záznamem `Failure` téže kategorie, ne ztrátou, jakou dává mechanická kontrola pravidla Q14 jinde: zahozená definice by nechala zdroj řádků jmenovat tabulku, která neexistuje.

### Co se nemění

`IQueryVisitor`, šest tvarů operandu, osm kroků šablony a jejich pořadí. Poddotaz v podmínce `ON` zůstává odmítnutý (061); join na definici má `ON` nad sloupci a tím pravidlem neprochází. Množinová operace zůstává nejvyšším tvarem dotazu; definice stojí před ní, jako stojí `WITH` před `UNION`.

## Důsledky

**Věta rozhodnutí 061 o odvozené tabulce přestává platit; 061 samo platí dál.** Jeho volba — poddotaz jako operand — se nemění; mění se jen to, že zdrojem řádků smí být i mezivýsledek, a oba důvody, které 061 uvedlo, padají: HQL NHibernatu tu pozici nemá, a proto NHibernate tvar odmítá, Q13 má k čemu rozresolvovat. Totéž pro 070: pravidlo — parser odmítá, co by změnilo množinu řádků — platí, jen `WITH` a odvozenou tabulku už model nese. Nový soubor je tu proto, že obě rozhodnutí jsou naimplementovaná; tentýž vztah má 074 k 061.

**Katalog LDBC se měří znovu.** BI 4 a BI 12 se přeloží do cílů, které tvar píší, a BI 8 a BI 13 mohou nést krok, o který byly zjednodušené; co přesně který dotaz nese, říká katalog a drží `LdbcCatalogTest`, ne tohle rozhodnutí. Joiny s nerovností v `ON`, které EF Core neumí (rozhodnutí 065), zůstávají důvodem, proč některé z nich EF Core odmítá.

**Matice T2 dostává dvě kategorie**: seskupení nad seskupeným výsledkem (odvozená tabulka) a mezivýsledek čtený dvakrát (`WITH` za joinem i v poddotazu), v obou sadách a v diferenční matici čtvrtého stupně, se zdroji Dapper, MyBatis, Hibernate a EF Core a s NHibernatem a EclipseLinkem jako cíli, které tvar odmítají deskriptorem.

**Pro sedmý framework se nemění nic** (S1): nový wrapper dostane definice ze šablony hotové — normalizované, s popisem řádku — a v deskriptoru řekne, jestli je píše. Determinismus (S2) drží pořadí definic, které je pořadím čtení, a pořadí parametrů metody, které začíná v tělech definic, protože tam je text začíná.

**Rekurzivní `WITH` stojí na tomhle.** Rekurzivní definice bude definice, jejíž tělo je množinová operace jmenující sebe samu; jméno, místo i popis řádku má. Otevřená zůstává otázka nativního SQL pro tři cíle, které ji nevysloví — a s ní i to, jestli by se jím nakonec nevyslovila i nerekurzivní definice v NHibernatu a EclipseLinku.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR**: vstupy, které dřív končily záznamem `Failure`, nově vydají artefakt; veřejná plocha se rozšiřuje o hodnotu výčtu `QueryFeature.IntermediateResult`.
