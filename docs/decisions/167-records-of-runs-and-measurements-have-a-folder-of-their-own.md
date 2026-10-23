# 167 — Záznamy běhů a měření mají vlastní složku `docs/runs/`: přehled a jeden neměnný soubor na každý popsaný běh

Datum: 2026-10-23
Stav: platí; nahrazuje místo naměřených čísel sad v rozh. 058, odstavec *Místem záznamů zůstává `ORMConvertor/README.md`* rozh. 095 a místo záznamu v odstavci *Co se měří* a v důsledku *Záznam ke každé verzi* rozh. 149; výčet obsahu přehledu ve větě *Její `README.md` je přehled …* odstavce *Rozhodnutí* nahrazuje [168](168-the-overview-of-runs-carries-the-method-of-runs.md)
Požadavky: žádné
Podklad: rozhodnutí [007](007-documentation-structure.md) (dokumentace podle žánru), [058](058-only-the-operational-half-of-the-deployment-view-moves.md) (provozní polovina nasazovacího pohledu v příručce), [095](095-a-dated-run-record-names-its-commit.md) (záznam běhu jmenuje commit), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (záznam ke každé verzi), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md) (nahrazení části); [`ORMConvertor/README.md`](../../ORMConvertor/README.md) ke dni rozhodnutí (strom `cefff1d`)

## Kontext

Záznam běhu je od rozhodnutí 095 samostatný žánr: událost s datem a commitem, která nezastarává, protože netvrdí nic o dnešku. Místo mu dalo rozhodnutí 058 — naměřená čísla „zůstávají tam, kde stojí příkaz, který je vyrobil", tedy v provozní příručce `ORMConvertor/README.md` — a 095 to potvrdilo. Tehdy šlo o dvě tabulky: velikost sady s pokrytím a výkon překladu S3.

Od té doby přibylo pět druhů záznamů, každý z vlastního rozhodnutí: verdikt soudce LDBC nad celou validační sadou (117), časy dotazů katalogu LDBC a hloubky dotazů na cesty (140, 141), šum a časy v zafixovaném prostředí (145), doba přehrání po krocích a v režimu soudce (164, 166), běh katalogu FIBEN na vyžádání s poznámkou ke každému běhu (149) a podíl F4. Ke dni rozhodnutí tvoří záznamy zhruba 4 100 z 8 200 slov příručky, tedy polovinu. Rozhodnutí 058 při 5 228 slovech měřilo právě tuhle cenu: adresát příručky chce mít instanci spuštěnou za dvě minuty a text, podle kterého nic nespustí, mu stojí v cestě, i když je správný.

Tři věci se od 058 změnily a argument „číslo stojí u příkazu" už neunesou:

- **Záznam přestal být řádkem.** Pravidlo v [`docs/README.md`](../README.md) říká, že záznam je řádek tabulky s commitem a krátkou poznámkou. Běh FIBEN má ale sedm sloupců na framework a odstavec poznámek, měření doby přehrání dvě tabulky a protokol (stav procesoru, prázdná cache plánů, vyřazené běhy), časy katalogu tabulku všech jeho dotazů. To je popis běhu, ne řádek u příkazu — a příkaz, který ho vyrobil, je stejně o několik obrazovek výš.
- **Záznam je podkladem textu práce.** Experimentální kapitola (T2, T3) se o tyhle běhy opírá, a ony stojí anglicky. Je to tatáž jazyková cena, kvůli které 058 vrátilo §6.2 do architektury.
- **Živý a neměnný text v jednom souboru.** Příručka je živá a nese razítko „naposledy ověřeno proti kódu"; záznam se podle 095 zpětně nepřepisuje. Soubor, ve kterém se jedna polovina opravuje a druhá opravovat nesmí, je přesně to míchání životních cyklů, které rozhodnutí 007 rozdělilo podle žánru.

K tomu odkazy: [`traceability.md`](../traceability.md), §9, [`subset.md`](../subset.md), [`open-items.md`](../open-items.md), kořenový README i analýza rekurze míří na kotvy anglických nadpisů příručky, které se mění s každou úpravou textu kolem nich.

## Zvažované varianty

1. **Záznamy v příručce nechat a zkrátit** na řádek s commitem; popis prostředí a toho, co se lišilo, by nesla zpráva commitu a anotace značky. Zamítáme: právě popis prostředí dělá z čísla doklad (095, varianta 1) — bez věty „turbo zapnuté" nelze časy dvou běhů srovnat, jak ukázal běh FIBEN z 2026-10-22 s mediány dvojnásobnými proti běhu téhož dne. Zprávu commitu nikdo jako dokument nečte a anotace značky vzniká jen s vydáním.
2. **Jeden živý soubor `docs/runs.md`** se všemi tabulkami, česky. Řeší jazyk a délku příručky, ale ne životní cyklus: soubor, do kterého se s každým během připisuje, je živý a záznam v něm neměnný jen slibem. Popis jednoho běhu by dál byl odrážkou pod tabulkou všech.
3. **Záznamy do [`audits/`](../audits/README.md).** Zamítáme: audit je revize stavu s nálezy po kapitolách a z nich plynoucími opravami, rozhodnutími a položkami; záznam běhu nálezy nemá, má výsledek. Smíchat je by rozmělnilo obojí — z auditů by se stal deník měření a čtenář, který hledá, co se kdy zjistilo o stavu, by se probíral tabulkami časů.
4. **Složka `docs/runs/`: přehled a jeden neměnný soubor na každý popsaný běh.**

## Rozhodnutí

