# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Kde se pokračuje, říká značka na řádku vazeb položky** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě. Samostatný seznam pořadí tenhle soubor nemá — rozcházel se s položkami pod ním.

**Položky jsou od 2026-08-24 seskupené do tří cílů v pořadí, ve kterém na ně dojde:** nejdřív dotáhnout překlad a dotazy tří .NET frameworků — tento cíl uzavře vydání 1.2.0 —, potom tři javovské ORM (F7–F10, F12–F13), a teprve potom Advisor, benchmarking a experimenty (T1–T7). Práci na Advisoru, benchmarkingu a rozhraní jsme vědomě odložili stranou: jejich položky tu zůstávají zapsané, ale nepracuje se na nich a značky pořadí nedostávají. Značky se pohybují uvnitř prvního nedokončeného cíle.

---

## Cíl 1 — překlad a dotazy tří .NET frameworků (uzavře vydání 1.2.0)

Dotáhnout překlad entit, mapování a dotazů mezi Dapperem, EF Core a NHibernate tak daleko, jak to jde — bez Advisoru a bez zásahů do rozhraní. Nástroj je na verzi 1.1.0; dokončení tohoto cíle vydáme jako 1.2.0 (verzování podle rozhodnutí [041](./decisions/041-versioning-and-release.md)).

Co už stojí: dotazová větev je hotová ve všech devíti směrech v rozsahu kategorií projekce, filtrace, join, agregace a řazení (rozhodnutí 022–027); bloky F4–F6 jsou hotové; z F11 jsou varování o nevyjádřitelných faktech a kontrola úplnosti IR hotové (rozhodnutí 010), syntaktické ověření generovaných souborů také (rozhodnutí 016, [`architecture.md`](./architecture.md) §6.2) a záznam běhu podle S6 — identifikátor běhu, verze obou frameworků z deskriptorů a verze nástroje z `Directory.Build.props` (rozhodnutí 034) — vydává `/convert`; 4. stupeň ověření má od 2026-08-25 spuštěný scénář uložení a načtení pro každý typ vztahu — 1:N s kolekcí, 1:1 sdíleným klíčem a M:N průchodem přes syntetizovanou junction entitu (§6.2 architektury), čímž je naplněné kritérium F3. Množinové operace, stránkování i poddotazy v podmínce jsou od 2026-08-25 hotové ve všech devíti směrech (rozhodnutí [060](./decisions/060-pagination-as-a-query-instruction.md) a [061](./decisions/061-subquery-as-a-condition-operand.md), `architecture.md` §5) — dotazová matice T2 tím nemá prázdnou kategorii a poddotazy vystoupily z hranice záruk (§9). Holé HQL se od 2026-08-25 čte vlastním parserem v NHibernate wrapperu (rozhodnutí [062](./decisions/062-hql-read-by-a-hand-written-parser.md)), takže převod NHibernate → NHibernate je textový round-trip a vyňatá oblast 4 z hranice vystoupila také. Anotace `[Keyless]` se od 2026-08-25 čte a vyslovená bezklíčovost je nesený fakt modelu (rozhodnutí [063](./decisions/063-stated-keylessness-as-a-carried-fact.md)): konvence ani katalog nevymyslí klíč přes výslovné popření a rozpor popření s klíčem dopadá podle sémantiky zdroje — `[Key]` padá se záznamem `Conflict`, třídní `[PrimaryKey]` vedle `[Keyless]` je `Failure` bez artefaktu. Strategie klíče se od 2026-08-25 doplňuje z katalogu z obou stran (rozhodnutí [064](./decisions/064-absence-of-generation-as-a-catalog-fact.md)): sloupec bez IDENTITY a bez default constraintu vychází jako přidělovaný, sloupec s defaultem se hlásí záznamem, a EF Core builder konvenční generování nevysloveného klíče nově hlásí — artefakt nad tabulkou `Products` už netvrdí generování, které schéma nemá. Co zbývá, jsou položky níže: zbytek F11 v položkách o atribuci záznamů a o mezích pravidla 053.

### Rozhodnutí

#### Kritérium pro širší čtení konvencí zdroje
*Navazuje na rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), které v tomto bodě nahradilo [008](./decisions/008-database-as-metadata-source.md). Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F2, F5, F6.*

