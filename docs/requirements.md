# Požadavky na další vývoj

---

## Funkční požadavky

**F1 – Reprezentace komplexních identifikátorů v IR.** Jedna entita může mít identifikátor tvořený jednou nebo více vlastnostmi. IR musí zachovat pořadí částí klíče, jejich datové typy, názvy sloupců a strategii generování.
*Ověření:* automatický test s jednoduchým klíčem a nejméně třemi složenými klíči o dvou až čtyřech částech.

**F2 – Překlad komplexních identifikátorů ve všech podporovaných frameworcích.** Parsovat a generovat složené primární klíče ve všech podporovaných .NET a Java frameworcích mechanismem daného frameworku (více klíčových sloupců, `@EmbeddedId`, `@IdClass`, XML mapování).
*Ověření:* pro každý framework nejméně dva obousměrné překladové scénáře; výstup se musí zkompilovat a obsahovat všechny části klíče.

**F3 – Cizí klíče odkazující na složené identifikátory.** Překlad vztahů s vícesloupcovým cizím klíčem, včetně 1:1, 1:N a N:M přes spojovací tabulku.
*Ověření:* alespoň jeden zkompilovaný a spuštěný test pro každý typ vztahu.

**F4 – Načtení metadat z databázového schématu.** Pokud vstupní kód neobsahuje úplná mapovací metadata, systém načte z připojené databáze alespoň tabulky, sloupce, datové typy, nullabilitu, primární a cizí klíče, unikátní omezení, délku, přesnost a měřítko.
*Ověření:* proti testovací databázi správně získáno ≥ 95 % sledovaných metadat; odchylky explicitně zaznamenané.

**F5 – Sloučení metadat z více zdrojů.** Slučování informací ze zdrojového kódu, anotací, externích mapovacích souborů a databázového katalogu do jediné IR, s dokumentovanou prioritou zdrojů a hlášením konfliktů.
*Ověření:* testy pokrývají nejméně čtyři konfliktní scénáře (nullabilita, název sloupce, datový typ, primární klíč).

**F6 – Generování úplného mapování z neúplného vstupu.** Z frameworku s neúplnou/implicitní mapovací informací (Dapper, MyBatis) vytvořit úplné mapování cílového frameworku doplněním z databáze.
*Ověření:* z entity obsahující jen názvy a jazykové typy vlastností vznikne kompilovatelná cílová entita se správnými názvy tabulek a sloupců, klíči a omezeními.

**F7 – Podpora Hibernate.** Parser a generátor pro Hibernate: entity, tabulky a schémata, sloupce, jednoduché a složené klíče, základní vztahy, read-only dotazy alespoň v JPQL nebo HQL.
*Ověření:* minimálně 20 překladových testů entit a 15 testů dotazů; generované projekty se sestaví.

**F8 – Podpora MyBatis.** Parser a generátor pro MyBatis: Java třídy, XML nebo anotované mapování, resultMap, parametry, dynamicky parametrizované read-only dotazy, ručně psané SQL joiny.
*Ověření:* minimálně 20 testů mapování a 15 testů dotazů, z toho alespoň pět vícetabulkových.

**F9 – Podpora EclipseLink.** Parser a generátor pro EclipseLink/JPA: anotované entity, složené klíče, vztahy, JPQL dotazy v rozsahu společné IR.
*Ověření:* minimálně 20 testů mapování a 15 testů dotazů; alespoň pět testů překladu mezi Hibernate a EclipseLink.

**F10 – Cross-ecosystem překlad mezi .NET a Java.** Překlad entity, mapování a podporovaného read-only dotazu z libovolného implementovaného .NET frameworku do libovolného Java frameworku a opačně.
*Ověření:* testovací matice s alespoň jedním end-to-end scénářem pro každou dvojici ekosystémů, nejméně 30 cross-ecosystem překladů celkem.

