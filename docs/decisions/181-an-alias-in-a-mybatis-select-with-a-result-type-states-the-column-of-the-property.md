# 181 — Alias sloupce ve výběru MyBatisu, který `resultType` mapuje na entitu, tvrdí sloupec její vlastnosti

Datum: 2026-10-24
Stav: platí
Požadavky: F4, F6, F11, T1, S1, S2
Podklad: rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), [017](017-source-precedence-for-mapping-facts.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md), [131](131-a-column-alias-in-a-typed-dapper-call-states-the-column-of-the-property.md) a [176](176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md); [první záznam experimentálního běhu](../runs/2026-10-24-experiment.md); dokumentace MyBatis 3 (*Mapper XML Files*, oddíl *Result Maps*: `select user_id as "id" … from some_table` s `resultType`, a *Auto-mapping*); výňatek `SampleData/ThirdParty/jpetstore`; položka *Katalog případové studie nedodá klíč JPetStore* v `open-items.md`

## Kontext

Výňatek JPetStore (zdroj MyBatis) byl v [záznamu z 2026-10-24](../runs/2026-10-24-experiment.md) odmítnut do NHibernatu, Hibernatu a EclipseLinku pro chybějící klíč i s katalogem případové studie, ačkoli jeho schéma má primární klíč na každé tabulce a Chinook ze zdroje Dapper týmž katalogem klíče dostal. Položka žádala zjistit, které entitě klíč chybí a proč. Zjištění: fáze doplnění klíč dodává jen tam, kde pro každý sloupec klíče najde vlastnost — podle tvrzeného sloupce, jinak podle jména (`CatalogCompletion.FindPropertyMapForColumn`) — a jinak hlásí `Incompleteness` *no property matches the key column*. JPetStore mapuje sloupce tak, jak to dělá většina projektů MyBatisu: bez `<resultMap>`, výběrem s `resultType` a aliasy v projekci — `CATID AS categoryId` pro `Category`, `LINENUM AS lineNumber` pro `LineItem`, `USERID AS username` pro `Order`. Parser MyBatisu ale sloupec vlastnosti čte jen z `<resultMap>` (`<id>`, `<result>`) a z `@Results`; `resultType` zahazuje jako `Loss` a alias zůstává aliasem projekce dotazu. `categoryId` a `lineNumber` tak nemají sloupec, klíč `catid` a `linenum` nemá vlastnost, a entity jsou do cílů, které klíč vyžadují, odmítnuty. Třetí entita, `Account`, je jiný případ: její klíč `userid` čte projekt ze spojené tabulky (`SIGNON.USERNAME`) a žádná vlastnost nad sloupcem `account.userid` nestojí — to je mez zdroje, ne nástroje.

Je to tentýž tvar, který rozhodnutí 131 rozhodlo pro Dapper. MyBatis mapuje řádek bez `<resultMap>` **automatickým mapováním**: každý sloupec výsledku jde do vlastnosti stejného jména bez ohledu na velikost písmen (dokumentace, *Auto-mapping*; přepínač `mapUnderscoreToCamelCase` je nastavení konfigurace, ne příkazu), a dokumentace sama dává alias v projekci jako způsob, jak sloupec jiného jména namapovat na vlastnost (`select user_id as "id"` s `resultType`). Alias ve výběru s `resultType` je tedy totéž tvrzení jako `<result column="CATID" property="categoryId"/>`, jen zapsané v příkazu — mapovací artefakt MyBatisu, který nástroj zatím nečte, se všemi třemi důvody, pro něž 131 rozhodovalo a nejen psalo: alias je výrok o jednom výsledku, ne každý alias je mapování, a dotazová větev ho čte až po katalogu.

## Zvažované varianty

1. **Nečíst a vyslovit mez.** F6 by pro MyBatis platilo jen tam, kde se jméno sloupce a vlastnosti shodují — v témže okrajovém případě, který 131 odmítlo pro Dapper, a případová studie T1 by byla odmítnutá ve třech cílech ze šesti kvůli něčemu, co projekt vyslovil. Zamítáme.

2. **Párovat klíč katalogu s vlastností volněji** (bez podtržítek, `mapUnderscoreToCamelCase`). `linenum` a `lineNumber` ani `catid` a `categoryId` se nepotkají žádným pravidlem nad jmény, a domněnka za výrok zdroje se nevydává nikdy (rozhodnutí 067). Zamítáme.

3. **Číst alias i ve výběru s `resultMap`,** protože MyBatis pod výchozím `autoMappingBehavior = PARTIAL` doplní automatickým mapováním i sloupce, které mapa nejmenuje. Je to chování nastavení konfigurace, kterou nástroj nečte, a projekt, který napsal explicitní mapu, vyslovil mapování v ní; alias za mapou je výchozí chování, ne výrok (067). Zamítáme — zůstává vyslovenou mezí.

4. **Alias v projekci výběru, jehož `resultType` jmenuje entitu převodu, nad sloupcem tabulky té entity, jako tvrzení zdroje** — čtený v entitním průchodu mapovacími parsery MyBatisu, podmínkami rozhodnutí 131.

## Rozhodnutí

