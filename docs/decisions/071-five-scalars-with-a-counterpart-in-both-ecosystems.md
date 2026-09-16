# 071 — Uzavřený seznam skalárů se rozšiřuje o pět hodnot s protějškem v obou ekosystémech

Datum: 2026-09-16
Stav: platí
Požadavky: F1, F6, F7–F10, F11, S2
Podklad: audit 2026-08-02, kap. 2.2; [srovnání .NET frameworků](../analysis/orm-frameworks-comparison.md), §6; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §6; NHibernate 5.7.0 (`TypeFactory`, `NHibernateUtil.GuessType` — ověřeno během 2026-09-16); EF Core 10.0.10 (`SqlServerTypeMappingSource` — ověřeno během 2026-09-16); Jakarta Persistence 3.2, výčet základních typů

## Kontext

Rozhodnutí [014](014-language-type-model.md) uzavřelo `ScalarType` na třinácti hodnotách a řeklo proč: jen nad konečnou množinou umí deskriptor cíle tvrdit, co cíl vyjádří. Seznam přitom nezná pět typů, které v C# běžně nesou sloupec: `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan` a `byte[]`. Audit 2026-08-02 (nález 2.2) je jmenoval ještě před 014 a 014 je vědomě odložilo, protože tehdy blokovaly něco jiného — referenční navigace a `Guid`. Dnes blokují čtyři věci, z nichž tři jsou vidět až v přípravě na javovou stranu.

**Neznámý typ nic netvrdí, a bez tvrzení není co překládat.** Vlastnost typu `DateOnly` se čte jako `LangType.Unknown("DateOnly")` a každý builder ji vypíše jménem ze zdroje. Uvnitř .NET to shodou okolností funguje, protože `DateOnly` je platné jméno i v cíli. Javový builder by ale podle 014 vypsal do javové třídy `DateOnly`, tedy nezkompilovatelný zdroj, a opačně by `LocalDate` z javového zdroje skončil doslova v C#. Pro F10 je to díra v základu: cross-ecosystem překlad stojí na tom, že mezireprezentace zná jméno typu neutrálně a každý builder si ho přeloží — což je přesně to, co u těchhle pěti typů chybí, ačkoli pro každý z nich má Java jeden přesný protějšek (`LocalDate`, `LocalTime`, `OffsetDateTime`, `Duration`, `byte[]`).

**Databázová strana slovník má, jazyková ne.** Rozhodnutí [019](019-neutral-database-type-vocabulary.md) zavedlo rodiny `Date`, `Time`, `TimestampWithTimeZone`, `Binary`, `VarBinary` a `Blob` a u `TinyInt` napsalo, že „`ScalarType.Byte` na ni míří" — oba slovníky se mají párovat. Šest rodin dnes žádný skalár nemíří: odvození jazykového typu z katalogu (`LanguageTypeInference`) rodinu `Date` rozšiřuje na `DateTime`, čímž ztrácí fakt „datum bez času", který obě .NET cílové verze umí nést (`DateOnly` podporuje NHibernate od 5.7.0 a EF Core 10 — srovnání .NET frameworků, §6, jeden ze tří doložených případů verzování zjištění), a rodiny `Time`, `TimestampWithTimeZone` a binární nedodá vůbec. Vlastnost známou jen z mapování nad takovým sloupcem pak odmítne brána úplnosti záznamem `Failure`, ač katalog o sloupci ví všechno. Pro F6 je to mezera, která s `resultMap` MyBatisu (F8) — mapováním bez třídy — vyplave hned.

**Název typu NHibernate je dvojice, ne rodina.** Při ověřování proti balíčku 5.7.0 jsme si nechali vypsat, co `TypeFactory` registruje a co `NHibernateUtil.GuessType` odvodí z CLR typu:

