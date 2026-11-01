# 201 — CASE v ELSE jiného CASE píše builder JPQL do EclipseLinku jako jeden CASE s větvemi vnitřního připojenými za větve vnějšího; CASE hlouběji pod ELSE jde nativním SQL

Datum: 2026-11-01
Stav: platí
Požadavky: F9, F10, T2, T3

Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md); [záznam javové půlky běhu mezí 2026-10-30](../runs/2026-10-30-limits-java.md), řada `case`; `eclipselink/NestedCaseClaimsTest` a `hibernate/NestedCaseClaimsTest` javové sady

## Kontext

Řada `case` běhu mezí (rozh. 182) roste po úrovních CASE vnořeného v ELSE toho nad ním: `case when p.UnitPrice > 1 then 1 else case when p.UnitPrice > 2 then 2 else … end end`, v projekci, bez jediné závorky, geometricky do 4096. Javová půlka běhu z 2026-10-30 ji do EclipseLinku dovedla jen na bod 1 z každého zdroje kromě EF Core: EclipseLink 5.0.0 artefakt přijal (třetí stupeň) a dotaz spadl až během (čtvrtý stupeň) — SQL Server odmítl `Incorrect syntax near the keyword 'ELSE'`. Ze zdroje EF Core, jehož řetěz `?:` čte LINQ jako jeden CASE s řadou WHEN, běžel bod 1024 i v EclipseLinku. Matice tvarů vnořený CASE neměla (kategorie `CaseInAProjection` a `CaseOverExistsInAProjection` mají jeden CASE s jednou větví), takže vada se ukázala až v řadě. Hibernate 7.4.5 týž text běžel do bodu 8 a dál ho zastavil SQL Server (CASE vnořený nejvýš desetkrát, `Msg 125`), tedy mez platformy, ne nástroje.

Otevřená položka žádala změřit, zda EclipseLink větve ztrácí u textu, který builder píše, nebo u každého vnořeného CASE, a podle toho rozhodnout mezi jiným tvarem textu a únikovou cestou nativním SQL (rozh. 113).

### Co měření ukázalo

Změřili jsme 2026-11-01 proti EclipseLinku 5.0.0 a Hibernatu 7.4.5 nad SQL Serverem v kontejneru sady, s logováním SQL obou implementací; výsledek drží `eclipselink/NestedCaseClaimsTest` a `hibernate/NestedCaseClaimsTest` (tabulka hodnot 1–4 pod klíči 1–4).

| JPQL | SQL EclipseLinku 5.0.0 | Výsledek |
|---|---|---|
| `case when p > 3 then 3 else case when p > 2 then 2 else 0 end end` | `CASE ELSE CASE WHEN (price > ?) THEN ? WHEN (price > ?) THEN ? ELSE ? END END` | server odmítne |
| totéž s vnitřním CASE v závorkách | totéž | server odmítne |
| tři úrovně | `CASE ELSE CASE ELSE CASE WHEN … WHEN … WHEN … ELSE ? END END END` | server odmítne |
| týž CASE ve `where` | totéž ve `WHERE` | server odmítne |
| `… else coalesce(case when p > 2 then 2 else 0 end, 7) end` | `CASE ELSE COALESCE(CASE WHEN … WHEN … ELSE ? END, ?) END` | server odmítne |
| `… else (select max(case when w.price > 2 then 2 else 0 end) from NcValue w) end` | vnější CASE bez větví | server odmítne |
| `case when p > 1 then case when p > 2 then 2 else 1 end else 0 end` (CASE v THEN) | `CASE WHEN (price > ?) THEN CASE WHEN … END ELSE ? END` | řádky správně |
| `case when (case when p > 2 then 1 else 0 end) = 1 then 3 else 0 end` (CASE v operandu WHEN) | vnořeně, jak stojí | řádky správně |
| `case … end + case … end` | obě vedle sebe | řádky správně |
| `… else (select max(w.price) from NcValue w) end` | poddotaz, jak stojí | řádky správně |
| `case when p > 3 then 3 when p > 2 then 2 when p > 1 then 1 else 0 end` | čtyři WHEN v jednom CASE | řádky správně |

EclipseLink tedy přesune větve WHEN vnějšího CASE do CASE, který najde kdekoli pod jeho ELSE — přímo, pod výrazem i v poddotazu —, a vnější CASE pošle bez větví; závorky to nemění, a CASE kdekoli jinde píše, jak stojí. Chyba je v EclipseLinku, ne v textu nástroje: tentýž text je standardní JPQL, Hibernate ho čte a píše vnořeně (`case when … else case when … end end` v jeho SQL) a vrací řádky, které vnoření znamená, ve všech třech tvarech. Co stojí za povšimnutí: přesunuté větve vnějšího CASE stojí ve vnitřním *před* jeho vlastními, takže vnitřní CASE sám o sobě počítá správnou hodnotu — chybí jen skořápka vnějšího kolem něj.

**Je třeba rozhodnout**, jak builder JPQL píše CASE, pod jehož ELSE stojí CASE, do EclipseLinku.

## Zvažované varianty

### 1 — Vnitřní CASE v závorkách

Zamítáme: změřeno bez účinku, SQL je totéž.

### 2 — Nativní SQL pro každý CASE, pod jehož ELSE stojí CASE

Rozhodnutí 113 tuto odpověď má: co jazyk cíle nevysloví, napíše cíl celé nativním SQL se záznamem `Fallback`. Zamítáme ji ale jako *celou* odpověď, protože JPQL EclipseLinku CASE v ELSE vysloví — jen jiným tvarem, který týž rozhodnutí 113 staví před únikovou cestu („cíl píše dotaz svým dotazovým jazykem, kdykoli ho ten jazyk vysloví“). Řetěz CASE v ELSE je běžný zápis pásem (cenových, množstevních) a řada `case` by do EclipseLinku měřila obaly kolem jednoho textu SQL od bodu 2 výš, tedy přesně to, proti čemu varianta 2 rozhodnutí 113 argumentuje. Úniková cesta zůstává tam, kde jiný tvar není (níž).

