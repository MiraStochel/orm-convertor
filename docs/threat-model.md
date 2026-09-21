# Model hrozeb

**Účel:** souhrnný pohled na to, čemu je nástroj vystavený — vstupní body, co po nich přichází, co s tím systém udělá a která hranice ho chrání. Popisuje **současný stav**, ne plán opatření; co by se dalo udělat, je poslední kapitola a je vědomě krátká. Jednotlivá opatření a jejich odůvodnění bydlí v rozhodnutích ([029](./decisions/029-database-connection-is-the-consumer-projects-fact.md), [040](./decisions/040-boundary-of-the-handed-over-artifact.md)), nárok verze v [`architecture.md`](./architecture.md) §9.

Důvod, proč tenhle dokument existuje: opatření v projektu jsou, ale rozptýlená, a **rozptýlená opatření nejsou model hrozeb**. Bez pojmenování toho, čemu je nástroj vystavený, nejde napsat ani jedna věta o bezpečnosti do textu práce.

---

## Předpoklad nasazení

Nástroj je jedna webová aplikace bez autentizace: **žádný koncový bod nevyžaduje přihlášení a žádný na to nemá schéma ani politiku.** Není to opomenutí — je to předpoklad nasazení: instance běží v důvěryhodné síti nebo za reverzní proxy, která přístup řídí (viz `ORMConvertor/README.md`, sekce *Deploying a real instance*). Do 2026-08-22 registroval `Program.cs` autorizační služby ze šablony ASP.NET, které nechránily nic; byly odstraněny právě proto, aby stav nevypadal jako ochrana.

Všechno níž se čte proti tomuhle předpokladu. **Instance vystavená přímo do internetu je mimo něj** a `AllowedHosts` je navíc `*`.

## Co je chráněné

| Aktivum | Kde je | Čím je dnes chráněné |
|---|---|---|
| Připojovací řetězce ke katalogu a k Advisor databázi | serverová konfigurace (proměnné prostředí, v Development user secrets); v repozitáři nikdy | Do generovaného artefaktu se nevypisují (rozhodnutí [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md)), hlídá `ArtifactCarriesNoCredentialsTest`; do logů také ne — logy nesou počty a časy |
| Databáze, ke které ty řetězce vedou | vně procesu | Nic víc než ty řetězce; oprávnění účtu je věc prostředí |
| Zdrojový kód, který uživatel vloží | v paměti procesu po dobu požadavku; kopie rozpracovaného vstupu překladové obrazovky v `localStorage` prohlížeče uživatele (rozhodnutí [056](./decisions/056-work-in-progress-input-stays-in-the-browser.md)) | Na serveru se neukládá, nikam se neposílá a do logu se nedostane. Kopie v prohlížeči leží na stroji uživatele, pod původem té instance, takže na ni nedosáhne jiná stránka ani jiná instance; smaže ji tlačítko *Clear* na téže obrazovce. Na sdíleném prohlížečovém profilu ji do té doby přečte další uživatel |
| CPU a paměť hostitele | proces aplikace | Nic — viz hrozby 1 a 2 |

## Vstupní body