| CLR typ | NHibernate 5.7.0 odvodí | `DbType` | EF Core 10.0.10 (SQL Server) vydá |
|---|---|---|---|
| `DateOnly` | `DateOnlyAsDate` | `Date` | `date` |
| `TimeOnly` | `TimeOnlyAsTime` | `Time` | `time` |
| `TimeSpan` | `TimeSpan` | **`Int64`** (ticky) | **`time`** |
| `DateTimeOffset` | `DateTimeOffset` | `DateTimeOffset` | `datetimeoffset` |
| `byte[]` | `binary` | `Binary` | `varbinary(max)` |
| `DateTime` | `DateTime` | `DateTime` | `datetime2` |

Jména `DateOnly` a `TimeOnly` jako alias registrovaná **nejsou** — `HeuristicType("DateOnly")` vrací `null`; registrované jsou `DateOnlyAsDate`, `TimeOnlyAsTime`, `TimeOnlyAsTicks` a `TimeOnlyAsDateTime`. Z tabulky plyne, že jeden sloupec `time` má v NHibernate tři jména podle toho, do čeho se čte: `Time` do `DateTime`, `TimeAsTimeSpan` do `TimeSpan`, `TimeOnlyAsTime` do `TimeOnly`. Dnešní `ToNHibernate` jmenuje typ jen podle rodiny — `Time` je vždy `TimeAsTimeSpan` — a vlastnost `DateTime` nad sloupcem `time` tak dostane mapování, které postaví session factory a spadne při prvním načtení řádku. Dokud jazyková strana znala jediný temporální skalár, dalo se to přehlédnout; s pěti to nejde.

**A jeden default se rozchází.** `TimeSpan` je v EF Core sloupec `time`, v NHibernate 64bitové celé číslo s ticky. Není to rozdíl dialektů ani verzí, ale výchozího předpokladu frameworku o tom, co doba trvání v databázi je — týž druh rozdílu, kvůli kterému 014 přesunulo převod jazyk ↔ databáze z `Common` do wrapperů (`varchar` proti `nvarchar`). Na javové straně je obraz podobný: JPA 3.2 uvádí mezi základními typy `LocalDate`, `LocalTime`, `OffsetDateTime` i `byte[]`, ale `java.time.Duration` ne — nese ho jen Hibernate nativně, EclipseLink ho bez konvertoru nemapuje a MyBatis pro něj vestavěný `TypeHandler` nemá.

## Zvažované varianty

1. **Nechat pět typů jako `Unknown`.** Nic nestojí a uvnitř .NET nic viditelně neselhává. Právě proto je to lákavé a právě proto špatné: mlčení se ukáže až v prvním javovém builderu, který bude podle 014 vypisovat neznámé jméno ze zdroje, a mezera v odvození z katalogu zůstane. Odložení by navíc zdražilo: dnes se rozšíření dotýká tří převodních tabulek, po javové trojici šesti.

2. **Doplnit jen `DateOnly` a `TimeOnly`.** Dvojice, kterou srovnání frameworků doložilo jako podporovanou v obou .NET cílích. Ostatní tři ale nejsou o nic méně doložené: `DateTimeOffset` a `byte[]` mají v obou .NET frameworcích výchozí mapování ověřené během a na databázové straně už rodinu, `TimeSpan` má mapování v obou, jen různé. Rozšířit seznam o dvě hodnoty a tři nechat neznámé by odvození z katalogu nadále nedodalo `TimestampWithTimeZone` ani binární rodiny.

3. **Jeden temporální skalár s facetou druhu**, po vzoru rodiny `Timestamp` s facetou `Precision` z rozhodnutí 019. Souměrné, ale o jinou věc: 019 samo napsalo, že „jazykový typ je jedna nedělitelná věc, databázový je typ plus facety". `DateOnly` a `DateTime` nejsou jeden typ ve dvou přesnostech, jsou to dvě třídy s jinou sadou hodnot, jiným literálem a jiným protějškem v Javě — a deskriptor by nad facetou nedokázal říct, že cíl umí jednu a druhou ne, což u NHibernate 5.5.2 proti 5.7.0 byla přesně otázka.

