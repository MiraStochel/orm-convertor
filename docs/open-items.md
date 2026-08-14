# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

---

## Doporučené pořadí

Nejbližší cíl je uzavřít práci s jednoduchými i kompozitními klíči ve všech třech .NET frameworcích, tedy F1–F3. Bod 1 k němu vede přímo a databázi nepotřebuje; teprve F3 v plném rozsahu — N:M přes spojovací tabulku a spuštěné testy — vyžaduje prostředí i katalog.

1. **Naplnění `ColumnPairs` v parserech pro entity převáděné společně** (práce) — cílové sloupce lze určit ze zdroje všude, kde je cílová entita součástí téhož převodu; teprve tím se vícesloupcový cizí klíč projeví v převodu, ne jen v modelu.
2. **Diagnostika převodu** (práce) — implementace rozhodnutí 010; odblokuje varování u Dapperu a je předpokladem čtení katalogu.
3. **Prostředí s databází pro vývoj a testy** (práce) — spuštěné testy vyžaduje F3; bez něj nelze otestovat ani čtení katalogu jinak než proti mocku.
4. **Čtení databázového katalogu** (práce) — implementace rozhodnutí 008; odblokuje odkaz mimo převod a Dapper jako plnohodnotný zdroj.
5. **Junction entita v builderech** (práce) — N:M přes spojovací tabulku, poslední kus F3.
6. **Neutralizace typového modelu** a **klíčová třída u formy `Embedded`** (rozhodnutí) — první blokuje F7–F10 i referenční navigace, druhá na něm stojí.
7. **Zbytek** podle priorit vyplývajících z požadavků F/S/E.

---

## Otevřená rozhodnutí

### Neutralizace typového modelu
*Rozsahem na samostatné rozhodnutí, ne na odstavec. Podklad: audit 2026-08-02, kap. 2.2–2.4 a 4.5.*

Nejrozsáhlejší otevřená položka, ve dvou rovinách:

1. `CLRType` → jazykově neutrální reprezentace (`LangType` podle JSS §5.2), s doplněním chybějících typů a s vyřešením případu `CLRType.Char`, který dnes nelze namapovat na správný typ NHibernate, protože v `DatabaseType` chybí hodnota pro jednotlivý unicode znak. Chybějící typ přitom není jen mezera v mapování: `CLRTypeConvertor.FromString` na neznámém názvu vyhodí `NotSupportedException`, takže entita s vlastností typu `Guid`, `short` nebo `uint` neprojde parsováním vůbec. `Guid` je z nich nejběžnější, mimo jiné jako typ primárního klíče — parser pro něj strategii `Uuid` odvodit umí, ale nikdy se k tomu nedostane.
2. `DatabaseType` → databázově neutrální reprezentace, případně s vrstvou pro dialekty. Dnešní výčet je fakticky seznam typů T-SQL. Sem patří i `sql-type` na vnořeném `<column>` elementu NHibernate — jediná cesta, jak v mapování udržet konkrétní SQL typ místo typu NHibernate; parser ho dnes nečte, protože nemá kam ho uložit. Sem rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md) odsunulo i slévání `DateTime`, `DateTime2` a `SmallDateTime` do jediného typu NHibernate: je to ztráta ve stejném smyslu jako nevyjádřitelný fakt, ale aby ji šlo ohlásit, musí být z převodu poznat, že zúžil — a to je práce tady, ne v diagnostice.

Je to **předpoklad** pro F7–F10, ne jejich příprava: javová ID třída se neobejde bez otypovaných polí, a ta vezme builder odsud.

### Kanonický slovník parametrů generátoru
*Navazuje na rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md). Předpoklad F7–F10.*

`StrategyParameters` nese parametry tak, jak je pojmenoval zdroj: u NHibernate `sequence` a `max_lo`, u JPA `sequenceName` a `allocationSize`. Uvnitř jednoho ekosystému to stačí, napříč nimi ne — cílový builder názvům zdroje nerozumí, takže je buď vypíše nesmyslně, nebo zahodí, a klíč vázaný na sekvenci se stane nespustitelným. Rozhodnout, jestli názvy kanonizovat v modelu, nebo jejich překlad nechat na wrapperech. Rozhodnutí 011 dalo parametrům místo v modelu a tuhle otázku nechalo vědomě otevřenou.

### Smí builder použít název strategie ze zdroje?
*Navazuje na rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md).*

