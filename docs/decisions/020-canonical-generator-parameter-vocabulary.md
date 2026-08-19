# 020 — Kanonický slovník parametrů generátoru

Datum: 2026-08-19
Stav: platí
Požadavky: F1, F2, F7–F10, F11, S1, S2
Podklad: rozhodnutí [011](011-key-generation-strategy-vocabulary.md), které otázku vědomě nechalo otevřenou; `analysis/orm-frameworks-comparison.md`; Jakarta Persistence 3.2 — `@SequenceGenerator` a `@TableGenerator`; NHibernate 5.7.0 — generátory `sequence`, `hilo`, `seqhilo` a `foreign`

## Kontext

Rozhodnutí [011](011-key-generation-strategy-vocabulary.md) dalo parametrům generátoru místo v modelu — `StrategyParameters` na `PrimaryKeyPart` jako dvojice klíč–hodnota — a otázku, jak se ty klíče jmenují, nechalo otevřenou. Tohle rozhodnutí ji zavírá.

**Dnešní stav je doslovný přenos.** `ReadGeneratorParameters` v XML parseru NHibernate přebírá prvky `<param name="…">` tak, jak je napsal zdroj, a komentář to i vyslovuje: *kept as the source wrote them rather than translated into a vocabulary the model does not have*. `AppendGenerator` v builderu je zpětně pouhé echo — co přišlo, to vypíše. EF Core parametry neplní ani nečte, protože `[DatabaseGenerated]` je vyjádřit neumí a mechanismy `Sequence`, `HiLo` a další builder zahazuje jako fluent-only ztrátu. Smyčka je tedy uzavřená jen proto, že parametry plní a čte **tentýž** framework. Rozbije se prvním javovým parserem, a proto je tahle otázka předpokladem F7–F10, ne kosmetikou.

Že doslovný název není jen záznam, ale i vstup generování, se v kódu už jednou stalo: `SharesPrimaryKeyThrough` v builderu NHibernate hledá část klíče se `SourceStrategyName == "foreign"` a v jejích parametrech klíč `property`, aby poznal vztah 1:1 přes sdílený primární klíč (rozhodnutí [012](012-foreign-key-rendering.md)). Názvosloví jednoho frameworku je tam natvrdo a nese význam.

Rozdíl mezi ekosystémy má tři vrstvy a jen první z nich je pravopis.

**Jiná jména pro tutéž věc.** NHibernate říká `sequence`, `max_lo`, `table`, `column`; Jakarta Persistence `sequenceName`, `allocationSize`, `table`, `pkColumnName`, `valueColumnName`, `pkColumnValue`.

**Jiný význam pod podobným jménem.** `max_lo` je *nejvyšší nízká hodnota*, takže blok obsahuje `max_lo + 1` hodnot; `allocationSize` je rovnou velikost bloku. Přemapování jména na jméno posune blok o jedničku a navíc mlčky zamění výchozí hodnoty, které se mezi ekosystémy liší řádově. Je to táž pojmenovací past, kvůli které rozhodnutí [019](019-neutral-database-type-vocabulary.md) zvolilo `DoublePrecision` místo `Float` — a tam už jednou vznikla chyba v převodní tabulce. Slovník proto musí fixovat **význam a jednotku**, ne pravopis.

**Jiná struktura zápisu.** V NHibernate je generátor vepsaný do `<id>` a jeho parametry jsou lokální. V JPA je generátor samostatně deklarovaný pojmenovaný objekt — `@GeneratedValue(generator="…")` odkazuje na `@SequenceGenerator(name="…")`, který smí stát u jiné vlastnosti, u třídy nebo v `orm.xml` a smí ho sdílet víc entit. Plochý balík dvojic per část klíče sdílení nevyjádří.

## Zvažované varianty

