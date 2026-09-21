# 090 — Cross-ecosystem překlad je celý převod přes vyslovenou hranici a matice si ho počítá sama

Datum: 2026-09-21
Stav: platí
Požadavky: F6, F10, S1, T2
Podklad: rozhodnutí [009](009-target-framework-descriptor.md), [016](016-generated-artifact-verification-levels.md), [086](086-target-database-dialect-declared-by-the-descriptor.md), [087](087-an-integration-test-is-a-run-against-the-database.md) a [089](089-differential-verification-as-the-fourth-level-over-a-query.md); [`requirements.md`](../requirements.md), F10 a T2; [`architecture.md`](../architecture.md) §6.2; otevřená položka „Matice překladů F10 se nepočítá sama"

## Kontext

Ověřovací kritérium F10 má dvě věty: **testovací matice s alespoň jedním end-to-end scénářem pro každou dvojici ekosystémů** a **nejméně třicet cross-ecosystem překladů celkem**. Matice v repozitáři je, a to v podobě, jakou žádá samo znění F10 — překlad z libovolného .NET frameworku do libovolného javového a zpátky. `QueryMatrixTest` projde součin `ORMEnum` se sebou, tedy všech šestatřicet směrů, a o každém tvrdí, že vydá dotazový artefakt a že o něm nemlčí; `CrossEcosystemTest` k tomu drží tvar tam, kde oba ekosystémy tentýž fakt hláskují jinak; `ConsumerProjectFactsTest` soudí každý směr znovu z úhlu rozhodnutí [040](040-boundary-of-the-handed-over-artifact.md), `RunRecordTest` z úhlu determinismu (S2) a `ArtifactCarriesNoCredentialsTest` z úhlu S4. Od 2026-09-21 k tomu přibyla diferenční matice rozhodnutí 089, která třicet dvojic dotazů dovádí až k porovnání řádků.

**Žádný test ale nepočítá to, co kritérium počítá.** Kolik je cross-ecosystem překladů, se dá dnes zjistit jedině tak, že člověk přečte několik testovacích tříd, sečte metody, u parametrizovaných je vynásobí počtem hodnot výčtu a od součtu ještě odečte směry, které hranici nepřekračují. Číslo získané takhle je přesně ten druh tvrzení, který tenhle projekt jinde odmítá: rozhodnutí [034](034-central-version-management.md) zavedlo jediné strojově psané místo pro verzi, aby dvě místa nemohla tvrdit každé jiné číslo, rozhodnutí 087 nechalo javovou sadu počítat si testy samu a rozhodnutí 089 totéž u dvojic diferenční matice. Nárok na F10 opřený o ruční přepočítání by zestárnul prvním přidaným směrem a nikdo by se to nedozvěděl.

**Ani jeden z obou pojmů nemá vyslovený význam.** „End-to-end scénář" v repozitáři není nikde; „cross-ecosystem překlad" také ne. A co je vlastně ekosystém, nevyslovuje řešení vůbec: že Hibernate, EclipseLink a MyBatis stojí na jedné straně a Dapper, EF Core a NHibernate na druhé, ví dnes jediné místo v celém repozitáři, a je jím **ruční seznam `javaSide` v `DifferentialMatrixTest`** — tedy přesně ten tvar zápisu, který sedmý framework tiše mine.

**Jedna věc je přitom zřejmá už z aritmetiky.** Šest frameworků dává osmnáct směrů přes hranici, a kritérium žádá třicet překladů. Kritérium tedy **nepočítá směry**: ani dokonale úplná matice všech osmnácti směrů by na třicet nedosáhla. Počítá se něco jemnějšího, a co to je, musíme vyslovit my.

Otázky jsou tři a každá má víc než jednu rozumnou odpověď: **co je ekosystém**, **co se počítá** a **kdo počítá**.

## Zvažované varianty

### 1 — Spočítat rukou a číslo zapsat do dokumentace

Do [`traceability.md`](../traceability.md) k řádku F10 napsat „třicet tři cross-ecosystem překladů, z toho čtyři end-to-end" a hotovo.

Zamítáme ze stejného důvodu, z jakého totéž zamítlo rozhodnutí 087 u F12. Není to kontrola, je to tvrzení, které se s prvním přidaným scénářem tiše rozejde se skutečností — a rozejde se právě v dokumentu, jehož jediným úkolem je držet vazbu požadavku na důkaz.