Parsery dnes čtou konvenci zdrojového frameworku jen tam, kde by její neznalost změnila význam, což je v praxi jediné místo: primární klíč v `EFCoreEntityParser.FindConventionKey`. Implicitní je ale i název sloupce, název tabulky a nullabilita odvozená z jazykového typu, a u MyBatisu je konvenční mapování jediné, které vůbec existuje, takže F6 na širším čtení stojí. Rozhodnutí 008 otázku odložilo s podmínkou „až mezireprezentace bude evidovat původ faktu"; rozhodnutí 010 ale původ z modelu vyňalo a 015 tuhle volbu převzalo, takže odkládací podmínka nikdy nenastane a otázka zůstala bez kritéria. Je třeba rozhodnout, podle čeho se pozná, kterou konvenci materializovat. Nabízí se „materializuj tam, kde by cíl doplnil něco jiného, než tvrdí zdroj", to ale znamená znát konvence obou stran a mít je kde zapsat — pravděpodobně v deskriptoru, který dnes kategorii pro konvence nemá. Dokud kritérium chybí, přibývají konvence jednotlivě a bez pravidla, jak se to stalo u klíče v EF Core.

#### Framework s vlastní precedencí mezi svými artefakty
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), které tenhle případ vědomě nechalo otevřený; rozhodnutí [049](./decisions/049-language-facts-under-source-precedence.md) zavřelo jeho mechanickou půlku. Předpoklad čtení fluent konfigurace EF Core a podpory MyBatisu (F8). Požadavky F5, F11.*

Rozhodnutí 017 uspořádalo zdroje faktů na vstupní text frameworku, pomocné mapovací artefakty, katalog a konvenci cíle. Některý framework ale mezi svými vlastními artefakty precedenci sám dokumentuje, a to opačnou: EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML mapperu vlastními pravidly. Přeložit takový projekt naším pořadím znamená přeložit něco jiného, než co zdroj znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Rozhodnout je třeba, jestli precedence zdrojového frameworku přebíjí naše pořadí uvnitř prvního stupně, a pokud ano, kde je ta precedence zapsaná: deskriptor popisuje cíl, ne zdroj, a kategorii pro tohle nemá. Dokud se čte jediný artefakt na framework, případ nenastane; nastane s prvním parserem fluent konfigurace. Co už otevřené není, je co se stane *mechanicky*: rozhodnutí [049](./decisions/049-language-facts-under-source-precedence.md) rozšířilo pravidlo 017 i na jazyková fakta vlastnosti, takže druhá deklarace téže vlastnosti nevyrobí duplikát, ale doplní prázdné fakty a rozdíl ohlásí. Otevřená zůstává otázka o *pořadí*: jestli precedenci, kterou si zdrojový framework dokumentuje sám, máme respektovat místo svojí.

#### Který vstupní jednotce záznam patří
*Sem odkázalo rozhodnutí [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md), které atribuci po jednotkách vědomě nechalo otevřenou. Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md) a s vícejednotkovým vstupem podle F14. Požadavky F11, F14, S6.*

Rozhodnutí 045 zavřelo ticho na úrovni běhu: jednotku v jazyce, který zdroj nečte, i převod, ze kterého nevyšlo nic, dnes hlásí `Failure`. Zbývá případ mezi tím — jednotka, ze které nic nevzejde, zatímco jiná jednotka téhož běhu artefakt vydá. Volající dostane výstup a o té jedné jednotce se nedozví nic, přestože ji poslal.

Rozeznat ji počítáním `EntityMaps` před a po každém parseru nejde: u NHibernate parser XML mapování obohacuje entitu, kterou už vytvořil parser třídy, takže nepřidá novou mapu a poctivá jednotka by dostala nepoctivý záznam. Poctivá atribuce vyžaduje, aby parser hlásil, co z které jednotky přečetl — tedy zásah do rozhraní parserů v `AbstractWrappers`, které dnes vrací `void`. To je změna, kterou má podle S1 předcházet rozhodnutí: rozhraní parserů je právě ta plocha, o které S1 tvrdí, že nový framework se do ní vejde beze změny. Precedens, jak takové rozhodnutí vypadá, je [047](./decisions/047-content-type-reaches-the-query-parser.md) — rozhraní se změnilo proto, že nenesené informace se dole rekonstruovaly odhadem; tady by šlo o návratovou hodnotu, ne o parametr, ale úvaha o S1 je táž. Zároveň je to předpoklad toho, aby diagnostika uměla ukázat na *soubor*, což je zúžení, které dnes u F14 vyslovuje [`architecture.md`](./architecture.md) §9.

