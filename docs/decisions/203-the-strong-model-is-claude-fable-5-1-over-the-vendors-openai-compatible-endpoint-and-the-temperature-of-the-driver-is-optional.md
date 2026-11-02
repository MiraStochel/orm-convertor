# 203 — Silným modelem srovnání je Claude Fable 5.1 týmž ovladačem nad koncovým bodem výrobce kompatibilním s OpenAI; teplota je v nastavení ovladače volitelná a model, který ji nepřijímá, jde druhým nastavením rozh. 175; strop výstupu 8192 zůstává a model, jemuž se do stropu počítá skryté uvažování, dostává 32768; fixtura domény nenese pravidlo, které nástroj bere z katalogu

Datum: 2026-11-02
Stav: platí
Požadavky: T4, T5, T6, T3, S2, F6
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu; uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady"); rozhodnutí [050](050-one-home-for-the-singular-plural-heuristic.md), [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [189](189-a-web-frontend-attempt-is-a-new-conversation-with-one-self-contained-prompt-answered-in-text-without-the-web-and-paired-with-the-tool.md) a [202](202-the-driver-over-an-api-is-an-on-demand-run-over-an-openai-compatible-endpoint-and-the-pilot-model-is-qwen35-9b.md); záznam [2026-11-01](../runs/2026-11-01-llm.md); dokumentace výrobce ke dni 2026-11-02 (stránky *Models overview* a *OpenAI SDK compatibility* dokumentace Claude API); otevřená položka *Srovnání s jazykovým modelem nemá silný model ani varianty s nástroji*

## Kontext

**Kanál stojí, silný model ne.** Rozhodnutí 202 dalo ovladač nad koncovým bodem kompatibilním s OpenAI a pilotní lokální model, a první záznam ([2026-11-01](../runs/2026-11-01-llm.md)) ukázal, že kanál projde od `cells.tsv` po souhrn bez ruční práce. Pilotní model o 9 miliardách parametrů ale není „silný LLM", který T4 jmenuje; rozhodnutí 175 nechalo volbu modelu a rozpočtu navazující práci a 202 ji zúžilo na model výrobce, který půjde týmž ovladačem, až bude klíč. Otevřená položka po záznamu nese tři otázky: který model výrobce, s jakým snímkem, klíčem a rozpočtem; zda před dalším během snížit strop výstupu 8192, protože odpověď zacyklená ve zprávě běží do stropu hodinu; a co s komentářem fixtury, který jde ve výpisu vstupů do promptu a jmenuje pravidlo pojmenování tabulek rozh. 050 i číslo rozhodnutí, takže model dostal jména tabulek, která nástroj bere z katalogu (F6).

**Co o kandidátovi říká výrobce** (dokumentace Claude API přečtená 2026-11-02, ne paměť — rozh. 202 připíná fakta o modelu stejně jako fakta o frameworku):

| Fakt | Hodnota |
|---|---|
| koncový bod kompatibilní s OpenAI | `https://api.anthropic.com/v1/chat/completions`; výrobce ho uvádí jako vrstvu „k testování a srovnávání schopností modelů", mezi podporovanými modely je `claude-fable-5-1` |
| identifikátor a snímek | `claude-fable-5-1`; identifikátor bez data je sám připnutý snímek, datovaný tvar neexistuje; vyřazení ne dřív než 2027-09-01 |
| hranice trénovacích dat | červen 2026 (výrobce uvádí shodně *training data cutoff* i *reliable knowledge cutoff*) |
| cena | 10 USD za milion tokenů vstupu, 50 USD za milion tokenů výstupu |
| `temperature` | na modelech Claude 4.7 a novějších vrací hodnota pod 1 stav 400; výrobce radí pole vynechat |
| `seed`, `reasoning_effort` | koncový bod je ignoruje |
| uvažování | adaptivní a vždy zapnuté, výchozí úsilí `high`; vrstva ho do odpovědi nevrací a nastavit ho touto vrstvou nelze; vstupuje do `max_tokens` a do účtovaných tokenů výstupu |
| `usage` | `prompt_tokens` a `completion_tokens` koncový bod vrací; `max_tokens` přijímá, nejvýš 128 k |
| klíč | klíč z konzole výrobce; klíč, který není vázaný na pracovní prostor, by potřeboval hlavičku `anthropic-workspace-id`, kterou ovladač neposílá |
| uchování dat | model vyžaduje třicetidenní uchování dat na straně výrobce; organizace s nulovým uchováním dostane stav 400 |
| odmítnutí | bezpečnostní klasifikátory výrobce mohou požadavek odmítnout; odpověď pak nenese soubory |

