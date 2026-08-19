# 019 — Neutrální slovník databázových typů

Datum: 2026-08-18
Stav: platí
Požadavky: F2, F5, F7–F10, F11, S2
Podklad: audit 2026-08-02, nálezy 2.1–2.4 a 4.5; NHibernate 5.7.0 — referenční tabulka typů

## Kontext

Rozhodnutí [014](014-language-type-model.md) zneutralizovalo jazykovou stranu typového modelu; databázová zůstala, jak byla. `DatabaseType` vypadá jako obecný seznam SQL typů, ale je to **seznam typů T-SQL**: `NVarChar`, `NText`, `UniqueIdentifier`, `Money`, `SmallMoney`, `SqlVariant`, `RowVersion`, `Image`. Většina z nich v PostgreSQL, MySQL ani Oracle neexistuje. Dokud oba ekosystémy míří na týž SQL Server, nic to nelže; s Hibernate, který si dialekt odvodí z JDBC metadat, přestane platit — a je to jediný deklarovaný blokátor F7–F10 na databázové straně.

Seznam má tři vady, které spolu souvisejí a jednotlivě se opravit nedají.

**Míchá typ s jeho facetami.** `DateTime`, `DateTime2` a `SmallDateTime` nejsou tři typy, ale jeden typ ve třech přesnostech; `Char`, `VarChar` a `Text` jsou jeden znakový typ ve třech délkových režimech, zdvojený ještě jednou kvůli unicode. `PropertyMap` přitom `Length`, `Precision` i `Scale` už nese a fáze doplnění z katalogu je plní zvlášť. Model tedy tutéž informaci vyjadřuje dvakrát, jednou hodnotou výčtu a jednou facetou, a při převodu se to sráží: konvertor pro NHibernate slévá všechny tři datočasové hodnoty do jediného `type="DateTime"` a zpětný převod vrací `DateTime2`. U `smalldatetime` je to změna přesnosti z minuty na 100 ns a rozsahu z 1900–2079 na 0001–9999 — a **rozhodnutí [010](010-diagnostics-as-returned-data.md) žádá, aby bylo z převodu poznat, že zúžil**, což dnes nejde, protože zúžení není fakt modelu, ale vlastnost tabulky.

**Neunese, co cíl umí.** Případ `Char`: podle referenční dokumentace je výchozím typem NHibernate pro `System.Char` typ `Char` (`DbType.StringFixedLength`) a `AnsiChar` se uvádí explicitně. C# `char` u nás končí jako neunicode `char(1)` místo `nchar(1)` a přesměrovat ho jinam nejde — žádná hodnota `DatabaseType` typ `"Char"` nevrací, protože `NChar` dává `"StringFixedLength"`, tedy řetězec pevné délky, ne jeden znak. Chybí rozlišení, které v seznamu není a být nemůže, dokud je unicode součástí názvu typu.

**Nemá kam uložit, co do slovníku nepatří.** `sql-type` na vnořeném `<column>` je jediná cesta, jak v mapování NHibernate udržet konkrétní SQL typ místo typu frameworku; parser ho nečte, protože pro něj v modelu není místo. Totéž potká `money`, `sql_variant` nebo `image`, jakmile se seznam zneutralizuje.

K tomu drobnost s následky: `DatabaseType.None = 0` se v celém řešení nikde nepřiřazuje ani nečte. `PropertyMap.Type` je přitom `DatabaseType?` a na pěti místech — v bráně úplnosti, v obou builderech a ve fázi doplnění — se „fakt chybí" pozná testem `Type is null`. Nenulová hodnota, která znamená „nic", by tenhle test obešla a doplnění z katalogu by se u takové vlastnosti tiše přeskočilo.

## Zvažované varianty

1. **Doplnit chybějící hodnoty a jinak seznam nechat.** Přidat hodnotu pro unicode jeden znak, případně další typy podle potřeby. Nejlevnější a nejhorší: rozšiřuje se tím právě ta vlastnost, která je vadná — že rozdíl v facetě má vlastní hodnotu výčtu. Po pár takových doplněních má seznam čtyřicet hodnot a pořád je to T-SQL.

2. **Sjednocení typů všech DBMS.** Jedna hodnota za každý typ SQL Serveru, PostgreSQL, MySQL a Oracle. Seznam by nabobtnal ke stovce a stejně by nestačil, protože uživatelské a doménové typy nejsou konečná množina. Do modelu by se vrátilo to, čemu se mezireprezentace vyhýbá — slovník konkrétního systému místo záměru (JSS §5.4). Táž úvaha zamítla variantu 2 v rozhodnutí [011](011-key-generation-strategy-vocabulary.md).

