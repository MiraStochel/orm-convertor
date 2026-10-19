# 156 — Predikát LINQ nese trojhodnotovou logiku SQL v obou směrech: zapisovač píše, kde podmínka platí, a negaci tlačí k porovnáním; čtečka čte, co z predikátu udělá jeho provider

Datum: 2026-10-19
Stav: platí
Požadavky: F7, F10, F13, T2, T3, S1
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [040](040-boundary-of-the-handed-over-artifact.md) (nastavení kontextu je věc konzumenta), [061](061-subquery-as-a-condition-operand.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md) (co smí být `NULL`: pravidlo stráže konkatenace), [118](118-nhibernate-writes-a-linq-form-beside-its-hql.md), [119](119-quantified-comparison-as-a-comparison-with-a-quantifier.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md); běh katalogu FIBEN z 2026-10-18 (q150, q155); měření 2026-10-19 proti zafixovaným verzím: EF Core 10.0.10 přes `ToQueryString()` (`EFCore/EFCoreNullSemanticsProviderTest`) a provider LINQ NHibernate 5.7.0 jeho vlastním plánem dotazu (`NHibernate/NHibernateLinqProviderTest`)

## Kontext

Podmínka SQL, HQL i JPQL má tři hodnoty: porovnání s `NULL` je *unknown* a `WHERE`, `HAVING`, `ON` i `CASE WHEN` berou jen *true*. Predikát C# má dvě: `null == null` je `true`, `null != 5` je `true`, `!(null > 5)` je `true`. Providery LINQ tu dvouhodnotovou logiku přenášejí do SQL, které píší (*null semantics compensation*), a nástroj s tím nepočítal ani při zápisu, ani při čtení. Změřeno nad entitou s `int? Score`, `int? Rank`, `int Level` a `string? Name`:

| Predikát C# | EF Core 10.0.10 | NHibernate 5.7.0 |
|---|---|---|
| `c.Score == 5`, `c.Score < c.Rank`, `c.Score <= 5` | `Score = 5`, `Score < Rank`, `Score <= 5` | totéž |
| `c.Score != 5` | `Score <> 5 OR Score IS NULL` | totéž |
| `c.Score == c.Rank` | `Score = Rank OR (Score IS NULL AND Rank IS NULL)` | totéž |
| `c.Score != c.Rank` | `(Score <> Rank OR Score IS NULL OR Rank IS NULL) AND (Score IS NOT NULL OR Rank IS NOT NULL)` | `Score<>Rank or case … <> case …` |
| `c.Level != c.Score` | `Level <> Score OR Score IS NULL` | totéž |
| `c.Score == ctx…Max(o => o.Total)` (`Total` bez `NULL`) | `= (SELECT MAX …) OR (Score IS NULL AND (SELECT MAX …) IS NULL)` | totéž |
| `!(c.Score == 5)` | `Score <> 5 OR Score IS NULL` | `not (Score = ?)` |
| `!(c.Score > 5)` | `CASE WHEN Score > 5 THEN 0 ELSE 1 END = 1` | `not (Score > ?)` |
| `!new int?[] { 1, 2 }.Contains(c.Score)` | `Score NOT IN (1, 2) OR Score IS NULL` | `not (Score in (?))` |
| `!c.Name.StartsWith("A")` | `Name NOT LIKE N'A%' OR Name IS NULL` | `not (Name like …)` |
| `ctx…Select(o => o.Rating).Contains(c.Score)` | `EXISTS (… Rating = Score OR (Rating IS NULL AND Score IS NULL))` | `Score in (select Rating …)` |
| `….All(v => c.Score != v)`, `….Any(v => c.Score == v)` | porovnání v lambdě kompenzované | totéž |
| `c.Score == c.Rank ? 1 : 0` | `CASE WHEN Score = Rank OR (… IS NULL AND … IS NULL) …` | totéž |
| `Join(…, c => c.Score, o => o.Rating, …)` | `ON Score = Rating` | `on (Rating=Score)` |
| `c.Score != 5 && c.Score != null` | `Score IS NOT NULL AND Score <> 5` | `Score<>? and Score is not null` |
| `c.Score == c.Rank && c.Score != null` | `(Score = Rank OR (… IS NULL AND … IS NULL)) AND Score IS NOT NULL` | `Score=Rank and Score is not null` |
| `c.Score != null && ….Select(o => o.Rating).All(v => v != null && v != c.Score)` | `Score IS NOT NULL AND NOT EXISTS (… Rating IS NULL OR Rating = Score)` | `not exists (… where not (Rating is not null and …))` |

