# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

---

## Doporučené pořadí

Dosavadní nejbližší cíl — **překlad z Dapperu do EF Core a NHibernate s mapovacími fakty doplněnými z databázového katalogu** (F6 a s ním F4) — je implementovaný: fáze doplnění podle rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) čte katalog jedinou komponentou, zapisuje podle poptávky deskriptoru s prioritou zdroj → katalog → konvence, hlásí původ i konflikty záznamy `Supplied`/`Conflict` a měří se odděleně (viz `architecture.md`, §5.2). Scénáře 2. a 3. stupně se zdrojem v Dapperu existují a běží proti testovací databázi (§6.2); kritéria F4 a F6 platí jen tam, kde běh s databází skutečně proběhl (rozhodnutí 016). Hotové zůstává, co bylo: jazykový typový model (rozhodnutí 014), fáze rozresolvování s naplněním `ColumnPairs`, diagnostika převodu (rozhodnutí 010), F1 nad mezireprezentací (`PrimaryKeyTest`, `CompositeKeyTest`), prostředí s testovací databází (§6.1) i nasucho běžící ověření převodu EF Core ↔ NHibernate.

1. **Junction entita v builderech** (práce) — generující část F3; `ColumnPairs` mezi entitami téhož převodu na ni stačí.
2. **Parser NHibernate — část klíče, která je cizím klíčem** (práce) — přímá mezera v F1 i F3, bez jakékoli infrastruktury; dnes taková část z klíče beze stopy zmizí. Na ničem výše nezávisí, takže ji lze vzít kdykoli.
3. **Detekce N:M v parserech** (práce) — čtečkou katalogu odblokovaná syntéza junction entity tam, kde ji vstup nenese.
4. **Klíčová třída u formy `Embedded`** (rozhodnutí) — přijetí frameworkem ji už umí odhalit: u té formy nejsou části klíče vlastnostmi entity, takže mapování odkáže na vlastnost, kterou třída nemá, a 3. stupeň takový pár odmítne. Tvarová aserce to odhalit nemohla.
5. **Zbytek** podle priorit vyplývajících z požadavků F/S/T.

---

## Otevřená rozhodnutí

### Neutralizace databázového typu
*Navazuje na rozhodnutí [014](./decisions/014-language-type-model.md), které vyřešilo jazykovou stranu. Předpoklad F7–F10 nad jiným DBMS. Podklad: audit 2026-08-02, kap. 2.2–2.4 a 4.5.*

`DatabaseType` je fakticky seznam typů T-SQL. Dokud oba ekosystémy míří na týž SQL Server, nic to nelže, ale s jiným DBMS — nebo s dialektem, který si Hibernate odvodí z JDBC metadat — přestane platit. Sem patří i `sql-type` na vnořeném `<column>` elementu NHibernate, jediná cesta, jak v mapování udržet konkrétní SQL typ místo typu frameworku; parser ho dnes nečte, protože nemá kam ho uložit. A sem patří případ `Char`, který dnes nelze namapovat na správný typ NHibernate, protože ve výčtu chybí hodnota pro jednotlivý unicode znak.

### Kanonický slovník parametrů generátoru
*Navazuje na rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md). Předpoklad F7–F10.*

`StrategyParameters` nese parametry tak, jak je pojmenoval zdroj: u NHibernate `sequence` a `max_lo`, u JPA `sequenceName` a `allocationSize`. Uvnitř jednoho ekosystému to stačí, napříč nimi ne — cílový builder názvům zdroje nerozumí, takže je buď vypíše nesmyslně, nebo zahodí, a klíč vázaný na sekvenci se stane nespustitelným. Rozhodnout, jestli názvy kanonizovat v modelu, nebo jejich překlad nechat na wrapperech. Rozhodnutí 011 dalo parametrům místo v modelu a tuhle otázku nechalo vědomě otevřenou.

### Smí builder použít název strategie ze zdroje?
*Navazuje na rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md).*

Rozhodnutí 011 dalo `SourceStrategyName` roli záznamu pro diagnostiku, ne vstupu generování, takže builder vypisuje kanonický název: z NHibernate do NHibernate se `seqhilo` vrátí jako `hilo`, `guid.comb` jako `guid` a `foreign` dokonce jako `assigned`, ačkoli cíl všechno tohle přijme. U `foreign` to není kosmetika — je to jediné tvrzení, ze kterého se pozná vztah 1:1 přes sdílený primární klíč, takže s ním padá i informace o vztahu. Rozhodnout, jestli smí builder název ze zdroje použít tam, kde mu cílový framework rozumí — kritériem se nabízí, že se název zpětně mapuje na tutéž hodnotu výčtu — nebo jestli má vstupem generování zůstat jedině slovník a rozdíl hlásit diagnostika. Změna role toho pole je změna volby, ne doplnění, takže patří do nového rozhodnutí.

