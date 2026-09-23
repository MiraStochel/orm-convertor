# 100 — Interaktivní srovnání je maketa nad zmrazeným během; pátý dokument frontendu

Datum: 2026-09-23
Stav: platí
Požadavky: F11, F14, S7
Podklad: rozhodnutí [099](099-examples-are-content-not-a-choice.md), které tímto nahrazujeme, a přes ně [033](033-shape-of-the-static-frontend-screens.md); dále [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [032](032-frontend-as-static-pages-without-a-build.md), [066](066-records-attributed-to-the-input-unit.md) a [095](095-a-dated-run-record-names-its-commit.md); položka „Směr překladu jako jedna věc a vstup vedle výstupu" v [`open-items.md`](../open-items.md); [`architecture.md`](../architecture.md), §6.3 a §9

## Kontext

Otevřená položka **„Směr překladu jako jedna věc a vstup vedle výstupu"** se ptá, jestli má na překladové obrazovce stát vstup vedle výstupu, a sama pojmenovává, co je na tom těžké: není to rozvržení, je to poctivost. Server neříká, který artefakt vznikl ze které jednotky ([`architecture.md`](../architecture.md), §9), takže dva sloupce vedle sebe buď musí párovat toutéž zobrazovací heuristikou, jakou se artefakty pojmenovávají, a jako heuristiku se i označit, nebo nesmí tvrdit párování vůbec. Půlka předpokladů skutečného párování stojí od rozhodnutí [066](066-records-attributed-to-the-input-unit.md) — jednotka nese jméno a záznam na ni ukazuje —, druhá chybí.

**Ta položka je o tvaru obrazovky, a tvar je přesně to, co se v próze rozhodnout nedá.** Navíc chybějící půlka není drobnost: znamenala by, že každý builder připisuje, co vydal. A pokud má být k něčemu, musí být jemnější než „tenhle artefakt vznikl z téhle jednotky" — otázka, kterou si čtenář převodu doopravdy klade, zní **které slovo se stalo kterým slovem**. Rozhodovat o takovém zásahu do pipeline, aniž by kdokoli viděl obrazovku, které by sloužil, je sázka.

**Co nástroj ukazuje dnes.** Výkladová stránka (rozhodnutí [099](099-examples-are-content-not-a-choice.md)) staví vstup a výstup vedle sebe jako dva seznamy souborů a pás záznamů říká, co se nepodařilo přenést, co doplnila konvence a co dodal katalog. Dohromady to říká hodně o tom, co **ne**doputovalo, a nic o tom, co doputovalo: **převod, který se povedl, nezaznamenává nic.** Zbytek je na oku čtenáře, který dvě okna porovnává sám. Požadavek F14 žádá zobrazení vstupu, mezireprezentace, výstupu a diagnostiky; tohle je pátá věc, kterou F14 nejmenuje a na kterou se čtenář ptá první.

A je to i otázka textu práce. Věta „nástroj přeloží entitu mezi šesti frameworky" se dokazuje špatně; obrázek, na kterém `[MaxLength(200)]` svítí zároveň s `length="200"` a `unique="true"` svítí samo, protože ho přinesl katalog, se nedokazuje vůbec — ten se ukáže.

## Zvažované varianty

**Čím se otevřená položka posune.** Tři cesty.

1. **Počkat, až mezireprezentace a buildery ponesou původ, a teprve pak obrazovku postavit.** Zamítáme. Není to špatné pořadí samo o sobě, ale je to pořadí, ve kterém se drahá část dělá naslepo: připisování původu se dotkne každého builderu a jeho tvar (artefakt? entita? token?) se řídí tím, co má obrazovka ukázat. Rozhodnout to bez obrazovky znamená rozhodnout dvakrát.
2. **Živá stránka, která spoje odvodí v prohlížeči** ze shody jmen. Zamítáme ze dvou důvodů, a druhý je horší než první. Za prvé by stránka **tvrdila párování, které nástroj nedělá**, právě na místě, kde je poctivost celým předmětem sporu — a otevřená položka na tuhle past výslovně ukazuje. Za druhé by odvozování bylo **druhou překladovou sémantikou, tentokrát v JavaScriptu**, bez testů, vedle té otestované na serveru; jakmile by se builder změnil, stránka by tiše ukazovala nesmysl a nic by to nezachytilo, protože frontend testy nemá (rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md), důsledky).
3. **Samostatný dokument, který se prohlásí za maketu**, nad zmrazeným během sedmi příkladů a s ručně psanými spoji. Volíme. Maketa nic netvrdí o nástroji, protože o sobě říká, čím je; a je to jediná varianta, ve které je **co si osahat** dřív, než se sáhne na pipeline.

