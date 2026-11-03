# 205 — Silný model jde nejdřív kanálem `code`: Claude Code bez nástrojů a bez přizpůsobení na předplatném výrobce, ovladač každý požadavek zachytí a pokus projde, jen když nese prompt a změřený kontext rozhraní; hlášení modelu o kontextu rozhraní v tomto kanálu stopou není a běh kanálem `api` rozh. 203 zůstává rozhodnutý na později

Datum: 2026-11-03
Stav: platí
Požadavky: T4, T5, T6, T3
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady"); rozhodnutí [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [189](189-a-web-frontend-attempt-is-a-new-conversation-with-one-self-contained-prompt-answered-in-text-without-the-web-and-paired-with-the-tool.md), [202](202-the-driver-over-an-api-is-an-on-demand-run-over-an-openai-compatible-endpoint-and-the-pilot-model-is-qwen35-9b.md), [203](203-the-strong-model-is-claude-fable-5-1-over-the-vendors-openai-compatible-endpoint-and-the-temperature-of-the-driver-is-optional.md) a [204](204-the-reply-is-a-whole-file-judged-as-one-zero-shot-as-written-and-the-import-repairs-the-envelope-of-every-other-variant.md); měření 2026-11-03 na MIS2 — dva požadavky Claude Code 2.1.317 zachycené logovacím proxy a jejich výstup `stream-json` (níž), trvale `CodeAttemptTest.WhatReachesTheModelIsThePromptAndTheMeasuredContext`; dokumentace Claude API ke dni 2026-11-03 (výchozí úsilí a strop výstupu Claude Fable 5.1); otevřená položka *Srovnání s jazykovým modelem nemá běh silného modelu ani varianty s nástroji*

## Kontext

**Model je zvolený, cesta k němu stojí peníze, které projekt jinak nepotřebuje.** Rozhodnutí 203 zvolilo Claude Fable 5.1 a kanál `api` rozh. 202 nad koncovým bodem výrobce kompatibilním s OpenAI. API se účtuje po tokenech zvlášť: běh, který položka žádá — 13 buněk vzorku, každá ve variantách `zero-shot` i `few-shot`, tři pokusy, 78 pokusů — vychází podle rozpočtu 203 zhruba na 40 USD a nejhůř na 130 USD. Míra má předplatné výrobce (Max), které nese claude.ai a Claude Code a měří se okny: pětihodinovým a sedmidenním. Předplatným vedou k témuž modelu dvě cesty: webové rozhraní claude.ai, tedy kanál `chat` rozh. 189 s 78 pokusy ručně, a Claude Code spuštěný bez interakce (`claude -p`).

**Co Claude Code modelu pošle, jsme změřili, ne převzali z dokumentace.** Claude Code 2.1.317 jsme na MIS2 spustili s přepínači níž, s promptem „Reply with the single word OK." na standardním vstupu, v prázdné složce mimo repozitář, a s `ANTHROPIC_BASE_URL` na logovací proxy, které požadavek uložilo a beze změny předalo výrobci. Claude Code proměnnou ctí i při přihlášení předplatným. Zachycené tělo `POST /v1/messages`:

| Část požadavku | Obsah |
|---|---|
| `system` | dva bloky: řádek fakturační hlavičky (`x-anthropic-billing-header: cc_version=…`) a věta „You are a Claude agent, built on Anthropic's Claude Agent SDK."; vlastní systémový prompt (`--system-prompt ""`) je prázdný, výchozí systémový prompt Claude Code nejde |
| první zpráva `user` | připomínka `<system-reminder>` s jediným oddílem `# userEmail` — e-mail účtu a věta, že kontext připojilo rozhraní a není součástí zprávy —, za ní prompt beze změny |
| zpráva `system` uprostřed konverzace | `# Environment`: pracovní adresář (v profilu uživatele, takže se jménem uživatele Windows), „není repozitář git", platforma, shell, verze OS, věta o stažených souborech, jméno a identifikátor modelu, hranice jeho znalostí (červen 2026) a dnešní datum |
| `tools` | prázdné |
| `thinking`, `output_config` | adaptivní uvažování (`display: updates`), `effort` z přepínače `--effort` |
| `max_tokens` | 64000, s proměnnou `CLAUDE_CODE_MAX_OUTPUT_TOKENS` její hodnota (změřeno s 32768) |
| ostatní | `context_management` (mazání bloků uvažování, `keep: all`), cache prefixu s životností 1 h na systémovém bloku a na zprávě prostředí, `metadata.user_id` (zařízení, účet, relace) |

