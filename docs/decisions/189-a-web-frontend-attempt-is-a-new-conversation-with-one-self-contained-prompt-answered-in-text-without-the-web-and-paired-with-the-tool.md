# 189 — Pokus jazykového modelu ve webovém rozhraní je nová konverzace s jedním samostatným promptem a odpověď je text v pevném tvaru; přístup k webu je zakázaný, čas, tokeny a kredity měří obsluha a hlášení modelu se jen opisuje, a souhrn staví pokusy vedle nástroje nad týmiž buňkami

Datum: 2026-10-26
Stav: revidováno
Požadavky: T4, T5, T6, T3
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu; uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady"); schválená [specifikace](../specifikace.tex) (T4–T6: projekt připraví data, rozhraní a měřicí infrastrukturu); rozhodnutí [167](167-records-of-runs-and-measurements-have-a-folder-of-their-own.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md) a [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md); kontrola protokolu z hosta z 2026-10-25 (níž, *Kontext*); otevřená položka *Srovnání s jazykovým modelem nemá model, prompty ani záznam*

## Kontext

**Protokol stojí, kanál ne.** Rozhodnutí 175 určilo, co je jedno měření modelu — artefakt jednoho pokusu, který soudí cesta cizího artefaktu (rozh. 172) —, kolik pokusů, při jaké teplotě, co je varianta a která pole metadat záznam nese. Neurčilo, jak se prompt k modelu dostane a jak se z odpovědi stane adresář s manifestem; model, prompty a rozpočet nechalo navazující práci a otevřená položka se na ně ptá dál.

**Kontrola z 2026-10-25 prošla protokolem jednou buňkou** a ukázala, co kanál musí vyřešit. Běžela na MIS3 z hosta nad stromem `014efee`: buňka `matrix:AggregationGroupingAndHaving` (`GROUP BY` se `SUM`, `COUNT(*)` a `HAVING`) z Dapperu do EF Core, model `claude-opus-5-5` volaný jako agent Claude Code, tři pokusy bez čehokoli navíc a tři nad kopií repozitáře bez překladového kódu, z níž byly vyňaté tvary měřené kategorie, entity v cílovém tvaru a kanonický výsledek. Všech šest artefaktů došlo na čtvrtý stupeň a shodlo se s kanonickým výsledkem, stejně jako artefakt nástroje; cesta ani sady se měnit nemusely. Kanál přitom nedal tři věci, které protokol žádá: teplotu nešlo nastavit; tokeny přišly jako jeden součet za agenta, z větší části jeho vlastní systémový prompt a definice nástrojů posílané v každém kole, bez rozdělení na vstup a výstup a bez ceny; a agent soubory zapisoval sám, tedy nástrojem, jehož použití se od toho, co model ví, oddělit nedá. Agent Claude Code navíc není kanál, který by čtenář práce zopakoval.

**Webové rozhraní je kanál, který zopakuje každý**, bez programu pro každého výrobce a bez klíče k API. Rozhraní jsou ale dvojí a liší se právě v tom, co protokol měří:

| | Aplikace chatu (claude.ai, chatgpt.com, gemini.google.com) | Vývojářská konzole (Workbench v Anthropic Console, OpenAI Playground, Google AI Studio) |
|---|---|---|
| teplota | nenastavitelná, výchozí | nastavitelná, kde ji model v daném režimu přijímá |
| tokeny, cena | neukáže; nanejvýš měřič využití předplatného či kreditů | tokeny volání ukáže, cenu z nich dá ceník |
| nástroje | hledání na webu, spouštění kódu, paměť napříč konverzacemi a vlastní pokyny podle nastavení účtu; smí-li model hledat, rozhodne se k tomu i bez pokynu | žádný, dokud ho obsluha nedefinuje |
| systémový prompt | výrobce, obsluha ho neovlivní | prázdný, nebo obsluhy |

**Srovnání s nástrojem potřebuje páry.** Souhrn rozhodnutí 173 staví řádky cizích artefaktů po producentovi, modelu a variantě vedle podílů nástroje přes celou populaci výpisu. Pokusy ve webovém rozhraní dělá obsluha ručně, takže pokryjí vzorek — desítky buněk, ne stovky klíčů v 36 směrech —, a podíl vzorku vedle podílu nástroje nad celým výpisem srovnává dvě různé populace.