**F11 – Validace cílových artefaktů.** Před předáním výsledku ověřit strukturální úplnost IR, omezení cílového frameworku a syntaktickou správnost generovaných souborů. Nepodporované konstrukce nesmí být potichu vynechány.
*Ověření:* každý neúspěšný překlad vrátí strukturovanou diagnostiku (framework, artefakt, chybějící vlastnost, důvod selhání).

**F12 – Spustitelná testovací sada pro Java frameworky.** Automatizovaná sada, která sestaví a spustí vygenerovaný kód pro Hibernate, MyBatis a EclipseLink nad stejnou databází a daty.
*Ověření:* spustitelná jedním příkazem, alespoň 60 Java testů, z toho nejméně 20 integračních.

**F13 – Diferenční ověření výsledků dotazů.** Spustit zdrojovou i přeloženou variantu dotazu a porovnat normalizované výsledky; pořadí zohlednit jen je-li dané dotazem, konfigurovatelná práce s numerickou přesností a null hodnotami.
*Ověření:* nejméně 30 dvojic dotazů se shodným výsledkem; záměrně chybný překlad musí být odhalen.

**F14 – Překlad celých tříd a dávkových vstupů v UI.** Vložení či nahrání jedné nebo více celých tříd, případně archivu projektu; volba zdrojového a cílového frameworku; validace a překlad. Rozhraní zobrazí vstup, IR, výstup a diagnostiku po jednotlivých souborech.
*Ověření:* scénář zpracuje projekt s alespoň deseti entitami a pěti dotazy bez ručního zadávání každého artefaktu.

**F15 – Výběr cílového frameworku a optimalizace.** Uživatel buď přímo zvolí cílový framework, nebo předá množinu kandidátů a nechá systém doporučit na základě měřeného času, paměti, vah dotazů a omezení počtu frameworků.
*Ověření:* pro stejný workload zobrazit výsledky variant „vše Hibernate", „vše MyBatis", „vše EclipseLink", nejméně jedné heuristiky a ILP optimalizace.

## Systémové a nefunkční požadavky

**S1 – Modulární rozšiřitelnost.** Přidání frameworku nesmí vyžadovat změny parserů/generátorů ostatních frameworků ani párové překladače; nový framework se přidává přes definované parserové a generátorové rozhraní.

**S2 – Determinismus a opakovatelnost.** Při stejném vstupu, konfiguraci, schématu a verzi nástroje vytvoří pravidlový překladač byte-wise shodné nebo po normalizaci ekvivalentní artefakty ve 100 % opakovaných běhů.

**S3 – Výkon překladu.** Překlad projektu se 100 entitami a 100 dotazy (bez benchmarkingu) skončí na referenčním stroji do 30 sekund; načtení metadat databáze se měří a reportuje odděleně.

**S4 – Izolace a bezpečnost spouštění.** Cizí a generovaný kód se sestavuje a spouští v izolovaném prostředí s omezením CPU, paměti a času. Přihlašovací údaje k databázi nesmí být v generovaných zdrojových souborech ani v lozích.

**S5 – Přenositelné a reprodukovatelné prostředí.** Celý systém, databáze, .NET a Java testovací projekty a experimentální pipeline spustitelné dokumentovanou kontejnerovou konfigurací; čisté prostředí reprodukuje testy jedním hlavním příkazem.

**S6 – Pozorovatelnost a auditovatelnost.** Každý překlad má identifikátor běhu a strojově čitelný záznam: zdroje metadat, provedená pravidla, varování, chyby, verze frameworků, výsledky kompilace a testů.

**S7 – Uživatelská přívětivost.** Validace před spuštěním, průběžný stav zpracování, zvýraznění chyb na úrovni souboru a řádku, stažení kompletního výstupního projektu. Základní scénář (nahrát projekt → zvolit cíl → přeložit → zobrazit chyby) dokončitelný nejvýše v pěti krocích.

## Experimentální požadavky (diplomová práce / rozšíření článku)

**E1 – Reálná případová studie.** Alespoň jedna, lépe dvě open-source aplikace s netriviálním ORM přístupem k datům; každá ≥ 15 entit, vztahy, složený klíč, ≥ 20 read-only dotazů.