**Kde maketa leží.** Dvě cesty. Buď **mimo repozitář** — obrázek v textu práce, podklad `*.local.md` —, nebo **jako stránka v repozitáři**. Volíme druhou: maketou tady není obrázek, ale **interakce**, a ta se obrázkem předvést nedá — celý návrh je o tom, co se stane, když čtenář ukáže myší na slovo. Stránka je zároveň jediná forma, ve které si to může vyzkoušet vedoucí práce i čtenář, který nástroj nikdy nespustí.

## Rozhodnutí

> **Frontend dostává pátý dokument, `comparison.html` — *Interactive comparison* —, a je to maketa. Nevolá žádný koncový bod, nic nepřevádí a nic neodvozuje: artefakty všech sedmi příkladů výkladové stránky jsou v něm zmrazené tak, jak je vydal jeden zaznamenaný běh, a spoje mezi slovy vstupu a výstupu jsou ručně psaná data. Obojí leží v oddělených souborech, protože má dvojí původ, a stránka to o sobě říká na prvním obrazovce. Maketa nezakládá nárok na žádný požadavek a nemění větu §9 o tom, že výstupní artefakty svůj původ nenesou.**

**Zmrazená půlka a ručně psaná půlka mají každá svůj soubor.** `js/comparison-run.js` je záznam: odpovědi `GET /examples` poslané na `POST /convert`, jeden běh na příklad, s identifikátorem běhu, verzemi obou frameworků, stavem katalogu i záznamy převodu, a v hlavičce datum, commit a prostředí, ve kterém vznikl (rozhodnutí [095](095-a-dated-run-record-names-its-commit.md)). `js/comparison-links.js` jsou spoje. **To rozdělení je věcné, ne úklidové:** čtenář, který chce vědět, co pochází z nástroje a co z ruky, se nemusí ptát — jsou to dva soubory a každý to o sobě říká v hlavičce. Jména souborů v oknech jsou táž zobrazovací heuristika, jakou artefakty pojmenovává výkladová stránka, takže se soubor jmenuje na obou stránkách stejně.

**Slovník makety má tři hodnoty, ne šest.** *Carried* — fakt stojí na obou stranách; *added* — stojí jen ve výstupu; *dropped* — stojí jen ve vstupu. Je to popis dvou textů na obrazovce, tedy přesně to, co čtenář sám vidí a může si ověřit. **Šest druhů záznamů nástroje jsou tvrzení jiného řádu** a maketa je nepřekládá do svého slovníku: kde záznam existuje, cituje se **doslova a s vlastním odznakem** vedle poznámky. Čtvrtý případ, *carried*, nástroj nezaznamenává a zaznamenávat nemá — povedený převod záznam nevydává —, a právě proto ho maketa mít musí: srovnání bez slova pro fakt, který doputoval, je jen seznam chyb.

**Maketa se prohlašuje za maketu, a to na čtyřech místech:** rámeček v prvním obrazovce stránky, značka *mockup* u položky v navigaci, karta na rozcestníku a patička. Rámeček říká tři věci — že artefakty jsou skutečný zaznamenaný běh a odkud; že nástroj **nehlásí, které slovo výstupu vzniklo ze kterého slova vstupu**, a spoje jsou proto psané rukou; a že spojeno není všechno, jen to, na co stojí za to ukázat.

**Co maketa nedělá.** Nevolá `/convert` ani `/examples`, takže na běžící instanci nezávisí a REST kontrakt se nemění. Nezobrazuje mezireprezentaci — to zůstává druhou otevřenou položkou rozhraní a maketa ji nepředjímá. Netvrdí úplnost: nespojené slovo neznamená, že nikam nedoputovalo, jen že spoj nikdo nenapsal. A **nemění nic na nástrojových obrazovkách**: překladová obrazovka i výkladová stránka zůstávají, jak jsou.

