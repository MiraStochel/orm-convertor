# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Kde se pokračuje, říká značka na řádku vazeb položky** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě. Samostatný seznam pořadí tenhle soubor nemá — rozcházel se s položkami pod ním.

---

## Otevřená rozhodnutí

### Kritérium pro širší čtení konvencí zdroje
*Navazuje na rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), které v tomto bodě nahradilo [008](./decisions/008-database-as-metadata-source.md). Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F2, F5, F6.*

Parsery dnes čtou konvenci zdrojového frameworku jen tam, kde by její neznalost změnila význam, což je v praxi jediné místo: primární klíč v `EFCoreEntityParser.FindConventionKey`. Implicitní je ale i název sloupce, název tabulky a nullabilita odvozená z jazykového typu, a u MyBatisu je konvenční mapování jediné, které vůbec existuje, takže F6 na širším čtení stojí. Rozhodnutí 008 otázku odložilo s podmínkou „až mezireprezentace bude evidovat původ faktu"; rozhodnutí 010 ale původ z modelu vyňalo a 015 tuhle volbu převzalo, takže odkládací podmínka nikdy nenastane a otázka zůstala bez kritéria. Je třeba rozhodnout, podle čeho se pozná, kterou konvenci materializovat. Nabízí se „materializuj tam, kde by cíl doplnil něco jiného, než tvrdí zdroj", to ale znamená znát konvence obou stran a mít je kde zapsat — pravděpodobně v deskriptoru, který dnes kategorii pro konvence nemá. Dokud kritérium chybí, přibývají konvence jednotlivě a bez pravidla, jak se to stalo u klíče v EF Core.

### Framework s vlastní precedencí mezi svými artefakty
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), které tenhle případ vědomě nechalo otevřený; rozhodnutí [049](./decisions/049-language-facts-under-source-precedence.md) zavřelo jeho mechanickou půlku. Předpoklad čtení fluent konfigurace EF Core a podpory MyBatisu (F8). Požadavky F5, F11.*

Rozhodnutí 017 uspořádalo zdroje faktů na vstupní text frameworku, pomocné mapovací artefakty, katalog a konvenci cíle. Některý framework ale mezi svými vlastními artefakty precedenci sám dokumentuje, a to opačnou: EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML mapperu vlastními pravidly. Přeložit takový projekt naším pořadím znamená přeložit něco jiného, než co zdroj znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Rozhodnout je třeba, jestli precedence zdrojového frameworku přebíjí naše pořadí uvnitř prvního stupně, a pokud ano, kde je ta precedence zapsaná: deskriptor popisuje cíl, ne zdroj, a kategorii pro tohle nemá. Dokud se čte jediný artefakt na framework, případ nenastane; nastane s prvním parserem fluent konfigurace. Co už otevřené není, je co se stane *mechanicky*: rozhodnutí [049](./decisions/049-language-facts-under-source-precedence.md) rozšířilo pravidlo 017 i na jazyková fakta vlastnosti, takže druhá deklarace téže vlastnosti nevyrobí duplikát, ale doplní prázdné fakty a rozdíl ohlásí. Otevřená zůstává otázka o *pořadí*: jestli precedenci, kterou si zdrojový framework dokumentuje sám, máme respektovat místo svojí.

### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

### Který vstupní jednotce záznam patří
*Sem odkázalo rozhodnutí [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md), které atribuci po jednotkách vědomě nechalo otevřenou. Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md) a s vícejednotkovým vstupem podle F14. Požadavky F11, F14, S6.*

Rozhodnutí 045 zavřelo ticho na úrovni běhu: jednotku v jazyce, který zdroj nečte, i převod, ze kterého nevyšlo nic, dnes hlásí `Failure`. Zbývá případ mezi tím — jednotka, ze které nic nevzejde, zatímco jiná jednotka téhož běhu artefakt vydá. Volající dostane výstup a o té jedné jednotce se nedozví nic, přestože ji poslal.

Rozeznat ji počítáním `EntityMaps` před a po každém parseru nejde: u NHibernate parser XML mapování obohacuje entitu, kterou už vytvořil parser třídy, takže nepřidá novou mapu a poctivá jednotka by dostala nepoctivý záznam. Poctivá atribuce vyžaduje, aby parser hlásil, co z které jednotky přečetl — tedy zásah do rozhraní parserů v `AbstractWrappers`, které dnes vrací `void`. To je změna, kterou má podle S1 předcházet rozhodnutí: rozhraní parserů je právě ta plocha, o které S1 tvrdí, že nový framework se do ní vejde beze změny. Precedens, jak takové rozhodnutí vypadá, je [047](./decisions/047-content-type-reaches-the-query-parser.md) — rozhraní se změnilo proto, že nenesené informace se dole rekonstruovaly odhadem; tady by šlo o návratovou hodnotu, ne o parametr, ale úvaha o S1 je táž. Zároveň je to předpoklad toho, aby diagnostika uměla ukázat na *soubor*, což je zúžení, které dnes u F14 vyslovuje [`architecture.md`](./architecture.md) §9.