#### Meze pravidla „dotaz, který vrátí jiné řádky, se nevydá"
*Na řadě. Sem odkázalo rozhodnutí [053](./decisions/053-a-query-that-would-return-other-rows-is-not-emitted.md), které svou vlastní hranici vyslovilo. Souvisí s [004](./decisions/004-unexpressible-facts-as-warnings.md) a [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F11, T2, T3.*

Rozhodnutí 053 zakázalo dosazovat náhražku za podmínku, kterou cíl vykreslit neumí: tautologie místo filtru vrátí všechny řádky, které zdroj vyloučil, takže artefakt nevzniká a důvod je v záznamu. Táž věta ale doslova sedí i na náhradu, která v nástroji zůstává a je popsaná jinde: **plný vnější join** se u EF Core i NHibernate vypisuje jako vnitřní — zúžení uvnitř kategorie `JoinKind`, hlášené v místě emise ([`architecture.md`](./architecture.md) §5.1) — a vnitřní join vrací *méně* řádků než plný, takže výsledek dotazu je jiný, ne jen chudší.

Rozhodnout je třeba, jestli je hranicí pravidla *podmínka* (dnešní znění 053), nebo *množina řádků* (jeho vlastní argument). Druhá odpověď je přísnější a konzistentnější, ale není zadarmo: u joinu by znamenala nevydat artefakt tam, kde ho dnes uživatel dostane spolu se srozumitelným záznamem o tom, co se stalo, a matice T2 by v kategorii *druh joinu* přestala měřit překlad a začala měřit odmítnutí. Volba se navíc dotýká čtení T3: dnes se takový výstup počítá jako přeložený a funkčně neekvivalentní, po zpřísnění by se nepočítal vůbec. Poddotazová půlka, kterou tahle položka původně nesla také, je rozhodnutá: zahozený poddotaz přestal existovat — poddotaz v operandu podmínky se vykresluje a poddotaz ponechaný v těle je `Failure` bez artefaktu (rozhodnutí [061](./decisions/061-subquery-as-a-condition-operand.md)), takže otázka zbývá jen u joinu. Stránkování se jí netýká také: rozhodnutí [060](./decisions/060-pagination-as-a-query-instruction.md) pro ně větu o množině řádků vyslovilo rovnou — co se nést nedá, je `Failure` bez artefaktu —, protože tam pravidlo vznikalo dřív, než vůbec existoval artefakt, o který by šlo přijít.

### Práce

#### Dvě měření téže revize se rozešla v počtu testů
*Nalezeno při ověření na druhém stroji (2026-08-24). Obě čísla dnes nese [`ORMConvertor/README.md`](../ORMConvertor/README.md#how-large-the-suite-is-and-what-it-covers); na počtu stojí důkazní sloupec S5 v [`traceability.md`](./traceability.md). Souvisí s rozhodnutím [039](./decisions/039-container-configuration-of-the-environment.md), které nulové přeskočení udělalo podmínkou důkazu F4 a F6. Požadavky F4, F6, S5, T3.*

Nad touž revizí a v týž den vyšla sada na dvou strojích různě: **484 testů a pokrytí 70,8 % řádků / 58,1 % větví** na prvním, **507 testů a 71,3 % / 58,9 %** na druhém. Přeskočením to není — obě měření hlásí nula přeskočených —, a kontejnerový běh na druhém stroji došel k 507 nezávisle na tamním hostitelském běhu. Po projektech se přitom obě měření shodují do posledního čísla, které README jmenuje: `SampleData` a `CSharpEntityParsing` na 100 %, `OrmConvertor` 95,0 %, `Model` 93,3 %, `Advisor` a `AdvisorBenchmarking` 0,0 %, `ORMConvertorAPI` 42,4 %. Rozchází se tedy jedině součet, a poslední commit, který sáhl do `Tests/`, je starší než obě měření, takže mezitím přidaný test to nevysvětlí.

Nejbližší vysvětlení navíc nesedí. U dvojice 419 a 392 z 2026-08-23 stačilo rozdíl spočítat — bylo to přesně dvacet sedm testů, které přidal `ConsumerProjectFactsTest`, a obě čísla platila, každé o jiné sadě. Tady rozdíl dělá dvacet tři, jenže poslední commit, který do `Tests/` sáhl (`91a5dc7`), přidal řádově sedmapadesát případů. Ani „změřeno nad předchozím stromem" tedy nevychází: před ním by sada měla mít o sedmapadesát méně, ne o dvacet tři.

Zbývá zjistit, co první měření počítalo — nabízí se pracovní kopie s rozpracovanou změnou, běh s filtrem nebo znovu nepřeložené testovací sestavení — a sadu přeměřit na prvním stroji nad čistým checkoutem téže revize. Číslo, které přežije, ať v README zůstane samo; dnes tam jsou obě a S5 v `traceability.md` to říká také. Naléhavé to je proto, že tenhle jeden počet je jediné místo, kde se velikost sady vyslovuje, a opírá se o něj jak důkaz S5, tak údaj o pokrytí, který má obsloužit T3. Číslo, kterému si dvě měření protiřečí, není důkaz.

#### Tvar `IN` seznamu v katalogových dotazech — nejdřív změřit
*Souvisí s [`architecture.md`](./architecture.md), §5.2, a rozhodnutím [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavek S3.*

Katalogové dotazy staví `IN` seznam z jednoho parametru na jméno tabulky, takže deset jmen a jedenáct jmen jsou dva různé texty příkazu a dvě položky plan cache SQL Serveru. Poloviční násobič — délku parametru odvozenou z hodnoty — odstranila výslovná deklarace `nvarchar(128)`; zbylý by odstranil jediný parametr nesoucí celý seznam (table-valued parameter, případně `OPENJSON`/`STRING_SPLIT`, které ale předpokládají úroveň kompatibility databáze 130). Před zásahem je třeba skutečnou cenu potvrdit pohledem do `sys.dm_exec_cached_plans` — dosavadní čtení je z kódu, ne z trasy — a bez potvrzené ceny se tvar nemění.

#### Překlad s připojeným katalogem nemá měření
*Souvisí s rozhodnutím [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) a s [`ORMConvertor/README.md`](../ORMConvertor/README.md#translation-performance-s3). Požadavek S3.*

`TranslationPerformanceTest` (100 entit × 100 dotazů pod 30 s) volá `ConversionHandler.Convert` bez připojovacího řetězce, takže třicetisekundová mez měří jen parsování a generování — katalogová fáze do ní přispívá nulou a tvrzení o výkonu překladu s připojenou databází dnes žádné číslo nemá. Data k němu přitom existují: `CatalogPhaseResult.CatalogReadTime` fázi měří u každého převodu, jen je nikdo neskládá dohromady. Zbývá scénář, který S3 změří i s katalogem — proti testovací databázi kolekce `TestDatabaseSchema`, kde už čtečka běží —, a agregace `CatalogReadTime` přes běh Advisoru, má-li se o katalogové ceně běhu něco tvrdit.

## Cíl 2 — tři javovské ORM

Jádro rozšíření ze zadání: Hibernate (F7), MyBatis (F8) a EclipseLink (F9), cross-ecosystem překlad mezi .NET a Javou (F10) a k tomu testovací infrastruktura pro Javu s diferenčním ověřením výsledků dotazů (F12, F13) jako důkaz funkční ekvivalence. Slovníkové předpoklady jsou hotové: typový model je zneutralizovaný na jazykové (rozhodnutí 014) i databázové straně (rozhodnutí 019) a parametry generátoru se nesou kanonicky s výběrem názvu ve výstupu (rozhodnutí 020 a 021, obojí implementované). Každý velký blok si zaslouží vlastní rozhodnutí, než se do něj sáhne.

S prvním javovým parserem se navíc vrátí dvě otázky z cíle 1: precedence mezi artefakty frameworku (MyBatis ji mezi anotacemi a XML mapperem dokumentuje vlastní) a kritérium pro čtení konvencí (u MyBatisu je konvenční mapování jediné, které existuje).

### Práce

#### Cílový databázový dialekt v deskriptoru
*Sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md); deskriptor s cílovou verzí, na kterou se dialekt tvarem podobá, je hotový (rozhodnutí [013](./decisions/013-target-framework-versions.md)). Vyňatá oblast hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F7–F10, S2.*

Cílový databázový dialekt je fakt o cíli převodu téhož tvaru jako verze frameworku v deskriptoru, a rozhodnutí 019 ho odmítlo řešit v typovém modelu. Bez jeho deklarace nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — oba .NET buildery dnes propisují jen doslovný `SourceSqlType`, který nese zdroj (rozhodnutí [052](./decisions/052-literal-sql-type-reaches-the-ef-core-annotation.md) srovnalo EF Core s NHibernate), a odvozený název typu berou z pevné tabulky pro SQL Server. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server a nástroj nic víc netvrdí.

Zdrojová strana je jiná otázka než tahle položka a deklarace cílového dialektu ji nevyřeší: `DapperSqlQueryParser` čte T-SQL gramatikou `TSql160Parser` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)), takže SQL napsané pro jiný databázový systém — u MyBatisu (F8) běžné — touhle cestou neprojde. Řešením je vlastní parser SQL v javovém wrapperu, ne pole v deskriptoru.

## Cíl 3 — Advisor, benchmarking a experimenty

Lepší Advisor a benchmarking a na nich stojící experimentální požadavky T1–T7; T7 navazuje na existující ILP Advisor. Advisor s benchmarkingem jsou ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) a celý cíl je vědomě odložený: dokud běží cíle 1 a 2, položky tady jen leží a značky pořadí nedostávají.