### Kritérium pro širší čtení konvencí zdroje
*Navazuje na rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), které v tomto bodě nahradilo [008](./decisions/008-database-as-metadata-source.md). Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F2, F5, F6.*

Parsery dnes čtou konvenci zdrojového frameworku jen tam, kde by její neznalost změnila význam, což je v praxi jediné místo: primární klíč v `EFCoreEntityParser.FindConventionKey`. Implicitní je ale i název sloupce, název tabulky a nullabilita odvozená z jazykového typu, a u MyBatisu je konvenční mapování jediné, které vůbec existuje, takže F6 na širším čtení stojí. Rozhodnutí 008 otázku odložilo s podmínkou „až mezireprezentace bude evidovat původ faktu"; rozhodnutí 010 ale původ z modelu vyňalo a 015 tuhle volbu převzalo, takže odkládací podmínka nikdy nenastane a otázka zůstala bez kritéria. Je třeba rozhodnout, podle čeho se pozná, kterou konvenci materializovat. Nabízí se „materializuj tam, kde by cíl doplnil něco jiného, než tvrdí zdroj", to ale znamená znát konvence obou stran a mít je kde zapsat — pravděpodobně v deskriptoru, který dnes kategorii pro konvence nemá. Dokud kritérium chybí, přibývají konvence jednotlivě a bez pravidla, jak se to stalo u klíče v EF Core.

### Centrální správa verzí

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně. Dnes se může nepozorovaně rozejít.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce.

### Osud `wwwroot`
*Souvisí s S5.*

Automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit. Dnes je to commitnutý build, takže po každé změně frontendu hrozí, že nasazený bundle neodpovídá zdrojákům.

---

## Otevřená práce

### Kontejnerová konfigurace prostředí
*Dohnání S5 odložené rozhodnutím [016](./decisions/016-generated-artifact-verification-levels.md).*

S5 žádá celý systém včetně databáze spustitelný dokumentovanou kontejnerovou konfigurací, kde čisté prostředí reprodukuje testy jedním hlavním příkazem. Lokální instance zvolená rozhodnutím 016 to nesplňuje a splnit nemá. Protože ale o hostiteli rozhoduje konfigurace, a ne kód testů, jde o přidání služby a proměnné prostředí, ne o návrat k rozhodnutí.

Patří sem trojí: service container do workflow v `.github` spolu s proměnnou `ConnectionStrings__TestDatabase`, aby databázově závislé testy běžely i v CI (dnes se tam přeskakují, protože proměnná není nastavená), volba mezi Docker Compose a Testcontainers pro lokální reprodukci, a ověření `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml` — commitnutá deklarace, kterou dosud nikdo nespustil a jejíž ověření dřívější plán čekal od stavby testovacího prostředí.

### Junction entita v builderech
*Rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md); vynucené členy bere z deskriptoru cílového frameworku (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)).*

Generování explicitní junction entity. Vícesloupcový cizí klíč už buildery vypsat umějí (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)) a `ColumnPairs` se mezi entitami téhož převodu plní z uvedených sloupců, takže zbývá sama junction entita. Fáze rozresolvování N:M vztahy záměrně nepáruje — sloupce jejich `<key>` patří spojovací tabulce, která má být entitou; parser NHibernate je u `<many-to-many>` čte a předává, takže na syntézu junction entity čekají připravené.

### Detekce N:M v parserech
*Odblokováno čtečkou katalogu; rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) a [005](./decisions/005-many-to-many-as-explicit-junction-entity.md).*

Detekce N:M na vstupu a syntéza junction entity. Cílové sloupce nejdou určit z jedné translation unit; mezi entitami téhož vstupu se `ColumnPairs` plní ve fázi rozresolvování a metadata pro zbytek už umí dodat fáze doplnění z katalogu (`architecture.md`, §5.2). Co zbývá, je sama detekce: poznat, že tabulka, jejíž celý klíč tvoří dva cizí klíče, je spojovací (`IsJunctionTable`), a syntetizovat junction entitu tam, kde ji vstup nenese — fáze doplnění dnes vztahy jen páruje a syntetizuje N:1/1:1 přes existující navigace, spojovací entitu nevyrábí.

### Cílová verze v deskriptoru
*Implementace rozhodnutí [013](./decisions/013-target-framework-versions.md) nad deskriptorem z rozhodnutí [009](./decisions/009-target-framework-descriptor.md). Požadavky S2, S6.*

