# 120 — Tělo poddotazového operandu smí být množinová operace: `IN (A UNION B)` a `EXISTS (A UNION B)` se nesou, cíl bez množinové operace jde únikovou cestou a skalární porovnání s takovým tělem do LINQ také

Datum: 2026-10-10
Stav: platí
Požadavky: F7–F10, F11, F13, T2, T3, S1, S2
Podklad: rozhodnutí [023](023-query-builder-template-method.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md), [061](061-subquery-as-a-condition-operand.md), [073](073-distinct-as-a-flag-of-the-query-scope.md), [112](112-a-query-as-a-row-source-is-a-named-intermediate-result.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) a [119](119-quantified-comparison-as-a-comparison-with-a-quantifier.md); JSS pravidla Q11 a Q12

## Kontext

Rozhodnutí 061 zavedlo poddotaz jako operand podmínky a mezi jeho pravidly vyslovilo: „**tělo poddotazu tvořené množinovou operací** (`IN (A UNION B)`) je `Failure`: operand nese jeden SELECT." Důvod byl v době rozhodnutí praktický — vykreslení operandu procházelo `Normalize` jednoho `SELECT`u a množinová operace měla vlastní cestu jen na vrcholu dotazu. Od té doby se obraz změnil třikrát. Rozhodnutí 112 dalo tělu pojmenovaného mezivýsledku právo být množinovou operací a buildery T-SQL, LINQ i JPA ji v té pozici vykreslují (`NormalizeDefinition(…, out setOperation)`). Rozhodnutí 113 dalo každému cíli únikovou cestu nativním SQL pro to, co jeho jazyk nevysloví, takže „cíl bez množinové operace" už není důvod odmítnout, ale důvod přepnout. A rozhodnutí 119 přidalo čtvrtý tvar operandu, kvantifikované porovnání, které stojí nad množinou hodnot stejně jako `IN`.

Čtečky přitom tělo-množinovou-operaci vyrábějí dávno: sdílená čtečka T-SQL čte `BinaryQueryExpression` uvnitř `IN (…)` i `EXISTS (…)`, parser LINQ čte `a.Union(b).Contains(x)` do téhož tvaru. Jediné místo, které ho zahazovalo, byl společný pomocník šablony `NormalizeSubQueryOperand` — záznamem `Failure`, tedy bez artefaktu, i pro cíle, které množinovou operaci v poddotazu vyslovují (T-SQL, LINQ EF Core, JPQL Hibernatu i EclipseLinku).

## Zvažované možnosti

### 1 — Nechat odmítnutí

Operand by dál nesl jeden `SELECT`. Zamítáme: pravidlo už neplyne z ničeho v modelu — `SubQueryInstruction` množinovou operaci jako tělo nese, buildery ji v pozici definice vykreslují — a odmítnout dotaz, který čtyři z pěti cílů napíší svým jazykem, je pravý opak toho, co rozhodnutí 113 pro slovník vyslovilo.

### 2 — Přepsat množinovou operaci v operandu na pojmenovaný mezivýsledek (112)

`IN (A UNION B)` by se četlo jako `WITH u AS (A UNION B) … IN (SELECT … FROM u)`. Zamítáme: přepis mění text všech cílů (i těch, které tvar píší na místě), přenáší na dotaz omezení mezivýsledků (NHibernate i EclipseLink je nemají a šly by únikovou cestou i tam, kde by `union` v poddotazu uměly), a rozhodnutí 112 varovalo před dvěma tvary jednoho faktu v opačném směru — tady bychom je zaváděli.

### 3 — Nést tělo-množinovou-operaci jako tělo operandu a vykreslit ho tam, kde cíl množinovou operaci má

Zvoleno. Popis níže.

## Rozhodnutí

**`NormalizeSubQueryOperand` vrací množinovou operaci zvlášť** (`out SetOperationInstruction? setOperation`), přesně jako `NormalizeDefinition` u mezivýsledku (112): klauzule jednoho `SELECT`u, nebo množinová operace, nebo nic se záznamem. Pravidlo 061 o **právě jedné projekci** pro `IN`, skalární i kvantifikované porovnání drží dál a nad množinovou operací se měří na **nejlevějším členu**, odkud SQL bere sloupce složeného výsledku; `EXISTS` ho nemá. `DISTINCT` nad tělem se skládá do operace podle identit rozhodnutí 073, jako u definice.