Z toho plynou dvě neshody s dnešním ovladačem: `temperature` je v `api.json` povinná a vždy se posílá, a strop 8192 by u modelu, jemuž se do něj počítá skryté uvažování, uťal odpověď dřív než u pilotního modelu.

**Je třeba rozhodnout** model výrobce a jeho nastavení, jak ovladač zachází s modelem bez teploty, co se stropem výstupu a co s komentářem fixtury.

## Zvažované varianty

### Model

**1 — Claude Fable 5.1 (`claude-fable-5-1`).** Volíme, níž. Nejschopnější obecně dostupný model výrobce v době rozhodnutí; výrobce k němu uvádí hranici trénovacích dat, identifikátor je připnutý snímek a závazek vyřazení sahá za konec projektu i obhajobu, takže čtenář práce pokus zopakuje nad týmž modelem. Klíč je k dispozici. Cena je u vzorku třinácti buněk po třech pokusech otázka desítek dolarů (níž, *Rozpočet*), ne rozpočtu, který by projekt neměl.

**2 — Claude Opus 5.5.** Výchozí model výrobce „pro většinu zátěží", za 4 a 20 USD za milion tokenů. Nezamítáme ho jako druhý model výrobce — jde týmž ovladačem nad týmiž prompty —, ale ne jako první: T4 žádá silný model a rozdíl ceny je u vzorku v jednotkách dolarů.

**3 — Model jiného výrobce.** Nezamítáme: T4 říká „alespoň s jedním", a každý další model jde týmž `api.json` s jiným `url`, `model` a klíčem. První silný model je ten, k němuž je klíč a jehož výrobce uvádí fakta, která záznam rozh. 175 žádá.

### Cesta k modelu

**1 — Druhý ovladač nad nativním API výrobce** (plný přístup k nastavení úsilí, k textu uvažování a k cache promptu). Zamítáme: rozh. 202 zvolilo jeden mechanismus pro lokální server i API výrobců, výrobce vrstvu kompatibilní s OpenAI uvádí právě pro srovnávání modelů, a co vrstva zadržuje, protokol nepotřebuje — text uvažování do `reply.md` nejde ani u lokálního modelu (rozh. 189, 202), cache promptu je u tří pokusů téže buňky naopak nežádoucí a úsilí je výchozí hodnota modelu, kterou záznam uvede.

**2 — Týž ovladač nad koncovým bodem kompatibilním s OpenAI.** Volíme.

### Teplota modelu, který ji nepřijímá

**1 — Poslat 1**, jedinou hodnotu, kterou koncový bod přijme. Zamítáme: není to 0 protokolu rozh. 175 a záznam by předstíral nastavení, které model nemá.

**2 — `temperature` v `api.json` volitelná; bez ní požadavek teplotu nenese, řádek pokusu je prázdný a import zapíše `default`.** Volíme: rozh. 189 přesně tento případ už vymezilo pro aplikaci chatu — „kde teplotu rozhraní nenabízí nebo ji model v daném režimu nepřijímá, pokus patří k druhému nastavení rozh. 175 a pole `temperature` říká `default`" — a ovladač jen dostává touž cestu. Tři pokusy téže buňky se pak mohou lišit a best-of-n měří, co má.

### Strop výstupu

**1 — Snížit pod 8192**, jak položka navrhovala: žádný soubor pilotního modelu nepřesáhl asi 4 000 tokenů a pokus uťatý stropem stál hodinu. Zamítáme (Míra, 2026-11-02): nevíme, kolik tokenů bude silnější nebo pozdější model potřebovat, a hodina zacykleného pokusu je cena pilotního modelu, ne pravidlo pro ovladač.

