# Audit stavu po vydání 1.1.0, 2026-08-23

Revize stavu repozitáře a pracovní kopie bezprostředně po vydání verze 1.1.0. Předchozí audit z [2026-08-21](2026-08-21-version-1-0-readiness-audit.md) se ptal, jestli je verze 1.0 připravená; tenhle se ptá obráceně a šířeji: **co v repozitáři není doložené, co v něm není popsané a co v něm nemá důvod být.** Není vázaný na jedno vydání ani na jeden dokument — prošli jsme kód, `docs/`, oba `README.md`, konfiguraci prostředí, historii gitu i pracovní kopii, a vedle toho jsme stav porovnali s vnější praxí pro software téhle třídy.

Nálezy jsou číslované po kapitolách. Co z nich plyne, je v kapitole 10.

## Zafixované verze a stav, ke kterému audit platí

Pracovní kopie a `origin/main` ke dni 2026-08-23, commit **`f1f570f` „Version 1.1.0"**, na kterém sedí anotovaná značka `1.1.0`. Strom je čistý, `git rev-list --left-right --count origin/main...HEAD` vrací `0 0`. Na rozdíl od auditu z 2026-08-21 vznikl tenhle v prostředí se shellem, takže **stav gitu ověřený je**.

Ověřovali jsme čtením zdrojů a příkazy nad repozitářem. **Neběžel žádný build, žádné testy a žádný kontejner** — čísla o pokrytí, doby běhu a výsledky sad přebíráme z dokumentace a označujeme je jako nedoložená (kapitola 9).

Verze jsou centrálně v `ORMConvertor/Directory.Packages.props`, cílová platforma v `Directory.Build.props`, pásmo SDK v `global.json` (rozhodnutí [034](../decisions/034-central-version-management.md)). Audit je nesnímkuje.

---

## 1. Co verze 1.1.0 tvrdí a co je z toho doložené

Vydání proběhlo podle rozhodnutí [041](../decisions/041-versioning-and-release.md) čistě: strom byl čistý, značka je **anotovaná**, sedí na commitu, který posunul `<Version>`, a anotace nese poznámky přesně ve třech předepsaných odstavcích a v předepsaném pořadí. Krok 2 se od předpisu odchýlil jen drobně (nález 4.4). Problém je jinde — v tom, čím je hlavní nárok vydání podložený.

### 1.1 Doklad pro S5 předchází testům, které vydání inzeruje — kritické

Vstup oblasti *přenositelné a reprodukovatelné prostředí* do nároku je **jediný důvod**, proč je 1.1.0 krokem MINOR; říká to anotace značky, `README.md` i `architecture.md` §9. Doklad pro ten nárok je jediný: záznam v [`architecture.md`](../architecture.md) §6.4 „**Ověřeno spuštěním 2026-08-22** … skončil výsledkem **392 prošlých, 0 selhaných, 0 přeskočených**".

Ten záznam ale popisuje běh menší sady, než jakou vydání obsahuje. Ověřeno gitem:

- věta o 392 prošlých vstoupila commitem `d2e53d9` (2026-08-22 10:11);
- `Tests/Combined/ConsumerProjectFactsTest.cs` vzniklo commitem `46672ee` (2026-08-23 18:20:24) — tři metody `[Theory]` nad `[MemberData(nameof(Directions))]`, kde `Directions()` je kartézský součin tří frameworků, tedy 3 × 9 = **27 případů**;
- 392 + 27 = **419**, což je přesně počet, který o den později uvádí §6.2.

Značka `1.1.0` vznikla jedenáct minut po `46672ee`. Kontejnerový běh se po přidání těch 27 testů **neopakoval**. Přitom `README.md`, anotace značky i [`traceability.md`](../traceability.md) u S5 shodně tvrdí, že tím příkazem se reprodukuje **celá** testovací sada.

Nález není o tom, že by sada neprošla — je pravděpodobné, že projde. Je o tom, že **jediné tvrzení, na kterém tohle vydání stojí, je doložené během jiné sady, než kterou vydání obsahuje**, a nikde to není řečeno. U požadavku, jehož celý smysl je reprodukovatelnost, je to ta nejcitlivější možná mezera.

### 1.2 Údaj v §6.2 je vnitřně nemožný — drobné

Táž aritmetika ukazuje druhou vadu. [`architecture.md`](../architecture.md) §6.2 píše „**První měření (2026-08-22, Release, celá sada 419 testů)**". Číslo 419 ale zahrnuje 27 testů, které 2026-08-22 neexistovaly — vstoupily až následujícího dne commitem `46672ee`, týmž, který tuhle větu do dokumentu zapsal. Buď je špatně datum, nebo počet.

Čtenář, který §6.2 a §6.4 čte za sebou, tak dostane dvě různá čísla „celé sady" k témuž dni a nemá je z čeho srovnat. §6.4 přitom na svém čísle staví: „nula přeskočených je to podstatné číslo".

### 1.3 Značka `1.0` neporušuje formát, který 041 předepisuje, a nikdo to neříká — drobné

Rozhodnutí 041 předepisuje značky ve tvaru `MAJOR.MINOR.PATCH`. Značka `1.0` je dvoudílná. Rozhodnutí i `README.md` řeší, co značka *obsahuje*, ale ani jedno neříká, že se liší i tvarem — a přitom je to týž případ jako obsah: značka se neposouvá, stav se narovnává dalším číslem. Stačí věta tam, kde se o značce už mluví.

---

## 2. `architecture.md` proti kódu

Dokument je celkově v dobrém stavu — kapitoly o mezireprezentaci, diagnostice, katalogu i nasazení jsme procházeli podrobně a sedí. Nálezy níž jsou místa, kde se rozešel.

### 2.1 §6.1 nese větu o `OpenConnection()`, o které projekt už ví, že je nesprávná — kritické

[`architecture.md`](../architecture.md):342: „Zápis dat by hranici potřeboval a fixture k němu nabízí `OpenConnection()`; **volajícího ale zatím nemá**, protože 4. stupeň ověření nemá jediného zástupce."

