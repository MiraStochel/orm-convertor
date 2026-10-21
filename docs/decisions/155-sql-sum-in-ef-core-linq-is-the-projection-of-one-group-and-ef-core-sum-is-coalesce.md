# 155 — `SUM` jazyka SQL píše cíl EF Core jako projekci nad jedinou skupinou a nad skupinou bez hodnoty jako `NULL`; `Sum` EF Core je `COALESCE(SUM(x), 0)` a tak se píše i čte

Datum: 2026-10-19
Stav: platí; zakončení `FirstOrDefault()` první odrážky *Rozhodnutí* a `TOP(1)` důsledku *Cena* nahrazuje 159
Požadavky: F7, F10, F13, T2, T3, S1
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) (seznam nad korelovaným poddotazem jako řetěz seskupený konstantou), [118](118-nhibernate-writes-a-linq-form-beside-its-hql.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md); běh katalogu FIBEN z 2026-10-18 (záznam *The FIBEN run* v `ORMConvertor/README.md`); měření 2026-10-19 proti zafixovaným verzím: EF Core 10.0.10 přes `ToQueryString()` (`EFCore/EFCoreSumProviderTest`) a provider LINQ NHibernate 5.7.0 jeho vlastním plánem dotazu (`NHibernate/NHibernateLinqProviderTest`)

## Kontext

`SUM` v SQL, HQL i JPQL sčítá hodnoty, které nejsou `NULL`, a nad žádnou takovou hodnotou — nad prázdným vstupem i nad samými `NULL` — je `NULL`. `Enumerable.Sum` je nad žádnou hodnotou `0` a EF Core tu sémantiku přenáší do SQL. Změřeno nad entitami s `decimal Total` a `int? Rating`:

| Řetěz EF Core 10.0.10 | SQL |
|---|---|
| `….Where(korelace).Sum(o => o.Total)` jako skalární poddotaz | `(SELECT COALESCE(SUM([o].[Total]), 0.0) …)` |
| totéž s prvkem zvednutým, `Sum(o => (decimal?)o.Total)` | totéž |
| `g.Sum(e => e.Rating)` nad skupinou, i se zvednutým prvkem | `COALESCE(SUM([o].[Rating]), 0)` |
| `….Max(o => o.Total)` jako skalární poddotaz | `(SELECT MAX([o].[Total]) …)` |
| `….GroupBy(o => 1).Select(g => g.Sum(e => e.Total)).FirstOrDefault()` | `COALESCE((SELECT TOP(1) COALESCE(SUM(…), 0.0) … GROUP BY [o0].[Key]), 0.0)` |
| totéž s projekcí `(decimal?)g.Sum(e => e.Total)` | `(SELECT TOP(1) COALESCE(SUM(…), 0.0) … GROUP BY [o0].[Key])` |
| `g.Count(e => e.Rating != null) > 0 ? (int?)g.Sum(e => e.Rating) : null` | `CASE WHEN COUNT(CASE WHEN [o].[Rating] IS NOT NULL THEN 1 END) > 0 THEN COALESCE(SUM([o].[Rating]), 0) END` |
| `q.Any() ? (decimal?)q.Sum(o => o.Total) : null` nad korelovaným `q` | `CASE WHEN EXISTS (SELECT 1 …) THEN (SELECT COALESCE(SUM(…), 0.0) …) END` |
| `g.Any(e => e.Rating != null) ? … : null` nad skupinou | `CASE WHEN EXISTS (SELECT 1 FROM [Order] … [o].[CustomerId] = [o0].[CustomerId] …)` — tabulka znovu, korelovaná klíčem skupiny |

Provider NHibernate 5.7.0 píše `Sum` holým `sum` v poddotazu (`(select (sum(order1_.Total)) …)`) i nad skupinou (`cast(sum(order0_.Rating) as INT)`), tedy se sémantikou SQL.

