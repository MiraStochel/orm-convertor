# 213 — Výuková stránka je samostatný dokument `tutorial.html`, který provede jedním živým převodem v krocích překladové obrazovky a předá ho překladači odkazem, jenž příklad jen předvybere

Datum: 2026-11-10
Stav: nahrazeno 214
Požadavky: S7, F14
Podklad: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md), [033](033-shape-of-the-static-frontend-screens.md), [056](056-work-in-progress-input-stays-in-the-browser.md), [099](099-examples-are-content-not-a-choice.md), [124](124-direction-as-one-row-and-input-beside-output-paired-per-fact.md) a [137](137-text-runs-the-full-width-and-controls-keep-a-fixed-width.md); scénáře UC1 a UC6 v [`use-cases.md`](../use-cases.md); [`architecture.md`](../architecture.md) §6.3

## Kontext

Frontend má rozcestník (`index.html`), nástroj (`translation.html`), výkladovou stránku s živými příklady (`examples.html`), Advisor a tři stránky katalogů. Výkladová stránka říká, *proč* překlad dopadne, jak dopadne — co zdroj tvrdí, co cíl vyjádří, proč fakt skončí záznamem —, a nástroj předpokládá, že uživatel ví, *jak* s ním zacházet. Žádná stránka neprovede jedním překladem od výběru vstupu po stažení výstupu a neřekne, jak číst výsledek: vstup vedle výstupu s párováním, které vyslovil server (rozh. 124), pás faktů mapování s původem a osudem, pás záznamů a jejich osm druhů, stav katalogu v hlavičce běhu, stažení jako ZIP. Přitom právě výsledek je část obrazovky, kterou nový uživatel bez výkladu nepřečte — pás faktů a záznamy jsou to, čím se nástroj liší od pouhého generátoru kódu.

Ze stejného místa vychází i potřeba mimo repozitář: krátký videonávod a živá ukázka u obhajoby potřebují scénář, který se dá projít krok za krokem a který odpovídá tomu, co obrazovka skutečně dělá. S7 žádá základní scénář nejvýše v pěti krocích; obrazovka ho má (volitelný příklad, směr, vstupní soubory, převod, výsledek — rozh. 137), ale nikde není vysloven jako postup.

## Zvažované varianty

**Kde výuka stojí.**

(a) **Oddíl na rozcestníku.** Nejlevnější — žádný nový soubor, žádný odkaz navíc. Rozcestník je však stránka, která říká, *co* nástroj dělá a kam vést dál; návod o pěti krocích s ukázkou kódu by z něj udělal nejdelší stránku nabídky a orientaci, kvůli které tam čtenář přišel, by odsunul pod sebe. Kdo nástroj zná, by návod při každém návratu přeskakoval.

(b) **Nápověda přímo v překladači** — rozbalovací vysvětlivky u oddílů nebo průvodce s překryvnými bublinami, který ukazuje na skutečné prvky. Je nejblíž práci a uživatel se učí na prvcích, které pak používá. Do hlavní obrazovky ale přidává stav („kde v prohlídce jsem", „prohlídka už proběhla"), kód, který musí znát rozvržení každého oddílu, a pro překryvné bubliny i polohování nad prvky, které se se šířkou okna přeskupují (dva sloupce dotazem na kontejner, rozh. 137). Je to přesně ta mechanika, kterou rozhodnutí 033 odmítlo, když volilo jednu stránku místo průvodce, a kterou 032 volbou bez frameworku nechtělo platit. Vysvětlivky natrvalo vložené do oddílů by zase zatížily obrazovku pro každého, kdo ji zná. A ani jedna podoba neunese výklad výsledku: ten se ukáže až po převodu a bubliny nad tabulkou o desítkách řádků nemají kam ukázat.

(c) **Samostatná stránka `tutorial.html`.** Výklad *proč* (výkladová stránka) a návod *jak* (výuková stránka) jsou dva žánry; smíchané rozmažou obojí — výkladová stránka má dvacet příkladů po skupinách a návod mezi nimi by se ztratil. Samostatná stránka nemění nic na nástroji, ke kterému se uživatel vrací, a návod se dá číst před prací, vedle ní i místo ní (komise, čtenář práce). Cena je jeden soubor a jeden odkaz v navigaci každé stránky.

Volíme (c).

**Co stránka ukazuje.**

(a) **Popis bez ukázky** — text, případně snímky obrazovky. Snímky zastarávají s každou změnou obrazovky, nesedí na světlý i tmavý motiv a nedají se číst čtečkou; text bez ukázky nechá čtenáře představovat si výsledek, který je právě to, co se má naučit číst.

(b) **Schematická ukázka psaná ručně** — vymyšlený vstup a výstup jako statický kód ve stránce. Neodbíhá motivu, ale odbíhá nástroji: vstup i výstup by byly tvrzením stránky o tom, co nástroj vydá, a rozešly by se s ním při první změně builderu, bez testu, který by to chytil.

(c) **Jeden živý převod.** Stránka si vezme jeden popsaný příklad z `/examples` podle klíče, ukáže jeho skutečné jednotky a tlačítkem převodu ho pošle na `/convert`; výsledek vykreslí stejnými funkcemi jako překladač (`renderArtifacts`, `renderFacts`, `renderRecords`, `renderCatalogState`). Vstup, výstup, fakta i záznamy jsou výrokem serveru jako na výkladové stránce (rozh. 099) — nemohou od nástroje odběhnout a za příklad ručí `ExampleCatalogTest`, který tvrdí, že každý příklad serveru převod přežije. Ručně psaný zůstává jen text kroků s popisky ovládacích prvků překladače. Volíme (c).

