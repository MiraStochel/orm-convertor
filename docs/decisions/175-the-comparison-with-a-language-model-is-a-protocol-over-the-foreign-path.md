# 175 — Srovnání s velkým jazykovým modelem je protokol nad cestou cizího artefaktu: táž vypsaná sada vstupů, tři pokusy na buňku při teplotě 0, varianty a ablace jako hodnoty manifestu, podíly T3 po pokusech i best-of-n, a vymezení proti souběžným pracím

Datum: 2026-10-24
Stav: revidováno
Požadavky: T3, T4, T5, T6, S2
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu; uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady", T5: „varianta poskytující modelu dokumentaci cílového frameworku, informace o schématu a relevantní příklady; porovnat se stejným LLM bez RAG", T6: „samostatně vyhodnotit vliv databázových metadat, validace, opravného cyklu a RAG"); schválená [specifikace](../specifikace.tex) (T4–T6: projekt *připraví* data, rozhraní a měřicí infrastrukturu, experimenty nese navazující práce; *Vztah k dalším projektům*: diplomové práce M. Čorovčáka a I. Oboňové, nástroj jako „deterministická baseline"; *Vyhodnocení*: „stejnými vstupy zpracovanými pomocí velkých jazykových modelů"); [záměr](../zamer.tex) (*Využití pro výzkumnou činnost*); rozhodnutí [016](016-generated-artifact-verification-levels.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [099](099-examples-are-content-not-a-choice.md), [138](138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md) a [174](174-the-reference-databases-of-the-experiments-are-three-by-role-and-the-case-study-of-t1-is-the-keycloak-excerpt-judged-to-the-third-level.md); [`traceability.md`](../traceability.md), řádky T4–T6; otevřené položky *Metodika srovnání s LLM a vymezení proti souběžným pracím* a *Meze složitosti dotazu, hloubky zanoření a velikosti tříd nikdo nehledal*

## Kontext

**Infrastruktura srovnání existuje, protokol ne.** Rozhodnutí 172 dalo cestu, kterou artefakt od modelu vstupuje do stupňů ověření — adresář s manifestem, verdikt po stupních, řádek s původem, který manifest vyslovil — a výpis vstupů, aby „stejná sada vstupů" byla adresář. Rozhodnutí 173 dalo řádek nástroje v témž tvaru, úplnost mapování pro artefakt od kohokoli a souhrn, který řádky nástroje a cizí řádky skládá vedle sebe po producentovi, modelu a variantě. Co chybí, je **protokol**: co přesně se modelu dá, kolikrát, při jakém nastavení, co znamená varianta a ablace, a jak se nedeterministický výstup poměří s deterministickým.

**Specifikace říká, kde protokol končí.** Projekt experimenty s modelem *neprovádí*; připraví data, rozhraní a měřicí infrastrukturu tak, aby je navazující práce provedla nad stejnými vstupy. Zadání přitom předepisuje, co se má *uvádět* — model a verzi, prompt, teplotu, počet pokusů, tokeny, čas, náklady — a co se má *samostatně vyhodnotit* — vliv metadat, validace, opravného cyklu a RAG. To nejsou věci, které lze rozhodnout až s daty: metrika a jednotka srovnání musí být pevné dřív, než první artefakt od modelu vznikne, jinak nebudou čísla baseline (173) a čísla modelu o témže.

**Nástroj je deterministický a model není.** S2 dává nástroji bajtově shodný výstup pro týž vstup; model dává pro týž prompt různé artefakty a výsledek závisí na počtu pokusů. Protokol musí říct, co je *jedno měření* modelu a jak se z pokusů skládá podíl, aby se nesrovnával jeden běh nástroje s nejlepším z deseti běhů modelu.

**Dvě souběžné práce se dotýkají téhož pole.** Práce M. Čorovčáka navrhuje univerzální objektový model a migraci aplikačního kódu mezi relačními, grafovými a dokumentovými systémy včetně mapování ORM, OGM a ODM, s využitím LLM; práce I. Oboňové migruje dotazy mezi relačními a grafovými systémy s využitím LLM. Záměr i specifikace je jmenují jako komplementární a nástroj staví jako deterministickou baseline. Vymezení musí říct, co je společné a co ne, aby se výsledky daly číst vedle sebe.

**Je třeba rozhodnout**, jak položka ptá: rozsah (kolik z 36 směrů, které kategorie, zda katalog FIBEN, zda i entity a mapování a řady hledání mezí), počet pokusů a zacházení s nedeterminismem vůči S2, co dostane varianta RAG, co vypíná ablace, a vymezení proti oběma pracím.