1. **Ponechat doslovné názvy a překlad nechat na wrapperech.** Zdánlivě nejmenší zásah. Na straně builderu je ale **neproveditelný**: builder by musel vědět, ze kterého frameworku model přišel, a mezireprezentace původ faktu nenese — rozhodnutí [010](010-diagnostics-as-returned-data.md) ho z modelu vyňalo a [015](015-mapping-fact-completion-from-the-catalog.md) tu volbu převzalo. Jediná proveditelná podoba téhle varianty je normalizace v parseru, což je kanonizace pojmenovaná jinak. I kdyby původ v modelu byl, znamenala by tabulku od každého zdroje ke každému cíli, tedy párové převody, které parser–builder architektura odstraňuje (S1, JSS §4.3).

2. **Kanonická jména jako volné řetězce.** Dohodneme se na `sequenceName` a `blockSize`, ale typ zůstane `Dictionary<string, string>`. Levné a neověřitelné: překlep je tichý, úplnost převodních tabulek nejde zkontrolovat a množina platných klíčů není nikde vyslovená. Táž námitka, kterou rozhodnutí 019 zamítlo volný SQL typ a [014](014-language-type-model.md) volný jazykový typ.

3. **Typované objekty per mechanismus** — `SequenceParameters`, `HiLoParameters` a podobně. Nejpřesnější: jednotky i typy hlídá překladač. Mechanismy bez parametrů by ale dostaly prázdné typy a hlavně: fáze doplnění z katalogu (rozhodnutí 015) pracuje **po jednotlivých faktech** a každý dílčí zápis by se nad neměnným objektem stal přestavbou celku. To je doslova argument, kterým rozhodnutí 019 zamítlo hodnotový objekt `DbType`. Nadto by se neměnný tvar nedal použít tam, kde je strategie sama na únikové cestě.

4. **Uzavřený výčet kanonických parametrů a doslovný záznam vedle něj.**

## Rozhodnutí

Volíme variantu 4. **Parametry generátoru pojmenovává uzavřený výčet, který fixuje význam a jednotku, ne pravopis zdroje. Co se do něj nevejde, zaznamenáme vedle něj — ne místo něj.**

Je to potřetí týž vzorec, který rozhodnutí 011 zvolilo pro strategie klíče a samo o něm napsalo, že na něj narazíme znovu, a který 019 použilo pro databázové typy: *mezireprezentace nese průnik záměrů, framework-specifické podrobnosti nese vedle něj jako záznam o zdroji.*

Pojmenovací pravidlo přebíráme z 011 a doplňujeme o třetí větu: **neutrální název tam, kde tentýž záměr vyjadřuje víc ekosystémů; slovo zdroje tam, kde mechanismus umí jediný framework; a hodnota se ukládá v jednotce, kterou určuje slovník, ne v jednotce zdroje.**

| Hodnota | Význam | Co se na ni mapuje |
|---|---|---|
| `SequenceName` | název sekvence, ze které hodnota vzniká | NH `sequence` u `sequence` i `seqhilo`, JPA `sequenceName`, EF Core `UseSequence(name)` |
| `Schema` | schéma sekvence nebo tabulky čítače | JPA `schema`, NH kvalifikovaný název, EF Core `UseSequence(name, schema)` |
| `BlockSize` | **počet hodnot v jednom přiděleném bloku** | NH `max_lo` **+ 1**, JPA `allocationSize` |
| `InitialValue` | první vydaná hodnota | JPA `initialValue`, NH `initial_value` u rozšířených generátorů |
| `CounterTable` | tabulka, ve které čítač bydlí | NH `table`, JPA `table` |
| `CounterValueColumn` | sloupec s další vysokou hodnotou | NH `column`, JPA `valueColumnName` |
| `CounterKeyColumn` | sloupec, kterým se vybere řádek čítače | JPA `pkColumnName`; v NHibernate protějšek nemá |
| `CounterKeyValue` | hodnota, kterou se vybere řádek čítače | JPA `pkColumnValue`; v NHibernate protějšek nemá |

Osm hodnot. K jednotlivým volbám patří zdůvodnění, protože se o ně opře i text práce:

- **`BlockSize`, ne `MaxLo` ani `AllocationSize`.** Obojí je slovo jednoho ekosystému a obojí nese jinou aritmetiku. Neutrální název nutí obě strany převod vyslovit: parser NHibernate ukládá `max_lo + 1`, javový parser `allocationSize` beze změny. Kdyby slovník převzal kterékoli z těch dvou slov, převod by se dal „udělat" prostým přejmenováním a chyba o jedničku by se do něj vešla nepozorovaně.
- **`CounterTable` a `CounterValueColumn` místo `Table` a `Column`.** Holé `Table` by se v modelu, kde `EntityMap.Table` znamená tabulku entity, četlo jako tabulka téhle entity. Předpona říká, že jde o čítač mimo ni — což je přesně to, co mechanismus `HiLo` podle 011 definuje.
- **`CounterKeyColumn` a `CounterKeyValue` slovník obsahuje, ačkoli je NHibernate nemá.** Tabulkový generátor JPA drží víc čítačů v jedné tabulce a vybírá řádek klíčem; NHibernate `hilo` počítá s tabulkou o jednom řádku. Fakt tedy existuje na jedné straně, a to je právě případ, kdy má být v modelu a při převodu do NHibernate skončit záznamem o ztrátě — ne zmizet cestou.
- **Název generátoru do slovníku nedáváme.** `@SequenceGenerator(name="…")` není parametrem mechanismu, ale identitou deklarovaného objektu; patří k otázce sdílení, kterou vylučujeme níže. Zůstává doslovným záznamem o zdroji.
- **Parametr, který je vlastností jednoho generátoru a nic neznamená jinde** — například `where` u `hilo` v NHibernate — hodnotu nedostává. Zůstává na doslovné cestě, přesně jako generátor mimo slovník u rozhodnutí 011.

**Normalizuje parser, nikoli builder.** Jen parser ví, co jeho framework tím slovem myslí, a musí to vědět per generátor: `sequence` znamená název sekvence u `sequence` i `seqhilo`, kdežto `table` a `column` dávají smysl jen u `hilo`. Builder čte kanonický slovník a překládá do svého názvosloví. Tím se drží dělba, na které stojí S1 a JSS §4.3 — každý wrapper zná jen svůj framework — a odpadá potřeba znát původ modelu.

**Model dostane dvě pole místo jednoho.** `StrategyParameters` se stává typovaným kanonickým slovníkem `IReadOnlyDictionary<GeneratorParameter, string>`; dnešní doslovný obsah přebírá nové pole `SourceStrategyParameters` typu `Dictionary<string, string>`. Je to táž dvojice, jakou už `PrimaryKeyPart` má u samotné strategie — kanonická hodnota a vedle ní záznam o zdroji (`SourceStrategyName`), a jakou má `PropertyMap` v `Type` a `SourceSqlType` po rozhodnutí 019. Pojmenování se řídí zavedenou konvencí: kanonická věc drží holý název, záznam o zdroji předponu `Source`.

Doslovná cesta není ozdoba, nese dnes zátěž: bez ní přestane fungovat rozpoznání sdíleného primárního klíče, které čte `property` u generátoru `foreign`. Z toho plyne pravidlo, které dvojici spojuje: **strategie, která skončila na únikové cestě, si tam bere i své parametry.** Je-li `Strategy` rovna `Unspecified` a zdroj přitom generátor pojmenoval, jsou jeho parametry pro nás neinterpretovatelné a zůstávají doslova; kanonizují se jen parametry rozpoznaného mechanismu. U mechanismu rozpoznaného pod cizím jménem — `seqhilo` je `HiLo` — se kanonizuje to, čemu rozumíme, a zbytek jde doslova vedle.

**Výchozí hodnoty nedoplňujeme.** Parametr, který zdroj neuvedl, zůstane neuvedený a cíl použije vlastní konvenci. Materializovat výchozí hodnotu zdroje by znamenalo tvrdit za zdroj něco, co neřekl; to je přesně ta konvence, jejíž kritérium je otevřenou položkou, a tohle rozhodnutí ho nepředjímá. Přiznáváme ale důsledek: výchozí velikost bloku se mezi ekosystémy liší, takže překlad `hilo` bez `max_lo` do JPA změní chování za běhu, aniž by se změnil jediný vyslovený fakt. Až kritérium konvencí vznikne, tohle je jeho případ.

