# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

---

## Doporučené pořadí

Nejbližší cíl je **překlad z Dapperu do EF Core a NHibernate s mapovacími fakty doplněnými z databázového katalogu**, tedy F6 a s ním F4. Jazykový typový model (rozhodnutí 014), fáze rozresolvování jmen entit s naplněním `ColumnPairs` i diagnostika převodu (rozhodnutí 010) jsou hotové, takže vztahy mezi entitami téhož převodu procházejí od parseru po výstup i se sloupci a převod vrací vedle artefaktů strukturované záznamy. F1 je pokryté nad mezireprezentací: `PrimaryKeyTest` drží jednoduchý klíč, `CompositeKeyTest` složené klíče o dvou, třech a čtyřech částech. Hotové je i **prostředí s testovací databází** — lokální instance, schéma vlastněné testy a přeskakování s uvedeným důvodem, když databáze není (viz `architecture.md`, §6.1).

Pořadí vychází z rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md). Ze zdroje v Dapperu nepřijde **nic** — parser klíč nevytvoří nikdy a deskriptor má všechny mapovací kategorie jako nevyjádřitelné —, takže tenhle převod stojí a padá s katalogem a katalog s připojenou databází. Kritérium F6 zároveň žádá *kompilovatelnou* cílovou entitu, o čemž tvarová aserce rozhodnout nemůže. Ověření překladem a přijetím frameworku je proto první krok a čtečka katalogu druhý; v tomto pořadí proto, že fakty doplněné z katalogu posoudí až cílový framework, a čtečka tak dostane verdikt hned, místo aby ji zatím soudily aserce nad mapováním, které jsme si sami poskládali.

1. **Ověření generovaných artefaktů překladem a přijetím frameworku** (práce) — 2. a 3. stupeň rozhodnutí 016, doslovné kritérium F6 i F2. Na převodu EF Core ↔ NHibernate databázi nepotřebuje.
2. **Čtení databázového katalogu** (práce) — implementace rozhodnutí 015. Tím je nejbližší cíl hotový: Dapper → EF Core a NHibernate s úplným mapováním, souzené 3. stupněm. Odblokuje zároveň odkaz mimo převod a detekci N:M.
3. **Junction entita v builderech** (práce) — generující část F3; `ColumnPairs` mezi entitami téhož převodu na ni stačí.
4. **Parser NHibernate — část klíče, která je cizím klíčem** (práce) — přímá mezera v F1 i F3, bez jakékoli infrastruktury; dnes taková část z klíče beze stopy zmizí. Na ničem výše nezávisí, takže ji lze vzít kdykoli.
5. **Klíčová třída u formy `Embedded`** (rozhodnutí) — pravděpodobně si ji vynutí krok 1 dřív, než na ni dojde řada: u té formy nejsou části klíče vlastnostmi entity, takže mapování odkáže na vlastnost, kterou třída nemá, a přijetí frameworkem to odhalí. Tvarová aserce to dodnes odhalit nemohla.
6. **Zbytek** podle priorit vyplývajících z požadavků F/S/T.

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

### Ověření generovaných artefaktů překladem a přijetím frameworku
*Rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md), 2. a 3. stupeň. Požadavky F2, F6, F11.*

Testy nad mezireprezentací dnes drží model — pořadí částí klíče, názvy sloupců, databázové i jazykové typy, strategii —, o vygenerovaném artefaktu ale tvrdí jen tvar textu. `NHibernateCompositeKeyRoundTrip` čeká výskyt `<key-property name="OrderID" column="OrderId" type="Int32" />` a pořadí tří takových elementů; to dokazuje, že builder napsal, co jsme čekali, ne že to NHibernate přijme. Doslovné kritérium F6 („kompilovatelná cílová entita") ani F2 („výstup se musí zkompilovat") tím splnit nelze a F11 nemá čím doložit syntaktickou správnost generovaných souborů.

Chybí dvě věci. **Překlad:** Roslyn nad generovaným C# a ověření mapování NHibernate proti XSD. Kompilace přes Roslyn v repozitáři už je, ale v podobě, která se sem nehodí — `RoslynBenchmarkCompiler` a `BenchmarkReferenceProvider` jsou `internal` v `AdvisorBenchmarking`, kompilátor při neúspěchu vyhazuje výjimku a sestavení rovnou zavádí do `AssemblyLoadContext`; ověření potřebuje vrácené diagnostiky a žádné zavádění. Druhá cesta přes Roslyn vedle první je ale právě ta dvojí odpověď na tutéž otázku, kterou vytklo rozhodnutí 015 kvalifikaci názvu tabulky, takže krok překladu potřebuje jedno místo použitelné z obou stran.

**Přijetí frameworkem:** `BuildSessionFactory` nad konfigurací NHibernate s pouhým dialektem a sestavení modelu EF Core z `DbContext`, kterému stačí připojovací řetězec jako text. Zachytí se tím mapování odkazující na neexistující vlastnost, klíčová třída bez `Equals` a `GetHashCode` i klíčová část bez typu, tedy třída chyb pojmenovaná rozhodnutím [006](./decisions/006-flat-composite-key-rendering.md); je to zároveň jediné skutečné ověření vynucených členů z rozhodnutí [009](./decisions/009-target-framework-descriptor.md). U mapování doplněného z katalogu je to navíc jediný verdikt, který něco znamená — fakty nepřišly ze zdroje, takže jinak než přijetím u cíle se jejich správnost nepozná.

Databázi nevyžaduje ani jeden z obou stupňů tam, kde zdroj klíč vyjadřuje, tedy na převodu EF Core ↔ NHibernate. Scénáře se zdrojem v Dapperu vzniknou až s hotovou čtečkou katalogu a poběží proti databázi; do té doby se přeskakují (rozhodnutí 016).