**Repozitář je veřejný a nese odpovědi.** Fork je na GitHubu veřejný od 2026-07-06, původní prototyp od 2024-11-25. `Tests/Database/QueryShapes/<kategorie>/` nese ručně psaný tvar každého dotazu matice ve všech šesti jazycích a `QueryShapes/entities/` entity matice v každém z nich, `Tests/Database/Differential/results/` kanonické výsledky, [`architecture.md`](../architecture.md) a [`subset.md`](../subset.md) pravidla, podle nichž nástroj překládá. Vstupy nesou dost jmen, aby je vyhledávač našel (`ShopOrderLine`, `CompanyId`, `{{schema}}`). Model, který hledá, tedy může najít odpověď měřené buňky — právě proto kontrola z 2026-10-25 z kopie pro variantu s korpusem odpovědi vyňala.

**Model ve webovém rozhraní nezná svůj čas ani spotřebu.** T4 žádá uvádět tokeny, čas a náklady. Model v konverzaci nevidí počítadlo tokenů ani stav kreditů a hodiny má, jen spustí-li kód; co o nich napíše, je odhad, nebo výmysl. Číslo, které má stát v záznamu, musí mít jiný zdroj.

**Je třeba rozhodnout** kanál a co je v něm pokus; co prompt nese a co nikdy; v jakém tvaru model odpovídá a jak se odpověď stane artefaktem; zda a jak zakázat přístup k webu a k repozitáři; odkud jsou čísla času, tokenů a kreditů a k čemu je hlášení modelu; a jak se vzorek poměří s nástrojem.

## Zvažované varianty

### Kanál

**1 — Ovladač nad API výrobce**: program, který čte výpis vstupů, volá model a z odpovědi rovnou píše artefakt i metadata z údaje o spotřebě. Přesný (teplota, tokeny vstupu a výstupu, cena) a škálovatelný na celou populaci; kontrola z 2026-10-25 ho jmenovala jako další krok. Nezamítáme ho, ale teď ho nevolíme: je to program pro každého výrobce, s klíčem a placenými voláními, která projekt nemá krytá (rozh. 175, varianta 1), a pro vzorek, který obsluha vybírá a prochází ručně, nedá nic, co by nedala konzole. Zůstává cestou pro celou populaci a pro navazující práci; prompt a import tohoto rozhodnutí mu poslouží beze změny, protože i odpověď API je text v témž tvaru.

**2 — Webové rozhraní, model vydá soubory** (příloha, archiv, plátno). Pohodlné tam, kde to rozhraní umí. Zamítáme: ne každé rozhraní soubory vydá, a které ano, vyrábí je spuštěným kódem v sandboxu — tedy nástrojem, který níž zakazujeme —; soubor stažený z rozhraní navíc nese méně jistý původ než text odpovědi, který obsluha zkopíruje celý. *Doplněno 2026-10-30, znovu zváženo kvůli času:* aplikace chatu odpověď vypisuje postupně a animuje ji, takže `seconds` obsluhy roste o vykreslování i tam, kde model odpověď už má. Soubor to neřeší ze tří důvodů. Obsah souboru model generuje token po tokenu stejně, jen uvnitř volání nástroje, a zda ho rozhraní ukáže dřív než animovaný text, není změřeno a liší se po výrobcích. Sandbox, který soubor píše, dává modelu běhové prostředí, ve kterém si odpověď může ověřit — pokus `zero-shot` se tím tiše stává opravným cyklem a otázka T6, co přidá validace, ztrácí odpověď; vydání souboru proto nejde zapnout, aniž by se zapnul nástroj, který toto rozhodnutí vypíná. A hlášení by stejně přišlo v textu odpovědi, takže obsluha by skládala dva zdroje různého původu. Čas kanálu chatu měří kanál, ne model: proto je aplikace chatu druhým nastavením a čas se s nástrojem srovnává jen z pokusů v konzoli nebo přes API; pokus v chatu dává správnost, ne čas.