4. **Otevřít seznam a nést jméno typu řetězcem.** Zamítla to už varianta 2 rozhodnutí 014 a důvod trvá: deskriptor se váže na uzavřenou množinu a párové převody mezi názvoslovím zdroje a cíle jsou to, čemu se parser–builder architektura vyhýbá (S1, JSS §4.3).

5. **Pět hodnot do uzavřeného výčtu, neutrálně pojmenovaných; tabulky ve wrapperech; u NHibernate jméno podle dvojice rodina × skalár; odvození z katalogu pro každou rodinu slovníku.**

## Rozhodnutí

Volíme variantu 5. **`ScalarType` dostává pět hodnot a zůstává uzavřený**; každá nová hodnota má právě jeden protějšek v C# i v Javě a je jazykovou stranou rodiny, kterou databázový slovník z rozhodnutí 019 už jmenuje.

| Hodnota | C# | Java | Rodina 019, na kterou míří |
|---|---|---|---|
| `Date` | `DateOnly` | `LocalDate` | `Date` |
| `TimeOfDay` | `TimeOnly` | `LocalTime` | `Time` |
| `DateTimeOffset` | `DateTimeOffset` | `OffsetDateTime` | `TimestampWithTimeZone` |
| `Duration` | `TimeSpan` | `Duration` | žádná jednoznačná — viz níže |
| `ByteArray` | `byte[]` | `byte[]` | `VarBinary` (a `Binary`, `Blob`) |

Jména se řídí týmž pravidlem jako v 014 a 019: neutrální termín, ne pravopis jednoho ekosystému, a bez kolize se jménem, které už něco jiného znamená. **`TimeOfDay`, ne `TimeOnly` ani `LocalTime`** — oba jsou jména jedné strany a `TimeOfDay` navíc vyslovuje rozdíl proti `Duration`, který je v obou jazycích jiná třída (doba trvání není čas dne). **`Duration`, ne `TimeSpan`** — javové slovo je tu přesnější, .NET `TimeSpan` znamená totéž jen jménem méně. **`DateTimeOffset`** je srozumitelný v obou ekosystémech a říká přesně to, co `OffsetDateTime`: datum a čas s posunem, ne se zónou (`ZonedDateTime` protějšek nemá). **`ByteArray`, ne `Binary`** — `Binary` je jméno rodiny pevné délky v `DatabaseType` a rozhodnutí 019 kolizi tohoto druhu u `Integer`/`Int` vědomě odmítlo; `Date` a `Time` na databázové straně kolize jsou, ale stejně jako u `Decimal` a `Char`, kde už dnes obě strany sdílejí jméno, je to totéž slovo pro dvě strany téhož faktu. Bezznaménkové typy, `Half`, `Int128` a `BigInteger` do seznamu nadále nepatří (v Javě protějšek nemají nebo ho má jen jedna strana); `Instant`, `Year` a `OffsetTime`, které JPA 3.2 přidalo, nemají protějšek v C# a rozhodne o nich čtení javového zdroje — `Instant` se nabízí číst jako `DateTimeOffset` s nulovým posunem, ale to je volba parseru JPA, ne tohoto slovníku.

**Čtení a zápis v C# (`Common`)** je tabulka jmen: `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan`, `byte[]` oběma směry, s kvalifikací `System.` i bez ní. Pole bajtů je pro tenhle převod jméno skaláru, ne kolekce — `byte[]` se čte jako `ByteArray`, ne jako `Collection` s prvkem `Byte`, protože jako sloupec je to jedna hodnota a oba ekosystémy ho tak mapují.

**NHibernate jmenuje typ podle dvojice rodina × skalár, ne podle rodiny samotné.** `ToNHibernate` dostává jazykový skalár vlastnosti a u temporálních rodin (`Date`, `Time`, `Timestamp`, `TimestampWithTimeZone`) a u rodiny `BigInt` — kde `TimeSpan` a `TimeOnlyAsTicks` ukládají ticky — vybírá registrované jméno dvojice:

| Rodina \ skalár | `DateTime` | `Date` | `TimeOfDay` | `Duration` | `DateTimeOffset` |
|---|---|---|---|---|---|
| `Date` | `Date` | `DateOnlyAsDate` | — | — | — |
| `Time` | `Time` | `TimeOnlyAsTime` | — | `TimeAsTimeSpan` | — |
| `Timestamp` | `DateTime` | — | `TimeOnlyAsDateTime` | — | — |
| `TimestampWithTimeZone` | — | — | — | — | `DateTimeOffset` |
| `BigInt` | — | — | `TimeOnlyAsTicks` | `TimeSpan` | — |

Kde NHibernate 5.7.0 pro dvojici žádný typ neregistruje (pomlčka), **vypíše se typ, který NHibernate pro vlastnost sám předpokládá, a změna tvrzení o sloupci se hlásí záznamem `Loss` kategorie `DatabaseType`** — týž vzor, jakým 019 řeší chybějící `StringFixedLength`. Pravidlo za tím: jméno, které vlastnost nepřečte, dá mapování, které postaví session factory a spadne při prvním načtení; 3. stupeň ověření (rozhodnutí [016](016-generated-artifact-verification-levels.md)) by ho pustil a chyba by se ukázala až v konzumentském projektu. Vlastnost bez skaláru — reference, kolekce, neznámé jméno, chybějící typ — dostane jméno podle rodiny jako dosud, protože o CLR straně nic netvrdíme. Opačným směrem čte parser registrovaná jména do rodin: `DateOnlyAsDate` a `LocalDate` jsou `Date`, `TimeOnlyAsTime` je `Time`, `TimeSpan` a `TimeOnlyAsTicks` jsou `BigInt`, `TimeOnlyAsDateTime`, `LocalDateTime` a `UtcDateTime` jsou `Timestamp`; CLR stranu dvojice nese třída, ne mapování, takže z názvu typu se čte jen rodina. Odhad databázového typu z jazykového skaláru (`GuessFromScalarType`) přebírá výchozí heuristiku NHibernate z tabulky v kontextu, včetně `TimeSpan` jako ticků v `bigint` — je to výchozí předpoklad tohoto frameworku, stejně jako `String` → `nvarchar`, a tabulka odhadů je podle 014 právě místo, kam patří. Odhad se uplatní tam, kde builder atribut `type` vypisuje vždy, tedy u `<id>` a `<key-property>`; `<property>` bez databázového typu atribut nenese ani dál a NHibernate si typ odvodí z třídy sám — dojde k témuž, jen nevysloveně.

**EF Core nic neodhaduje** — o typu sloupce rozhoduje provider z CLR typu, builder vypisuje jen to, co model nese (`[Column(TypeName)]` z `SourceSqlType` nebo z rodiny, rozhodnutí [052](052-literal-sql-type-reaches-the-ef-core-annotation.md)). Čtení `[Column(TypeName = "...")]` se nemění; záložní čtení CLR jmen v téže tabulce zná nově i `DateOnly` a `TimeOnly`. **Dapper** vypisuje jen C# jména.

**Odvození jazykového typu z katalogu dodá skalár pro každou rodinu slovníku.** `Date` → `Date` (dosud `DateTime`), `Time` → `TimeOfDay`, `TimestampWithTimeZone` → `DateTimeOffset`, `Binary`, `VarBinary` a `Blob` → `ByteArray`. Dvě volby stojí za zdůvodnění. Sloupec `date` je datum, ne datum a čas o půlnoci: obě .NET cílové verze ho čtou do `DateOnly` (EF Core od verze 8 tak i scaffolduje, NHibernate 5.7.0 má `DateOnlyAsDate`) a Java do `LocalDate`, takže `DateTime` by tvrdil víc, než schéma říká. Sloupec `time` je čas dne, ne doba trvání: že EF Core čte `TimeSpan` do `time`, je výchozí předpoklad zdrojového frameworku, ne tvrzení schématu — a podle rozhodnutí [067](067-a-derived-convention-is-a-statement-a-default-is-not.md) absenční výchozí není tvrzení. `Duration` proto z katalogu nikdy nevzejde; nese ho jen zdroj.

