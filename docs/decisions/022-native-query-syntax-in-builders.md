# 022 — Nativní syntaxe cílového frameworku v dotazových builderech

Datum: 2026-08-19
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [009](009-target-framework-descriptor.md) a [010](010-diagnostics-as-returned-data.md); JSS §6.2, pravidla Q13–Q15, a Table 2; `analysis/orm-frameworks-comparison.md` §15

## Kontext

Dotazová větev má dnes jediný builder — `DapperSqlQueryBuilder` — a u něj otázka, co se má vygenerovat, nevzniká: Dapper nemá nic než SQL řetězec. U EF Core a NHibernate vzniká hned prvním řádkem. Tytéž dotazové instrukce jde vyjádřit dvojím způsobem a obojí je legitimní kód cílového frameworku:

| | nativní tvar | úniková cesta na syrové SQL |
|---|---|---|
| EF Core 10.0.10 | řetěz LINQ nad `ctx.Set<T>()` | `FromSql`, `FromSqlRaw`, `SqlQuery<T>` |
| NHibernate 5.7.0 | HQL, LINQ přes `session.Query<T>()`, QueryOver, ICriteria | `CreateSQLQuery` |
| Dapper 2.1.79 | — | SQL je jediný tvar |

Původní návrh z diplomky, ze které projekt vzešel, na to odpovídal dvěma abstraktními metodami: `Build()` pro nativní syntaxi a `BuildSQL()` pro syrové SQL. **V kódu `BuildSQL()` neexistuje**; `AbstractQueryBuilder` má jedinou abstraktní `Build()` vracející `List<ConversionSource>`. Otázka tedy nikdy nebyla zodpovězená, jen odložená tím, že hotový builder je jeden a je pro framework, kde se neptá.

Odložit ji dál nejde. Je to první větev obou chybějících builderů a zároveň volba, která rozhoduje o tom, co vlastně měří experimentální část práce.

**Druhá polovina otázky je hlášení.** Pravidlo Q14 říká, že cílové frameworky se v expresivitě liší a že builder smí nepodporovanou instrukci přepsat, rozložit nebo částečně vyhodnotit; pravidlo Q15 zužuje záruku překladu na pořadí instrukcí a strukturu podmínek, ne na prováděcí plán. Obojí předpokládá, že se o takovém zásahu ví. Dotazová větev ale **nemá kam hlásit vůbec nic**: `Records` a `Report` sedí na `AbstractEntityBuilder`, `AbstractQueryBuilder` je nemá a `ConversionHandler` vrací jen záznamy entitního builderu. Dnešní stav jde ještě dál — když cílový framework dotazový builder nemá nebo zdrojový framework nemá dotazový parser, `ConversionHandler` dotaz zahodí prostým `continue` bez jediného záznamu. To je doslovné porušení rozhodnutí [010](010-diagnostics-as-returned-data.md) i požadavku F11, který zakazuje potichu vynechat nepodporovanou konstrukci, a týká se osmi z devíti směrů.

Rozhodnout je proto třeba dvojí naráz, protože druhé je podmínkou prvního: **co builder generuje** a **jak řekne, že něco vyjádřit nedokázal**.

## Zvažované varianty

### 1 — Syrové SQL do všech cílů

Každý builder by složil SQL a obalil ho únikovou cestou svého frameworku. Je to nejlevnější varianta, protože visitor by byl jeden a už existuje, a pro diferenční ověření podle F13 nejpohodlnější: obě strany porovnání by běžely na stejném textu.

Zamítáme ze tří důvodů a každý sám o sobě stačí.

**Měřilo by se něco jiného, než co má práce měřit.** Požadavek T2 žádá matici překladů dělenou podle kategorií dotazů a T3 podíl spustitelných výstupů a funkční ekvivalenci. Kdyby všechny tři cíle dostaly tentýž SQL řetězec, matice by neporovnávala překlad do tří ORM, ale tři obaly kolem jednoho SQL — a rozdíly, kvůli kterým se ORM vybírá, by z měření zmizely. Totéž platí pro Advisor: jeho vstupem je naměřený výkon, a ten by se u tří frameworků nad identickým SQL lišil jen režií materializace.

**Úniková cesta není u všech cílů plnohodnotná.** `FromSqlRaw` v EF Core je *zdroj*, nad kterým se dál skládá LINQ, a syrové SQL v něm musí vrátit sloupce mapované entity. Projekci do anonymního typu, agregaci se seskupením ani join na nesouvisející entitu tedy únikovou cestou vyjádřit nelze — musely by se stejně napsat v LINQ. „Syrové SQL všude" by proto nebylo jedno pravidlo, ale pravidlo s výjimkami přesně tam, kde leží zajímavé kategorie T2.

