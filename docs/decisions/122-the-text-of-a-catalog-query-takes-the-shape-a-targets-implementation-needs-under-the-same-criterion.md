# 122 — Text dotazu katalogu LDBC smí mít tvar, který implementace jednoho cíle potřebuje, za týchž podmínek jako tvar pro plánovač: zůstane-li T-SQL téže věty specifikace, jaké by stálo vedle referenční implementace; kde takový tvar jediný `SELECT` nemá, katalog vysloví mez běhu, ne mez překladu

Datum: 2026-10-10
Stav: platí
Požadavky: T2, T3, F9, F13
Podklad: rozhodnutí [110](110-ldbc-snb-as-a-second-reference-domain.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) a [121](121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md); specifikace *The LDBC Social Network Benchmark*, verze 2.2.4, příloha A (IS 7, IC 5) a příloha B (BI 2, BI 5, BI 15, BI 19); referenční implementace `ldbc_snb_interactive_v1_impls` a `ldbc_snb_bi`; měření 2026-10-10 nad `mssql_db` (SF 1) na MIS3, zapsané v otevřené položce *Definice hran uvnitř rekurze: BI 15 a BI 19 nad SF 1* před tímto rozhodnutím

## Kontext

Katalog LDBC (rozh. 110) drží 41 dotazů a ke každému říká, kterým cílům jde jazykem cíle a kterým nativním SQL (113). Cílem je co nejvíc dotazů jazykem cíle, protože právě to měří T2 a T3; nativní SQL se počítá zvlášť a za překlad se nevydává. Dvě otázky stály otevřené od 117 a 121, obě o tom, **smí-li text katalogu dostat tvar kvůli něčemu jinému než specifikaci**:

**Čtyři dotazy EclipseLinku** (IS 7, IC 5, BI 2, BI 5) jdou do EclipseLinku nativním SQL z jediného důvodu: vnější join, jehož podmínka jmenuje alias vnitřního joinu. EclipseLink 5.0.0 píše vnitřní entitní joiny do SQL až za všechny vnější (změřeno soudcem 117), takže podmínka vnějšího joinu jmenuje tabulku, kterou SQL Server ještě nepotkal, a dotaz odmítne. Nástroj to neobejde — pořadí píše EclipseLink a proměnná rozsahu nepomůže, protože podmínka `ON` přes čárku v SQL Serveru nesahá. Obejde to text:

| Dotaz | Dnešní tvar | Tvar, který EclipseLink unese | Je to táž věta? |
|---|---|---|---|
| IS 7 | `LEFT JOIN Person_knows_Person k ON k.Person1Id = m.CreatorPersonId AND k.Person2Id = c.CreatorPersonId` + `CASE WHEN k.Person1Id IS NULL …` | příznak jako `CASE WHEN EXISTS (SELECT 1 FROM Person_knows_Person AS k WHERE …) THEN 1 ELSE 0 END` | ano: „zda autor odpovědi zná autora zprávy" je podle specifikace existence hrany `knows`, a tu SQL vyslovuje `EXISTS` |
| IC 5 | `FROM Forum_hasMember_Person fm JOIN Forum f … LEFT JOIN Message m ON m.ContainerForumId = f.Id AND m.CreatorPersonId = fm.PersonId …` | vnější join hned za kořenem a jen nad jeho sloupci: `FROM Forum_hasMember_Person fm LEFT JOIN Message m ON m.ContainerForumId = fm.ForumId AND m.CreatorPersonId = fm.PersonId … JOIN Forum f ON f.Id = fm.ForumId` | ano: `fm.ForumId` a `f.Id` jsou týž klíč; mění se jen pořadí joinů, které je v T-SQL volné |
| BI 2 | `FROM TagClass tc JOIN Tag t … LEFT JOIN Message_hasTag_Tag mt ON mt.TagId = t.Id …` | kořenem je `Tag`: `FROM Tag t JOIN TagClass tc ON tc.Id = t.TypeTagClassId LEFT JOIN Message_hasTag_Tag mt ON mt.TagId = t.Id …` | ano: táž tabulka joinů, jiný kořen |
| BI 5 | `FROM Tag t JOIN Message_hasTag_Tag mt … JOIN Message m … LEFT JOIN Message r ON r.ParentMessageId = m.Id` | kořenem je `Message`: `FROM Message m JOIN Message_hasTag_Tag mt ON mt.MessageId = m.Id JOIN Tag t ON t.Id = mt.TagId LEFT JOIN Message r ON r.ParentMessageId = m.Id` | ano: táž tabulka joinů, jiný kořen |

