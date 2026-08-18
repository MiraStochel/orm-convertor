# Audit soudržnosti dokumentace, 2026-08-15

Revize `docs/` jako celku: sedí spolu rozhodnutí, `architecture.md` a `open-items.md`, a odpovídají stavu kódu? Na rozdíl od auditu z 2026-08-02, který mířil na kód, se tenhle audit dívá na dokumentaci samotnou — na to, jestli čtenář, který otevře jeden dokument, dostane platnou informaci.

Nálezy jsou číslované po kapitolách, aby na ně šlo odkazovat. Co z nich plyne, je v kapitole 7.

## Zafixované verze a stav, ke kterému audit platí

Repozitář ve stavu `7542913` („Add decision on the language type model"), pracovní strom čistý. Prošli jsme `architecture.md`, `open-items.md`, `baseline.md`, `requirements.md`, audit 2026-08-02 a všech čtrnáct rozhodnutí včetně indexu; `analysis/` jen v odkazech. Tvrzení o kódu jsme ověřovali proti témuž commitu.

Verze frameworků jsou v tabulce v `architecture.md`, části „Zafixované verze". Tenhle audit je nepřepisuje ani nesnímkuje — kde na verzi něco závisí, odkazuje se tam.

## 1. Rozhodnutí, která si protiřečí

### 1.1 Rozhodnutí 010 změnilo volbu z 008, a na 008 to není vidět

Kritické. Rozhodnutí 010 samo píše: *„Toto rozhodnutí ten důsledek zužuje: původ se vydává jako záznam v okamžiku, kdy fakt dodá katalog, a v modelu se neukládá. […] Je to změna volby, ne její doplnění, takže se 008 nereviduje."*

Podle `decisions/README.md` má ale změna volby jediný předepsaný výsledek: *„Vznikne nové rozhodnutí a to původní dostane stav `nahrazeno NNN`."* 010 změnu přizná a pak ji nezaznamená nikam. V 008 dál stojí *„Mezireprezentace musí evidovat původ faktu. Dnes nese jen hodnotu."* a *„každý doplněný fakt nese svůj původ"*, obojí ve stavu `revidováno`, tedy jako platné.

Vada se propisuje dál. Rozhodnutí 009 (`platí`): *„fakt nese záznam o svém původu podle 008"*. Rozhodnutí 011, které vzniklo **po** 010: *„konvence třetího stupně nesoucí svůj původ podle rozhodnutí 008"*. Obojí odkazuje na znění, které už neplatí.

Jádro problému není v 010, ale ve slovníku stavů: 010 ruší jen část 008, a `platí` / `revidováno` / `nahrazeno NNN` částečné nahrazení nezná. Tohle není překlep k opravě — potřebuje rozhodnutí.

### 1.2 Rozhodnutí 010 vrátilo zamítnutou variantu na straně čtení katalogu

Kritické. 010 tvrdí: *„Platí z něj všechno ostatní — priorita zdrojů, vítězství zdroje nad katalogem i požadavek, aby se rozpor ohlásil; mění se jen to, kde ohlášení žije."* O dva odstavce dál ale zavádí: *„Poptávka řídí, co se použije, ne co se načte. […] Načítá se tedy celý sloupcový obraz dotčených tabulek."*

To je varianta C, kterou 008 výslovně zamítlo (*„rozsah dotazů neurčuje nikdo: doplní se i fakta, která cílový framework nevyjádří"*) ve prospěch varianty E (*„rozsah čtení určuje cílová strana převodu"*). Padají tím dvě konkrétní tvrzení, která v 008 zůstávají: *„Jako cíl [Dapper] nevyvolá žádný dotaz […] jeho poptávka je prázdná"* a *„Doplňování je proto přírůstkové a idempotentní"*. Padá i definice poptávky v 009: *„Poptávka do katalogu je sjednocení kategorií ve stavu vyžaduji a umím vyjádřit, zmenšené o to, co už v mezireprezentaci je."* 010 tuhle formulaci sice cituje jako vadnou, ale připisuje ji rozhodnutí 008, takže 009 nikdo neopraví.

### 1.3 Rozhodnutí 014 mění to, co 009 prohlásilo za nedotčené

Rozhodnutí 009: *„Nepopisuje ani typový model: převodní tabulky zůstávají tam, kde jsou, a neutralizace typového modelu je na tomto rozhodnutí nezávislá."*

Rozhodnutí 014: *„Převod jazyk ↔ databáze se stěhuje z `Common` do wrapperů"* a *„jen nad konečnou množinou umí deskriptor říct, co cíl vyjádřit umí"*, s migrací, která se *„dotkne […] deskriptoru"*. 014 se na 009 odkazuje jen v zamítnuté variantě 2 a rozpor s jeho sekcí o tom, co deskriptor nepopisuje, nikde nepojmenuje.

### 1.4 Rozhodnutí 012 vykresluje N:M, kterou 005 z modelu odstranilo

Tabulka výstupů v 012 má řádek *„1:N a N:M | `Inverse` | `<bag>` s `<key>`, sloupce z `ColumnPairs`"*. Podle 005 ale žádná N:M relace do builderu nedojde: *„N:M je v mezireprezentaci zachycená jako junction `EntityMap` plus dvě relace N:1"*, a varianta B je *„výchozí a jediná povinná cesta"*. Řádek tedy popisuje tvar, který nemá vzniknout, a navíc ho vykresluje shodně s 1:N, ačkoli v NHibernate jde o jiný vztah. 012 se na 005 neodkazuje ani jednou, přestože zároveň zavádí diagnostickou kategorii „N:M bez spojovací entity".

### 1.5 Odůvodnění revize v 008 zužuje pravidlo z README

`decisions/README.md`: *„Revidovat na místě je bezpečné jen dokud rozhodnutí není naimplementované."* Historie 008: *„Ukázalo se to hned poté, co podle tohoto rozhodnutí začal EF Core builder generovat bezklíčový typ […] Revidovat na místě je ale bezpečné jen dokud rozhodnutí není naimplementované; čtení katalogu v době revize nezačalo."*

Kód podle rozhodnutí vznikl, takže podmínka neplatila. Odůvodnění si ji zúžilo na „čtení katalogu", což pravidlo neříká.

## 2. Odkazy na text, který v cíli není

### 2.1 Rozhodnutí 012 přisuzuje 001 definici role

012: *„Role je v modelu podle rozhodnutí 001 definovaná jako strana držící fyzický cizí klíč."* Rozhodnutí 001 slova „role", „Owning", „Inverse" ani „cizí klíč" neobsahuje — řeší výhradně to, že `Relation` identifikuje entitu jménem místo reference. Pojem role zavádí do dokumentace až 012, a rovnou jako převzatý.

Věcně je přitom v pořádku: v kódu `Model/AbstractRepresentation/Enums/RelationRole.cs` je `Owning = 1, // side holding the physical foreign key`. Chybí jen rozhodnutí, které to zavedlo.

### 2.2 Rozhodnutí 011 se opírá o papírovou stopu, která neexistuje

011: *„Přesnější `KeyGenerationStrategy` zamítáme kvůli papírové stopě: odkazují na něj rozhodnutí 006 a 009, audit i kategorie `MappingFactCategory.PrimaryKeyStrategy`."* Identifikátor `PrimaryKeyStrategy` se v 006 ani 009 nevyskytuje; 006 má jen opis, 009 strategii generování klíče nezmiňuje vůbec.

Závěr přesto obstojí — kategorie v kódu je (`AbstractWrappers/Descriptors/MappingFactCategory.cs`, `PrimaryKeyStrategy = 9`). Chybná je citace, ne volba.

### 2.3 Výčet kategorií v 009 je proti kódu neúplný, a nikde to není vidět

009 vyjmenovává kategorie mapovacích faktů jako uzavřený seznam osmi. V kódu jich je deset — přibyly `PrimaryKeyStrategy` (rozhodnutí 011) a `ForeignKeyColumns` (rozhodnutí 012), obě o dva dny později. Rozhodnutí je zmražený snímek volby, takže samo o sobě v pořádku není co opravovat; vadné je, že aktuální výčet nenese `architecture.md`, který o `MappingFactCategory` nemluví vůbec.

### 2.4 Rozhodnutí 011 stojí na verzi, kterou 013 přepsalo

011: *„u JPA se opíráme o Jakarta Persistence 3.1 […] Deklarace cílových verzí je samostatná otevřená položka a tohle rozhodnutí ji nepředjímá."* Následující den 013 fixovalo 3.2 a položka se uzavřela. Volba slovníku se tím nemění, ale podklad se nesrovnal.

## 3. `open-items.md` — plán a položky

### 3.1 Druhá položka doporučeného pořadí v souboru neexistuje

Pořadí uvádí jako bod 2 *„Naplnění `ColumnPairs` v parserech pro entity převáděné společně"*. Položka toho jména v souboru není; obsah je rozdrobený do tří cizích položek (Junction entita, Detekce N:M, Rozresolvování jmen). Druhou nejdůležitější práci v celém plánu tedy nejde odškrtnout.

### 3.2 Bod 7 pořadí neodpovídá položce názvem ani kategorií

Pořadí: *„Klíčová třída u formy `Embedded` (rozhodnutí)"*. V souboru je *„Klíčová třída u kompozitního klíče na straně entity"*, a leží pod `## Otevřená práce`. Pořadí ji vede jako rozhodnutí, soubor jako práci.

### 3.3 Bod 6 pořadí tvrdí „poslední kus F3", ačkoli dvě položky F3 v pořadí nejsou

Položka *„Parser NHibernate — část klíče, která je cizím klíčem"* nese `Požadavky F1, F3` a *„Detekce N:M v parserech"* je rovněž F3. Preambule přitom cíl vymezuje jako *„uzavřít práci s jednoduchými i kompozitními klíči ve všech třech .NET frameworcích, tedy F1–F3"*, takže vynechání nevypadá jako záměr.

### 3.4 Duplicitní vedení téže práce

*„Chybějící jazykový typ shodí generování"* a *„Jazykový typový model"* popisují tutéž věc — pád `CLRTypeConvertor` na neznámém typu. To je doslova kontext rozhodnutí 014 (*„`Unknown` se nikdy neprojeví výjimkou"*) a důsledek 010 (*„Pád na chybějícím jazykovém typu se stává záznamem"*). První z těch dvou položek navíc uvnitř sekce Otevřená práce navrhuje třetí, nerozhodnutou variantu řešení (*„`PropertyMap.Type` databázový typ nese, jen opačný převod v `DatabaseTypeConvertor` neexistuje"*), což je rozhodnutí, ne práce, a rozporuje volbu ze 014 i 010.

### 3.5 Položka „Junction entita v builderech" nejmenuje chybějící kód

Formát vyžaduje, aby položka řekla, co konkrétně v kódu chybí. Tahle uvádí jen *„`ColumnPairs` se automaticky neplní"*, což patří položce z nálezu 3.1. `architecture.md` přitom o junction entitě říká *„dnešní buildery ho k ničemu nepotřebují"* a 005 *„výchozí generování ho nevyžaduje a vždy vytvoří explicitní entitu"*.

### 3.6 Tabulka „Vzdálenější horizont" popírá stav dvou bloků

Tabulka uvozená větou *„každý si zaslouží vlastní rozhodnutí, než se do něj sáhne"* vede blok F4–F6 a blok F11. Rozhodnutí pro obojí existují (008 z 2026-08-11, 010 z 2026-08-12) a obojí je aktivní prací v doporučeném pořadí, na místech 5 a 3. Řádek F4–F6 navíc uvádí `ColumnPairs` jako závislé na databázi, což popírá vlastní položku o pár desítek řádků výš.

### 3.7 Zastaralý název blokátoru

Položka o klíčové třídě je *„Blokováno neutralizací typového modelu"*. Takový název v souboru není — rozhodnutí 014 tu věc rozdělilo na *Jazykový typový model* a *Neutralizaci databázového typu*, a z těla položky plyne, že jde o první z nich. Táž zastaralá formulace je i v tabulce horizontu.

### 3.8 Sedm položek nemá řádek odkazů

Bez kurzívového řádku vazeb: *Centrální správa verzí*, *Dotazová větev*, *EF Core — nullabilita*, *NHibernate builder — schéma*, *Chybějící jazykový typ*, *NHibernate builder — kolekce*, *Frontend*. U *Dotazové větve* a *Frontendu* je vada hlubší: nemají popisný nadpis (jmenují větev a vrstvu, ne problém) a místo souvislého odstavce jsou to seznamy nesouvisejících podpoložek — doslovný přenos kapitoly 8 auditu 2026-08-02, tedy přesně to, co pravidlo o soběstačnosti zakazuje.

### 3.9 Mezery popsané v `architecture.md`, které v `open-items.md` nejsou

- Build `advisor.dll` pro Windows: *„Build krok pro Windows neexistuje, i když `ilp.c` má exportní makra připravená."*
- Rozpoznatelnost zúžení u `smalldatetime`. Rozhodnutí 010 pro to výslovně přiděluje práci (*„musí být z převodu poznat, že zúžil — to je práce v typovém modelu"*), položka *Jazykový typový model* to nezmiňuje.
- Deklarace cílového dialektu: *„vyžaduje znát cílový dialekt, který se dnes nikde nedeklaruje"*. Položka *Neutralizace databázového typu* pokrývá `sql-type`, ne deklaraci dialektu.
- Dokumentace nasazení mimo Docker. `architecture.md` u `ecosystem.config.js` odkazuje *„viz `open-items.md`"*, kde odpovídající položka není.
- *„Otestovaný je jen `http` launch profil."*

## 4. `architecture.md` — stav a jeho ověřitelnost

### 4.1 Datum ověření je nepravdivé

Hlavička uvádí *„Naposledy ověřeno proti kódu: 2026-08-02"*. Dokument přitom jako dnešní chování popisuje slovník `PrimaryKeyStrategy` se `SourceStrategyName` (rozhodnutí 011, 2026-08-13), volbu značky podle role a sloupce z `ColumnPairs` (012, 2026-08-13) i tabulku verzí s odkazem na 013 (2026-08-14).

Popis kódu jsme ověřili a sedí (kapitola 6). Špatně je jen to datum — a je to jediná pojistka čtenáře proti zastaralosti.

### 4.2 Blokace `ColumnPairs` je popsaná přísněji, než jaká je

`architecture.md`: *„Prázdný seznam znamená, že sloupce zatím nejsou rozresolvované; cílové sloupce nejdou určit z jedné translation unit a jejich doplnění závisí na metadatech z databáze (F4/F5)"* a *„napojení na databázi […] neexistuje – a s ním ani rozresolvované `ColumnPairs`"*.

Proti tomu `open-items.md`: *„Je-li cílová entita součástí téhož vstupu, jsou její klíčové sloupce v mezireprezentaci a `ColumnPairs` lze naplnit ze zdroje; katalog je potřeba teprve tam, kde cílová entita ve vstupu není"*, a 012: *„Páry proto vznikají teprve tam, kde jsou obě entity součástí téhož převodu."*

Rozhoduje kód a dává za pravdu druhé straně: `AbstractEntityBuilder.cs` má komentář *„so ColumnPairs stay empty (to be filled from DB metadata / multi-entity context)"*. `architecture.md` tedy podmiňuje `ColumnPairs` databází bezpodmínečně, ačkoli databáze je jen jedna ze dvou cest — a plán klade naplnění ze zdroje na místo 2, kdežto katalog až na místo 5.

### 4.3 Chybí výčet kategorií deskriptoru

Viz nález 2.3. `MappingFactCategory` se v `architecture.md` nevyskytuje, ačkoli deskriptor je od rozhodnutí 009 nosná konstrukce a jeho výčet se od té doby dvakrát rozšířil.

## 5. Číslování a formát

### 5.1 Kolize označení E

Zadání vedoucího používalo pro experimentální požadavky E1–E7, JSS článek používá pro překladová pravidla E1–E10. Rozhodnutí obojí zapisují stejně, a nejméně dvakrát se stejný token liší významem: hlavička 010 uvádí `E3` ve smyslu požadavku „Metriky korektnosti", tělo 008 píše *„pravidlo E3"* ve smyslu pravidla článku „Property-to-column mapping". Pravidla E8, E9 a E10, o která se rozhodnutí 008 a 010 opírají, v zadání neexistují vůbec. Vazba na požadavky, kterou má podle rozhodnutí 007 nést pole `Požadavky`, se tím při čtení rozpadá.

### 5.2 Rozhodnutí 003 a 007 nemají pole `Požadavky`

Šablona v `decisions/README.md` ho neoznačuje za nepovinné (na rozdíl od `Podklad`) a rozhodnutí 007 samo stanoví: *„Každé rozhodnutí nese datum, stav a vazbu na požadavky F/S/T."* 007 tedy porušuje pravidlo, které zavádí. U obou přitom jde o rozhodnutí o způsobu práce, ne o nástroji, takže vazba na požadavky žádná není — chybí spíš pravidlo pro tenhle případ než hodnota v poli.

### 5.3 Index zkracuje názvy dvou rozhodnutí

`decisions/README.md` uvádí u 004 „Nevyjádřitelné fakty hlásit varováním" (soubor má navíc „, negenerovat náhražky") a u 006 „Ploché vykreslení kompozitního klíče" (soubor má navíc „a identitní členy jako odpovědnost builderu"). U 006 tím z indexu mizí půlka rozhodnutí.

### 5.4 Šablona připouští stav, který nikde není definovaný

`decisions/README.md` nabízí v šabloně `zavrženo`, který prosa téhož dokumentu nedefinuje a žádné rozhodnutí ho nepoužívá.

### 5.5 Rozsah `F7–F10` je místy paušál

Rozhodnutí 011 deklaruje `F7–F10`, tedy včetně F8 (Podpora MyBatis). MyBatis se v 011 nevyskytuje ani jednou a mapovací tabulka pokrývá výhradně NHibernate, EF Core a JPA. Slabší případ téhož je 006.

## 6. Co bylo ověřeno proti kódu

Nálezy, které z dokumentů vypadaly jinak, než jak to je:

**Nález 2.4 z auditu 2026-08-02 a oprava O3 nejsou nezvěstné.** V žádném dokumentu po nich není stopa, ale v kódu jsou hotové: `NHibernateWrappers/Convertors/DatabaseTypeConvertor.cs` má `CLRType.Byte => ToNHibernate(DatabaseType.TinyInt)`, `CLRType.Float => ToNHibernate(DatabaseType.Real)` a větev `"datetimenoms"` malým písmem. Pravidlo „nic nesmí zmizet" je tím formálně porušené, ale u třířádkové opravy překlepu, která nemění popsané chování, to nemá důsledek — evidence chybí, stav je v pořádku.

**`docs/analysis/` existuje** — pět souborů, odkazy z rozhodnutí 011 a 013 vedou na existující cíle.

**Model odpovídá popisu.** Ověřeno: `RelationRole` s hodnotami `Owning`/`Inverse`, `PrimaryKey.SourceKeyClass`, `PrimaryKeyPart.SourceStrategyName`, `MappingFactCategory.PrimaryKeyStrategy`, výčet `PrimaryKeyStrategy`. Popis v `architecture.md` je věcně správný — vadné je jen datum ověření (nález 4.1).

## 7. Co z auditu plyne

Většina nálezů nejsou změny názoru, ale chyby od začátku: odkaz na text, který v cíli nikdy nebyl, název, který se neshoduje s položkou, datum, které nikdy neplatilo. U takových je zpětná oprava dosavadních souborů přípustná, přestože se to běžně nedělá — nová verze nemá co postavit vedle staré, protože stará nenese žádnou volbu, jen omyl. Rozhodnutí ve stavu `platí` se tím nemění; opravuje se text, který popisuje něco jiného, než co bylo rozhodnuto.

### Opravy

| co | kde |
|---|---|
| Datum ověření proti kódu | `architecture.md`, hlavička (4.1) |
| Blokace `ColumnPairs` uvést na dvě cesty místo jedné | `architecture.md` a tabulka horizontu v `open-items.md` (4.2, 3.6) |
| Doplnit výčet kategorií deskriptoru | `architecture.md` (4.3, 2.3) |
| Odstranit odkaz na neexistující položku o nasazení | `architecture.md` u `ecosystem.config.js` (3.9) |
| Přisouzení role rozhodnutí 001 nahradit vlastní volbou | rozhodnutí 012 (2.1) |
| Citaci papírové stopy uvést na kategorii v kódu | rozhodnutí 011 (2.2) |
| Srovnat verzi Jakarta Persistence s rozhodnutím 013 | rozhodnutí 011, `revidováno` a nenaimplementované, oprava na místě je přípustná (2.4) |
| Doplnit chybějící položku k bodu 2 pořadí | `open-items.md` (3.1) |
| Srovnat názvy a kategorie bodů 6 a 7 pořadí, doplnit vynechané položky F3 | `open-items.md` (3.2, 3.3) |
| Sloučit duplicitní položky o jazykovém typu, třetí variantu vyjmout nebo povýšit na rozhodnutí | `open-items.md` (3.4) |
| Doplnit, co konkrétně chybí u junction entity, nebo položku zrušit | `open-items.md` (3.5) |
| Přejmenovat zastaralý blokátor | `open-items.md` (3.7) |
| Doplnit řádky odkazů, přepsat *Dotazovou větev* a *Frontend* na soběstačné položky | `open-items.md` (3.8) |
| Založit položky pro pět mezer z `architecture.md` | `open-items.md` (3.9) |
| Přejmenovat experimentální požadavky E1–E7 na T1–T7 | `requirements.md`, `baseline.md`, rozhodnutí 005 a 010, index rozhodnutí, `open-items.md` (5.1) |
| Doplnit názvy 004 a 006 v indexu, odstranit nedefinovaný stav `zavrženo` | `decisions/README.md` (5.3, 5.4) |

Přejmenování E → T se týká jen požadavků ze zadání. Označení E1–E10 pro překladová pravidla článku zůstává; po přejmenování už se ty dvě řady nepřekrývají. Zásah do `baseline.md` a `requirements.md` je výjimka z pravidla o zmražených dokumentech: obojí je čistě přeznačení, žádné tvrzení se nemění, a ponechat v nich starou řadu by kolizi udrželo naživu.

### Potřebná rozhodnutí

**Jak zaznamenat částečné nahrazení.** Nálezy 1.1 a 1.2 nejsou opravitelné editem, protože slovník stavů nezná případ, kdy nové rozhodnutí ruší část staršího a zbytek nechává platit. Na výběr je označit 008 jako `nahrazeno 010` a přenést do 010 i to, co z 008 platí dál, nebo slovník o částečné nahrazení rozšířit. Do téhož rozhodnutí patří i to, co se stane s definicí poptávky v 009, kterou 010 fakticky přepsalo.

**Vztah rozhodnutí 014 a 009 k umístění převodních tabulek** (1.3). 009 je ve stavu `platí` a tvrdí opak toho, co 014 zavádí. Než se 014 začne implementovat, musí být jasné, které z těch dvou tvrzení platí a jak se to zaznamená.

**Vykreslení N:M v 012 proti modelu z 005** (1.4). Buď řádek z tabulky výstupů zmizí, protože takový vstup nemůže nastat, nebo se pojmenuje případ, kdy nastat může — a pak jde o změnu 005.

**Vazba rozhodnutí o způsobu práce na požadavky** (5.2). Rozhodnutí 003 a 007 žádnou nemají a mít nemohou. Pravidlo z 007 buď dostane výjimku, nebo se pole u takových rozhodnutí vyplní explicitním „netýká se".

### Delší horizont

Kolize označení E ukázala, že dokumentace pracuje se třemi číslovanými řadami (požadavky zadání, pravidla článku, dotazová pravidla Q) a nikde je nevysvětluje pohromadě. Až po přejmenování na T bude namístě zvážit krátký přehled těchto řad — kde vznikly a co znamenají — v `architecture.md` nebo jako samostatný dokument v `analysis/`.

Nálezy 3.1 až 3.8 mají společnou příčinu: doporučené pořadí na začátku `open-items.md` a seznam položek pod ním se udržují nezávisle a rozešly se. Stojí za úvahu, jestli má pořadí zůstat samostatným seznamem, nebo jestli se má nahradit značkou přímo u položek.