**Odporuje to pravidlům Q14 a Q15.** Obě mluví o tom, že se instrukce mapují na *konstrukce cílového frameworku* a že omezení expresivity se řeší přepsáním nebo rozkladem. Varianta, která expresivitu cíle obejde, ta pravidla nesplňuje, jen obchází.

### 2 — Generovat obojí, nativní tvar i SQL

Každý cíl by vydal dva artefakty. Pro T2 a T3 je to nejbohatší materiál a pro F13 by SQL varianta byla přirozeným rozhodčím. Zamítáme jako rozsah, ne jako myšlenku: zdvojuje práci na builderech ve chvíli, kdy ještě ani jeden ze dvou chybějících neexistuje, a mezireprezentace by musela říct, který z artefaktů je závazný — jinak není jasné, co se vlastně přeložilo. Vracíme se k tomu, až budou nativní buildery hotové; tehdy je přidání SQL varianty rozšířením, ne přepisem.

### 3 — Nativní tvar cíle, úniková cesta jen tam, kde nativní tvar není, a vždy se záznamem

## Rozhodnutí

**Volíme variantu 3. Každý dotazový builder generuje nativní dotazový tvar svého frameworku — EF Core řetěz LINQ, NHibernate HQL, Dapper SQL. Únikovou cestu na syrové SQL použije jen tam, kde cíl instrukci nativně vyjádřit neumí, a takový případ je vždy záznamem, nikdy tichou náhradou.**

**Nativní tvar je definice překladu, ne preference.** Nástroj má z popisu dotazu vyrobit dotaz, jaký by v cílovém frameworku napsal člověk. Tabulka 2 článku pojmenovává u NHibernate a EF Core „LINQ / HQL", resp. „LINQ" jako to, čím se v těch frameworcích dotazuje, a řadí oba mezi zdroje i cíle vhodnosti *High* právě proto, že dotaz v nich je strukturovaný, ne řetězec. Vygenerovat do nich řetězec by tu vlastnost zahodilo.

**Který nativní tvar, když jich framework nabízí víc.** U EF Core je volba jediná. U NHibernate volíme **HQL**, ne LINQ, QueryOver ani ICriteria. Důvody jsou tři. HQL pokrývá celý rozsah dotazové mezireprezentace včetně kategorií, které se do LINQ převádějí hůř. Je to tvar, který má NHibernate a ostatní .NET frameworky nikoli, takže matice T2 získá datový bod, který by jinak neměla — jinak by oba plnohodnotné .NET cíle vydávaly LINQ a lišily by se jen jménem kořene. A je textový, takže se skládá týmž `StringBuilder`em jako SQL a dá se ověřit bez spuštění (viz Důsledky). Kdyby se ukázalo, že některou kategorii HQL nepokrývá, není to návrat k tomuto rozhodnutí, ale případ pravidla o únikové cestě.

**Úniková cesta má jednu podmínku a jeden důsledek.** Podmínka: nativní tvar cíle danou instrukci vyjádřit neumí — ne že by se nám nechtělo. Důsledek: vzniká záznam podle rozhodnutí [010](010-diagnostics-as-returned-data.md), který pojmenuje dotazovou vlastnost, kvůli které se ustoupilo. Tím se úniková cesta chová stejně jako `SourceSqlType` u typů a `SourceStrategyName` u strategií klíče: je to zaznamenaný ústup vedle definice, ne náhrada za ni.

**Dotazová větev dostává tentýž diagnostický kanál, jaký má entitní.** `AbstractQueryBuilder` dostane `Records`, `Report` a odkaz na `Descriptor` svého frameworku — tytéž tři členy, které na `AbstractEntityBuilder` zavedlo rozhodnutí 010. `ConversionHandler` záznamy dotazových builderů připojí k záznamům entitního builderu do jediného seznamu `ConversionResult.Records`. Kanály jsou dva proto, že dotazový builder vzniká zvlášť pro každý dotaz; sjednocení je jedno, protože volající se ptá na jeden převod.

**Nepodporovanou dotazovou vlastnost pojmenovává vlastní uzavřený slovník `QueryFeature`, ne `MappingFactCategory`.** Kategorie mapovacích faktů popisují tabulku, sloupec a klíč; dotazová ztráta se týká instrukce. Slovník sleduje kategorie, kterými požadavek T2 dělí dotazovou matici — projekce, filtrace, join, druh joinu, agregace, seskupení, filtrace po agregaci, řazení, stránkování, poddotaz, množinová operace, parametr dotazu. Stav vyjádřitelnosti se deklaruje v deskriptoru cílového frameworku vedle mapovacích kategorií a hlásí se z něj **mechanicky**, přesně jako `ReportLosses` u entit: builder ztrátu nevypisuje ručně, plyne z deklarace. Rozhodnutí [009](009-target-framework-descriptor.md) tenhle argument už jednou vedlo a platí beze změny — deklarace, kterou nikdo nečte, se rozejde s emisí, kterou nikdo nekontroluje.

