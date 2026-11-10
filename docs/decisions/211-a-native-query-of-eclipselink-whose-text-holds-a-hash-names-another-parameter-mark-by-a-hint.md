# 211 — Nativní dotaz EclipseLinku, jehož text drží `#`, dostane hintem jinou značku pojmenovaných parametrů

Datum: 2026-11-09
Stav: platí
Požadavky: F9, F10, F11, T3
Podklad: rozhodnutí [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [123](123-a-native-query-of-eclipselink-expands-a-collection-parameter-at-run-time.md) a [208](208-sede-as-the-fourth-reference-domain-over-the-dba-stack-exchange-dump-judged-by-a-judge-of-our-own.md); [záznam běhu SEDE 2026-11-08](../runs/2026-11-08-sede-2.md)

## Kontext

Texty katalogu SEDE číslují řádky výsledku pod aliasem `[#]`. Alias, který není identifikátorem, jazyk nad entitami nevysloví, a dotaz proto do obou profilů JPA odchází nativním SQL (rozh. 113) — týž text, jaký píše Dapper, předaný `EntityManager.createNativeQuery`. [Běh SEDE z 2026-11-08](../runs/2026-11-08-sede-2.md) ukázal, že EclipseLink takový text nespustí: 102 buněk *selhalo*, 99 z nich s `#` v textu, s hlášením ovladače „The index 1 is out of range". Hibernate tytéž texty spustil.

EclipseLink čte v textu nativního dotazu `#` jako začátek pojmenovaného parametru (`#jméno`) a za každý přepíše značku `?`, pro kterou pak hodnotu nemá. Kde přesně, jsme změřili nad připnutou verzí EclipseLink 5.0.0 (javová sada, `eclipselink/NativeSqlHashClaimsTest`, bez tabulek):

| Text | Bez hintu | S hintem `eclipselink.jdbc.parameter-delimiter` = U+0001 |
|---|---|---|
| `SELECT 1 AS [#]`, `[#x]`, `[a#b]` | **selže**, „The index 1 is out of range" | proběhne |
| `SELECT 1 AS "#"`, `SELECT 'a #b c' AS x` | proběhne | proběhne |
| `SELECT 1 AS "a#b" WHERE 1 = ?1` | proběhne | proběhne |
| `SELECT 1 AS "a#b" WHERE 'x#' = ?1` | **selže**, týž důvod | proběhne |
| `SELECT ?1 AS [#], ?2 AS y`; `… IN (?1, ?2) AND N'#x' = ?3` | selže | proběhne, poziční parametry navázané |

Uvozovky tedy `#` před EclipseLinkem chrání jen někdy — jeho sledování uvozovek se rozpadne, jakmile text drží víc uvozených úseků s `#` a parametr —, hranaté závorky nikdy. Hint, který jmenuje jiný znak jako značku pojmenovaného parametru, čtení `#` vypne úplně a poziční parametry `?n`, jediné, které nativní SQL builderu píše (rozh. 113, 123), nechá, jak jsou. Hibernate 7.4.5 čte `#` ve všech tvarech tabulky jako text (`hibernate/NativeSqlHashClaimsTest`).

## Zvažované varianty

1. **Odmítnout dotaz s `#` v nativním SQL EclipseLinku.** Zamítáme: dotaz, který implementace spustit umí, bychom ztratili celý — v katalogu SEDE skoro stovku.
2. **Přepsat hranatou závorku na uvozovky standardu SQL** (`"#"`) — text by zůstal bez prvku navíc a SQL Server ho pod výchozím `QUOTED_IDENTIFIER ON` čte stejně. Zamítáme podle měření: uvozovky EclipseLink nečte spolehlivě, `"a#b"` vedle literálu `'x#'` a parametru selže stejně jako `[a#b]`, takže by oprava platila jen pro část textů a o zbytku by rozhodovala náhoda jejich tvaru.
3. **Přejmenovat alias** na jméno bez `#`. Zamítáme: alias je jméno sloupce výsledku, které zdroj vyslovil, a výsledek by se jmenoval jinak, než ho čte konzument.
4. **Pojmenovat hintem jinou značku pojmenovaných parametrů, a to znak, který text nedrží.**

## Rozhodnutí

**Volíme variantu 4.** Profil implementace (`JpaImplementationProfile`) dostává volání `NativeSqlParameterDelimiterCall` — u EclipseLinku `.setHint(QueryHints.PARAMETER_DELIMITER, "{0}")`, u Hibernatu žádné. `JpaNativeSqlQueryBuilder` ho připojí k nativnímu dotazu, jehož text drží `#`, a k žádnému jinému, takže ostatní dotazy zůstávají beze změny. Za `{0}` dosadí první řídicí znak od U+0001, který text nedrží (U+000A a U+000D vynechá: javac je i uvnitř řetězce čte jako konec řádku), zapsaný jako únikovou sekvenci Javy `\u0001` — artefakt tak žádný řídicí znak nedrží, a v textu, který se nikdy nenaváže jako parametr, nic neznamená. Text by musel držet všech třicet řídicích znaků, aby dotaz skončil záznamem `Failure`.

Je to tentýž tvar jako přepínač vázání literálů (`InlineLiteralsCall`, rozh. 113): nastavení jednoho dotazu, ne jazyka, konstanta z `org.eclipse.persistence.config`, kterou si projekt konzumenta importuje stejně jako u literálů. Holé SQL vedle metody (rozh. 025) se nemění — je to dotaz, jak ho přečte databáze.

Fakta drží `eclipselink/NativeSqlHashClaimsTest` (selhání bez hintu včetně uvozovek, běh s hintem včetně pozičních parametrů a seznamu) a `hibernate/NativeSqlHashClaimsTest` (Hibernate hint nepotřebuje); zápis drží `Combined/CatalogRunDefectTest` a hodnotu profilu `Combined/TargetFrameworkDescriptorTest`.

## Důsledky

- **Dotazy SEDE s `[#]` EclipseLink spustí**; buňky, které selhaly na „The index 1 is out of range", se srovnají se zdrojem.
- **`?` uvnitř identifikátoru nebo literálu hint neřeší** — EclipseLink ho čte jako poziční parametr v závorkách i v uvozovkách (změřeno touž sondou: `[a?b]` i `"a?b"` selžou). Katalogy ho v aliasech nemají a běh SEDE ho jako příčinu nenašel; zůstává vyslovenou mezí v [`subset.md`](../subset.md).
- **Hibernate se nemění**; jeho nativní SQL `#` čte jako text.
- **Dotčená místa:** `JpaImplementationProfile`, `EclipseLinkDescriptor.Profile`, `JpaNativeSqlQueryBuilder.Emit`; `architecture.md` (§5, úniková cesta JPA), `subset.md`, `traceability.md` (F9).
