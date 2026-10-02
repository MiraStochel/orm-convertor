# Model hrozeb

Čemu je nástroj vystavený a co ho chrání — **současný stav**, ne plán. Důvody opatření nesou rozhodnutí, nárok verze [`architecture.md`](./architecture.md) §9.

## Předpoklad nasazení

Žádný koncový bod nevyžaduje přihlášení; schéma ani politika autentizace neexistují. Instance běží v důvěryhodné síti nebo za reverzní proxy, která přístup řídí ([`ORMConvertor/README.md`](../ORMConvertor/README.md), *Deploying a real instance*). **Instance vystavená přímo do internetu je mimo předpoklad**; `AllowedHosts` je `*`.

## Co je chráněné

| Aktivum | Kde je | Ochrana dnes |
|---|---|---|
| Připojovací řetězce (katalog, Advisor) | proměnné prostředí, v Development user secrets; nikdy v repozitáři | nejdou do artefaktu (rozh. [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md), `ArtifactCarriesNoCredentialsTest`) ani do logu (ten nese počty a časy) |
| Databáze za nimi | vně procesu | jen řetězce; oprávnění účtu určuje prostředí |
| Vložený kód | paměť po dobu požadavku; rozpracovaný vstup v `localStorage` prohlížeče (rozh. [056](./decisions/056-work-in-progress-input-stays-in-the-browser.md)) | neukládá se, nikam nejde, do logu ne; kopii maže *Clear*, na sdíleném profilu prohlížeče ji vidí další uživatel |
| CPU a paměť hostitele | proces | nic — hrozby 1 a 2 |

## Vstupní body

| Cesta | Co přichází | Co systém udělá | Co ho chrání |
|---|---|---|---|
| `POST /convert` | C#, Java, XML, SQL, HQL, JPQL; jednotek libovolně, velikost jen do výchozího stropu Kestrelu | čte: Roslyn, `XDocument.Parse`, `TSql160Parser` a vlastní rekurzivní sestup (`JavaLexer` + `JavaClassReader`, `NHibernateHqlQueryParser`, `JpqlQueryParser`); **nic nekompiluje ani nespouští** | strop zanoření 128 změřený smyčkou nad tokeny před sestupem, u všech pěti parserů (rozh. [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md)); DTD v `XDocument.Parse` zakázané (bez XXE); vydané XML escapuje zapisovač prvků (rozh. [046](./decisions/046-xml-mapping-written-through-an-element-writer.md)) |
| `POST /archive` | dvojice *jméno + obsah* | ZIP v paměti | nic — jména beze změny, velikost = vstup |
| `POST /advisor/run` | entity a dotazy | **zkompiluje Roslynem a spustí v procesu** proti nastavené databázi | nic — kolektibilní `AssemblyLoadContext` je úklid, ne izolace; bez limitu CPU, paměti, času |
| `POST /advisor-test` | matice nákladů a rozměry | P/Invoke do GLPK, pole podle rozměrů z požadavku | kontroly .NET; nesmysl → 400 |
| `GET /samples`, `/samples-advisor`, `/examples`, `/ldbc`, `/required-content`, `/required-content-advisor` | nic | statická data ze sestavení | — |
| `/orm/…` | nic | `wwwroot` z gitu (rozh. [032](./decisions/032-frontend-as-static-pages-without-a-build.md)) | — |

## Hrozby

| Hrozba | Vstupní bod | Co ohrožuje | Co chrání dnes | Co verze nenárokuje |
|---|---|---|---|---|
| **1. Spuštění cizího kódu Advisorem** (jediné místo, kde se cizí kód spouští) | `/advisor/run` | proces s jeho právy a připojením do Advisor databáze | nic; zužuje ji jen potřeba připojení a `libadvisor.so` (jen v Dockeru) | první větu S4 — §9, oblast 1 |
| **2. Vyčerpání zdrojů překladem** | `/convert` | CPU, paměť; hluboké zanoření **shodí proces** (`0xC00000FD`, v .NET nezachytitelné) | strop zanoření → záznam `Failure` s řádkem a sloupcem | limit počtu a velikosti jednotek; odolnost parserů proti nepřátelskému vstupu (neanalyzovaná, neměřená); S3 platí jen pro běžnou zátěž |
| **3. Jména položek v archivu** | `/archive` | soubory uživatele při rozbalení (`../`, absolutní cesta) | nic; jediné místo, kde nástroj vydá soubor se jménem, které neurčil (jinde je jméno popiskem klienta, rozh. [033](./decisions/033-shape-of-the-static-frontend-screens.md)) | chování rozbalovače |
| **4. Text chyby z infrastruktury** | všechny (`400` s textem výjimky); katalog záznamem s `ex.Message` | název serveru či instance (údaje ne) | předpoklad důvěryhodné sítě | veřejnou instanci — tam je to únik |
| **5. Neomezený počet požadavků** | všechny | stroj; s hrozbou 2 stačí běžný klient | nic | omezení frekvence |

**Strop zanoření (hrozba 2)** má výchozí hodnotu 128; provozovatel ho mění klíčem `Parsing__MaxNestingDepth`, `0` ho vypíná a hrozba pak platí tak, jak je změřená níž. Tělo požadavku ho měnit nemůže. Účinnou hodnotu vrací každá odpověď `/convert` (`maxNestingDepth`).

Hloubky bez stropu, změřené 2026-09-21 (Windows, zásobník 1 MB) — poslední bez pádu / první s pádem: HQL a JPQL závorky 2048/3000, poddotazy 1024/2048; javová třída generika 3000/4096, vnitřní třídy 2048/3000; **`TSql160Parser` (Dapper, MyBatis) 1024/2048, nejdřív ze všech**; `CSharpSyntaxTree.ParseText` ve 4096 vydá diagnostiku, padá na 6000; `XDocument.Parse` čte smyčkou a nepadá ani na 65536.

**Co hrozba není:** vstup neopouští proces a kopie v prohlížeči se nikam neodesílá; do artefaktu se nedostane nic, co nástroj nedostal (rozh. [040](./decisions/040-boundary-of-the-handed-over-artifact.md)), hlídá test; heslo `sa` v `docker-compose.yml` a ve workflow je vývojový údaj zahoditelné instance — S4 zakazuje údaje v generovaných artefaktech a v logu, ne v popisu prostředí.

## Co by se dalo udělat, kdyby se na oblast sáhlo

Není to plán — Advisor je ze záruk vyňatý vcelku; rozhodnutí patří do `decisions/`.

- **Izolace spouštění** (první věta S4): samostatný proces nebo kontejner s limitem CPU, paměti a času.
- **Časový strop** benchmarku a ILP; dnes jen `CancellationToken` klienta.
- **Limit velikosti a počtu jednotek** na `/convert` (hrozba 2); strop zanoření už platí.
- **Autentizace před Advisorem** mimo důvěryhodnou síť; překladu stačí proxy.