### 2 — Cross-ecosystem překladem je směr

Nejjednodušší mechanická definice: dvojice (zdrojový framework, cílový framework) přes hranici je jeden překlad.

Zamítáme, protože definice nesplní kritérium ani tehdy, když je matice úplná. Směrů přes hranici je osmnáct, kritérium žádá třicet, a víc jich při šesti frameworcích být nemůže. Definice, při které se doslovné znění kritéria nedá splnit jinak než přidáním sedmého frameworku, měří něco jiného, než co kritérium měřit chtělo.

### 3 — Cross-ecosystem překladem je každá aserce nad převodem přes hranici

Počítalo by se, kolikrát nějaký test něco tvrdí o převodu, jehož konce leží v různých ekosystémech.

Zamítáme, protože se tím počítají tvrzení, ne překlady. Týž převod soudí dnes `QueryMatrixTest` dvakrát, `ConsumerProjectFactsTest` třikrát a `RunRecordTest` s `ArtifactCarriesNoCredentialsTest` ještě dvakrát, takže jediný směr by do počtu přispěl sedmi; číslo by se dalo zvednout rozepsáním jedné aserce na dvě, aniž by nástroj přeložil cokoli navíc. Kritérium žádá překlady a překlad je to, co nástroj provedl, ne to, co o tom sada řekla.

### 4 — Cross-ecosystem překladem je jen ten, který doběhl proti databázi

Opačná krajnost: počítá se jedině 4. stupeň rozhodnutí 016, tedy to, co dnes dělá diferenční matice.

Zamítáme jako příliš úzké, a to ze dvou důvodů. Kritérium má **dvě** věty a tahle definice by z druhé udělala zesílenou první — „aspoň jeden end-to-end scénář na dvojici ekosystémů" a „aspoň třicet end-to-end překladů" jsou pak jedno tvrzení řečené dvakrát a první věta nemá co tvrdit. A počtem by to nevyšlo: diferenční matice má osmnáct dvojic přes hranici, takže bychom ji museli nafouknout ze šesti dotazů na deset jenom proto, abychom dosáhli na číslo — a měřila by přitom pořád totéž, jen desetkrát místo šestkrát.

### 5 — Celý převod scénáře přes vyslovenou hranici, počítaný sadou

## Rozhodnutí

**Volíme variantu 5, ve třech krocích: ekosystém vyslovuje deskriptor frameworku; cross-ecosystem překlad je celý převod jednoho scénáře přes tuhle hranici, počítaný jednou za trojici (scénář, zdroj, cíl); a end-to-end scénář je takový překlad dovedený na 4. stupeň rozhodnutí 016. Kolik jich je, si sada tvrdí sama.**

### Ekosystém vyslovuje deskriptor

Ekosystém je fakt o frameworku a patří tam, kde jsou ostatní fakty o frameworku: do deskriptoru. Přibývá uzavřený slovník `Ecosystem` v `Model`, vedle `ORMEnum` a `DatabaseDialect`, se dvěma hodnotami — `DotNet` a `Java` —, a `TargetFrameworkDescriptor` dostává povinné pole `Ecosystem`. Je to fakt téhož tvaru, jaký sem poslalo rozhodnutí 086 u dialektu: nástroj ho nevymýšlí, framework ho má.

Deklarovaný, ne odvozený. Odvodit ekosystém z obsahových typů, které builder vydává, by znamenalo tvrzení o výstupu tam, kde kritérium tvrdí něco o frameworku — a framework, který by v daném převodu nevydal nic, by neměl ekosystém žádný. Hlavně ale: **ruční seznam v testu je ten zápis, který nová hodnota výčtu mine.** Seznam `javaSide` v `DifferentialMatrixTest` by u sedmého frameworku nic neřekl; deskriptor u něj chybět nemůže, protože pole je povinné a rozhodnutí 009 spolu s `TargetFrameworkDescriptorTest` drží, že deskriptor má každá hodnota `ORMEnum`. Nový wrapper tím přináší svůj ekosystém stejně, jako přináší svou verzi, svůj dialekt a své vynucené členy.

**Překladová cesta se podle pole nevětví a větvit nemá.** Mezireprezentace je ekosystémově neutrální z principu (S1, JSS §4.3) a builder svůj jazyk zná bez ptaní; jediným čtenářem je ověření. Pole tedy nepřidává nástroji schopnost, nýbrž **dává jméno hranici, kterou kritérium F10 počítá** — a dokud hranice jméno neměla, počítat ji uměl jedině člověk.