Rozhodnutí 011 dalo `SourceStrategyName` roli záznamu pro diagnostiku, ne vstupu generování, takže builder vypisuje kanonický název: z NHibernate do NHibernate se `seqhilo` vrátí jako `hilo`, `guid.comb` jako `guid` a `foreign` dokonce jako `assigned`, ačkoli cíl všechno tohle přijme. U `foreign` to není kosmetika — je to jediné tvrzení, ze kterého se pozná vztah 1:1 přes sdílený primární klíč, takže s ním padá i informace o vztahu. Rozhodnout, jestli smí builder název ze zdroje použít tam, kde mu cílový framework rozumí — kritériem se nabízí, že se název zpětně mapuje na tutéž hodnotu výčtu — nebo jestli má vstupem generování zůstat jedině slovník a rozdíl hlásit diagnostika. Změna role toho pole je změna volby, ne doplnění, takže patří do nového rozhodnutí.

### Centrální správa verzí

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně. Dnes se může nepozorovaně rozejít.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s E-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce.

### Osud `wwwroot`
*Souvisí s S5.*

Automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit. Dnes je to commitnutý build, takže po každé změně frontendu hrozí, že nasazený bundle neodpovídá zdrojákům.

---

## Otevřená práce

### Prostředí s databází pro vývoj a testy
*Předpoklad čtení databázového katalogu. Souvisí s S5.*

Testovací projekt dnes nereferencuje žádného databázového klienta, žádný test spojení neotevírá a workflow v `.github` spouští `dotnet test` bez service containeru. Čtení katalogu je první práce, kterou takto otestovat nelze — zbyly by testy proti mocku, které ověří tvar volání, ne to, že se z katalogu vrátí správná metadata.

Potřeba je databáze dostupná testům lokálně i v CI a rozhodnutí, které testy na ní závisí a jak se zachovají, když není. `docker-compose.yml` staví SQL Server s WideWorldImporters přes `database.Dockerfile`, ale jako prostředí pro aplikaci; `ConnectionStrings__AdvisorDatabase` je v něm commitnutá deklarace, kterou zatím nikdo nespustil, takže se ověří tady.

### Čtení databázového katalogu
*Rozhodnutí [008](./decisions/008-database-as-metadata-source.md). Blokováno diagnostikou jako kategorií a prostředím s databází. Požadavky F4, F5, F6.*

Komponenta, která na jednom místě čte metadata z připojené databáze, a sestavení poptávky z deskriptoru cílového frameworku. Dnes žádné čtení katalogu pro doplnění mezireprezentace neexistuje — buildery chybějící fakt nahrazují konvencí a nikde to nezaznamenají.

Součástí je sjednocení dvou míst, která dnes tentýž problém řeší různě: `EFCoreLinqQueryParser.ResolveQualifiedTableName` doplňuje chybějící schéma heuristikou nad `EntityMaps`, `HarnessGenerationUtilities.ResolveQualifiedTableName` dotazem do `INFORMATION_SCHEMA` s prázdným `catch` při selhání spojení.

Fáze musí být měřitelná odděleně od času překladu (S3) a překlad bez připojené databáze nesmí selhat.

### Junction entita v builderech
*Rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md); vynucené členy bere z deskriptoru cílového frameworku (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)).*

Generování explicitní junction entity. Vícesloupcový cizí klíč už buildery vypsat umějí (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)), takže zbývá sama junction entita; testovací vstupy je pořád nutné skládat ručně přes builder API, protože `ColumnPairs` se automaticky neplní.

### Detekce N:M v parserech
*Blokováno jen zčásti — viz níže; rozhodnutí [008](./decisions/008-database-as-metadata-source.md).*

Detekce N:M na vstupu, syntéza junction entity a naplnění `ColumnPairs`. Cílové sloupce nejdou určit z jedné translation unit, takže to stojí na metadatech z databáze (F4/F5). Totéž místo je předznamenané i v NHibernate builderu, kde chybějící typ vlastnosti nese TODO „query database for the missing type". Blokace ale platí jen pro odkaz mimo převod. Je-li cílová entita součástí téhož vstupu, jsou její klíčové sloupce v mezireprezentaci a `ColumnPairs` lze naplnit ze zdroje; katalog je potřeba teprve tam, kde cílová entita ve vstupu není.

### Diagnostika převodu
*Rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md), naplňuje [004](./decisions/004-unexpressible-facts-as-warnings.md). Seznam faktů bere z deskriptoru cílového frameworku ([009](./decisions/009-target-framework-descriptor.md)). Požadavky F5, F11, E3.*

Návratový typ převodu, který nese artefakty i záznamy, a dvě místa, kde záznamy vznikají: kontrola úplnosti proti deskriptoru před generováním a záznamy o ztrátě při emisi. Záznam nese framework, artefakt, entitu a vlastnost, kategorii mapovacího faktu a důvod.

