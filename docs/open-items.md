# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Položky jsou roztříděné do pěti kategorií, ne do cílů zadání.** Cíle 1 a 2 jsou hotové a třetí by zbyl jediný, takže dělení podle nich přestalo třídit. Kategorie říká, na čem se pracuje teď — a u zbytku, proč se na něm nepracuje:

- **[Vady](#vady)** — místa, kde nástroj vydá artefakt, který odporuje větě, kterou verze nárokuje ([`architecture.md`](./architecture.md), §9, a sekce *Guarantees* kořenového [`README.md`](../README.md)). Každá položka jmenuje větu, kterou vyvrací, a čím je vada ověřená.
- **[Advisor](#advisor)** — Advisor, benchmarking a experimentální požadavky T1–T7, tedy třetí cíl zadání a příští milník.
- **[Rozhraní](#rozhraní)** — zásahy do frontendu, odložené stranou od všeho ostatního.
- **[Užitečné, ne nutné](#užitečné-ne-nutné)** — co by nástroji nebo repozitáři prospělo, ale nic to nenárokuje a nic tím není blokované.
- **[Zbytky](#zbytky)** — zdokumentované mezery a nezodpovězené otázky, na které se nesahá.

**Odkazy na cíle zůstávají čitelné a čísla se nepřidělují znovu.** Cíl 1 je vydání `1.2.0`, cíl 2 vydání `2.0.0`, a cíl 3 je dnešní kategorie [Advisor](#advisor), takže zmínky o „cíli 1" a „cíli 2" v rozhodnutích [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [069](./decisions/069-major-marks-a-milestone-not-a-break.md) dál platí. Co které vydání změnilo, nese anotace jeho značky (`git tag -n99 2.0.0`); co nástroj nárokuje dnes, [`architecture.md`](./architecture.md), §9, a sekce *Guarantees* kořenového [`README.md`](../README.md).

**Kategorie neříká pořadí — to nese značka** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě, obě stojí na začátku kurzívového řádku vazeb. **Po vydání `2.0.0` značku nenese žádná položka** — čím se pokračuje, je volba, která ještě nepadla. Samostatný seznam pořadí tenhle soubor nemá a kategorie jím nejsou: položka je v souboru právě jednou a mezi kategoriemi se stěhuje jedinou úpravou.

---

## Vady

Nejde o mezery, které §9 vyslovuje a na které verze spoleh neslibuje, ale o vady uvnitř toho, co slibuje: vstup, na který se nárok vztahuje, dá artefakt, který cílový framework odmítne nebo který tiše znamená něco jiného. Obě dnešní vyšly najevo 2026-09-22 při stavbě rozsáhlých příkladů výkladové stránky (rozhodnutí [099](./decisions/099-examples-are-content-not-a-choice.md)) a obě ověřil cílový framework sám, ne úvaha nad kódem. Opravují se jako práce, protože rozhodnutí, podle kterého se má výstup chovat, už existuje. Značku pořadí nenesou z téhož důvodu jako ostatní položky po vydání `2.0.0`; nárok, který vyvracejí, ale do jejich opravy platí jen s touhle výhradou — a kdyby se opravovat neměly, musí se zúžení vyslovit v §9 i v *Guarantees*.

### Práce

#### Inverzní kolekce nad složeným cizím klíčem dostane v NHibernate jednosloupcový `<key>`
*Vyšlo najevo 2026-09-22 na výpůjční knihovně výkladové stránky (rozhodnutí [099](./decisions/099-examples-are-content-not-a-choice.md)); NHibernate 5.7.0 vydané mapování odmítne při stavbě session factory. Práce podle rozhodnutí [012](./decisions/012-foreign-key-rendering.md). Vyvrací nárok na F3 v [`architecture.md`](./architecture.md), §9, pro cíl NHibernate. Požadavky F2, F3, F10.*

NHibernate builder píše `<key>` kolekce ze sloupců, které nese vztah kolekce sám (`ColumnPairs`), pak ze sloupců, které zdroj u kolekce uvedl, a když nemá ani jedno, sáhne po sloupci klíče vlastníka — a to po **prvním** z nich (`AppendKey` v `NHibernateEntityBuilder`). Kolekce, kterou zdroj vyslovil jako inverzní stranu bez vlastních sloupců — JPA `@OneToMany(mappedBy)` i kolekce EF Core, kterou páruje konvence nebo `[InverseProperty]` —, ale takové sloupce nemá nikdy: nese je vlastnící N:1 protistrana, kterou builder v témž převodu najde, když rozhoduje o `inverse="true"`. Nad jednoduchým klíčem vyjde výstup správně jen tehdy, když se cizí klíč jmenuje stejně jako klíč vlastníka; nad složeným klíčem vyjde špatně vždycky. EF Core entita `Order` s klíčem `(CompanyId, OrderId)` a kolekcí `Lines`, jejíž `OrderLine` nese `[ForeignKey("CompanyId,OrderId")]`, vydá správný `<many-to-one>` se dvěma sloupci, ale `<bag name="Lines">` s `<key column="CompanyId" />`, a NHibernate takové mapování odmítne výjimkou `FKUnmatchingColumnsException` — cizí klíč musí mít tolik sloupců jako primární klíč, na který míří. Záznam `Convention`, který náhradní sloupec doprovází, tvrdí konvenci tam, kde vzniká nepoužitelný artefakt. Zelená sada tu cestu nepotkává: důkazy F3 v [`traceability.md`](./traceability.md) ověřují 1:N převodem z Dapperu a `<key-many-to-one>`, ne inverzní kolekci, kterou zdroj vyslovil bez sloupců.

Oprava nic nevolí: `<key>` inverzní kolekce má vzít zdrojové sloupce párů vlastnící protistrany v jejich pořadí — tutéž protistranu builder už čte kvůli `inverse="true"` —, a náhradní cesta nad složeným klíčem vlastníka má vypsat všechny jeho části, ne první. K opravě patří test převodu EF Core → NHibernate i JPA → NHibernate, který mapování předloží NHibernate stejně jako ostatní testy 3. stupně.

#### Číselná verze se v EF Core vypíše jako `[Timestamp]` a přestane verzovat
*Vyšlo najevo 2026-09-22 při stavbě příkladů výkladové stránky (rozhodnutí [099](./decisions/099-examples-are-content-not-a-choice.md)); ověřeno modelem, který EF Core 10.0.10 nad generovanou entitou postaví. Souvisí s rozhodnutím [030](./decisions/030-scope-of-version-1-0.md), které sloupec verze zavedlo jako mapovací fakt, a s [004](./decisions/004-unexpressible-facts-as-warnings.md), které náhražky zakazuje. Vyvrací nárok na F11 v [`architecture.md`](./architecture.md), §9: artefakt tiše mění význam vysloveného faktu. Požadavky F5, F10, F11.*

Sloupec verze nese mezireprezentace jako příznak `IsVersion` bez ohledu na typ a EF Core builder ho vypisuje vždy anotací `[Timestamp]`. U binární rodiny — sloupce rowversion, který plní databáze — je to správně. U číselné verze, kterou si JPA (`@Version int`) i NHibernate (`<version>` nad `Int32`) zvyšují samy, ne: EF Core podle `[Timestamp]` pokládá hodnotu za generovanou databází při vložení i změně — model, který nad generovanou entitou postaví, nese u vlastnosti `ValueGenerated.OnAddOrUpdate` nad sloupcem typu `int` —, takže ji do zápisu neposílá a po uložení ji jen čte zpátky. Sloupec `int` ale nikdo nevyrobí: podle toho modelu skončí vložení do sloupce `NOT NULL` bez výchozí hodnoty chybou databáze a při změnách zůstane hodnota pořád stejná, takže optimistické zamykání, kvůli kterému zdroj verzi vyslovil, přestane chránit; proti databázi to spuštěné není. Artefakt se přitom přeloží, model se postaví a převod nevydá žádný záznam — tichá náhražka, kterou rozhodnutí 004 zakazuje. Týká se to každého směru do EF Core ze zdroje s číselnou verzí, tedy i NHibernate → EF Core uvnitř .NET, a sada to nepozná: do EF Core míří testy sloupce verze jen s binární rodinou (`Combined/VersionColumnTest`) a jeho 3. stupeň (`Verification/VersionColumnVerificationTest`) ověřuje směr do NHibernate.

Nejbližší věrný tvar, který anotace EF Core mají, je `[ConcurrencyCheck]`: hodnota se při zápisu porovná, ale zvyšovat ji musí aplikace, protože inkrementaci anotace EF Core nevyjádří — a to je ztráta, kterou převod podle rozhodnutí 004 ohlásí záznamem `Loss`. Binární verze zůstává u `[Timestamp]`.

## Advisor

Lepší Advisor a benchmarking a na nich stojící experimentální požadavky T1–T7; T7 navazuje na existující ILP Advisor. Advisor s benchmarkingem jsou ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) a jsou zároveň **příštím milníkem zadání**, tedy látkou na `3.0.0` (rozhodnutí [098](./decisions/098-the-number-is-decided-once-per-release.md)).

Celá oblast byla vědomě odložená, dokud běžely cíle 1 a 2, a ta podmínka vydáním `2.0.0` vypršela: je to příští milník zadání a nic jiného už před ním nestojí. Čím se uvnitř něj začne, je ale volba, která zatím nepadla, a do té doby položky značku pořadí nenesou.

### Rozhodnutí

#### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1, a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), které hranici pro javovou větev už vyslovilo. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Pro javovou větev je hranice daná: rozhodnutí 076 drží JVM v kontejneru a javová větev Advisoru — implementace `IBenchmarkExecutor` pro javové frameworky, které F15 žádá měřit — běží mimo proces aplikace, buď jako `java` spuštěná v kontejneru aplikace, nebo jako samostatná služba runneru. Tím se otázka nezmenšuje, ale zostřuje: .NET měřený in-process a Java mimo proces se musí dát srovnat, takže volba hranice pro .NET větev a metodologie měření se rozhodují spolu. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

#### Parametrizovaný dotaz Advisor nezměří
*Vyplynulo z rozhodnutí [083](./decisions/083-parameter-as-the-fifth-operand-shape.md), které dalo generované metodě parametry za dosavadní první argument. Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F15, T4, T7.*

Harness benchmarkingu hledá v přeloženém sestavení veřejnou metodu s **právě jedním** parametrem typu `DbContext` (`EFCoreBenchmarkHarnessBuilder`, viz [`architecture.md`](./architecture.md) §8). Metoda parametrizovaného dotazu má za kontextem ještě parametry dotazu, takže ji harness přeskočí a celý běh skončí hláškou „EF Core query method not found", ačkoli překlad proběhl a artefakt je v odpovědi. Rozšířit hledání je práce na pět minut; otázka, která z toho dělá rozhodnutí, je **čím se parametr při měření naplní**. Hodnota určuje selektivitu dotazu, a tedy i naměřený čas i alokaci, takže volba hodnoty je volba o tom, co se vlastně měří: výchozí hodnota typu měří prázdný výsledek, hodnota od volajícího vyžaduje nové pole v požadavku běhu, a hodnota odvozená z katalogu (třeba medián sloupce) váže měření na data, která v cílové databázi být nemusejí. Rozhodnutí musí vyslovit, která z cest platí, a co se stane s dotazem, jehož parametr se naplnit nedá — jestli z běhu vypadne se záznamem, nebo běh odmítne. Souvisí s položkou o nedoplněném překladu níž, protože obě jsou o tom, co přesně Advisor měří.

#### Advisor měří nedoplněný překlad
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Sem odkázalo rozhodnutí [059](./decisions/059-advisor-response-carries-the-measured-translations.md), které svou variantu 3 zamítlo jen pro teď. Souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F15, T7.*

Překladová fáze `/advisor/run` volá `ConversionHandler.Convert` bez připojovacího řetězce, takže benchmark kompiluje a měří překlad bez katalogového doplnění — kdežto `/convert` tentýž vstup doplní a uživatel by nasadil doplněnou verzi. Čísla Advisoru tedy platí o jiném kódu, než jaký si uživatel z nástroje odnese. Od rozhodnutí 059 je to aspoň vidět: odpověď nese měřené artefakty a jejich stav říká, že katalog nebyl použit. Rozhodnout je třeba, jestli má překladová fáze dostat tutéž cachovanou čtečku jako fáze benchmarková — technicky je to po zavedení `CachingCatalogReader` levné, jedna dávka na framework — a co to udělá s naměřenými čísly: doplněné entity nesou jiné atributy a vztahy, takže se mění kompilovaný harness, a změna metodologie měření se musí přeměřit, ne jen zapnout. K témuž rozhodnutí patří i agregace `CatalogReadTime` přes převody běhu, má-li se o katalogové ceně běhu Advisoru něco tvrdit: každý převod svou fázi měří (`architecture.md`, §5.2), ale dokud překladová fáze čtečku nedostane, je ten čas u všech převodů běhu null — součet by tvrdil nulu, která není měřením.

#### Iterační politika benchmarku je konstanta v kódu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavek T7.*

`BenchmarkExecutor` měří každý pár (dotaz × framework) pevným postupem: dvě zahřívací iterace, pilotní běh a z něj odvozených 3–20 měřených iterací s cílem ~500 ms celkem. Konstanty jsou zapsané v kódu bez odůvodnění a bez možnosti je ovlivnit z rozhraní, přitom právě ony určují rozptyl a délku běhu, o které v T7 jde; nadbytečná náhledová invokace — celé jedno provedení dotazu jen kvůli ladicímu výpisu — už je zrušená. Rozhodnout je třeba, jestli jsou tyhle hodnoty součástí metodologie, kterou text práce vysloví a odůvodní, nebo parametrem požadavku, a čím se volba podloží; měnit je bez rozhodnutí znamená měnit význam všech dosavadních čísel.

#### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient`, který k nim teče přes `benchmarks/Common`. Pro srovnání výkonu je to metodologický confound. **Rozsah je nově dohledaný celý** — u PetaPoco vyloučením, protože `Microsoft.Data.SqlClient` v grafu balíků obou jeho projektů není, u EF6 z `WWIDbConfiguration`; podrobnosti nese [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Zbývá tedy volba, ne zjišťování: buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` (samostatný balík, dnes nereferencovaný) a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

### Práce

#### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

#### Hláška o neřešitelném ILP modelu dorazí do logu až s dalším voláním
*Nalezeno při ověření (2026-08-24); popis v [`architecture.md`](./architecture.md), §8, je podle toho opravený. Vyňatá oblast 1 hranice záruk (§9). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavky T7, S6.*

`solve_problem()` v `Advisor/ilp.c` vypisuje `No feasible solution found.` obyčejným `printf`. Standardní výstup je v kontejneru přesměrovaný na rouru, tedy plně bufferovaný, a nikdo ten buffer nevyprazdňuje. Hláška se do logu **dostane**, ale teprve až ji protlačí výstup dalšího volání: tři neřešitelné úlohy za sebou vydaly dvě hlášky, každou o jeden běh opožděnou. Vlastní výpis GLPK dorazí včas, protože nejde přes `stdio`, takže v logu stojí `PROBLEM HAS NO PRIMAL FEASIBLE SOLUTION` bez naší věty vedle sebe.

Oprava je jednořádková — `fflush(stdout)` za tím výpisem, případně řádkové bufferování při inicializaci knihovny —, zadarmo ale není: `libadvisor.so` se překládá jedině v Docker buildu, takže změnu je nutné přeložit a ověřit v kontejneru, a sahá se přitom do oblasti bez jediného testu, kterou vyjímáme ze záruk vcelku. Dokud se to nestane, drží ten stav §8 svým popisem, aby nikdo nehledal hlášku, která po jeho volání v logu ještě není. Návratový kód ani tělo odpovědi to nijak nemění — neřešitelnou úlohu pozná volající z **400**, respektive ze `status: -1`, přesně jak §8 popisuje a jak jsme ověřili.

#### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Souvisí s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), podle kterého je Advisor kontejnerový rozhodnutím a build nativní knihovny pro Windows nevzniká. Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so` a `AdvisorRunHandler` výjimku z P/Invoke zachytí a vrátí její text, takže uživatel se o nedostupnosti dozví jako o `DllNotFoundException` — po vyplnění celého formuláře a po odeslání běhu. Úvodní odstavec obrazovky přitom říká dopředu, že Advisor potřebuje kontejner; nedostupnost se tedy sděluje dvakrát, jednou naší větou předem a jednou hláškou zavaděče potom.

Aby to obrazovka mohla říct **místo** běhu a vlastními slovy, potřebuje se serveru zeptat, jestli je Advisor na tomhle hostiteli k dispozici — dnes na to není koncový bod a klient si to odvodit nemůže. Je to tedy nový koncový bod; sám o sobě je malý, ale předchází mu volba, jestli do vyňaté oblasti sahat dřív, než se dodělá rozdělané.

## Rozhraní

Zásahy do rozhraní jsme odložili stranou všeho ostatního. Z F14 je hotový vícesouborový vstup a výstup po souborech; zbytek bloku F14–F15 — dávkové vstupy a zobrazení mezireprezentace — je tady a optimalizační půlka F15 patří [Advisoru](#advisor).

### Rozhodnutí

#### Směr překladu jako jedna věc a vstup vedle výstupu
*Navazuje na rozhodnutí [099](./decisions/099-examples-are-content-not-a-choice.md), které pětikrokový tvar obrazovky převzalo beze změny z [033](./decisions/033-shape-of-the-static-frontend-screens.md); je podle něj napsaný kód, takže případnou změnu je třeba nahradit, ne revidovat. Souvisí s [032](./decisions/032-frontend-as-static-pages-without-a-build.md) a s rozhodnutím [066](./decisions/066-records-attributed-to-the-input-unit.md). Požadavky F14, S7.*

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných sekcí viditelných najednou a první dvě z nich jsou volba zdroje a volba cíle. Důsledek je, že směr překladu na obrazovce nikde nestojí jako jedna věc: „EF Core → NHibernate" se poprvé objeví až v hlavičce výsledku a prohodit obě volby jde jen ručně, dvěma zásahy do dvou rozbalovacích seznamů. Jedna řádka směru s tlačítkem pro prohození by z pěti sekcí udělala čtyři, což S7 nebrání — „nejvýš pět kroků" je strop, ne kvóta —, ale je to změna volby, kterou 033 vyslovilo výslovně a 099 převzalo, takže patří do nového rozhodnutí.

Do téhož rozhodnutí patří druhá otázka, protože obě mění tvar téže obrazovky a navrhovat je zvlášť by znamenalo navrhnout ji dvakrát: **jestli má vstup stát vedle výstupu.** Dnes jsou vstupní jednotky nahoře jako textová pole a artefakty dole jako panely, takže se zdroj a výsledek nedají číst současně — na výkladové stránce vedle sebe stojí, na nástrojové ne. Podstatná je tu poctivost, ne rozvržení: server neříká, který artefakt vznikl ze které jednotky (§9, zúžení F14), takže sloupce vedle sebe se buď musí spárovat toutéž jmennou heuristikou, jakou se artefakty pojmenovávají, a jako heuristika se i označit, nebo nesmí tvrdit párování vůbec a nesou pak nadpisy typu „co jste poslali" a „co přišlo zpět". Druhá cesta nic nevymýšlí. Půlka předpokladů skutečného párování už stojí: jednotky od rozhodnutí [066](./decisions/066-records-attributed-to-the-input-unit.md) nesou jméno a záznamy na ně ukazují; co dál chybí, je druhá půlka — aby artefakt výstupu nesl, ze které jednotky (přesněji: z které entity a jejích jednotek) vznikl.

#### Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje
*Zúžení, které dnes vyslovuje [`architecture.md`](./architecture.md), §9 („zobrazení IR verze nenárokuje vůbec"). Souvisí s rozhodnutími [010](./decisions/010-diagnostics-as-returned-data.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md) a [098](./decisions/098-the-number-is-decided-once-per-release.md). Požadavky F11, F14.*

Požadavek F14 žádá zobrazení čtyř věcí — vstupu, mezireprezentace, výstupu a diagnostiky — a nárokujeme tři: `/convert` mezireprezentaci nevrací a rozhraní ji nemá odkud vzít. Je to jediné místo, kde se dnes nárok na F14 zužuje z důvodu, který leží na serveru, ne na obrazovce, a zároveň to nejlépe placené místo pro text práce: pipeline parse → doplnění → build se čtenáři, který nástroj nikdy nespustí, ukazuje právě prostředním článkem.

Rozhodnout je třeba dřív, než se cokoli začne psát, protože cena není ve vykreslení: serializovaný tvar `EntityMap`, klíče, vztahů a dotazových instrukcí by se stal součástí REST kontraktu se vším, co to znamená pro verzování (rozhodnutí [098](./decisions/098-the-number-is-decided-once-per-release.md)). Otázka tedy zní, jestli se mezireprezentace vydává jako plnohodnotná část odpovědi, nebo jako výslovně nestabilní náhled, u kterého se dopředu řekne, že se může měnit mezi vydáními — a druhá odpověď je levnější jen zdánlivě, protože nestabilní část kontraktu je pořád část kontraktu.

### Práce

#### Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška
*Souvisí s rozhodnutím [033](./decisions/033-shape-of-the-static-frontend-screens.md), které validaci XML s číslem řádku zavedlo, a s [032](./decisions/032-frontend-as-static-pages-without-a-build.md), bod f (žádná další vendorovaná knihovna bez rozhodnutí). Požadavek S7.*

Validace před odesláním hlásí u nesprávně utvořeného XML číslo řádku a serverová hláška u SQL nese řádek a sloupec z `TSql160Parser`. Editor jednotky je ale holý `<textarea>` bez číslování, takže „řádek 7" se v něm hledá počítáním. Doslovné znění S7 mluví o zvýraznění chyb na úrovni souboru a řádku a tohle je jeho druhá půlka, která chybí — první, tedy chyba přiřazená ke konkrétní jednotce, hotová je.

Práce je to hotová v zadání, ne v rozvaze: postranní sloupec s čísly řádků, který se posouvá spolu s textovým polem, je několik desítek řádků vlastního kódu a chová se spolehlivě. Co je potřeba nedělat, je sáhnout po hotovém editoru — CodeMirror nebo cokoli podobného by byla třetí vendorovaná knihovna, a to je podle bodu 032f samostatné rozhodnutí, ne detail implementace.

## Užitečné, ne nutné

Co by nástroji nebo repozitáři prospělo, ale nic to nenárokuje: žádná věta záruk na tom nestojí a žádná jiná položka tím není blokovaná. Odbavit se to dá kdykoli — nebo nikdy. Značky pořadí tyhle položky nedostávají.

### Rozhodnutí

#### Druhý databázový dialekt
*Vyplynulo z rozhodnutí [086](./decisions/086-target-database-dialect-declared-by-the-descriptor.md), které deklaraci zavedlo a slovník otevřelo s jedinou hodnotou. Jednu ze tří otázek odbavilo rozhodnutí [088](./decisions/088-a-declared-foreign-source-dialect-is-not-read.md). Souvisí s rozhodnutím [082](./decisions/082-t-sql-read-and-written-by-a-shared-project.md), které multidialektovou knihovnu zamítlo. Vyňatá oblast 5 hranice záruk ([`architecture.md`](./architecture.md), §9). Značku pořadí nemá a nedostane: žádná záruka na ní nestojí a žádnou jinou položku neblokuje. Požadavky F5, F7–F10, S2.*

Od 2026-09-21 nástroj říká, pro jaký databázový systém artefakty píše, a je to SQL Server 2022. Druhý systém do slovníku přidat lze, ale zapsat hodnotu nestačí a odpovědět bylo potřeba trojí. **Druhá z těch tří otázek je od 2026-09-21 zodpovězená** rozhodnutím 088 a zbývají dvě.

**Čím se dialekt volí.** Deskriptor dnes nese jedinou hodnotu a volba z rozhraní neexistuje — je to táž otevřená věta, jakou rozhodnutí [013](./decisions/013-target-framework-versions.md) nechalo u verze frameworku, a obě se pravděpodobně zodpoví spolu, protože obojí je fakt o cíli převodu a obojí by se muselo dostat do požadavku, do deskriptoru a do záznamu běhu.

**Jak se deklaruje dialekt zdrojového artefaktu — zodpovězeno 2026-09-21** rozhodnutím [088](./decisions/088-a-declared-foreign-source-dialect-is-not-read.md): zdroj smí dialekt svého doslovného SQL deklarovat v požadavku převodu a deklarace jiného systému, než který tahle verze čte, čtení **zastaví** — dotaz se nevydá (`Failure`), doslovný typ sloupce se nepřečte (`Loss`). Neplyne z toho, že by se cizí dialekt překládal; plyne z toho, že se neuhodne. Rozhodnutí u toho opravilo předpoklad, se kterým tahle položka vznikla: věta, že SQL psané pro jiný systém gramatikou neprojde, platí pro cizí **syntaxi** (`LIMIT 10`, `||`) a pro **neznámá jména** (`VARCHAR2(50)`), kdežto jméno legální v obou dialektech s jiným významem projde tiše a špatně — `timestamp` je v T-SQL osm bajtů binárních dat, jinde okamžik v čase, a `SUBSTR` je pro `TSql160Parser` obyčejné volání funkce. Práce z toho je od 2026-09-21 hotová — zábrana sedí v `SqlQueryReader` a v `SqlTypeSpelling.Read`, deklaraci nese požadavek převodu i překladová obrazovka a záznam běhu ji vydává ([`architecture.md`](./architecture.md), §5 a §5.1).

**Jestli se tím mění zamítnutí multidialektové knihovny.** Rozhodnutí 082 ji zamítlo s odůvodněním, že jiný dialekt by se přeložil **tiše**. Deklarace je právě ta věc, která tichost odstraňuje, takže argument sám o sobě už neplatí a otázka se otevírá znovu — nikoli ale automaticky ve prospěch knihovny: cena je pořád celá gramatika navíc a druhá tabulka jmen, a užitek je nulový, dokud v repozitáři není druhý databázový systém, proti kterému by to šlo ověřit. To byl i druhý důvod rozhodnutí 019 dialekt tehdy nezavádět. Rozhodnutí 088 přitom tuhle otázku nebere: zábrana cizí dialekt odmítá, nečte ho, takže knihovnu nepotřebuje ani nevylučuje.

**Co se změní v den, kdy budou čitelné dialekty dva.** Deklarace zdroje, kterou zavedlo rozhodnutí 088, se ze **zábrany** stane **přepínač** — hodnota `AnotherSystem` se rozpadne na pojmenované systémy a totéž pole začne vybírat gramatiku a tabulku jmen. Tvar se nemění, roste jen slovník. Teprve tehdy vznikne otázka, která se dnes položit nedá, protože čitelný i zapisovatelný dialekt je jeden: **co nástroj dělá, když se dialekt zdroje s cílovým rozejde.** Překlad dialektu je vlastní volba a patří sem, ne do 088.

#### Celý vložený zdroj si nástroj nerozdělí na mapovací a dotazovou část
*Souvisí s rozhodnutími [025](./decisions/025-query-language-as-content-type.md) a [047](./decisions/047-content-type-reaches-the-query-parser.md), která jazyk jednotky svěřila deklaraci klienta, a hlavně s [081](./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md), které roli jednotky přesunulo z deklarace na nárok parserů zdrojového frameworku. Míří proti vyslovenému necíli v [`use-cases.md`](./use-cases.md) („nástroj nepozná dotaz uvnitř service třídy"). Požadavky F11, F14, S1, S2, S7.*

Uživatel má v ruce soubor, ne jednotku. Typicky je v něm entitní třída a hned pod ní repozitář s `connection.Query<Customer>("select …")` nebo s LINQ řetězem, v Javě entita a vedle ní metoda s `createQuery`. Dnes ten text musí rozřezat sám: jednotka deklaruje typ obsahu a u C# i u Javy ta hodnota nese vedle jazyka i roli (`CSharpEntity` proti `CSharpQuery`, `JavaEntity` proti `JavaQuery`), takže „vlož, co máš" znamená „vlož zvlášť entitu a zvlášť dotaz". Otázka zní, jestli rozřezání má umět nástroj.

Celý vložený soubor dnes nekončí chybou, nýbrž nesmyslem, a je to tiché. Sdílený C# parser — a stejně tak javový — bere **každou deklaraci třídy v jednotce** jako entitu převodu; je to vědomé pravidlo, na kterém stojí vícetřídní vstup F14 ([`architecture.md`](./architecture.md), §5), takže z repozitáře vznikne mapa entity `CustomerRepository` a z ní artefakt, prázdná třída pojmenovaná po něm. Dotaz v jeho metodě nepřečte nikdo: orchestrace od rozhodnutí 081 nabízí jednotku oběma průchodům, jenže `LinqQueryParser` i `DapperSqlQueryParser` si nárokují `CSharpQuery`, ne `CSharpEntity`, takže dotazový průchod jednotku minul. Záznam o tom nevznikne — jednotka byla nárokovaná a něco vydala, takže není ani nenárokovaná, ani jalová (rozhodnutí [066](./decisions/066-records-attributed-to-the-input-unit.md)).

Rozhodnout je třeba trojí. **Kde rozdělení bydlí.** V klientovi ho rozhodnutí 081 už jednou zamítlo: rozřezávací kód v JavaScriptu leží mimo testovací sadu a záznamy by ukazovaly na fragmenty vyrobené klientem, ne na soubor, který má uživatel v ruce. Na serveru zbývá říct, kdo dělí — rozpoznat entitu od repozitáře je tvrzení o zdrojovém frameworku, takže podle S1 patří do wrapperu, ne do orchestrace, a je to táž věta, jakou vyslovilo 081 („hodnota jmenuje jazyk, framework si jmenuje roli"). Mechanika k tomu existuje celá; chybí jen to, aby si tutéž hodnotu nárokovaly obě strany i u C# a Javy, jak to dnes dělá jedině `JavaQuery` u MyBatisu (rozhodnutí [084](./decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md)).

**Podle čeho se role pozná.** Tady je cena celé položky. Věta „třída, která nese volání dotazu, není entita" je tvrzení o tvaru textu, a tvar textu jsme k rozhodování dvakrát odmítli pustit — v 025 u volby jazyka a v 047 u výběru dotazového parseru —, pokaždé proto, že hádání je nedeterminismus zakázaný S2. Rozdíl je v tom, že jazyk se tu nehádá, deklaruje ho klient dál, a hádá se role uvnitř souboru; tu vyslovit pravidlem lze, ale pravidlo musí být zapsané a musí se hlásit. Co se přečetlo jako entita, co jako dotaz a co se přeskočilo, patří do záznamů — jinak je špatné rozdělení přesně ten tiše špatný výstup, jaký nástroj jinde odmítá (rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md) a [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md)).

**Kam až ambice sahá.** [`use-cases.md`](./use-cases.md) dnes říká, že vyhledání artefaktů v projektu je práce uživatele a že nástroj „nepozná dotaz uvnitř service třídy". Rozhodnutí tu větu buď posune — jeden vložený soubor ano, procházení repozitáře ne —, nebo ji potvrdí; posunout ji lze, zmrazený ten dokument není, ale musí se to udělat vědomě a v tomtéž rozhodnutí. S dávkovým vstupem, který F14 jmenuje („archiv projektu"), to nesplývá: archiv je víc souborů, z nichž každý má dál jednu roli, kdežto tady jde o jeden soubor, jehož role jedna není.

Proč je položka tady, a ne mezi příštími: nenárokujeme ji nikde — ani §9, ani `use-cases.md` neslibují, že nástroj vstup rozřeže — a obejít se dá tím, co obrazovka umí dnes, tedy jednou jednotkou na roli. Užitek je v S7 a ve větě F14 o vkládání celých tříd: uživatel má v ruce nejčastěji právě ten celý soubor.

#### Vynucení stylu a reprodukovatelnost sestavení
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.4. Souvisí s rozhodnutími [034](./decisions/034-central-version-management.md) a [039](./decisions/039-container-configuration-of-the-environment.md). Požadavky S2, S5.*

„Reprodukovatelné prostředí" dnes znamená „jedním příkazem", ne „bajtově stejně": soubor zámku závislostí neexistuje, základní obrazy kontejnerů jsou připnuté na pohyblivé značky a pravidla stylu, která v repozitáři jsou, build nevynucuje. Rozhodnout je třeba, jestli se nárok S2 rozšiřuje z výstupu překladu i na sestavení samo — zámek závislostí, obrazy podle digestu, styl vynucený v CI — a jestli je to tvrzení, které text práce potřebuje, nebo údržba, která počká; dokud volba nepadne, platí dnešní užší čtení a nic víc se netvrdí. Táž otázka se týká i akcí v CI: `actions/checkout@v5` a spol. visí na pohyblivé značce, ne na digestu, takže co workflow spustí, se může změnit bez zásahu do repozitáře.

#### Trvalý identifikátor vydání
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.1, a [pět doporučení fair-software.eu](https://fair-software.eu/). Souvisí s rozhodnutím [098](./decisions/098-the-number-is-decided-once-per-release.md), jehož vysloveným předpokladem je, že nástroj není publikovaný. Sahá se na to až úplně na konci vývoje; značku pořadí do té doby nedostává.*

Kořenový [`README.md`](../README.md) žádá citovat verzi a `CITATION.cff` k tomu nese metadata, jenže vydání nemá trvalý identifikátor: jediným nositelem je značka v gitu, která existuje, dokud existuje repozitář. Vnější praxe pro výzkumný software — pět doporučení fair-software.eu, tedy veřejný repozitář, licence, záznam v registru, citovatelnost a kontrolní seznam kvality — má tady čtyři body z pěti a chybí právě ten registr.

Rozhodnout je třeba dvojí. **Jestli se fork cizího prototypu archivuje pod vlastním identifikátorem**, a pokud ano, kde a s jakým autorstvím; `LICENSE` nese dva držitele autorských práv právě proto, že repozitář je napůl zděděný. A **jestli záznam v registru znamená, že nástroj je publikovaný** ve smyslu předpokladu rozhodnutí 098: to rozhodnutí se má podle vlastní věty nahradit, ne dovysvětlit, jakmile předpoklad přestane platit, a archiv s identifikátorem je té hranici blízko, byť konzumenta nevyrábí.

Do té doby stojí citace na značce a na `CITATION.cff`, a je to vědomé: identifikátor se razí z vydání, takže se přidá až docela nakonec, ne uprostřed vývoje.

#### Join po asociační cestě jde odvodit ze vztahu, a neodvozuje se
*Vyslovila to revize [2026-09-21](./audits/2026-09-21-pre-release-2-0-0-audit.md), nález 6.2, a pojmenovalo rozhodnutí [096](./decisions/096-a-rule-of-the-paper-is-cited-where-it-argues.md). Souvisí s rozhodnutím [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md), které cestu nechává odmítat, a s [001](./decisions/001-entity-reference-by-name.md). Značku pořadí nemá: hranice je vyslovená v [`architecture.md`](./architecture.md), §5, takže žádná věta není nepravdivá. Požadavky F8, F9, F11, T2.*

Pravidlo **Q7** článku odvozuje podmínku implicitního joinu z metadat vztahu — `FK(levá) = PK(pravá)` —, a je to jediné z pětadvaceti pravidel, které nástroj nesplňuje. `from Customer c join c.orders o` v HQL i v JPQL končí od rozhodnutí 070 záznamem `Failure`, protože čtečka cestu nepřečte; odmítnutí je správné potud, že dotaz bez joinu vrací jiné řádky, ale přečíst se ta cesta **dá**. Mezireprezentace nese všechno, co je k tomu třeba: `Relation` na `EntityMap`, `ColumnPairs` s uspořádanými dvojicemi sloupců i pro kompozitní klíč, a `Role`, která říká, která strana nese fyzický cizí klíč.

Rozhodnout je třeba dvojí. **Odkud se vztah vezme, když ho zdroj nevyslovil** — `c.orders` je jméno vlastnosti, ne tabulky, takže se musí spárovat s `Relation` cílové entity, a ta v převodu být nemusí; převod jediné dotazové jednotky bez entit ji nemá vůbec. A **co se stane, když se vztah najde, ale `ColumnPairs` jsou prázdné**, protože je nikdo nedoplnil ani z katalogu, ani z druhé entity převodu: dohadovat jméno sloupce by bylo přesně to hádání, které rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) váže na vyslovené tvrzení. Obě odpovědi musí platit pro HQL i JPQL zároveň, protože cesta je v obou týmž tvarem.

#### Sdílená entitní báze roste a rozšiřovací plocha ne
*Podklad: revize [2026-09-21](./audits/2026-09-21-pre-release-2-0-0-audit.md), nález 5.4. Souvisí s invariantem S1 a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md). Značku pořadí nemá a nedostane: invariant dnes platí a žádná položka na tom nestojí. Požadavek S1.*

Invariant „nový framework je nový wrapper" dnes **platí** a revize ho ověřila třemi způsoby: jméno frameworku neprosakuje do `AbstractWrappers`, `Common` ani `Model`, `ConversionHandler` ho nejmenuje ani jednou a jedinými místy, která je jmenují, jsou čtyři továrny v `OrmConvertor/Factories/`. Nový wrapper je přitom opravdu levný — `HibernateWrappers` má 185 řádků a `EclipseLinkWrappers` 226 nad sdílenou vrstvou o 5 251 řádcích.

Posunul se ale poměr. Od značky `1.2.0` vyrostl `AbstractEntityBuilder` z 2 137 na 2 728 řádků (+27,7 %) a počet jeho `virtual`/`abstract` členů zůstal na devíti, z nichž sedm jsou kroky šablonové metody s pevným počtem — volné zásuvné body jsou tedy fakticky dva. `AbstractQueryBuilder` vyrostl víc (651 → 1 475), ale jeho plocha rostla s ním (12 → 16). Framework, jehož potřeba se do dvou volných háčků netrefí, nemá kam jinam než do těla báze.

Není to překážka ničeho a odbavit se to dá kdykoli — nebo nikdy. Rozmyslet je třeba, jestli se ten poměr má **měřit** (dvě čísla u každého vydání jsou levná), jestli se má entitní báze rozdělit tak, jak je rozdělená vrstva JPA, a co by vlastně bylo prahem: sedmý framework, nebo zásah do těla báze kvůli jedinému z nich.

### Práce

#### Atributy ostatních prvků NHibernate mapování mizí dál beze slova
*Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md) a [004](./decisions/004-unexpressible-facts-as-warnings.md), táž látka jako u atributů `<class>` a `<property>`, kterou jsme odbavili 2026-09-21. Značku pořadí nemá: hranice je vyslovená v [`architecture.md`](./architecture.md), §5, takže žádná věta není nepravdivá a nic tím není blokované. Požadavek F11.*

Seznamy hlášených atributů jsou jmenné a prošly jimi dva prvky, `<class>` a `<property>`. Atributy zbylých prvků se dál přeskakují beze slova: `access` a `unsaved-value` na `<id>`, k nim `generated`, `insert` a `source` na `<version>`, a u vztahových prvků `cascade`, `fetch`, `lazy`, `not-found` a `formula` na `<many-to-one>` a `<one-to-one>` — z celé té skupiny má dnes záznam jediný `property-ref`. Mimo jmenný seznam zůstávají i ostatní atributy `<class>` (`catalog`, `proxy`, `persister`, `entity-name`, `subselect`, `abstract`, `rowid`, `polymorphism`, `select-before-update`, `check`).

Práce to není mechanická a proto nešla s předchozí položkou. Každý prvek potřebuje vlastní úvahu o tom, co ztráta stojí — `generated` na `<version>` si builder odvodí z binární typové rodiny sám, takže ztráta to není —, a hlavně: `insert="false" update="false"` na `<many-to-one>` **vypisuje náš vlastní builder** u ploché části kompozitního klíče a u syntetizované junction entity (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md) a [006](./decisions/006-flat-composite-key-rendering.md)), takže paušální hlášení by z převodu NHibernate → NHibernate udělalo hlásiče ztrát nad vlastním výstupem. Je třeba oddělit atribut, který zdroj tvrdí, od atributu, který je odvozený — což je kritérium rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) z druhé strany.

#### Skalární poddotaz bez `Where` si v LINQ nevezme alias ze své lambdy
*Vyšlo najevo 2026-09-22 na knize objednávek výkladové stránky (rozhodnutí [099](./decisions/099-examples-are-content-not-a-choice.md)). Souvisí s rozhodnutím [061](./decisions/061-subquery-as-a-condition-operand.md). Značku pořadí nemá: množina řádků se nemění a žádná věta záruk není nepravdivá. Požadavky F7–F10.*

Sdílený LINQ parser bere alias vnořeného rozsahu z parametru první lambdy jeho řetězu a záložně z prvního písmene tabulky (`FirstElementLambdaParameter` v `LinqQueryParser`) — pravidlo, které vzniklo právě proto, aby si poddotaz nad touž tabulkou nepřivlastnil vnější alias (rozhodnutí 061). Koncový agregát bez předchozího kroku, `ctx.Products.Average(x => x.ListPrice)`, ale takovou lambdu v řetězu nemá: parametr `x` stojí v agregátu samotném, který se mezi kroky nepočítá. Poddotaz proto dostane záložní `p`, tedy tentýž alias jako vnější `ctx.Products.Where(p => …)`, a výstup píše `p.ListPrice > (select avg(p.ListPrice) from Product p)`.

Řádky se tím nemění. Takový poddotaz je vždy nekorelovaný — korelaci by nesl predikát a ten přidává do řetězu krok `Where` s vlastní lambdou —, vnitřní alias vnější jen zastíní, NHibernate 5.7.0 takové HQL přeloží (ověřeno sestavením dotazu nad generovaným mapováním), T-SQL zastínění připouští a EF Core builder vnitřní parametr při kolizi přejmenuje (`p1 => p1.ListPrice`); nad Hibernate ani EclipseLinkem spuštěné JPQL není. Ztrácí se jen jméno, které poddotazu dal zdroj, a výstup se hůř čte. Oprava je malá: tam, kde řetěz poddotazu žádný krok s lambdou nemá, vzít za alias parametr lambdy koncového agregátu.

## Zbytky

Zdokumentované mezery a nezodpovězené otázky, na které se nesahá. Jsou tu **zapsané, ne zařazené**: značky pořadí nedostávají a dojít na ně může kdykoli — nebo vůbec. Zapisujeme je proto, aby nález nezůstal jen v konverzaci a aby text práce věděl, co nástroj o svých frameworcích netvrdí.

Zdroje jsou tři. **Revize celého repozitáře** — vlastní soubor v [`audits/`](./audits/README.md) nemá, protože se její nálezy rozepsaly rovnou do položek; zapsaná je v kapitole 8 revize [2026-09-21](./audits/2026-09-21-pre-release-2-0-0-audit.md), která ji **nenahrazuje**, jen jí dává místo, kam ukazovat. Co z ní byla oprava, leží v kódu a v [`architecture.md`](./architecture.md) — sdílená čtečka T-SQL odmítá příkaz stojící vedle překládaného `SELECT`u, klauzuli `WITH`, `INTO`, `FOR XML`/`FOR JSON` a `TABLESAMPLE` a hlásí nápovědy ztrátou; sdílený LINQ parser jmenuje kroky, které mění množinu řádků, a čte zpět `g.Key`; T-SQL visitor vypisuje `COUNT(*)` bez aliasu. Ze čtyř otázek, které po opravách zbyly, zodpovědělo dvě rozhodnutí [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md) — strop hloubky zanoření, který nakonec dostalo všech pět rekurzivně čtených jazyků, ne tři, protože měření ukázalo, že na prosté závorce padá ze všech nejdřív `TSql160Parser` — a [093](./decisions/093-unreadable-input-is-a-unit-failure.md), po kterém je nečitelná jednotka `Failure` té jednotky, ne pád celého převodu; třetí je odbavená a tady zůstává jedna.

Druhým zdrojem je **porovnání [`analysis/`](./analysis/README.md) s kódem** a k němu čtyři levné konstrukce, které rozhodnutí [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) vědomě nechalo odmítat, ač by je model unesl a všechny cíle vyjádří. Deset dalších položek z tohohle zdroje je odbavených — pět z nich rozšiřovalo model nebo slovník, který javové buildery zdědily, a bylo levnější je rozšířit před šesti buildery než po nich; poslední odbavená je konstruktor `DateTime` v predikátu LINQ —, takže z tohohle zdroje už nezbývá nic.

**Třetím zdrojem je rozhodnutí [093](./decisions/093-unreadable-input-is-a-unit-failure.md) a má tu jedinou položku.** Při srovnání toho, jak všech pět jazyků hlásí nečitelný vstup, vyšlo najevo, že u C# nehlásí pozici nikdo; rozhodnutí to vědomě nechalo stranou, protože samo volilo mezi výjimkou a záznamem a C# je na straně záznamu už dnes.

**Dvě ze zbylých otázek jsou tatáž věc v jiném hávu** — `ESCAPE` u `LIKE` a agregační `DISTINCT`: obojí je konstrukce, kterou mezireprezentace nenese, kterou všechny cíle vyjádřit umějí a kterou parser proto odmítá nebo zahazuje. Až na ně dojde, je to jedno rozhodnutí, ne dvě; sdružit dvě otázky téže látky do jednoho souboru je tvar, který v tomhle souboru drží i položka o směru překladu („obě mění tvar téže obrazovky").

### Rozhodnutí

#### Syntaktickou chybu v C# nehlásí nikdo s pozicí
*Vyšlo najevo při psaní rozhodnutí [093](./decisions/093-unreadable-input-is-a-unit-failure.md), které tenhle případ vědomě nechalo stranou. Souvisí s [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md). Požadavky F11, S7.*

`CSharpSyntaxTree.ParseText` syntaktickou chybu zná a my se jí neptáme: parser vezme ze stromu deklarace tříd a diagnostiky Roslynu nečte vůbec. Jednotka `CSharpEntity` s rozbitou třídou tak nevydá nic a mluví za ni až obecný `Failure` orchestrace — „jednotka byla přečtena a nevzešlo z ní ani mapování, ani dotaz" —, tedy bez pozice a bez důvodu; u jednotky LINQ je to věta „No LINQ query chain was found in the source". Zbylé čtyři jazyky přitom řádek a sloupec hlásí a [`architecture.md`](./architecture.md) §9 o klientské validaci tvrdí, že syntaktickou chybu C# hlásí server — hlásí ji ovšem jen v té míře, že z jednotky nic nevzešlo.

Rozhodnout je třeba, co s tím, že Roslyn na rozdíl od našich čteček **zotavuje**: ze zpřeházeného textu vytáhne třídu, kterou dnes přeložíme. Odmítnout celou jednotku, jakmile strom nese syntaktickou chybu, je odpověď shodná se zbylými čtyřmi jazyky, ale vzala by překlad vstupům, které dnes projdou; hlásit chybu a přeložit, co se zotavilo, zase odporuje tomu, že `Failure` podle §5.1 znamená artefakt nevydaný.

#### Jednotka SQL nese právě jeden příkaz, a víc jich odmítá
*Vyplynulo z opravy z 2026-09-21 (viz [`architecture.md`](./architecture.md), §5). Souvisí s rozhodnutími [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) a [081](./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md). Požadavky F8, F11, F14.*

Sdílená čtečka T-SQL brala z textu první `SELECT` a zbytek ignorovala, takže `DELETE FROM T; SELECT …` odcházelo jako artefakt pro čtení a o smazání neřeklo nic. Od 2026-09-21 je každý příkaz vedle překládaného `SELECT`u `Failure`, který ho jmenuje — to je odpověď rozhodnutí 070 a táž, jakou dává MyBatis wrapper zapisujícímu `<insert>`.

Odmítnutí je ale jen bezpečná polovina odpovědi. Rozhodnutí 081 dalo jednotce právo nést **víc dotazů** a `IQueryParser.Parse` vrací builder na každý z nich; hbm.xml i mapper MyBatisu toho využívají a jednotka `SqlQuery` s dvěma `SELECT`y by mohla také. Rozhodnout je třeba, jestli se dva `SELECT`y jedné jednotky mají číst jako dva dotazy — a pokud ano, čím se pojmenují, když `SELECT` na rozdíl od `<query name>` a `<select id>` jméno nenese, takže by ho musel vymyslet nástroj (proti čemuž stojí rozhodnutí [028](./decisions/028-assembly-name-is-not-ours-to-invent.md)). Zapisující příkaz zůstane odmítnutý tak jako tak.

#### Agregační `DISTINCT` nemá v projekci místo
*Souvisí s rozhodnutím [073](./decisions/073-distinct-as-a-flag-of-the-query-scope.md), které nese `DISTINCT` jako příznak (pod)dotazu a modifikátor agregátu vědomě nechalo stranou. Požadavky F8, F11, T2.*

`COUNT(DISTINCT c.CustomerId)` je modifikátor funkce, ne dotazu: `ProjectInstruction` nese funkci jako holý název a operand sloupce s agregační funkcí totéž, takže zhroucení uvnitř agregátu nemá kam. T-SQL parser ho do rozhodnutí 073 tiše přepisoval na `COUNT(c.CustomerId)`, tedy jinou hodnotu; dnes projekci s ním vypustí se záznamem `Loss` a v podmínce filtru nebo filtru po agregaci odmítne artefakt záznamem `Failure`, HQL parser ho ve své podmnožině gramatiky hlásí jako syntaktickou chybu a LINQ tvar `g.Select(e => e.Sloupec).Distinct().Count()` je nečitelná projekce se záznamem `Loss`. Všechny tři cíle modifikátor vyjádří — T-SQL i HQL `count(distinct …)`, LINQ `.Select(…).Distinct().Count()` nad skupinou — a v ručně psaném SQL, které žádá F8, je běžný. Rozhodnout je třeba, jestli funkce v projekci a v operandu dostane příznak zhroucení, a jak ho vypíše LINQ cíl bez seskupení, kde agregát v projekci dnes ani nenese.

#### Alias v SQL jako zdroj mapování Dapperu
*Souvisí s rozhodnutími [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), [017](./decisions/017-source-precedence-for-mapping-facts.md) a [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md). Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4, a [tutoriál k Dapperu](./analysis/tutorials/dapper-getting-started.md), krok 4. Požadavek F6.*

Jedinou formou mapování, kterou Dapper má, je alias `AS` v dotazu, a nástroj ji nečte: SQL parser aliasy nese jen jako alias projekce a entitní mapa Dapper zdroje dostává sloupce až z katalogu, který páruje sloupec s vlastností podle jména. Doménu z tutoriálu — vlastnost `Id` nad sloupcem `AuthorId` — tak katalog nespáruje a klíč nedodá. Rozhodnout je třeba, jestli má alias z dotazové jednotky téhož převodu propsat název sloupce do entitní mapy jako tvrzení prvního stupně; je to fakt jednoho dotazu, ne třídy, a dva dotazy mohou týž sloupec aliasovat různě, takže rozhodnutí musí říct, co je konflikt a co ne.

#### Fluent konfigurace EF Core jako vstupní jednotka
*Rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [068](./decisions/068-source-framework-precedence-orders-the-reading.md) s parserem fluent konfigurace počítají, ale položku k němu nikdo nezapsal. Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4. Požadavky F1, F5.*

Srovnání frameworků označuje fluent API v `OnModelCreating` za primární formu mapování EF Core a nástroj čte jen anotace a konvence. Třída kontextu navíc není ze čtení vyloučená: každá deklarace třídy v jednotce je entita, takže vložený `DbContext` vyjde jako entita s kolekčními vztahy na své `DbSet` vlastnosti. Rozhodnout je třeba, jestli fluent konfigurace vstupuje jako další artefakt EF Core, čtený v pořadí, které 068 stanovilo, a co se do té doby dělá s třídou kontextu ve vstupu — vyloučení se záznamem je levnější než dnešní tichý omyl.

#### Dapper.Contrib v rozsahu, nebo mimo něj
*Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §2. Souvisí s rozhodnutím [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md).*

Dapper.Contrib přidává k holému Dapperu atributy `[Table]`, `[Key]` a `[ExplicitKey]`, tedy tři mapovací fakty, které holý Dapper nemá kde vyslovit. Nástroj čte jen holý Dapper. Rozhodnout je třeba, jestli je Contrib výslovně mimo rozsah, nebo jestli ho Dapper parser čte jako anotace — po vzoru EF Core parseru a s týmž kritériem rozhodnutí 067.

#### Klauzule `ESCAPE` u `LIKE` nemá v podmínce místo
*Souvisí s rozhodnutím [051](./decisions/051-like-pattern-translated-not-carried-over.md), které vzorek `LIKE` do LINQ překládá, a s [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md), které `ESCAPE` nechává odmítat. Požadavky F11, T2, T3.*

`LIKE 'A!_%' ESCAPE '!'` odmítá artefakt záznamem `Failure`, protože `ComparisonCondition` s operátorem `Like` nese jen vzorek a vzorek čtený bez únikového znaku vybere jiné řádky — podtržítko by bylo zástupným znakem. Všechny tři cíle únikový znak nesou: T-SQL i HQL klauzulí `escape`, EF Core přetížením `EF.Functions.Like(x, vzorek, únik)`. Rozhodnout je třeba, jestli únikový znak dostane místo na porovnání s operátorem `Like`, a jak se s ním vypořádá překlad vzorku z rozhodnutí 051: kotvený vzorek s únikem před zástupným znakem má stále přesný protějšek (`'A!_%'` je `StartsWith("A_")`), jen ho rozpoznání musí číst po únikovém znaku.