Sdílený zapisovač LINQ psal `SUM` mezireprezentace jako `Sum`, tedy u EF Core jako `COALESCE(SUM(x), 0)`. Porovnání se součtem, který nemá co sčítat, pak vybere jiné řádky beze slova: dotaz typu JA katalogu FIBEN s `HAVING SUM(a) > (SELECT SUM(b) … korelovaně)` ponechá skupinu bez vnitřních řádků (porovnání s nulou), kterou SQL vyloučí (porovnání s `NULL`) — q207 vrací z textu 1 řádek a z EF Core 211, q211 a q213 podobně. Totéž platí v projekci (`0` za `NULL`) a nad skupinou, jejíž hodnoty jsou samé `NULL`. Dotaz se přeloží, zkompiluje i spustí a odpoví jinak; rozhodnutí 053 ho vydat zakazuje.

Opačný směr nese týž rozdíl: holý `Sum` zdroje EF Core znamená `COALESCE(SUM(x), 0)`, čtečka LINQ ho čte jako `SUM(x)` a cíle SQL, HQL a JPQL vrátí nad prázdným vstupem `NULL`. Druhý tvar NHibernatu ani zdroj NHibernate vadu nenesou, ačkoli to položka seznamu o nich tvrdila; záznam běhu FIBEN zas připsal téže vadě i q150 a q155, které v `HAVING` porovnávají rovností dva `MAX` a liší se jinou vadou: rovností dvou hodnot, které smí být `NULL` (rozhodnutí [156](156-a-linq-predicate-carries-sql-three-valued-logic-in-both-directions.md)).

## Zvažované varianty

1. **Vyslovená mez cíle EF Core** v `subset.md` 2.8 a známý rozdíl, který diferenční ověření jmenuje. Zamítáme. Mez by platila pro každý `SUM` ve skalárním poddotazu a nad každým sloupcem, který smí být `NULL` — pro běžný tvar výkazových dotazů, ne pro okrajovou konstrukci —, a dotaz by dál vracel jiné řádky, jen s poznámkou v katalogu. Diferenční ověření, které by rozdíl jmenovalo jako známý, by přestalo vidět i jeho další výskyty.

2. **Nativní SQL** (rozhodnutí 113) pro každý takový `SUM`. Výsledek by byl správný, ale EF Core by opouštěl svůj jazyk u konstrukce, pro kterou jazyk tvar má (varianta 4); rozhodnutí 113 vyhrazuje únikovou cestu tomu, co jazyk cíle nevysloví. Zamítáme.

3. **Stráž `Any()`**, `(q.Any() ? (T?)q.Sum(…) : null)`. Nad prázdným vstupem dá `NULL`, ale změřeně za cenu dvou průchodů: `CASE WHEN EXISTS (…) THEN (SELECT …)` opakuje korelovaný poddotaz i s jeho joiny. Samé `NULL` nepokryje bez dalšího filtru a nad skupinou se z `g.Any(…)` stane korelovaný poddotaz nad tabulkou znovu, ne test uvnitř agregace. Zamítáme.

4. **Projekce nad jedinou skupinou.** Skalární poddotaz s jediným `SUM` se píše jako řetěz seskupený konstantou, součet jako projekce té skupiny zvednutá do nullovatelného typu a `FirstOrDefault()` — tvar, který rozhodnutí 113 dalo seznamu nad korelovaným poddotazem a který má přesně potřebnou vlastnost: nad žádným řádkem skupina není a poddotaz dá `NULL`, jinak jeden průchod s agregací. Nad skupinou, jejíž hodnota smí být `NULL`, rozhoduje stráž počtu hodnot `COUNT(x) > 0`, kterou provider spočítá v témže průchodu agregací. Tuhle volíme.

## Rozhodnutí

**Volíme variantu 4. `SUM` mezireprezentace je `SUM` jazyka SQL a cíl, jehož provider `Sum` koalescuje, ho píše tvarem, který nad žádnou hodnotou dává `NULL`; `COALESCE(SUM(x), 0)` je naopak přesně `Sum` takového providera a píše se i čte jako on.**