Konkrétní příklad, na kterém je vidět, proč slovník potřebuje i *druh* joinu zvlášť: EF Core 10 přinesl operátory `LeftJoin` a `RightJoin`, takže z hodnot `JoinKind` zůstává nevyjádřitelný jedině `Full`. Kdyby slovník znal jen kategorii „join", nešlo by tenhle rozdíl deklarovat, a plný vnější join by se buď tiše zúžil na vnitřní, nebo by builder musel ztrátu hlásit ručně — obojí je stav, kterému rozhodnutí 009 předchází.

**Dva tiché `continue` v `ConversionHandler` se stávají záznamy.** Chybějící dotazový builder u cíle a chybějící dotazový parser u zdroje jsou obojí `Failure`: vstup byl předán a výstup z něj nevznikl. Do doby, než bude matice úplná, je to jediná věc, která uživateli řekne, že se dotaz nepřeložil — a po jejím dokončení zůstává jako správná odpověď na framework, který se teprve přidává.

**`BuildSQL()` z původního návrhu nezavádíme.** Zůstává jediná abstraktní `Build()`. Dvě výstupní metody by tvrdily, že syrové SQL je souběžný výstup rovnocenný nativnímu, což je varianta 2, kterou odkládáme; a u Dapperu by jedna z nich byla vždy prázdná. Až se varianta 2 otevře, je to přidání dalšího artefaktu do vráceného seznamu, ne druhá abstraktní metoda — `Build()` vrací seznam právě proto, že počet artefaktů je vlastností frameworku.

## Důsledky

**Odblokovává se položka „Nativní syntaxe versus syrové SQL v dotazových builderech"** z `open-items.md`, vytčená jako předpoklad dotazových builderů pro EF Core i NHibernate. Oba se teď dají psát.

**NHibernate bude nesymetrický a je to vědomé.** Jako cíl vydává HQL, jako zdroj se bude číst LINQ — HQL parser by znamenal buď vlastní gramatiku, nebo referenci na NHibernate uvnitř wrapperu, což je přesně to, co wrappery dělat nemají (S1). Převod NHibernate → NHibernate tedy proběhne, ale nebude textovým round-tripem, a HQL na výstupu žádný parser zpět nepřečte. Zapisujeme to jako známý stav, ne jako mezeru k tichému opravení; kdy a jak přibude HQL parser, je otázka mimo tohle rozhodnutí.

**Ověření nativního tvaru je silnější než ověření řetězce a nepotřebuje databázi.** U EF Core přeloží `ToQueryString()` celý výraz na SQL bez spojení a bez provedení, takže neplatný řetěz LINQ se pozná okamžitě. U NHibernate zkompiluje HQL proti namapovanému modelu plán dotazu, rovněž bez spojení, a odmítne neexistující entitu i vlastnost — což je zároveň kontrola pravidla Q13. U Dapperu žádný framework SQL neposuzuje, takže nejsilnějším dostupným tvrzením je, že se SQL rozparsuje a že se odkazované tabulky a sloupce najdou v mezireprezentaci. Podrobnosti patří ke stupňům ověření podle rozhodnutí [016](016-generated-artifact-verification-levels.md), které se tím rozšiřují o dotazovou větev; tvrzení o verzích se tím opět váže na tabulku v `architecture.md`.

**S1 se neporušuje.** Přibývají tři členy na `AbstractQueryBuilder` a jedna kategorie deklarací v deskriptoru — obojí je součást překladového kontraktu, ne znalost o konkrétním frameworku. Čtvrtý framework deklaruje svou dotazovou expresivitu ve vlastním deskriptoru a nic v `AbstractWrappers` ani v orchestraci kvůli němu měnit nemusí. Slovník `QueryFeature` je uzavřený ze stejného důvodu jako `MappingFactCategory`: jmenuje kategorie požadavku T2, ne konstrukce jednoho frameworku.

**Determinismus podle S2 se nemění.** Volba tvaru je funkcí cílového frameworku a deskriptoru, ne vstupu; tentýž model dá tentýž výstup.

**Testy.** Pro každý cíl aspoň jeden dotaz v každé kategorii rozsahu (projekce, filtrace, join, agregace, řazení) s tvarovou asercí, a k tomu negativní případ: instrukce, kterou deskriptor cíle označí za nevyjádřitelnou, musí skončit záznamem a artefakt musí vzniknout bez ní. Dále test, že překlad dotazu do cíle bez dotazového builderu vydá `Failure`, a ne mlčení.