Úvodní událost výstupu (`system`/`init`) hlásí prázdné nástroje, servery MCP, skills a příkazy, verzi Claude Code a `apiKeySource: none`, tedy přihlášení předplatným. Po každém volání přijde `rate_limit_event` s vytížením pětihodinového a sedmidenního okna a časem jejich obnovení; závěrečná událost `result` nese text odpovědi, `stop_reason`, tokeny (vstup, zápis a čtení cache, výstup, z toho uvažování), dobu a `total_cost_usd` — cenu volání podle ceníku API, kterou předplatné neúčtuje.

**E-mail účtu se vypnout nedá.** Kód Claude Code ho připojí vždy, když je přihlášen účet výrobce; jedinou větví, která ho vynechá, je jiný transport (`ANTHROPIC_UNIX_SOCKET`). Přepínač `--bare` by vynechal připomínku i prostředí, ale přihlášení předplatným nečte — jde jen klíčem API, a pak je čistší rovnou kanál `api`.

**A tím se Claude Code srazí s pravidlem promptu.** Pravidlo 2 šablon rozh. 189 žádá, aby model kontext, který rozhraní dodalo, ignoroval a vyjmenoval v hlášení (`contextUsed`), a `WebTrace` takovou odpověď odmítne jako stopu hledání a pokus se dělá znovu. V Claude Code dodá rozhraní e-mail každému pokusu: model, který pravidlo poslechne, by dostal každou odpověď odmítnutou a opakovanou bez konce. Ve webovém rozhraní je hlášení modelu jediným oknem do toho, co rozhraní přidalo; tady je oknem zachycený požadavek.

## Zvažované varianty

**1 — Kanál `api` podle rozh. 203 hned.** Nejčistší cesta: model dostane prompt a nic jiného, bez nového kódu a bez rozhodnutí. Stojí 40 až 130 USD navíc k předplatnému, které už je zaplacené. Nezamítáme ho: zůstává rozhodnutý a odložený (Míra, 2026-11-03) a níž má své místo.

**2 — Kanál `chat` rozh. 189 na předplatném.** Bez kódu, ale 78 pokusů ručně, se skrytým systémovým promptem claude.ai, který nikdo nezměří, bez tokenů a s časem, který zahrnuje vykreslování rozhraní a s nástrojem se nesrovnává (rozh. 189). Zamítáme: oproti variantě 4 je horší ve všem kromě nulového kódu.

**3 — Claude Code ve výchozím nastavení.** Výchozí systémový prompt Claude Code (dlouhé pokyny k programování a k prostředí), nástroje pro soubory, shell a web, `CLAUDE.md` z pracovního adresáře a všech nadřazených, automatická paměť, skills, pluginy a servery MCP včetně konektorů účtu. To je přesně to, co rozh. 189 zakazuje, a spuštěno v pracovní kopii by `CLAUDE.md` modelu popsal celý nástroj. Zamítáme.

**4 — Claude Code bez nástrojů a bez přizpůsobení, se zachyceným požadavkem.** Přepínače z měření odeberou nástroje, přizpůsobení, servery MCP a výchozí systémový prompt; co zbývá — identita agenta, e-mail účtu a prostředí —, je změřené, nejmenuje úlohu, repozitář ani nástroj a model k tomu nemá čím sáhnout. Ovladač každý požadavek zachytí, uloží k pokusu a pustí pokus dál, jen když požadavek nese prompt a právě tento kontext. Volíme.

**5 — Varianta 4 s `--bare`.** Bez připomínky a prostředí, ale jen klíčem API — tedy za tutéž cenu jako varianta 1 a s horším požadavkem než ona. Zamítáme.

**6 — Složka s vlastním `CLAUDE.md` a podklady pro model** (Míra, 2026-11-03). Pro varianty `zero-shot` a `few-shot` ne: prompt je podle rozh. 189 samostatný a nese všechno, co dostane i nástroj — vstup, pravidla, u `few-shot` příklady —, a cokoli dalšího mimo prompt mění variantu na `rag`. Složka s dokumentací připnutých verzí, schématem, příklady a příkazem, který artefakt odsoudí, je ale přesně to, co chybí variantám `rag` a `agent` (T5, T6), a Claude Code je pro `agent` přirozený kanál. Ty tu nerozhodujeme.

## Rozhodnutí