| Cesta | Co po ní přichází | Co s tím systém udělá | Co ho chrání |
|---|---|---|---|
| `POST /convert` | cizí C#, Javu, XML, SQL, HQL a JPQL, libovolný počet jednotek, bez vlastního limitu velikosti (platí výchozí strop těla požadavku v Kestrelu) | Roslyn postaví syntaktický strom C#, XML čte `XDocument.Parse`, SQL gramatika `TSql160Parser`, a **javovou stranu i oba dotazové jazyky čtou vlastní ručně psané parsery** (`JavaLexer` a `JavaClassReader`, `NHibernateHqlQueryParser`, `JpqlQueryParser` — rekurzivní sestup, tedy rekurze po hloubce vstupu); **nic se nekompiluje a nespouští** | **Hloubku zanoření hlídá od 2026-09-21 strop** (rozhodnutí [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md)): každý z pěti parserů ji změří na svém tokenovém proudu — smyčkou — a sestup nespustí, když je hlubší než 128 úrovní; platí to i pro `TSql160Parser` a Roslyn, jejichž rekurze naše není. Že překladová cesta cizí kód nekompiluje ani nespouští, je vlastnost návrhu (rozhodnutí 029); `XDocument.Parse` má ve výchozím nastavení zakázané DTD, takže XXE ani rozvoj entit nehrozí. **Emisní strana je hlídaná zvlášť:** generované XML mapování skládá zapisovač prvků, který hodnoty escapuje na jediném místě (rozhodnutí [046](./decisions/046-xml-mapping-written-through-an-element-writer.md)), takže název tabulky nebo sloupce ze vstupu nemůže změnit strukturu vydaného dokumentu. Není to hrozba tohohle modelu — artefakt dostane zpátky týž uživatel, který vstup poslal —, ale bez escapování to byla cesta, jak vstupem řídit tvar výstupu |
| `POST /archive` | seznam dvojic *jméno + obsah* od klienta | Sestaví ZIP v paměti a vrátí ho; nepřekládá nic | Nic. Jména položek se zapisují tak, jak přišla, a velikost archivu je součet toho, co klient poslal |
| `POST /advisor/run` | entity a dotazy | Přeloží je, **Roslynem zkompiluje a v procesu spustí** proti nastavené databázi a změří běh | **Nic.** Kolektibilní `AssemblyLoadContext` je úklid paměti, ne izolace: žádný limit CPU, paměti ani času, žádný samostatný proces |
| `POST /advisor-test` | matice nákladů a rozměry úlohy | Předá je přes P/Invoke nativní knihovně GLPK; pole se alokují podle čísel z požadavku | Nic nad rámec kontrol .NET; nesmyslné rozměry skončí výjimkou a odpovědí 400 |
| `GET /samples`, `/samples-advisor`, `/required-content`, `/required-content-advisor` | nic | Vrací statická data ze sestavení | — |
| statické soubory `/orm/…` | nic | `wwwroot` tak, jak je v gitu (rozhodnutí [032](./decisions/032-frontend-as-static-pages-without-a-build.md)) | — |

## Hrozby

**1. Spuštění cizího kódu Advisorem.** Zdaleka nejrizikovější místo celého systému a jediné, kde nástroj cizí kód vůbec spouští. Kdo dosáhne na `/advisor/run`, dosáhne na spuštění kódu v procesu aplikace, s jejími právy a s připojením do Advisor databáze. §9 to přiznává vyjmutím první věty S4 ze záruk — to je poctivé přiznání mezery, a tenhle dokument je ta mezera popsaná. Co ji dnes zužuje, jsou dvě věci a ani jedna není opatření: endpoint potřebuje nastavené připojení a nativní `libadvisor.so`, která se staví jen v Dockeru.

**2. Vyčerpání zdrojů překladem.** Vstup není omezený počtem jednotek ani vlastním stropem velikosti a parsery nad nepřátelským vstupem nikdo bezpečnostně neanalyzoval: patologický SQL nebo velmi široká entita jsou práce, jejíž trvání jsme neměřili. **Hloubka zanoření je ale od 2026-09-21 změřená, a to u všech čtených jazyků** (Windows, výchozí zásobník vlákna 1 MB): dost hluboký vstup **přeteče zásobník a proces spadne** (`0xC00000FD`, ukončení bez odpovědi — přetečení zásobníku se v .NET zachytit nedá). Poslední hloubka, která prošla, a první, která shodila proces: HQL a JPQL 2048/3000 na závorkách v podmínce a 1024/2048 na vnořených poddotazech, javová třída 3000/4096 na generických typech a 2048/3000 na vnitřních třídách, `TSql160Parser` 1024/2048 — **ze všech nejdřív, a je to čtecí cesta Dapperu a MyBatisu**. `CSharpSyntaxTree.ParseText` se jako jediný brání sám (ve 4096 vydá diagnostiku místo stromu), ale na 6000 padá taky. `XDocument.Parse` čte smyčkou a nepadá ani na 65536, takže mapovací XML sem nepatří. Byla to tedy jediná třída vstupu, po které instance končila — a **od 2026-09-21 je ohraničená** (rozhodnutí [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md)): každý z pěti parserů projde svůj tokenový proud smyčkou dřív, než spustí sestup, a vstup zanořený hlouběji než 128 úrovní odmítne záznamem `Failure` s řádkem a sloupcem. Grammatika, která by přetekla, se tím nespustí ([`architecture.md`](./architecture.md), §5). **Tenhle odstavec proto popisuje dvojí stav.** S výchozím nastavením je hloubka ohraničená a hluboký vstup končí záznamem. Strop je ale nastavitelný provozovatelem (`Parsing__MaxNestingDepth`) a hodnotou `0` se vypíná — na instanci s vypnutým stropem platí hrozba dál přesně v té podobě, v jaké je změřená výš, a je to vědomé rozhodnutí toho, kdo ji nastavil. Požadavek hodnotu změnit nemůže: pole v těle nemá, protože vstup, proti kterému strop chrání, by si jinak nesl i svolení ho překročit. Účinnou hodnotu vydává každá odpověď `/convert`. Měřený limit S3 (100 entit a 100 dotazů do 30 s) je tvrzení o běžné zátěži, ne o nepřátelské.