Metoda je definovaná v `Tests/Database/TestSchemaFixture.cs:129` a volá se na **sedmi místech** `Tests/Database/TestSchemaFixtureTest.cs` (řádky 19, 38, 68, 87, 103, 136, 170).

Podstatné je, že tohle projekt už jednou opravil. [`audits/README.md`](README.md):13 nález auditu z 2026-08-21 výslovně přeznačuje jako nesprávný — „`TestSchemaFixture.OpenConnection()` volajícího **má** — testy v `TestSchemaFixtureTest`" — a [`open-items.md`](../open-items.md):44 nese opravené znění („volají ji testy v `TestSchemaFixtureTest`"). Opravný průchod `40b69a3` sáhl na `open-items.md` a na `audits/README.md`, a **na `architecture.md` ne**. Nesprávnou větu tak dnes nese jediný dokument, jehož úkolem je popisovat současný stav.

### 2.2 §6.1 tvrdí, že v repozitáři není připojovací řetězec; je tam od rozhodnutí 039 — závažné

Tatáž kapitola, [`architecture.md`](../architecture.md):338: „…čte z nich připojovací řetězec pod jménem `TestDatabase` … **V repozitáři řetězec není (S4)**; pojmenování je stejné jako u `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml`."

`ORMConvertor/docker-compose.yml:88` nese `ConnectionStrings__TestDatabase=Server=test_db,1433;Database=ORMConvertorTests;User ID=sa;Password=Testingorms123;…`, a řádky 21 a 22 totéž pro katalog a Advisor. Věta tedy odkazuje na soubor, který ji vyvrací, v téže větě.

Věcně o mezeru v bezpečnosti nejde a [`threat-model.md`](../threat-model.md) to vyslovuje správně: „Heslo `sa` v `docker-compose.yml` a ve workflow je vývojový údaj zahoditelné instance — S4 zakazuje údaje v generovaných artefaktech a v logu, ne v popisu prostředí." Nesprávná je formulace v `architecture.md`, ne stav. Ale je to formulace opřená o číslo požadavku, takže se čte jako nárok.

### 2.3 Dva koncové body v `architecture.md` chybí úplně — závažné

`grep -c 'required-content' docs/architecture.md` vrací **0**. Koncové body `/required-content` a `/required-content-advisor` v dokumentu nejsou, ačkoli na nich stojí celá vstupní obrazovka frontendu (`wwwroot/js/api.js`), popisuje je tabulka v `ORMConvertor/README.md` i tabulka vstupních bodů v `threat-model.md`, a rozhodnutí [025](../decisions/025-query-language-as-content-type.md) je jmenuje jako nositele jazyka obsahu.

Dokument, který má odpovídat na otázku „jak to teď funguje", tedy o čtvrtině čtených koncových bodů mlčí.

### 2.4 §4.3 vypisuje dvě pole mezireprezentace, která nikdo nečte — závažné

Blok kódu v §4.3 uvádí `Relation` včetně `public string? Name` a `public string? InverseRelationName`. V řešení nemá ani jedno z nich čtenáře: `grep -rn 'InverseRelationName'` vrací jedinou řádku — vlastní deklaraci v `Model/AbstractRepresentation/Relation.cs:19` —, a `Relation.Name` se nečte nikde (nálezy `relation.Name` v `NHibernateXMLMappingParser.cs` jsou `XElement.Name`, ne tohle pole).

Není to samo o sobě vada modelu; mezireprezentace smí nést fakt, který dnešní buildery nevyužijí. Vada je, že to §4.3 nerozlišuje: čtenář dostane pole vedle polí, která nese celá pipeline, bez informace, že tahle dvě zatím nikam nevedou.

### 2.5 Množinové operace: vykreslovací cesta existuje, ale žádný parser ji neumí vyrobit — závažné

[`open-items.md`](../open-items.md):64 píše „množinovou operaci navíc dnes vykresluje jedině Dapper builder", a §7 architektury totéž. Obojí se čte jako živá, byť neúplná cesta. Ve skutečnosti do ní nic nevstupuje:

- `LinqParsing/LinqQueryParser.cs:183-187` odmítá `Union`, `Concat`, `Intersect` i `Except` rovnou přes `ReportUnsupported(step.Name, QueryFeature.SetOperation)`;
- `DapperSqlQueryParser` nemá pro `BinaryQueryExpression` větev, takže T-SQL `UNION` neprojde ani odtud;
- `AbstractQueryBuilder.SetOperation()` má dva volající a **oba jsou testy** (`Tests/Combined/QueryTargetShapeTest.cs:179`, `Tests/Dapper/DapperSqlQueryBuilderTest.cs:126`).

Vykreslovací kód v `AbstractQueryBuilder.cs:138-147`, `DapperSqlQueryBuilder.cs:129` a `DapperSqlQueryVisitor.cs:153-162` je tedy v provozu nedosažitelný. Souvisí s tím i nález 6.2.

### 2.6 §1 mluví o šestnácti balíčcích; je jich sedmnáct — drobné

[`architecture.md`](../architecture.md):53: „soubor jmenuje **všech šestnáct** přímo referencovaných balíčků". `grep -c '<PackageVersion' ORMConvertor/Directory.Packages.props` vrací **17**. Sedmnáctým je `coverlet.collector`, který přibyl commitem `46672ee` — tedy tím, který tentýž odstavec přepisoval kvůli měření pokrytí.

Rozhodnutí [034](../decisions/034-central-version-management.md) uvádí „šestnáct" také, a **to je správně**: je to zmražený snímek stavu ke dni svého vzniku a nesahá se do něj.

### 2.7 Dva datované záznamy o ověření uvádějí verzi nástroje `1.0.0` — drobné

[`architecture.md`](../architecture.md):328 a `ORMConvertor/README.md`:55 popisují ověření z 2026-08-22 a citují „verzi nástroje `1.0.0`". Jako datovaný záznam to nesprávné není. Nepříjemné je to proti §1, která říká, že číslo verze „jinam se neopisuje, aby dvě místa netvrdila každé jiné číslo" — a čtenář vydané 1.1.0 narazí v popisu současného nástroje na `1.0.0` dvakrát.

