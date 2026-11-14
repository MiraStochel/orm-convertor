# Revize celého repozitáře před vydáním 2.4.1, 2026-11-13

Revize jedné otázky: **je strom, který má nést značku `2.4.1` a odevzdává se, soudržný — odkazy, dokumentace proti kódu, nárok na třech místech, stránky, rejstříky a záznamy?** Postup je týž jako u revizí [2026-10-18](2026-10-18-whole-repository-audit.md) a [2026-11-01](2026-11-01-pre-submission-audit.md): mechanické kontroly skriptem nad celým repozitářem, živé dokumenty čtené proti kódu po oddílech, ověření spuštěním. Na rozdíl od nich revize opravila jen prosté věcné omyly živých dokumentů (kapitola 8); zmražené soubory, text nároku, stránky a kód nechala být a jejich opravy vypisuje jako čekající.

Nálezy jsou číslované po kapitolách a označené závažností — **kritické**, **závažné**, **drobné**.

**Stav k 2026-11-14.** Revize se prošla znovu proti pracovnímu stromu téhož commitu s opravami provedenými po ní. Kde se stav nálezu změnil nebo byl znovu ověřen, stojí pod ním řádek *Stav 2026-11-14*; nález bez něj znovu ověřován nebyl a platí, jak byl napsán. Kapitoly 7 a 8 jsou podle toho doplněné.

## Zafixované verze a stav, ke kterému revize platí

Pracovní kopie na MIS3, commit **`bbc20ef` „Frontend scrolling to linked artefacts"** s nezapsanou přípravou vydání: `<Version>` v `Directory.Build.props` a `version` v `CITATION.cff` nesou `2.4.1`, zámky balíčků rozsahy `2.4.1`, `open-items.md` a §9 větu o vydání. Poslední značka je `2.4.0` na `a054cd5`. .NET SDK 10.0.403 na hostiteli (`global.json` pin `10.0.100` s `latestFeature`), SQL Server LocalDB s `ORMConvertorTests` a `ORMConvertorCaseStudies`; Docker neběžel.

Tabulka „Zafixované verze" v [`architecture.md`](../architecture.md) §1 souhlasí s `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `JavaTests/pom.xml`, základními obrazy obou Dockerfilů, obrazem SQL Serveru v compose i v CI a s commity akcí, řádek po řádku.

---

## 1. Nárok na třech místech

Stav každého požadavku F1–F15, S1–S7, T1–T7 souhlasí v sekci *Guarantees* kořenového README, v §9 i v [`traceability.md`](../traceability.md) a každé zúžení má protějšek na všech třech místech. Nálezy jsou kolem tohoto jádra.

### 1.1 Výčet toho, co sady přeskočí, je v nároku užší než skutečnost — závažné

README (*Guarantees*, oblast 6) a §9 říkají, že zelený běh obou sad přeskočí **jen** tvar LINQ, který provider NHibernatu nevysloví, hloubky dotazů LDBC na cesty a běhy FIBEN a SEDE; `ORMConvertor/README.md` (*The Java test suite*, „No skip") a §6.2 („Bez přeskočení") jmenují ještě méně. Přeskakují i běhy na vyžádání bez svého nastavení: experimentální běh a jeho souhrn, běh mezí, běh celého vstupu, běh cizích artefaktů a tři kanály srovnání s jazykovým modelem — v .NET sadě třináct případů (kapitola 7), v javové experimentální běh a běh cizích artefaktů (`Assumptions.abort`). Je to týž druh nálezu jako 1.1 revize 2026-10-18.

