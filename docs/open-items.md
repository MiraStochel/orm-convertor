# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Kde se pokračuje, říká značka na řádku vazeb položky** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě. Samostatný seznam pořadí tenhle soubor nemá — rozcházel se s položkami pod ním.

**Co patří do verze 1.0, říká značka `Verze 1.0 — N.`** na témž řádku (rozhodnutí [030](./decisions/030-scope-of-version-1-0.md)), kde `N` je pořadí uvnitř vydání. Číslované pořadí dává smysl jen tam, kde je množina uzavřená, a obsah jednoho vydání uzavřený je; mimo něj platí předchozí odstavec beze změny. Značky se tím nezdvojují, nýbrž váží: „Na řadě" nese položka s nejnižším dosud neodbaveným číslem a „Potom" následující dvě, takže se jejich umístění dá zkontrolovat, ne jen přečíst. Položka bez čísla do verze 1.0 nepatří a bere se podle priorit plynoucích z požadavků F/S/T; kde je důvod jejího vynechání čerstvý, říká ho její vlastní text.

---

## Otevřená rozhodnutí

### Kritérium pro širší čtení konvencí zdroje
*Navazuje na rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), které v tomto bodě nahradilo [008](./decisions/008-database-as-metadata-source.md). Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F2, F5, F6.*

Parsery dnes čtou konvenci zdrojového frameworku jen tam, kde by její neznalost změnila význam, což je v praxi jediné místo: primární klíč v `EFCoreEntityParser.FindConventionKey`. Implicitní je ale i název sloupce, název tabulky a nullabilita odvozená z jazykového typu, a u MyBatisu je konvenční mapování jediné, které vůbec existuje, takže F6 na širším čtení stojí. Rozhodnutí 008 otázku odložilo s podmínkou „až mezireprezentace bude evidovat původ faktu"; rozhodnutí 010 ale původ z modelu vyňalo a 015 tuhle volbu převzalo, takže odkládací podmínka nikdy nenastane a otázka zůstala bez kritéria. Je třeba rozhodnout, podle čeho se pozná, kterou konvenci materializovat. Nabízí se „materializuj tam, kde by cíl doplnil něco jiného, než tvrdí zdroj", to ale znamená znát konvence obou stran a mít je kde zapsat — pravděpodobně v deskriptoru, který dnes kategorii pro konvence nemá. Dokud kritérium chybí, přibývají konvence jednotlivě a bez pravidla, jak se to stalo u klíče v EF Core.

### Framework s vlastní precedencí mezi svými artefakty
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), které tenhle případ vědomě nechalo otevřený. Předpoklad čtení fluent konfigurace EF Core a podpory MyBatisu (F8). Požadavky F5, F11.*

Rozhodnutí 017 uspořádalo zdroje faktů na vstupní text frameworku, pomocné mapovací artefakty, katalog a konvenci cíle. Některý framework ale mezi svými vlastními artefakty precedenci sám dokumentuje, a to opačnou: EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML mapperu vlastními pravidly. Přeložit takový projekt naším pořadím znamená přeložit něco jiného, než co zdroj znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Rozhodnout je třeba, jestli precedence zdrojového frameworku přebíjí naše pořadí uvnitř prvního stupně, a pokud ano, kde je ta precedence zapsaná: deskriptor popisuje cíl, ne zdroj, a kategorii pro tohle nemá. Dokud se čte jediný artefakt na framework, případ nenastane; nastane s prvním parserem fluent konfigurace.

### Priorita zdrojů se nevztahuje na primární klíč
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), jehož implementace tuhle cestu vědomě nechala stranou. Souvisí s rozhodnutím [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) a s položkou o vlastní precedenci frameworku výše. Požadavky F5, S2.*

