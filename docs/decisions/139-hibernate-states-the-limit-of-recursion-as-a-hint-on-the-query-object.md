# 139 — Hibernate vysloví limit rekurze databázovým hintem na objektu dotazu, takže dotaz s limitem zůstává v HQL

Datum: 2026-10-15
Stav: platí
Požadavky: F7, F11, F13, T2
Podklad: rozhodnutí [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) (odstavec *Kdo píše* — „HQL limit rekurze nemá, takže dotaz s limitem ustoupí i v Hibernatu"), [022](022-native-query-syntax-in-builders.md), [040](040-boundary-of-the-handed-over-artifact.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [060](060-pagination-as-a-query-instruction.md); profil `JpaImplementationProfile.InlineLiteralsCall` jako vzor volání na objektu dotazu; dvě sondy proti Hibernate 7.4.5.Final nad SQL Serverem 2025 Express 2026-10-15

## Kontext

Rozhodnutí 113 zavedlo rekurzivní definici a s ní limit rekurze jako fakt dotazu: `OPTION (MAXRECURSION n)` rozhoduje, jestli dotaz doběhne, nebo ho SQL Server po `n` úrovních zastaví chybou 530, takže se nikde nesmí ztratit. Dapper a MyBatis ho píšou do textu, EF Core, NHibernate a EclipseLink rekurzi vůbec nevysloví a píšou celý dotaz nativním SQL, kde limit stojí v textu také. Hibernate je jediný cíl, který rekurzi vysloví vlastním jazykem — `with x as (… union all …)` HQL 7.4 —, a přesto dotaz s limitem psal nativním SQL, protože HQL limit ve svém textu nemá. Zdroj, který napíše jedno číslo navíc, tak u Hibernatu přišel o celý strukturovaný tvar dotazu. V maticích kategorií to byla buňka `RecursiveWalkOfACyclicGraph` → Hibernate a Hibernate nebyl zdrojem téhle kategorie vůbec.

Text HQL ale není jediné místo, kde dotaz Hibernatu nese fakta: výřez je `setFirstResult` a `setMaxResults` na objektu dotazu (060) a EclipseLinku vypíná vázání literálů volání na objektu dotazu (`InlineLiteralsCall`, 113). Je proto třeba zodpovědět, jestli objekt dotazu Hibernatu limit rekurze unese tak, aby ho SQL Server dostal beze změny.

**Sonda 1** (javová sada, Hibernate 7.4.5.Final, `SQLServerDialect`, řetěz 150 hran, rekurzivní HQL bez limitu v textu):

| Objekt dotazu | SQL, které Hibernate pošle | Výsledek |
|---|---|---|
| bez hintu | `with Walk (…) as (…) select …` | chyba 530 po 100 úrovních |
| `addQueryHint("MAXRECURSION 5")` | `… select w2_0.NodeId from Walk w2_0 OPTION (MAXRECURSION 5)` | chyba 530 po 5 úrovních |
| `addQueryHint("MAXRECURSION 0")` | `… OPTION (MAXRECURSION 0)` | 150 řádků |
| `addQueryHint("MAXRECURSION 200")` | `… OPTION (MAXRECURSION 200)` | 150 řádků |
| `em.createQuery(…)` + `.unwrap(org.hibernate.query.Query.class).addQueryHint("MAXRECURSION 0")` | `… OPTION (MAXRECURSION 0)` | 150 řádků |

Klíče `HibernateHints` a `SpecHints` verze 7.4.5 (vypsané reflexí) žádný databázový hint nenesou; `org.hibernate.comment` je komentář, ne hint. **Sonda 2:** nad `createNativeQuery` bez `OPTION` v textu přidá `addQueryHint("MAXRECURSION 0")` klauzuli `OPTION (MAXRECURSION 0)` na konec nativního SQL také a dotaz vrátí 150 řádků. Řetěz `createQuery(…, X.class).unwrap(…).addQueryHint(…)` vrácený jako `TypedQuery<X>` se přeloží (surový typ, jen varování překladače).

## Zvažované varianty

### 1 — Ponechat nativní SQL (stav podle 113)

Správné, ale za cenu, kterou 113 samo odmítá jinde: nativní SQL tam, kde jazyk cíle konstrukci má, zahodí strukturovaný tvar, kvůli kterému se ORM vybírá, a matice by u Hibernatu měřila obal kolem textu Dapperu (022). Tady jazyk cíle konstrukci má celou; chybí mu jen místo pro číslo, které API frameworku nabízí. Zamítáme.

### 2 — Limit v HQL vypustit se záznamem `Loss`

Dotaz by zůstal v HQL, ale limit by se změnil na výchozích sto úrovní. Dotaz, který zdroj pustil hlouběji (`MAXRECURSION 0` nad hierarchií hlubší než sto), by v cíli skončil chybou, a dotaz, který zdroj zastavil dřív, by běžel dál. To je dotaz, který se chová jinak, ne ztráta ladění; 113 to vylučuje výslovně. Zamítáme.

### 3 — Hint přes `setHint` specifikace JPA

Generovaná metoda by zůstala čistě nad `jakarta.persistence.Query`. Hibernate 7.4.5 ale žádný klíč `setHint` pro databázový hint nemá (sonda 1) a neznámý klíč tiše ignoruje. Zamítáme — nic by to nevyslovilo.

### 4 — Nastavení konzumentova projektu (`StatementInspector`, vlastní dialekt)

Platilo by pro všechny dotazy továrny a vyžadovalo by kód mimo vygenerovaný artefakt; nastavení konzumenta je fakt jeho konfigurace (040), ne dotazu. Zamítáme.

### 5 — Databázový hint `addQueryHint` na objektu dotazu Hibernatu

## Rozhodnutí

**Volíme variantu 5. Dotaz, jehož rekurzivní definici zdroj ohraničil limitem, píše Hibernate v HQL a limit připojí k objektu dotazu voláním `.unwrap(org.hibernate.query.Query.class).addQueryHint("MAXRECURSION n")`, které jeho dialekt SQL Serveru napíše jako `OPTION (MAXRECURSION n)` na konec příkazu — tutéž klauzuli, kterou píše cíl Dapper. Volání stojí za vazbami parametrů a před výřezem, jako volání literálů EclipseLinku; holé HQL ho nenese, stejně jako nenese výřez. Čtečka Hibernatu čte `addQueryHint` s textem `MAXRECURSION n` jako limit rekurze — na dotazu v HQL i na nativním, protože Hibernate ho připojí k obojímu — a každý jiný databázový hint jako `Loss`, jako čtečka T-SQL čte ostatní hinty klauzule `OPTION`.**

### Proč to je překlad, a ne úniková cesta

Rozhodnutí 113 odlišuje překlad od únikové cesty podle toho, jestli dotaz vysloví *jazyk* cíle. Limit rekurze ale v SQL Serveru není součástí dotazového výrazu: je to hint příkazu, který stojí za ním v klauzuli `OPTION`, vedle `RECOMPILE` nebo `MAXDOP`. HQL nese dotazový výraz celý — definici, kotvu, rekurzivní člen i hlídání cyklu — a hint příkazu nese API, jako nese výřez. Stejné rozdělení drží model už od 060: co SQL Server píše v textu a JPQL na objektu dotazu, je pořád fakt dotazu, jen na jiném místě artefaktu. Buňka je proto *jazyk*, ne `Fallback`.

### Proč volání v profilu implementace

Volání patří Hibernatu, ne specifikaci: EclipseLink rekurzi nemá a jeho dotaz s rekurzí jde nativním SQL tak jako tak. Profil (`JpaImplementationProfile.RecursionLimitCall`) říká, jak implementace limit vysloví, a builder JPA podle něj odpoví `WritesRecursionLimit`; implementace bez volání se vrací k únikové cestě beze změny kódu, jako u `InlineLiteralsCall`. Šablona se nemění (S1).

### Čtení zpátky

Bez čtení by výstup Hibernatu přišel při překladu zpátky o limit, a tedy o to, jestli dotaz doběhne. Čtečka proto zná `addQueryHint` jako volání Hibernatu na objektu dotazu a jeho argument čte jako text hintu: řetězcový literál `MAXRECURSION n` (bez ohledu na velikost písmen) volá `LimitRecursion(n)`, cokoli jiného je `Loss` s textem hintu. Protože sonda 2 změřila, že Hibernate hint připojí i k nativnímu dotazu, platí totéž pro `createNativeQuery`.

### Co rozhodnutí neřeší

Limit rekurze není mez hloubky: SQL Server po jeho dosažení příkaz ukončí chybou, nevrátí to, co do té doby našel. Zjednodušení katalogu LDBC u IC 13, IC 14, BI 15, BI 19 a BI 20 proto nezmění — ta plynou z toho, že rekurzivní člen SQL Serveru neslučuje řádky (113, *Co rekurze v SQL Serveru neumí*), a žádný dotaz katalogu limit nenese.

## Důsledky

- **Kód:** `JpaImplementationProfile.RecursionLimitCall`; `HibernateDescriptor.Profile` ho nastavuje; `AbstractJpaQueryBuilder.WritesRecursionLimit` a `FinalizeText` volání připojí; `JpqlQueryParser` dostává druh volání `HintsDatabase` a `ReadDatabaseHint`, `HibernateJpqlQueryParser` mapuje `addQueryHint`.
- **Matice:** kategorie `RecursiveWalkOfACyclicGraph` jde do Hibernatu jazykem a Hibernate je jejím zdrojem (`hibernate/FindRecursiveWalkOfACyclicGraph.query.java`); `fallbackBy` ztrácí `Hibernate:Recursion`. Diferenční matice tak získává dvojice z Hibernatu a jednu dvojici nativního SQL vyměňuje za HQL.
- **Testy:** `Combined/RecursionTest` (zápis, žádný hint bez limitu, čtení zpátky, hint na nativním dotazu a jiný hint jako `Loss`); javová sada soudí kategorii na 2. a 3. stupni a diferenčně na 4.
- **Dokumentace:** `architecture.md` (rekurze, čtení volání na objektu dotazu, počty diferenční matice), `subset.md` (tabulka 1.2, 1.3, 1.4), `traceability.md` F13, `README.md` (*Guarantees*).
- **Mimo sondy:** nativní dotaz, který má `OPTION (MAXRECURSION n)` v textu a k tomu hint na objektu dotazu. Hibernate by nejspíš připojil druhou klauzuli `OPTION`; čtečka vezme za limit hint, který čte až po textu. Takový zdroj nástroj sám nevyrobí.