### Meze pravidla „dotaz, který vrátí jiné řádky, se nevydá"
*Sem odkázalo rozhodnutí [053](./decisions/053-a-query-that-would-return-other-rows-is-not-emitted.md), které svou vlastní hranici vyslovilo. Souvisí s [004](./decisions/004-unexpressible-facts-as-warnings.md) a [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F11, T2, T3.*

Rozhodnutí 053 zakázalo dosazovat náhražku za podmínku, kterou cíl vykreslit neumí: tautologie místo filtru vrátí všechny řádky, které zdroj vyloučil, takže artefakt nevzniká a důvod je v záznamu. Táž věta ale doslova sedí i na dvě náhrady, které v nástroji zůstávají a jsou popsané jinde. **Plný vnější join** se u EF Core i NHibernate vypisuje jako vnitřní — zúžení uvnitř kategorie `JoinKind`, hlášené v místě emise ([`architecture.md`](./architecture.md) §5.1) — a vnitřní join vrací *méně* řádků než plný, takže výsledek dotazu je jiný, ne jen chudší. **Zahozený poddotaz** hlásí `Normalize()` záznamem o ztrátě a dotaz se vydá bez něj, což je tentýž tvar.

Rozhodnout je třeba, jestli je hranicí pravidla *podmínka* (dnešní znění 053), nebo *množina řádků* (jeho vlastní argument). Druhá odpověď je přísnější a konzistentnější, ale není zadarmo: u joinu by znamenala nevydat artefakt tam, kde ho dnes uživatel dostane spolu se srozumitelným záznamem o tom, co se stalo, a matice T2 by v kategorii *druh joinu* přestala měřit překlad a začala měřit odmítnutí. Volba se navíc dotýká čtení T3: dnes se takový výstup počítá jako přeložený a funkčně neekvivalentní, po zpřísnění by se nepočítal vůbec. Poddotazová půlka se překrývá s položkou *Poddotazy a množinové operace se nevykreslí* — až se vykreslovat začnou, přestane být náhradou a otázka zbude jen u joinu.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient`, který k nim teče přes `benchmarks/Common`. Pro srovnání výkonu je to metodologický confound. **Rozsah je nově dohledaný celý** — u PetaPoco vyloučením, protože `Microsoft.Data.SqlClient` v grafu balíků obou jeho projektů není, u EF6 z `WWIDbConfiguration`; podrobnosti nese [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Zbývá tedy volba, ne zjišťování: buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` (samostatný balík, dnes nereferencovaný) a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

---

### Směr překladu jako jedna věc a vstup vedle výstupu
*Navazuje na rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md), které pětikrokový tvar obrazovky zvolilo a je podle něj napsaný kód; případnou změnu je proto třeba nahradit, ne revidovat. Souvisí s [032](./decisions/032-frontend-as-static-pages-without-a-build.md) a s položkou „Který vstupní jednotce záznam patří". Požadavky F14, S7.*

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných sekcí viditelných najednou a první dvě z nich jsou volba zdroje a volba cíle. Důsledek je, že směr překladu na obrazovce nikde nestojí jako jedna věc: „EF Core → NHibernate" se poprvé objeví až v hlavičce výsledku a prohodit obě volby jde jen ručně, dvěma zásahy do dvou rozbalovacích seznamů. Jedna řádka směru s tlačítkem pro prohození by z pěti sekcí udělala čtyři, což S7 nebrání — „nejvýš pět kroků" je strop, ne kvóta —, ale je to změna volby, kterou 033 vyslovilo výslovně, takže patří do nového rozhodnutí.

Do téhož rozhodnutí patří druhá otázka, protože obě mění tvar téže obrazovky a navrhovat je zvlášť by znamenalo navrhnout ji dvakrát: **jestli má vstup stát vedle výstupu.** Dnes jsou vstupní jednotky nahoře jako textová pole a artefakty dole jako panely, takže se zdroj a výsledek nedají číst současně — na výkladové stránce vedle sebe stojí, na nástrojové ne. Podstatná je tu poctivost, ne rozvržení: server neříká, který artefakt vznikl ze které jednotky (§9, zúžení F14), takže sloupce vedle sebe se buď musí spárovat toutéž jmennou heuristikou, jakou se artefakty pojmenovávají, a jako heuristika se i označit, nebo nesmí tvrdit párování vůbec a nesou pak nadpisy typu „co jste poslali" a „co přišlo zpět". Druhá cesta nic nevymýšlí a je slučitelná s tím, že skutečné párování čeká na pojmenované jednotky — tedy na otevřenou položku *Který vstupní jednotce záznam patří*.