**Volíme variantu 4.** Alias sloupce ve výběru MyBatisu, jehož `resultType` jmenuje entitu převodu, je tvrzením zdroje o sloupci její vlastnosti: fakt `ColumnName` úrovně 1b (rozhodnutí 017), zapsaný dřív, než běží katalog.

**Kdy je alias tvrzením** — podmínky rozhodnutí 131 s tím, co je v MyBatisu jinak:

| Podmínka | MyBatis |
|---|---|
| příkaz mapuje řádky na jeden typ | `<select resultType="…">` v mapperu XML; `@Select` na metodě rozhraní bez `@Results` a `@ResultMap`, jejíž návratový typ třídu materializuje (`MyBatisTypeNames.MaterializedClass`: typ, nebo prvek kolekce); `resultMap` a `@Results` jsou explicitní mapa (varianta 3), `resultType="map"`, skalár a `Map<…>` nic netvrdí |
| text se čte jako dotaz | týmž `MyBatisStatementText` jako dotazová větev (statické značky rozvinuté, OGNL odmítnuté), jeden `SELECT`; co text odmítne, hlásí dotazový průchod, entitní mlčí |
| typ je entita převodu | podle jednoduchého jména, u kvalifikovaného i podle balíku; jen entita, kterou některá jednotka deklarovala — jméno třídy není její deklarace |
| holý sloupec pod aliasem ve vnější projekci | `SqlRenamedColumns` v `TransactSql`, beze změny |
| tabulka sloupce je tabulkou entity | tabulka vyslovená, jinak kandidát rozhodnutí 050; u nekvalifikovaného sloupce jediná tabulka `FROM` |
| alias jmenuje skalární vlastnost | pravidlem automatického mapování MyBatisu: přesně, pak bez ohledu na velikost písmen; tečkovaný alias (`"product.productId"`) žádnou vlastnost nejmenuje a zůstává pokynem vnoření, které model nenese ([`subset.md`](../subset.md)) |
| alias se od sloupce liší a v projekci se neopakuje | beze změny |

**Je to tvrzení zdroje, ne doplnění** (131): katalog ho nepřepíše, jen porovná; záznam nevzniká; fakt nese jednotka s příkazem. Dva různé sloupce pro tutéž vlastnost — dva příkazy, nebo `<resultMap>` a alias — jsou `Conflict` kategorie `ColumnName` a platí první v pořadí čtení (017); `<resultMap>` téže jednotky se čte před jejími příkazy.

**Kde se čte.** V entitním průchodu, v parserech, které už mapovací polovinu obou jednotek čtou (rozhodnutí 081): `MyBatisXmlMappingParser` po `<resultMap>` dokumentu projde jeho `<select resultType>`, `MyBatisMapperInterfaceParser` metody s `@Select`. Sdílený krok stojí v `MyBatisMappingParser` vedle `WriteColumn`. Orchestrace se nemění, `ParserFactory` jen předá mapovacím parserům deklarovaný dialekt zdroje, který `SqlText.Selects` vyžaduje (rozhodnutí 088). Znalost MyBatisu — který příkaz mapuje na který typ — zůstává ve wrapperu, čtení SQL ve sdíleném projektu, jako u Dapperu (S1).

**Co se nečte a zůstává vyslovenou mezí:** alias ve výběru s `resultMap`/`@Results` (varianta 3); `mapUnderscoreToCamelCase` a `autoMappingBehavior` v `mybatis-config.xml`, který se nečte; nekvalifikovaný sloupec nad víc tabulkami `FROM` (`USERID AS username` v `getOrder` nad `ORDERS INNER JOIN ORDERSTATUS` — komu patří, ví jen schéma; totéž pravidlo jako u holého sloupce dotazu); tabulka entity z dotazu (131).

## Důsledky

- **JPetStore v případové studii:** `Category` a `LineItem` dostanou klíč z katalogu studie (`catid`; `orderid`, `linenum`) a projdou do cílů, které klíč vyžadují; `Account` zůstává odmítnutá, protože její klíč stojí nad sloupcem, který žádná vlastnost nemapuje — od teď je to odmítnutí podle vyslovené meze zdroje, a řádek běhu jmenuje entitu, které se týká (řádek *odmítnuto* nese jméno entity záznamu `Failure`, `Tests/Experiment/ExperimentRun`). Projekt jako celek tedy do NHibernatu, Hibernatu a EclipseLinku dál nevyjde, a tabulka případových studií to řekne přesně.
- **F6 platí i pro idiomatický zápis MyBatisu**, nejen pro `<resultMap>`; řádek MyBatisu v tabulce faktů [`subset.md`](../subset.md) dostává u sloupce vedle `<result column>` i alias výběru s `resultType`, a mez v oddílu 2.2 vedle té Dapperovy.
- **Dapper se nemění**, 131 platí dál; obě čtení sdílejí `SqlRenamedColumns`.
- **Dotčená místa:** `MyBatisMappingParser`, `MyBatisXmlMappingParser`, `MyBatisMapperInterfaceParser`, `ParserFactory`; `architecture.md` (§5, čtení MyBatisu), `subset.md`, `traceability.md` (F6, T1); test `MyBatis/MyBatisColumnAliasTest`.
