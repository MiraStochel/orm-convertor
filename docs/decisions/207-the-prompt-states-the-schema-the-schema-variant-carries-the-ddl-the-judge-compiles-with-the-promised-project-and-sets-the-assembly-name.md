# 207 — Prompt každé varianty říká prostředí, které zná soudce — schéma tabulek jako `{{schema}}` a konvence konzumenta —, varianta `schema` dává modelu DDL katalogu, který nástroj čte, soudce překládá cizí artefakt s referencemi projektu, jejž prompt slibuje, dosazuje schéma klíče a jméno sestavení sám, parametr typu entity je vyslovená mez cesty a sada buňky je pole manifestu

Datum: 2026-11-07
Stav: revidováno
Požadavky: T4, T5, T6, T3, F4, F6, S2
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu", T5: „varianta poskytující modelu dokumentaci cílového frameworku, informace o schématu a relevantní příklady", T6: „samostatně vyhodnotit vliv databázových metadat"); rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), [028](028-assembly-name-is-not-ours-to-invent.md), [138](138-third-party-excerpts-are-verbatim-with-marked-changes.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [176](176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md), [186](186-the-package-graph-is-locked-and-restored-in-locked-mode.md), [189](189-a-web-frontend-attempt-is-a-new-conversation-with-one-self-contained-prompt-answered-in-text-without-the-web-and-paired-with-the-tool.md), [204](204-the-reply-is-a-whole-file-judged-as-one-zero-shot-as-written-and-the-import-repairs-the-envelope-of-every-other-variant.md); záznam [2026-11-06](../runs/2026-11-06-llm.md) a záznamy [2026-11-03](../runs/2026-11-03-llm.md), [2026-11-05](../runs/2026-11-05-llm.md); otevřená položka *Srovnání s jazykovým modelem nemá varianty s nástroji*; literatura v oddílu *Co dělají srovnatelné experimenty*

## Kontext

**Běh 2026-11-06 byl první, který model od nástroje rozlišil, a čtyři z jeho nálezů nejsou o modelu.** Záznam je třídí sám: co je obsah modelu (cesta, kterou entity nemají, typ, vnořený CASE, chybějící `HasNoKey`) a co je protokol. Protokolu patří čtyři věci, u nichž záznam říká „otevřená položka" nebo „je třeba rozhodnout", a toto rozhodnutí je bere najednou, protože mají jednu příčinu: **soudce ví o prostředí artefaktu víc, než prompt modelu říká, a některé z toho dává artefaktu nástroje a artefaktu modelu nestejně.**