Zapisovač LINQ psal podmínku mezireprezentace doslova (`!=`, `==`, `!(…)`), takže cíl EF Core i druhý tvar NHibernatu vybíraly řádky, které SQL zdroje vyřadí: `WHERE x <> 'A'` nad sloupcem, který smí být `NULL`, vrátil v EF Core i řádky s `NULL`; `HAVING MAX(a) = (SELECT MAX(b) …)` katalogu FIBEN (q150, q155) vrátil 2 737 a 2 363 skupin, kde text nevrací žádnou, protože vnitřní `MAX` je nad daty `NULL` a skupin bez částky je 2 737. Čtečka LINQ četla týž predikát jako podmínku SQL a dělala tutéž chybu obráceně: `Where(c => c.Score != 5)` zdroje EF Core, který řádky s `NULL` vrací, přeložila do `WHERE c.Score <> 5`, které je vyřadí. Obojí je dotaz, který vrací jiné řádky beze slova (053).

## Zvažované varianty

1. **Vyslovená mez obou cílů LINQ a obou zdrojů LINQ.** Zamítáme: `<>` a negace nad sloupcem, který smí být `NULL`, nejsou okrajová konstrukce, a mez by vyňala z nároku velkou část filtrů nad nepovinnými sloupci.

2. **`UseRelationalNulls()`** — přepínač EF Core, s nímž provider píše porovnání doslova jako SQL. Je to nastavení kontextu, které rozhodnutí 040 nechává konzumentskému projektu; artefakt ho nevysloví a převod by ho musel předepsat. NHibernate žádný takový přepínač nemá. A čtení zdroje by to neopravilo: zdroj EF Core je psaný s kompenzací, kterou jeho projekt má. Zamítáme.

3. **Nativní SQL** pro každý dotaz s porovnáním nad hodnotou, která smí být `NULL`. Správné, ale cíl LINQ by opouštěl svůj jazyk u filtrů, které jazyk vysloví — rozhodnutí 113 vyhrazuje únikovou cestu tomu, co jazyk nemá. Zamítáme.

4. **Zápis „kde podmínka platí" a čtení podle providera.** Podmínka SQL se píše jako predikát C#, který je pravdivý právě tam, kde je podmínka *true*, a pod `NOT` jako predikát pravdivý právě tam, kde je *false*; negace se tlačí k porovnáním De Morganem jako u kvantifikátoru (119), takže provider nedostane `!` nad ničím, co by kompenzoval, a tentýž text platí pro EF Core i NHibernate. Čtečka čte predikát tak, jak ho provider zdroje přeloží, a tam, kde mezireprezentace tvar nenese přesně, dotaz odmítne (070). Tuhle volíme.

## Rozhodnutí

**Volíme variantu 4.**

**Zápis (cíle EF Core a druhý tvar NHibernatu).** Hodnota *smí být `NULL`* podle téhož pravidla jako u stráže konkatenace (107): sloupec, za který mapování neručí, i `NOT NULL` na volitelné straně vnějšího joinu, agregát nad takovým, poddotaz, výraz nad takovou hodnotou; literál a parametr ne. *Test na `NULL`* je `x != null`, u hodnoty, kterou C# typuje jako nenulovatelnou — člen tak deklarovaný na volitelné straně vnějšího joinu, agregát nad ním, skalární poddotaz —, zvednuté do nullovatelného typu (`(decimal?)… != null`), což provider píše jako `IS NOT NULL` sloupce či poddotazu.

