# 180 — Plný vnější join do EclipseLinku jde nativním SQL: JPQL ho nemá a EclipseLink 5.0.0 ho odmítne, HQL Hibernatu ho má

Datum: 2026-10-24
Stav: platí
Požadavky: F9, F10, F11, T2, T3
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [076](076-java-frameworks-through-a-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md) a [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md); [první záznam experimentálního běhu](../runs/2026-10-24-experiment.md); Jakarta Persistence 3.2 §4.4.5 (`join` je `[LEFT [OUTER] | INNER]`); položka *`full join` v JPQL pro EclipseLink* v `open-items.md`

## Kontext

Sdílená vrstva JPA píše jeden dotazový jazyk pro oba profily a `JpqlQueryVisitor` zapisuje druh joinu doslova: `join`, `left join`, `right join`, `full join`. Jakarta Persistence 3.2 zná v gramatice `join` jen vnitřní a levý vnější (§4.4.5); pravý a plný jsou rozšíření implementace. Hibernate 7.4.5 je v HQL má obě, a tak se nikdy neukázalo, že totéž slovo do druhého profilu nedojde: [experimentální běh z 2026-10-24](../runs/2026-10-24-experiment.md) změřil, že dotaz s `full join` ze zdroje Hibernate (příklad `marketplace-query-tour`) vyjde do EclipseLinku jako JPQL s `full join` a **EclipseLink 5.0.0 ho při vytvoření odmítne syntaktickou chybou**, zatímco round-trip Hibernate → Hibernate téhož běhu týž dotaz přijal. Deskriptor JPA přitom hlásí `JoinKind` jako vyjádřitelný pro oba profily, takže řádek stojí v tabulce T3 jako *nepřijatý* — vada.

Otázka, co cíl udělá s konstrukcí, kterou jeho dotazový jazyk nemá, je rozhodnutá jednou pro všechny cíle (rozhodnutí 113): místo emise hlásí, že tvar *jazyk cíle nemá*, a šablona builderu z toho udělá únikovou cestu nativním SQL, má-li ji cíl, jinak `Failure`. Právě plný vnější join byl v 113 jmenovaným příkladem pro HQL NHibernatu, jehož vizitor hlásí `Fallback` kategorie `JoinKind` a dotaz vychází jako `CreateSQLQuery` s `FULL JOIN`; starší odmítnutí z rozhodnutí 065 tím padlo. EclipseLink má únikovou cestu stejnou (`createNativeQuery`, rozhodnutí 113) a používá ji pro desítku jiných tvarů, které jeho implementace nenapíše (`DropsJoinsInSubqueries`, `InnerJoinsFollowOuterJoins`, `RebuildsHavingOnTheRoot`, …). Co chybí, je jen fakt profilu, že plný join je jedním z nich.

## Zvažované varianty

1. **Odmítnout** (`Failure` bez artefaktu), jak pro NHibernate rozhodlo 065. Zamítáme: 065 odmítalo, protože HQL 5.7 neměl ani z čeho plný join složit, a 113 to pro NHibernate nahradilo únikovou cestou právě proto, že odmítnutí jako jediná odpověď měří hlavně naše odmítnutí. Vrátit se k odmítnutí u jednoho cíle, když ostatních pět plný join vydají, by bylo šesté rozhodnutí o jedné otázce proti tomu, co 113 sjednotilo.

2. **Složit plný join z levého a pravého joinu s množinovou operací**, jak to dělá EF Core (rozhodnutí 065). JPQL EclipseLinku množinové operace má, ale `UNION ALL` dvou entitních joinů se spojenými členy EclipseLink 5.0.0 nenapíše — vypouští joiny členů množinové operace z SQL (profil `DropsJoinsInSetOperations`, změřeno katalogem FIBEN), takže skladba by vrátila jiné řádky bez chyby. Zamítáme.

3. **Plný vnější join do EclipseLinku jde únikovou cestou rozhodnutí 113:** `JpqlQueryVisitor` pod profilem, který plný join nebere, hlásí `Fallback` kategorie `JoinKind` a šablona dotaz vydá celý nativním SQL dialektu cíle přes `createNativeQuery`.

## Rozhodnutí

**Volíme variantu 3.** Profil implementace dostává změřený fakt `RefusesFullJoin`: EclipseLink 5.0.0 `true` (parser Hermes plný join odmítne syntaktickou chybou), Hibernate 7.4.5 `false` (HQL ho má). `JpqlQueryVisitor.Visit(JoinInstruction)` pod takovým profilem u `JoinKind.Full` hlásí `Fallback` (`JoinKind`) týmž kanálem a s touž větou, jakou má vizitor HQL NHibernatu — *JPQL v EclipseLinku nemá plný vnější join* —, a vrací prázdný text; šablona `AbstractQueryBuilder` z toho udělá nativní SQL (rozhodnutí 113), které `FULL JOIN` má a vrací tytéž řádky jako zdroj. Pravý vnější join, který specifikace také nezná, toto rozhodnutí neměří a nemění: žádný vstup běhu ho do EclipseLinku neposlal, a co není změřené, se nerozhoduje.

Zdrojová strana se nemění: `JpqlQueryParser` čte `full join` dál, protože je to legitimní vstup z HQL pro cíl, který ho má.

**Měření drží test vedle háčku:** `eclipselink/FullJoinClaimsTest` (dotaz s `full join` odmítne `createQuery`, týž dotaz s `left join` přijme) a `hibernate/FullJoinClaimsTest` (Hibernate 7.4.5 `full join` přijme) v javové sadě; zápis drží `EclipseLink/EclipseLinkQueryTest` (nativní SQL se záznamem) a `Hibernate/HibernateJpqlQueryBuilderTest` (JPQL `full join`).

## Důsledky

- **Řádek `marketplace-query-tour` → EclipseLink se v T3 posouvá z *nepřijatého* na *přijatý s nativním SQL*** — přesně do sloupce *nativní SQL*, kterým záznam vede ostatní úniky, a kategorie `JoinKind` zůstává v deskriptoru vyjádřitelná: vnitřní, levý a pravý join JPQL EclipseLinku má, jen plný nikoli, a místo emise je to, kde se tento rozdíl vysloví (rozhodnutí 113, *V místě emise*).
- **[`subset.md`](../subset.md)** v tabulce *Konstrukce po cílech* dostává u EclipseLinku „plný SQL“, jako má NHibernate; **`architecture.md`** v odstavci o joinech, které EclipseLink 5.0.0 nenapíše, a v tabulce profilů.
- **Dotčená místa:** `JpaImplementationProfile`, `EclipseLinkDescriptor.Profile`, `HibernateDescriptor.Profile`, `JpqlQueryVisitor`. Model, deskriptory a orchestrace se nemění.