### Rozhodnutí

#### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

#### Advisor měří nedoplněný překlad
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Sem odkázalo rozhodnutí [059](./decisions/059-advisor-response-carries-the-measured-translations.md), které svou variantu 3 zamítlo jen pro teď. Souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F15, T7.*

Překladová fáze `/advisor/run` volá `ConversionHandler.Convert` bez připojovacího řetězce, takže benchmark kompiluje a měří překlad bez katalogového doplnění — kdežto `/convert` tentýž vstup doplní a uživatel by nasadil doplněnou verzi. Čísla Advisoru tedy platí o jiném kódu, než jaký si uživatel z nástroje odnese. Od rozhodnutí 059 je to aspoň vidět: odpověď nese měřené artefakty a jejich stav říká, že katalog nebyl použit. Rozhodnout je třeba, jestli má překladová fáze dostat tutéž cachovanou čtečku jako fáze benchmarková — technicky je to po zavedení `CachingCatalogReader` levné, jedna dávka na framework — a co to udělá s naměřenými čísly: doplněné entity nesou jiné atributy a vztahy, takže se mění kompilovaný harness, a změna metodologie měření se musí přeměřit, ne jen zapnout.

#### Iterační politika benchmarku je konstanta v kódu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavek T7.*

`BenchmarkExecutor` měří každý pár (dotaz × framework) pevným postupem: dvě zahřívací iterace, pilotní běh a z něj odvozených 3–20 měřených iterací s cílem ~500 ms celkem. Konstanty jsou zapsané v kódu bez odůvodnění a bez možnosti je ovlivnit z rozhraní, přitom právě ony určují rozptyl a délku běhu, o které v T7 jde; nadbytečná náhledová invokace — celé jedno provedení dotazu jen kvůli ladicímu výpisu — už je zrušená. Rozhodnout je třeba, jestli jsou tyhle hodnoty součástí metodologie, kterou text práce vysloví a odůvodní, nebo parametrem požadavku, a čím se volba podloží; měnit je bez rozhodnutí znamená měnit význam všech dosavadních čísel.

#### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient`, který k nim teče přes `benchmarks/Common`. Pro srovnání výkonu je to metodologický confound. **Rozsah je nově dohledaný celý** — u PetaPoco vyloučením, protože `Microsoft.Data.SqlClient` v grafu balíků obou jeho projektů není, u EF6 z `WWIDbConfiguration`; podrobnosti nese [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Zbývá tedy volba, ne zjišťování: buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` (samostatný balík, dnes nereferencovaný) a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

### Práce

#### Advisor nemá build nativní knihovny pro Windows
*Mezera popsaná v [`architecture.md`](./architecture.md), §8. Souvisí s T7. Kontejnerová cesta Advisor pokrývá a je ověřená (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md), [`ORMConvertor/README.md`](../ORMConvertor/README.md#tests)); tahle položka je o hostiteli bez Dockeru.*

`libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`) a název je v P/Invoke natvrdo linuxový, takže mimo Linux a Docker Advisor endpointy selhávají; překladová část na tom nezávisí. Advisor je ze záruk vyňatý vcelku ([`architecture.md`](./architecture.md), §9), takže doslovně linuxový název v `LibraryImport` odpovídá tomu, co o něm tvrdíme; zbývá jen build krok pro `advisor.dll`, pro který má `ilp.c` exportní makra připravená. Nasazenou instanci to neshodí — `AdvisorRunHandler` výjimku z P/Invoke zachytává a vrací její text, takže uživatel dostane hlášku.

#### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

#### Hláška o neřešitelném ILP modelu dorazí do logu až s dalším voláním
*Nalezeno při ověření na druhém stroji (2026-08-24); popis v [`architecture.md`](./architecture.md), §8, je podle toho opravený. Vyňatá oblast 1 hranice záruk (§9). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavky T7, S6.*

`solve_problem()` v `Advisor/ilp.c` vypisuje `No feasible solution found.` obyčejným `printf`. Standardní výstup je v kontejneru přesměrovaný na rouru, tedy plně bufferovaný, a nikdo ten buffer nevyprazdňuje. Hláška se do logu **dostane**, ale teprve až ji protlačí výstup dalšího volání: tři neřešitelné úlohy za sebou vydaly dvě hlášky, každou o jeden běh opožděnou. Vlastní výpis GLPK dorazí včas, protože nejde přes `stdio`, takže v logu stojí `PROBLEM HAS NO PRIMAL FEASIBLE SOLUTION` bez naší věty vedle sebe.

Oprava je jednořádková — `fflush(stdout)` za tím výpisem, případně řádkové bufferování při inicializaci knihovny —, zadarmo ale není: `libadvisor.so` se překládá jedině v Docker buildu, takže změnu je nutné přeložit a ověřit v kontejneru, a sahá se přitom do oblasti bez jediného testu, kterou vyjímáme ze záruk vcelku. Dokud se to nestane, drží ten stav §8 svým popisem, aby nikdo nehledal hlášku, která po jeho volání v logu ještě není. Návratový kód ani tělo odpovědi to nijak nemění — neřešitelnou úlohu pozná volající z **400**, respektive ze `status: -1`, přesně jak §8 popisuje a jak jsme ověřili.

#### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Souvisí s rozhodnutím [041](./decisions/041-versioning-and-release.md) — nový koncový bod je změna REST kontraktu, tedy vydání MINOR — a s položkou „Advisor nemá build nativní knihovny pro Windows". Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so` a `AdvisorRunHandler` výjimku z P/Invoke zachytí a vrátí její text, takže uživatel se o nedostupnosti dozví jako o `DllNotFoundException` — po vyplnění celého formuláře a po odeslání běhu. Úvodní odstavec obrazovky přitom říká dopředu, že Advisor potřebuje kontejner; nedostupnost se tedy sděluje dvakrát, jednou naší větou předem a jednou hláškou zavaděče potom.

Aby to obrazovka mohla říct **místo** běhu a vlastními slovy, potřebuje se serveru zeptat, jestli je Advisor na tomhle hostiteli k dispozici — dnes na to není koncový bod a klient si to odvodit nemůže. Je to tedy nový koncový bod, tedy změna veřejné plochy podle rozhodnutí 041; sám o sobě je malý, ale předchází mu volba, jestli do vyňaté oblasti sahat dřív, než se dodělá rozdělané.

## Stranou cílů — rozhraní

Zásahy do rozhraní jsme odložili stranou všech tří cílů. Z F14 je hotový vícesouborový vstup a výstup po souborech; zbytek bloku F14–F15 — dávkové vstupy a zobrazení mezireprezentace — je tady a optimalizační půlka F15 patří Advisoru (cíl 3).

### Rozhodnutí

#### Směr překladu jako jedna věc a vstup vedle výstupu
*Navazuje na rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md), které pětikrokový tvar obrazovky zvolilo a je podle něj napsaný kód; případnou změnu je proto třeba nahradit, ne revidovat. Souvisí s [032](./decisions/032-frontend-as-static-pages-without-a-build.md) a s položkou „Který vstupní jednotce záznam patří". Požadavky F14, S7.*

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných sekcí viditelných najednou a první dvě z nich jsou volba zdroje a volba cíle. Důsledek je, že směr překladu na obrazovce nikde nestojí jako jedna věc: „EF Core → NHibernate" se poprvé objeví až v hlavičce výsledku a prohodit obě volby jde jen ručně, dvěma zásahy do dvou rozbalovacích seznamů. Jedna řádka směru s tlačítkem pro prohození by z pěti sekcí udělala čtyři, což S7 nebrání — „nejvýš pět kroků" je strop, ne kvóta —, ale je to změna volby, kterou 033 vyslovilo výslovně, takže patří do nového rozhodnutí.