**Silný model rozh. 203 jde nejdřív kanálem `code`: ovladač spustí na každý pokus Claude Code bez interakce, bez nástrojů a bez přizpůsobení, přihlášený předplatným, v prázdné složce mimo repozitář, s úsilím `high` a stropem výstupu 32768, a pošle mu prompt na standardní vstup. Každé volání jde přes zachytávací proxy ovladače; pokus projde, jen když požadavek nese prompt beze změny a nic než změřený kontext rozhraní, a zachycený požadavek se uloží k pokusu. V kanálu `code` hlášení modelu o kontextu rozhraní (`contextUsed`) stopou hledání není; nástroj a zdroj (`toolsUsed`, `sourcesConsulted`), jméno repozitáře či nástroje a odkaz mimo soubory stopou zůstávají. Běh kanálem `api` podle rozh. 203 zůstává rozhodnutý a přijde později nad týmiž buňkami a prompty.**

### Volání

```
claude -p --output-format stream-json --verbose --model claude-fable-5-1 --effort high
       --tools "" --safe-mode --strict-mcp-config --disable-slash-commands
       --no-session-persistence --system-prompt ""
```

| Co | Hodnota | Proč |
|---|---|---|
| pracovní adresář | prázdná složka mimo repozitář (`workingDirectory` v `code.json`); ani ona, ani žádná nadřazená nenese `CLAUDE.md` ani `.git` | prostředí řekne „není repozitář git" a cesta nejmenuje projekt |
| prostředí procesu | bez proměnných relace Claude Code a bez `ANTHROPIC_*`; `ANTHROPIC_BASE_URL` na proxy ovladače, `CLAUDE_CODE_MAX_OUTPUT_TOKENS` ze stropu, `DISABLE_AUTOUPDATER=1` | běh jde předplatným, ne klíčem; ovladač spuštěný z relace Claude Code ji nezdědí; verze se uprostřed běhu nezmění |
| úsilí | `high` | výchozí hodnota API pro Claude Fable 5.1 (dokumentace k 2026-11-03), tedy totéž, co dostane pozdější běh kanálem `api` bez `effort`; Claude Code by jinak poslal svou výchozí `xhigh` nebo nastavení uživatele |
| strop | 32768 | strop rozh. 203; Claude Code by poslal 64000 |
| teplota | žádná | model ji nepřijímá; řádek prázdný, metadata `default` — druhé nastavení rozh. 175, jako u 203 |

### Zachycený požadavek je kontrolou kontextu

Ovladač pustí každé volání přes vlastní proxy v procesu (Kestrel na `127.0.0.1`, požadavek předá výrobci beze změny a odpověď vrací průběžně) a uloží tělo požadavku k pokusu jako `request.json` bez `metadata`. Pokus projde, jen když:

- je právě jeden `POST /v1/messages`, s modelem, úsilím a stropem z nastavení a s prázdným `tools`;
- `system` nese jen dva změřené bloky — fakturační hlavičku a větu o agentovi;
- zpráva `user` nese prompt beze změny a vedle něj nejvýš připomínku s oddílem `userEmail`; jiný oddíl (`claudeMd`, `gitStatus`, `attachedProject`, paměť) zastaví běh;
- každá zpráva `system` uprostřed konverzace je oddíl `# Environment` a žádný jiný a nenese jméno repozitáře ani nástroje;
- žádná zpráva nemá roli `assistant`;
- úvodní událost hlásí prázdné nástroje, servery MCP, skills a příkazy, model z nastavení a `apiKeySource: none`; závěrečná událost hlásí úspěch v jednom kole.

Cokoli jiného zastaví běh jménem pokusu a odpověď se nezapíše. Změna Claude Code, která by modelu poslala víc, se tak ukáže na prvním pokusu, ne v záznamu.

### Hlášení modelu o kontextu

V kanálu `code` není `contextUsed` stopou hledání. Kontext, který rozhraní dodalo, je změřený, u každého pokusu uložený a pro všechny pokusy týž; deklarace modelu jen opakuje, co požadavek nese, a kontrolou je požadavek. Hlášení jde do metadat beze změny jako u ostatních kanálů. Ostatní vrstvy zákazu rozh. 189 platí: `toolsUsed` a `sourcesConsulted` jsou stopou, jméno repozitáře, nástroje či autorů kdekoli v odpovědi a odkaz mimo bloky souborů také.

### Řádek pokusu

| Pole | Hodnota |
|---|---|
| `channel` | `code` |
| `model`, `version` | z `code.json` (`claude-fable-5-1`, identifikátor bez data je snímek, jak říká rozh. 203) |
| `frontend` | popis z `code.json` a verze Claude Code z úvodní události |
| `temperature` | prázdná; import zapíše `default` |
| `seconds` | hodinami ovladače kolem celého procesu — včetně startu Claude Code, který proti volání API přidá jednotky sekund |
| `tokensIn`, `tokensOut` | vstup včetně zápisu a čtení cache; výstup včetně uvažování |
| `costUsd` | prázdné: předplatné tokeny neúčtuje; cena podle ceníku API jde do `note` |
| `credits`, `creditsUnit` | přírůstek vytížení pětihodinového okna v procentních bodech od předchozího pokusu téhož spuštění, `% of the five-hour window`; u prvního pokusu spuštění prázdné |
| `note` | `stop_reason`, tokeny uvažování, zápis a čtení cache, cena podle ceníku API, vytížení obou oken, model, liší-li se |