**3 — Webové rozhraní, odpověď textem v pevném tvaru, který deterministický importér převede na artefakt.** Volíme, níž.

### Přístup k webu a k repozitáři

**1 — Dovolit**, protože vývojář by model s hledáním opravdu používal. Zamítáme ze čtyř důvodů, níž.

**2 — Zakázat jen větou v promptu.** Zamítáme jako jedinou pojistku: aplikace chatu se k hledání rozhoduje sama a věta v promptu nic nevynutí.

**3 — Zakázat ve čtyřech vrstvách**: nastavení rozhraní, výslovný zákaz v promptu, prompt bez stop vedoucích k repozitáři a importér, který pokus se stopou hledání odmítne. Volíme, níž.

### Čas, tokeny a kredity

**1 — Hlášení modelu jako míra.** Zamítáme z důvodu v kontextu: model čísla nezná.

**2 — Jen měření obsluhy, model se neptat.** Čistší záznam. Zamítáme: dotaz nic nestojí, hlášení nese i to, jaké nástroje a zdroje model použil a co předpokládal — druhou vrstvu kontroly přístupu k webu —, a rozdíl mezi hlášením a měřením je pozorování, které text práce může použít.

**3 — Obsluha měří, model hlásí, a obojí stojí v záznamu odděleně.** Volíme, níž.

## Rozhodnutí

**Volíme kanál webového rozhraní s odpovědí v textu, zákaz přístupu k webu ve čtyřech vrstvách a měření obsluhy vedle hlášení modelu. Pokus je nová konverzace, do které obsluha vloží jeden samostatný prompt vygenerovaný ze šablony a z vypsaného vstupu buňky, a jedna odpověď modelu. Odpověď nese každý soubor artefaktu jako blok kódu se značkou a na konci hlášení modelu; importér z ní deterministicky udělá adresář cizího artefaktu s manifestem a sada ho soudí jako každý jiný (rozh. 172). Čas, tokeny, náklady a kredity v polích rozhodnutí 175 jsou měření obsluhy nebo údaj rozhraní; hlášení modelu se opisuje do vlastního pole a do těch polí nikdy nevstupuje. Hledání na webu, spouštění kódu a každý jiný nástroj jsou v rozhraní vypnuté a promptem zakázané, prompt nenese nic, co vede k repozitáři, a pokus se stopou hledání importér odmítne a obsluha ho opakuje. Nástroj stojí ve srovnání nad týmiž buňkami: experimentální běh (rozh. 173) nad vstupy vzorku zapíše jeho řádky do téhož adresáře a souhrn je postaví vedle pokusů buňku po buňce.**

### Pokus a nastavení rozhraní

- **Pokus je nová konverzace**: jeden vložený prompt, jedna odpověď. Obsluha prompt nemění, na otázku modelu neodpovídá a o pokračování nežádá; odpověď, kterou rozhraní uřízlo, je výsledkem pokusu. Tři pokusy buňky (rozh. 175) jsou tři konverzace s týmž promptem.
- **Opravné kolo** varianty `repair` je další zpráva v téže konverzaci: řádek verdiktu cesty z předchozího kola (stupeň, výsledek, diagnostika) a pokyn vydat celý artefakt znovu; `round` v metadatech. Šablona kola přijde s variantou `rag`, na které oprava stojí (rozh. 175).
- **Konzole je hlavní nastavení, aplikace chatu druhé.** V konzoli se nastaví teplota 0 (rozh. 175), prázdný systémový prompt, žádný nástroj a strop výstupu na maximum modelu. Kde teplotu rozhraní nenabízí nebo ji model v daném režimu nepřijímá, pokus patří k druhému nastavení rozh. 175 a pole `temperature` říká `default`. Obě nastavení jdou týmiž prompty a týmž importem a souhrn je nesmíchá: rozlišuje je pole `channel` (`console`, `chat`).
- **Před každým pokusem** obsluha v rozhraní vypne hledání a prohlížení webu, spouštění kódu, konektory, soubory a projekty, paměť napříč konverzacemi, personalizaci a vlastní pokyny. Rozhraní, které hledání vypnout nedovolí, se pro protokol nepoužije.