**E2 – Matice překladů.** Minimálně .NET → Java, Java → .NET a Java → Java, rozdělené podle kategorií dotazů: projekce, filtrace, joiny, agregace, stránkování, poddotazy, množinové operace.

**E3 – Metriky korektnosti.** Podíl parsovatelných, kompilovatelných a spustitelných výstupů, funkční ekvivalence, úplnost přenesených mapovacích vlastností.

**E4 – LLM baseline.** Porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu; uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady.

**E5 – RAG / agentní varianta.** Varianta poskytující modelu dokumentaci cílového frameworku, informace o schématu a relevantní příklady; porovnat se stejným LLM bez RAG.

**E6 – Ablace.** Samostatně vyhodnotit vliv databázových metadat, validace, opravného cyklu a RAG – ukázat, která komponenta zlepšení skutečně způsobuje.

**E7 – Baseline optimalizace.** ILP porovnat proti každému jednotlivému frameworku, nejméně jedné ruční heuristice a případně greedy strategii; použít i neuniformní váhy odvozené z reálné četnosti dotazů.

Pro LLM experimenty se počítá se zero-shot, few-shot a strukturovanější RAG/agentní variantou a s metrikami syntaktické správnosti, funkční ekvivalence, času a nákladů.

## Mapování na současný stav

| Požadavek | Stav | Poznámka |
|---|---|---|
| F1 | chybí, návrh hotový | `docs/design/001` – `PrimaryKey`/`PrimaryKeyPart` |
| F2 | chybí | .NET část navazuje na 001; Java část závisí na F7–F9 |
| F3 | chybí, návrh hotový | `docs/design/001` – `Relation` s `ColumnPairs`, N:M přes junction entitu |
| F4 | chybí úplně | dnes žádné čtení DB katalogu (viz current-state) |
| F5 | částečně | agregace více parserů do jedné IR funguje (NHibernate C# + XML); chybí DB katalog jako zdroj, priority zdrojů a hlášení konfliktů |
| F6 | chybí | závisí na F4 + F5 |
| F7–F9 | chybí | nový Java ekosystém, zatím nezačato |
| F10 | chybí | pozor: typový model IR je dnes CLR-specifický (`CLRTypeModel`, `CLRTypeConvertor` v `Common`) – pro Javu bude potřeba jazykově neutrální reprezentace typů (článek, §5.2 „LangType") |
| F11 | nesplněno | validace úplnosti IR neexistuje; nepodporované konstrukce se dnes potichu vynechávají (Dapper builder přeskakuje PK/FK); strukturovaná diagnostika chybí |
| F12–F13 | chybí | testovací infrastruktura pro Javu a diferenční běh dotazů neexistuje |
| F14 | částečně | frontend přijímá ručně vkládané artefakty po jednom; nahrání souborů/archivu projektu a per-file diagnostika chybí |
| F15 | částečně | přímá volba cíle i ILP advisor fungují (jen Dapper a EF Core); srovnání „vše X" a heuristik se uživateli nezobrazuje |
| S1 | drží | zajištěno parser/builder architekturou; hlídat při přidávání Java frameworků |
| S2 | pravděpodobně drží, netestováno | šablonové generování je deterministické, ale žádný test opakovatelnosti neexistuje |
| S3 | neměřeno | článek uvádí škálovací čísla jen pro původní prototyp |
| S4 | nesplněno | benchmarky běží in-process bez limitů CPU/paměti/času; fallback connection string se `sa` heslem je natvrdo v `AdvisorRunCoordinator.cs` (ř. 42) – odstranit |
| S5 | částečně | docker-compose (aplikace + DB) existuje; jednopříkazová reprodukce testů a experimentů ne |
| S6 | chybí | žádný identifikátor běhu ani strojově čitelný záznam překladu |
| S7 | částečně | základní scénář v UI projde; validace před spuštěním, průběžný stav, chyby na úrovni souboru/řádku a stažení výstupního projektu chybí |
| E1–E7 | nezačato | E7 přímo navazuje na existující ILP advisor |