**Pátý dokument.** První volba rozhodnutí 033, kterou 099 přebralo beze změny, mluví o **čtyřech dokumentech**; nově je jich pět a pátý je maketa. Navigace ji nese se značkou, která to říká dřív, než na ni čtenář klikne. Ostatní volby 099 **platí beze změny**: spodní hranice sedmi živých příkladů a jejich rozsah, `GET /examples` jako zdroj jejich vstupů, a osm voleb převzatých z 033 (role dokumentů, pětikrokový tvar překladové obrazovky, dávkový vstup podle F14, validace před odesláním, výsledek po souborech a záznamy po entitách, hlavička výsledku se stavem katalogu, `POST /archive` a Advisor v dosavadním rozsahu) — s jedinou změnou, a tou je počet dokumentů v první z nich.

## Důsledky

**Rozhodnutí 099 dostává stav `nahrazeno 100` celé**, ačkoli se z něj mění jediné číslo. Je to tentýž krok, jaký 099 udělalo s 033 a zdůvodnilo tam: nahrazené rozhodnutí se nepřepisuje a čtenář, který v něm najde „čtyři dokumenty", má z hlavičky poznat, že to neplatí. Vše ostatní z 099 je výčtem výš převzaté a nadále v platnosti.

**Nárok se nemění a traceability také ne.** Maketa nezakládá nárok na žádný požadavek — stejně jako výkladová stránka (rozhodnutí 032, důsledky) — a nevyvrací ani větu §9, že výstupní artefakty jméno nenesou a jejich párování se vstupem je otevřená položka: párování na téhle stránce je **ručně psaný údaj makety**, ne něco, co nástroj spočítal. Požadavky v hlavičce jsou ty, jejichž předmět stránka ukazuje, ne ty, které by dokládala.

**Frontend ztěžkne, a platí to jen tahle stránka.** Zmrazený běh a spoje jsou největší soubory, které kdy ve `wwwroot` byly; naměřené číslo nese [`architecture.md`](../architecture.md), §6.3. Ostatní čtyři dokumenty nestáhnou z makety ani bajt, protože je to samostatný dokument s vlastním modulem — což je mimochodem druhý důvod, proč nevznikla jako oddíl výkladové stránky.

**Maketa stárne konstrukcí a počítá se s tím.** Je to fotografie jednoho běhu; jakmile se výstup nástroje změní, ukazuje starý. Proto v hlavičce stojí commit, ze kterého vznikla: kdo chce vědět, jestli ještě platí, má z čeho běh zopakovat — jsou to dvě volání. **Až otevřená položka dospěje a pipeline původ ponese, maketa se smaže, ne opraví:** její úkol končí tím, že existuje skutečná obrazovka. Hlídá to jediné: že hlavička nese commit. Frontend testy nemá (rozhodnutí 032) a maketa žádné nezakládá — u stránky, která nic netvrdí, by test hlídal jen sám sebe.

**Jedna věc se přesto hlídá strojově, a hlídá se v samotné stránce.** Spoj ukazuje na doslovný text ve zmrazeném souboru; překlep v něm by nevysvítil nic a vypadal by úplně stejně jako slovo, které nikdo nespojil. Stránka proto při načtení rozřeší **všechny** spoje, ne jen ty právě zobrazeného příkladu, a do konzole napíše, kolik jich je a kolik z nich nenašlo nic; nerozřešený spoj přeskočí a jmenuje. Hledání spojů kvůli tomu stojí v samostatném modulu bez závislosti na DOM (`js/comparison-marking.js`), takže se dá spustit i nad daty mimo prohlížeč — což je způsob, jakým maketa vznikla. Test v repozitáři to není a nemá být: frontend testy nemá (rozhodnutí 032) a stránka, která nic netvrdí, si první z nich nezaslouží.

**Model hrozeb se nemění.** Statické soubory pod `/orm` má [`threat-model.md`](../threat-model.md) v jednom řádku a maketa je tři další; nový koncový bod nevzniká a zmrazená data neopouštějí sestavení.

**Co tohle rozhodnutí nerozhoduje:** jestli vstup nakonec bude stát vedle výstupu na **překladové** obrazovce, jestli mezireprezentace a buildery budou nést původ a v jaké jemnosti. Obojí zůstává otevřenou položkou; maketa je k ní **podklad**, ne odpověď.