**2 — 8192 zůstává; model, jemuž se do stropu počítá skryté uvažování, dostane v `api.json` 32768.** Volíme: strop pilotního modelu kryje jen viditelnou odpověď, strop Claude Fable 5.1 i uvažování, které výrobce nevrací a jehož rozsah nastavit nelze; 32768 nechává uvažování násobek toho, co soubory potřebují, a zároveň drží nejhorší cenu pokusu pod dvěma dolary (níž).

### Komentář fixtury

**1 — Výpis vstupů komentáře vynechá.** Zamítáme: rozh. 189 vypisuje každou jednotku vstupu beze změny a jedinou výjimku — značku výňatku — nahrazuje, ne škrtá; výňatky reálných projektů nesou v komentářích licenční poznámky (rozh. 138) a vynechání komentářů by změnilo vstup pro každého producenta, nástroj nevyjímaje.

**2 — Každý záznam uvede, že vstupy komentář mají.** Zamítáme (Míra, 2026-11-02): trvalá poznámka pod čarou kvůli jedné větě fixtury.

**3 — Věta jde z fixtury pryč.** Volíme: fixtura `QueryShapes/entities/dapper/Shop.cs` nese jako Dapper jen holé třídy a jména tabulek jsou katalogu (F6), takže komentář říkal víc, než zdroj Dapperu říká — a proč mají třídy předponu `Shop`, stojí pro vývojáře v `Tests/Combined/QueryShapeInputs.cs`. Komentáře fixtur EF Core a JPA opakují, co jejich anotace říkají samy (tabulky, klíče, jejich části), a model z nich nad kód nedostane nic; zůstávají. Záznam 2026-11-01 stál nad vstupy s komentářem a říká to jednou větou; další záznamy to neuvádějí.

## Rozhodnutí

**Silným modelem srovnání T4 je Claude Fable 5.1 (`claude-fable-5-1`), volaný týmž ovladačem rozh. 202 nad koncovým bodem výrobce kompatibilním s OpenAI. `temperature` je v `api.json` volitelná: bez ní ovladač teplotu neposílá, řádek pokusu ji nechá prázdnou a import zapíše `default`, takže běh je druhým nastavením rozh. 175; `seed` se u tohoto modelu neuvádí, protože ho koncový bod ignoruje. Strop výstupu 8192 zůstává hodnotou pilotního modelu a nesnižuje se; model, jemuž se do stropu počítá skryté uvažování, dostává 32768. Fixtura domény matic nenese pravidlo pojmenování tabulek: věta o předponě `Shop` a rozh. 050 jde z hlavičky `entities/dapper/Shop.cs` pryč, záznam 2026-11-01 uvádí, že jeho vstupy ji měly, a žádný další záznam to neuvádí.**

### `api.json` silného modelu

| Pole | Hodnota | Proč |
|---|---|---|
| `url` | `https://api.anthropic.com/v1` | koncový bod výrobce kompatibilní s OpenAI |
| `model` | `claude-fable-5-1` | identifikátor, jak ho koncový bod přijímá |
| `version` | `claude-fable-5-1` | identifikátor bez data je u výrobce sám připnutý snímek; záznam k němu uvede datum pokusů a jméno modelu, které koncový bod vrátil v poli `model` |
| `frontend` | `Claude API, OpenAI-compatible layer (/v1/chat/completions)` | server je API výrobce; vrstva se jmenuje, protože určuje, co z požadavku platí |
| `temperature` | chybí | model ji nepřijímá; řádek prázdný, metadata `default`, druhé nastavení rozh. 175 |
| `maxTokens` | `32768` | strop kryje i skryté uvažování |
| `seed` | chybí | koncový bod ho ignoruje |
| `usdPerMillionTokensIn`, `usdPerMillionTokensOut` | `10`, `50` | ceník výrobce k 2026-11-02; záznam uvede datum |
| `apiKeyVariable` | `ANTHROPIC_API_KEY` | klíč z konzole výrobce vázaný na pracovní prostor; v souboru nikdy |
| `extra` | chybí | nic navíc: `cache_prompt` je llama.cpp, `reasoning_effort` vrstva ignoruje |