Ve třech ze čtyř je tedy tvar jen jiný kořen `FROM` a pořadí joinů, které SQL nijak nepředepisuje; ve čtvrtém je `EXISTS` to, co referenční implementace sama píše. Žádný z nich není dotaz, „který by takto nikdo nenapsal" — to byla obava otevřené položky, a platí dál pro tvary, které by ji naplnily (korelovaný skalární poddotaz místo joinu u IC 5 s okruhem osob vypsaným dvakrát).

**BI 15 a BI 19 nad SF 1.** Oba procházejí v rekurzivním členu definici hran postavenou nad seskupením odpovědí (969 tisíc dvojic autorů ze 3 milionů zpráv). SQL Server definici nematerializuje a plán ji přepočítává pro každý klíč procházky, takže BI 19 nedoběhne do 500 s ani z obou konců, ani z jednoho — a stejně dopadl tvar s hranami jako vnějším joinem až za rekurzí, se seskupením podle neuspořádané dvojice i s odpověďmi počítanými korelovaným poddotazem na krok. BI 15 z obou konců doběhne za 49 s, z jednoho ne. Nad hranami uloženými do dočasných tabulek dá BI 19 týž výsledek za 6 s — jenže dočasná tabulka jsou dva příkazy, a dotaz katalogu je jeden `SELECT` (110). Soudce 117 dotazy BI nesoudí; `Combined/LdbcCatalogTest` každý text spouští nad prázdným schématem a profil `test` nad SF 0,1, kde oba doběhnou.

## Zvažované varianty

### 1 — Text se kvůli cíli nemění; čtyři dotazy zůstávají nativním SQL

Čisté čtení 110: text je přirozené T-SQL specifikace a cíl, který ho neunese, jde únikovou cestou. Zamítáme: 121 už připustilo tvar kvůli plánovači databáze za podmínek (a)–(d), a tvar, který je *jen jiným pořadím joinů téže věty*, ty podmínky plní stejně — rozdíl „kvůli databázi" a „kvůli implementaci cíle" není rozdíl v textu, ale v tom, kdo text neunesl. Měřilo by se o čtyři dotazy méně v jazyce cíle kvůli vlastnosti textu, kterou specifikace nepředepisuje.

### 2 — Text dostane jakýkoli tvar, který cíl unese

Včetně IC 5 s korelovaným poddotazem a okruhem osob vypsaným dvakrát, nebo dotazů BI rozepsaných do tvarů, které by plánovač unesl jen s dočasnou tabulkou. Zamítáme: matice by měřila dotazy, které by vedle referenční implementace nikdo nenapsal, a to je přesně závislost katalogu na nástroji, kterou 110 zakázalo.

### 3 — Builder EclipseLinku přepíše vnější join sám

Přeskládat joiny nebo přepsat vnější join na `EXISTS` v builderu, aby text zůstal. Zamítáme: přeskládání joinů obecně mění řádky (vnější join za vnitřním a před ním není totéž), přepis na `EXISTS` platí jen pro join, ze kterého se čte jen existence řádku, a obojí je přepis dotazu podle vlastnosti jedné verze jedné implementace — ten patří do únikové cesty (113), ne do překladu.

### 4 — BI 15 a BI 19: text ve tvaru dočasných tabulek, nebo vyjmout z katalogu

Dočasné tabulky nejsou jeden `SELECT`; vyjmutí by porušilo zásadu 110, že katalog nic nevynechává, aby pokrytí nevznikalo mlčením.

### 5 — Tvar pro implementaci cíle za podmínkami 121; mez běhu vyslovená v katalogu

## Rozhodnutí

