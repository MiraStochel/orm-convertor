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
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), které tenhle případ vědomě nechalo otevřený. Předpoklad čtení fluent konfigurace EF Core a podpory MyBatisu (F8). Požadavky F5, F11.*

Rozhodnutí 017 uspořádalo zdroje faktů na vstupní text frameworku, pomocné mapovací artefakty, katalog a konvenci cíle. Některý framework ale mezi svými vlastními artefakty precedenci sám dokumentuje, a to opačnou: EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML mapperu vlastními pravidly. Přeložit takový projekt naším pořadím znamená přeložit něco jiného, než co zdroj znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Rozhodnout je třeba, jestli precedence zdrojového frameworku přebíjí naše pořadí uvnitř prvního stupně, a pokud ano, kde je ta precedence zapsaná: deskriptor popisuje cíl, ne zdroj, a kategorii pro tohle nemá. Dokud se čte jediný artefakt na framework, případ nenastane; nastane s prvním parserem fluent konfigurace.

### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

---

## Otevřená práce

### Generovaný artefakt nikdo nespustil proti databázi
*Na řadě. 4. stupeň ověření podle rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md); popsaný stav je v [`architecture.md`](./architecture.md), §6.2. Kontejnerová konfigurace ho odblokovala (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md)). Požadavky F3, F4, F6, F11, S2.*

Rozhodnutí 016 uznává čtyři stupně ověření generovaných artefaktů a čtvrtý z nich — *entita se uloží a načte se stejnou identitou* — nemá jediného zástupce. Připravená je pro něj hranice transakce na `TestSchemaFixture`: metoda `OpenConnection()` existuje a volají ji testy v `TestSchemaFixtureTest`, ty ale hlídají schéma fixture samotné (F4 potřebuje znát očekávanou odpověď), nikoli generovaný artefakt. Chybí tedy scénář, ve kterém se vygenerovaná entita a její mapování skutečně použijí k zápisu a čtení řádku.

Mezera není teoretická. Kolekce deklarovaná konkrétním typem místo rozhraní prošla druhým i třetím stupněm bez povšimnutí a odhalilo ji až čtení kódu — NHibernate váže kolekci na CLR typ vlastnosti teprve při načtení entity, takže právě tuhle třídu vad vidí jen 4. stupeň. Náhradou je dnes 1. stupeň se zakázanými značkami v deskriptoru (rozhodnutí [035](./decisions/035-nhibernate-collections-declared-by-interface.md)), což je kontrola tvaru, ne běhu. Až stupeň dostane prvního zástupce, patří načtení entity s kolekcí mezi první scénáře.

### HQL parser pro round-trip NHibernate → NHibernate
*Vyňatá oblast 4 hranice záruk ([`architecture.md`](./architecture.md), §9). Navazuje na rozhodnutí [022](./decisions/022-native-query-syntax-in-builders.md) a [026](./decisions/026-home-of-shared-query-reading.md). Požadavky F7–F10, T2, T3.*

Dotazová větev je hotová ve všech devíti směrech, ale jeden z nich není textovým round-tripem: u NHibernate se jako zdroj čte LINQ a jako cíl se generuje HQL (rozhodnutí 022 zvolilo nativní syntaxi cíle), takže převod NHibernate → NHibernate proběhne a vrátí platný dotaz, jen ne týž text, který přišel na vstupu. Parser HQL v řešení není a sdílené čtení dotazů (`LinqParsing`, rozhodnutí 026) mu nepomůže — HQL je vlastní gramatika, ne tvar LINQ.

Dokud parser chybí, nemá požadavek T3 u tohohle směru co porovnávat a matice T2 v něm měří překlad z jiného jazyka, než v jakém je zdroj napsaný. Domovem takového parseru je `NHibernateWrappers`, ne `LinqParsing`: je to čtení nativní syntaxe jednoho frameworku, tedy táž kategorie jako `DapperSqlQueryParser` s gramatikou T-SQL.

### Cílový databázový dialekt v deskriptoru
*Sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md); deskriptor s cílovou verzí, na kterou se dialekt tvarem podobá, je hotový (rozhodnutí [013](./decisions/013-target-framework-versions.md)). Vyňatá oblast hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F7–F10, S2.*

Cílový databázový dialekt je fakt o cíli převodu téhož tvaru jako verze frameworku v deskriptoru, a rozhodnutí 019 ho odmítlo řešit v typovém modelu. Bez jeho deklarace nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — NHibernate builder dnes propisuje jen doslovný `SourceSqlType`, který nese zdroj. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server a nástroj nic víc netvrdí.

Zdrojová strana je jiná otázka než tahle položka a deklarace cílového dialektu ji nevyřeší: `DapperSqlQueryParser` čte T-SQL gramatikou `TSql160Parser` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)), takže SQL napsané pro jiný databázový systém — u MyBatisu (F8) běžné — touhle cestou neprojde. Řešením je vlastní parser SQL v javovém wrapperu, ne pole v deskriptoru.

### Poddotazy a množinové operace se nevykreslí
*Potom. Podklad: audit 2026-08-02, kap. 8. Požadavek T2, který dotazovou matici dělí i podle poddotazů a množinových operací.*

Dotazová mezireprezentace zanoření nese, vykreslovací strana ne. `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec, takže poddotaz projde parsováním, ale jeho výsledek se nikam neskládá. **Tiché to už není** — `Normalize()` v šabloně dotazového builderu (rozhodnutí [023](./decisions/023-query-builder-template-method.md)) vnořený poddotaz ohlásí záznamem o ztrátě —, ale vykreslit ho to neumí. Vedle toho `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu), takže složený dotaz se poskládá špatně; množinovou operaci navíc dnes vykresluje jedině Dapper builder, kdežto NHibernate ji podle deskriptoru vyjádřit neumí a EF Core builder pro ni nemá větev. Sem patří i stránkování, které mezireprezentace vůbec nenese — parsery ho hlásí jako ztrátu. Všechno tohle jsou kategorie dotazové matice podle T2, které tak nemají co měřit.

### Advisor nemá build nativní knihovny pro Windows
*Mezera popsaná v [`architecture.md`](./architecture.md), §8. Souvisí s T7. Kontejnerová cesta Advisor pokrývá a je ověřená (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md), `architecture.md` §6.4); tahle položka je o hostiteli bez Dockeru.*

`libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`) a název je v P/Invoke natvrdo linuxový, takže mimo Linux a Docker Advisor endpointy selhávají; překladová část na tom nezávisí. Advisor je ze záruk vyňatý vcelku ([`architecture.md`](./architecture.md), §9), takže doslovně linuxový název v `LibraryImport` odpovídá tomu, co o něm tvrdíme; zbývá jen build krok pro `advisor.dll`, pro který má `ilp.c` exportní makra připravená. Nasazenou instanci to neshodí — `AdvisorRunHandler` výjimku z P/Invoke zachytává a vrací její text, takže uživatel dostane hlášku.

### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

---

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