## Otevřená práce

### Generovaný artefakt nikdo nespustil proti databázi
*Na řadě. 4. stupeň ověření podle rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md); popsaný stav je v [`architecture.md`](./architecture.md), §6.2. Kontejnerová konfigurace ho odblokovala (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md)). Požadavky F3, F4, F6, F11, S2.*

Rozhodnutí 016 uznává čtyři stupně ověření generovaných artefaktů a čtvrtý z nich — *entita se uloží a načte se stejnou identitou* — nemá jediného zástupce. Připravená je pro něj hranice transakce na `TestSchemaFixture`: metoda `OpenConnection()` existuje a volají ji testy v `TestSchemaFixtureTest`, ty ale hlídají schéma fixture samotné (F4 potřebuje znát očekávanou odpověď), nikoli generovaný artefakt. Chybí tedy scénář, ve kterém se vygenerovaná entita a její mapování skutečně použijí k zápisu a čtení řádku.

Mezera není teoretická. Kolekce deklarovaná konkrétním typem místo rozhraní prošla druhým i třetím stupněm bez povšimnutí a odhalilo ji až čtení kódu — NHibernate váže kolekci na CLR typ vlastnosti teprve při načtení entity, takže právě tuhle třídu vad vidí jen 4. stupeň. Náhradou je dnes 1. stupeň se zakázanými značkami v deskriptoru (rozhodnutí [035](./decisions/035-nhibernate-collections-declared-by-interface.md)), což je kontrola tvaru, ne běhu. Až stupeň dostane prvního zástupce, patří načtení entity s kolekcí mezi první scénáře.

### `[Keyless]` na vstupu nikdo nečte a konvence přes něj klíč vymyslí
*Nalezeno při implementaci rozhodnutí [055](./decisions/055-unique-constraint-as-a-carried-mapping-fact.md). Navazuje na rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md), [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) a [017](./decisions/017-source-precedence-for-mapping-facts.md). Požadavky F5, F11, S2.*

`EFCoreEntityBuilder` anotaci `[Keyless]` vypisuje — entita bez klíče ji v EF Core potřebuje, jinak by si framework klíč odvodil z vlastnosti `Id` sám —, ale `EFCoreEntityParser` ji nečte. Od rozhodnutí 055 už aspoň nemizí potichu: třídní anotace, kterou parser nezná, vydá záznam o ztrátě. Fakt se tím ale nedostane do modelu, a to má důsledek: `FindConventionKey` běží pokaždé, když chybí `[Key]` i třídní `[PrimaryKey]`, takže třída označená `[Keyless]` a nesoucí vlastnost `Id` dostane primární klíč, který zdroj **výslovně popřel**. Konvence tu přebíjí tvrzení, což je přesný opak pravidla priority zdrojů.

Oprava je malá — konvenční klíč se nesmí odvodit tam, kde třída nese `[Keyless]` —, ale nese s sebou jednu otázku, kterou je třeba zodpovědět: co znamená `[Keyless]` vedle `[Key]` na téže třídě. Vstup si protiřečí a rozhodnutí 010 pro takový případ zná dva tvary, `Conflict` a `Failure`; volba mezi nimi říká, jestli je to vstup ke spravení, nebo artefakt, který nemá vzniknout.

### HQL parser pro round-trip NHibernate → NHibernate
*Vyňatá oblast 4 hranice záruk ([`architecture.md`](./architecture.md), §9). Navazuje na rozhodnutí [022](./decisions/022-native-query-syntax-in-builders.md) a [026](./decisions/026-home-of-shared-query-reading.md). Požadavky F7–F10, T2, T3.*

Dotazová větev je hotová ve všech devíti směrech, ale jeden z nich není textovým round-tripem: u NHibernate se jako zdroj čte LINQ a jako cíl se generuje HQL (rozhodnutí 022 zvolilo nativní syntaxi cíle), takže převod NHibernate → NHibernate proběhne a vrátí platný dotaz, jen ne týž text, který přišel na vstupu. Parser HQL v řešení není a sdílené čtení dotazů (`LinqParsing`, rozhodnutí 026) mu nepomůže — HQL je vlastní gramatika, ne tvar LINQ.