## Zvažované varianty

**1 — Provést srovnání v projektu: zvolit model, napsat prompty, spustit.** Zamítáme. Specifikace to výslovně odsouvá do navazující práce a rozhodnutí 171 řadí čas projektu na experimenty T1–T3 a přípravu T4–T6; placené volání modelu je navíc náklad, který projekt nemá krytý. Co projekt může dát, je protokol, podle něhož první spuštění dá srovnatelná čísla hned.

**2 — Jen infrastruktura, protokol nechat navazující práci.** Nejméně práce teď. Zamítáme: metriky T3 a jednotka srovnání jsou rozhodnuté (016, 089, 173) a protokol je jejich poslední člen — bez něj by navazující práce buď převzala naše metriky mlčky, nebo zvolila jiné a baseline by přestala být baseline. A zadání uvádí povinná pole záznamu, která má manifest nést už dnes; navazující práce by je musela zpětně doplňovat.

**3 — Protokol rozhodnout teď, data získat v navazující práci.** Volíme, níž.

## Rozhodnutí

**Volíme variantu 3. Srovnání s velkým jazykovým modelem je protokol nad cestou cizího artefaktu: populací je výpis vstupů rozhodnutí 173 celý — matice T2 ze všech zdrojů do všech šesti cílů, scénáře a příklady včetně případové studie, katalog FIBEN s otázkou v přirozeném jazyce jako záměrem dotazu, katalog LDBC do třetího stupně —, jedno měření modelu je artefakt jednoho pokusu, pokusy jsou tři na buňku (klíč, cíl, varianta) při teplotě 0, podíly T3 se počítají po pokusech a vedle nich best-of-n, varianty a ablace jsou hodnoty pole `variant` v manifestu a sada o nich nic neví, povinná pole metadat jsou ta, která zadání předepisuje uvádět, a nástroj je v tabulce jeden řádek na buňku, protože je deterministický (S2). Model, prompty a rozpočet volí navazující práce.**

### Populace a rozsah

Vstupem modelu je **přesně to, co dostává nástroj**: složka výpisu vstupů (`input.json`, jednotky zdroje s příponou podle typu, u FIBEN otázka a parafráze), nic míň a nic víc; co model dostane navíc, říká varianta. Rozsah baseline je celý výpis, protože každý jeho druh měří jinou věc:

| Druh | Co měří u modelu | Čtvrtý stupeň |
|---|---|---|
| `matrix` — kategorie T2 z každého zdroje, který je vysloví, do šesti cílů; vlastní dotazy matice | tabulku T2 × T3 nad týmiž buňkami jako nástroj, kategorii po kategorii | kanonický výsledek (rozh. 089) |
| `cross`, `example`, `case study` | entity a mapování: úplnost přenesených mapovacích vlastností (rozh. 173), projekt jako celek | není (rozh. 172, 174) |
| `fiben` | šířku a zanoření nad 237 dotazy, s otázkou v přirozeném jazyce jako záměrem — jediný vstup, u kterého model smí dostat *co* dotaz dělá, ne jen *jak* | soudce FIBEN (rozh. 149) |
| `ldbc` | překlad dotazů benchmarku, které jsme nepsali | třetí stupeň; soudce je přehrání (rozh. 172) |

Z 36 směrů se měří všech 36, protože model na rozdíl od nástroje nemá deskriptor, který by směr předem odmítl, a zajímavé je právě to, kde model vydá artefakt tam, kde nástroj odmítá — a zda ten artefakt projde. Řady hledání mezí (položka *Meze složitosti*) do baseline nepatří: nejdřív musí existovat, a pak jsou další populací téže cesty. Entity a mapování se měří, protože poslední metrika T3 je o nich a cesta ji měří pro artefakt od kohokoli.

### Pokusy, teplota a nedeterminismus

**Jedno měření modelu je jeden artefakt jednoho pokusu**, zapsaný do vlastní složky s `attempt` v metadatech; sada ho soudí jako každý jiný a nic o pokusech neví (rozh. 172). **Pokusy jsou tři na buňku** (klíč, cíl, varianta): dva by neřekly, zda se liší, deset by stálo třikrát víc a u 36 směrů nad stovkami klíčů by rozpočet navazující práce šel do pokusů místo do variant. **Teplota je 0** jako hlavní nastavení — nejblíž deterministickému protějšku, takže rozptyl mezi pokusy měří nedeterminismus modelu samého, ne vzorkování —; výchozí teplota modelu je druhé nastavení, bere-li ho navazující práce, a stojí v metadatech.

