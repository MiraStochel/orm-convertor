# 185 — Jméno sady EF Core jmenuje entitu, kterou kontext převodu deklaruje: oba průchody čtou přes jeden kontext čtení nalezený přes builder entit, a jméno sady faktem mezireprezentace není

Datum: 2026-10-25
Stav: platí
Požadavky: F10, F11, F14, S1, S2
Podklad: rozhodnutí [026](026-home-of-shared-query-reading.md), [050](050-one-home-for-the-singular-plural-heuristic.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [084](084-mybatis-wrapper-over-two-language-layers.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md), [114](114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md), [132](132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md) a [158](158-a-property-declared-dbset-declares-a-set-whatever-supplies-its-value.md); nález V7 výňatku Cofoundry ([`analysis/real-world-sources.md`](../analysis/real-world-sources.md)); JSS článek, pravidlo Q2 a §4.3; dokumentace EF Core 10, *DbSet<TEntity> Class* a *Table name*

## Kontext

**Kořen `ctx.PageDirectories` se čte jménem, a jméno sady není jméno entity.** Dotazový průchod EF Core bere za kořen člen jména (rozhodnutí 026 a 114) a jméno členu dává dál sdílenému čtení LINQ, které k němu hledá mapování převodu třemi otázkami pravidla Q2 (`LinqQueryParser.MapFor`): je to jméno tabulky, jméno entity, nebo druhé číslo jména entity podle heuristiky koncového „s" rozhodnutí 050. Výňatek Cofoundry (rozhodnutí 184) ukázal vstup, na který žádná z otázek neodpoví: kontext uvádí `DbSet<PageDirectory> PageDirectories` a konfigurace jmenuje tabulku `ToTable(nameof(PageDirectory), …)`. Jméno `PageDirectories` není tabulkou (`PageDirectory`), není entitou a není ani jejím druhým číslem (`PageDirectory` + „s" je `PageDirectorys`). Čtení pak vzdá hledání, vezme jméno sady za tabulku a cíl vydá dotaz nad tabulkou `PageDirectories` s typem výsledku `PageDirectorie`, odvozeným podle rozhodnutí 050 se záznamem `Convention` — jediným, který o tom mluví. Do Dapperu či MyBatisu je to SQL nad tabulkou, která v databázi není; do Hibernatu, EclipseLinku či NHibernatu dotaz nad entitou, kterou artefakty nedeklarují, takže se nezkompiluje. Je to vada ve smyslu rozhodnutí 053 a 070: artefakt, který cíl odmítne, nebo který vrátí jiné řádky než zdroj, vydaný s jediným záznamem, který ho za vadu neoznačuje.

**Není to okraj.** Totéž postihne každou sadu, jejíž jméno vzniklo jinou pluralizací než glued „s" — `Categories`, `Companies`, `Addresses`, `People`, `Statuses` — všude, kde anotace či konfigurace jmenuje tabulku jinak než jménem sady. Kde tabulku nejmenuje nic, konvence EF Core ji pojmenuje po sadě (`Categories`) a rozhodnutí 132 ji tak i materializuje, takže první otázka Q2 odpoví a vada se neprojeví; stačí ale `[Table("Category")]` nebo `ToTable("Category")`, tedy přesně to, co píše projekt, který si tabulky pojmenoval sám. Výňatek Cofoundry vadu obešel přepisem šesti kořenů na `Set<PageDirectory>()` a jednoho na `Set<PagePublishStatusQuery>()` (jeho README, změna 9) — tedy úpravou textu projektu kvůli nástroji, což je přesně to, co rozhodnutí 184 u případové studie chce mít vyznačené a co nárok T1 oslabuje.

**Nástroj přitom odpověď zná.** Parser fluent konfigurace čte vlastnosti `DbSet<T>` kontextu (rozhodnutí 132) a zapisuje páry entita → jméno sady do `EFCoreReadingContext.AddDbSet`, aby parser třídy mohl jméno sady materializovat jako tabulku podle rozhodnutí 067. Kontext čtení ale vzniká v `ParserFactory` pro každé volání zvlášť, a orchestrace továrnu volá dvakrát — jednou pro entitní průchod, jednou pro dotazový —, takže dotazový parser dostane prázdný kontext a o párech neví. Dotazový průchod má k dispozici jen `EntityMaps`, a tabulka v nich stojí už pojmenovaná konfigurací; jméno sady do mezireprezentace nedoputuje a doputovat ani nemá kam.