- **Skalární poddotaz, jehož jedinou projekcí je `SUM` bez seskupení**, je `….GroupBy(x => 1).Select(g => (T?)g.Sum(e => …)).FirstOrDefault()`, kde `T` je typ součtu v C# (`int` pro celá čísla do `int`, `long`, `decimal`, `double`, `float`). Zvednutí je nutné: bez něj provider obalí poddotaz znovu `COALESCE(…, 0)`. Nezná-li nástroj typ součtu, jde dotaz nativním SQL (113).
- **`SUM` nad skupinou, jehož argument smí být `NULL`** — v projekci, v `HAVING`, v řazení i v jediné skupině poddotazu —, je `(počet hodnot > 0 ? (T?)g.Sum(e => …) : null)`, kde počet hodnot je zapsán jako `COUNT(x)` (test na `null`, na volitelné straně vnějšího joinu test řádku). Nad hodnotou, která `NULL` nenese, zůstává holý `g.Sum(…)`: skupina `GROUP BY` prázdná není.
- **`COALESCE(SUM(x), 0)`** — nad skupinou i nad skalárním poddotazem s jediným `SUM` bez seskupení, s nulou jakéhokoli číselného typu — je holý `Sum`, protože to je přesně on (katalog LDBC, IC 14, tak zůstává beze změny).
- **Čtečka LINQ** čte tvary prvních dvou bodů zpět jako `SUM(x)`, aby překlad z cíle EF Core zpět dal tutéž mezireprezentaci. Holý `Sum` zdroje EF Core čte jako `COALESCE(SUM(x), 0)`: ve skalárním poddotazu vždy, nad skupinou tam, kde argument smí být `NULL`.
- **Háček `CoalescesSum`** nese fakt providera (S1): EF Core ano, NHibernate 5.7.0 ne — jeho `Sum` je holé `sum`, druhý tvar 118 i čtení jeho zdroje zůstávají, jak jsou.

`AVG`, `MIN` a `MAX` EF Core nekoalescuje (změřeno), `COUNT` je nad prázdnou množinou `0` v obou jazycích; ty se nemění.

## Důsledky

- **Kód.** `LinqQueryVisitor`: háček `CoalescesSum`, zápis `SUM` se stráží a zvednutím, `COALESCE(SUM(x), 0)` jako holý `Sum`; `AbstractLinqQueryBuilder.OverOneGroup` (dřív `ListOverOneGroup`) píše seznam i `SUM` nad jedinou skupinou; `LinqQueryParser.ReadOneGroupSubQuery` (dřív `ReadListSubQuery`) a `ReadSumOverValues` je čtou zpět. Model, `AbstractWrappers` ani orchestrace se nemění (S1).
- **Testy.** Fakta providerů drží `EFCore/EFCoreSumProviderTest` a `NHibernate/NHibernateLinqProviderTest`; čtvrtý stupeň nad fixturou diferenční matice `Combined/SumOverNoValuesTest`: korelovaný poddotaz bez řádků, skupina samých `NULL` a tvar q207. FIBEN q207, q211 a q213 se kontrolou z 2026-10-19 v EF Core shodují se zdrojem.
- **Cena.** SQL EF Core má u každého `SUM` ve skalárním poddotazu odvozenou tabulku se seskupením a `TOP(1)` a u `SUM` nad hodnotou, která smí být `NULL`, podmíněný počet v téže agregaci.
- **Čtení holého `Sum` zdroje EF Core** dává cílům `COALESCE` nad skalárním poddotazem a nad agregátem skupiny. EF Core, Hibernate, MyBatis a Dapper je vyslovovaly už v katalogu LDBC (IC 14); EclipseLink 5.0.0 a Hibernate 7.4.5 je změřeně čtou i spouštějí (javová sada, `NullTestedSubqueryClaimsTest`), NHibernate 5.7.0 je překládá svým plánem dotazu (`Combined/LinqNullSemanticsTest`). Čtvrtý stupeň čtení drží týž test: dotaz zdroje EF Core vrátí tytéž řádky jako SQL, do kterého ho nástroj přečte.
- **Soudce FIBEN** vznikne znovu z běhu po této změně a po rozhodnutí 156 ([`open-items.md`](../open-items.md)).
- **Dokumentace.** `architecture.md` §5 (agregace LINQ) a §6.2 (běh FIBEN), `subset.md` 2.13, §9 a sekce *Guarantees* kořenového `README.md`.