---

## 3. Hranice záruk je vyslovená na třech místech a rozchází se

Rozhodnutí 041 i `docs/README.md` stanovily patrový vztah: nad `traceability.md` platí `architecture.md` §9, nad §9 anglická sekce *Guarantees* v kořenovém `README.md`. Pravidlo říká, který text vyhrává — neomlouvá to, že se texty liší.

### 3.1 `README.md` tvrdí o značce `1.0` něco, co značka nenese — kritické

Kořenový [`README.md`](../../README.md):54: „The tag `1.0` … does not contain the container configuration described above, **although its own guarantees section already claimed it**." Totéž česky v §9:438: „ačkoli ji **tehdejší README v sekci nároku výslovně uvádělo**".

Ověřeno proti značce. `git show 1.0:README.md` má v sekci *Covered* (ř. 31) kontejnerovou konfiguraci **neuvedenou**, a v sekci *Exempt from guarantees* (ř. 33) ji naopak jmenuje mezi **sedmi** vyňatými oblastmi: „…database dialects other than SQL Server; **containerized deployment and a database in CI**; and the Java ecosystem…". Značka je tedy v tomhle bodě sama se sebou v souladu: README i §9 v ní kontejnerizaci shodně vyjímají.

Odkud vada vzešla, se dá dohledat. Rozhodnutí 041:16 formuluje totéž **přesně**: „Kontejnerovou konfiguraci, kterou README v sekci *Version 1.0 guarantees* výslovně nárokuje…" — a to platilo, jenže o `main`, ne o značce: `git show 46672ee~1:README.md` má nadpis stále „## Version 1.0 guarantees", zatímco tělo *Covered* už kontejnerovou konfiguraci obsahuje a *Exempt* mluví o šesti oblastech. Byl to tedy zastaralý nadpis nad aktualizovaným textem. Při psaní vydání se ta věta přenesla do `README.md` a do §9 jako tvrzení **o značce**, a tam už pravdivá není.

Je to nález o kanonickém textu nároku, proto kritický. Odůvodnění kroku MINOR tím nepadá — ten stojí na tom, co značka obsahuje, ne na tom, co o sobě tvrdila.

### 3.2 `README.md` nenárokuje S1 a S3, které §9 i traceability nárokují — závažné

§9:442 nárokuje mimo jiné „**S1** (modulární rozšiřitelnost)" a „**S3** (výkon překladu, §6)"; [`traceability.md`](../traceability.md) obojí vede jako „nárokované" s konkrétním důkazem (`Combined/TranslationPerformanceTest` — 100 entit a 100 dotazů do 30 s). Sekce *Covered* v `README.md`:31 nezmiňuje ani výkon, ani rozšiřitelnost.

Podle patrového pravidla platí `README.md`, takže **dva nárokované požadavky tiše vypadly** — a to zrovna ty dva, které nesou strukturu řešení a jediné měřené číslo, jaké nástroj o sobě vydává.

### 3.3 Vyňatá oblast 2 je v `README.md` užší než v §9 — závažné

§9:459 vyjímá vedle dědičnosti, komponent a `<join>` také „ostatními prvky mimo plochou třídu (**`<natural-id>`, `<idbag>`, `<array>`**)". `README.md`:40 uvádí jen „inheritance, components, and `<join>`". Kód je na straně §9: `NHibernateWrappers/NHibernateXMLMappingParser.cs:161-162` má whitelist jedenácti prvků a všechno ostatní hlásí jako `Loss`.

Podle patrového pravidla by tedy ty tři prvky spadly **dovnitř** záruky, kterou nástroj neplní. `use-cases.md`:57 kratší tvar převzal.

### 3.4 Tři novější dokumenty jmenují jako autoritu §9, ne `README.md` — drobné

`threat-model.md`:3, `use-cases.md`:5 a `quality-model.md`:3 odkazují na „nárok verze v `architecture.md` §9". Patrové pravidlo z `docs/README.md`:19 je dvoustupňové a nad §9 stojí anglická sekce *Guarantees*. Jediný dokument, který obě patra vyslovuje, je `traceability.md`:5.

---

## 4. Rozhodnutí a jejich rejstřík

Rejstřík sám je v pořádku: 40 souborů proti 40 řádkům, bez rozdílu v datu, stavu i požadavcích, vynechané číslo 038 je vyslovené (`decisions/README.md`:5), jediné `nahrazeno` je oboustranné a všech šest `revidováno` má datovanou `Historie`. Nálezy níž jsou uvnitř textů.

### 4.1 Rozhodnutí 030 počítá zbylé vyňaté oblasti na pět; jsou čtyři plus dvě, tedy šest — drobné