**Dotazové literály.** Tři visitory vypisují konstantu nového temporálního skaláru tak, jak ji cíl čte: T-SQL a HQL v apostrofech jako u `DateTime`, LINQ jako `DateOnly.Parse("…")`, `TimeOnly.Parse("…")`, `DateTimeOffset.Parse("…")` a `TimeSpan.Parse("…")` po vzoru `DateTime.Parse`; pole bajtů jde beze změny (`0x…` je už zápis SQL). Žádný parser takovou konstantu dnes nevyrobí — literál C# takový typ nemá a datum v SQL či HQL je řetězec —, což je táž situace jako u `DateTime` (položka o konstruktoru `DateTime` v LINQ v [`open-items.md`](../open-items.md)); visitory ale mají být úplné nad uzavřeným seznamem, aby přibývající producent nenarazil na tichý průchod `_ => Text`.

**Deskriptor se nemění.** Kategorie mapovacích faktů jsou hrubší než skalár (rozhodnutí [009](009-target-framework-descriptor.md)) a všechny tři .NET cíle všech pět typů vyjádří, takže není co deklarovat. Javové deskriptory budou mít co: `Duration` je u EclipseLinku a MyBatisu *neumím vyjádřit* bez konvertoru — a to je právě tvrzení, které jde vyslovit jen nad uzavřeným seznamem.

**Co si javová strana odnáší** — očekávání pro rozhodnutí k F7–F9, s tím, co je doložené a co ne:

| Skalár | Java | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|---|
| `Date` | `LocalDate` | `date` (doloženo, srovnání §6) | základní typ JPA 3.2; SQL typ volí platforma — ověřit | vestavěný handler od 3.4.5 |
| `TimeOfDay` | `LocalTime` | `time` — ověřit během | základní typ JPA 3.2 — ověřit | vestavěný handler od 3.4.5 |
| `DateTimeOffset` | `OffsetDateTime` | `datetimeoffset` (doloženo, srovnání §6) | základní typ JPA 3.2 — ověřit | vestavěný handler od 3.4.5 |
| `Duration` | `java.time.Duration` | nativně; SQL Server bez intervalového typu — ověřit, co dialekt vydá | **není základní typ JPA**; bez `AttributeConverter` nelze vyjádřit | **bez vestavěného handleru** |
| `ByteArray` | `byte[]` | `varbinary` s výchozí délkou dialektu — ověřit | základní typ JPA | `ByteArrayTypeHandler` |

„Ověřit" znamená během tutoriálu s DDL, který žádá položka o tutoriálech k EclipseLinku a MyBatisu; zapsat nedoložený default do deskriptoru je to, před čím varují rozhodnutí 067 a 013.

## Důsledky

Přepis je jednorázový (rozhodnutí [003](003-one-shot-migration.md)) a sahá do výčtu, převodu jmen v `Common`, obou tabulek NHibernate wrapperu a jeho builderu, záložní tabulky EF Core wrapperu, odvození z katalogu a tří visitorů. Řídicí tok se nemění nikde.

**Čtyři změny chování, které je fér vyslovit:**