3. **Zrušit výčet a nést SQL typ řetězcem, překlad řešit dialekty.** Model by byl otevřený a věrný. Přestal by ale být slovníkem: deskriptor cílového frameworku se váže na **uzavřenou** množinu — jen nad ní umí říct, co cíl vyjádřit umí (rozhodnutí [009](009-target-framework-descriptor.md), a přesně tuhle úvahu použilo 014 pro `ScalarType`). Padla by kontrola úplnosti i mechanické záznamy o ztrátě, protože by nešlo spočítat průnik faktů modelu s kategoriemi *neumím vyjádřit*.

4. **Hodnotový objekt `DbType` symetrický k `LangType`.** Typ i facety v jednom neměnném objektu. Lákavé kvůli souměrnosti, ale rozbíjí to, co funguje: fáze doplnění z katalogu (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) pracuje **po jednotlivých faktech** — typ, délku, přesnost a měřítko doplňuje a hlásí zvlášť záznamy `Supplied` a `Conflict`, a `MappingFactCategory` má pro každou z nich vlastní kategorii. Nad neměnným objektem by se každý dílčí zápis stal přestavbou celku a slévání by potřebovalo vlastní mechanismus. Souměrnost s jazykovou stranou by byla vykoupená ztrátou granularity, kterou obě strany kvůli různé povaze nepotřebují stejně: jazykový typ je jedna nedělitelná věc, databázový je typ plus facety.

5. **Uzavřený slovník typových rodin, facety zvlášť na `PropertyMap`, a úniková cesta pro to, co se do slovníku nevejde.**

## Rozhodnutí

Volíme variantu 5. **Výčet pojmenovává rodinu typu, ne typ konkrétního DBMS. Šířka, délka a přesnost zůstávají facetami vedle něj, a co se do slovníku nevejde, zaznamenáme vedle něj — ne místo něj.**

Je to týž vzorec, který rozhodnutí 011 zvolilo pro strategie klíče a samo o něm napsalo, že na něj narazíme znovu u typového modelu: *mezireprezentace nese průnik záměrů, framework-specifické podrobnosti nese vedle něj jako záznam o zdroji.* Tady je ten druhý případ.

**Název `DatabaseType` zůstává.** Vadný je obsah, ne jméno — a jméno drží papírovou stopu přes `MappingFactCategory.DatabaseType`, kterou čte deskriptor, brána úplnosti i fáze doplnění.

Jména se řídí pravidlem převzatým z 011: **termín SQL standardu tam, kde ho standard má; obecně srozumitelný termín tam, kde ho nemá, ale rodina existuje ve víc systémech; nic tam, kde jde o typ jediného systému** — ten patří na únikovou cestu.

| Hodnota | Rodina | Slévá se do ní z dneška |
|---|---|---|
| `Boolean` | pravdivostní hodnota | `Bit` |
| `TinyInt` | celé číslo nejmenší šířky | `TinyInt` |
| `SmallInt` | celé číslo | `SmallInt` |
| `Integer` | celé číslo | `Int` |
| `BigInt` | celé číslo | `BigInt` |
| `Decimal` | přesné desetinné; přesnost a měřítko nesou facety | `Decimal`, `Numeric`, `Money`, `SmallMoney` |
| `Real` | přibližné, jednoduchá přesnost | `Real` |
| `DoublePrecision` | přibližné, dvojitá přesnost | `Float` |
| `Date` | datum bez času | `Date` |
| `Time` | čas bez data | `Time` |
| `Timestamp` | datum a čas; zlomkové vteřiny nese `Precision` | `DateTime`, `DateTime2`, `SmallDateTime` |
| `TimestampWithTimeZone` | datum a čas s posunem | `DateTimeOffset` |
| `Char` | znakový, pevná délka | `Char`, `NChar` |
| `VarChar` | znakový, proměnná délka | `VarChar`, `NVarChar` |
| `Text` | znakový, velký a bez deklarované délky | `Text`, `NText` |
| `Binary` | binární, pevná délka | `Binary` |
| `VarBinary` | binární, proměnná délka | `VarBinary` |
| `Blob` | binární, velký | `Image` |
| `Uuid` | globálně unikátní identifikátor | `UniqueIdentifier` |
| `Xml` | XML dokument | `Xml` |

