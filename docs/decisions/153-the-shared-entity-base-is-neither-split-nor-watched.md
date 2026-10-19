# 153 — Sdílenou entitní bázi nerozdělujeme a její velikost nehlídáme; strážcem S1 zůstává pravidlo, že nový framework `AbstractWrappers` nemění

Datum: 2026-10-19
Stav: platí
Požadavky: S1
Podklad: revize z [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nález 5.4, a z [2026-10-18](../audits/2026-10-18-whole-repository-audit.md), kapitola 5; [`architecture.md`](../architecture.md) §7

## Kontext

Entitní builder každého ze šesti frameworků dědí `AbstractEntityBuilder`. Báze má dvě poloviny (§7). **Naplnění** jsou veřejné, na frameworku nezávislé metody, kterými model plní parsery i fáze doplnění z katalogu (`BeginEntity`, `AddPrimaryKey`, `AddRelation`, `SetPropertyDatabaseMapping` …). **Generování** je šablonová metoda `Build()`: nad celou sadou entit rozpustí klíčové třídy, rozřeší konvenční navigace, posoudí vyslovené bázové typy a odkazované sloupce, syntetizuje spojovací entity, rozřeší jména, projde bránou úplnosti, ohlásí ztráty, zavolá sedm kroků cíle a zapíše knihu tvrzení. Wrapper smí přepsat jen deskriptor a těch sedm kroků; jiný `virtual` člen báze nemá.

Obě revize změřily touž dvojici čísel:

| | `1.2.0` | 2026-09-21 | 2026-10-18 |
|---|---|---|---|
| `AbstractEntityBuilder`, řádků | 2 137 | 2 728 | 3 666 |
| `AbstractEntityBuilder`, přepsatelných členů | 9 | 9 | 9 |

Báze rostla a plocha, kterou wrapper může přepsat, ne. Revize z toho nevyvodily porušení S1 — jména frameworků se v `AbstractWrappers`, `Common`, `Model` ani v `ConversionHandler` mimo deklaraci `ORMEnum` nevyskytují a `Descriptor.Framework` báze čte jen do záznamů —, ale riziko: framework, jehož potřeba se do sedmi kroků a deskriptoru netrefí, nemá kam jít než do těla báze, nebo do příznaku deskriptoru, podle kterého by se tělo větvilo. Revize to zařadila do delšího horizontu se slovy „hlídat se to dá levně, opravovat draze". Je třeba rozhodnout, zda s tím něco uděláme.

## Zvažované varianty

1. **Rozdělit bázi** — naplnění, které je dnes většinou řádků, do samostatné třídy skládající mezireprezentaci, a generování zvlášť; poměr těla šablony a háčků by pak měřil to, co měřit má.
2. **Přidat háčky dopředu** — `virtual` členy tam, kde báze rozhoduje něco, co by jiný framework mohl chtít jinak (syntéza spojovací entity, rozřešení jmen, konvenční navigace).
3. **Hlídat dvě čísla u každého vydání** — řádky a přepsatelné členy v anotaci značky (rozh. [098](098-the-number-is-decided-once-per-release.md)) s prahem, který spustí rozhodnutí.
4. **Nic z toho** — strážcem S1 zůstává pravidlo, že nový framework je nový wrapper a nic v `AbstractWrappers` nemění.

## Rozhodnutí

**Varianta 4. Bázi nerozdělujeme, háčky dopředu nepřidáváme a její velikost nehlídáme.**

**Dvojice čísel měří něco jiného než S1.** Poměr řádků a háčků nerozezná schopnost sdílenou všemi frameworky od logiky jednoho frameworku, která do báze prosákla — a právě to první S1 v bázi chce: parsery vydávají fakta mezireprezentace přes rozhraní builderu nezávislá na frameworku a buildery z naplněné mezireprezentace generují (článek *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection*, §4.3). Všechno, oč báze vyrostla, je sdílené: rozpuštění klíčové třídy zdroje jednou fází pro každý framework, který klíč vyslovuje třídou, syntéza spojovacích entit, konvenční navigace, vyslovené bázové typy, kniha tvrzení a pozice faktů. Přesunout to do wrapperů by znamenalo napsat totéž šestkrát, tedy opak S1.

**Skutečné porušení hlídá pravidlo, které už platí, a hlídá ho lépe.** §7 říká, že nový framework je nový projekt wrapperu a nic v `AbstractWrappers`, orchestraci ani ostatních wrapperech se nemění. Framework, jehož potřeba se do sedmi kroků a deskriptoru nevejde, by musel `AbstractWrappers` změnit, a tím by pravidlo porušil; taková změna je rozhodnutí. To je správná chvíle volit mezi háčkem a rozdělením, protože tehdy je v ruce konkrétní potřeba. Háček navržený dopředu, bez frameworku, který ho potřebuje, je odhad. Rozšiřovací body, které vznikly pro skutečného druhého uživatele, se osvědčily: deskriptor a sdílené jazykové vrstvy (`CSharpEntityParsing`, `JavaEntityParsing`, `JakartaPersistence`, `TransactSql`) nesou wrappery, které nad nimi přidávají málo — EclipseLink 226 řádků nad `JakartaPersistence` (revize z 2026-09-21).

**Rozdělení by stálo hodně a nepřineslo nic.** Báze, na které stojí šest wrapperů, sdílené čtecí vrstvy i doplnění z katalogu, by se přepisovala jednorázově (rozh. [003](003-one-shot-migration.md)) bez jediného rozdílu v chování, s rizikem regrese v každém směru překladu. Schválený záměr jmenuje právě šest frameworků, které nástroj má, a projekt končí 2026-12-01; sedmý, který by rozdělení využil, v projektu není.

**Hlídat čísla, která vedou k jediné odpovědi, nemá smysl.** Překročený práh by bez nového frameworku spustil rozhodnutí bez konkrétní potřeby — a to by dopadlo jako tohle. Měření, jehož výsledek nemůže nic změnit, nezaznamenáváme.

## Důsledky

**Kód se nemění.** Nález revizí zůstává vyslovenou mezí, ne otevřenou položkou.

**§7 odkazuje sem.** Oddíl *Přidání nového frameworku* říká, že potřeba mimo sedm kroků a deskriptor je změna `AbstractWrappers`, tedy rozhodnutí — háček, nebo rozdělení báze — učiněné nad potřebou toho frameworku.

**Nárok S1 se vyslovuje nad šesti frameworky.** Text práce uvede S1 jako doložený šesti wrappery a změřeným oddělením (jména frameworků ve sdílených vrstvách, velikost wrapperů nad sdílenými vrstvami) a pojmenuje mez: sedmý framework s potřebou mimo sedm kroků zaplatí změnou sdílené vrstvy.

**Dotazové báze se rozhodnutí netýká.** `AbstractQueryBuilder` rostl i s plochou, kterou wrapper přepíše (16 → 21 členů mezi revizemi), takže otázka u něj nevzniká.