### Prompt

Prompt je **jeden samostatný text** v angličtině, vygenerovaný ze šablony a z vypsaného vstupu buňky (rozh. 172); obsluha ho vloží celý. Anglicky, protože vstupy, identifikátory i dokumentace frameworků jsou anglické a český prompt by přidal krok překladu, který nástroj nemá.

| Oddíl | Co nese |
|---|---|
| úloha | zdrojový framework a jeho verze → cílový framework a jeho připnutá verze (z deskriptorů, které drží `Directory.Packages.props` a `pom.xml`), dialekt SQL Serveru 2022, tabulky existují a nemění se |
| pravidla | jen tato zpráva a vlastní znalost; žádné hledání, odkaz, spuštění kódu, soubor ani jiný nástroj, a nehledat, odkud úloha pochází; jedna odpověď bez doplňujících otázek, nejasnost rozhodnout a předpoklad uvést v hlášení; `{{schema}}` ponechat, kde ho vstup měl |
| tvar artefaktu | jednotky, které sada od cíle čeká, popsané slovy a podpisem — tvar ručně psaných artefaktů v `Tests/Database/Foreign/`, ne jejich obsah: entity, holá dotazová metoda s podpisem harnessu, mapovací dokumenty, každá s rolí jménem `ConversionContentType` |
| varianta | `zero-shot` nic; `few-shot` tři příklady překladu v témž směru, nikdy měřený klíč ani entity měřeného vstupu v cílovém tvaru; `rag` korpus vložený do promptu (rozh. 175) |
| vstup | každá jednotka vstupu jako blok se značkou souboru, beze změny |
| tvar odpovědi a hlášení | níž |

**Co prompt nikdy nenese:** klíč buňky (`answers`) ani jiný identifikátor výpisu, jméno nástroje či repozitáře, cestu v repozitáři, nic z artefaktu nástroje ani z kanonického výsledku. Identifikátor pokusu je jméno jeho složky a do promptu nejde; tři pokusy buňky mají prompt bajtově shodný.

**Vstup, který nástroj jmenuje sám.** Výňatky reálných projektů nesou v komentářích značku výňatku `ORMConvertor:` (rozh. [138](138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md)), takže prompt případové studie by jméno nástroje nesl a věrná kopie komentáře by importér zastavil. Generátor proto značku v promptu nahradí neutrální `Excerpt note:` a nic jiného ve vstupu nemění: obsah komentáře zůstává a nástroj komentáře stejně nečte. Že hotový prompt žádné ze jmen, která importér hledá, nenese, generátor ověří a jinak selže jménem. Z téhož zákazu plyne, odkud jsou příklady varianty `few-shot`: ručně psané překlady v tvaru artefaktu cíle, ne překlady nástroje — výkladová stránka (rozh. [099](099-examples-are-content-not-a-choice.md)) ukazuje výstup nástroje a tvary v `QueryShapes/` jsou vstupy, ne artefakty —, takže sadu příkladů je třeba napsat.

**Šablona je soubor s identifikátorem** (`web-zero-shot-1`, `web-few-shot-1`), který jde do pole `prompt`. Text šablony, kterou použil zapsaný běh, se nemění — změna je nová šablona s novým identifikátorem, aby záznam ukazoval na text, který platil. Do metadat jde i otisk SHA-256 vygenerovaného promptu (`promptSha256`), takže záznam dokládá přesný text, ne jen šablonu. Šablony jsou první, které projekt dává jako součást rozhraní, jak ho specifikace žádá; navazující práce přidá vlastní pod jiným identifikátorem a pole `prompt` je rozliší.

### Odpověď a import

Model odpovídá **textem v odpovědi samé**, bez přílohy, archivu a plátna:

~~~text
FILE: ShopOrderLine.cs | CSharpEntity
```csharp
…celý obsah souboru…
```

REPORT:
```json
{ … }
```
~~~