**Podíly se počítají dvakrát a obě čísla se uvádějí.** *Po pokusech*: každý pokus je řádek a podíl skupiny je podíl řádků — to je číslo srovnatelné s nástrojem, který má na buňku řádek jeden (S2: další pokus by vydal tytéž bajty). *Best-of-n*: buňka prošla, prošel-li kterýkoli z jejích pokusů — to je číslo, které říká, co by dal opravný cyklus s dokonalým soudcem, a je horní mezí varianty s opravou. Rozdíl mezi nimi je mírou nedeterminismu a uvádí se vedle. Souhrn rozhodnutí 173 dává první číslo z řádků přímo; druhé se čte ze řádků po klíči a cíli.

### Varianty a ablace

Varianta je **hodnota pole `variant` v manifestu** a nic jiného: sada neinterpretuje, souhrn seskupuje. Protokol pojmenovává tyto, aby T4–T6 měly po jedné každou:

| `variant` | Co model dostane navíc proti vstupu | Požadavek |
|---|---|---|
| `zero-shot` | zadání úlohy, cílový framework a jeho připnutou verzi (ta z `Directory.Packages.props` a `pom.xml`), tvar odpovědi (soubory jednotek cíle) | T4 |
| `few-shot` | k tomu tři příklady překladu v témž směru z příkladů výkladové stránky (rozh. 099) nebo z matice, nikdy klíč, který se měří | T4 |
| `rag` | k tomu vyhledané úryvky dokumentace cílového frameworku v připnuté verzi, schéma databáze měřeného klíče jako DDL (`Tests/Database/TestSchema.sql` a `QueryShapes/FixtureData.sql` pro matici, `database/fiben/schema.sql` pro FIBEN, `database/ldbc/schema.sql` pro LDBC) a vyhledané příklady | T5 |
| `repair` | k variantě `rag` opravný cyklus: po každém pokusu dostane model řádek verdiktu cesty — stupeň, výsledek, diagnostiku — a vydá další pokus; nejvýš tři kola, každé kolo je artefakt s `round` v metadatech | T6 |
| `rag-no-schema`, `rag-no-docs`, `rag-no-examples`, `repair-no-verdict` | ablace T6: z nejplnější varianty se vyjme jedna složka — metadata databáze, dokumentace, příklady, nebo verdikt v opravném cyklu (model dostane jen „zkus znovu") | T6 |
| `agent` | tytéž složky jako `repair` — korpus dokumentace připnuté verze, schéma jako DDL, příklady a verdikt cesty —, ale ne vložené do promptu a podávané obsluhou: model je dostává jako **nástroje protokolu**, které sám volá — hledání v korpusu, čtení schématu, výběr příkladu, soud artefaktu cestou rozh. 172 jako operace —, a sám rozhoduje, co si přečte a kdy vydá další pokus; strop volání nástrojů a kol je parametr běhu v metadatech (`toolCalls`, `rounds`), celá trajektorie je jeden pokus a měří se její poslední artefakt. Nástroje protokolu jsou jediné, které model má: web, repozitář a soubory stroje zůstávají zakázané (rozh. 189) | T5 |
| `agent-no-schema`, `agent-no-docs`, `agent-no-examples`, `agent-no-verdict` | ablace T6 nad `agent`: týž nástroj se odebere, model o něm neví | T6 |

Co je „úryvek dokumentace" a jak se hledá, je metodika navazující práce; protokol určuje jen, že korpus je dokumentace **připnuté verze** cíle, protože tvrzení nástroje platí proti ní (rozh. 013, 034) a model nad jinou verzí by se měřil proti jinému frameworku. Opravný cyklus dostává verdikt cesty **jako operaci** — proces na artefakt podle rozh. 172 — a ne jako test; to je důvod, proč cesta vznikla souborem a ne harnessem v sadě. Rozdíl mezi `repair` a `agent` je právě v tom, kdo řídí: v `rag` a `repair` protokol — obsluha nebo ovladač — model krmí a jeho odpověď soudí, v `agent` si model obsah i soud vyžádá sám; zadání obě jmenuje vedle sebe („RAG / agentní varianta", T5) a specifikace je staví jako dvě podoby téže plnější varianty. `agent` nemůže jít kanálem webového rozhraní, kde jsou nástroje vypnuté (rozh. 189); patří ovladači nad API výrobce s nástroji, které ovladač definuje (rozh. 189, varianta 1 kanálu), a dokud ovladač nestojí, varianta nemá kanál (otevřená položka).