Zápisové cesty builderu vyplňují od rozhodnutí 017 jen prázdný fakt: shodné opakování není událost a odlišné pozdější tvrzení skončí záznamem `Conflict` se zachovanou první hodnotou. `AddPrimaryKey` je z toho pravidla vyňaté — klíč vzniká jediným voláním a opakované volání ho i s detaily strategie nahradí, jak jeho kontrakt dokumentuje (viz [`architecture.md`](./architecture.md), §5). Dva vstupní artefakty téhož frameworku, které o téže třídě tvrdí různý klíč, tak tiše skončí u toho pozdějšího a převod o rozporu neřekne nic — proti F5, který hlášení konfliktů žádá, i proti S2, protože výsledek pak závisí na pořadí artefaktů. Fáze doplnění z katalogu vyňatá není: klíč doplňuje jen tam, kde žádný není, a jinak ho porovnává (viz §5.2), takže nehlídaný zůstává právě první stupeň. Dosažitelné je to už dnes dvěma mapovacími XML NHibernate nad toutéž třídou; s prvním parserem fluent konfigurace EF Core přibude druhý artefakt, který klíč tvrdí. Rozhodnout je třeba, jestli má klíč pod pravidlo 017 spadnout celý, a pokud ano, čím nahradit kontrakt „opakované volání klíč nahrazuje" tak, aby na něm nedoplatila vnitřní volání builderu (syntéza junction entit) ani detaily strategie, které se k částem zapisují až po definici klíče.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce. Do verze 1.0 to nepatří: rozhodnutí [030](./decisions/030-scope-of-version-1-0.md) vyňalo benchmarking ze záruk vcelku, takže srovnávat jeho konfiguraci nemá proti čemu.

---

## Otevřená práce

### Kontejnerová konfigurace prostředí
*Dohnání S5 odložené rozhodnutím [016](./decisions/016-generated-artifact-verification-levels.md).*

S5 žádá celý systém včetně databáze spustitelný dokumentovanou kontejnerovou konfigurací, kde čisté prostředí reprodukuje testy jedním hlavním příkazem. Lokální instance zvolená rozhodnutím 016 to nesplňuje a splnit nemá. Protože ale o hostiteli rozhoduje konfigurace, a ne kód testů, jde o přidání služby a proměnné prostředí, ne o návrat k rozhodnutí.

Patří sem trojí: service container do workflow v `.github` spolu s proměnnou `ConnectionStrings__TestDatabase`, aby databázově závislé testy běžely i v CI (dnes se tam přeskakují, protože proměnná není nastavená), volba mezi Docker Compose a Testcontainers pro lokální reprodukci, a ověření `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml` — commitnutá deklarace, kterou dosud nikdo nespustil a jejíž ověření dřívější plán čekal od stavby testovacího prostředí.

Do verze 1.0 nic z toho nepatří (rozhodnutí [030](./decisions/030-scope-of-version-1-0.md)): nasazení řešíme jedinou ručně spravovanou instancí, ne kontejnerem. Důsledek je ale potřeba nést nahlas i v textu práce — bez databáze v CI se databázově závislé testy dál přeskakují, takže **kritéria F4 a F6 platí jen tam, kde běh s lokální databází skutečně proběhl**.

### Cílový databázový dialekt v deskriptoru
*Sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md); deskriptor s cílovou verzí, na kterou se dialekt tvarem podobá, je hotový (rozhodnutí [013](./decisions/013-target-framework-versions.md)). Mimo verzi 1.0 (rozhodnutí [030](./decisions/030-scope-of-version-1-0.md)). Požadavky F7–F10, S2.*

Cílový databázový dialekt je fakt o cíli převodu téhož tvaru jako verze frameworku v deskriptoru, a rozhodnutí 019 ho odmítlo řešit v typovém modelu. Bez jeho deklarace nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — NHibernate builder dnes propisuje jen doslovný `SourceSqlType`, který nese zdroj. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server; verze 1.0 to říká a nic víc netvrdí.