Deskriptor cílového frameworku nese, co cíl umí vyjádřit, ale ne verzi, proti které to platí. Doplnit ji a nechat buildery volit syntaxi tam, kde se verze rozcházejí — u EF Core `[PrimaryKey]` proti `HasKey`, u NHibernate dostupnost `DateOnly`. Bez explicitní volby platí verze zafixovaná v `architecture.md`. Tímtéž údajem se pak plní záznam běhu podle S6, aby nemohl tvrdit něco jiného než generátor.

### Rozresolvování jmen entit — `property-ref` na inverzní straně
*Zbytek důsledku rozhodnutí [001](./decisions/001-entity-reference-by-name.md) a [012](./decisions/012-foreign-key-rendering.md). Požadavek F11.*

Fáze rozresolvování (`ResolveEntityNames` v `AbstractEntityBuilder`) běží před generováním, plní `ColumnPairs`, povyšuje `Unknown` typy na reference a nenalezené jméno cílové entity i nesouhlasící počet či pořadí sloupců hlásí záznamem podle rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md). Z důsledků rozhodnutí 001 tak zbývá jediné: `property-ref` na inverzní straně vztahu 1:1, který rozhodnutí 012 odkládá právě sem. Navigace protistrany je po rozresolvování dostupná, ale NHibernate builder ji zatím nehledá a atribut nevypisuje; zahozenou hodnotu ze vstupu aspoň hlásí parser záznamem o ztrátě.

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
*Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md) a [014](./decisions/014-language-type-model.md). Podklad: audit 2026-08-02, kap. 3.2.*

Mapovací stranu už parser čte: `<composite-id class=>` i `<composite-id name= class=>` skončí jako `SourceKeyClass`. U formy `Embedded` ale části klíče nejsou vlastnostmi entity, nýbrž klíčové třídy, a entita nese jedinou vlastnost jejího typu. Zbývá tedy trojí: odkud vzít jazykové typy částí, jak zabránit tomu, aby se držící vlastnost dostala do mezireprezentace jako běžná vlastnost (ploché vykreslení klíčovou třídu ruší), a co udělat s C# zdrojem klíčové třídy, pokud do převodu vstoupí — entitní parser by z něj dnes udělal další entitu.

Vstupní překážku odstranil jazykový typový model: vlastnost typu `OrderLineId` projde parsováním jako `Unknown` a entita té formy se dostane do mezireprezentace. Trojí otázka výše tím ale zodpovězená není, proto zůstává rozhodnutím.

### NHibernate builder — schéma se nepropisuje do mapování

`BuildTableSchema` čte `em.Schema`, ale při prázdné hodnotě dosadí prázdný řetězec a dál s ním nepracuje (TODO v kódu). Mapování tak vzniká bez `schema` atributu i tam, kde ho zdroj nese, což u databází s víc schématy vyrobí mapování mířící do výchozího schématu.

### NHibernate builder — kolekce jen jako `<bag>`

Kolekční vlastnost se generuje natvrdo jako `<bag>` a ostatní kolekční tvary (`set`, `list`, `map`) ani další kolekční vlastnosti builder neřeší (dva TODO v kódu). Volba tvaru kolekce je v NHibernate sémantická — `set` vylučuje duplicity, `list` nese pořadí — takže dnešní stav mění chování, ne jen zápis. Model už druh kolekce nese (`CollectionKind` na `LangType`, rozhodnutí [014](./decisions/014-language-type-model.md)) a plní ho jazyková strana (`HashSet<T>` → `Set`); XML parser tvar elementu (`<set>` vs. `<bag>`) do modelu zatím nepropisuje a builder druh nečte — obojí patří k této položce.

### Frontend

Odloženo do cílenější přestavby, současný stav je funkční:

1. V `advisor-page.component.ts` přeskočit prázdné dotazové jednotky v `convert()` před odesláním — server je striktní záměrně, tolerance patří do UI.
2. V `main-page.component.ts` zobrazovat chyby ze serveru přes `err.error`, ne `err.message`; vzor je v `advisor-page.component.ts`.
3. Validace před odesláním podle S7, tedy chyby na úrovni souboru a řádku.
4. Zobrazit diagnostické záznamy, které `/convert` nově vrací v poli `records` vedle artefaktů (rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md)) — dnes je frontend ignoruje.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne. F4–F6 mezi nimi už nejsou — jsou nejbližším cílem a jejich práce je výše.

| Blok | Co odblokuje |
|---|---|
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech a kontrola úplnosti IR jsou hotové (rozhodnutí 010), syntaktické ověření generovaných souborů také (rozhodnutí 016, `architecture.md` §6.2); zbývá záznam běhu podle S6 |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; jazykovou stranu typového modelu už má (rozhodnutí 014), stojí ještě na neutralizaci databázové strany |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání |
| **T1–T7** experimenty | T7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání zůstává rozpracovaná: chybí NHibernate LINQ parser, Dapper SQL parser a query buildery pro EF Core i NHibernate.