Obsluha zkopíruje celou odpověď tlačítkem rozhraní (jako Markdown) do `reply.md` ve složce pokusu. **Importér je deterministický**: soubor je řádek `FILE: <cesta> | <role>` a první blok kódu za ním, text mimo bloky nečte. Z buňky vezme `answers`, `source` a `target`, z odpovědi jednotky, zapíše `producer: llm` a metadata (níž) a vytvoří adresář cizího artefaktu s `artifact.json`, který sada soudí touž cestou jako každý jiný (rozh. 172).

**Dva druhy nezdaru se nesmí plést:**

| Co | Čím to je | Co se stane |
|---|---|---|
| odpověď bez souboru (model se ptá, odmítne, odpoví prózou), odpověď, kterou importér nepřečte, soubor s rolí, kterou cíl nemá, uříznutý soubor | výsledkem modelu | pokus je řádek — bez artefaktu na stupni 0 s důvodem, jako odmítnutí nástroje (rozh. 173), s uříznutým souborem na stupni, kam ho sada pustí —, a počítá se do podílu po pokusech |
| stopa hledání (níž), pokus v konverzaci, která nebyla nová, prompt změněný obsluhou | porušením protokolu, ne měřením | importér pokus odmítne jménem, obsluha ho opakuje v nové konverzaci a záznam uvede počet odmítnutých po modelu |

Kdyby se nezdar modelu opakoval jako porušení protokolu, podíl po pokusech by vyšel lepší, než model je.

### Měření a hlášení

| Pole | Kdo | Odkud |
|---|---|---|
| `model` | obsluha | přesný identifikátor, jak ho rozhraní ukáže: v konzoli jméno modelu API, v aplikaci chatu volba modelu i s režimem |
| `version` | obsluha | snapshot v konzoli; v aplikaci chatu, která ho neukáže, datum pokusu |
| `channel`, `frontend` | obsluha | `console`, nebo `chat`; host rozhraní |
| `temperature` | obsluha | nastavená hodnota, nebo `default` |
| `seconds` | obsluha | od odeslání promptu do konce odpovědi, vlastními hodinami |
| `tokensIn`, `tokensOut`, `costUsd` | obsluha | údaj konzole za volání, cena z něj ceníkem výrobce k datu pokusu; aplikace chatu je nemá a pole chybí — mezera záznamu podle rozh. 175, nikdy odhad |
| `credits`, `creditsUnit` | obsluha | rozdíl měřiče kreditů či využití rozhraní před pokusem a po něm, kde ho rozhraní ukazuje |
| `prompt`, `promptSha256`, `variant`, `attempt`, `round` | generátor | šablona, otisk, buňka |
| `promptChars`, `replyChars` | importér | velikost výměny ve znacích, nezávislá na kanálu i tokenizéru |
| `selfReport` | importér | blok `REPORT` modelu beze změny |

Obsluha zapisuje změřené hodnoty do jedné tabulky pokusů, kterou generátor založí s prázdnými sloupci, a importér z ní metadata vezme. **Skryté uvažování** modelů, které ho mají, v `replyChars` není a aplikace chatu ho neukáže; konzole ho účtuje jako výstup, takže je v `tokensOut`. Záznam to u každého modelu řekne.

**Hlášení modelu** žádá prompt na konci odpovědi jako JSON: jak dlouho model pracoval a kolik tokenů či kreditů spotřeboval, u každého s údajem `measured`, `estimated` nebo `unknown`; jaké nástroje použil a jaké zdroje četl (obojí smí být jen prázdné); jaké předpoklady udělal. Importér ho opíše do `selfReport` a do polí rozh. 175 nikdy nevstoupí. Slouží dvakrát: deklarace nástrojů a zdrojů je jednou z vrstev kontroly přístupu k webu (níž) a rozdíl mezi hlášením a měřením obsluhy je pozorování o modelu, ne míra.

### Přístup k webu a k repozitáři je zakázaný

Zakazujeme ho ve všech variantách rozh. 175, ze čtyř důvodů:

1. **Měřila by se kopie nástroje.** T4 porovnává pravidlový přístup s modelem. Model, který najde `QueryShapes/<kategorie>/` v cílovém jazyce, kanonický výsledek nebo pravidla v `architecture.md`, opíše ruční tvar matice nebo zopakuje překlad nástroje; srovnání by se sbíhalo k baseline a o modelu by neřeklo nic.
2. **Stejná sada vstupů.** T4 a rozh. 175 dávají modelu vstup a k němu přesně to, co řekne varianta. Výsledky hledání jsou vstup navíc, nezapsaný a neopakovatelný: web se mění a co model našel, záznam nenese.
3. **Scénář by nebyl reprezentativní.** Vývojář, který model s hledáním opravdu použije, překládá svůj projekt, k němuž na webu žádný klíč odpovědí neleží. Náš veřejný repozitář je vedlejší produkt měření, ne vlastnost scénáře; dovolit hledání by měřilo situaci, kterou žádný uživatel nemá.
4. **Varianty by splynuly.** T5 a T6 měří vliv dokumentace, schématu, příkladů a opravného cyklu jako rozdíl mezi variantami s řízeným obsahem. Hledání by do každé varianty — i do `zero-shot` — přidalo neznámý korpus a rozdíl, který T6 žádá, by rozmazalo.

Varianta s hledáním na webu je měřitelná jen nad vstupy, k nimž na webu odpověď neleží, a do protokolu rozh. 175 nepatří; kdo ji bude chtít, rozhodne ji zvlášť.

**Čtyři vrstvy**, protože žádná sama nestačí:

| Vrstva | Co dělá | Proč sama nestačí |
|---|---|---|
| rozhraní | hledání, kód, konektory, paměť a vlastní pokyny vypnuté | obsluha se může splést a rozhraní se mění |
| prompt | výslovný zákaz nástrojů i hledání původu úlohy a pokyn přiznat v hlášení nástroj, který rozhraní použilo samo | model ho nemusí dodržet a nic ho nevynutí |
| prompt bez stop | žádný klíč, jméno nástroje ani cesta repozitáře (výš) | jména ve vstupu jsou vyhledatelná sama, proto rozhodují první dvě vrstvy |
| importér | odmítne pokus, jehož odpověď mimo bloky souborů nese odkaz nebo citaci zdroje, jehož hlášení jmenuje nástroj nebo zdroj, nebo který kdekoli jmenuje repozitář, nástroj či jejich autory (`orm-convertor`, `ORMorpher`, `ORMConvertor`, `MiraStochel`, `milan252525`) | pozná jen stopu, kterou odpověď nese |

Odkaz uvnitř souboru — jmenný prostor XML, DOCTYPE mapperu MyBatisu, schéma `orm.xml` — stopou hledání není; importér hledá odkazy jen mimo bloky souborů, jména repozitáře všude.

**Co zakázat nejde, je trénovací korpus.** Model, jehož hranice trénovacích dat leží po 2026-07-06, mohl vidět tvary matice z forku, po 2024-11-25 původní prototyp. Záznam běhu uvede u každého modelu hranici, kterou zveřejnil výrobce, a text práce to řekne touto větou; vyloučit to protokol neumí.

### Srovnání s nástrojem nad týmiž buňkami

Podíl vzorku stojí jen vedle podílu nástroje nad týmž vzorkem. Nástroj je deterministický (S2), na buňku má jeden řádek, a jeho podíl nad buňkami vzorku je číslo, vedle kterého stojí podíl po pokusech i best-of-n (rozh. 175). Celý postup proto jde jedním adresářem běhu:

| Krok | Kdo | Co vznikne |
|---|---|---|
| 1. výpis vstupů | sada (rozh. 172) | složka na klíč a zdroj s `input.json` |
| 2. výběr buněk | obsluha | tabulka buněk: klíč, zdroj, cíl, varianta; záznam řekne, proč tyto |
| 3. generování | sada, běh na vyžádání | složka na pokus s `prompt.md`, tabulka pokusů s prázdnými sloupci měření a `inputs/` běhu jen se vstupy vzorku |
| 4. pokus | obsluha ve webovém rozhraní | `reply.md` ve složce pokusu, vyplněný řádek tabulky pokusů |
| 5. import | sada, běh na vyžádání | adresář cizího artefaktu s `artifact.json` za každý pokus s artefaktem, řádek na stupni 0 za každý bez něj |
| 6. nástroj | experimentální běh (rozh. 173) nad `inputs/` běhu | řádky nástroje nad týmiž vstupy |
| 7. soud | běh cizích artefaktů (rozh. 172) do téhož adresáře | řádky pokusů |
| 8. souhrn | souhrn experimentálního běhu | buňka po buňce řádek nástroje vedle pokusů; po modelu, kanálu a variantě podíl po pokusech, best-of-n a podíl nástroje nad týmiž buňkami |