Zdrojová strana je jiná otázka než tahle položka a deklarace cílového dialektu ji nevyřeší: `DapperSqlQueryParser` čte T-SQL gramatikou `TSql160Parser` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)), takže SQL napsané pro jiný databázový systém — u MyBatisu (F8) běžné — touhle cestou neprojde. Řešením je vlastní parser SQL v javovém wrapperu, ne pole v deskriptoru.

### Poddotazy a množinové operace se nevykreslí
*Podklad: audit 2026-08-02, kap. 8. Požadavek T2, který dotazovou matici dělí i podle poddotazů a množinových operací.*

Dotazová mezireprezentace zanoření nese, vykreslovací strana ne. `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec, takže poddotaz projde parsováním, ale jeho výsledek se nikam neskládá. **Tiché to už není** — `Normalize()` v šabloně dotazového builderu (rozhodnutí [023](./decisions/023-query-builder-template-method.md)) vnořený poddotaz ohlásí záznamem o ztrátě —, ale vykreslit ho to neumí. Vedle toho `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu), takže složený dotaz se poskládá špatně; množinovou operaci navíc dnes vykresluje jedině Dapper builder, kdežto NHibernate ji podle deskriptoru vyjádřit neumí a EF Core builder pro ni nemá větev. Sem patří i stránkování, které mezireprezentace vůbec nenese — parsery ho hlásí jako ztrátu. Všechno tohle jsou kategorie dotazové matice podle T2, které tak nemají co měřit.

### Kolekční vlastnosti pro NHibernate se generují konkrétním typem
*Souvisí s rozhodnutím [016](./decisions/016-generated-artifact-verification-levels.md) — mezera je viditelná až na 4. stupni ověření, který nemá zástupce. Požadavek F3.*

NHibernate vyžaduje, aby perzistentní kolekce byla deklarovaná rozhraním (`IList<T>`, `ISet<T>`): za běhu vlastnost nahrazuje vlastní implementací a ta se do konkrétního `List<T>` či `HashSet<T>` přiřadit nedá. Generovaná entita ale kolekce deklaruje konkrétně, protože sdílený převod jazykových typů vykresluje `List`/`HashSet` pro všechny cíle. Stavba session factory to přijímá — 3. stupeň ověření proto mlčí — a selhání by se ukázalo až při načtení entity, tedy na 4. stupni, který zástupce nemá. Náprava patří NHibernate builderu, stejně jako vynucené `virtual`: vykreslit rozhraní a přepsat inicializátor — `= new()` se nad rozhraním nepřeloží vůbec a `= []` nad `ISet<T>` také ne — ne měnit sdílený převod.

### Advisor nemá build nativní knihovny pro Windows
*Mezera popsaná v [`architecture.md`](./architecture.md), §8. Souvisí s S5 a T7.*

`libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`) a název je v P/Invoke natvrdo linuxový, takže mimo Linux a Docker Advisor endpointy selhávají; překladová část na tom nezávisí. Soubor `ilp.c` má přitom exportní makra pro Windows připravená, jen build krok pro `advisor.dll` neexistuje. Rozhodnutí [030](./decisions/030-scope-of-version-1-0.md) tuhle dvojici uzavřelo druhým způsobem: Advisor je ze záruk verze 1.0 vyňatý vcelku, takže doslovně linuxový název v `LibraryImport` odpovídá tomu, co o něm tvrdíme, a rozpor mizí bez zásahu do kódu. Zbývá tedy jen build krok pro `advisor.dll`, a ten je mimo verzi; `ilp.c` má pro něj exportní makra připravená. Nasazenou instanci to neshodí — `AdvisorRunHandler` výjimku z P/Invoke zachytává a vrací její text, takže uživatel dostane hlášku.

### Deskriptor deklaruje vynucené členy, které čte jen test
*Souvisí s rozhodnutím [009](./decisions/009-target-framework-descriptor.md), které dělbu deklarace a emise zavedlo. Požadavek S2.*