Do téhož rozhodnutí patří druhá otázka, protože obě mění tvar téže obrazovky a navrhovat je zvlášť by znamenalo navrhnout ji dvakrát: **jestli má vstup stát vedle výstupu.** Dnes jsou vstupní jednotky nahoře jako textová pole a artefakty dole jako panely, takže se zdroj a výsledek nedají číst současně — na výkladové stránce vedle sebe stojí, na nástrojové ne. Podstatná je tu poctivost, ne rozvržení: server neříká, který artefakt vznikl ze které jednotky (§9, zúžení F14), takže sloupce vedle sebe se buď musí spárovat toutéž jmennou heuristikou, jakou se artefakty pojmenovávají, a jako heuristika se i označit, nebo nesmí tvrdit párování vůbec a nesou pak nadpisy typu „co jste poslali" a „co přišlo zpět". Druhá cesta nic nevymýšlí a je slučitelná s tím, že skutečné párování čeká na pojmenované jednotky — tedy na otevřenou položku *Který vstupní jednotce záznam patří*.

#### Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje
*Zúžení, které dnes vyslovuje [`architecture.md`](./architecture.md), §9 („zobrazení IR verze nenárokuje vůbec"). Souvisí s rozhodnutími [010](./decisions/010-diagnostics-as-returned-data.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md) a [041](./decisions/041-versioning-and-release.md). Požadavky F11, F14.*

Požadavek F14 žádá zobrazení čtyř věcí — vstupu, mezireprezentace, výstupu a diagnostiky — a nárokujeme tři: `/convert` mezireprezentaci nevrací a rozhraní ji nemá odkud vzít. Je to jediné místo, kde se dnes nárok na F14 zužuje z důvodu, který leží na serveru, ne na obrazovce, a zároveň to nejlépe placené místo pro text práce: pipeline parse → doplnění → build se čtenáři, který nástroj nikdy nespustí, ukazuje právě prostředním článkem.

Rozhodnout je třeba dřív, než se cokoli začne psát, protože cena není ve vykreslení: serializovaný tvar `EntityMap`, klíče, vztahů a dotazových instrukcí by se stal součástí REST kontraktu se vším, co to znamená pro verzování (rozhodnutí 041, změna MINOR při přidání pole a MAJOR při jeho odebrání). Otázka tedy zní, jestli se mezireprezentace vydává jako plnohodnotná část odpovědi, nebo jako výslovně nestabilní náhled, u kterého se dopředu řekne, že se může měnit mezi vydáními — a druhá odpověď je levnější jen zdánlivě, protože nestabilní část kontraktu je pořád část kontraktu.

### Práce

#### Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška
*Souvisí s rozhodnutím [033](./decisions/033-shape-of-the-static-frontend-screens.md), které validaci XML s číslem řádku zavedlo, a s [032](./decisions/032-frontend-as-static-pages-without-a-build.md), bod f (žádná další vendorovaná knihovna bez rozhodnutí). Požadavek S7.*

Validace před odesláním hlásí u nesprávně utvořeného XML číslo řádku a serverová hláška u SQL nese řádek a sloupec z `TSql160Parser`. Editor jednotky je ale holý `<textarea>` bez číslování, takže „řádek 7" se v něm hledá počítáním. Doslovné znění S7 mluví o zvýraznění chyb na úrovni souboru a řádku a tohle je jeho druhá půlka, která chybí — první, tedy chyba přiřazená ke konkrétní jednotce, hotová je.

Práce je to hotová v zadání, ne v rozvaze: postranní sloupec s čísly řádků, který se posouvá spolu s textovým polem, je několik desítek řádků vlastního kódu a chová se spolehlivě. Co je potřeba nedělat, je sáhnout po hotovém editoru — CodeMirror nebo cokoli podobného by byla třetí vendorovaná knihovna, a to je podle bodu 032f samostatné rozhodnutí, ne detail implementace.