Rozhodnout je třeba, kudy se jméno sady dostane od entitního průchodu k dotazovému: zda se stane faktem mezireprezentace u entity, nebo zda oba průchody sdílejí kontext čtení; a jak se pak kořen jménem sady čte vedle dnešních otázek Q2.

## Zvažované varianty

### 1 — Ponechat a vyslovit mez

Kořen by se dál četl jen otázkami Q2 a mez — „sada pojmenovaná jinak než entita s ‚s' nad tabulkou pojmenovanou jinak než sada" — by šla do katalogu podmnožiny. Zamítáme. Není to konstrukce, kterou nástroj nepřečte, ale dotaz, který si vymyslí: tabulku i typ výsledku, které zdroj neklade, se záznamem `Convention`, který říká „odvodili jsme jméno", ne „dotaz je špatně". Rozhodnutí 053 a 070 zakazují vydat dotaz, který vrací jiné řádky než zdroj, a F11 zakazuje nepodporovanou konstrukci potichu vynechat; tiše ji zkomolit je horší než obojí. A nástroj to vědět umí — mez by zakrývala, že odpověď leží v jednotce, kterou entitní průchod už přečetl.

### 2 — Jméno sady jako fakt mezireprezentace u entity

`EntityMap` by nesl jméno sady, která entitu vystavuje, tak jako nese `SourceKeyClass` u klíče, a `MapFor` sdíleného čtení by se ptal i na něj. Varianta je lákavá, protože dotazový průchod dnes dostává jen `EntityMaps`, a tak by nic jiného nepotřeboval. Zamítáme ze tří důvodů.

**Jméno sady nic nemapuje.** Mezireprezentace je rámcově nezávislý popis mapování mezi třídami a schématem (JSS článek, §4.3); každý její fakt má na jedné straně třídu a na druhé databázi, a cílový framework ho má kam zapsat. Jméno `DbSet` vlastnosti má jen jednu stranu: je to jméno členu v API jedné aplikace nad jedním frameworkem. Co z něj EF Core odvozuje pro databázi — tabulku — rozhodnutí 132 už materializuje podle 067 jako fakt tabulky, a to je celý jeho mapovací obsah. `SourceKeyClass` je jiný případ: klíčová třída je tvar mapování, který cíl musí reprodukovat (`@IdClass`/`@EmbeddedId` v JPA, `<composite-id>` v NHibernatu), takže ji cílové buildery čtou. Jméno sady by nečetl žádný.

**Fakt bez konzumenta by museli obcházet všichni.** Co leží v `Model`, vidí každý builder, fáze doplnění z katalogu, účetní kniha faktů rozhodnutí 124 i měření úplnosti mapování experimentálního běhu (rozhodnutí 173), a každý by musel vědět, že tento fakt má přeskočit: kniha by ho musela zařadit do kategorie, kterou žádný artefakt nenese, měření úplnosti by ho muselo vyloučit, aby nehlásilo ztrátu, kterou nikdo neztratil. Jeden framework by svou potřebou při čtení zatížil sdílené jádro, které S1 drží od frameworků čisté.

**Dva průchody téhož frameworku už mají, jak si předat, co jeden přečetl a druhý potřebuje.** Rozhodnutí 084 to řešilo pro MyBatis, kde dotazový průchod potřebuje podpisy metod rozhraní přečtené v entitním průchodu: jeden kontext čtení na převod, nalezený přes builder entit, který orchestrace tvoří jednou a oběma voláním továrny předává. Fakt, který potřebuje jen čtení jednoho frameworku, patří do kontextu čtení toho frameworku, ne do mezireprezentace všech.

### 3 — Sémantický model jednotky podle rozhodnutí 114