Dnes je nástroj tichý: Dapper builder klíče i vztahy zahazuje bez hlášení, `ConversionHandler.Convert` vrací jen `List<ConversionSource>`, takže kanál pro cokoli dalšího neexistuje, a chybějící jazykový typ končí výjimkou uprostřed generování. Se slovníkem strategií (rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md)) přibyla tři konkrétní zúžení, která na hlášení čekají: mechanismy `Identity`, `Sequence`, `HiLo`, `Uuid` a `Increment` anotace EF Core nevyjádří, `<composite-id>` v NHibernate neunese strategii žádnou, a strategii, kterou nikdo neuvedl, vypisuje NHibernate builder jako `assigned`, tedy jako konvenci cíle. Rozhodnutí [012](./decisions/012-foreign-key-rendering.md) přidalo čtyři další: neznámé sloupce cizího klíče, pořadí párů neodpovídající klíči cílové entity, N:M bez spojovací entity a nezachovanou hodnotu `property-ref` na inverzní straně. Změna se propíše do REST API; frontend zůstává na příště a je veden zvlášť.

Deskriptor tím dostane prvního konzumenta v produkčním kódu — dosud ho četl jen test.

### Cílová verze v deskriptoru
*Implementace rozhodnutí [013](./decisions/013-target-framework-versions.md) nad deskriptorem z rozhodnutí [009](./decisions/009-target-framework-descriptor.md). Požadavky S2, S6.*

Deskriptor cílového frameworku nese, co cíl umí vyjádřit, ale ne verzi, proti které to platí. Doplnit ji a nechat buildery volit syntaxi tam, kde se verze rozcházejí — u EF Core `[PrimaryKey]` proti `HasKey`, u NHibernate dostupnost `DateOnly`. Bez explicitní volby platí verze zafixovaná v `architecture.md`. Tímtéž údajem se pak plní záznam běhu podle S6, aby nemohl tvrdit něco jiného než generátor.

### Rozresolvování jmen entit před generováním
*Nesplněný důsledek rozhodnutí [001](./decisions/001-entity-reference-by-name.md); záznam podle [010](./decisions/010-diagnostics-as-returned-data.md). Požadavek F11.*

Vztah odkazuje na cílovou entitu jménem a důsledky rozhodnutí 001 slibují, že se všechna jména před generováním rozresolvují proti `EntityMaps` a nenalezené jméno bude chyba úplnosti se strukturovanou diagnostikou. V kódu nic takového není: `AbstractEntityBuilder.Build` projde entity a rovnou volá jednotlivé kroky, takže překlep v názvu cílové entity projde beze slova až do výstupu, kde z něj vznikne odkaz na třídu, která neexistuje. Kontrola patří na totéž místo jako kontrola úplnosti proti deskriptoru, tedy před generování.

Čekají na ni dvě věci: `property-ref` na inverzní straně vztahu 1:1, který rozhodnutí [012](./decisions/012-foreign-key-rendering.md) odkládá právě sem, protože potřebuje navigaci protistrany, a naplnění `ColumnPairs`, které pracuje s toutéž množinou entit.

### Dotazová větev
- `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec — poddotazy projdou, ale výsledek se nikam neskládá.
- `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu).
- `BuildSQL()` z původního návrhu neexistuje; rozlišení nativní syntaxe od syrového SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate.

### EF Core — nullabilita se vyjadřuje jen jazykově

Anotaci `[Required]` builder negeneruje. Databázová nullabilita z `PropertyMap.IsNullable` se propisuje jen do modifikátoru `required` a otazník za typem vychází z jazykové nullability vlastnosti. Parser přitom `[Required]` číst umí, takže vstup s ním se přeloží a zpět už tuto podobu nezíská. Deskriptor uvádí kategorii jako vyjádřitelnou, protože popisuje framework, ne dnešní stav builderu.

### Parser NHibernate — část klíče, která je cizím klíčem
*Navazuje na rozhodnutí [012](./decisions/012-foreign-key-rendering.md) a na naplnění `ColumnPairs`. Požadavky F1, F3.*

Uvnitř `<composite-id>` smí stát i `<key-many-to-one>` — část klíče, která je zároveň odkazem na jinou entitu. Smyčka v `NHibernateXMLMappingParser` bere jen `<key-property>`, takže taková část z klíče beze stopy zmizí a vznikne klíč o menším počtu částí, než jaký zdroj popsal; u dvousložkového klíče může zbýt jednosložkový, který se navíc tváří jako úplný. Vypsat takovou část klíče buildery po rozhodnutí [012](./decisions/012-foreign-key-rendering.md) umějí, takže zbývá ji přečíst a doplnit hlášení podle rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md).