Dokud parser chybí, nemá požadavek T3 u tohohle směru co porovnávat a matice T2 v něm měří překlad z jiného jazyka, než v jakém je zdroj napsaný. Domovem takového parseru je `NHibernateWrappers`, ne `LinqParsing`: je to čtení nativní syntaxe jednoho frameworku, tedy táž kategorie jako `DapperSqlQueryParser` s gramatikou T-SQL.

### Cílový databázový dialekt v deskriptoru
*Sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md); deskriptor s cílovou verzí, na kterou se dialekt tvarem podobá, je hotový (rozhodnutí [013](./decisions/013-target-framework-versions.md)). Vyňatá oblast hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F7–F10, S2.*

Cílový databázový dialekt je fakt o cíli převodu téhož tvaru jako verze frameworku v deskriptoru, a rozhodnutí 019 ho odmítlo řešit v typovém modelu. Bez jeho deklarace nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — oba .NET buildery dnes propisují jen doslovný `SourceSqlType`, který nese zdroj (rozhodnutí [052](./decisions/052-literal-sql-type-reaches-the-ef-core-annotation.md) srovnalo EF Core s NHibernate), a odvozený název typu berou z pevné tabulky pro SQL Server. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server a nástroj nic víc netvrdí.

Zdrojová strana je jiná otázka než tahle položka a deklarace cílového dialektu ji nevyřeší: `DapperSqlQueryParser` čte T-SQL gramatikou `TSql160Parser` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)), takže SQL napsané pro jiný databázový systém — u MyBatisu (F8) běžné — touhle cestou neprojde. Řešením je vlastní parser SQL v javovém wrapperu, ne pole v deskriptoru.

### Poddotazy a množinové operace se nevykreslí
*Potom. Podklad: audit 2026-08-02, kap. 8. Požadavek T2, který dotazovou matici dělí i podle poddotazů a množinových operací.*

Dotazová mezireprezentace zanoření nese, vykreslovací strana ne. `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec, takže poddotaz projde parsováním, ale jeho výsledek se nikam neskládá. **Tiché to už není** — `Normalize()` v šabloně dotazového builderu (rozhodnutí [023](./decisions/023-query-builder-template-method.md)) vnořený poddotaz ohlásí záznamem o ztrátě —, ale vykreslit ho to neumí. Vedle toho `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu), takže složený dotaz se poskládá špatně; množinovou operaci navíc dnes vykresluje jedině Dapper builder, kdežto NHibernate ji podle deskriptoru vyjádřit neumí a EF Core builder pro ni nemá větev. Sem patří i stránkování, které mezireprezentace vůbec nenese — parsery ho hlásí jako ztrátu. Všechno tohle jsou kategorie dotazové matice podle T2, které tak nemají co měřit.

### Advisor nemá build nativní knihovny pro Windows
*Mezera popsaná v [`architecture.md`](./architecture.md), §8. Souvisí s T7. Kontejnerová cesta Advisor pokrývá a je ověřená (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md), [`ORMConvertor/README.md`](../ORMConvertor/README.md#tests)); tahle položka je o hostiteli bez Dockeru.*

`libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`) a název je v P/Invoke natvrdo linuxový, takže mimo Linux a Docker Advisor endpointy selhávají; překladová část na tom nezávisí. Advisor je ze záruk vyňatý vcelku ([`architecture.md`](./architecture.md), §9), takže doslovně linuxový název v `LibraryImport` odpovídá tomu, co o něm tvrdíme; zbývá jen build krok pro `advisor.dll`, pro který má `ilp.c` exportní makra připravená. Nasazenou instanci to neshodí — `AdvisorRunHandler` výjimku z P/Invoke zachytává a vrací její text, takže uživatel dostane hlášku.

### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

---

### Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška
*Souvisí s rozhodnutím [033](./decisions/033-shape-of-the-static-frontend-screens.md), které validaci XML s číslem řádku zavedlo, a s [032](./decisions/032-frontend-as-static-pages-without-a-build.md), bod f (žádná další vendorovaná knihovna bez rozhodnutí). Požadavek S7.*

Validace před odesláním hlásí u nesprávně utvořeného XML číslo řádku a serverová hláška u SQL nese řádek a sloupec z `TSql160Parser`. Editor jednotky je ale holý `<textarea>` bez číslování, takže „řádek 7" se v něm hledá počítáním. Doslovné znění S7 mluví o zvýraznění chyb na úrovni souboru a řádku a tohle je jeho druhá půlka, která chybí — první, tedy chyba přiřazená ke konkrétní jednotce, hotová je.

