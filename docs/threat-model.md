# Model hrozeb

Čemu je nástroj vystavený a co ho chrání — **současný stav**, ne plán. Důvody opatření nesou rozhodnutí, nárok verze [`architecture.md`](./architecture.md) §9.

## Předpoklad nasazení

Žádný koncový bod nevyžaduje přihlášení; schéma ani politika autentizace neexistují. Instance běží v důvěryhodné síti nebo za reverzní proxy, která přístup řídí ([`ORMConvertor/README.md`](../ORMConvertor/README.md), *Deploying a real instance*). **Instance vystavená přímo do internetu je mimo předpoklad**; `AllowedHosts` je `*`.

## Co je chráněné

| Aktivum | Kde je | Ochrana dnes |
|---|---|---|
| Připojovací řetězce (katalog, Advisor) | proměnné prostředí, v Development user secrets; nikdy v repozitáři | nejdou do žádného generovaného zdroje — artefaktu (rozh. [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md)) ani harnessu Advisoru, který řetězec bere za běhu — ani do textu o běhu: záznam o nečitelném katalogu, chybová odpověď a log (jen konzole) nesou text výjimky přes začernění hesla, jména, celého řetězce a při integrovaném přihlášení účtu procesu (rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)). Co si cizí kód Advisoru zakóduje sám, začernění nechytí — hrozba 1 |
| Databáze za nimi | vně procesu | jen řetězce; oprávnění účtu určuje prostředí |
| Vložený kód | paměť po dobu požadavku; rozpracovaný vstup v `localStorage` prohlížeče (rozh. [056](./decisions/056-work-in-progress-input-stays-in-the-browser.md)) | neukládá se, nikam nejde, do logu ne; kopii maže *Clear*, na sdíleném profilu prohlížeče ji vidí další uživatel |
| CPU a paměť hostitele | proces | nic — hrozby 1 a 2 |

## Vstupní body

| Cesta | Co přichází | Co systém udělá | Co ho chrání |
|---|---|---|---|
| `POST /convert` | C#, Java, XML, SQL, HQL, JPQL; jednotek libovolně, velikost jen do výchozího stropu Kestrelu | čte: Roslyn, `XDocument.Parse`, `TSql160Parser` a vlastní rekurzivní sestup (`JavaLexer` + `JavaClassReader`, `NHibernateHqlQueryParser`, `JpqlQueryParser`); **nic nekompiluje ani nespouští** | strop zanoření 128 změřený smyčkou nad tokeny před sestupem, u všech pěti parserů (rozh. [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md)); DTD v `XDocument.Parse` zakázané, u mapperu MyBatisu ignorované a bez resolveru (bez XXE); vydané XML escapuje zapisovač prvků (rozh. [046](./decisions/046-xml-mapping-written-through-an-element-writer.md)) |
| `POST /archive` | dvojice *jméno + obsah* | ZIP v paměti | nic — jména beze změny, velikost = vstup |
| `POST /advisor/run` | entity a dotazy | **zkompiluje Roslynem a spustí v procesu** proti nastavené databázi | jen mez jednoho spuštění dotazu — `Command Timeout` harnessu z politiky měření (rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md)); kolektibilní `AssemblyLoadContext` je úklid, ne izolace; bez limitu CPU, paměti a délky celého běhu |
| `POST /advisor-test` | matice nákladů a rozměry | P/Invoke do GLPK, pole podle rozměrů z požadavku | kontroly .NET; nesmysl → 400 |
| `GET /samples`, `/samples-advisor`, `/examples`, `/ldbc`, `/fiben`, `/required-content`, `/required-content-advisor` | nic | statická data ze sestavení | — |
| `GET /openapi/v1.json` (Swagger UI jen v `Development`) | nic | dokument rozhraní ze sestavení | — |
| `/orm/…` | nic | `wwwroot` z gitu (rozh. [032](./decisions/032-frontend-as-static-pages-without-a-build.md)) | — |

## Hrozby