Dvacet hodnot místo třiceti. K jednotlivým volbám patří zdůvodnění, protože se o ně opře text práce:

- **`Integer`, ne `Int`.** Slovo standardu, a zároveň se tím neplete se jménem hodnoty jazykové strany (`ScalarType.Int`), která znamená něco jiného — jazykový typ, ne sloupec.
- **`DoublePrecision`, ne `Float`.** `Float` je past v pojmenování: v T-SQL je to osmibajtový typ, ve většině ostatních systémů čtyřbajtový. Audit 2026-08-02 (nález 2.4) ukázal, že na téhle pasti už jednou vznikla chyba v převodní tabulce. Standardní `DOUBLE PRECISION` je jednoznačné.
- **`TinyInt` ponecháváme, ač není standardní.** Rodina existuje v T-SQL i MySQL a `ScalarType.Byte` na ni míří. Bez ní by se bajtová vlastnost tiše rozšířila na `SmallInt`, což je ztráta faktu, kterou opravujeme jinde.
- **`Timestamp` místo tří datočasových hodnot.** Rozdíl mezi `datetime`, `datetime2` a `smalldatetime` je zlomková přesnost, a tu `Precision` už nese — čtečka katalogu ji pro `datetime2` dokonce už plní. Tím **mizí i zúžení, které žádalo hlásit rozhodnutí 010**: `smalldatetime` je `Timestamp` s `Precision = 0`, ne třetí hodnota výčtu, která se cestou slije s ostatními. Zúžení se stalo faktem modelu místo vlastnosti převodní tabulky, takže není co dohlašovat.
- **`Decimal` bez `Numeric`.** V T-SQL jsou to synonyma a všude v našem kódu se obsluhují stejnou větví; rozdíl, který standard mezi nimi činí, žádný náš zdroj netvrdí. Kdo ho potřebuje, má ho na únikové cestě.
- **`Money` a `SmallMoney` mizí.** Jsou to typy jediného systému. Stávají se `Decimal` s odpovídající přesnosti a měřítkem a s doslovným názvem na únikové cestě — což je věrnější než dnešek, kdy se `SmallMoney` do NHibernate vypisuje jako `Currency` stejně jako `Money` a rozdíl se ztrácí bez záznamu.
- **`Uuid`, ne `UniqueIdentifier`.** Táž úvaha, kterou rozhodnutí 011 zvolilo `Uuid` místo `Guid`: mezireprezentace má být ekosystémově neutrální, `UniqueIdentifier` je pravopis jednoho systému.
- **`Blob`, ne `Image`.** `Image` je v SQL Serveru navíc typ označený za zastaralý; rodina velkého binárního objektu existuje všude.
- **`SqlVariant` a `RowVersion` mizí ze slovníku.** `SqlVariant` je typ jediného systému. `RowVersion` je jiný případ a stojí za pojmenování: **není to typ, je to token pro optimistickou souběžnost** — v JPA `@Version`, v EF Core `IsRowVersion()`. Že ho dnes neseme jako typ, je záměna kategorie; slovník ho ztrácí a mapovací fakt „sloupec verze" v modelu není. Zakládáme na to otevřenou položku, aby se to neztratilo mlčky.
- **`None` mizí.** Nikde se nepoužívá a `Type` je nullable, takže „fakt chybí" už znamená `null`. Dvě zápisy téhož by se rozešly právě tam, kde na tom záleží — ve fázi doplnění, která na `Type is null` stojí.

**Unicode se stává facetou, ne součástí typu.** `PropertyMap` dostává `bool? IsUnicode`. Neznámá hodnota znamená, že to zdroj netvrdil, a cíl doplní vlastní konvencí. Rozdělení na `N`-verze je T-SQL-ismus — PostgreSQL ani MySQL protějšek nemají, protože kódování je vlastnost sloupce nebo databáze, ne typu. Jako facet to zároveň **řeší případ `Char`**, kvůli kterému položka vznikla: `Char` s `IsUnicode = true` a `Length = 1` je přesně NHibernate typ `Char`, kdežto s `IsUnicode = false` je `AnsiChar`. Nová kategorie mapovacích faktů kvůli tomu nevzniká — je to část tvrzení o typu a spadá pod `MappingFactCategory.DatabaseType`.

