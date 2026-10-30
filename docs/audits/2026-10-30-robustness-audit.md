# Revize odolnosti aplikace, 2026-10-30

Revize jedné otázky: **co aplikace udělá se vstupem, se kterým nepočítala** — vadným, podivným, nadměrným nebo chybějícím — a s infrastrukturou, která neodpovídá. Zaměřená na překladovou cestu (`/convert`), vedle ní na ostatní koncové body a frontend; Advisor je ze záruk vyňatý vcelku a revize ho vynechala. Nic z toho revize nečetla z kódu: každý nález stojí na spuštění (kapitola 4) a kde se dalo, i na čísle.

Nálezy jsou číslované po kapitolách a označené závažností jako v revizi [2026-10-18](2026-10-18-whole-repository-audit.md) — **kritické**, **závažné**, **drobné**. Co revize našla, v téže změně i opravila, kde oprava nevolí; kde volí, vzniklo rozhodnutí (197, 198, 199). U každého nálezu stojí, co se s ním stalo; kapitola 5 to shrnuje.

## Zafixované verze a stav, ke kterému revize platí

Pracovní kopie ke dni 2026-10-30 na MIS2, commit **`15838f6` „Tighten the web-channel prompt, add the agentic variant, keep chat time out of the comparison"**, strom čistý. .NET SDK 10, `Microsoft.Data.SqlClient` 7.0.2, SQL Server 2025 Express (`localhost\SQLEXPRESS`) s databází `ORMConvertorTests`; bez Dockeru. Sondy překladu běžely v procesu testovací sady, sondy rozhraní proti instanci spuštěné z kopie sestavení `Release` na vlastním portu, v prostředí `Production` a bez katalogu — tedy bez frontendu, který sestavení do `bin` nekopíruje.

---

## 1. Překladová cesta

Sonda prohnala **14 148 převodů** — 393 vstupů × 36 směrů — orchestrací `ConversionHandler.Convert` bez katalogu: syntakticky vadné texty všech osmi jazyků vstupu, syntakticky správné texty s chybějícími entitami, vlastnostmi, klíči a cizími třídami, prázdné a bílé jednotky, BOM, `NUL`, emoji, tisíce členů `IN`, sloupců, joinů, `OR` a parametrů, cyklické `<include>` a `extends` MyBatisu, DTD s entitou, neznámý kořen XML, dvojice a trojice jednotek s neúplnými vazbami. Souběžně 400 převodů vstupů matice v šestnácti vláknech proti sériovému základu.

### 1.1 Vlastněný člen klíče ve fluent konfiguraci EF Core shodil celý požadavek — závažné

`b.Entity<A>().OwnsOne(x => x.Id)` — EF Core vlastní chyba, přesto přijatelný text — vypadl ze tří ze 36 směrů výjimkou `InvalidOperationException: Sequence contains no matching element`: vyloučení podle rozh. 177 vzalo vlastnost `Id` z entity, část klíče nad ní zůstala, a buildery Hibernatu, EclipseLinku a MyBatisu psaly přístupovou metodu pro člena, který entita neměla (`AbstractJpaEntityBuilder.FinalizeBuild`, `MyBatisEntityBuilder.FinalizeBuild`). Jediný vstup z 393, který takhle skončil; všechny ostatní vady se staly záznamem `Failure`, `Loss` nebo `Incompleteness` té jednotky.

*Opraveno:* `RemoveProperty` bere s vlastností i část klíče nad ní (klíč bez částí je `null`); `Combined/OwnedKeyMemberTest` do všech šesti cílů.

### 1.2 Každá výjimka mimo přetečení zásobníku byla 400 pro celý požadavek, bez stopy na instanci — závažné

Orchestrace chytala jen `InsufficientExecutionStackException` (rozh. 191). Cokoli jiného z parseru, doplnění nebo builderu zahodilo artefakty a záznamy všech jednotek požadavku, odpovědělo 400 s textem výjimky, který není o vstupu, a nezalogovalo nic — `Endpoints.Failure` text jen vracela. Chyba programu a chyba vstupu byly k nerozeznání jak pro uživatele, tak pro provozovatele.

*Rozhodnuto a provedeno:* rozh. [197](../decisions/197-a-defect-of-the-tool-refuses-the-unit-entity-or-query-with-a-record-and-a-log-line-and-400-stays-for-the-request-itself.md) — záchyt po jednotce v obou průchodech, po entitě v `Build()` a po dotazu, `Failure` s větou *vada nástroje, ne vstupu*, výjimka v `ConversionResult.Defects` a v logu (`Error`), 400 jen chybě požadavku samého.