| Hrozba | Vstupní bod | Co ohrožuje | Co chrání dnes | Co verze nenárokuje |
|---|---|---|---|---|
| **1. Spuštění cizího kódu Advisorem** (jediné místo, kde se cizí kód spouští) | `/advisor/run` | proces s jeho právy a připojením do Advisor databáze | mez jednoho spuštění dotazu (146); zužuje ji jen potřeba připojení a `libadvisor.so` (jen v Dockeru) | první větu S4 — §9, oblast 1 |
| **2. Vyčerpání zdrojů překladem** | `/convert` | CPU, paměť; hluboké zanoření **shodí proces** (`0xC00000FD`, v .NET nezachytitelné) | strop zanoření nad textem (závorky, CASE až po END, řada prefixových operátorů) a nad modelem po čtení → záznam `Failure`; čtečka, které dochází zásobník, odmítne jednotku | limit počtu a velikosti jednotek; artefakt, jehož délka roste exponenciálně s hloubkou vstupu ([položka](./open-items.md#délka-artefaktu-roste-exponenciálně-s-hloubkou-vstupu)); řetěz mezivýsledků ze zdroje EF Core, jehož čtení roste exponenciálně s délkou ([položka](./open-items.md#řetěz-mezivýsledků-ze-zdroje-ef-core-se-čte-exponenciálně)); jinak odolnost parserů proti nepřátelskému vstupu neanalyzovaná; S3 platí jen pro běžnou zátěž |
| **3. Jména položek v archivu** | `/archive` | soubory uživatele při rozbalení (`../`, absolutní cesta) | nic; jediné místo, kde nástroj vydá soubor se jménem, které neurčil (jinde je jméno popiskem klienta, rozh. [033](./decisions/033-shape-of-the-static-frontend-screens.md)) | chování rozbalovače |
| **4. Text chyby z infrastruktury** | všechny (`400` s textem výjimky); katalog záznamem s textem výjimky | název serveru či instance (přihlašovací údaje ne — začernění, rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)) | předpoklad důvěryhodné sítě | veřejnou instanci — tam je to únik |
| **5. Neomezený počet požadavků** | všechny | stroj; s hrozbou 2 stačí běžný klient | nic | omezení frekvence |

**Strop zanoření (hrozba 2)** má výchozí hodnotu 128 a týmž číslem měří text před sestupem i model po čtení (rozh. [191](./decisions/191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md)); provozovatel ho mění klíčem `Parsing__MaxNestingDepth`, `0` ho vypíná a hrozba pak platí tak, jak je změřená níž — čtečka, které dochází zásobník, jednotku odmítne i tehdy, průchody po čtení ne. Tělo požadavku ho měnit nemůže. Účinnou hodnotu vrací každá odpověď `/convert` (`maxNestingDepth`).

Hloubky bez stropu, změřené 2026-09-21 (Windows, zásobník 1 MB) — poslední bez pádu / první s pádem: HQL a JPQL závorky 2048/3000, poddotazy 1024/2048; javová třída generika 3000/4096, vnitřní třídy 2048/3000; **`TSql160Parser` (Dapper, MyBatis) 1024/2048, nejdřív ze všech**; `CSharpSyntaxTree.ParseText` ve 4096 vydá diagnostiku, padá na 6000; `XDocument.Parse` čte smyčkou a nepadá ani na 65536. Řetězy bez závorek (součet, CASE v ELSE, `NOT NOT`, `UNION` po `UNION`), změřené 2026-10-27 se stropem vypnutým: na Windows (hostitel MIS3) první pád při 512 — CASE ze zdroje MyBatis v gramatice `TSql160Parser` —, jinak při 1024 až 2048 v gramatice, v našich čtečkách a v průchodech po čtení; v linuxovém kontejneru až při 4096.

**Čas a paměť překladu po osách**, změřené během mezí 2026-10-29 ([záznam](./runs/2026-10-29-limits.md); MIS3, linuxový kontejner, u řetězů strop vypnutý): proces překladu nespadl v žádném bodě — přetečení zásobníku a pády pro paměť byly až na cestě artefaktu cílem. Mez S3 překročil překlad ve třech řadách: řetěz mezivýsledků ze zdroje EF Core (16 článků 3,5 s, 32 přes 35 s a 8,3 GB alokováno), 512 joinů ze zdroje EF Core a projekt od 1600 entit ze zdroje EF Core či od 3200 ze zdroje Hibernate (13–22,5 s, 2–7 GB). Nejvíc paměti z prošlých bodů alokoval překlad 4096 vlastností do MyBatisu (9 GB) a 2048 vnořených poddotazů do NHibernatu (5 GB).

**Co hrozba není:** archiv projektu rozbaluje prohlížeč (rozh. [136](./decisions/136-a-project-archive-is-unpacked-by-the-browser-with-own-code.md)), takže server dostane jednotky jako z vybraných souborů a vstupní bod nepřibývá; archiv, který se rozbalí do obřích rozměrů, vyčerpá jen záložku, do které ho uživatel přidal. Vstup neopouští proces a kopie v prohlížeči se nikam neodesílá; do artefaktu se nedostane nic, co nástroj nedostal (rozh. [040](./decisions/040-boundary-of-the-handed-over-artifact.md)), hlídá test; heslo `sa` v `docker-compose.yml` a ve workflow je vývojový údaj zahoditelné instance — S4 zakazuje údaje v generovaných artefaktech a v logu, ne v popisu prostředí. Cizí artefakt podle rozh. [172](./decisions/172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md) — překlad od modelu nebo od člověka — kompiluje a spouští sada ve vlastním procesu, v kontejneru profilu `test` nebo na hostiteli autora experimentu, nikdy nástroj; hranicí je kontejner a proces na artefakt s mezí času (`foreign-run.sh`), nic víc se nenárokuje a vstupní bod nástroje nepřibývá.

## Co by se dalo udělat, kdyby se na oblast sáhlo

Není to plán — Advisor je ze záruk vyňatý vcelku; rozhodnutí patří do `decisions/`.

- **Izolace spouštění** (první věta S4): proces na každou buňku měření rozhodlo rozh. [147](./decisions/147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md) a zatím nestojí; limity CPU, paměti a práv toho procesu rozhodnuté nejsou ([`open-items.md`](./open-items.md), *Izolace spouštění cizího kódu Advisorem*).
- **Časový strop** celého běhu a ILP; dnes jen mez jednoho spuštění (146) a `CancellationToken` klienta.
- **Limit velikosti a počtu jednotek** na `/convert` (hrozba 2); strop zanoření už platí.
- **Autentizace před Advisorem** mimo důvěryhodnou síť; překladu stačí proxy.