Práce je to hotová v zadání, ne v rozvaze: postranní sloupec s čísly řádků, který se posouvá spolu s textovým polem, je několik desítek řádků vlastního kódu a chová se spolehlivě. Co je potřeba nedělat, je sáhnout po hotovém editoru — CodeMirror nebo cokoli podobného by byla třetí vendorovaná knihovna, a to je podle bodu 032f samostatné rozhodnutí, ne detail implementace.

### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Souvisí s rozhodnutím [041](./decisions/041-versioning-and-release.md) — nový koncový bod je změna REST kontraktu, tedy vydání MINOR — a s položkou „Advisor nemá build nativní knihovny pro Windows". Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so` a `AdvisorRunHandler` výjimku z P/Invoke zachytí a vrátí její text, takže uživatel se o nedostupnosti dozví jako o `DllNotFoundException` — po vyplnění celého formuláře a po odeslání běhu. Úvodní odstavec obrazovky přitom říká dopředu, že Advisor potřebuje kontejner; nedostupnost se tedy sděluje dvakrát, jednou naší větou předem a jednou hláškou zavaděče potom.

Aby to obrazovka mohla říct **místo** běhu a vlastními slovy, potřebuje se serveru zeptat, jestli je Advisor na tomhle hostiteli k dispozici — dnes na to není koncový bod a klient si to odvodit nemůže. Je to tedy nový koncový bod, tedy změna veřejné plochy podle rozhodnutí 041; sám o sobě je malý, ale předchází mu volba, jestli do vyňaté oblasti sahat dřív, než se dodělá rozdělané.

### Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje
*Zúžení, které dnes vyslovuje [`architecture.md`](./architecture.md), §9 („zobrazení IR verze nenárokuje vůbec"). Souvisí s rozhodnutími [010](./decisions/010-diagnostics-as-returned-data.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md) a [041](./decisions/041-versioning-and-release.md). Požadavky F11, F14.*

Požadavek F14 žádá zobrazení čtyř věcí — vstupu, mezireprezentace, výstupu a diagnostiky — a nárokujeme tři: `/convert` mezireprezentaci nevrací a rozhraní ji nemá odkud vzít. Je to jediné místo, kde se dnes nárok na F14 zužuje z důvodu, který leží na serveru, ne na obrazovce, a zároveň to nejlépe placené místo pro text práce: pipeline parse → doplnění → build se čtenáři, který nástroj nikdy nespustí, ukazuje právě prostředním článkem.

Rozhodnout je třeba dřív, než se cokoli začne psát, protože cena není ve vykreslení: serializovaný tvar `EntityMap`, klíče, vztahů a dotazových instrukcí by se stal součástí REST kontraktu se vším, co to znamená pro verzování (rozhodnutí 041, změna MINOR při přidání pole a MAJOR při jeho odebrání). Otázka tedy zní, jestli se mezireprezentace vydává jako plnohodnotná část odpovědi, nebo jako výslovně nestabilní náhled, u kterého se dopředu řekne, že se může měnit mezi vydáními — a druhá odpověď je levnější jen zdánlivě, protože nestabilní část kontraktu je pořád část kontraktu.

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne. F4–F6 mezi nimi už nejsou — jsou hotové a to, co z nich zbývá, je výše. Z F14 je hotový vícesouborový vstup a výstup po souborech; zbytek bloku je tady.

| Blok | Co odblokuje |
|---|---|
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech a kontrola úplnosti IR jsou hotové (rozhodnutí 010), syntaktické ověření generovaných souborů také (rozhodnutí 016, `architecture.md` §6.2) a záznam běhu podle S6 — identifikátor běhu, verze obou frameworků z deskriptorů a verze nástroje z `Directory.Build.props` (rozhodnutí 034) — vydává `/convert` |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; typový model má zneutralizovaný na jazykové (rozhodnutí 014) i databázové straně (rozhodnutí 019) a parametry generátoru se nesou kanonicky s výběrem názvu ve výstupu (rozhodnutí 020 a 021, obojí implementované), takže slovníkové předpoklady jsou hotové |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání; F14 je zároveň předpokladem třetí otázky u klíčové třídy |
| **T1–T7** experimenty | T7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání je hotová ve všech devíti směrech v rozsahu kategorií projekce, filtrace, join, agregace a řazení (rozhodnutí 022–027). Co z ní zbývá, jsou zbylé kategorie požadavku T2 — stránkování, poddotazy a množinové operace — a HQL parser, bez kterého NHibernate → NHibernate není textovým round-tripem; obojí má vlastní položku výše.