1. **Schéma případové studie.** Soudce vytváří tabulky výňatku prázdné ve schématu na projekt v databázi případových studií a nástroj se je dozví z katalogu (rozh. 176, F4): jeho artefakt nese `Schema = "<schéma projektu>"` na každé tabulce. Výňatek sám žádné schéma nejmenuje (rozh. 138, doslovný přepis) a pravidlo 5 šablony `web-zero-shot-3` káže psát `{{schema}}` jen tam, kde ho má vstup. Model tedy nemá odkud schéma vzít, píše `[Table("Album")]` a `@Table(name = "alert_current")`, SQL Server hledá v `dbo` a čtvrtý stupeň končí *Invalid object name* — 25 pokusů pěti buněk, žádný pokus žádné ze sedmi studií nedošel ke čtvrtému stupni, zatímco nástroj má nad čtyřmi `ran`. A kde model `{{schema}}` napsal sám (Cofoundry, tři pokusy), cesta ho nahradila schématem fixtury sady, ne projektu: `ForeignArtifactRunTest` čte každý artefakt s `TestDatabase.SchemaName`, ačkoli projekt studie běží nad databází případových studií (`ForeignArtifactRun.RunProject`). Sedm buněk velké sady tak neměří překlad, ale to, že model neví, kde tabulky jsou.
2. **Sestavení soudce proti slibu promptu.** Tvar EF Core v šabloně slibuje, že „volající přeloží soubor … v projektu, který odkazuje Entity Framework Core s jeho poskytovatelem SQL Serveru". Takový projekt nese `Microsoft.Data.SqlClient` — `Microsoft.EntityFrameworkCore.SqlServer` 10.0.10 ho vyžaduje jako 6.1.1 (`Tests/packages.lock.json`, rozh. 186) — a `System.Text.Json` ze sdíleného rámce (`Microsoft.NETCore.App` 10.0.14 ho nese). Soudce však překládá s `GeneratedQueryCompiler.EFCoreConsumerReferences`, kde je sestavení poskytovatele, ne jeho závislosti: dvanáct pokusů `not compiled` (`ldbc:IC13`, `marketplace-long-query`, `clinic-application`, Keycloak), které by v projektu s tím poskytovatelem prošly. Totéž u Dapperu: tvar žádá „prosté veřejné třídy" a slibuje „projekt, který odkazuje Dapper"; model dvakrát napsal `[Table]`/`[Column]` z `System.ComponentModel.DataAnnotations.Schema` — ve sdíleném rámci, takže v projektu s Dapperem se přeloží a Dapper je nečte —, a soudce, jehož sada pro Dapper sdílený rámec nenese, to má za `not compiled`. Porušení tvaru to je; nefunkční artefakt ne.
3. **Atribut `assembly` v mapování NHibernatu.** Rozh. 028 vyslovilo, že jméno sestavení „je příspěvek konzumentského projektu stejně jako soubor projektu nebo připojovací řetězec", builder ho nepíše a harness ho dodává jako konzument. Dodává ho ale jen tam, kde chybí (`QualifyAssembly`: `if (Attribute("assembly") is null)`); model ho píše skoro vždy (`assembly="Shop"`) a jmenuje tak sestavení, které v žádném konzumentovi neexistuje — *Could not compile the mapping document*. Je to jediná největší příčina pod nejvyšším stupněm u cílů NHibernate ve všech čtyřech záznamech: deset z dvanácti pokusů základní sady, pět ze šesti u `BestRowPerGroup`, dva u `fiben:q200`, část Keycloaku. Prompt přitom o atributu neříká nic; tvar NHibernate jmenuje jen `<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" …>`.
4. **Parametr typu entity.** `ForeignArtifactRun.Sample` a javová `PreparedJavaQuery.sample` dávají hodnotu skaláru, kolekce, okamžiku a identifikátoru; metoda, jejímž parametrem je entita, končí *No sample value for a parameter of type …* — šest pokusů JPetStore do Hibernatu, jeden pokus Cofoundry. Nástroj tu mez nepotká, protože buňku odmítá dřív (bez klíče); potkává ji jen cizí artefakt, a nikde není napsána.

K tomu dvě menší věci téhož běhu: sadu buňky (základní, velká, záludná) držel soubor `sets.tsv` vedle `cells.tsv` a tabulky po sadách se počítaly ručně; a otázka Míry z 2026-11-07, **proč model nemůže dostat schéma jako text** — popis databáze, jaký si nástroj bere z katalogu.

**Odpověď na tu otázku je, že může a protokol s tím počítá, jen to odložil.** Rozh. 175 dává „schéma databáze měřeného klíče jako DDL" variantě `rag` spolu s dokumentací a příklady a ablaci `rag-no-schema` k měření jeho vlivu (T6). Varianty `zero-shot` a `few-shot` dostávají přesně to, co dostává parser nástroje — složku výpisu —, a katalog (F4) zůstal tím, co má nástroj navíc. To je legitimní základna (nástroj *je* deterministický překladač s katalogem a model bez něj je to, s čím se srovnává), ale běh ukázal, že nad případovými studiemi tím nic neměříme, a `rag` nestojí, protože potřebuje korpus dokumentace a způsob hledání. Složka schématu z `rag` nepotřebuje nic z toho: skripty existují (`Tests/Database/TestSchema.sql`, `SampleData/ThirdParty/<projekt>/schema.sql`, `database/ldbc/schema.sql`, `database/fiben/schema.sql`) a nástroj z nich sestavuje právě ty katalogy, které čte.

### Co dělají srovnatelné experimenty

Ověřili jsme obě otázky — jak se modelu dává schéma a jak se mu říká kontrakt soudce — proti publikovaným experimentům (hledání 2026-11-07; čísla jsou jejich, ne naše).

**Schéma.** V text-to-SQL, kde je schéma vstupem úlohy, ho každý benchmark modelu dává jako text a ablace ukazují, který tvar funguje: Rajkumar, Li a Bahdanau 2022 (*Evaluating the Text-to-SQL Capabilities of Large Language Models*, arXiv 2204.00498) nad Spiderem s Codexem — bez schématu 8,3 % EX, `Table(col, col)` 56,8 %, `CREATE TABLE` s typy a cizími klíči 59,9 %, s třemi vzorovými řádky 67,0 %, víc řádků škodí; Chang a Fosler-Lussier 2023 (*How to Prompt LLMs for Text-to-SQL*, arXiv 2305.11853) — `CREATE TABLE` 73,1 % proti 71,9 % výčtu sloupců, řádek cizích klíčů +1,3 až +2,1 bodu, tři odlišné hodnoty na sloupec dalších +2,6; Gao a kol. 2024 (DAIL-SQL, PVLDB 17) — rozdíly mezi tvary bez hodnot v jednotkách bodů a podle modelu, cizí klíče +0,6 až +2,9, dialekt jako věta instrukce; XiYan-SQL 2024 (M-Schema, arXiv 2411.08599) — tvar se jménem, typem, klíčem a příklady hodnot o ~2 body nad holým DDL u každého ze čtyř modelů; DIN-SQL 2023 Spider bez obsahu databáze, BIRD s řádky, protože bez hodnot selže vazba na schéma. Shrnuto: **`CREATE TABLE` s klíči a cizími klíči je nejlepší tvar bez hodnot, hodnoty přidávají jednotky bodů, dialekt se říká větou.** Jméno schématu (`dbo`, vlastní schéma) žádný z nich neřeší — Spider i BIRD jsou SQLite bez schémat; Spider 2.0 (ICLR 2025) jmenuje `project.dataset` BigQuery a `DATABASE.SCHEMA` Snowflake jako součást prostředí. Práce o generování kódu ORM je jedna, Dialect-SQL (Shi a kol., EMNLP 2025): model píše SQLAlchemy a schéma dostává jako třídy ORM se sloupci a cizími klíči; práce, která by srovnávala deterministický nástroj čtoucí katalog s modelem, jemuž se katalog dal jako text, nalezena nebyla — všechny benchmarky modelu serializované schéma dávají a nikdo to neargumentuje, je to tichá konvence.

**Kontrakt soudce.** Benchmarky generování a překladu kódu kontrakt **vyslovují v promptu a jeho porušení počítají modelu**: HumanEval dává hlavičku, signaturu a docstring a ořezává výstup na stop-sekvencích; MBPP dává jméno funkce třemi `assert`; MultiPL-E obaluje Javu a C# třídou a jmenuje konvence jazyka; TransCoder-test má pevné `f_filled`; AVATAR počítá chybějící třídu jako selhání modelu; SWE-bench říká „odpověz jedním patchem v tomto tvaru". Normalizace v post-processingu je vzácná a kde je, **uvádí se s ní i bez ní**: SWE-bench opravuje hlavičky patchů (25–69 % aplikovaných to potřebovalo), CodeTransBenchmark (Kowalczuk a kol. 2026, arXiv 2609.20257) přejmenovává hlavní třídu Javy podle souboru a uvádí 23,7 % → 56,3 % u Codestralu, +7,3 bodu v průměru. Chyby, které způsobí prostředí soudce, jsou známý zdroj falešných selhání: TransCoder-test-X (ExeCoder 2025) přepsal šablony, protože správné překlady padaly na tvaru parametru; Rabbi, Saha a Yang 2026 (arXiv 2605.02195) definují *false failure* jako selhání, které opraví jen změna konfigurace soudce (chybějící `-lm`, `-lgmp`), a doporučují je hlásit zvlášť; MigrationBench přiznává falešná selhání z throttlingu Maven Central. Benchmarky na úrovni repozitáře (SWE-bench, CoderEval, EvoCodeBench) instalují **celou sadu závislostí projektu** před soudem a prostředí ověří zlatým řešením, takže selhání při soudu jde na vrub modelu; modelu přitom neříkají, které balíčky jsou k dispozici. Pan a kol. 2024 (*Lost in Translation*, ICSE) importy a signaturu z promptu záměrně vynechali a mají 77,8 % neúspěchů jako chyby překladu kompilátorem — bez rozlišení, co z toho je harness. Podspecifikovaný prompt je uznaný confound: SWE-bench Verified nechal 93 vývojářů ohodnotit 1 699 instancí a nechal 500; *Semantic collapse* (arXiv 2607.01953) má přes 10 % MBPP a 32 % LiveCodeBench podspecifikovaných.

**Co z toho pro nás plyne.** Náš protokol je v pořádku tam, kde kontrakt vyslovuje (tvar odpovědi, role, volání, dialekt), a mimo konvenci tam, kde soudce zná něco, co prompt neříká: schéma tabulek, závislosti slíbeného projektu, jméno sestavení. Rozh. 028 a 204 už dávají odpověď, kterou stačí dotáhnout: co je příspěvkem konzumenta, dodává konzument — a soudce je tu konzument.

## Zvažované varianty

### A — schéma

**A1 — nechat; studie zůstanou pro model na třetím stupni a souhrn to řekne.** Nejméně práce. Zamítáme: sedm buněk velké sady by trvale měřilo něco jiného než ostatní, a není důvod — schéma je jméno z konfigurace soudce, ne znalost, kterou by model měl mít.

**A2 — do každé varianty vložit DDL (model dostane katalog jako text vždy).** Nejblíž otázce Míry a konvenci text-to-SQL. Zamítáme jako jedinou změnu: vzalo by to T6 ablaci „vliv databázových metadat" (bez varianty bez metadat se nedá změřit) a přetrhlo srovnatelnost `zero-shot` a `few-shot` se čtyřmi záznamy; F4 je vlastnost nástroje, kterou chceme vidět i proti modelu bez ní.

**A3 — oddělit prostředí od metadat: jméno schématu říká prompt každé varianty, DDL dostává varianta `schema`.** Volíme. Jméno schématu je prostředí téhož druhu jako dialekt (pravidlo 4 šablony ho už říká) — soudce ho zná z konfigurace, nástroj ho dostává od harnessu, ne z katalogu, a Spider 2.0 ho dává jako součást prostředí. DDL je metadata: sloupce, typy, klíče a vztahy, které nástroj čte katalogem (F4, F6). Varianta `schema` je složka `rag` z rozh. 175 vyňatá a postavená teď, protože nepotřebuje korpus ani hledání; `zero-shot` proti `schema` je měření T6 dostupné před `rag`, a `rag-no-schema` zůstává, jak je.

### B — co model dostane jako DDL

**B1 — skripty, jak jsou (`CREATE TABLE` s klíči a cizími klíči, bez řádků).** Volíme. Je to přesně to, co katalog nástroji dává — tabulky, sloupce, typy, klíče, cizí klíče, nic víc —, takže srovnání je o témž; a je to podle literatury nejlepší tvar bez hodnot. Vzorové řádky (Rajkumar +7, Chang +2,6) jsou obsah, který nástroj nečte, a tabulky případových studií jsou prázdné; patří k `rag` jako „relevantní příklady" T5, ne sem. FIBEN má 152 tabulek — pro silný model to kontext unese, pro pilotní ne, a záznam to řekne.

**B2 — výtah: jen tabulky, které vstup jmenuje.** Zamítáme: to je vazba na schéma (*schema linking*), kterou by za model dělal protokol, a nástroj ji nedostává — čte celé schéma, které katalog zpřístupňuje.

### C — reference soudce

**C1 — prompt vysloví, co kompilace odkazuje (výčet sestavení).** Zamítáme: prompt by jmenoval sestavení, která žádný vývojář nevyjmenovává, a každá změna balíčku by měnila šablonu; a slib „projekt s poskytovatelem SQL Serveru" je správný popis konzumenta.

**C2 — soudce překládá s referencemi projektu, který prompt slibuje: sdílený rámec připnutého cílového rámce a připnutý balíček cíle s jeho tranzitivním uzávěrem.** Volíme. Pravidlo má jednu definici a dva zdroje pravdy, které rozh. 034 a 186 už drží: `Directory.Build.props` (rámec, tedy obsah `Microsoft.NETCore.App`) a `packages.lock.json` (uzávěr). Pro artefakt nástroje se nic nemění — co se přeložilo s méně referencemi, přeloží se i s více —, takže sada referencí je jedna na cíl, ne zvlášť „cizí". Javová sada už tak soudí: classpath Mavenu je uzávěr `pom.xml`.

### D — atribut `assembly`

**D1 — nechat jako obsah modelu.** Tak to čtou záznamy. Zamítáme: rozh. 028 říká opak — jméno sestavení je příspěvek konzumenta —, a konvence, kterou prompt nevyslovuje a soudce zná, je podspecifikace promptu, ne chyba překladu. Nechat to by znamenalo měřit u cílů NHibernate hlavně znalost našeho harnessu.

**D2 — vyslovit v promptu a porušení počítat (konvence HumanEval, AVATAR, Turbulence).** Správný první krok a bereme ho. Sám nestačí: model, který atribut napíše přesto, dál padá na něčem, co v žádném konzumentu nemůže být správně, protože jméno sestavení určuje konzument až při sestavení.

**D3 — vyslovit v promptu a soudce jméno dosazuje vždy, s poznámkou v řádku.** Volíme. Je to totéž, co harness dělá dnes pro chybějící atribut, rozšířené na přítomný; konzument dosazuje své jméno sestavení, nikdy cizí. Normalizaci, jak radí CodeTransBenchmark a SWE-bench, uvádíme oběma způsoby: řádek nese, že atribut byl přepsán, a záznam počet takových pokusů, takže čtenář vidí podíl „jak přišlo" i „jak konzument sestavil". Nejde o opravu obálky rozh. 204 (ta je importu a `zero-shot` ji nemá): je to akt soudce jako konzumenta, který se uplatňuje na každý artefakt včetně nástroje, a rozh. 204 se nemění.

### E — parametr typu entity

**E1 — vzorová instance entity s klíčovými členy nastavenými na vzor jejich typu.** Nejúplnější. Odkládáme: jak Hibernate 7 a NHibernate vážou přechodnou instanci jako parametr dotazu (zda vezmou identifikátor, nebo odmítnou), je fakt frameworku, který je třeba změřit před rozhodnutím, a buňka, která ho potřebuje, je buňka, kterou nástroj odmítá — mez potkává jen cizí artefakt.

**E2 — vyslovená mez cesty.** Volíme: dotaz, jehož parametr je typu entity artefaktu, se neváže a neběží; projekt zůstává na nejvyšším stupni, kterého došel bez něj, a řádek jmenuje dotaz a parametr jako důvod. Mez stojí v `subset.md` a v §6.2. E1 zůstává možným pozdějším rozhodnutím s měřením.

### F — sada buňky

**F1 — `sets.tsv` vedle `cells.tsv`.** Dnešní stav; souhrn o sadě neví a tabulky po sadách se počítají ručně. **F2 — sloupec `set` v `cells.tsv`, nesený do `metadata.set` artefaktu a do souhrnu.** Volíme: varianta je hodnota manifestu (rozh. 175) a sada je téhož druhu — řekne, *proč* je buňka ve vzorku; souhrn seskupuje i po ní, kde je, a řádky bez ní seskupuje jako dosud.

## Rozhodnutí

**Volíme A3, B1, C2, D3, E2 a F2.**

### Prostředí v promptu každé varianty

Nové šablony `web-zero-shot-4` a `web-few-shot-3` (text `web-zero-shot-3` a `web-few-shot-2`; šablona, kterou záznam použil, se nemění, rozh. 189) říkají navíc:

| Kde | Co |
|---|---|
| pravidlo 4 | tabulky vstupu leží ve schématu `{{schema}}` té databáze; `{{schema}}` se píše jako schéma **každé** tabulky — v anotaci tabulky, v atributu mapování, v textu SQL —, ať ho vstup píše, nebo ne; před překladem se nahradí skutečným schématem (dosavadní pravidlo 5 se do něj slučuje) |
| tvar NHibernate | mapovací dokument nenese atribut `assembly`; jméno sestavení dodává sestavení volajícího a jméno zapsané v dokumentu jím nahradí |
| pravidlo 7 | každý soubor nese direktivy `using` či `import`, které potřebuje; volající dodává jen implicitní direktivy, které tvar odpovědi jmenuje, a nic víc |

Nic jiného se v textu nemění, aby `zero-shot` a `few-shot` zůstaly srovnatelné se záznamy nad `web-zero-shot-3`: u klíčů matice, kde vstup `{{schema}}` nese, pravidlo říká totéž co dřív.

### Varianta `schema` a soubor `schema.sql` výpisu

**Výpis vstupů** (`ForeignInputs.Export`) píše vedle `input.json` soubor `schema.sql`: DDL katalogu, který nástroj pro ten klíč čte (`ExperimentRun.CatalogFor`), se schématem zpět jako `{{schema}}` — tabulky fixtury (`Tests/Database/TestSchema.sql`, `QueryShapes/FixtureData.sql` a `Differential/FixtureData.sql` bez dávek, které zapisují řádky) pro `matrix:`, `cross:` a `series:`, `database/ldbc/schema.sql` ve schématu LDBC fixtury pro `ldbc:`, `database/fiben/schema.sql` pro `fiben:`, `SampleData/ThirdParty/<projekt>/schema.sql` pro `example:` výňatku; klíč, pro který nástroj katalog nečte (ostatní `example:`, rozh. 099), soubor nemá. Nástroj ho nečte — čte katalog —; `input.json` ho jmenuje v poli `schema`, aby bylo vidět, co výpis nese.

**Varianta `schema`** je `zero-shot` s oddílem *The database* mezi voláním a vstupem: „The tables of the input are these, in the schema `{{schema}}`:" a obsah `schema.sql` jako jeden blok `sql`. Šablona `web-schema-1`; buňku klíče bez `schema.sql` odmítne příprava jménem, jako variantu bez šablony. Pokus varianty `schema` se soudí, jak přišel, jako `zero-shot` (rozh. 204 opravuje obálku jen u ostatních variant): obě varianty se liší v DDL a v ničem jiném, aby rozdíl mezi nimi byl vliv metadat. Varianta má v rozh. 175 místo mezi `zero-shot` a `rag`: `rag` = `schema` + dokumentace + příklady, `rag-no-schema` = `rag` bez tohoto souboru; `few-shot` DDL nedostává. Měření T6 „vliv databázových metadat" je `zero-shot` proti `schema` nad týmiž buňkami, po pokusech i best-of-n.

### Soudce dosazuje schéma klíče

Cesta cizího artefaktu nahrazuje `{{schema}}` schématem katalogu, který nástroj pro klíč čte: schéma fixtury pro `matrix:`, `cross:` a `ldbc:` (u LDBC s jeho příponou, jak výpis píše), schéma projektu v databázi případových studií pro `example:` výňatku, `dbo` databáze `Fiben` u `fiben:`, jejíž vstupy schéma nepíší a jejíž tabulky tam leží. V obou sadách (`ForeignArtifactRunTest`, javový `ForeignArtifactRunTest`), jedním místem, které schéma z klíče odvodí.

### Reference soudce

Sada referencí cíle .NET je **sdílený rámec připnutého cílového rámce** (`Microsoft.NETCore.App` verze z `Directory.Build.props`) **a připnutý balíček cíle s tranzitivním uzávěrem** podle `packages.lock.json` (rozh. 186). Proti dnešku to přidává `System.ComponentModel.Annotations`, `System.Text.Json` a ostatní sestavení sdíleného rámce všem třem cílům a `Microsoft.Data.SqlClient` 6.1.1 cíli EF Core (uzávěr `Microsoft.EntityFrameworkCore.SqlServer` 10.0.10); u Dapperu a NHibernatu uzávěr `SqlClient` nenese, protože ho jejich balíčky nevyžadují a prompt slibuje jen je. Jedna sada na cíl pro artefakt nástroje i cizí; test drží, že sada obsahuje každé sestavení uzávěru z lock souboru. Javová sada se nemění.

### Jméno sestavení

`QualifyAssembly` obou přijetí NHibernatu nastavuje `assembly` kořene na jméno přeloženého sestavení **vždy**: chybějící doplní, přítomný přepíše. Řádek cizího artefaktu nese `assemblyOverwritten` (ano/ne), souhrn uvádí podíl přepsaných mezi pokusy cílů NHibernate a záznam běhu jejich počet, takže výsledek stojí „jak přišel" i „jak konzument sestavil". Rozh. 028 platí beze změny; rozh. 204 také — není to oprava obálky importem, `zero-shot` se dál soudí, jak přišel, a soudce jako konzument dosazuje své jméno u každého artefaktu včetně nástroje.

### Parametr typu entity

Dotaz, jehož parametr je typu entity artefaktu (třída, kterou artefakt deklaruje, nebo kolekce takových), se na čtvrtém stupni projektu a při javovém přijetí **neváže a neběží**; ostatní dotazy projektu ano. Projekt zůstává na stupni, kterého došel bez něj, řádek jmenuje dotaz a parametr, nikdy `failed`. Mez je zapsána v `subset.md` jako vyslovená mez cesty a v §6.2.

### Sada buňky

`cells.tsv` má volitelný sloupec `set`; příprava ho nese do `attempts.tsv`, import do `metadata.set` artefaktu, souhrn seskupuje po producentovi, modelu, kanálu, variantě a — kde je — sadě. `sets.tsv` zaniká.

### Co se netvrdí

Rozhodnutí neruší žádný záznam a nepřepočítává ho: záznamy jsou neměnné a čísla nad `web-zero-shot-3` zůstávají čísly nad ní. Další běh nad vzorkem 2026-11-06 jde celý nad novými šablonami — pokusy jednoho záznamu jsou z jedné šablony —, s variantami `zero-shot`, `few-shot` a `schema`; kdy a nad kolika buňkami, říká Míra jménem běhu, ne toto rozhodnutí. T4–T6 zůstávají vyňaté (§9, oblast 6); varianta `schema` nedokazuje nic o žádném modelu. Záludné vstupy mapovacích faktů, které výpis neumí, zůstávají otevřenou položkou.

## Důsledky

- **Kód, prompt:** `Tests/Foreign/Prompts/web-zero-shot-4.md`, `web-few-shot-3.md`, `web-schema-1.md`; `WebTemplate.ForVariant` na ně; `WebPrompt.Render` plní oddíl *The database* z `schema.sql`.
- **Kód, výpis:** `ForeignInputs.Export` píše `schema.sql` a pole `schema` v `input.json`; `ExperimentInput` a javový `ExperimentInput` soubor přeskočí.
- **Kód, soudce:** schéma z klíče v `ForeignArtifactRunTest` obou sad; `GeneratedQueryCompiler` a `GeneratedEntityCompiler` se sadami podle pravidla a testem proti `packages.lock.json`; `QualifyAssembly` v `NHibernateAcceptance` a `NHibernateQueryAcceptance` přepisuje a hlásí; `ForeignVerdict` nese `assemblyOverwritten` (sloupce obou sad drží `TheRowsHaveTheColumnsTheJavaSuiteWrites`); `ForeignArtifactRun.RunProject` a javová `runProject`/přijetí vynechají dotaz s parametrem typu entity s důvodem.
- **Kód, sada buňky:** `WebCell.Read` sloupec `set`, `WebAttempts.Prepare`/`Import` ho nesou, `ExperimentSummary` seskupuje.
- **`ORMConvertor/README.md`**, *Foreign artifacts* a *A language model in a web frontend*: `schema.sql` výpisu, varianta `schema`, sloupec `set`, co soudce dosazuje (schéma klíče, jméno sestavení) a mez parametru typu entity.
- **[`architecture.md`](../architecture.md) §6.2:** výpis se `schema.sql`, varianta `schema`, pravidlo referencí, dosazení schématu a jména sestavení, mez parametru.
- **[`subset.md`](../subset.md):** mez parametru typu entity na cestě cizího artefaktu.
- **[`runs/README.md`](../runs/README.md):** záznam srovnání uvádí šablony, počet pokusů s přepsaným `assembly` a u varianty `schema` velikost DDL v tokenech.
- **[`open-items.md`](../open-items.md):** tři body rozhodnutí a sloupec sady v položce *Srovnání s jazykovým modelem* se mění v práci podle tohoto rozhodnutí; položka záludných vstupů mapovacích faktů zůstává.
- **Text práce:** kapitola srovnání rozlišuje tři druhy nálezů běhu 2026-11-06 — obsah modelu, prostředí, které prompt neříkal, a fakta frameworku, která nástroj měří (rozh. 201) — a u atributu `assembly` uvádí obě čísla.

## Historie

- **2026-11-07, revidováno** (téhož dne, před implementací): na otázku Míry, zda chybějící `using` není také vadou promptu, prošly záznamy běhu — z 258 pokusů padly na chybějící direktivě dva (`SHA512` bez `System.Security.Cryptography`), všechny ostatní `not compiled` jsou reference soudce —; prompt direktivy žádal jen v tvarech odpovědi, a tak dostává pravidlo 7. Doplněno, že varianta `schema` se soudí, jak přišla, aby se od `zero-shot` lišila jen DDL; že DDL fixtury pro klíče matice je sjednocení tří skriptů fixtury bez dávek s daty, protože tabulky domény matic nejsou v `TestSchema.sql`; a že soudce u `fiben:` dosazuje `dbo`, protože nové pravidlo 4 nechá model psát `{{schema}}` i tam.