**Únikovou cestu tvoří `SourceSqlType` na `PropertyMap`** — doslovný SQL typ, jak ho napsal zdroj, když slovník rodinu nezachytí nebo když je rodina hrubší než tvrzení zdroje. Sem se čte `sql-type` z vnořeného `<column>` NHibernate, sem přistane `money`, `sql_variant`, `image` i uživatelský typ. Je to záznam o zdroji vedle definice, ne její součást — táž role, jakou má `SourceStrategyName` u strategie klíče (rozhodnutí 011) a `SourceKeyClass` u klíčové třídy (rozhodnutí [006](006-flat-composite-key-rendering.md)), a táž, jakou má `LangType.Unknown` na jazykové straně (rozhodnutí 014). Jestli ho smí builder použít jako vstup generování, se řídí toutéž otevřenou otázkou jako u názvu strategie; do jejího zodpovězení je to podklad pro diagnostiku.

**Cílový dialekt tohle rozhodnutí nezavádí.** Emitovat `sql-type` nebo vybrat typ podle systému vyžaduje vědět, na jaký DBMS se míří, a to se dnes nikde nedeklaruje. Je to ale **fakt o cíli převodu, ne o typovém modelu** — přesně téhož tvaru jako cílová verze frameworku, kterou zafixovalo [013](013-target-framework-versions.md) a která na místo v deskriptoru teprve čeká. Patří proto k ní, ne sem. Navrhovat vrstvu dialektů teď by navíc znamenalo navrhovat naslepo: v repozitáři není druhý DBMS, proti kterému by to šlo ověřit. Do té doby je jediným dialektem SQL Server a `SourceSqlType` nese, co se nevejde.

## Důsledky

Přepis je jednorázový a bez přechodného období (rozhodnutí [003](003-one-shot-migration.md)).

**Čtyři převodní tabulky se přepisují celé**: oba směry u NHibernate, oba u EF Core, `MapType` ve čtečce katalogu a `FromDatabaseType` v odvozování jazykového typu. Volání je přitom jen šest ve čtyřech souborech, takže řídicí logika pipeline se nemění — zásah je široký v datech a úzký v toku.

**Žádná z tabulek nesmí končit výjimkou.** Čtyři `NotImplementedException` v převodech typů jsou pozůstatek stavu před rozhodnutím 010: typ, který cíl nezná, je záznam a artefakt vzniká chudší, ne pád uprostřed generování. Neznámý vstupní typ se stává `SourceSqlType` bez rodiny.

**Netypovaný kanál mezi parserem a builderem se musí odstranit.** Typ dnes přechází z obou parserů do `SetPropertyDatabaseMapping` jako stringifikovaný ordinál výčtu (`dbProps["type"] = ((int)…).ToString()` a zpět `(DatabaseType)int.Parse(…)`). Změna hodnot výčtu tímhle místem projde **bez jediného varování překladače**, takže je to nejrizikovější bod celé práce; opravit se má ve stejném průchodu, ne později.

**Facety se stávají nosnými.** Dosud šlo `Length`, `Precision` a `Scale` chápat jako ozdobu k typu; po tomhle rozhodnutí bez nich typ netvrdí totéž, co tvrdil zdroj. Fáze doplnění z katalogu je už plní a měří odděleně, takže mechanismus existuje; přibývá jen `IsUnicode` a `SourceSqlType`, obojí pod kategorií `DatabaseType`.

**Zpětný převod přestává být bijekcí i tam, kde býval.** Konvertor pro EF Core byl u data a času bijekcí, protože `HasColumnType` bere doslovný SQL typ; nově vyjde `datetime2` s odpovídající přesností a původní `datetime` drží `SourceSqlType`. Je to vědomá výměna: ztrácí se identita zápisu, získává se pravdivost tvrzení a přenositelnost. Kritériem korektnosti převodu proto nesmí být rovnost řetězců typu — což platilo už po rozhodnutí [012](012-foreign-key-rendering.md), kde totéž způsobily dogenerované vlastnosti.

**Deskriptor se strukturálně nemění.** Kategorie `MappingFactCategory.DatabaseType` zůstává; mění se jen množina hodnot, nad kterou se stav *umím vyjádřit* vyhodnocuje. Právě proto musí zůstat uzavřená.

Tvrzení o typech NHibernate v nových tabulkách je nutné při implementaci ověřit proti referenční tabulce verze 5.7.0. Rozhodnutí fixuje slovník a pravidlo, ne jednotlivé řádky převodu — ty jsou vlastností frameworku a jeho verze, a mýlka v nich je oprava tabulky, ne změna volby.