### Co záznam uvede navíc proti pilotnímu modelu

| Co | Jak |
|---|---|
| uvažování | vždy zapnuté, výchozí úsilí `high`, touto vrstvou nenastavitelné; do `reply.md` nejde (koncový bod ho nevrací, `reasoning_content` chybí) a je v `tokensOut`, jak ho koncový bod počítá a výrobce účtuje — rozh. 189 ho tam počítá i u obsluhy |
| hranice trénovacích dat | červen 2026 podle výrobce |
| `finish_reason` | v `note` jako u pilotního modelu; odmítnutí bezpečnostním klasifikátorem výrobce je odpověď bez souborů, tedy řádek na stupni 0 jako každá jiná — výsledek modelu, ne nezdar kanálu |
| nezdar kanálu | stav 400 za teplotu, za klíč bez pracovního prostoru nebo za organizaci s nulovým uchováním dat zastaví běh jménem pokusu, jak rozh. 202 určuje; nic z toho není výsledek modelu |
| rozptyl | bez teploty se tři pokusy buňky mohou lišit; best-of-n se tedy může od podílu po pokusech lišit — na rozdíl od pilotního modelu, kde byl rozptyl nula |

### Rozpočet

Vzorek záznamu 2026-11-01 je 13 buněk po třech pokusech, 39 pokusů. Prompt má 1 400–3 400 tokenů (tam ~2 500 v průměru, ~100 000 celkem, 1 USD); výstup s uvažováním odhadujeme na ~8 000 tokenů na pokus (~310 000, ~16 USD), takže očekávaná cena běhu je **~20 USD** a nejhorší, kdyby každý pokus došel na strop, **~65 USD** (39 × 32 768 × 50 USD za milion). Záznam uvede skutečnou cenu z `costUsd` řádků, ne odhad.

### Co se netvrdí

Běh silného modelu je navazující práce (položka): toto rozhodnutí volí model a nastavení, nezapisuje běh. Druhý model výrobce (Claude Opus 5.5) a modely jiných výrobců jdou týmž ovladačem, až je navazující práce zvolí. Varianty `rag`, `repair` a `agent` ovladač dál nenese. T4–T6 zůstávají vyňaté (§9, oblast 6).

## Důsledky

- **Kód:** `Tests/Foreign/ApiAttempt.cs` — `temperature` v `ApiSettings` volitelná, požadavek bez ní teplotu nenese, řádek je prázdný; `WebAttempts.Import` zapíše `default` i u kanálu `api`; `ApiAttemptTest` o případ modelu bez teploty. `Tests/Database/QueryShapes/entities/dapper/Shop.cs` bez věty o pravidlu pojmenování; prompty vzorku z ní vznikají znovu (jiný `promptSha256`), a protože odpovědi pilotního modelu jsou pokusy nad promptem s komentářem, nový běh pilotního modelu jde do nového adresáře běhu.
- **[`open-items.md`](../open-items.md):** položka se zužuje na běh silného modelu a na varianty s nástroji; otázka stropu a komentáře mizí.
- **[`architecture.md`](../architecture.md) §6.2:** silný model u kanálu `api`, volitelná teplota v řádku ovladače.
- **`ORMConvertor/README.md`**, *A language model over an API*: `api.json` silného modelu a co u API výrobce platí.
- **[`traceability.md`](../traceability.md), T4:** silný model ve sloupci implementace; stav `vyňaté` zůstává.
- **[`runs/README.md`](../runs/README.md):** co záznam u API výrobce uvádí; záznam [2026-11-01](../runs/2026-11-01-llm.md) nese větu, že jeho vstupy komentář měly (Míra, 2026-11-02; jediná změna hotového záznamu, místo *Nesrovnalostí*, protože nejde o omyl záznamu, ale o fakt vstupů, který další běhy nemají).