**Volíme variantu 4. Záznamy běhů sad a měření mají vlastní složku `docs/runs/`. Její `README.md` je přehled: co je záznam a co musí nést, rejstřík popsaných běhů a krátké záznamy v tabulkách. Každý popsaný běh má vlastní soubor `RRRR-MM-DD-<druh>.md`, po zapsání neměnný jako audit.**

**Co je záznam.** Běh nebo měření, jehož výsledek je faktem o commitu a na který se odvolává verze, nárok nebo rozhodnutí: verdikt soudce LDBC nad celou sadou, běh FIBEN, měření časů (katalog LDBC, přehrání po krocích, výkon překladu S3), velikost sady a pokrytí, podíl F4. Kontrola — prefix sady, `ORMCONVERTOR_FIBEN_QUERIES`, běh z hosta mimo zafixované prostředí — záznamem není a nezapisuje se; pro běh FIBEN to tak říká už 149.

**Dvě velikosti záznamu.** Co se vejde do řádku s commitem a poznámkou — velikost sady, pokrytí, podíl F4, výkon překladu —, je řádkem tabulky přehledu a vlastní soubor nemá. Záznam s popisem — prostředí, protokol, tabulky po frameworcích, poznámky — dostane soubor a v přehledu řádek rejstříku s jednou větou výsledku. Rejstřík je obsah, ne opis: čísla stojí jen v souboru.

**Soubor běhu.** Jméno `RRRR-MM-DD-<druh>.md` podle dne, kdy běh — u dvou sad jeho první půlka — začal; druhý běh téhož druhu téhož dne dostane příponu `-2`. Hlavička nese strom (commit podle 095), stroj a prostředí — režim serveru (164) a stav procesoru (145) —, pak přijde co běželo, výsledky a poznámky. Obě půlky běhu, který spouští .NET a javovou sadu po sobě nad týmž stromem, jsou jeden soubor. Omyl zjištěný později se v souboru neopravuje; přehled ho uvede u řádku, jak `audits/README.md` vede opravy zmražených souborů.

**Jazykem je čeština**, jako všude v `docs/`. Převzaté záznamy se převádějí beze změny obsahu; čísla dostanou jen desetinnou čárku.

**Příručka si nechá, co je návod**: příkazy, proměnné, nastavení soudce a běhu FIBEN a větu o tom, kam záznam patří a co má nést. Věty o ověření nasazovacích cest (*Verified by running it*) v ní zůstávají. Nejsou to běhy sad, ale tvrzení příručky o sobě samé, které rozhodnutí 057 svázalo s razítkem data; v tom 058 platí dál.

**Co z dřívějších rozhodnutí platí.** Z 095 všechno kromě místa: záznam jmenuje commit a tvrzení o stavu číslo neopisuje, ale odkazuje — dnes do přehledu. Odstavec 095 o místě záznamů nahrazujeme celý, včetně jednoho záznamu ponechaného v `architecture.md` §6.2: ten už v §6.2 nestojí a záznamem nároku je dnes záznam v `runs/` jako každý jiný. Z 058 platí dělicí čára žánru; mění se jen to, kam padají naměřená čísla sad. Ze 149 platí záznam ke každé verzi; značka verze odkazuje na soubor jejího běhu. Rozhodnutí 117 a 141 jmenují příručku jako místo záznamu jen odvozeně od 095, takže jejich stav se nemění a vede je tabulka níž.

## Důsledky

- **Vzniká `docs/runs/`** se záznamy převzatými z příručky. Kde zmražené dokumenty — rozhodnutí a audity — jmenují oddíl příručky, platí tahle tabulka:

  | Oddíl `ORMConvertor/README.md` do dne rozhodnutí | Kde je teď |
  |---|---|
  | *The F4 share*, tabulka | přehled, *Podíl F4* |
  | *Whole-set runs* | `2026-10-12-ldbc-judge.md` |
  | *Time by step*, měřicí režim | `2026-10-18-ldbc-judge-time-by-step.md` |
  | *Time by step*, odstavec *The judge's mode* | `2026-10-22-ldbc-judge-mode.md` |
  | *Catalog query times*, okno a hloubky dotazů na cesty | `2026-10-15-ldbc-catalog-times.md` |
  | *Catalog query times*, *Noise* | `2026-10-17-ldbc-catalog-noise.md` |
  | *Catalog query times*, celý katalog v zafixovaném prostředí | `2026-10-17-ldbc-catalog-times.md` |
  | *The FIBEN run*, *Runs* | `2026-10-18-fiben.md`, `2026-10-19-fiben.md`, `2026-10-22-fiben.md`, `2026-10-22-fiben-2.md` |
  | *How large the suite is, and what it covers* | přehled, *Velikost sady a pokrytí* |
  | *Translation performance (S3)* | přehled, *Výkon překladu (S3)* |

- **Odkazy živých dokumentů** — `architecture.md` §6.2 a §9, `traceability.md`, `subset.md`, `open-items.md`, kořenový README, [`docs/README.md`](../README.md), analýza rekurze a stránka FIBEN frontendu — míří do přehledu nebo na soubor běhu. Zmražené dokumenty své odkazy nechávají; vede je tabulka nahoře.
- **`docs/README.md`** dostává řádek pro `runs/` a pravidlo o záznamu běhu se mění: záznam je soubor nebo řádek přehledu v `runs/`, ne řádek příručky.
- **Hlavička `database/fiben/judge.tsv`** jmenuje oddíl příručky dál. Je součástí soudce (149, 163) a změna souboru by změnila jeho kontrolní součet, takže by se soudce v databázi při příštím startu načetl znovu.
- **Cena je druhé místo zápisu:** běh se píše do souboru a do řádku rejstříku. Obojí se zapíše jednou a dál nemění, takže se nemohou rozejít tak, jako se rozcházela čísla opisovaná do živých dokumentů (095).