Kroky 3 a 5 nepotřebují databázi ani instanci a běží na obou strojích; kroky 6 a 7 potřebují testovací databázi a pro javové cíle javovou sadu — z hosta je to kontrola, záznam vzniká v zafixovaném prostředí na MIS3 (rozh. 172). Krok 4 je jediný ruční a jediný, u kterého na stroji nezáleží.

**Popsaný běh** dostane soubor v [`runs/`](../runs/README.md) (rozh. 167) s tím, co žádají rozh. 172 a 175, a navíc: kanál a rozhraní, šablony, vybrané buňky a důvod výběru, počet odmítnutých pokusů po modelu, hranici trénovacích dat každého modelu a součty času, tokenů, nákladů a kreditů tam, kde je kanál měl.

### Co se netvrdí

Rozhodnutí nevolí model ani rozpočet a nedělá korpus varianty `rag` — ty zůstávají otevřené položce. Webový kanál není rovnocenný ovladači nad API: aplikace chatu dává jen druhé nastavení rozh. 175 a tokeny nemá, a vzorek, který obsluha projde ručně, není populace. Zákaz přístupu k webu sníží možnost, že model odpověď opíše, nevyloučí ji: trénovací korpus zůstává a záznam ho jen jmenuje. T4–T6 zůstávají vyňaté (§9, oblast 6); zda by je zapsaný běh projektu nad vzorkem z vyňaté oblasti vyvedl, toto rozhodnutí neříká.

## Důsledky

- **[`open-items.md`](../open-items.md):** položka rozhodnutí *Srovnání s jazykovým modelem nemá model, prompty ani záznam* se zužuje na model a jeho verzi, korpus varianty `rag` s hledáním v něm, rozpočet a rozsah; vzniká práce *Webový kanál srovnání s jazykovým modelem nestojí* — šablony `web-zero-shot-1` a `web-few-shot-1`, generátor promptů, importér odpovědí, oddíl souhrnu nad týmiž buňkami.
- **Generátor a importér jsou běhy na vyžádání v .NET sadě** vedle výpisu vstupů a řídí je proměnné prostředí jako výpis a běh cizích artefaktů: tvar artefaktu každého cíle a připnuté verze tam už jsou. Šablony leží vedle cesty, v `Tests/Foreign/Prompts/`.
- **Cesta cizího artefaktu ani javová sada se nemění.** Pokus bez artefaktu zapíše importér jako řádek na stupni 0 v témž tvaru, jaký píše cesta, a souhrn ho čte s řádky cizích artefaktů.
- **Po implementaci:** [`architecture.md`](../architecture.md) §6.2 — v oddílu *Cizí artefakt* import odpovědi, v *Experimentální běh* oddíl souhrnu nad týmiž buňkami; `ORMConvertor/README.md`, *Foreign artifacts*, pododdíl s postupem pro obsluhu (nastavení rozhraní, příkazy kroků 3 a 5–8, tabulka pokusů); [`runs/README.md`](../runs/README.md) druh běhu *srovnání s jazykovým modelem*; [`traceability.md`](../traceability.md) řádky T4–T6 ve sloupci implementace, stav zůstává `vyňaté`.
- **Cena.** Pokus stojí obsluhu jednu konverzaci — vložit, počkat, zkopírovat, zapsat několik čísel —, buňka se třemi pokusy ve dvou variantách šest konverzací. Vzorek desítek buněk je práce na hodiny, ne na dny; celá populace patří ovladači nad API.

## Historie

- **2026-10-26, revidováno před implementací:** doplněn vstup, který nástroj jmenuje sám — značka výňatku rozh. 138 jde do promptu neutrální a generátor hotový prompt ověří —, a odkud jsou příklady varianty `few-shot`.