### 3 — Zploštit vnořený CASE v modelu, tedy pro všechny cíle

Čtečky by CASE v ELSE skládaly do jednoho CASE, jako LINQ skládá řetěz `?:`. Zamítáme. Model nese tvar zdroje a hloubka modelu ho počítá (rozh. 191); čtečky T-SQL, HQL a JPQL vnoření drží a Dapper, MyBatis, NHibernate a Hibernate ho píšou, jak by ho napsal člověk v tom frameworku (rozh. 022), a artefakt přečtený zpět dává model zdroje. Mez SQL Serveru — CASE vnořený nejvýš desetkrát —, kterou běh mezí našel jako mez platformy, by z ostatních cílů zmizela. Měnit zápis pěti cílů kvůli vadě jednoho není v souladu s tím, jak nástroj odlišnosti implementací nese: profilem té implementace (rozh. 076, 080).

### 4 — Jiný tvar textu pod profilem EclipseLinku; co tvar nemá, nativním SQL

Volíme. Model nese jen hledaný CASE (`QueryExpression.Case` s podmínkou v každé větvi; jednoduchý CASE čtou parsery jako porovnání vstupu s hodnotou) a hledaný CASE počítá první větev, jejíž podmínka platí, jinak ELSE. CASE stojící přímo v ELSE se vyhodnotí jen tehdy, když neplatila žádná větev vnějšího, takže jeden CASE s větvemi vnitřního *za* větvemi vnějšího a s ELSE vnitřního je táž hodnota: pořadí větví zůstává, podmínka s `NULL` v obou tvarech „neplatí“, chybějící vnitřní ELSE je `else null` jako dnes a typ výsledku SQL Server určuje z větví podle precedence, která je asociativní, takže je týž u obou tvarů. Model už dnes oba zápisy drží za jednu hodnotu: LINQ čte řetěz `?:` jako jeden CASE a píše jeden CASE jako řetěz `?:`. Zploštění jde přes libovolný počet úrovní, protože každá úroveň je CASE přímo v ELSE té nad ní.

CASE, který stojí pod ELSE *hlouběji* — pod `coalesce`, v operaci, v poddotazu —, takový tvar nemá: skořápku, kterou EclipseLink ztratí, nelze odstranit, aniž by se změnila hodnota. Tam jde dotaz nativním SQL se záznamem `Fallback` podle rozhodnutí 113 a záznam ELSE jmenuje; builder ho najde průchodem pod ELSE přes výrazy, podmínky větví CASE a poddotazy do libovolné hloubky, protože měření ukázalo, že EclipseLink větve přesune i do CASE uvnitř poddotazu.

Nese to profil implementace (`JpaImplementationProfile.MovesTheOuterBranchesIntoACaseInElse`), jako nesou ostatní změřené odlišnosti EclipseLinku: u Hibernatu je vypnutý a text zůstává vnořený.

## Rozhodnutí

**Volíme variantu 4.** Pod profilem `MovesTheOuterBranchesIntoACaseInElse` (EclipseLink 5.0.0, změřeno) píše `JpqlQueryVisitor` CASE, v jehož ELSE stojí přímo CASE, jako jeden CASE s větvemi vnitřního připojenými za větve vnějšího a s ELSE nejvnitřnějšího, přes libovolný počet úrovní. CASE, který pod ELSE stojí hlouběji — pod výrazem, v podmínce větve, v poddotazu —, pošle dotaz nativním SQL se záznamem `Fallback` (`Expression`), který ELSE jmenuje (rozh. 113). Hibernate 7.4.5 píše vnořený CASE vnořeně. Matice tvarů dostává kategorii `CaseNestedInAnElseInAProjection` ze všech šesti zdrojů a obě javové sady měření drží testem vedle háčku.

## Důsledky

- **Kód:** `JakartaPersistence/JpaImplementationProfile` — `MovesTheOuterBranchesIntoACaseInElse`; `EclipseLinkWrappers/EclipseLinkDescriptor` — profil; `JakartaPersistence/JpqlQueryVisitor.Case` a `HoldsACase`; `Tests/EclipseLink/EclipseLinkQueryTest`, `Tests/Hibernate/HibernateJpqlQueryBuilderTest`; kategorie `CaseNestedInAnElseInAProjection` (`categories.txt`, `matrix.txt`, `results/`, `QueryShapeInputs`); javové `eclipselink/NestedCaseClaimsTest` a `hibernate/NestedCaseClaimsTest`.
- **Dokumentace:** [`architecture.md`](../architecture.md) §5, odstavec *CASE v ELSE jiného CASE*; [`subset.md`](../subset.md) — tabulka tvarů podle cíle, kategorie, mez deseti úrovní CASE a řádek únikové cesty; položka vady mizí z `open-items.md`.
- **Co záznam uvidí jinak:** příští běh mezí dovede řadu `case` do EclipseLinku ze všech zdrojů tak daleko jako ze zdroje EF Core, protože jeden CASE s řadou WHEN mez deseti úrovní vnoření nemá — dál než ostatní cíle, které vnoření drží. Je to vlastnost tvaru, ne silnější překlad, a záznam to má říct.
- **Co rozhodnutí neurčuje:** zápis ostatních pěti cílů a model (varianta 3), ani to, zda vadu EclipseLinku hlásíme jeho projektu.