Deklaruje-li jednotka typ `ctx` i jeho člen `PageDirectories` jako `DbSet<PageDirectory>`, sémantický model Roslynu, který čtení od rozhodnutí 109 nad jednotkou staví, typový argument zná, a kořen by se mohl číst jako `Set<PageDirectory>()`. Zamítáme jako jediný mechanismus, protože odpovídá jen tam, kde kontext stojí v téže jednotce jako dotaz, a to je nejméně častý tvar: kontext stojí obvykle ve vlastním souboru (rozhodnutí 114 to říká při zamítnutí své varianty 2), v Cofoundry ve `Data/CofoundryDbContext.cs`, zatímco každý dotaz v jiném souboru ve `Queries/`. Model se také staví jen tam, kde jednotka jméno příjemce deklaruje, takže by to byla druhá cesta vedle kontextu čtení, pro zlomek vstupů. Jeden mechanismus pro všechny tvary je lepší než dva; sdílený kontext odpoví i v téže jednotce, protože entitní průchod ji přečte dřív než dotazový.

### 4 — Jeden kontext čtení pro oba průchody a kořen jménem sady jako `Set<T>()` její entity

## Rozhodnutí

**Volíme variantu 4. `EFCoreReadingContext` je jeden na převod, nalezený přes builder entit (`EFCoreReadingContext.For`, týž tvar jako `MyBatisReadingContext.For` rozhodnutí 084), a továrna parserů ho dává oběma seznamům — entitnímu i dotazovému. Dotazový parser EF Core čte kořen `x.M`, jehož jméno `M` je jménem sady, kterou některý kontext převodu deklaruje `DbSet<T>`, jako `Set<T>()` té entity; teprve jméno, které žádný kontext převodu nedeklaruje, jde do otázek Q2 jako dosud. Jméno sady se do mezireprezentace nezapisuje.**

**Pořadí: nejdřív co kontext deklaruje, pak Q2.** Deklarace `DbSet<PageDirectory> PageDirectories` je výpověď jednotky o jménu, a rozhodnutí 114 výpověď jednotky staví nad všechno ostatní; shoda jména sady se jménem tabulky či entity jiné entity je oproti ní náhoda jmen. Jméno, které kontext deklaruje, se proto bere dřív než otázky Q2, a výsledek je přesně ten, jaký dává `Set<T>()` s týmž typovým argumentem (rozhodnutí 158 už zavedlo, že jméno deklarované `DbSet<T>` je sadou své entity, ať se jmenuje jakkoli; tady to platí i pro člen kontextu, jehož deklarace leží v jiné jednotce). Zbytek čtení — tabulka podle mapy entity, schéma, sloupce, vztahy — se nemění, protože jde dál přes `MapFor(entita)`.

**Čtyři tvary jednoho kořene.** Pravidlo platí pro každý zápis kořene jménem, který rozhodnutí 111 a 114 uznávají: `ctx.M`, `this._ctx.M`, a uvnitř kontextu `M` i `this.M`. Všechny dávají `Set<T>()` entity, kterou kontext u `M` deklaruje. `Set<T>()` sám, jméno deklarované `DbSet<T>` (rozhodnutí 158) a explicitní načtení (115) se nemění, protože jmenují entitu samy.

**Kontext mimo převod: platí Q2.** Nedeklaruje-li jméno `M` žádný kontext, který převod nese — dotazový fragment nad `ctx`, nebo projekt poslaný bez svého kontextu —, převod o sadě nic neví a kořen se čte jako dosud: tabulka, entita, druhé číslo, jinak jméno sady jako tabulka s odvozeným typem výsledku a záznamem `Convention`. Je to vyslovená mez: kdo chce, aby se sady četly podle svých typů, pošle kontext. Hranice je stejná jako u rozhodnutí 132 — mapování, které stojí v kontextu, se čte, když kontext v převodu je.