### Klíčová třída u kompozitního klíče na straně entity
*Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md). Blokováno neutralizací typového modelu. Podklad: audit 2026-08-02, kap. 3.2.*

Mapovací stranu už parser čte: `<composite-id class=>` i `<composite-id name= class=>` skončí jako `SourceKeyClass`. U formy `Embedded` ale části klíče nejsou vlastnostmi entity, nýbrž klíčové třídy, a entita nese jedinou vlastnost jejího typu. Zbývá tedy trojí: odkud vzít jazykové typy částí, jak zabránit tomu, aby se držící vlastnost dostala do mezireprezentace jako běžná vlastnost (ploché vykreslení klíčovou třídu ruší), a co udělat s C# zdrojem klíčové třídy, pokud do převodu vstoupí — entitní parser by z něj dnes udělal další entitu.

Vstupní překážka je přitom dřív: `CLRTypeConvertor.FromString` na typu `OrderLineId` vyhodí `NotSupportedException`, takže entita té formy neprojde už parsováním. Je to táž překážka jako u vlastnosti odkazující na jinou entitu, a proto to patří k neutralizaci typového modelu, ne před ni.

### NHibernate builder — schéma se nepropisuje do mapování

`BuildTableSchema` čte `em.Schema`, ale při prázdné hodnotě dosadí prázdný řetězec a dál s ním nepracuje (TODO v kódu). Mapování tak vzniká bez `schema` atributu i tam, kde ho zdroj nese, což u databází s víc schématy vyrobí mapování mířící do výchozího schématu.

### Chybějící jazykový typ shodí generování

Vlastnost, kterou zná jen XML mapování a ne entitní třída, vznikne s `CLRType.None` a `CLRTypeConvertor.ToString` na ní vyhodí `NotSupportedException("None")` uprostřed generování. Neúplný vstup je tedy pád, ne diagnostika — proti F11, který žádá framework, artefakt, chybějící vlastnost a důvod selhání; z výjimky nejde určit ani entitu. Rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md) z toho dělá záznam o selhání: je to kategorie, kterou cíl vyžaduje a nikdo ji nedodal. Týž typový model brání i opačnému směru: navigační vlastnost odkazující na jinou entitu se do modelu nedostane vůbec, protože `CLRTypeConvertor.FromString` na názvu entity vyhodí `NotSupportedException` už v `AddProperty`. Vztah 1:1 nebo N:1 tedy dnes projde jen tam, kde navigaci založí mapovací parser bez jazykového typu — a generování C# na ní pak spadne.

Potřebný fakt je přitom po ruce: `PropertyMap.Type` databázový typ nese, jen opačný převod v `DatabaseTypeConvertor` neexistuje, ačkoli směr CLR → databáze je v něm jako `GuessFromPropertyType`. Podle rozhodnutí [008](./decisions/008-database-as-metadata-source.md) je odvození z databázového typu konvence třetího stupně a musí nést svůj původ.

### NHibernate builder — kolekce jen jako `<bag>`

Kolekční vlastnost se generuje natvrdo jako `<bag>` a ostatní kolekční tvary (`set`, `list`, `map`) ani další kolekční vlastnosti builder neřeší (dva TODO v kódu). Volba tvaru kolekce je v NHibernate sémantická — `set` vylučuje duplicity, `list` nese pořadí — takže dnešní stav mění chování, ne jen zápis.

### Frontend

Odloženo do cílenější přestavby, současný stav je funkční:

1. V `advisor-page.component.ts` přeskočit prázdné dotazové jednotky v `convert()` před odesláním — server je striktní záměrně, tolerance patří do UI.
2. V `main-page.component.ts` zobrazovat chyby ze serveru přes `err.error`, ne `err.message`; vzor je v `advisor-page.component.ts`.
3. Validace před odesláním podle S7, tedy chyby na úrovni souboru a řádku.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne:

| Blok | Co odblokuje |
|---|---|
| **F4–F6** metadata z databáze | `ColumnPairs`, detekci N:M v parserech, úplné mapování z neúplného vstupu |
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech, kontrolu úplnosti IR |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; stojí na neutralizaci typového modelu |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání |
| **E1–E7** experimenty | E7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání zůstává rozpracovaná: chybí NHibernate LINQ parser, Dapper SQL parser a query buildery pro EF Core i NHibernate.