`030`:114 (řádek `Historie` z 2026-08-22): „kritéria hotovosti, pořadí položek ani **zbylých pět** vyňatých oblastí se tenhle řádek nedotýká". Výčet v témže rozhodnutí má **sedm** odrážek a rozhodnutí [039](../decisions/039-container-configuration-of-the-environment.md) odbavilo jednu, takže zbývá **šest** — což je počet, který shodně uvádí §9:456 („šest"), `README.md`:40 („six areas") i anotace značky.

Řádek `Historie` je součástí rozhodnutí, které je jinak zmražené, ale tenhle konkrétní řádek je datovaný dodatek — opravit číslovku v něm je táž kategorie jako oprava překlepu, ne přepis volby.

### 4.2 Rozhodnutí 041 argumentuje devíti koncovými body; je jich osm — drobné

`041`:26: „rozhraní má **devět** koncových bodů, je stabilní a nudné". `ORMConvertorAPI/Endpoints.cs` jich mapuje **osm** (`/required-content`, `/required-content-advisor`, `/convert`, `/samples`, `/samples-advisor`, `/advisor-test`, `/advisor/run`, `/archive`) a tabulka v `ORMConvertor/README.md` i tabulka v `threat-model.md` uvádějí shodně osm.

Číslo stojí v zamítnuté variantě, takže volbu neohrožuje. Ale je to dokument, který definuje, co znamená „REST kontrakt se nezměnil", takže by měl počítat správně — a bude se citovat v textu práce. Patří sem řádek `Historie`, ne přepis.

### 4.3 `architecture.md` deleguje na 041 povinnost, kterou 041 nezná — drobné

§1:55 říká, že strojově čitelným protějškem verze je pole `version` v `CITATION.cff`, „které se posouvá při vydání (rozhodnutí [041](../decisions/041-versioning-and-release.md))". Rozhodnutí 041 ale `CITATION.cff` **nikde nezmiňuje** — jeho čtyřkrokový postup mluví jen o `Directory.Build.props`, značce a anotaci. U vydání 1.1.0 se pole posunulo správně, jenže díky tomu, že vzniklo v témž commitu; u 1.2.0 nebude předpis, který by na ně upozornil.

### 4.4 Commit s verzí nese i nový soubor — drobné

`041` krok 2: „`<Version>` v `Directory.Build.props` se posune na vydávané číslo **vlastním commitem**." `git show f1f570f --stat` ukazuje `Directory.Build.props | 2 +-` **a** nový `CITATION.cff` (+61 řádků). Smyslem těch čtyř kroků je pořadí, a to dodrženo bylo; zapsat to sem stojí za to jen proto, že je to první vydání podle vlastního předpisu.

---

## 5. Zmražené dokumenty přepsané po datu

`docs/README.md`:28 říká „**Zmražené dokumenty se nepřepisují vůbec**" a `audits/README.md`:3 totéž o auditech („zpětně se nepřepisuje ani neaktualizuje"). Obojí se v minulosti porušilo a nikde to není zaznamenané.

### 5.1 Oba starší audity byly po svém datu editované — závažné

