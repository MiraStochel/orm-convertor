# 104 — Projekce do SQL cíle se materializuje jako netypovaný řádek

Datum: 2026-09-29
Stav: platí
Požadavky: F7–F10, F11, F13, T2, T3
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md), [025](025-query-language-as-content-type.md), [027](027-query-artifact-verification.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md) a [089](089-differential-verification-as-the-fourth-level-over-a-query.md); JSS článek, pravidlo Q3; první běh diferenční matice přes kategorie T2 (2026-09-29); dokumentace Dapperu (*Query* bez typového parametru vrací `dynamic`) a MyBatisu (`resultType="map"`, `autoMappingUnknownColumnBehavior`)

## Kontext

Framework, jehož dotazy jsou SQL, nikde v dotazu nejmenuje typ, do kterého se řádky materializují — to je věta rozhodnutí 082, podle níž sdílený T-SQL builder odvozuje **výsledný typ z tabulky klauzule `FROM`**: entita převodu, která je na tu tabulku namapovaná, a kde není, jméno tabulky v jednotném čísle se záznamem `Convention`. Dapper z toho vypisuje `connection.Query<Entita>(…)` a metodu vracející `List<Entita>`, MyBatis `<select resultType="balík.Entita">` a deklaraci `List<Entita> metoda(…)`. **Pro dotaz nad celou entitou je to přesně pravidlo Q3 článku:** dotaz bez projekční instrukce materializuje celou entitu, a `SELECT *` nad `OrderLines` opravdu vrátí řádky, které oba frameworky do třídy `OrderLine` naplní podle jmen sloupců.

**Pro dotaz s projekcí je to tvrzení, které neplatí, a oba frameworky to zamlčí.** `SELECT ol.Description AS Text, ol.Quantity AS Qty` vrací sloupce `Text` a `Qty`; třída `OrderLine` žádnou takovou vlastnost nemá. Dapper sloupec, ke kterému nenajde vlastnost, ve výchozím nastavení přeskočí a vlastnost, ke které nepřijde sloupec, nechá na výchozí hodnotě; MyBatis mapuje sloupce na vlastnosti podle jména a neznámý sloupec ve výchozím nastavení (`autoMappingUnknownColumnBehavior = NONE`) mlčky ignoruje. Výsledkem je v obou cílech **seznam instancí entity, ve kterých není ani jedna z projektovaných hodnot** — `Description` null, `Quantity` nula — a žádná výjimka, žádný záznam. Totéž platí pro každou projekci, jejíž jména nejsou jmény vlastností kořenové entity: agregát (`SUM(ol.Quantity) AS Total`, `COUNT(*) AS Lines`, `COUNT(DISTINCT ol.OrderId) AS Orders`), sloupec spojené tabulky (`o.CustomerId` u řádku, jehož typem je `OrderLine`) a množinová operace (`ol.Description AS Text UNION p.ProductName AS Text`, materializovaná do `Product`). Jediná projekce, kterou dnešní tvar unese, je ta, jejíž aliasy se náhodou kryjí se jmény vlastností — a přesně tak byly napsané všechny projekční dotazy dosavadní diferenční matice (`p.ProductName AS ProductName`), takže 4. stupeň si vady nevšiml.

**Našel ji teprve 4. stupeň nad kategoriemi T2.** Sdílené texty kategorií píší projekci tak, jak se píše v projektech — s aliasem, s agregátem, přes join —, a při stavbě diferenční matice přes ně vyšlo najevo, že v pěti z devatenácti kategorií (projekce, join, agregace se seskupením, množinová operace, `COUNT(DISTINCT …)`) vrací dva ze šesti cílů řádky bez projektovaných hodnot. Není to vada dat ani porovnání: je to artefakt, který **vypadá jako přeložený dotaz a vrací jiný výsledek** — přesně ta věta, kterou rozhodnutí 053 označilo za nejhorší druh chyby a kvůli které se dotaz s dosazenou tautologií nevydává. Rozdíl je jen v tom, že tady se neliší množina řádků, nýbrž jejich obsah; pro T3 je to týž tichý falešný pozitiv.