- Pět typů přestává být `Unknown`. Na tvaru .NET artefaktů to samo o sobě nic nemění — C# jména jsou táž a `<property>` bez databázového typu atribut `type` nenese ani dál —, změní se jen `<id>` a `<key-property>` nad takovým typem, kde builder odhad vypisuje (`type="DateOnlyAsDate"` u klíče typu `DateOnly`); je to tvrzení výchozího předpokladu NHibernate, ne změna schématu, protože NHibernate by bez atributu odvodil totéž.
- Sloupec `date` známý jen z mapování se odvozuje jako `DateOnly`, ne `DateTime`.
- Jméno typu NHibernate nad temporální rodinou závisí na skaláru: `DateTime` nad `time` vyjde jako `Time` (dosud `TimeAsTimeSpan`, tedy mapování, které nešlo použít), `DateOnly` nad `datetime2` jako `DateOnlyAsDate` se záznamem `Loss`.
- **`TimeSpan` mezi EF Core a NHibernate bez katalogu mění sloupec.** EF Core zdroj s vlastností `TimeSpan` bez `[Column(TypeName)]` nenese rodinu, mapování vyjde bez atributu `type` a NHibernate si za běhu odvodí svůj default — `bigint` s ticky —, kdežto EF Core by vydal `time`. Není to chyba převodu, je to rozdíl výchozích předpokladů obou frameworků, přesně případ `varchar`/`nvarchar` z kontextu 014; lék je týž jako tam — katalog (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) nebo vyslovený typ ve zdroji, po kterém vyjde `TimeAsTimeSpan`. Cross-ecosystem překlad zdědí totéž ve třetí podobě: Hibernate ukládá `Duration` jinak než oba .NET frameworky.

**Okrajové případy na .NET straně, které implementace drží a testy tvrdí:**

- `byte[]` s `[Timestamp]` zůstává sloupcem verze (rozhodnutí [030](030-scope-of-version-1-0.md)): element `<version>` bere typ jen z databázové rodiny, takže bez ní zůstává bez atributu a s rodinou `VarBinary` vyjde `generated="always" type="binary"` jako dosud; nový skalár mu do cesty nevstupuje.
- `byte[]` bez `[Timestamp]` je obyčejná vlastnost: NHibernate `binary` (`varbinary`, délku dá dialekt), EF Core `varbinary(max)`, s `[MaxLength(n)]` `varbinary(n)` — délka cestuje facetou jako dřív.
- Nullabilita se čte ze zápisu `?` jako u každého typu; `TimeOnly?` je `TimeOfDay` s `IsNullable`.
- NHibernate typ `TimeSpan` v `hbm.xml` se čte jako rodina `BigInt` a s vlastností `TimeSpan` ve třídě se vypíše zpět jako `TimeSpan`; s vlastností `long` nad týmž sloupcem vyjde `Int64`. Round trip drží, protože jméno vzniká z dvojice.
- `TimeOnlyAsTicks` a `TimeOnlyAsDateTime` — čas dne v `bigint`, resp. v `datetime2` — se čtou i vypisují, ale odhad je nikdy nevydá: default NHibernate pro `TimeOnly` je `TimeOnlyAsTime`.
- Datový literál v LINQ (`new DateOnly(…)`, `DateOnly.Parse(…)`) parser stále nečte a hlásí ho jako nepřečtený filtr; totéž platí pro `new DateTime(…)` a je to zapsané v `open-items.md`.

**Pozorování mimo rozsah, zapsané do `open-items.md`:** vlastnost s typem `Unknown` dnes nevydává žádný záznam, ač 014 píše, že „diagnostika ho ohlásí jako fakt, který cíl nemusí přijmout". Uvnitř .NET to nevadí, u javového builderu bude záznam jediná stopa, proč artefakt nejde přeložit.

Tvrzení o jménech typů NHibernate a o výchozích mapováních EF Core v tomto rozhodnutí jsou ověřená během proti balíčkům, na kterých stojí 3. stupeň ověření; test `ScalarVocabularyVerificationTest` je drží — session factory nad všemi pěti typy a EF Core model s očekávanými typy sloupců. Tvrzení o javové straně jsou z dokumentace a specifikace a ověří je tutoriály. Přibývá schopnost, takže příští vydání je podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) MINOR.