**Který příklad.** Dapper → EF Core (`dapper-to-efcore`, *A source that states little*). Je to UC1, nejvíc namáhaný scénář nástroje, a je to směr, kde zdroj tvrdí málo — entita bez atributů, dotaz v T-SQL — a výstup se opírá o katalog nebo o konvence. Výsledek proto nese záznamy více druhů a stav katalogu v hlavičce běhu má co říct; na směru, kde zdroj tvrdí všechno, by pás záznamů byl skoro prázdný a výuka o něm by neměla na čem stát. Tři soubory se navíc vejdou na obrazovku bez rolování panelů. Stránka přitom říká obojí, co se může stát: s katalogem (kontejnerové nasazení) fakta doplní katalog a záznamy jsou *Supplied*, bez něj vyjde entita na konvencích se záznamy *Incompleteness* a *Convention*. Výklad tedy neslibuje výsledek, který instance nevydá.

**Podoba stránky.** Kroky jsou očíslované oddíly viditelné najednou, shora dolů — tentýž argument jako v rozhodnutí 033 proti průvodci platí i tady a stránka bez stavu se dá vytisknout, prolistovat i natočit. Čísla tu na rozdíl od překladače (oddíly bez čísel, rozh. 137) jsou: návod *je* posloupnost a číslo kroku je to, čím se na něj ve videu nebo v textu odkazuje. Pět kroků odpovídá oddílům překladače a jmenuje je jejich nadpisy (*Start from an example*, *Direction*, *Input files*, *Convert*, *Result*); každý říká jednou větou, co udělat, a jednou, co se objeví. Nad kroky stojí lepivý pruh s jejich čísly a názvy, který vyznačí krok, u něhož čtenář je — na stránce, jejíž pátý krok má tabulky o desítkách řádků, je to jediná orientace, kterou nese bez kódu stavu (vyznačení plyne z polohy rolování). Převod v kroku 4 spustí čtenář sám tlačítkem *Convert*, ne stránka při načtení: krok, ve kterém se má naučit stisknout tlačítko, ho nechá stisknout, a výsledek kroku 5 do té doby říká, odkud přijde. Stránka zůstává v kontejneru Pico, nepřechází na celou šířku okna jako překladač a výkladová stránka: rozhodnutí 137 roztáhlo ty dvě stránky, aby oddálení ukázalo víc kódu z příkladů o desítkách souborů; výuková stránka má tři soubory a jejím obsahem je text kroků, kterému široký řádek nepomůže.

**Předání překladači.** Návod končí tím, že čtenář totéž udělá v nástroji. (a) Odkaz na `translation.html` a pokyn „vyber příklad v seznamu" — funguje, ale nechá čtenáře hledat řádek v seznamu o dvaceti položkách. (b) Odkaz, který příklad rovnou načte — pohodlný, jenže by tichým kliknutím přepsal rozpracovaný vstup, který si překladač drží v prohlížeči (rozh. 056); překladač se dnes před přepsáním ptá, a odkaz z jiné stránky by tu otázku musel položit dřív, než čtenář vidí, k čemu. (c) Odkaz `translation.html?example=<klíč>`, který příklad v seznamu jen **předvybere** a přesune fokus na *Load example*. Načtení zůstává čtenářovým krokem 1 přesně tak, jak ho návod popisuje — a s otázkou, má-li obrazovka rozpracovaný vstup. Neznámý klíč se ignoruje a seznam zůstane na výzvě. Volíme (c): odkaz šetří hledání a nebere čtenáři nic, co by bez něj měl.

## Rozhodnutí

> **Sadu obrazovek doplňuje samostatná výuková stránka `tutorial.html` (*Tutorial*). V pěti očíslovaných krocích, které nesou nadpisy oddílů překladače, provede jedním živým převodem popsaného příkladu Dapper → EF Core: skutečné jednotky z `/examples`, převod tlačítkem čtenáře přes `/convert` a výsledek vykreslený funkcemi překladače, s výkladem hlavičky běhu, vstupu vedle výstupu, pásu faktů, pásu záznamů a stažení. Nad kroky stojí lepivý pruh s vyznačeným krokem; stránka zůstává v kontejneru Pico, bez snímků obrazovky. Odkaz na stránku nese navigace každé stránky, rozcestník a úvod překladače. Překladač přijímá parametr `?example=<klíč>`, který příklad v seznamu jen předvybere; načtení zůstává na čtenáři.**

## Důsledky

**Stránka nečte ani nepíše stav překladače.** Převod na výukové stránce nesahá do `localStorage` překladače (rozh. 056); rozpracovaný vstup v nástroji zůstane, ať čtenář na výukové stránce dělá cokoli.

**Text kroků jmenuje popisky ovládacích prvků překladače** (*Load example*, *Add files or a ZIP…*, *Convert*, *Download all as ZIP*, …). Je to jediná ručně psaná vazba mezi stránkami: přejmenování prvku v `translation.html` mění i `tutorial.html`. Test ji nehlídá — frontend testy nemá (rozh. 032) — a ověřuje se projitím scénáře S7 jako zbytek rozhraní.

**Příklad návodu je klíč, ne obsah.** Vypadne-li `dapper-to-efcore` z `Examples.cs`, stránka to řekne u kroku místo prázdných panelů, jako výkladová stránka u neznámého klíče; výměna příkladu je změna klíče a textu kroků, ne nové rozhodnutí (rozh. 099).

**Stránka je zároveň scénářem videonávodu a kostrou živé ukázky.** Kroky jsou záběry v pořadí, v jakém se natáčejí; video i scénář obhajoby vznikají mimo repozitář a stránka je nezná.

**REST kontrakt, server a S7 beze změny.** Nový je jeden statický soubor, jeden modul a čtení parametru adresy v `translation.js`; nový soubor se servíruje až po sestavení (§6.3).