**Ostatní čtyři cíle to dělají správně a stejně.** EF Core builder vypisuje projekci jako anonymní typ (`new { Text = ol.Description, Qty = ol.Quantity }`), HQL a JPQL vracejí řádek projekce pozičně jako `object[]`, respektive `Object[]` — tedy **netypovaný řádek**, jehož tvar dává projekce a ne entita. Nikdo z nich nesestavuje pro projekci třídu; u SQL cílů jsme ji dosud podsouvali jen proto, že odvození z tabulky bylo napsané pro jediný případ a nikdo ho nepodmínil.

Otázka je, co má SQL cíl vydat za dotaz, jehož řádek není entitou.

## Zvažované varianty

### 1 — Nechat typ entity a hlásit `Loss`

Nejmenší změna: artefakt zůstává, přibude záznam, že projektované hodnoty do instance nedorazí. Zamítáme, protože záznam nemění to, co konzument dostane — seznam instancí s výchozími hodnotami. Rozhodnutí [004](004-unexpressible-facts-as-warnings.md) dovoluje varováním zakrýt fakt, který cíl **nevyjádří**; oba frameworky ale projekci vyjádřit umí, jen jiným tvarem. Varovat před chybou, kterou umíme nedělat, je špatný obchod.

### 2 — Vygenerovat pro každý projekční dotaz vlastní třídu řádku

Typované DTO `QueryRow` s vlastností na každou projekci, `Query<QueryRow>` a `resultType="QueryRow"`. Vypadá nejčistěji a je to nejdražší volba: vzniká druh artefaktu, který mezireprezentace nezná — třída, která není entitou a nemá mapování —, pro každý dotaz jedna, se jménem, které by nástroj vymýšlel (a rozhodnutí 028 o vymýšlených jménech je opatrné z dobrého důvodu), s typem každé vlastnosti odvozeným ze sloupce nebo z agregátu, a MyBatis by k ní ještě potřeboval `<resultMap>`, protože aliasy agregátů nejsou sloupce žádné tabulky. Ani jeden z ostatních čtyř cílů to nedělá; SQL cíle by tak byly jediné, kterým projekce zakládá třídu. Zamítáme jako řešení mimo měřítko vady.

### 3 — Odmítnout projekci do SQL cíle záznamem `Failure`

Doslovné použití rozhodnutí 053. Zamítáme, protože 053 odmítá to, co cíl **nevyjádří** správně, a projekci oba frameworky vyjádří — Dapper ji dokonce nabízí jako svůj nejběžnější tvar. Odmítnutí by z matice T2 vyškrtlo Dapper a MyBatis jako cíle projekce, agregace, joinu s projekcí i množinové operace, tedy čtyř kategorií, a tvrdilo by o obou frameworcích něco, co o nich neplatí.

### 4 — Materializovat projekci jako netypovaný řádek

## Rozhodnutí

**Volíme variantu 4. Dotaz s projekční instrukcí se do SQL cíle materializuje jako netypovaný řádek, který pojmenovává sloupce projekce; dotaz bez projekce se materializuje do entity, jak to říká pravidlo Q3.**

Konkrétně:

- **Dapper** vypisuje `connection.Query(…)` bez typového parametru a metodu vracející `List<dynamic>`. Je to Dapperův vlastní tvar pro ad hoc projekci: řádek je `DapperRow`, implementuje `IDictionary<string, object>` a konzument k hodnotám sahá jménem sloupce, tedy aliasem, který dotaz napsal. Dotaz bez projekce zůstává `connection.Query<Entita>(…)` a `List<Entita>`.
- **MyBatis** vypisuje `<select resultType="map">` a deklaraci `List<Map<String, Object>> metoda(…)`. Je to tvar, který MyBatis pro ad hoc projekci dokumentuje; klíči mapy jsou popisky sloupců, jak je vrátí ovladač. Dotaz bez projekce zůstává `resultType="balík.Entita"` a `List<Entita>`.
- **Odvození výsledného typu z tabulky se děje jedině u dotazu bez projekce.** Záznam `Convention` o typu odvozeném ze jména tabulky tam, kde na tabulku není namapovaná entita, vzniká jen tehdy, když se typ opravdu použije; u projekce se neodvozuje nic a nehlásí nic, protože netypovaný řádek není konvence, nýbrž tvar, který projekce určuje sama.
- **Operand množinové operace nese svůj vlastní tvar.** Množinová operace nad dvěma dotazy bez projekce zůstává typovaná entitou pravého operandu, jak to bylo dosud; operand s projekcí dělá z celku netypovaný řádek. `SELECT DISTINCT` s projekcí je projekce a řídí se tímtéž.
- **Nic se nemění v mezireprezentaci ani v ostatních čtyřech cílech.** EF Core, NHibernate, Hibernate a EclipseLink už netypovaný řádek vracejí; tohle rozhodnutí je dorovnává, ne mění. Nic se nemění ani na straně čtení: aliasy projekce parser nese od začátku a právě ony teď pojmenují sloupce řádku.

## Důsledky

**Čtvrtý stupeň nad projekcí se u SQL cílů stává měřitelným** (rozhodnutí 089): řádek, který dotaz vrací, se dá přečíst podle jména sloupce ve všech šesti cílích — po jménu u Dapperu, MyBatisu, EF Core a u obou entitních tvarů, pozičně u HQL a JPQL — a kanonický výsledek diferenční matice o něm může vypovídat. Bez tohoto rozhodnutí by pět kategorií T2 mělo ve dvou cílech výsledek, který se s ničím neshoduje, a podíl funkční ekvivalence T3 by je buď počítal za selhání překladu, kterým nejsou, nebo by je matice musela vynechat, což 089 dovoluje jedině odmítnutému směru.

**Tvar artefaktu se u projekčních dotazů mění, a podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR:** konzument, který dnes dostává `List<Entita>` s prázdnými hodnotami, dostane `List<dynamic>`, respektive `List<Map<String, Object>>` s hodnotami; REST kontrakt se nemění. Testy, které u projekce tvrdily typ entity, se přepisují na tvar, který platí — je to totéž, co rozhodnutí 053 udělalo s testy tautologií.

**Konzumentský projekt MyBatisu potřebuje jeden import navíc**, `java.util.Map`, vedle `java.util.List`, který tam pro `List<Entita>` byl už dřív; javová sada ho jako konzument doplňuje (`JavaSources.wrapMapperInterface`), tak jako pro parametry doplňuje `java.util.Collection`. Na .NET straně `dynamic` žádnou referenci navíc nežádá, protože nad řádkem se v generované metodě neprovádí žádná dynamická operace.

**Co tím není rozhodnuto.** Typovaná třída řádku (varianta 2) může být někdy žádaná — konzument, který chce projekci procházet s kontrolou typů. Není to ale otázka správnosti, nýbrž pohodlí, a mezireprezentace by pro ni potřebovala pojem, který dnes nemá; zůstává mimo rozsah, dokud si o ni některý požadavek neřekne.

**Testy.** Dotaz bez projekce dál vydá `Query<Entita>` a `resultType` s entitou; dotaz s aliasovanou projekcí, s agregátem, s projekcí přes join a množinová operace s projekcí vydají netypovaný tvar v obou cílech; záznam `Convention` o odvozeném typu se u projekce nevydá. Doložení, kvůli kterému rozhodnutí vzniklo, nese diferenční matice: kategorie projekce, joinu, agregace, množinové operace a `COUNT(DISTINCT …)` mají v Dapperu i v MyBatisu 4. stupeň, který se shoduje s kanonickým výsledkem.
