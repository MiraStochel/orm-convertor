# 119 — Kvantifikované porovnání (`ALL`, `ANY`, `SOME`) je porovnání s kvantifikátorem nad poddotazem: `= ANY` je `IN`, `<> ALL` je `NOT IN`, LINQ ho píše jako `All()`/`Any()` nad projekcí a negaci převrací De Morganem

Datum: 2026-10-10
Stav: platí
Požadavky: F7–F10, F11, F13, T2, T3, S1, S2
Podklad: rozhodnutí [002](002-is-null-as-comparison-operator.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [061](061-subquery-as-a-condition-operand.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [083](083-parameter-as-the-fifth-operand-shape.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [102](102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) a [118](118-nhibernate-writes-a-linq-form-beside-its-hql.md); JSS §5.3 (operandy podmínkového stromu) a pravidla Q11, Q14; ISO/IEC 9075-2 (SQL/Foundation), §8.9 *quantified comparison predicate*; dokumentace EF Core *Null semantics* (verze 10) a zdroj `NHibernate.Linq` 5.7.0 (`AllResultOperatorProcessor`)

## Kontext

**Kvantifikované porovnání** — `x > ALL (SELECT …)`, `x < ANY (SELECT …)`, `x >= SOME (SELECT …)` — je součástí standardu SQL od SQL-92, JPQL ho má od JPA 1.0 a HQL NHibernatu i Hibernatu ho vyslovují týmiž slovy. Pravidlo Q11 článku jmenuje poddotaz jako operand porovnání a rozhodnutí 061 ho do mezireprezentace zavedlo ve třech tvarech: `IN (poddotaz)`, `EXISTS (poddotaz)` a skalární porovnání `x > (poddotaz)`. Čtvrtý tvar, který standard staví hned vedle nich, chyběl: sdílená čtečka T-SQL nemá pro `SubqueryComparisonPredicate` větev, takže celá klauzule `WHERE` končí odmítnutím „konstrukce mimo slovník"; parsery HQL a JPQL neznají `any` ani `some` jako klíčová slova; parser LINQ čte `Any(predikát)` jako `EXISTS` (061) a `All(…)` jmenovitě odmítá, protože ho zná jen jako koncové volání, které dotaz vykoná. Odmítnutí je správná reakce na konstrukci, kterou model nenese (rozhodnutí 070) — ale konstrukce, kterou vyslovují čtyři z pěti dotazových jazyků, kterými nástroj čte, patří do slovníku, ne za jeho hranici.

Otázka není, *zda* kvantifikátor nést, ale **kde v modelu sedí a co s ním udělá cíl, jehož jazyk kvantifikátor nemá** — LINQ. Oba poskytovatelé LINQ, EF Core 10 i NHibernate 5.7.0, nabízejí `All(predikát)` a `Any(predikát)` nad vnořeným řetězem, a oba je překládají na `NOT EXISTS`/`EXISTS` s predikátem uvnitř. To je právě místo, kde se SQL a C# rozcházejí: SQL počítá v trojhodnotové logice, kde porovnání s `NULL` je *unknown* a `WHERE` takový řádek vyřadí; C# počítá ve dvouhodnotové, kde porovnání s `null` je `false` a jeho negace `true`. Pro `ANY` je to jedno — `x > ANY S` platí, právě když existuje hodnota, pro kterou `x > v` je *pravda*, a to je v obou logikách tatáž množina řádků. Pro `ALL` ne: `x > ALL S` je v SQL *unknown*, jakmile S obsahuje `NULL` a žádná hodnota porovnání nevyvrací, a řádek vypadne; `S.All(v => x > v)` by v C# byl `true` a řádek by zůstal. Právě tohle je ten případ, který pravidlo 053 zakazuje: dotaz, který vrací jiné řádky.

| S | `x > ALL S` v SQL | `S.All(v => x > v)` v dvouhodnotové logice |
|---|---|---|
| prázdná | pravda | pravda |
| všechny hodnoty menší než x | pravda | pravda |
| některá hodnota ≥ x | nepravda | nepravda |
| některá hodnota `NULL`, ostatní menší než x | **unknown → řádek vypadne** | **pravda → řádek zůstane** |
| x je `NULL`, S neprázdná | unknown → řádek vypadne | pravda → řádek zůstane |

Druhý rozdíl je **negace**: `NOT (x > ALL S)` je v SQL *unknown*, kde `x > ALL S` je *unknown*, a řádek vypadne; `!S.All(…)` by řádek, který `All` vrátil jako `false`, obrátil na `true` a nechal. A třetí: dva z dvanácti párů operátor–kvantifikátor jsou jen jiným zápisem tvarů, které model už nese — `x = ANY S` je přesně `x IN S` a `x <> ALL S` je přesně `x NOT IN S`, i v trojhodnotové logice.

## Zvažované možnosti

### 1 — Nové hodnoty `ComparisonOperator` (`GreaterThanAll`, `GreaterThanAny`, …)

Dvanáct nových operátorů, po jednom pro každý pár. Zamítáme: kvantifikátor je ortogonální k operátoru — říká, *jak* se dvě strany porovnávají s množinou, ne *čím* — a dvanáct hodnot by znamenalo dvanáct nových větví v převodní tabulce každého ze čtyř visitorů a v každé bráně šablony, která operátor rozlišuje (typování parametru podle druhé strany, poptávka po katalogu, `Like`). Rozhodnutí 102 vyřešilo stejnou otázku pro `ESCAPE`: vlastnost porovnání, ne nový operátor.

### 2 — Přepsat při čtení na tvary, které model už nese

`x > ANY S` je přesně `EXISTS (SELECT 1 FROM S WHERE x > v)`, a `x > ALL S` je přesně `NOT EXISTS (SELECT 1 FROM S WHERE x <= v OR x IS NULL OR v IS NULL)`; model by se neměnil. Zamítáme ze dvou důvodů. Za prvé **přepis by dostaly i cíle, které kvantifikátor vyslovují** — Dapper, MyBatis, NHibernate, Hibernate i EclipseLink by z dotazu napsaného s `ALL` vydaly `NOT EXISTS` s trojicí podmínek, výstup, v němž uživatel nepozná vstup a který rozhodnutí 113 vede jako zásadu opačnou: přepis patří jen tam, kde cíl konstrukci nevysloví. Za druhé přepis `ALL` na `MAX`/`MIN` (`x > ALL S` jako `x > (SELECT MAX(v) FROM S)`), který se nabízí jako čitelnější, **není ekvivalentní**: nad prázdnou S je `> ALL` pravda a `> (SELECT MAX …)` unknown, a `MAX` přeskakuje `NULL`, který `ALL` nepřeskočí.

### 3 — Kvantifikátor jako vlastnost porovnání; dva páry se složí do `IN`; LINQ přepisuje jen sám sebe

Zvoleno. Popis níže.

## Rozhodnutí

**`ComparisonCondition` nese `Quantifier?` (`All` | `Any`) vedle `Escape?`** (rozhodnutí 024, 102). Kvantifikované porovnání má v modelu jeden tvar: vlevo hodnota vnějšího dotazu (sloupec, konstanta, parametr, výraz), operátor jeden ze šesti relačních, vpravo poddotaz (`QueryOperand.Nested`, 061) a kvantifikátor. Porovnání napsané s poddotazem vlevo (`(SELECT …) < ALL …` LINQ `v < c.Credit`) čtečka zrcadlí (`Mirrored()`), aby model držel jeden tvar. `IQueryVisitor` se nemění — táž věta jako u rozhodnutí 060, 061 a 083, a ze stejného důvodu: povrch, který S1 slibuje sedmému frameworku, zůstává.

**`SOME` je `ANY`.** Standard definuje `SOME` jako synonymum; model nese jedno jméno za jeden význam a čtečky čtou `some` jako `Any` beze záznamu — řádky jsou tytéž a není co hlásit.

**`= ANY` je `IN` a `<> ALL` je `NOT IN`, přesně.** Továrna `ComparisonCondition.Quantified` tyto dva páry složí do stromu `IN`, resp. `NotCondition(IN)`, který cíle už píší (061); čtečka vydá záznam `Convention` („bylo přepsáno na `IN`, vybírá tytéž řádky"), jako rozhodnutí o pravidle Q14 vydává u `BETWEEN`. Model tím nenese dva zápisy jednoho faktu — a cíl, který `IN` nad poddotazem vysloví, ho vysloví pro oba zápisy stejně. Ostatních deset párů kvantifikátor drží.

**Textové cíle píší kvantifikátor slovem.** T-SQL `x > ALL (SELECT …)`, HQL i JPQL `x > all (select …)`; poddotaz prochází týmž `NormalizeSubQueryOperand` jako operand `IN` a dostává totéž pravidlo právě jedné projekce (061). Nic jiného se v nich nemění.

**LINQ přepisuje kvantifikátor na `All()`/`Any()` nad projekcí poddotazu.** Poddotaz se vykreslí v pozici, kterou má pravá strana `IN` — řetěz zakončený `Select` jednoho sloupce —, a porovnání se přesune do lambdy: `řetěz.Select(o => o.Total).All(v => c.Credit > v)`, `….Any(v => c.Credit < v)`. Pro `ANY` je přepis přesný (tabulka výše). Pro `ALL` je přesný tam, kde poskytovatel sám přenese dvouhodnotovou logiku C# do SQL: **EF Core 10** negaci uvnitř svého `NOT EXISTS` přepíše tak, že nad `NULL` na kterékoli straně platí (*null semantics compensation*, výchozí režim bez `UseRelationalNulls`), takže `All()` vybere právě řádky, které vybere `ALL`. **NHibernate 5.7.0** negaci zapíše jako `NOT (…)` SQL a `NULL` nechá *unknown*; jeho druhý tvar (118) proto dostane testy na `NULL` vypsané do lambdy — `All(v => c.Credit > v && v != null && c.Credit != null)`, každý jen u strany, o které mapování neříká `NOT NULL` — a tím vybírá tytéž řádky jako SQL. Visitor to rozhoduje jedním přepínačem `CompensatesNullSemantics`, který NHibernate překlápí; typ projektovaného sloupce si visitor dohledá přes zdroj poddotazu (`EntityByName`, který mu půjčí builder), protože rozsah poddotazu je v okamžiku zápisu už zavřený. Závazný tvar NHibernatu je dál HQL, které kvantifikátor píše slovem.

**Negaci kvantifikovaného porovnání LINQ nepíše vykřičníkem, ale převrátí ji De Morganem**: `NOT (x > ALL S)` vyjde jako `x <= ANY S`, `NOT (x >= ANY S)` jako `x < ALL S` (`NegatedQuantified()`). V trojhodnotové logice je to přesná rovnost — obě strany jsou *unknown* nad stejnými řádky —, kdežto `!All(…)` by řádky, které `All()` vyřadil, obrátil na vybrané. Textové cíle negaci píší tak, jak stála, protože jejich `NOT` je `NOT` SQL.

**Čtečky.** T-SQL čte `SubqueryComparisonPredicate` ScriptDomu (`SOME` v něm je `Any`). HQL a JPQL čtou `all`/`any`/`some` mezi operátorem a závorkou poddotazu; za kvantifikátorem smí stát jen poddotaz, cokoli jiného je syntaktická chyba s pozicí. LINQ čte **`řetěz.All(p => vnější op p.X)`** — strana s parametrem lambdy se stane `Select` řetězu, druhá je hodnota vnějšího rozsahu, čtená ve vnějším rozsahu *před* otevřením vnořeného, aby ji korelovaný poddotaz nevzal za svůj sloupec — a **`řetěz.Select(x => x.X).All(v => vnější op v)`**, kde prvek je projektovaná hodnota sama. `Any` nad řetězem zakončeným `Select` se čte stejně jako `ANY`; `Any(predikát)` nad neprojektovaným řetězem zůstává čtením rozhodnutí 061, `Where(predikát).Any()` — vybírá tytéž řádky a změna by přepsala každý korelovaný `EXISTS` ve stávajících testech na `IN`.

**Co se odmítá, a proč.**

- **`All(predikát)`, který není jedním relačním porovnáním s parametrem lambdy právě na jedné straně** (`All(o => o.Total > 1 && o.CustomerID == c.CustomerID)`, `All(o => o.Total > o.OrderID)`, `All(o => c.Credit > 1)`, `All(v => c.Credit > v.Value)` nad projekcí) je `Failure` se jménem tvaru. Jediné, co by z něj šlo udělat, je `NOT EXISTS` nad negovaným predikátem — a negace složeného predikátu nad `NULL` vybírá v SQL jiné řádky než v C#, a to pro oba poskytovatele různě. Model nehádá (070).
- **Kvantifikátor na jiném tvaru než relační porovnání s poddotazem vpravo** odmítá brána šablony jednou za všechny cíle (`QueryFeature.Subquery`); továrna takový strom ani nepostaví.
- **`ALL`/`ANY` nad poddotazem, který projektuje neseskupený agregát** (`x > ALL (SELECT MAX(v) …)`), jde do LINQ únikovou cestou nativním SQL (113): je to pravidlo pravé strany `IN` z rozhodnutí 061, které kvantifikované porovnání dědí s pozicí — `Select` LINQ neseskupený agregát nenese. Textové cíle ho píší.
- **Koncové `All()`/`Any()` jako dotaz sám** (`ctx.Orders.All(…)` vrací `bool`) zůstává `Failure` jmenovitě — není to dotaz vracející řádky.

## Důsledky

- **Model:** `Quantifier` (enum), `ComparisonCondition.Quantifier`, továrna `Quantified` se skládáním do `IN`/`NOT IN`, `NegatedQuantified()`, rozšíření `IsRelational()`, `Negated()`, `Mirrored()` nad `ComparisonOperator`. `IQueryVisitor` beze změny.
- **Šablona:** brána kvantifikátoru vedle brány `EXISTS`; `NormalizeSubQueryOperand` beze změny (pravidlo jedné projekce platí i tu). Sdílený pomocník `QuantifiedComparisons` (pravopis, text záznamu o přepisu) v `AbstractWrappers`, aby čtyři čtečky hlásily jednou větou.
- **Čtečky:** T-SQL, HQL, JPQL, LINQ, jak výše; `any` a `some` jsou klíčová slova HQL i JPQL (lexer).
- **Zápis:** `SqlQueryVisitor`, `NHibernateHqlQueryVisitor`, `JpqlQueryVisitor` píší slovo; `LinqQueryVisitor` přepis, De Morgan, `CompensatesNullSemantics` (EF Core pravda, NHibernate nepravda), `EntityByName` od builderu.
- **Matice T2:** dvě nové kategorie nad jediným nulovatelným sloupcem domény, `ShopDepartment.ParentDepartmentId` — `QuantifiedComparisonOverAll` (`<` `ALL` korelovaně, kanonicky jeden řádek: oddělení 9, nad nímž je poddotaz prázdný; oddělení 6 má v poddotazu `NULL` a trojhodnotová logika ho vyřadí) a `QuantifiedComparisonOverAny` (`< SOME`, čtyři řádky) — v `categories.txt` ze všech šesti zdrojů a v diferenční matici s mutacemi `filter` a `operator`. Čtvrtý stupeň nad první z nich měří přesně ten řádek, na kterém se `ALL` a dvouhodnotové `All()` rozcházejí.
- **Testy (`Combined/QuantifiedComparisonTest`):** čtení SQL, HQL, JPQL (`all`, `any`, `some`) a LINQ (oba tvary, zrcadlení, `Any` nad projekcí); zápis do všech čtyř jazyků; skládání `= ANY`/`<> ALL` do `IN` se záznamem; De Morgan při negaci; testy na `NULL` v druhém tvaru NHibernatu a jejich nepřítomnost u EF Core i u `ANY`; odmítnutí `All` nad jiným predikátem jmenovitě, brána šablony, továrna.
- **Dokumentace:** `architecture.md` §4.4 (uzel porovnání, operátory) a §5 (řádek poddotazu); `subset.md` kategorie a §2.7; `traceability.md` počty kategorií.