| Podmínka | Kde platí (`true`) | Kde neplatí (`false`, pod `NOT`) |
|---|---|---|
| `a = b` | `a == b`, smí-li být `NULL` obě strany, `&& a != null` | `a != b` a test každé strany, která smí být `NULL` |
| `a <> b` | `a != b` a test každé strany, která smí být `NULL` | jako `a = b` výš |
| `a < b`, `<=`, `>`, `>=` | `a < b` (C# je nad `null` nepravdivé, provider píše `<`) | obrácený operátor, `a >= b` |
| `a IS NULL` | `a == null` | `a != null` |
| `a LIKE p` | dosavadní zápis | jeho negace `&& a != null` |
| `a IN (seznam)`, `IN` parametru | `seznam.Contains(a)` | `!seznam.Contains(a) && a != null` |
| `a IN (poddotaz)` | `poddotaz.Contains(a)`, smí-li být `NULL` obě strany, `&& a != null` | smí-li být `NULL` projektovaná hodnota: `a != null && poddotaz.All(v => v != null && v != a)`; jinak `!poddotaz.Contains(a)` s testem `a` |
| `EXISTS` | `.Any()` | `!….Any()` |
| `x op ALL`, `x op ANY` (119) | `All`/`Any` nad tím, kde porovnání platí | De Morgan (119) a totéž |
| `NOT c` | kde `c` neplatí | kde `c` platí |
| `c AND d`, `c OR d` | `&&`, `\|\|` nad tím, kde platí | `\|\|`, `&&` nad tím, kde neplatí |

Uvnitř `All` druhého tvaru NHibernatu, jehož provider negaci nekompenzuje, dostane porovnání test každé strany, která smí být `NULL`, jako dosud (119). Klíče joinu (`Join(…, a => a.X, b => b.Y, …)`) oba providery porovnávají jako SQL a zůstávají.

**Čtení (zdroje EF Core a NHibernate LINQ).** Predikát se čte, jak ho přeloží provider zdroje: oba kompenzují `==` a `!=` nad hodnotami, které smí být `NULL`, EF Core navíc negaci a `Contains` nad poddotazem. Čtečka proto čte

- `a == b`, smí-li být `NULL` obě strany, jako `a = b OR (a IS NULL AND b IS NULL)`; `a != b` jako `a <> b OR a IS NULL` u strany, která smí být `NULL`, a nad dvěma takovými `a <> b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL)`;
- negaci v EF Core jako místo, kde predikát *neplatí*: `!(a > b)` je `a <= b OR a IS NULL OR b IS NULL`, `!seznam.Contains(a)` je `a NOT IN (…) OR a IS NULL`, `!s.StartsWith("A")` `s NOT LIKE 'A%' OR s IS NULL`, De Morgan přes `&&` a `||`; v NHibernatu jako `NOT` toho, co přečetla;
- `Contains` nad poddotazem, jehož hodnota smí být `NULL`, v EF Core jako `EXISTS` řádku, jehož hodnota se rovná hledané, `NULL` rovné `NULL` — `EXISTS (… WHERE v = x OR (v IS NULL AND x IS NULL))`, jak ho provider píše; jeho negaci jako `NOT EXISTS` téhož (`NOT IN` by byl nad každým `NULL` mezi hodnotami *unknown*); v NHibernatu jako `IN`;
- `All(v => x op v)` relačním operátorem v NHibernatu, smí-li být `NULL` některá strana, jako `NOT (x op' ANY …)` s obráceným operátorem — tak ho provider píše (`NOT EXISTS (… NOT (x op v))`).

Co mezireprezentace přesně nenese, se odmítá jmenovitě (070): `Contains` nad poddotazem, který seskupuje, stránkuje či skládá řádky, smí-li být `NULL` projektovaná hodnota (filtr shody by stál jinde); kvantifikátor s `==` či `!=` nad hodnotou, která smí být `NULL` (pod lambdou páruje `NULL` s `NULL`, kvantifikátor SQL ne); negace kvantifikátoru v EF Core nad takovou hodnotou. Podmínka, která neporovnává nic, co smí být `NULL`, se čte i píše jako dosud, `!` i `NOT` včetně — obě logiky se v ní shodují. Test skalárního poddotazu na `NULL`, který čtení přináší, píší T-SQL, HQL i JPQL poddotazem v místě operandu.

## Důsledky

- **Kód.** `LinqQueryVisitor` píše podmínku polaritou (`Holds`/`Fails`, `MeetsNoNull`), testy na `NULL` podle `IsNeverNull`; `Visit(NotCondition)` píše `!(…)` jen nad podmínkou, která nic `NULL` neporovná. `LinqQueryParser` převádí přečtenou podmínku podle háčků providera (`ProviderCompensatesNegation`, `ProviderMatchesNullInContains`; EF Core ano, NHibernate ne), `Contains` nad poddotazem s `NULL` čte jako `EXISTS` (`MatchingExists`) a `Where` po `GroupBy` čte jako podmínku kteréhokoli tvaru, protože tam zapisovač píše testy na `NULL` také. `SqlQueryVisitor`, `NHibernateHqlQueryVisitor` a `JpqlQueryVisitor` píší test skalárního poddotazu na `NULL`. Model, `AbstractWrappers` ani orchestrace se nemění (S1).
- **Testy.** Fakta providerů `EFCore/EFCoreNullSemanticsProviderTest` a `NHibernate/NHibernateLinqProviderTest`; čtvrtý stupeň nad fixturou `Combined/LinqNullSemanticsTest` v obou směrech (`<>`, negace, `NOT IN`, `NOT LIKE`, rovnost dvou hodnot s `NULL`, `NOT IN` nad poddotazem s `NULL`; zdroj EF Core proti řádkům, které vrátí sám) a překlad HQL plánem NHibernatu; v javové sadě `NullTestedSubqueryClaimsTest` (EclipseLink 5.0.0, Hibernate 7.4.5: `coalesce` nad poddotazem a agregátem, test poddotazu na `NULL`). FIBEN q150 a q155 se kontrolou z 2026-10-19 v EF Core shodují se zdrojem.
- **Text.** Filtr nad nepovinným sloupcem je v LINQ delší (`c.Score != 5 && c.Score != null`) a SQL přeložené ze zdroje EF Core také (`c.Score <> 5 OR c.Score IS NULL`); oboje je, co ty dotazy znamenají.
- **Zúžení.** Odmítnuté tvary jsou vyslovená mez v `subset.md` 2.7.
- **Dokumentace.** `architecture.md` §5 (zápis a čtení podmínky LINQ), `subset.md` 2.7 a 2.13, §9 a *Guarantees* kořenového `README.md`.