`EnforcedMembers` a `EnforcedMembersFor` volá jedině `EnforcedMembersTest`; produkční kód je nečte. Každý builder si vynucené členy vypisuje sám a nezávisle — `virtual` v `BuildPropertySignature`, `[Serializable]` v `BuildTableSchema`, `[Keyless]` u EF Core. Rozhodnutí 009 to tak popsalo záměrně: deskriptor deklaruje, builder implementuje, test je váže. Trojice ale drží jen tak dlouho, dokud test skutečně pokrývá každý člen za každé podmínky; jinak se deklarace a emise rozejdou a nikdo se to nedozví. Zbývá rozhodnout, jestli má vazbu držet test, nebo jestli si má builder vynucené členy z deskriptoru brát — s vědomím, že párování podle názvu vrací zpět riziko překlepu, kterým 009 tuhle variantu zamítlo.

### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť. Sem patří i to, že 4. stupeň ověření podle rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md) nemá jediného zástupce: `TestSchemaFixture.OpenConnection()` existuje i s popsanou hranicí transakce, ale nikdo ho nevolá.

Do verze 1.0 položka nepatří. Rozhodnutí [030](./decisions/030-scope-of-version-1-0.md) vyňalo `Advisor` i `AdvisorBenchmarking` ze záruk vcelku právě proto, že netestované jsou; testovat oblast, na kterou verze neslibuje spoleh, by bylo otevírání nové části místo dokončení rozdělané.

### Verze nástroje a značka vydání
*Verze 1.0 — 19. Na řadě. Uzavírá rozhodnutí [030](./decisions/030-scope-of-version-1-0.md). Požadavky S2, S6.*

S2 mluví o „stejné verzi nástroje" a S6 žádá strojově čitelný záznam běhu včetně verzí; obojí předpokládá, že nástroj nějakou verzi má. Nemá. Sestavení se nikde nečíslují, repozitář nenese značku vydání a `README.md` je zděděný z původního prototypu, takže popisuje stav, který dávno neplatí. Zbývá tedy trojí: číslo verze v sestaveních — nejlépe na jednom místě spolu s centrální správou verzí —, značka v gitu, a průchod `README.md`, aby řekl, co nástroj v této verzi umí, co je z jeho záruk vyňaté a jak se spouští. Je to poslední položka vydání, protože až do ní se obsah verze ještě mění.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne. F4–F6 mezi nimi už nejsou — jsou hotové a to, co z nich zbývá, je výše. Do verze 1.0 nepatří ani jeden z nich (rozhodnutí [030](./decisions/030-scope-of-version-1-0.md)); F14 je jedinou výjimkou, a to jen v rozsahu vícesouborového vstupu a diagnostiky po souborech.

| Blok | Co odblokuje |
|---|---|
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech a kontrola úplnosti IR jsou hotové (rozhodnutí 010), syntaktické ověření generovaných souborů také (rozhodnutí 016, `architecture.md` §6.2) a záznam běhu podle S6 — identifikátor a verze frameworků z deskriptorů — vydává `/convert`; verzi nástroje do záznamu doplní položka o značce vydání |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; typový model má zneutralizovaný na jazykové (rozhodnutí 014) i databázové straně (rozhodnutí 019) a parametry generátoru se nesou kanonicky s výběrem názvu ve výstupu (rozhodnutí 020 a 021, obojí implementované), takže slovníkové předpoklady jsou hotové |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání; F14 je zároveň předpokladem třetí otázky u klíčové třídy |
| **T1–T7** experimenty | T7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání je hotová ve všech devíti směrech v rozsahu kategorií projekce, filtrace, join, agregace a řazení (rozhodnutí 022–027). Co z ní zbývá, jsou zbylé kategorie požadavku T2 — stránkování, poddotazy a množinové operace — a HQL parser, bez kterého NHibernate → NHibernate není textovým round-tripem.