### Cross-ecosystem překlad je celý převod jednoho scénáře

**Překladem se počítá převod, jehož zdrojový a cílový framework leží v různých ekosystémech a jehož výstup obsahuje obě půlky věty F10** — entitní stranu (entita a mapovací artefakt, pokud si ho cíl drží zvlášť) i dotazovou stranu. Počítá se **jednou za trojici (scénář, zdrojový framework, cílový framework)**: týž převod souzený třemi testy je jeden překlad, protože kritérium počítá překlady, ne aserce, a dva různé scénáře v témž směru jsou naopak dva překlady, protože nástroj opravdu přeložil dvakrát.

Proč „celý". F10 jmenuje **entitu, mapování a podporovaný read-only dotaz**; směr, který předá jen dotaz, není překladem entity, a započítat ho by znamenalo, že třicítka měří něco jiného, než co kritérium vyjmenovává.

**Tři směry ze současných osmnácti takové dnes jsou** — Dapper → Hibernate, Dapper → EclipseLink a MyBatis → NHibernate — **a není to mezera ve F10.** U obou zdrojů uvádí deskriptor kategorii `PrimaryKey` jako nevyjádřitelnou, všechny tři cíle ji vyžadují, takže brána úplnosti rozhodnutí [010](010-diagnostics-as-returned-data.md) entitu odmítne a řekne proč. To je doslova předmět F6, ne F10: rozhodnutí 016 už jednou vyslovilo, že scénář „zdroj v Dapperu → cíl" patří pod F6, a **s připojeným katalogem se ty tři směry dopočítají** — přesně to, co diferenční matice u všech tří dělá, protože q1 má zdroj v Dapperu a q6 v MyBatisu a oba míří i na tyhle cíle. Sada proto tvrdí obojí: buď převod přes hranici dorazí celý, **nebo řekne, proč ne**. Mlčky hranici nepřekročí nic.

### End-to-end scénář je 4. stupeň rozhodnutí 016 přes hranici

**End-to-end scénář je cross-ecosystem překlad dovedený na 4. stupeň ověření:** artefakt se přeložil, cílový framework ho přijal, spustil se proti databázi a vrácené řádky se posoudily. „End-to-end" bereme doslova — celý řetěz od jednotky zdrojového frameworku po řádky z databáze —, protože kratší řetěz už má jméno a je jím stupeň ověření, ne scénář. Od 2026-09-21 takové scénáře existují nad dotazem (rozhodnutí 089) i nad entitou (rozhodnutí 087) a je z čeho vybírat.

**Dvojic ekosystémů jsou čtyři, ne dvě.** Bereme uspořádané dvojice, tedy všechny čtyři buňky matice dvou ekosystémů: .NET → .NET, .NET → Java, Java → .NET a Java → Java. Že se dvojice počítají po směrech, říká zadání samo — **T2 jmenuje „minimálně .NET → Java, Java → .NET a Java → Java"**, tedy tři uspořádané dvojice, a ne tři neuspořádané. Čtvrtou, úhlopříčnou buňku .NET → .NET přidáváme proto, že matice dvou ekosystémů má čtyři buňky a nechat jednu bez scénáře znamená nemít ji doloženou na téhle úrovni vůbec; nestojí to nic, protože doložená je, a vysloveno je tím víc.

### Kdo počítá

**Počítá sada, a to ze dvou matic, které v repozitáři už jsou.** Třetí místo, které by totéž vyslovilo znovu, by bylo přesně ten dokument, co zestárne (rozhodnutí 034), takže žádné nevzniká.

- **Vzorkové scénáře `CrossFrameworkInputs`** — jeden na zdrojový framework, entita s mapováním a jedním dotazem — převádí `CrossEcosystemMatrixTest` sám a celistvost posuzuje na místě. Přes hranici jich dnes dorazí celých **patnáct** z osmnácti.
- **Dotazy diferenční matice** — jeden scénář na dotaz — počítá z deklarace, a je to poctivé, protože jejich celistvost dokládá **běh**: co nevzniklo, nemůže běžet, a že každou dvojici matice opravdu spustí jedna ze dvou sad, tvrdí `DifferentialMatrixTest` spolu s oběma sadami (rozhodnutí 089). Přes hranici jich je **osmnáct**.