**3. Jména položek v archivu.** `/archive` zapíše do ZIPu jméno, které dostal. Co s `../` nebo s absolutní cestou udělá rozbalovač na straně uživatele, není v naší moci — a je to jediné místo, kde nástroj vydává soubor, jehož jméno neurčil sám (jinde jsou jména popiskem na klientovi, viz rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md)).

**4. Text chyby z infrastruktury v odpovědi.** Každý koncový bod vrací při selhání `400` s textem výjimky a nedosažitelný katalog se hlásí záznamem, který nese `ex.Message`. Přihlašovací údaje v takové zprávě nejsou, ale název serveru nebo instance ano — tedy informace o vnitřní síti. Proti předpokladu nasazení (důvěryhodná síť) je to přijatelné; pro veřejnou instanci je to únik.

**5. Neomezený počet požadavků.** Žádné omezení frekvence neexistuje. Ve spojení s hrozbou 2 stačí k vyčerpání stroje běžný klient.

**Co naopak hrozba není.** Vstup neopouští proces a server ho neukládá; kopie rozpracovaného vstupu, kterou si od rozhodnutí [056](./decisions/056-work-in-progress-input-stays-in-the-browser.md) drží překladová obrazovka, leží v prohlížeči na stroji uživatele a nikam se neodesílá. Do generovaného artefaktu se nedostanou přihlašovací údaje ani nic jiného, co nástroj nedostal na vstupu (rozhodnutí [040](./decisions/040-boundary-of-the-handed-over-artifact.md)), a obojí hlídá test. Heslo `sa` v `docker-compose.yml` a ve workflow je vývojový údaj zahoditelné instance — S4 zakazuje údaje v generovaných artefaktech a v logu, ne v popisu prostředí.

## Co by se dalo udělat, kdyby se na oblast sáhlo

Tenhle seznam **není plán**; oblast Advisoru je ze záruk vyňatá vcelku a otevírat ji dřív, než se dodělá rozdělané, by bylo proti pořadí práce. Až se na ni sáhne, začíná se tady:

- **Izolace spouštění** (první věta S4): samostatný proces s limitem CPU, paměti a času, v ideálním případě kontejner na běh. Kolektibilní `AssemblyLoadContext` tuhle roli neplní a nikdy neplnil.
- **Časový strop** na benchmark i na řešení ILP; dnes je jediným stropem `CancellationToken` od klienta.
- **Vlastní limit velikosti a počtu jednotek** na `/convert`, aby hrozba 2 měla horní mez vyslovenou námi, ne výchozím nastavením serveru. Druhá půlka téhle věty — **strop hloubky zanoření** — už na seznam nepatří: jako jediná část hrozby 2 končila pádem procesu, ne pomalou odpovědí, a proto se rozhodla a udělala zvlášť a dřív (rozhodnutí [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md)), aniž by na ni čekalo otevření celé oblasti.
- **Autentizace před Advisorem**, pokud by instance kdy stála mimo důvěryhodnou síť; překladové cestě stačí proxy.

Rozhodnutí o kterémkoli z těch bodů patří do `decisions/`, ne sem — a předpokladem je, že Advisor vůbec vystoupí z vyňaté oblasti.