**Cíl, jehož jazyk množinovou operaci nemá, dostane v tomtéž místě záznam `Fallback`** a dotaz jde celý nativním SQL (113) — NHibernate (HQL 5.7). Rozhoduje deskriptor (`QueryFeature.SetOperation` = `NotExpressible`), ne builder, takže sedmý framework se zařadí sám. **EclipseLink 5.0.0 množinovou operaci na vrcholu dotazu čte, ale v závorce poddotazu ji odmítne syntaktickou chybou** („Syntax error parsing", parser Hermes) — změřeno javovou sadou nad kategorií `InOverASetOperation` ve třech směrech. Je to vlastnost implementace, ne jazyka JPQL, a tak ji nese profil implementace (`RefusesSetOperationsInSubqueries`, vedle `DropsJoinsInSubqueries`) a builder JPA pošle takový dotaz únikovou cestou; Hibernate 7.4.5 ho píše.

**Vykreslení** je v každém builderu totéž, co dělá na vrcholu dotazu, složené na jeden řádek: T-SQL `IN (SELECT … UNION SELECT …)`, JPQL `in (select … union select …)` v rámci aliasů obklopujícího dotazu, LINQ `a.Select(x => x.K).Union(b.Select(y => y.K)).Contains(o.K)` a `a.Union(b).Any()` ve vnořeném rámci, který operand sdílí s jedním `SELECT`em (`InNestedFrame`: odložení a návrat scope, visitoru a aliasů). Dva členy, které v LINQ materializují různé typy prvků, jdou únikovou cestou podle pravidla, které pro množinové operace platí už na vrcholu.

**Skalární porovnání s tělem-množinovou-operací** (`x = (SELECT MIN(a) … UNION SELECT MIN(b) …)`) jde do LINQ únikovou cestou: složené řádky nemají jeden koncový agregát, který by LINQ vzal (`.Max()` nad `Union` by byl jiný dotaz), a `First()` by tiše vybral řádek tam, kde SQL víc řádků odmítá (061). Textové cíle ho píší, jak stál.

**Čtení** se nemění u T-SQL a LINQ; parser JPQL nově čte množinový operátor i uvnitř závorky poddotazu (dvojice rozsahů, jako u těla definice). HQL NHibernatu množinovou operaci nemá, takže ji ani nečte.

## Důsledky

- Rozhodnutí 061 platí dál s jedinou výjimkou: odrážka o těle-množinové-operaci je tímto nahrazena; ostatní pravidla (jedna projekce, `EXISTS` bez ní, `ON` klauzule, projekce, zdroj ve `FROM` podle 112) zůstávají.
- Kód: `AbstractQueryBuilder.NormalizeSubQueryOperand` s `out`, `LeftmostMember`; `RenderSubQuery` čtyř builderů; `JpqlQueryParser.ParseSubQuery`; `AbstractLinqQueryBuilder.InNestedFrame` jako společný rámec jednoho `SELECT`u i množinové operace.
- Matice T2: kategorie `InOverASetOperation` (projekce, `IN` nad `UNION` dvou poddotazů) z pěti zdrojů — HQL ji nevysloví — s `fallbackBy = NHibernate:SetOperation, EclipseLink:SetOperation`, v diferenční matici s mutacemi `filter` a `operator`. Testy `Combined/SubQueryConditionTest`: nesení do SQL, LINQ a JPQL, úniková cesta HQL, `EXISTS` nad `UNION ALL` jako `Concat(…).Any()`, odmítnutí dvou sloupců nad nejlevějším členem, úniková cesta skalárního porovnání v LINQ.
- Dokumentace: `architecture.md` §5 (řádek poddotazu), `subset.md` 1.3 a 2.7, počty kategorií v `subset.md` a `traceability.md`.