Dohromady **třicet tři**, tedy nad mezí kritéria; test porovnává s mezí, ne s přesným číslem, protože mez je to, co kritérium říká, a přesné číslo by se rozbíjelo při každém přidaném scénáři a netvrdilo by o nic víc.

Proč se obě půlky neslijí do jednoho měření: **diferenční scénáře s bezklíčovým zdrojem potřebují katalog**, kdežto tenhle test běží nasucho. Kdyby počítal i je vlastním převodem, musel by mít databázi — a tam, kde by nebyla, by se přeskočil a s ním by zmizel celý nárok na F10, což je přesně ta tichá díra, před kterou varovalo rozhodnutí 016 u přeskočených testů. Dělba je tedy táž, jakou rozhodnutí 089 zavedlo u F13: **každá půlka se tvrdí tam, kde má svou záruku**, a ani jedna nejde splnit vynecháním druhé.

### Co se nemění

Matice směrů zůstává součinem výčtu se sebou a `QueryMatrixTest` i `CrossEcosystemTest` zůstávají tím, čím jsou — první tvrdí, že žádný směr nemlčí, druhý drží tvar tam, kde se ekosystémy v hláskování rozcházejí. Nové je počítání, ne pokrytí. Do překladové cesty nepřibývá nic: ekosystém čte ověření, ne builder.

## Důsledky

**Obě věty kritéria F10 se stávají kontrolovatelnými.** Počet překladů přes hranici nemůže klesnout pod mez, aniž by spadl build, a buňka matice nemůže přijít o svůj end-to-end scénář nepozorovaně. [`traceability.md`](../traceability.md) proto u F10 odkazuje na test, ne na hodnotu — hodnota tam žádná není.

**Deskriptor má nové povinné pole a hranice ekosystémů poprvé jméno.** Ruční seznam javových frameworků v `DifferentialMatrixTest` mizí a nahrazuje ho dotaz na deskriptor. Sedmý framework tím vstupuje do počítání týmž krokem, kterým vstupuje do řešení; zapomenout ho nejde, protože bez deskriptoru neprojde `TargetFrameworkDescriptorTest` (rozhodnutí 009) a bez ekosystému se deskriptor nezkompiluje.

**Tři neúplné směry jsou nově vyslovené a jsou tvrzením o F6.** Dapper a MyBatis jako zdroje nevyslovují primární klíč a NHibernate ani obě implementace JPA bez něj entitu nepřijmou; nasucho tedy přes hranici projde jen dotaz a záznam, s katalogem celý převod. Dosud to nikde napsané nebylo a při ručním počítání by ty tři směry buď chyběly, nebo se započítaly — podle toho, kdo počítá.

**F10 z vyňaté oblasti 6 nevystupuje tímhle rozhodnutím, nýbrž až zeleným během v zafixovaném prostředí**, jak to platilo u F7, F8, F9 a jak to od rozhodnutí 087 a 089 platí i u F12 a F13. Důvod je tentýž: dvě ze čtyř buněk matice — .NET → Java a Java → Java — spouští javová sada, a ta patří do profilu `test` v compose. Do té chvíle je požadavek dál vyňatý a [`open-items.md`](../open-items.md) to nese jako zbývající krok, ne jako hotovou věc.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to PATCH:** mění se testovací sada a přibývá deklarace, podle které se nic nevětví. Tvar vydávaného artefaktu se nemění, REST kontrakt se nemění — rozhraní deskriptor nevydává —, a hranice záruk se posune teprve tím zeleným během, a bude to MINOR z téhož důvodu, z jakého jím bylo vstoupení F7, F8 a F9.

**Testy.** Vlastní počet cross-ecosystem překladů proti mezi kritéria, s hláškou, která rozepisuje obě půlky součtu. Každá ze čtyř uspořádaných dvojic ekosystémů má v diferenční matici aspoň jeden scénář dovedený k běhu. Každý ze současných osmnácti směrů přes hranici dorazí celý, nebo řekne proč ne. A každý deskriptor vyslovuje ekosystém, přičemž obě strany hranice jsou obydlené — jednostranná hranice by nebyla hranice a součet by vyšel nulový nebo úplný, podle toho, která strana by zůstala prázdná. Dosavadní testy procházejí beze změny, protože na verdikt žádného z nich nové pole nesahá.