**Dvě sady téhož jména nad různými entitami.** Převod se dvěma kontexty, které vystavují pod týmž jménem různé entity, není modelem jedné aplikace a EF Core by ho nikdy nepostavil; nástroj ho přečte, a jméno pak neurčuje entitu. Takové jméno kontext nezná a kořen jde do Q2 jako jméno, které nikdo nedeklaroval. Opačný směr — jedna entita pod dvěma jmény — hlásí už rozhodnutí 132 záznamem `Conflict` a první jméno stojí.

**Co je fakt C# a co fakt EF Core.** Že `DbSet<T>` vystavuje entitu `T`, je fakt EF Core, a bydlí proto v jeho wrapperu: v jeho kontextu čtení a v jeho rozpoznávači kořene (`TryReadQueryRoot`). Sdílené čtení LINQ dostane kořen pojmenovaný entitou, jako od `Set<T>()`, a `MapFor` nezná žádnou novou otázku; mění se v něm jediné místo — vnitřní posloupnost joinu se dosud jmenovala jen podle textu (`NameOfSource`) a nově se na ni nejdřív ptá rozpoznávače kořene wrapperu, jako se ptá na hlavu řetězu, protože `ctx.Categories` v `Join` je tatáž sada jako `ctx.Categories` na začátku dotazu. NHibernate se nemění: jeho kořen `Query<T>()` entitu jmenuje. Orchestrace se mění jen tím, že továrna parserů najde kontext přes builder místo `new`, což je tvar, který už má pro MyBatis; `AbstractWrappers` ani `Model` se nemění (S1).

## Důsledky

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR navenek.** Kořen jménem sady, kterou kontext převodu deklaruje a jejíž jméno není jménem entity s „s", dával artefakt nad tabulkou a typem, které zdroj neklade; dává artefakt `Set<T>()`. Každý jiný vstup zůstává: jméno sady rovné jménu tabulky nebo entity s „s" dává tutéž odpověď oběma cestami, fragment nad `ctx` bez kontextu jde do Q2 jako dosud. Matice směrů, matice kategorií, diferenční matice i katalogy LDBC a FIBEN nesou fragmenty nebo kontexty, jejichž sady se jmenují entitou s „s", takže jejich artefakty a záznamy se nemění, a tím se změna ověří.

**Výňatek Cofoundry se vrací k textu projektu.** Sedm kořenů přepsaných na `Set<T>()` dostává zpět `.PageDirectories` a `.PagePublishStatusQueries`, změna 9 jeho README mizí a nález V7 v [`analysis/real-world-sources.md`](../analysis/real-world-sources.md) se značí jako opravený. Nárok T1 tím ztrácí jednu z úprav textu kvůli nástroji; počty zadání, které z výňatku počítá `Combined/CaseStudyCriteriaTest`, se nemění, protože dotazy se četly i přes `Set<T>()`.

**Vyslovená mez** jde do [`subset.md`](../subset.md): kořen jménem sady, kterou žádný kontext převodu nedeklaruje a která není jménem tabulky, entity ani jejím druhým číslem, se čte jako tabulka toho jména s odvozeným typem výsledku a záznamem `Convention` (Q2, rozhodnutí 050).

**Testy.**

- Kontext v jiné jednotce s `DbSet<Category> Categories` a tabulkou `Category` — jednou `[Table]`, jednou `ToTable` v `OnModelCreating`, jednou v `IEntityTypeConfiguration<T>`: `ctx.Categories.Where(…)` dává týž artefakt jako `ctx.Set<Category>()`, bez záznamu `Convention` o typu výsledku a bez `Failure`.
- Totéž s kontextem v téže jednotce, přes pole `this._ctx.Categories`, a uvnitř kontextu (`Categories`, `this.Categories`).
- Tatáž sada jako cíl joinu (`ctx.Categories` v `Join`) a v poddotazu.
- Sada, jejíž jméno je entitou s „s" (`Orders`): beze změny proti dnešku.
- Bez jednotky kontextu: `ctx.Categories` se čte jako dosud — tabulka `Categories`, typ `Categorie`, záznam `Convention` (mez).
- Dva kontexty se sadou `Items` nad různými entitami: jméno jde do Q2.
- Všechny matice a katalogy beze změny očekávaných výstupů.