*Opraveno* v přehledu běhů ([`runs/README.md`](../runs/README.md#velikost-sady-a-pokrytí)), který skupiny vyjmenovává. *Čeká:* věta nároku v README a v §9 a obě věty „bez přeskočení" — doplnit „a běhy na vyžádání, které bez svého nastavení přeskočí s důvodem".

*Stav 2026-11-14:* opraveno na všech čtyřech místech — README (*Guarantees*, oblast 6), §9, `ORMConvertor/README.md` („No skip") i §6.2 běhy na vyžádání jmenují.

### 1.2 SEDE vstoupila do nároku napůl — závažné

Čtvrtou doménu jmenuje výčet přeskočeného v README a v §9, [`traceability.md`](../traceability.md) a [`use-cases.md`](../use-cases.md) o ní nemluví vůbec a §9 nemá k odstavci *Katalog FIBEN není nárok, je míra* protějšek, takže nárok neříká, zda je SEDE nárokem, nebo mírou. Řádek T1 v traceability a §9 citují pro referenční databáze rozh. 174, jehož tabulku nahradilo rozh. 208. Stránka `sede.html` říká „The catalog has not been run in the fixed environment of the measurements yet", ačkoli [přehled běhů](../runs/README.md#popsané-běhy) vede dva záznamy z 2026-11-08; `ORMConvertor/README.md` (*The SEDE run*), §6.2, §6.3 a komentář v `database.Dockerfile` říkají, že soudce vznikne „z prvního doloženého běhu", kdežto rozh. 209 ho váže na běh podle sebe. Doplnění dokumentů vede otevřená položka *SEDE jako čtvrtá doména*; věta stránky a čtyři věty o soudci v ní nejsou.

*Stav 2026-11-14:* věta stránky `sede.html` a věty o soudci (`ORMConvertor/README.md`, §6.2, §6.3, komentář v `database.Dockerfile`) jsou opravené podle rozh. 209. Zbytek trvá a vede ho otevřená položka: `traceability.md` a `use-cases.md` o SEDE nemluví, §9 nemá protějšek k odstavci o FIBEN a řádek T1 cituje rozh. 174.

### 1.3 Traceability řadila T1 mezi vyňaté — drobné

Úvod oddílu T a věta o úkolu 5 záměru jmenovaly v užším rozsahu jen T2 a T3; řádek T1 téhož dokumentu, §9 i README vedou T1 v užším rozsahu (rozh. 184). *Opraveno.*

### 1.4 Drobnosti — drobné

- Traceability odkazuje pro čísla případové studie jen na záznam 2026-10-24; Cofoundry, druhá aplikace T1, je poprvé v záznamu 2026-11-05.
- Věta S7 zadání „stažení kompletního výstupního projektu" není v README ani v §9 nárokovaná ani zúžená; čtení „archiv artefaktů, ne projekt" stojí jen v `use-cases.md` jako nečinnost.
- README u T2 říká „at all four levels"; traceability vede třetí stupeň jen nad javovými cíli.
- §9 říká „Poslední vydání je `2.4.1`" dřív, než značka existuje; `open-items.md` to vede správně jako zbývající krok. Vyřeší se značkou.

*Stav 2026-11-14:* beze změny ve všech čtyřech bodech; značka `2.4.1` neexistuje.

## 2. Stránky, sestavení a kód

### 2.1 Výuková stránka selže při prvním převodu, jakmile odpověď nese fakta — kritické

`js/facts.js` na konci `renderFacts` volá `wireMarks()`, které bere `document.getElementById("result")` a věší na něj posluchače. Prvek `#result` mají `translation.html` a `advisor.html`; `tutorial.html` má `#tutorial-result`. S neprázdným polem `facts` tak první převod na výukové stránce skončí výjimkou, kterou `tutorial.js` vypíše jako „The conversion failed: …", a pás záznamů kroku 5 se nevykreslí; druhý převod neselže, ale svícení řádku a podtržení, které stránka slibuje, nefunguje nikdy. Bez katalogu jsou fakta příkladu `dapper-to-efcore` prázdná (ověřeno voláním `/convert` na hostiteli, kapitola 7) a vada se neprojeví; s katalogem — nasazení z compose, tedy ukázka u obhajoby — je podle textu stránky samé doplňuje katalog. V prohlížeči nad instancí s katalogem revize vadu nespustila. Vada je ve stromu od `d05d76d`, tedy i ve vydání `2.4.0`.

*Stav 2026-11-14:* opraveno — `js/facts.js` věší posluchače na dokument a žádný prvek stránky nejmenuje, takže výuková stránka i překladová obrazovka dostávají totéž chování. Ověřeno čtením změny, ne v prohlížeči; test výukové stránky nad odpovědí s fakty není a nevznikne — automatické testy frontendu jsou mimo rozsah (rozh. 032).

### 2.2 Výkladová stránka nemá příklad `cofoundry-pages` — závažné

`/examples` vydává 22 příkladů (`ORMConvertorAPI/Data/Examples.cs`), `examples.html` jich popisuje 21 a slovo Cofoundry na žádné stránce není; text stránky říká „Twenty-one examples" a „The last five are … excerpts of real open-source projects", komentář v `Examples.cs` totéž. Překladová obrazovka přitom všechny řádky `/examples` nabízí ve skupině „Described on the Examples page" a §6.3 za výčet stránky označuje `Examples.cs`. Cofoundry je druhá aplikace případové studie T1 a otevřená položka ukázky ji jmenuje mezi příklady výkladové stránky. Stránku ke katalogu žádný test neváže.

*Stav 2026-11-14:* opraveno — `examples.html` má oddíl `cofoundry-pages` a počet příkladů už neuvádí. Test, který by stránku vázal na katalog, není; vede ho otevřená položka *Výkladová stránka není vázaná na katalog příkladů*.

### 2.3 Snímek `openapi.json` je o tři vydání pozadu — závažné

`ORMConvertorAPI/openapi.json` nese `info.version` `2.2.0` a nemá cestu `/sede`; `ORMConvertor/README.md` o něm říká, že se z běžící instance obnovuje. Běžící dokument stromu nese `2.4.1` a dvanáct cest (kapitola 7). Snímek záměrně nehlídá žádný test (§6.5), o to spíš patří jeho obnovení ke krokům vydání.

*Stav 2026-11-14:* opraveno — snímek nese `2.4.1` a cestu `/sede`.

### 2.4 Odvozenina z FIBEN nemá u sebe licenci — závažné

`SampleData/Fiben/queries.json` a `database/fiben/schema.sql` jsou podle svých hlaviček vygenerované z `IBM/fiben-benchmark` (Apache-2.0); licenční soubor u nich není a odstavec *License* kořenového README jmenuje jako výjimky z MIT jen `wwwroot/vendor/`, `SampleData/ThirdParty/` a katalog SEDE. Text licence FIBEN je jen přílohou vydání `data_sets`. U SEDE a u výňatků třetích stran licence vedle souborů leží.

*Stav 2026-11-14:* opraveno — `SampleData/Fiben/` nese `LICENSE.txt` a `README.md` s poznámkou o změnách a odstavec *License* kořenového README jmenuje katalog FIBEN i skripty `database/fiben/`.

### 2.5 Drobnosti — drobné

- `fiben.html` odkazuje na `github.com/IBM/fiben-benchmark`, který vrací 404 (repozitář zanikl, jak říká i rozh. 215).
- Rozh. 215 říká, že pravidlo vlastní kopie platí pro každou sadu a že stahuje `fetch-dataset.sh`; `database.Dockerfile` bere výpis SEDE a zálohu WideWorldImporters přímo příkazem `curl` (zálohu bez `-f` a bez součtu), náhradní cesta validační sady LDBC vytvoří soubor jmenovaný v `datasets.sha256` a proti součtu ho nezkontroluje a neúspěch SEDE přepínač `SEDE_DATASET=none` nejmenuje. Cizí zdroje vede otevřená položka; tyhle tři odchylky od znění rozhodnutí ne.
- Sestavení má sedm varování analyzátorů v `Tests` (xUnit1051, xUnit2013, xUnit2029, CS8604).
- Komentář v `OrmConvertor/ConversionResult.cs` řadí neparsovatelný vstup mezi výjimky; od rozh. 093 je to záznam.
- Popis veřejného repozitáře na GitHubu zní „.NET ORM Convertor".
- *Neověřeno:* výjimka v úvodních krocích doplnění z katalogu (`CatalogCompletion.Complete`, `QueryDemandCompletion.Complete`) by podle čtení kódu došla ke čtyřistovce, ačkoli §6.5 a rozh. 197 slibují pro vadu nástroje záznam a odpověď 200.

*Stav 2026-11-14:* odkaz `fiben.html` je opravený (vede na vydání `data_sets`) a záloha WideWorldImporters se stahuje s `-f`; ostatní odchylky od rozh. 215 trvají. Komentář v `ConversionResult.cs` je beze změny. Varování analyzátorů, popis repozitáře na GitHubu a neověřená cesta výjimky znovu ověřované nebyly.

## 3. Rozhodnutí a jejich rejstřík

### 3.1 Číslo 216 nesla dvě různá rozhodnutí — závažné

Commit `7145f60` přidal `216-every-page-takes-the-whole-window.md` („Na celou šířku okna se roztahuje každá stránka"), commit `f534c02` soubor smazal a pod týmž číslem přidal rozhodnutí opačného obsahu, bez stavu `nahrazeno` a bez *Historie*. Rejstřík přitom říká, že čísla jsou stabilní a nepřepoužívají se; je to jediný smazaný soubor rozhodnutí v historii.

*Stav 2026-11-14:* zaznamenáno dodatkem — číslo zůstává dnešnímu rozhodnutí; smazané znění žádné vydání nenese a nic ho necituje. Záznam je v [`README.md`](README.md), *Opravy zmražených souborů*, a rejstřík rozhodnutí na něj odkazuje.

### 3.2 Rozhodnutí 211 stojí na změřeném faktu, který žádný test nedrží — závažné

Rozh. 211 a po něm [`subset.md`](../subset.md) uvádějí jako mez, že `?` uvnitř identifikátoru nebo literálu nativního dotazu EclipseLinku nápověda oddělovače neřeší („změřeno touž sondou"). `eclipselink/NativeSqlHashClaimsTest` má tři testy, všechny o `#`; měření s `?` v sadě není, takže povýšení EclipseLinku by změnu tohoto faktu neohlásilo.

*Stav 2026-11-14:* opraveno a rozšířeno. Měření fakt u identifikátoru potvrdilo, u literálu vyvrátilo (`'a?b'` je text) a ukázalo, že tutéž mez mají i Hibernate a NHibernate a že Hibernate má únik. Věta rozh. 211 je opravená na místě se záznamem v *Historie*; z měření vzešlo rozh. [217](../decisions/217-what-the-host-of-native-sql-reads-inside-a-bracketed-name-goes-out-behind-its-escape-and-refuses-the-query-where-it-has-none.md): Hibernate píše čtený znak za zpětným lomítkem, EclipseLink a NHibernate dotaz odmítají záznamem. Fakta drží `NativeSqlReadingClaimsTest` v balíčcích `hibernate` a `eclipselink` javové sady a `NHibernate/NHibernateNativeSqlReadingTest`; `?` v komentáři je změřené také (EclipseLink ho čte, zapisovač komentář nepíše).

### 3.3 Sedm odkazů ve zmražených souborech vede na neexistující cíl — drobné

Týž druh jako nález 3.1 revize 2026-11-01. *Neopraveno*, správné cíle:

| Soubor | Odkaz | Správný cíl |
|---|---|---|
| rozh. 160 | `ORMConvertor/README.md#translation-performance-s3` | `../runs/README.md#výkon-překladu-s3` |
| rozh. 204, *Podklad* | `027-verification-levels-for-the-query-branch.md` | `027-query-artifact-verification.md` |
| rozh. 204, *Podklad* | `040-consumer-project-contract.md` | `040-boundary-of-the-handed-over-artifact.md` |
| rozh. 207, *Podklad*; záznam 2026-11-06 (LLM) | `138-third-party-excerpts-are-verbatim-with-marked-changes.md` | `138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md` |
| rozh. 207, *Podklad* | `186-the-package-graph-is-locked-and-restored-in-locked-mode.md` | `186-the-build-environment-is-pinned-on-content-lock-files-image-digests-and-action-commits-moved-by-dependabot.md` |
| záznam 2026-11-06 (LLM) | `201-eclipselink-moves-the-branches-of-a-case-nested-in-an-else-…` | `201-a-case-in-the-else-of-a-case-is-written-for-eclipselink-as-one-case-with-the-inner-branches-appended-and-a-case-deeper-under-the-else-goes-out-in-native-sql.md` |

*Stav 2026-11-14:* opraveno — všech sedm cílů vede na skutečné soubory, text beze změny; záznam v [`README.md`](README.md), *Opravy zmražených souborů*. Kontrola odkazů nad celým repozitářem je čistá.

### 3.4 Stavy a rejstřík — drobné

- Rozh. 207 má v hlavičce `revidováno`, v rejstříku `platí`.
- Rozh. 161 nahrazuje pro mezivýsledek řádek rozh. 070 o celém řádku vedle sloupců; stav rozh. 070 to nenese (rozh. 150 žádá obě strany).
- Rejstřík říká, že stav přebírá z hlavičky; u třinácti rozhodnutí ho zkracuje (092, 104, 145, 174, 182, 187, 188, 190, 191, 193, 195, 208, 212) a u několika zkracuje či rozšiřuje nadpis. Řádek 199 stojí za řádkem 216.
- Soubor rozh. 101 začíná značkou BOM.

*Stav 2026-11-14:* rejstřík vede rozh. 207 jako `revidováno`, stav rozh. 070 nese i část nahrazenou rozh. 161 (v hlavičce i v rejstříku) a řádek 199 stojí na svém místě. Zkrácené stavy a nadpisy v rejstříku a BOM v rozh. 101 trvají.

### 3.5 Citace plně nahrazených rozhodnutí — drobné

Rozhodnutí se stavem `nahrazeno` (033, 055, 099, 124, 125, 127, 135, 213) se citují jako podklad dnešního chování: v komentářích kódu a frontendu na necelých dvou stech místech (nejvíc 125, 055, 099 a 124), v živých dokumentech u `architecture.md` (033 v §6.3, §6.5 a §9, 055 v §9, 099 v §6.2 a §6.3, 124 v §7, 213 v §6.3), v `subset.md` (055), `quality-model.md` a `threat-model.md` (033). Čtenář dojde k platnému znění přes řetěz stavů; pravidlo, zda se cituje původ, nebo hlava řetězu, vyslovené není. Tři citace míří přímo na nahrazenou část: §5 na rozh. 029 pro výklad S4 (nahrazuje 160), §6.2 na tabulku os rozh. 182 (190) a na tabulku databází rozh. 174 (208).

*Stav 2026-11-14:* beze změny; pravidlo i tři citace nahrazené části vede otevřená položka *Citace nahrazených rozhodnutí nemá pravidlo*.

## 4. Živé dokumenty proti kódu

### 4.1 `architecture.md`

Oddíly §1–§8 prošly proti kódu po jménech (každý identifikátor ve zpětných apostrofech) a ve vzorku chování (přes sto tvrzení §5 a §6). *Opraveno* v téže změně:

| Místo | Bylo | Je |
|---|---|---|
| §4.3, fáze | „první dvě" fáze, schéma bez vypuštění vyňaté konstrukce | tři, s krokem `DropExcludedMembers` |
| §4.4 | `Window` jen s pořadovou funkcí a povinným řazením | i agregát z `WindowAggregates`, řazení volitelné |
| §5, slovník funkcí | Jakarta Persistence bez `DateAdd` a `DateDiff` | Hibernate nese vše; bez nich jen NHibernate a EclipseLink |
| §5, `SelectMany` | join, jinak `Failure` | nad druhým kořenem bez filtru křížový join |
| §5, JPQL | čárka mezi proměnnými rozsahu `Failure` | křížový join (rozh. 196) |
| §6.2 | `FibenJudgeTable` | `JudgeTable` |
| §6.3 | `examples.html` „na celou šířku okna"; výška sloupce `80vh` | bez věty o šířce; `90vh` |
| §6.5 | „Jedenáct bodů" | bez počtu (bodů je dvanáct) |
| §7 | `XyzDescriptor : TargetFrameworkDescriptor`; `QueryName` virtuální; oba výstupy plní jen NHibernate; `IParser` bez `Limits`; `Normalize` jen `Failure` | statická třída s `Instance`; obyčejná vlastnost; i MyBatis; s `Limits`; Q8 `Incompleteness` |

*Neopraveno*, drobné: háček `ImplicitJoinKind` stojí mezi tím, co přidává wrapper, ačkoli ho žádný nepřepisuje (§5); věta o `System.Security.Cryptography.Pkcs` „vedle každé přímé reference" neplatí pro `SedeImport`; věta o digestech „ve všech souborech" neplatí pro zděděný `benchmarks/Dockerfile`; §5.1 jmenuje tři selhání orchestrace z osmi; §5.2 mluví v jedné odrážce o čtyřech i pěti dotazech a schéma doplnění jmenuje dva úvodní kroky ze tří; §6.3 cituje pro rozvržení rozh. 137 a rozh. 216 nejmenuje nikde, komentáře v `css/app.css` také ne.

*Stav 2026-11-14:* věta o `System.Security.Cryptography.Pkcs`, věta o digestech a chybějící citace rozh. 216 stojí beze změny; ostatní body znovu ověřované nebyly. Tabulka profilu a řádky únikové cesty v §5 nově nesou rozh. 217.

### 4.2 `subset.md`

Kategorie manifestu, příznaky deskriptorů všech šesti frameworků a vzorek čtyřiceti řádků záporné půlky souhlasí s kódem. *Opraveno:* věta, že IC 13 a IC 14 mají nad dnešními texty jen prefix, s odkazem na položku, která neexistuje (verdikt je záznam 2026-11-02 a texty katalogu se od něj nezměnily); kategorie `SetOperationChain` chyběla v tabulce 1.3; odkaz `[207]` neměl definici; úvod počítal dvě sporná čtení ze tří.

*Neopraveno*, drobné: tabulka 1.5 dává Dapperu u sloupce buňku, ačkoli deskriptor `ColumnName` nevyjádří (buňka popisuje čtení zdroje); řádek okenních funkcí odkazuje na oddíl 2.13, který říká „Známé není žádné"; `FOR XML` a spol. odkazují na §5, který je nejmenuje; korelovaný seznam odkazuje na §6.2 místo §5; řádky `Class_` a `Guid` v MyBatisu nesou značku čtení, ačkoli záznam píše builder.

*Stav 2026-11-14:* znovu neprocházeno; řádek o `?` v nativním SQL nahradil řádek rozh. 217 — únik u Hibernatu, odmítnutí u EclipseLinku a NHibernatu místo vyslovené meze.

### 4.3 `open-items.md` — drobné

Přehledová tabulka a položky si odpovídají jedna k jedné, značka `Na řadě` je jediná a každá citovaná rozhodnutí existují. Položka srovnání s jazykovým modelem říká „dvě varianty" a o dva odstavce níž jmenuje tři; její věta, že javová půlka rozh. 207 je ověřena jen sestavením, je nejspíš překonaná během vydání `2.4.0`. Položka cizích zdrojů říká, že vydání `data_sets` nese vše, co potřebuje profil `test`, a vzápětí, že zálohu WideWorldImporters potřebují oba profily. Šest položek druhu *rozhodnutí* to v kurzivním řádku neříká.

*Stav 2026-11-14:* obě věty položky srovnání s jazykovým modelem jsou opravené podle záznamů běhů a logů sady: varianty jsou tři a `schema` žádný běh nenesl; javová půlka změn rozh. 207 prošla v bězích vydání `2.4.0` a `2.4.1` společnou cestou (`ForeignArtifactPathTest`), schéma odvozené z klíče a vynechaný dotaz s parametrem typu entity ne, protože `ForeignArtifactRunTest` bez adresáře cizích artefaktů přeskakuje. Položku cizích zdrojů uzavřelo rozh. 218.

### 4.4 Vyprávění, počty a zdvojené fakty — drobné

Hrubé hledání frází historie je v živých dokumentech čisté; čtení našlo jednotlivá místa: T4 a F12 v traceability, hrozby 2 a 5 v `threat-model.md`, v `architecture.md` §5 („jako dosud", „Katalog LDBC tak přeložil IC 12", odstavec o pořadí joinů EclipseLinku), §6.2 (původ souboru soudce FIBEN, historie šablon promptu) a tři řádky *vystoupila* v §9. Počty, které další změna posune: slova profilu EclipseLinku a „třináct / devět" faktů v §5, hodnoty `QueryFeature` v §5.1. Čtrnáct faktů stojí v §5 dvakrát až třikrát (asociační cesta mimo `join`, pravidlo `ImplicitJoinKind`, poddotaz nad jednou skupinou a další); `architecture.md` má 369 kB.

*Stav 2026-11-14:* znovu neprocházeno; `architecture.md` má 372 kB.

### 4.5 Obě README — drobné až závažné

- `ORMConvertor/README.md` nese proti pravidlu návodu vyprávění a záznamy: první řádek „Last verified against the code: 2026-10-18", sloupec „Verified by running it" s daty ze srpna, „Verified on the same Docker Desktop version" bez verze, původ dnešního soudce FIBEN, čísla jednoho běhu pilotního modelu a popis, jak se našla vada běhu mezí. Jmenuje `MIS3`, který žádné README nezavádí.
- Kořenové README v *Beyond this version* opisuje dvě ze tří položek *Užitečné, ne nutné* a položky SEDE a cizích zdrojů nemá; říká „no released artifact besides the git tag", ačkoli sestavení bere datové sady z vydání `data_sets`.
- Věta o chybě sestavení se jménem přepínače platí jen pro LDBC a FIBEN; výčet toho, co kopíruje stupeň `java-tests`, nemá katalogy FIBEN a SEDE; tabulka konfigurace nemá proměnné experimentálního běhu, kanálů LLM a režimu databáze, které jsou popsané jen ve svých oddílech.

*Stav 2026-11-14:* zčásti opraveno. `ORMConvertor/README.md` už nejmenuje `MIS3`, větu „Verified on the same Docker Desktop version" nahradila věta bez verze, výčet stupně `java-tests` jmenuje katalogy FIBEN a SEDE a tabulka konfigurace má proměnné experimentálního běhu, kanálů LLM i režimu databáze. Kořenové README má v *Beyond this version* čtvrtou doménu i zdroje sestavení a větu „no released artifact besides the git tag" už nenese. První řádek „Last verified against the code: 2026-10-18" už `ORMConvertor/README.md` nenese; sloupec s daty ze srpna zůstává. Ostatní body znovu ověřované nebyly.

## 5. Záznamy běhů a analýzy — drobné

Každý commit, který záznamy jmenují, v historii existuje, každý záznam má řádek rejstříku a tabulky záznamů se sčítají. Nálezy:

- Oba záznamy SEDE říkají, že stav procesoru za běhu zaznamenán nebyl; mediány druhého tak stojí nad neověřeným stavem.
- Záznamy 2026-11-02 odkazují na kroky 4–6 a 7–9 úplného běhu; seznam pořadí měl tehdy šest kroků a dnes sedm. V *Nesrovnalostech* to není.
- Řádky rejstříku od 2026-10-24 nesou čísla a víc vět, proti pravidlu jedné věty bez čísel.
- *Metodika* vede soudce LDBC s turbem zapnutým, poslední verdikt běžel s vypnutým; oddíl *Před dlouhým během* nejmenuje běh mezí a běh celého vstupu, které na něj z README odkazují.
- Starší záznamy (do 2026-10-22) nemají číselné stavy procesoru, tři pozdější nejmenují režim serveru.
- Obě srovnání frameworků v `analysis/` uvádějí obraz `2022-latest` „v obou Dockerfilech"; `real-world-sources.md` vede křížový join čárkou mezi mezemi (dokument je datovaný).
- Zděděné poznámky v `notes/` mají čtyři mrtvé odkazy na dokumentaci RepoDB; jsou vedené „jak byly napsány".

*Stav 2026-11-14:* *Nesrovnalosti* přehledu běhů vedou stav procesoru obou záznamů SEDE i kroky úplného běhu v záznamech 2026-11-02 a oddíl *Před dlouhým během* jmenuje běh mezí i běh celého vstupu. Ostatní body znovu ověřované nebyly.

## 6. Co drželo — bez nálezu

- Relativní odkazy a kotvy všech 301 souborů Markdown (4553 odkazů): mimo sedm z nálezu 3.3 a dva opravené referenční odkazy vede každý na existující soubor a nadpis.
- Rejstřík rozhodnutí má řádek pro každý soubor, žádné číslo se v pracovním stromu neopakuje, názvy souborů mají nejvýše 168 znaků, data rostou s číslem a žádné není v budoucnosti; rejstříky `runs/`, `audits/` a `analysis/` jmenují každý soubor.
- Řešení, disk, seznam projektů v `ORMConvertorAPI/Dockerfile` a zámky balíčků si odpovídají; žádný `.csproj` řešení nenese číslo verze; v kódu není `TODO` ani `FIXME`.
- Žádné README neodkazuje do `open-items.md`; žádný sledovaný soubor neodkazuje na soubor, který v repozitáři není.
- Konce řádků: skripty a soudci s `eol=lf`, obraz databáze si CRLF z pracovní kopie normalizuje a seznam součtů čte přes `tr -d '\r'`.
- Deskriptory proti `subset.md`, dvanáct koncových bodů proti §6.5, README i testu dokumentu OpenAPI, model hrozeb proti kódu (stropy, redakce, Swagger jen ve vývoji), prahy ověřovacích kritérií v testech.
- Frontend: každý odkaz, zdroj a import stránek i modulů vede na existující soubor, navigace je na všech osmi stránkách stejná, žádný osiřelý soubor, texty Advisoru neslibují víc než nárok.
- Vydání `data_sets` na GitHubu nese čtyři přílohy, jejichž jména odpovídají `datasets.sha256` a `database.Dockerfile`.

*Stav 2026-11-14:* kontrola odkazů skriptem znovu, nad 302 soubory Markdown: každý relativní odkaz a každá kotva vede na existující cíl, sedm odkazů nálezu 3.3 mezi nimi. Kontrola rejstříku rozhodnutí hlásí už jen zkrácené stavy a nadpisy nálezu 3.4.

## 7. Co bylo ověřeno spuštěním

- **Sestavení:** `dotnet build ORMConvertor.sln --configuration Release` bez chyby, sedm varování v `Tests`.
- **.NET sada na hostiteli:** `dotnet test Tests/Tests.csproj --configuration Release` proti LocalDB: **26 075 testů, 0 selhaných, 12 253 prošlých, 13 822 přeskočených** za 10 min 22 s. Přeskočené po třídách: běh SEDE 11 999, běh FIBEN 1 660, soudce LDBC 80 a hloubky cest 10, tvary LINQ `Omitted` 60 a třináct případů běhů na vyžádání (nález 1.1). Běh pracovní strom nezměnil.
- **CI:** běh *ORMConvertor tests* nad `bbc20ef` z 2026-11-13 skončil `success`; přečteno z GitHub API. Javová sada ani profil `test` na stroji revize neběžely, řádek velikosti sady za strom `2.4.1` tedy nevzniká.
- **Běžící instance** z téhož sestavení na hostiteli, bez katalogu: `/orm/openapi/v1.json` nese `2.4.1` a dvanáct cest včetně `/sede`; `/examples` vydává 22 příkladů; převod příkladu `dapper-to-efcore` vrací 200, tři artefakty, tři záznamy a žádný fakt.
- **Odkazy:** skript mimo repozitář nad všemi soubory Markdown; 136 vnějších adres dokumentů a souborů sestavení dotázáno, mrtvé jsou jen ty z nálezů 2.5 a 5.
- **Nespuštěno:** nic v kontejneru, žádná stránka v prohlížeči.

*Stav 2026-11-14,* nad pracovním stromem s opravami — commit `bbc20ef` s nezapsanými změnami; řádek velikosti sady za vydání `2.4.1` je v [přehledu běhů](../runs/README.md#velikost-sady-a-pokrytí) z běhu obou sad v zafixovaném prostředí téhož dne:

- **.NET sada na hostiteli:** 26 102 testů, 0 selhaných, 12 280 prošlých, 13 822 přeskočených za 6 min; přeskočené jsou tytéž skupiny jako výše.
- **Javová sada v profilu `test`,** obrazy sestavené z téhož stromu: 16 948 testů, 0 selhaných, 13 661 přeskočených — běh FIBEN 1 660, běh SEDE 11 999, experimentální běh a běh cizích artefaktů po jednom — za 10 min 34 s.
- **Odkazy a rejstřík rozhodnutí:** skripty znovu (kapitola 6).
- **Nespuštěno** zůstává: žádná stránka v prohlížeči.

## 8. Co z revize plyne

### Opravy, provedené v téže změně

- `architecture.md`: tabulka nálezu 4.1.
- `subset.md`: verdikt IC 13 a IC 14, `SetOperationChain`, definice `[207]`, počet sporných čtení (4.2).
- `traceability.md`: T1 v užším rozsahu na dvou místech; nedefinovaný odkaz `[124]` vede na rozh. 137 (1.3).
- `threat-model.md`: `/sede` mezi vstupními body.
- `runs/README.md`: CI bez `Sede`; úplný výčet skupin přeskočených testů (1.1).

### Opravy, které čekají

Před značkou `2.4.1`:

- výuková stránka (2.1) a chybějící příklad výkladové stránky (2.2);
- obnovení `openapi.json` z běžící instance (2.3);
- licence u odvozenin z FIBEN a věta v odstavci *License* (2.4);
- věta nároku o přeskočeném v README, §9, `ORMConvertor/README.md` a §6.2 (1.1);
- věta `sede.html` o běhu a čtyři věty o původu soudce (1.2); mrtvý odkaz `fiben.html` (2.5).

*Stav 2026-11-14:* všech pět bodů je provedeno v pracovním stromu.

Bez vazby na značku: cíle sedmi odkazů a stavy rozh. 070 a 207 (3.3, 3.4) se záznamem v [`README.md`](README.md), *Opravy zmražených souborů*; zbylé drobnosti kapitol 4 a 5; dvě položky *Nesrovnalostí* přehledu běhů (kroky úplného běhu v záznamech 2026-11-02).

*Stav 2026-11-14:* cíle sedmi odkazů a stavy rozh. 070 a 207 jsou opravené se záznamem v README a obě položky *Nesrovnalostí* jsou v přehledu běhů; zbylé drobnosti kapitol 4 a 5 trvají, jak je u nich vyznačeno.

### Potřebná rozhodnutí

- Jak zaznamenat, že číslo 216 neslo dvě rozhodnutí (3.1): dodatek v rejstříku, nebo přečíslování.
- Zda se v kódu a živých dokumentech cituje původní rozhodnutí, nebo hlava řetězu nahrazení (3.5).

*Stav 2026-11-14:* první je zaznamenané dodatkem — číslo 216 zůstává dnešnímu rozhodnutí, záznam je v [`README.md`](README.md), *Opravy zmražených souborů*; druhé vede otevřená položka *Citace nahrazených rozhodnutí nemá pravidlo*. Z nálezu 3.2 navíc vzešlo rozh. 217, které revize jako potřebné rozhodnutí nevedla — volbu si vyžádalo až měření.

### Práce

- Test, který drží fakt rozh. 211 o `?` v identifikátoru a literálu (3.2).
- Doplnění SEDE do nároku je otevřená položka; přibývá k ní věta stránky a věty o soudci (1.2).
- Test, který váže `examples.html` na katalog příkladů, a test výukové stránky nad odpovědí s fakty (2.1, 2.2).

*Stav 2026-11-14:* test faktu rozh. 211 je hotový a rozšířený na všechny tři hostitele nativního SQL (3.2). Doplnění SEDE do nároku vede otevřená položka *SEDE jako čtvrtá referenční doména*, test vazby `examples.html` na katalog položka *Výkladová stránka není vázaná na katalog příkladů*; test výukové stránky by byl automatický test frontendu, který je mimo rozsah (rozh. 032).