### 1.3 Co drželo — bez nálezu

Všechny syntaktické chyby pěti dotazových jazyků, Javy, C# i XML jsou `Failure` jednotky s řádkem a sloupcem; DTD `XDocument.Parse` odmítne; neznámý framework je 400 s větou; strop zanoření a strop délky textu (rozh. 191, 194) zastavily vše, co jim revize nabídla. Souběh: 400 převodů v šestnácti vláknech dal text artefaktů i záznamů bajtově shodný se sériovým během a žádnou výjimku; v překladových projektech není sdílený měnitelný statický stav (jen `FrozenSet`, `Regex` a neměnné množiny).

## 2. Katalog

### 2.1 Nedostupný katalog zdržel každý požadavek o 37 s — závažné

Instance s katalogem na adrese, která neodpovídá: převod jedné entity trval **37,5 s** (`catalogReadMilliseconds` 37 299) a skončil 200 se stavem `Unreachable`; další požadavek 0,1 s (pool SqlClientu po selhání blokuje pokusy), po uplynutí blokace zase desítky sekund. Žádná mez v řetězci, žádné zrušení, frontend čeká bez omezení.

*Rozhodnuto a provedeno:* rozh. [198](../decisions/198-the-catalog-connection-waits-a-bounded-time-and-table-names-go-to-the-catalog-in-batches-under-the-parameter-limit.md) — `Connect Timeout` 5 s a bez opakování tam, kde řetězec mlčí; změřeno, že mez drží jen se zdrojem dat s portem nebo protokolem (5,0 s), bez nich 27 s (odpadlo jen opakování); README to provozovateli říká.

### 2.2 Víc než 2100 jmen tabulek v jednom příkazu by shodilo čtení katalogu — závažné

Čtečka posílala každé jméno jako parametr jednoho příkazu; SQL Server přijme nejvýš 2100. Projekt od zhruba tisíce entit (dva kandidáti jména na entitu) by dostal `SqlException`, čtení by selhalo vcelku a záznam by řekl *katalog nešlo přečíst* — mez velikosti vykázaná jako výpadek. Odvozeno z kódu a z dokumentované meze serveru; proti serveru změřeno až opravou.

*Rozhodnuto a provedeno:* rozh. 198 — dávky nejvýš 2000 jmen v každém z pěti dotazů i v sondě junction tabulek; `SqlServerCatalogReaderTest.ReadsMoreNameCandidatesThanOneCommandTakes` (2501 kandidátů proti SQLEXPRESS).

### 2.3 Co drželo — bez nálezu

Jména tabulek jdou do `sys.*` jako parametry `nvarchar(128)`, ne spojováním textu; selhání čtení je `Incompleteness` s textem výjimky začerněným proti vlastnímu řetězci (rozh. 160) a překlad pokračuje.

## 3. Rozhraní, archiv a velikost vstupu

### 3.1 Odmítnutí požadavku se nelogovalo — závažné

Jednotka `null` v `sources` a `files: null` v archivu odpověděly 400 s textem `Object reference not set to an instance of an object.`; log instance zůstal u řádků startu. Totéž místo jako nález 1.2.

*Rozhodnuto a provedeno:* rozh. 197 — `sources` a `files` se kontrolují před prací a odmítnutí jmenuje, co klient změní (*Source unit 2 is null; a unit declares a content type and a content.*); každé odmítnutí je řádek logu `Warning`, s výjimkou, byla-li.

### 3.2 Jména položek archivu šla do ZIP doslova — závažné

`../../evil.txt` a `/etc/passwd` prošly beze změny (hrozba 3 modelu hrozeb, dosud s ochranou *nic*); prázdné jméno a `C:\abs\p.txt` shodily celý archiv s 400 a textem `ZipArchive`, který položku nejmenuje.

*Rozhodnuto a provedeno:* rozh. [199](../decisions/199-an-archive-entry-is-a-relative-path-and-any-other-name-refuses-the-archive-by-name.md) — položka je relativní cesta bez `..` a bez znaků, které jméno souboru nesmí nést; jiné jméno odmítne celý archiv s 400, které položku jmenuje.

### 3.3 Čas překladu roste s počtem jednotek nadlineárně — drobné

Entity EF Core do NHibernatu, jedna entita s referencí na další na jednotku, přes HTTP:

| Jednotek | Čas | Odpověď |
|---|---|---|
| 2 000 | 1,3–2,1 s (podle cíle) | 4–7 MB |
| 5 000 | 6,6 s | 14 MB |
| 10 000 | 30,7 s | 27 MB |
| 20 000 | 149 s | 55 MB |

Od pěti tisíc jednotek zhruba kvadraticky. Pod nárokem S3 pro projekty, které nástroj dostává (největší vstup experimentu má stovky jednotek), a běh mezí (rozh. 182) má osu počtu entit; limit počtu a velikosti jednotek model hrozeb vede jako nenárokovaný. Kde čas vzniká, revize nehledala.

*Otevřená položka* (`open-items.md`, *Užitečné, ne nutné*).

### 3.4 Co drželo — bez nálezu

Tělo 40 MB dostane 413 z výchozího stropu Kestrelu (30 MB); neznámý typ obsahu, neznámý dialekt a prázdný seznam jednotek jsou 200 se záznamy (rozh. 045); vadné nebo chybějící tělo a chybějící `content` jednotky jsou prázdná 400 rámce; `text/plain` je 415; šestnáct souběžných požadavků dalo šestnáct 200. Frontend čte `detail` odpovědi `ProblemDetails` a chybu ukáže u tlačítka; archiv projektu rozbaluje sám a nečitelný soubor vyjmenuje (rozh. 136).

## 4. Co bylo ověřeno spuštěním

- **Sonda překladu v procesu:** throwaway test v `Tests/` (smazaný), 393 vstupů × 36 směrů, 5,3 s celkem, 3 výjimky (nález 1.1), žádný převod nad 2 s; 400 souběžných převodů vstupů matice v šestnácti vláknech bez rozdílu proti sériovému běhu; sonda byla smazaná před opravou a případ 1.1 po ní drží `Combined/OwnedKeyMemberTest`.
- **Sonda rozhraní:** instance z kopie sestavení `Release` (`Production`, bez katalogu) na vlastním portu; `curl` s těly z kapitoly 3; druhá instance s katalogem `10.255.255.1` pro nález 2.1; třetí po opravě (27,5 s bez portu, log s řádkem odmítnutí).
- **Sonda SqlClientu:** konzolový projekt mimo řešení s `Microsoft.Data.SqlClient` 7.0.2, `SqlConnection.Open()` nad pěti řetězci (tabulka v rozh. 198).
- **Sada po opravách:** `dotnet test` nad celým `Tests.csproj` proti SQLEXPRESS: **13 438 testů, 0 selhaných, 11 623 prošlých, 1 815 přeskočených** — soudce LDBC, běh FIBEN, buňky `Omitted` a skupiny bez databáze případových studií, jak README sady vyjmenovává. Javová sada neběžela: změny se dotýkají orchestrace a rozhraní, jejichž kontrakt drží `Tests/Api`, a žádného artefaktu, který javová sada soudí.

## 5. Co z revize plyne

### Opravy, provedené v téže změně

- Část klíče odchází s vyloučenou vlastností (1.1), `Combined/OwnedKeyMemberTest`.
- Kontrola `sources` a `files` před prací (3.1).

### Rozhodnutí, napsaná a provedená

- [197](../decisions/197-a-defect-of-the-tool-refuses-the-unit-entity-or-query-with-a-record-and-a-log-line-and-400-stays-for-the-request-itself.md) — vada nástroje jako záznam jednotky, entity či dotazu a řádek logu (1.2, 3.1).
- [198](../decisions/198-the-catalog-connection-waits-a-bounded-time-and-table-names-go-to-the-catalog-in-batches-under-the-parameter-limit.md) — omezené čekání na katalog a dávky jmen (2.1, 2.2).
- [199](../decisions/199-an-archive-entry-is-a-relative-path-and-any-other-name-refuses-the-archive-by-name.md) — jména položek archivu (3.2).

### Práce a delší horizont

- Nadlineární čas s počtem jednotek a limit počtu a velikosti jednotek (3.3) — `open-items.md`, *Užitečné, ne nutné*.
- Log vad nemá test (rozh. 197, *Co zůstává*); `Command Timeout` dotazů do katalogu zůstává výchozí (rozh. 198, *Co rozhodnutí neřeší*).
- Hrozba 2 modelu hrozeb drží dál to, co revize nenašla: limit počtu a velikosti jednotek a omezení frekvence požadavků (hrozba 5).