Testovací projekt kvůli tomu získá balíky cílových frameworků (EF Core, NHibernate, Dapper) — `Microsoft.Data.SqlClient` v něm už je z prostředí s testovací databází, NHibernate naopak v tomto řešení dosud není vůbec, je jen v odděleném `benchmarks/`. S1 to neporušuje: wrappery zůstávají bez závislosti na frameworku, pro který generují. Testy tím ale začnou tvrdit něco o konkrétní verzi, takže se rozejití verze v testech a v deskriptoru (rozhodnutí [013](./decisions/013-target-framework-versions.md)) stává tichou vadou.

### Čtení databázového katalogu
*Rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Nejbližší cíl; prostředí s testovací databází, proti kterému se ověří, už stojí (`architecture.md`, §6.1), a soudí ho 2. a 3. stupeň ověření (rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md)). Diagnostický kanál podle rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md) už existuje a čtečka do něj přidá záznamy o původu doplněného faktu a o konfliktu se zdrojem. Požadavky F4, F5, F6.*

Komponenta, která na jednom místě čte metadata z připojené databáze, a sestavení poptávky z deskriptoru cílového frameworku. Dnes žádné čtení katalogu pro doplnění mezireprezentace neexistuje — buildery chybějící fakt nahrazují konvencí; entita bez klíče nebo vlastnost bez jazykového typu se u cíle, který je vyžaduje, odmítne se záznamem o selhání a právě tyhle fakty má katalog dodávat.

Cílovým scénářem je Dapper jako zdroj: entita nesoucí jen názvy vlastností a jazykové typy má vyjít jako úplné mapování EF Core nebo NHibernate se správným názvem tabulky a schématu, sloupci, typy, klíčem a cizími klíči. Poptávku sestavuje deskriptor cílového frameworku (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)), načítá se celý sloupcový obraz dotčených tabulek, zápis je přírůstkový a idempotentní a zdroj má přednost před katalogem — rozpor se hlásí, nepřepisuje.

Sem patří i odvození jazykového typu vlastnosti, kterou zná jen mapování: `PropertyMap.Type` databázový typ nese, jen opačný převod v `DatabaseTypeConvertor` neexistuje, ačkoli směr jazyk → databáze je v něm jako `GuessFromScalarType`. Podle rozhodnutí 015 je odvození z databázového typu konvence třetího stupně a musí nést svůj původ.

Součástí je sjednocení dvou míst, která dnes tentýž problém řeší různě: `EFCoreLinqQueryParser.ResolveQualifiedTableName` doplňuje chybějící schéma heuristikou nad `EntityMaps`, `HarnessGenerationUtilities.ResolveQualifiedTableName` dotazem do `INFORMATION_SCHEMA` s prázdným `catch` při selhání spojení.

Fáze musí být měřitelná odděleně od času překladu (S3) a překlad bez připojené databáze nesmí selhat.

### Kontejnerová konfigurace prostředí
*Dohnání S5 odložené rozhodnutím [016](./decisions/016-generated-artifact-verification-levels.md).*

S5 žádá celý systém včetně databáze spustitelný dokumentovanou kontejnerovou konfigurací, kde čisté prostředí reprodukuje testy jedním hlavním příkazem. Lokální instance zvolená rozhodnutím 016 to nesplňuje a splnit nemá. Protože ale o hostiteli rozhoduje konfigurace, a ne kód testů, jde o přidání služby a proměnné prostředí, ne o návrat k rozhodnutí.

Patří sem trojí: service container do workflow v `.github` spolu s proměnnou `ConnectionStrings__TestDatabase`, aby databázově závislé testy běžely i v CI (dnes se tam přeskakují, protože proměnná není nastavená), volba mezi Docker Compose a Testcontainers pro lokální reprodukci, a ověření `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml` — commitnutá deklarace, kterou dosud nikdo nespustil a jejíž ověření dřívější plán čekal od stavby testovacího prostředí.

### Junction entita v builderech
*Rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md); vynucené členy bere z deskriptoru cílového frameworku (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)).*

Generování explicitní junction entity. Vícesloupcový cizí klíč už buildery vypsat umějí (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)) a `ColumnPairs` se mezi entitami téhož převodu plní z uvedených sloupců, takže zbývá sama junction entita. Fáze rozresolvování N:M vztahy záměrně nepáruje — sloupce jejich `<key>` patří spojovací tabulce, která má být entitou; parser NHibernate je u `<many-to-many>` čte a předává, takže na syntézu junction entity čekají připravené.

### Detekce N:M v parserech
*Blokováno jen zčásti — viz níže; rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md).*

Detekce N:M na vstupu a syntéza junction entity. Cílové sloupce nejdou určit z jedné translation unit, takže to stojí na metadatech z databáze (F4/F5). Totéž místo je předznamenané i v NHibernate builderu, kde chybějící typ vlastnosti nese TODO „query database for the missing type". Blokace ale platí jen pro odkaz mimo převod: mezi entitami téhož vstupu se `ColumnPairs` už plní ve fázi rozresolvování a katalog je potřeba teprve tam, kde cílová entita ve vstupu není.

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
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech a kontrola úplnosti IR jsou hotové (rozhodnutí 010), syntaktické ověření generovaných souborů je krokem 2 výše; zbývá záznam běhu podle S6 |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; jazykovou stranu typového modelu už má (rozhodnutí 014), stojí ještě na neutralizaci databázové strany |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání |
| **T1–T7** experimenty | T7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání zůstává rozpracovaná: chybí NHibernate LINQ parser, Dapper SQL parser a query buildery pro EF Core i NHibernate.