**Nová kategorie mapovacích faktů nevzniká.** Parametry jsou součástí tvrzení o strategii a spadají pod `MappingFactCategory.PrimaryKeyStrategy`. Granularita kategorií je záměrně hrubá — jedna hodnota za fakt, který umí dodat katalog a vypsat builder — a týmž argumentem podřadilo rozhodnutí 019 facetu `IsUnicode` pod `DatabaseType`. Deskriptor se tedy strukturálně nemění.

**Mimo rozsah** zůstává sdílení pojmenovaného generátoru mezi entitami a jeho deklarace mimo entitní třídu. Číst `@SequenceGenerator` z jiného souboru je vícesouborový vstup podle F14, pro který rozhodnutí neexistuje — táž hranice, na kterou naráží položka o klíčové třídě. Javový builder proto vypíše deklaraci u té entity, které se týká, a jméno si odvodí deterministicky z entity a vlastnosti (S2). Ztráta je přitom menší, než vypadá: dvě entity odkazující na stejnou `sequenceName` sdílejí tutéž databázovou sekvenci i tehdy, když má každá vlastní deklaraci. Rozchází se zápis, ne chování — a rozdíl se ohlásí záznamem podle rozhodnutí 010.

## Důsledky

Přepis je jednorázový a bez přechodného období (rozhodnutí [003](003-one-shot-migration.md)).

**Parser NHibernate přestává být doslovný.** `ReadGeneratorParameters` dostane jako vstup i třídu generátoru a podle ní rozhodne, co který název znamená; `max_lo` se ukládá jako `BlockSize` o jedničku větší, `sequence` jako `SequenceName`, `table` a `column` jako sloupce čítače, zbytek jde do `SourceStrategyParameters`. Builder NHibernate přestává echovat: kanonické hodnoty vypisuje pod názvy své verze a pro strategii z únikové cesty vypíše doslovné parametry beze změny, takže rozpoznání sdíleného klíče zůstává, jak je.

**EF Core se dnes nemění.** Anotace parametry generátoru nevyjádří vůbec a mechanismy, které je mají, builder už zahazuje se záznamem o ztrátě. Změna se ho dotkne, až vznikne cesta přes fluent API — a ta má vlastní otevřenou položku.

**Parametr, který cíl vyjádřit neumí, je záznam `Loss` podle rozhodnutí 010**, ne pád ani tichý výpis. Konkrétně `CounterKeyColumn` a `CounterKeyValue` při překladu do NHibernate a všechny parametry při překladu do EF Core anotacemi.

**Determinismus se zlepší.** Dnes se `<param>` vypisují v pořadí, které dává výčet `Dictionary<string, string>`, tedy v pořadí nezaručeném; nad výčtovým klíčem lze vypisovat v pořadí hodnot výčtu, což je stabilní vlastnost modelu, ne vstupu. To je přesně to, co žádá S2.

**Testy.** Obousměrný převod NHibernate pro `sequence`, `hilo` a `seqhilo` s uvedenými parametry, včetně kontroly, že `max_lo` přežije cestu tam a zpět jako totéž číslo. A regresní test na sdílený primární klíč přes `foreign`, protože ten je jediným dnešním čtenářem doslovné cesty.

**Konkrétní názvy a výchozí hodnoty parametrů je nutné při implementaci ověřit proti zafixovaným verzím** (NHibernate 5.7.0, Jakarta Persistence 3.2 podle rozhodnutí [013](013-target-framework-versions.md)). Rozhodnutí fixuje slovník, jednotku a pravidlo; jednotlivé řádky převodu jsou vlastností frameworku a jeho verze, a mýlka v nich je oprava tabulky, ne změna volby.

**Rozšíření slovníku o parametr, který se řídí pravidlem výše, je věc implementace, ne nového rozhodnutí.** Javové parsery přinesou mechanismy, které dnes v repozitáři nemáme čím ověřit, a je čekaný stav, že se výčet o jednu dvě hodnoty doplní. Nové rozhodnutí potřebuje až změna samotného pravidla — například kdyby se ukázalo, že jednotku nelze určit bez znalosti cíle.