### Povinná pole záznamu

Manifest artefaktu od modelu nese v `metadata` pole, která zadání u T4 předepisuje uvádět, pod těmito jmény, aby souhrn seskupoval shodně: `model` (přesný identifikátor), `version` (verze či datum snapshotu modelu), `prompt` (identifikátor šablony promptu, jejíž text navazující práce verzuje), `temperature`, `attempt`, `variant`, `round` (u opravy), `tokensIn`, `tokensOut`, `seconds`, `costUsd`. Sada pole opisuje beze změny (rozh. 172) a souhrn (173) seskupuje po `model` a `variant`; záznam běhu v [`runs/`](../runs/README.md) uvádí součty tokenů, času a nákladů z metadat. Pole chybějící v manifestu je mezera záznamu, ne selhání sady.

### Vymezení proti souběžným pracím

**Práce M. Čorovčáka** (univerzální objektový model, migrace mezi relačními, grafovými a dokumentovými systémy, mapování ORM/OGM/ODM, LLM) a **práce I. Oboňové** (migrace dotazů mezi relačními a grafovými systémy, LLM) mění **datový model**; ORMorpher ho drží: překládá mezi frameworky nad jedním relačním modelem a jedním dialektem (rozh. 129), deterministicky, přes mezireprezentaci s vysloveným slovníkem. Společné je pole — migrace aplikační datové vrstvy — a je nabídnuto společné **měřidlo**: čtyři stupně rozhodnutí 016 nad artefaktem, který nevyrobil nástroj, s řádkem a souhrnem rozhodnutí 172 a 173. Kde cílem není relační databáze, čtvrtý stupeň měřítko nemá (kanonický výsledek i soudci jsou relační) a cesta dá stupně 1–3 a úplnost mapování jen tam, kde cílový framework je jedním ze šesti; obě práce tedy mohou převzít protokol, metriky a formát řádku, ne databáze a soudce. Výsledky se čtou vedle sebe po tomhle: táž metrika, jiná úloha, a text práce to řekne touto větou.

### Co se netvrdí

Protokol nedokazuje nic o žádném modelu; T4–T6 zůstávají vyňaté (§9, oblast 6) a [`traceability.md`](../traceability.md) u nich ve sloupci implementace řekne, že protokol, vstupy, cesta a souhrn existují. Nevolí model ani prompt, nestanoví rozpočet a neříká, kolik z populace navazující práce zvládne; říká, jak se každé její číslo spočítá, aby stálo vedle čísla nástroje.

## Důsledky

- **[`open-items.md`](../open-items.md):** položka rozhodnutí *Metodika srovnání s LLM a vymezení proti souběžným pracím* mizí; položka *Meze složitosti* odkazuje sem pro metodiku a zůstává.
- **[`traceability.md`](../traceability.md):** řádky T4–T6 jmenují protokol a jeho nosiče (výpis vstupů, cesta 172, souhrn 173 po modelu a variantě); stav `vyňaté` zůstává.
- **`ORMConvertor/README.md`**, *Foreign artifacts*: povinná pole metadat a jména variant, aby autor experimentu měl kontrakt na jednom místě; souhrn 173 už po `model` a `variant` seskupuje.
- **Nic v sadách se nemění.** Protokol stojí celý na tom, co rozhodnutí 172 a 173 postavila; sada dál pokus, variantu ani model nezná.
- **Co toto rozhodnutí neurčuje:** model, prompty, korpus RAG, rozpočet a rozsah, který navazující práce z populace vezme; řady hledání mezí jako další populaci (položka); čtvrtý stupeň nad LDBC pro cizí artefakt (rozh. 172 ho nechalo zvlášť).

## Historie

- **2026-10-30, revidováno:** doplněna varianta `agent` a její ablace. Zadání (T5 „RAG / agentní varianta", *Vyhodnocení* „RAG/agentní variantou") a specifikace (*Vztah k dalším projektům*: „variant s RAG nebo agentním přístupem") jmenují agentní přístup vedle RAG a tabulka variant ho neměla; bez něj by srovnání, které specifikace slibuje, nemělo v protokolu hodnotu `variant`, pod kterou by se spočítalo. Volba zůstává: varianta je hodnota manifestu, sada o ní nic neví, souhrn seskupuje. Žádný kód variantu neznal — kanál webového rozhraní (rozh. 189) má šablonu jen pro `zero-shot` a buňku jiné varianty odmítá jménem —, proto oprava v textu, ne nové rozhodnutí.