Pokusy 2 a 3 buňky čtou prefix požadavku z cache (body cache stojí za promptem), takže jejich `seconds` je kratší než u prvního; `note` to ukáže čtením cache.

### Dávky a okna předplatného

- **Pořadí průchodů.** Ovladač dělá pokusy po průchodech: pokus 1 všech buněk ve variantě `zero-shot`, pak v `few-shot`, pak pokus 2 a 3. Běh přerušený oknem má napřed úplné první průchody obou variant.
- **Dávka.** `ORMCONVERTOR_LLM_BATCH` omezí počet pokusů jednoho spuštění; bez ní ovladač pokračuje do konce nebo do okna.
- **Okno.** Před dalším pokusem ovladač zastaví sám, když vytížení pětihodinového okna dosáhne `stopAtFiveHourUtilization` nebo sedmidenního `stopAtSevenDayUtilization` z `code.json`, a řekne, kdy se okno obnoví. Předplatné sdílí okno s prací v Claude Code a claude.ai, proto hranice nechávají rezervu.
- **Nezdar volání** — vyčerpané okno, chyba API, nenulový kód procesu — zastaví běh jménem pokusu bez odpovědi; pokus se udělá při dalším spuštění, stejně jako u kanálu `api`.

### Místo pro běh kanálem `api`

Rozhodnutí 203 platí celé. Běh kanálem `api` půjde nad týmž `cells.tsv` a týmiž prompty (týž `promptSha256`), ve vlastním adresáři běhu s `api.json` podle 203, s úsilím výchozím, tedy `high`, a se stropem 32768 — obojí totéž jako tady. Souhrn experimentálního běhu staví skupiny po modelu, kanálu a variantě, takže řádky `llm · claude-fable-5-1 · code · …` a `llm · claude-fable-5-1 · api · …` stanou vedle sebe a druhý běh je zároveň měřením toho, co kanál `code` přidává při témže modelu. Kdy, rozhodne Míra; položka ho vede.

### Co se netvrdí

Kanál `code` není holé API. Model ví, že běží jako agent Claude Agent SDK, zná e-mail operátora, jméno uživatele Windows z cesty, platformu, datum a hranici svých znalostí; nic z toho nejmenuje úlohu, repozitář ani nástroj a nástroj, kterým by to využil, nemá. Je to přesto kontext, který kanál `api` nemá, a záznam ho vyjmenuje — bez samotné adresy. Že žádný ze tří kanálů srovnání nezkreslí, netvrdíme; tvrdíme, co který kanál modelu pošle. T4–T6 zůstávají vyňaté (§9, oblast 6).

## Důsledky

- **Kód:** `Tests/Foreign/CodeAttempt.cs` — nastavení `code.json` (`CodeSettings`), ovladač (`CodeAttempts.Make` v pořadí průchodů, s dávkou a okny), kontrola požadavku a událostí (`CodeRequest`), zachytávací proxy (`CaptureProxy`) a spouštění procesu (`ClaudeCodeRunner`); `CodeAttemptTest` s ovladačem nad zástupným procesem, s proxy nad zástupným výrobcem, s během na vyžádání `TheAttemptsAreMadeWhereAsked` a s měřením na vyžádání `WhatReachesTheModelIsThePromptAndTheMeasuredContext`, které jedním voláním ověří, že rozhraní posílá právě změřený kontext. `WebAttempts` zná kanál `code` (povinný kanál importu, `default` u teploty), `WebTrace` v něm nečte `contextUsed`.
- **`ORMConvertor/README.md`**, *A language model through Claude Code*: `code.json`, příkaz, dávka a okna, co se kontroluje a co model dostane.
- **[`architecture.md`](../architecture.md) §6.2:** kanál `code` vedle webového a `api`, řádek ovladače.
- **[`runs/README.md`](../runs/README.md):** záznam běhu kanálem `code` uvede verzi Claude Code, přepínače, změřený kontext mimo prompt, úsilí, strop, vytížení oken a součet ceny podle ceníku API.
- **[`open-items.md`](../open-items.md):** položka vede běh kanálem `code` jako další krok a běh kanálem `api` jako pozdější.