Commit `a975a41` (2026-08-18) sáhl na `2026-08-02-post-step-4-audit.md` i na `2026-08-15-documentation-coherence-audit.md`. U prvního šlo o přeznačení požadavků E→T. U druhého jde jeden z hunků nad rámec přeznačení: **přepisuje citaci** a **přepisuje řádek v kapitole *Opravy***, ze kterého mizí dva předepsané cíle (`requirements.md` „v repozitáři i v souborech projektu" a `CLAUDE.md`).

`audits/README.md`:15 přitom o auditu z 2026-08-02 tvrdí, že „se kvůli nim nepřepisuje" — v době, kdy už přepsaný byl.

Model je jinak v pořádku a projekt ho umí použít správně: nález o `OpenConnection()` u auditu z 2026-08-21 je zaznamenaný v indexu, ne v souboru. Chybí totéž u těchhle dvou.

### 5.2 `requirements.md` a `baseline.md` byly po zmrazení editované — drobné

`requirements.md` commitem `1b9af82` (2026-08-16), `baseline.md` commitem `3326336` (2026-08-15), v obou případech přeznačení E1–E7 na T1–T7. Výjimka je odůvodněná — ale jedině uvnitř auditu z 2026-08-15, tedy na místě, ze kterého se podle `docs/README.md`:21 takové věci číst nemají.

---

## 6. Mrtvý kód a deklarace bez čtenáře

Řešení je na tuhle kategorii nezvykle čisté: v celém kódu jsou **dva** komentáře `TODO` a oba přesně odpovídají mezerám sepsaným v `open-items.md`; žádný `FIXME`, `HACK`, `XXX` ani `NotImplementedException`. Frontend je propojený beze zbytku a po Angularu (rozhodnutí [032](../decisions/032-frontend-as-static-pages-without-a-build.md)) nezůstal jediný soubor. Níž je, co zbylo.

### 6.1 `Advisor/ilp.c` končí devětačtyřiceti řádky zakomentovaného `main()` — drobné

Řádky 195–245 ze 245 jsou zakomentovaný samostatný ovladač s napevno zadanými čísly prototypu (`int mem[] = {989, 3048, 819, …}`, `double cost[] = {30.0, 21.0, …}`) a výpisem `"Query %d → Framework %d\n"`. Soubor se překládá výhradně jako sdílená knihovna (`ORMConvertorAPI/Dockerfile`, stupeň `advisor-native`), takže se ten blok nemůže přeložit ani omylem. Je to největší jednotlivý pozůstatek prototypu v repozitáři a `docs/` o něm nemluví.

### 6.2 Tři deklarace dotazového modelu nemají výrobce — drobné

- `ComparisonOperator.In` nikdo nevyrábí (žádná větev `InPredicate` v `DapperSqlQueryParser`, žádné mapování `Contains` v `LinqParsing`), takže jeho tři konzumenti jsou nedosažitelní — včetně záznamu `Loss` v `EFCoreLinqQueryVisitor.cs:100-104`, který se nikdy nevypíše.
- `SetOperationType.ExceptAll` má v celém řešení jediný výskyt, vlastní deklaraci. Kdyby ho někdo vyrobil, `EFCoreLinqQueryVisitor.cs:69` má větev `_ => "Except"`, takže by se `EXCEPT ALL` **tiše vykreslilo jako `EXCEPT`** — sémantická změna provedená místo ohlášená, což je proti rozhodnutí [004](../decisions/004-unexpressible-facts-as-warnings.md).
- `GeneratorParameter.InitialValue` a `CounterKeyValue` mají rovněž jen vlastní deklaraci. U druhého je to v pořádku — rozhodnutí [020](../decisions/020-canonical-generator-parameter-vocabulary.md) ho vědomě zavádí kvůli javové straně. U `InitialValue` takové odůvodnění není a §4 je vypisuje bez rozlišení.

Souvisejícím případem je `Visit(SetOperationInstruction)` v `NHibernateHqlQueryVisitor.cs:64-71` a `EFCoreLinqQueryVisitor.cs:64-70`: nesou vlastní text záznamu, ale protože ani jeden builder nepřepisuje `BuildSetOperation`, uplatní se obecný záznam ze základní třídy a jejich text se nikdy neukáže.

### 6.3 `Microsoft.AspNetCore.OpenApi` je referencovaný a zafixovaný, ale nepoužitý — drobné

`ORMConvertorAPI/ORMConvertorAPI.csproj:10` ho referencuje, `Directory.Packages.props` fixuje na 10.0.10, a v řešení není jediné volání `AddOpenApi()`, `MapOpenApi()` ani `WithOpenApi()` — dokumentaci obsluhuje Swashbuckle. Původ je dohledatelný: commit `5c4b39a` se jmenuje „Migration from .NET 8 to .NET 10 and **removal of obsolete WithOpenApi()**" a referenci po sobě nechal.

Je to zafixovaná závislost, kvůli které může týdenní úloha `dependencies` shodit build, aniž by za ní stál jediný řádek kódu. Odstranění navíc srovná počet z nálezu 2.6 zpět na šestnáct.

### 6.4 `Tests/xunit.runner.json` nekonfiguruje nic — drobné

Obsahuje jediný klíč `$schema` a je bajtově shodný se sedmi kopiemi pod `benchmarks/*Features/` — placeholder z doby prototypu. Přitom je zapojený: `Tests.csproj:11` ho kopíruje do výstupu. Buď zmizí i s tím řádkem, nebo dostane nastavení, kvůli kterému takový soubor vedle databázové fixture obvykle vzniká (`parallelizeTestCollections`).

### 6.5 Drobnosti bez dopadu na chování — drobné

`PropertyMap.OtherDatabaseProperties` se jen zapisuje a nikdy nečte; `AdvisorRunCoordinator.KnownFrameworks` nic nedělá; jeden `try/catch` obepíná celočíselnou aritmetiku, která nemůže vyhodit; zakomentovaný řádek logování volá metodu `Truncate`, která už neexistuje; `ComparisonOperator.cs:9` má useknutý komentář `// <`; `EFCoreEntityParser.cs:20-22` má explicitní konstruktor tam, kde oba sourozenci používají primární.

---

## 7. Repozitář a pracovní kopie

### 7.1 `benchmarks/results/` je 92 % velikosti repozitáře a nikdo ho nečte — závažné

235 souborů (209 PNG, 9 CSV, 8 HTML, 8 MD, 1 R) = **34,13 MB** ze 37,16 MB všech verzovaných bajtů; největší jednotlivý soubor má 5,17 MB. `git log -1 -- benchmarks/results` vrací **2025-03-06**, tedy rok před převzetím; obsah se ve forku nikdy nezměnil. Hledání odkazů z `ORMConvertor/` nevrací nic. Uvnitř jsou navíc dvě dvojice bajtově shodných PNG.

Není to zapomenutý adresář — `benchmarks/` je jako celek popsaný v obou README, má vlastní položku v `open-items.md` a rozhodnutí 034 vysvětluje, proč stojí mimo centrální správu verzí. Nepopsané je, že drží devět desetin repozitáře na výsledky jednoho běhu z března 2025, které se z verzovaného `.R` skriptu dají vykreslit znovu.

Dvě věci k tomu patří rovnou: je to volba, ne úklid, takže patří rozhodnutí; a odstranění z HEAD **velikost klonu nezmenší**, protože blob zůstává v historii, a přepis historie se dvěma vydanými značkami nestojí za to.

### 7.2 `LICENSE` uvádí jediného držitele autorských práv k napůl zděděnému repozitáři — závažné

`LICENSE` nese „Copyright (c) 2026 Miroslav Štochel" a `README.md` k tomu říká „The licence covers the contents of this repository". Přitom `git log -- LICENSE` má jediný commit (`40b69a3`, 2026-08-21, tedy z forku) a v celé historii žádná licence dřív neexistovala. Podíl na kódu podle `git shortlog -sne --all`: Milan Abrahám 186 commitů, Míra 113.

Připsání zásluh v pořádku je — má ho sekce *Origin and attribution* i `CITATION.cff`. Chybí právě a jen řádek autorských práv. U artefaktu, který má být citovatelný a znovu použitelný, je to ta jediná věc, kterou si čtenář ověří.

### 7.3 `.gitignore` má 484 řádků, fungují čtyři — a jeden komentář zve k pasti — drobné

Ze 260 nekomentářových pravidel odpovídá něčemu v repozitáři jen `*.user`, `[Bb]in/`, `[Oo]bj/` a `.vs/`. Zbytek je stock šablona `dotnet new gitignore`: VS6, Silverlight, TFS 2012, Paket, FAKE, Chutzpah, NCrunch, BizTalk, LightSwitch, Xamarin, macOS AFP, Vim swap.

Ostrý je řádek 40–41: `# Uncomment if you have tasks that create the project's static files in wwwroot` následovaný `#wwwroot/`. Od rozhodnutí 032 drží `wwwroot` **zdrojové soubory frontendu**. Kdo tu pozvánku přijme, odverzuje celý frontend. Nahrazení desetiřádkovým souborem je bezpečné — žádný verzovaný soubor dnes není zastíněný ignorovacím pravidlem.

### 7.4 Konfigurace konců řádků a stylu nepokrývá, co tvrdí — drobné

`git ls-files --eol` hlásí **133 souborů** uložených jako `i/lf` a v pracovní kopii ponechaných `w/lf`, ačkoli `.gitattributes` i `.editorconfig` žádají CRLF: celý `wwwroot` (HTML, JS, CSS), celý `Tests/Verification/`, `DatabaseCatalog/*.cs`, `global.json` a dvacet souborů v `docs/`. Gitu to nevadí (do indexu se normalizuje na LF), ale konfigurace popisuje něco jiného, než je na disku.

Vedle toho `.editorconfig` leží až v `ORMConvertor/` s `root = true` a má jen sekce `[*.cs]` a `[*.{cs,vb}]` — žádný soubor `.vb` v repozitáři není a nic nad `ORMConvertor/` (tedy `docs/`, `.github/`, `notes/`, kořen) žádné pravidlo nedostává.

Co je naopak v pořádku a je to to podstatné: `database/init-db.sh` je uložený i vyexpandovaný jako LF, takže se v linuxovém kontejneru nerozbije, a všechny textové soubory, na kterých závisí gcc a Docker (`ilp.c`, oba `Dockerfile`y, compose, workflow), jsou `i/lf`.

### 7.5 `.dockerignore` má tři vzory ukotvené v kořeni, kde musí být rekurzivní — drobné

`bin` a `obj` jsou uvedené dvakrát — jednou kořenově a jednou správně jako `**/bin`, `**/obj`. `TestResults`, `logs` a `*.user` jsou uvedené **jen** kořenově. Docker vzory rekurzivní nejsou (přesně proto vznikly ty dva s `**`), a jediné `TestResults`, které projekt vyrábí, je `Tests/TestResults/` — tedy to, které CI nahrává jako artefakt. Po lokálním běhu s `--collect` tak sběr pokrytí vstupuje do build kontextu přes `COPY . .`.

### 7.6 `ORMConvertor.sln` odkazuje na neexistující položku a tři centrální soubory v ní nejsou — drobné

`ProjectSection(SolutionItems)` uvádí `.gitignore = .gitignore`, což se řeší jako `ORMConvertor/.gitignore` — ten neexistuje, skutečný je v kořeni repozitáře. Naopak tři soubory, které rozhodnutí 034 udělalo centrálními (`Directory.Build.props`, `Directory.Packages.props`, `global.json`), mezi položkami řešení nejsou, takže se ve Visual Studiu neotevřou.

### 7.7 Historie gitu nese pět identit pro dva lidi a jeden prázdný commit — drobné

`git log --format='%an|%ae' | sort -u` vrací `Míra Štochel`, `Miroslav Stochel` a `Míra Stochel` pod jedním e-mailem a `milan_25` s `Milan Abrahám` pod druhým. `git shortlog -sne` proto oba přispěvatele rozpadá na dva a tři řádky. Repozitář má `CITATION.cff` i sekci o původu, takže autorství je tady vyslovený nárok; soubor `.mailmap` o čtyřech řádcích to srovná pro všechny nástroje najednou a do historie nesahá.

Vedle toho nesou commity `90a1836` a `f580fb2` **týž předmět** pět sekund po sobě, přičemž první je prázdný (přejmenování rozhodnutí 038→039, 0 vložených a 0 odebraných řádků). Rozhodnutí 007 dělá z git historie odpověď na otázku „co se změnilo kdy" — dvě shodné zprávy nad prázdným commitem tuhle odpověď zhoršují. Přepisovat vydanou historii se kvůli tomu nevyplatí; stojí za to to vědět pro příští vydání.

### 7.8 `diagrams/` a jeden lokální soubor popisují stav, který přestal platit — drobné

Tři soubory v `diagrams/` mají poslední commit z července 2025, tedy rok před převzetím. `architecture.md`:49 je jmenuje a říká, že „slouží textu práce" — jenže text práce byl z forku smazán (`c103d59`) a diagramy předcházejí všem rozhodnutím 001–041. Věta o tom, že „diagram je obraz ke dni, kdy vznikl", tam je; chybí, že ten den je před začátkem téhle práce.

Mimo repozitář je v kořeni pracovní kopie `2026-08-17-podklad-klice.local.md`. Jeho §1–§7 platí, ale **§8 „Co vědomě zbývá" je celé odbavené**: všech pět položek má dnes zástupce v `Tests/Verification/`, `DatabaseCatalog/` a `Tests/Catalog/`, a obě otevřené otázky, které jmenuje, uzavřela rozhodnutí 020 a 021 z 2026-08-19.

---

## 8. Co k tomu říká vnější praxe

Porovnání s vnějšími zvyklostmi pro výzkumný software, pro překladače a pro .NET repozitáře jsme dělali zvlášť; podrobnosti se zdroji jsou v pracovním podkladu mimo repozitář. Sem patří jen to, co je nálezem o stavu.

**Většina toho, co by takové srovnání vytklo, tady už je** — a v několika bodech je projekt nad běžnou praxí: centrální správa verzí se **zakázaným lokálním přebitím**, pravidlo pro nahrazování rozhodnutí přísnější než standardní ADR (revize na místě jen do doby, než podle rozhodnutí vznikne kód), matice trasovatelnosti požadavek → důkaz, model hrozeb s tabulkou vstupních bodů, mapování na ISO/IEC 25010:2023, které samo pojmenovává dvě nepokryté charakteristiky, a čtyřstupňové ověření generovaných artefaktů (rozhodnutí [016](../decisions/016-generated-artifact-verification-levels.md) a [027](../decisions/027-query-artifact-verification.md)) s **povinnou zápornou polovinou** u 2. a 3. stupně. Vědomá odmítnutí — changelog (007), build frontendu (032), přechodná období (003), mez pokrytí (§6.2) — jsou odmítnutí, ne mezery.

Čtyři věci vnější praxe žádá a tady nejsou:

### 8.1 Vydání nemá trvalý identifikátor, ačkoli README žádá citovat verzi — drobné

`README.md` říká „Please cite the version you used rather than `main`, which moves under the reader", ale `CITATION.cff` nemá `doi:` ani `identifiers:` a žádná archivní kopie neexistuje. „Verze, kterou jste použili" tak nemá jiný úchyt než značku v repozitáři, který se dá přejmenovat i smazat. Návazný drobný rozpor: `CITATION.cff` cituje diplomovou práci odkazem na `github.com/milan252525/orm-convertor`, kdežto `README.md` na její záznam v SIS.

### 8.2 REST kontrakt je chráněný verzí, ale žádný test ho nepřekračuje — závažné

Rozhodnutí 041 dělá z REST kontraktu jednu ze tří ploch, jejichž rozbití je MAJOR, a `ORMConvertor/README.md` ho nazývá „the tool's actual product surface". Přitom `ORMConvertorAPI` má podle §6.2 **6,0 % pokrytí řádků** a v řešení není `WebApplicationFactory` ani `HttpClient` — obsluhy se testují přes orchestraci, ne přes HTTP. Verzovaný kontrakt, přes který nevede jediný test, je z hlediska obhajoby nejslabší místo jinak silné sady.

### 8.3 Workflow nemá `permissions:` a akce jsou připnuté na pohyblivé značky — drobné

`.github/workflows/ormconvertor-tests.yml` nedeklaruje `permissions:`, takže obě úlohy dostávají výchozí rozsah `GITHUB_TOKEN`, ačkoli ani jedna nic nezapisuje. Akce jsou připnuté na `checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v4`, tedy na značky, které se posouvají.

Vedle toho **filtr cest workflow vylučuje sám sebe**: `paths: ["ORMConvertor/**"]` znamená, že změna `.github/workflows/**`, `.gitattributes`, `CITATION.cff` ani `LICENSE` běh nespustí, takže vadná úprava workflow se pozná až při příští změně řešení nebo v pondělí podle rozvrhu.

### 8.4 Pravidla stylu, která v repozitáři jsou, build nevynucuje — drobné

`.editorconfig` má jedenáct kilobajtů pravidel, ale `EnforceCodeStyleInBuild`, `AnalysisMode`, `AnalysisLevel` ani `TreatWarningsAsErrors` nejsou nastavené nikde v `Directory.Build.props` ani v `.csproj` — pravidla tedy váží Visual Studio, ne překlad. Souvisí s tím `ContinuousIntegrationBuild`, který by nárok S2 na determinismus rozšířil z výstupu i na sestavení.

---

## 9. Co bylo ověřeno a co ne

Aby bylo zřejmé, která tvrzení tohoto auditu stojí na zdrojích a která přebíráme.

**Ověřeno proti kódu, gitu nebo souborovému systému:** všechny nálezy kapitol 1–8 s výjimkou uvedenou níž. Konkrétně: sedm volajících `OpenConnection()`; nulový výskyt `required-content` v `architecture.md`; jediný výskyt `InverseRelationName`; 17 položek `PackageVersion`; obě čísla 419 a 392 dohledaná ke commitům `46672ee` a `d2e53d9` i s aritmetikou 3 × 9 = 27 z `Directions()`; obsah `git show 1.0:README.md` a `git show 46672ee~1:README.md`; osm mapovaných koncových bodů v `Endpoints.cs`; sedm odrážek vyňatých oblastí v rozhodnutí 030; nulový výskyt `AddOpenApi`/`MapOpenApi`/`WithOpenApi`; typ, commit a anotace obou značek; `git ls-files --eol` nad celým stromem; velikosti a data v `benchmarks/results` z metadat, **bez otevření jediného souboru** (adresář je pro čtení zakázaný).

**Ověřeno jako správné** (aby se z nepřítomnosti nálezu nedalo usoudit na opomenutí): rejstřík rozhodnutí proti souborům bez jediného rozdílu; **všechny relativní odkazy ve všech dokumentech `docs/` a v obou `README.md` vedou na existující cíle, včetně kotev s diakritikou**; `open-items.md` má právě jednu značku „Na řadě" a jednu „Potom"; všech 33 testovacích tříd citovaných v `traceability.md` na disku existuje; každé číslo požadavku F1–F15, S1–S7, T1–T7 má v matici řádek a žádné číslo navíc se nikde neobjevuje; `CITATION.cff` je platný a jeho verze i datum odpovídají značce; verze nástroje je zapsaná právě na dvou místech a obě říkají 1.1.0; řešení má 15 projektů proti 15 `.csproj` bez fantomů a graf referencí je acyklický; každá z 17 fixací má aspoň jednu referenci; úloha `dependencies` v CI odpovídá rozhodnutí 041 do detailu včetně vynuceného nenulového návratového kódu; commit „compressed static assets" nepřidal do gitu žádný generovaný soubor bez zdroje (`.gz`/`.br` v repozitáři nejsou, komprese vzniká až publikací); a pracovní kopie neobsahuje jediný zbytkový soubor (`.orig`, `.rej`, `.bak`, `.swp`, `Thumbs.db`) nad rámec jednoho `.csproj.user` s jedinou položkou.

> **Doplněno téhož dne, po sepsání auditu.** Sada byla spuštěna: `dotnet test --configuration Release` na hostiteli s lokálním SQL Serverem dal **419 prošlých, 0 selhaných, 0 přeskočených**, měření pokrytí zopakovalo 66,7 % řádků a 53,5 % větví včetně 0,0 % u obou projektů Advisoru. Tím padá většina odstavce níž — zůstává neověřený **kontejnerový** běh, protože na tomhle stroji není Docker. Odstavec se nepřepisuje, protože popisuje stav při psaní; co platí dnes, nese `architecture.md` §6.2.

**Neověřeno.** Neběžel build, testy ani kontejner, takže **z vlastního pozorování nepotvrzujeme**: že se řešení přeloží; počty 419 a 392 ani jejich poměr k dnešnímu stromu; čísla pokrytí v §6.2 včetně 6,0 % u `ORMConvertorAPI` a 0,0 % u Advisoru; časy v §6.4 a §5; velikost frontendu 177 kB a 40 kB v Brotli. Dál nevíme, jestli byl běh CI na `f1f570f` zelený (krok 1 rozhodnutí 041), jestli k oběma značkám existuje objekt GitHub Release, a jestli jsou značky publikované na `origin` — z klonu se to nepozná. Obsah `benchmarks/` a `diagrams/` jsme neotevírali; tvrzení o nich stojí na metadatech a na dokumentaci. Sekvence volání `glp_*` v `Advisor/ilp.c` proti popisu v §8 zkontrolovaná nebyla.

---

## 10. Co z auditu plyne

### Opravy

**`architecture.md` §6.1 — přenést opravené znění o `OpenConnection()`** z `open-items.md`; nesprávnou větu dnes nese jediný dokument, který má popisovat stav (nález 2.1). **A přeformulovat větu o připojovacím řetězci**, protože od rozhodnutí 039 v repozitáři je (nález 2.2).

**`architecture.md` doplnit o `/required-content` a `/required-content-advisor`** (nález 2.3) a **rozlišit v §4.3 pole, která dnes nemají čtenáře** (nález 2.4).

**Srovnat čísla.** Šestnáct na sedmnáct v §1 (nález 2.6) — nebo odstranit `Microsoft.AspNetCore.OpenApi` a nechat šestnáct platit (nález 6.3). Rozlišit v §6.2 a §6.4, o který běh jde (nález 1.2). Vypustit doslovné `1.0.0` ze dvou datovaných záznamů (nález 2.7).

**Kořenový `README.md` — opravit větu o značce `1.0`** (nález 3.1), **doplnit S1 a S3 do *Covered*** (nález 3.2) a **rozšířit vyňatou oblast 2 o `<natural-id>`, `<idbag>` a `<array>`** (nález 3.3). Tohle je nejnaléhavější skupina celého auditu: jde o kanonické znění nároku.

**Řádky `Historie` u rozhodnutí 030 a 041** — pět na šest, devět koncových bodů na osm (nálezy 4.1, 4.2). **A doplnit do 041 `CITATION.cff`**, na které se `architecture.md` §1 odvolává (nález 4.3).

**Do `audits/README.md` zaznamenat, že oba starší audity byly po datu editované**, a čím (nález 5.1) — týmž způsobem, jakým je tam zaznamenaný nesprávný nález auditu z 2026-08-21. Totéž pro `requirements.md` a `baseline.md` (nález 5.2).

**Úklid:** zakomentovaný `main()` v `ilp.c` (6.1), nepoužitá reference `Microsoft.AspNetCore.OpenApi` (6.3), prázdný `xunit.runner.json` i s řádkem v `Tests.csproj` (6.4), drobnosti z 6.5. **Konfigurace:** nahradit `.gitignore` desetiřádkovou verzí — především kvůli pozvánce k `#wwwroot/` (7.3), srovnat konce řádků a zavést kořenový `.editorconfig` (7.4), zrekurzivnit `.dockerignore` (7.5), opravit položku řešení v `.sln` (7.6). **Přidat `.mailmap`** (7.7). **Doplnit větu o stáří `diagrams/`** (7.8). **Přidat `permissions: contents: read` do workflow a `.github/workflows/**` do filtru cest** (8.3).

**Mimo repozitář:** `2026-08-17-podklad-klice.local.md` má §8 celé odbavené (7.8).

### Potřebná rozhodnutí

**Držení `benchmarks/results/` (nález 7.1).** Devadesát dva procent verzovaných bajtů na výsledky jednoho běhu z března 2025, které jsou z verzovaného skriptu obnovitelné. Volba je mezi ponecháním, zúžením na měření a souhrny, a přesunem mimo git — a musí počítat s tím, že z historie to velikost klonu nesníží. Patří sem i to, jestli inzerovat výsledky jako historický snímek, jak už to dělá `benchmarks/README.md`.

**Autorská práva v `LICENSE` (nález 7.2).** Repozitář má dva přispěvatele, převažující část kódu je zděděná a licence vznikla až ve forku. Rozhodnout je třeba, jestli přibude druhý řádek copyrightu, nebo jestli se vyžádá a zaznamená potvrzení od původního autora. U citovatelného artefaktu to není formalita.

**Množinové operace jako živá, nebo jako připravená cesta (nález 2.5, souvisí 6.2).** Dnešní stav — vykreslovací kód bez výrobce, `ExceptAll` bez vykreslení, `In` bez parseru — je uvnitř oblasti, kterou §9 vyjímá vcelku, takže o vadu nejde. Rozhodnout je třeba, jestli se ta část popíše jako předpřipravená pro budoucí parser, nebo se zúží na to, co skutečně projde. Bez volby přibývá kód, který nikdo nevolá, a záznamy `Loss`, které se nikdy nevypíšou.

**Testy REST kontraktu (nález 8.2).** Rozhodnutí 041 povýšilo REST kontrakt na plochu chráněnou verzí. Rozhodnout je třeba, jestli ho má hlídat test přes HTTP, nebo jestli se nárok zúží na to, co dnes hlídá orchestrace — a jestli by protějškem nebyl radši generovaný `openapi.json` v repozitáři, který by byl navíc lepším artefaktem než Swagger UI, jež produkční instance neservíruje.

### Delší horizont

**Trvalý identifikátor vydání (nález 8.1).** Zbytek citačního řetězce je hotový — `CITATION.cff`, licence, anotované značky s poznámkami. Chybí jediný článek a rozhodnutí 041 už s tím počítá, když mluví o vykreslení v přehledu vydání.

**Vynucení stylu a reprodukovatelnost sestavení (nález 8.4).** Rozšířilo by nárok S2 z výstupu na sestavení. Souvisí s tím, že žádný soubor zámku závislostí neexistuje a základní obrazy kontejnerů jsou připnuté na pohyblivé značky — tedy že „reprodukovatelné prostředí" dnes znamená „jedním příkazem", ne „bajtově stejně".

**Vlastnost, kterou audit našel napříč nálezy.** Předchozí audit uzavřel pozorováním, že se hranice záruk dobře píše v rozhodnutí a špatně udržuje v popisu stavu. Tenhle nachází jeho pokračování o patro výš: **od 1.1.0 existují tři místa, která nárok vyslovují — `README.md`, §9 a `traceability.md` — a rozcházejí se všechna tři navzájem** (nálezy 3.1–3.3). Patrové pravidlo, které se kvůli tomu zavedlo, říká jen, který text vyhrává; udržet je v souladu neumí. Levnější než pravidlo by bylo, kdyby si jedno patro odvozovalo znění z druhého — nebo aby se všechna tři měnila jediným průchodem, jehož součástí je porovnání, ne jen zápis.

Druhá věc téhož druhu: **nálezy 1.1, 1.2, 2.1, 2.6 a 4.1 mají společnou příčinu — poslední den před vydáním**. Čtyři z pěti vznikly commity `d2e53d9`, `46672ee` a opravným průchodem `40b69a3`, tedy tam, kde se dokumentace dopisovala souběžně s prací a doklady se pořizovaly dřív, než byla práce hotová. Krok 1 rozhodnutí 041 („práce je hotová, pracovní kopie čistá a CI zelené") tuhle situaci popisuje, ale nežádá, aby se **doklady pořídily až po posledním commitu** — a přesně to je, co u nálezu 1.1 chybělo.