**Volíme variantu 5. Text dotazu katalogu smí dostat tvar, který implementace jednoho cíle potřebuje, za týchž podmínek, jaké 121 stanovilo pro plánovač: (a) jde o tutéž větu specifikace, (b) výsledný tvar je T-SQL, jaké referenční implementace píše nebo by napsala, (c) nemění, co nástroj přeloží — žádný cíl nepřibude v nativním SQL ani v odmítnutí —, a (d) důvod je zapsaný u dotazu. Kde takový tvar pro plánovač jediný `SELECT` nemá, text zůstává přirozený a katalog u dotazu vysloví, že nad SF 1 neběží v rozumném čase: je to mez běhu jedné databáze, ne mez překladu, a nárok T2 a T3 je o překladu.**

Pro čtyři dotazy EclipseLinku to znamená tvary z tabulky výš: IS 7 s `EXISTS`, IC 5, BI 2 a BI 5 s jiným kořenem `FROM`. Všechny čtyři jsou pak jazykem cíle ve všech pěti cílech; EclipseLink nativním SQL píše 14 dotazů místo 18 a důvod `EclipseLinkOuterJoinOrder` z katalogu mizí — vlastnost implementace zůstává v deskriptoru (`InnerJoinsFollowOuterJoins`) a v `subset.md` 2.6 a 2.11 pro každý vstup, který takový join napíše. Pro BI 15 a BI 19 to znamená poznámku u dotazu s naměřenými časy a stav *podle specifikace* beze změny: text vrací, co specifikace definuje, jen nad SF 1 to SQL Server nespočítá v čase, který by soudce snesl — a soudce je nad BI nesoudí.

### Proč stejné podmínky, a ne přísnější

Podmínky 121 nejsou o tom, *proč* se tvar mění, ale o tom, *co* smí vzniknout: táž věta, T-SQL referenční implementace, žádná ztráta pro překlad, zapsaný důvod. Tvar, který je plní, stojí vedle referenční implementace stejně dobře bez ohledu na to, zda ho vynutil plánovač SQL Serveru nebo pořadí joinů EclipseLinku. Přísnější podmínka pro cíl by se musela odůvodnit tím, že vlastnost implementace je méně legitimní než vlastnost plánovače — a není: obě jsou měřené vlastnosti zafixované verze, obě 113 vede jako důvod únikové cesty, a nad oběma je soudce 117, který drží, že nový text vrací tytéž řádky (IS 7 a IC 5 na každém čtení validační sady; BI 2 a BI 5 soudce nemá a zůstávají tvrzením autora jako dosud, s textem spuštěným nad tabulkami v obou sadách).

### Co to neotevírá

Podmínka (b) dál vylučuje tvary, které by text ohnuly kvůli cíli do podoby, kterou referenční implementace nepíše: korelovaný skalární poddotaz místo vnějšího joinu tam, kde se z joinu čtou sloupce, okruh osob vypsaný dvakrát, `UNION` v poddotazu kvůli plánovači (121, varianta 4). Úniková cesta po kouscích a rekurze rozepsaná do pevného počtu joinů zůstávají zamítnuté (113).

## Důsledky

- **Katalog:** texty IS 7, IC 5, BI 2 a BI 5 v `SampleData/LdbcSnbSample.cs` a jejich poznámky; `FallbackBy` bez `EclipseLinkOuterJoinOrder`; poznámky BI 15 a BI 19 nesou mez běhu nad SF 1 s časy. Stavy se nemění (36 *podle specifikace*, 5 *se zjednodušením*). `Combined/LdbcCatalogTest` drží, že čtyři dotazy jdou do EclipseLinku jazykem cíle a že každý text běží.
- **Soudce 117:** IS 7 a IC 5 musí souhlasit na každém čtení v šesti frameworcích; nový tvar se tím měří stejně jako starý. BI 2 a BI 5 zůstávají na 1. stupni, v cílech EF Core a NHibernate na 2.
- **Nástroj:** nic — vlastnost `InnerJoinsFollowOuterJoins` a její brána v `AbstractJpaQueryBuilder` zůstávají, protože každý jiný vstup s takovým joinem jde dál nativním SQL se záznamem.
- **Dokumentace:** `architecture.md` §6.2 (počty), `subset.md` 1.3 (katalog) a 2.11 (řádek o pořadí joinů odkazuje sem), `traceability.md` T2; otevřené položky *Přepis textu katalogu kvůli vadě jednoho cíle* a *Definice hran uvnitř rekurze: BI 15 a BI 19 nad SF 1* zanikají.
