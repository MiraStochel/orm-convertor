# 162 — Poddotaz stojí v podmínce joinu jako v každé jiné podmínce

Datum: 2026-10-22
Stav: platí; nahrazuje odrážku rozh. 061 o klauzuli `ON` joinu a větu rozh. 112, že poddotaz v podmínce `ON` zůstává odmítnutý
Požadavky: F7–F11, F13, T2, T3, S1, S2
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [061](061-subquery-as-a-condition-operand.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [112](112-a-query-as-a-row-source-is-a-named-intermediate-result.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [118](118-nhibernate-writes-a-linq-form-beside-the-hql-that-binds.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md); JSS §5.3 (operandy podmínky: „constants, attributes, or nested subqueries") a pravidla Q6, Q11, Q15; katalog FIBEN q124–126 a q200–202; sondy proti připnutým verzím z 2026-10-22 (`Tests/NHibernate/NHibernateHqlProviderTest`, `Tests/EFCore/EFCoreJoinSubqueryProviderTest`, javová sada `hibernate/WindowAggregateAndJoinSubqueryClaimsTest` a `eclipselink/JoinSubqueryClaimsTest`); otevřená položka „Co katalog FIBEN odmítá: okenní agregát a stínovaný alias odvozené tabulky"

## Kontext

Rozhodnutí 061 udělalo z poddotazu třetí tvar operandu a vyslovilo pozice taxativně: vykresluje se jako operand porovnání ve `WHERE` a `HAVING`, a **klauzule `ON` joinu je `Failure` v bráně úplnosti**, protože „LINQ join bere dva klíčové selektory a poddotaz do nich nevepíše, a zahodit join s podmínkou znamená jiné řádky". Rozhodnutí 112 větu zopakovalo. Brána šablony od té doby odmítá každý join, jehož podmínka nese poddotaz, pro všech šest cílů.

Katalog FIBEN má šest takových dotazů: q124–126 a q200–202 píší `INNER JOIN … ON klíč = klíč AND sloupec [NOT] IN (SELECT …)`, dva z nich korelovaně na alias vnějšího dotazu. Katalog je vede jako `NotTranslated` s poznámkou o „skalárním poddotazu v podmínce `ON` vnějšího joinu" — poznámka je věcně špatně, join je vnitřní a poddotaz množinový; opravuje se s tímhle rozhodnutím.

Z důvodů rozhodnutí 061 dnes neplatí ani jeden. **Klíčové selektory nejsou jediný tvar joinu v LINQ:** od rozhodnutí 113 píše builder EF Core join, jehož podmínka jde nad rámec rovností sloupců, jako korelovaný `SelectMany` s `Where` nad spojovanou posloupností, a ve `Where` LINQ poddotaz vyslovuje — `Contains` a `Any` — od rozhodnutí 061 samého. **A kde jazyk cíle tvar nemá, cíl ustupuje** (rozhodnutí 113), neodmítá. Co zbylo, je otázka, zda cíle poddotaz v podmínce joinu vysloví a zda vrátí tytéž řádky; to se změřilo 2026-10-22 proti připnutým verzím:

- **NHibernate 5.7.0** (`NHibernateHqlProviderTest`, plán dotazu bez spojení): `with` entitního joinu přijímá `in (select …)`, `not in`, `exists` i korelovaný poddotaz, u vnitřního i levého joinu, a píše je do `ON` SQL.
- **EF Core 10.0.10** (`EFCoreJoinSubqueryProviderTest`, `ToQueryString()`): korelovaný `SelectMany`, jehož predikát nese `Contains` nad řetězem nebo `Any`, překládá na `CROSS APPLY` — nad `DefaultIfEmpty()` na `OUTER APPLY` — s poddotazem ve `WHERE` aplikované množiny: tytéž řádky jako vnitřní, resp. levý join s tou podmínkou.
- **Hibernate 7.4.5** (`WindowAggregateAndJoinSubqueryClaimsTest`, spuštěno nad SQL Serverem): `on` entitního joinu s `in`, `not in`, `exists` a korelací vrací tytéž řádky jako T-SQL, u vnitřního i levého joinu.
- **EclipseLink 5.0.0** (`JoinSubqueryClaimsTest`, spuštěno nad SQL Serverem): totéž ve všech pěti tvarech. Vydání, které přestavuje `HAVING` na kořen (rozhodnutí 149), podmínku `ON` nepřestavuje; řádky se shodují.

## Zvažované varianty

### 1 — Ponechat odmítnutí

Zamítáme: oba důvody rozhodnutí 061 padly a měření ukazuje, že každý ze šesti cílů tvar vysloví vlastním jazykem. Odmítat tam, kde framework odpověď má, je vada, kterou rozhodnutí 065 vytklo odmítnutí plného vnějšího joinu.

### 2 — Přesunout poddotazový konjunkt do `WHERE` ve čtečce

U vnitřního joinu, za kterým nestojí pravý ani plný join, je konjunkt v `ON` tatáž množina řádků jako ve `WHERE`, a čtečka by ho tam mohla přesunout. Zamítáme ze dvou důvodů. Mění to strukturu podmínek, kterou pravidlo Q15 zachovává, a cíl SQL by vydal jiný text, než zdroj napsal — přepis tam, kde jde nést přímo. A vnější join nepokryje vůbec: v levém joinu podmínka v `ON` rozhoduje, zda se řádek spáruje, ve `WHERE` zda zůstane.

### 3 — Poddotaz je operand i v podmínce joinu

## Rozhodnutí

**Volíme variantu 3. Poddotaz je operand porovnání v podmínce joinu stejně, jako je operandem ve `WHERE` a `HAVING`: `IN (SELECT …)`, `EXISTS (…)`, skalární porovnání i korelace na alias vnějšího rozsahu. Brána šablony pozici přestává odmítat a vnořený rozsah se vykresluje týmž zpětným voláním builderu, jakým se vykresluje v filtru. Každý ze šesti cílů tvar píše vlastním jazykem; nic neustupuje.**

### Jak který cíl píše

- **Dapper a MyBatis** (T-SQL): `ON … AND x IN (SELECT …)`, jak zdroj napsal.
- **Hibernate** (HQL 7.4) a **EclipseLink** (JPQL 5.0): `join X x on … and x in (select …)` — ověřeno v obou.
- **NHibernate** (HQL 5.7): `join X x with (… and x in (select …))` — ověřeno.
- **EF Core** (LINQ): konjunkt s poddotazem je podmínka nad rámec rovností klíčů, a tedy korelovaný `SelectMany` rozhodnutí 113 s `Contains`/`Any` v predikátu; konjunkt, který jmenuje jen spojovaný řádek, filtruje spojovanou posloupnost před joinem jako dosud. Pravý a plný join s podmínkou nad rámec rovností ustupují jako dosud. Parametr vnořené lambdy se přejmenuje, koliduje-li s parametrem `SelectMany` (rozhodnutí 061).
- **Druhý tvar NHibernatu** (LINQ, rozhodnutí 118): týž řetěz; co provider 5.7.0 nepřeloží, je `Omitted` podle 118 a měří ho `NHibernateLinqProviderTest`.

### Kdo čte

Čtečka T-SQL čte podmínku joinu touž gramatikou podmínek jako `WHERE`, a tak už dnes poddotaz v ní čte; parsery HQL a JPQL také. Sdílený parser LINQ čte korelovaný `SelectMany` s `Contains`/`Any` v predikátu jako join s poddotazem v podmínce — tvar, který cíl EF Core vydává, se čte zpět jako týž dotaz.

### Co se nemění

`IQueryVisitor`, tvary operandu, osm kroků šablony. Mechanické kontroly poddotazu (`NormalizeSubQueryOperand`: tělo-množinová-operace, počet projekcí) platí v podmínce joinu stejně. Join na definici má `ON` nad sloupci (rozhodnutí 112) a poddotaz v něm je poddotaz jako jinde. Laterální odkaz definice na dotaz kolem (rozhodnutí 112) zůstává odmítnutý: korelace poddotazu v `ON` míří na aliasy rozsahu, ve kterém join stojí, ne ven z definice.

## Důsledky

**Odrážka rozhodnutí 061 o klauzuli `ON` přestává platit; 061 samo platí dál** — poddotaz je operand, jen má o pozici víc. Totéž pro větu rozhodnutí 112.

**Katalog FIBEN se měří znovu**: q124–126 a q200–202 přecházejí v překlad do všech šesti cílů a poznámka katalogu říká, co dotazy skutečně píší. Co nese který dotaz, drží `FibenCatalogTest`.

**Matice T2 dostává kategorii** poddotazu v podmínce joinu ze všech šesti zdrojů, bez ústupu.

**Pro sedmý framework se nemění nic** (S1): podmínku joinu dostává od šablony normalizovanou včetně vnořených rozsahů a vykresluje ji visitorem, jak vykresluje filtr. Determinismus (S2) drží.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR**: vstupy, které končily `Failure`, nově vydají artefakt; veřejná plocha se nemění.

**Testy.** Poddotaz v podmínce vnitřního i levého joinu — `IN`, `NOT IN`, `EXISTS`, korelace — z T-SQL do všech šesti cílů a zpět z HQL, JPQL a LINQ; druhý stupeň nad EF Core (`ToQueryString()`) a NHibernatem (plán HQL); čtyři sondy drží měřené fakty cílů.
