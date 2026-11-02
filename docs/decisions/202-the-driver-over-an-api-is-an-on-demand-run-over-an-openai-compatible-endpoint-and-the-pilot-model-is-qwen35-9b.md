# 202 — Ovladač nad API je běh sady na vyžádání nad koncovým bodem kompatibilním s OpenAI, nastavený souborem v adresáři běhu; kanál `api` je třetí vedle konzole a chatu; pilotním modelem je Qwen3.5-9B v kvantizaci Q4_K_M, spouštěný lokálně v llama.cpp a připnutý otiskem souboru a digestem obrazu

Datum: 2026-11-01
Stav: platí
Požadavky: T4, T5, T6, T3, S2
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu; uvádět přesný model, verzi, prompt, teplotu, počet pokusů, tokeny, čas a náklady"); schválená [specifikace](../specifikace.tex) (T4–T6: projekt připraví data, rozhraní a měřicí infrastrukturu); [záměr](../zamer.tex) (*Využití pro výzkumnou činnost*: porovnání s metodami založenými na velkých jazykových modelech); rozhodnutí [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [186](186-the-build-environment-is-pinned-on-content-lock-files-image-digests-and-action-commits-moved-by-dependabot.md) a [189](189-a-web-frontend-attempt-is-a-new-conversation-with-one-self-contained-prompt-answered-in-text-without-the-web-and-paired-with-the-tool.md); kontrola z 2026-11-01 na MIS3 (níž, *Kontext*); otevřená položka *Srovnání s jazykovým modelem nemá model, prompty ani záznam*

## Kontext

**Protokol a kanál webového rozhraní stojí, model a ovladač ne.** Rozhodnutí 175 určilo, co je jedno měření modelu a která pole záznam nese; rozhodnutí 189 dalo kanál, kterým obsluha ručně prochází vzorek buněk ve webovém rozhraní, a jako první variantu kanálu jmenovalo *ovladač nad API výrobce* — program, který čte prompt, volá model a z odpovědi píše artefakt i metadata ze spotřeby —, nezamítlo ho a nechalo ho navazující práci spolu s volbou modelu, protože placená volání projekt nemá krytá a pro ručně procházený vzorek konzole stačí. Otevřená položka od té doby žádá model a jeho verzi, rozpočet a rozsah.

**Oba kanály rozhodnutí 189 měří model někoho jiného.** Konzole i aplikace chatu dávají model výrobce za jeho cenu, s jeho systémovým promptem, s nástroji, které obsluha musí vypínat, a s verzí, kterou výrobce kdykoli stáhne; čtenář práce pokus zopakuje, jen dokud výrobce model nabízí. Tokeny dává jen konzole, čas v chatu měří rozhraní, ne model (rozh. 189).

**Lokálně spouštěný model s otevřenými váhami to obrací.** Soubor vah je ke stažení pod licencí, která to dovoluje, otisk souboru ho určuje navždy, server běží v připnutém obrazu bez nástrojů, bez webu a bez paměti, teplota se nastaví, tokeny se počítají přesně a cena volání je nula. Všechny čtyři vrstvy zákazu přístupu k webu rozhodnutí 189 jsou splněné stavbou, ne kázní obsluhy. Co takový model nedává, je síla: na stroji se 16 GB paměti a bez grafické karty běží nejvýš model o ~9 miliardách parametrů ve 4bitové kvantizaci, a to není „silný LLM", který T4 jmenuje.

**Kontrola z 2026-11-01** na MIS3 (Intel Core i7-1065G7, 4 jádra, 16 GB; virtuální stroj Dockeru 12 GB a 4 procesory): obraz `ghcr.io/ggml-org/llama.cpp:server` (`sha256:e67d88af…`, sestavení `b12030-a91eab67d`) se souborem `Qwen3.5-9B-Q4_K_M.gguf` (5 680 522 464 B, SHA-256 `03b74727…`, shodný s otiskem zveřejněným v `unsloth/Qwen3.5-9B-GGUF`) se načetl za 62 s do 6,1 GiB. Prompt šablony `web-zero-shot-1` buňky `matrix:AggregationGroupingAndHaving` z Dapperu do EF Core (6 657 znaků) měl 1 697 tokenů a zpracoval se za 127,8 s (13,3 tok/s); odpověď 1 281 tokenů vznikala 471,0 s (2,7 tok/s); celý pokus ~10 minut, uvažování vypnuté (`reasoning_content` chybí), odpověď v žádaném tvaru `FILE:` a `REPORT:`. Obsahem byla špatně — dotaz projektoval do entity místo do projekce a model o sobě napsal, že je GPT-4 —, což je výsledek, který protokol měří, ne vada kanálu. Z rodiny Qwen3.5 (Apache 2.0) se do 12 GB vejdou jen modely 0,8B–9B; 35B-A3B potřebuje ~22 GB, 27B a větší víc; novější otevřené modely Qwen (3.6: 35B-A3B, 27B; 3.8: 27B) jsou všechny větší a Qwen3.7 váhy nemá. Výrobce u Qwen3.5 neuvádí hranici trénovacích dat.

**Je třeba rozhodnout** kanál pro lokální model a kde ovladač stojí, jak se nastavuje, co píše do polí záznamu, který model a jak se připíná, a s jakým nastavením model běží.

## Zvažované varianty

### Kanál a ovladač

**1 — Grafické rozhraní lokálního serveru** (LM Studio, Open WebUI) jako další „aplikace chatu" rozhodnutí 189. Zamítáme: obsluha by znovu kopírovala rukou, tokeny a čas by měřilo rozhraní, a nic z toho by neumělo celou populaci.

**2 — Skript vedle sady** (`curl` a `jq`). Rychle napsaný, na jiném stroji jinak. Zamítáme: na MIS3 není `jq` ani `bc`, skript by nesl formát tabulky pokusů podruhé, a za chybu v něm by sada neručila; co vyrobí, nemá test.

**3 — Běh sady na vyžádání v `Tests/Foreign/` vedle generátoru a importéru**, který volá koncový bod kompatibilní s OpenAI (`/v1/chat/completions`). Volíme, níž. Jeden mechanismus pokryje lokální server (llama.cpp, vLLM, Ollama) i API výrobců, která tento tvar přijímají; prompt a importér rozhodnutí 189 slouží beze změny, protože odpověď API je text v témž tvaru.

### Nastavení ovladače

**1 — Proměnné prostředí** jako u výpisu a běhu cizích artefaktů. Osm proměnných na jeden běh; záznam by je musel opisovat. Zamítáme.

**2 — Soubor `api.json` v adresáři běhu**, vedle `cells.tsv`. Volíme: adresář běhu už je jediné místo, kde běh žije (rozh. 189), soubor je zároveň záznamem nastavení, a klíč API v něm nikdy není — soubor jen jmenuje proměnnou prostředí, která ho drží.

### Model

**1 — Silný model výrobce přes API** hned. Nezamítáme ho — je to model, který T4 míní, a ovladač ho volá týmž mechanismem —, ale ne jako první: potřebuje klíč a rozpočet, které projekt nemá, a první průchod kanálem má ukázat kanál, ne utratit rozpočet na jeho chybách.

**2 — Větší otevřený model** (Qwen3.5-27B, 35B-A3B, Qwen3.8-27B). Zamítáme pro oba stroje: nevejdou se do 16 GB, natož do 12 GB virtuálního stroje.

**3 — Qwen3.5-9B, Q4_K_M, lokálně v llama.cpp.** Volíme, níž. Nejsilnější otevřený model, který se vejde; měřený průchod ~10 minut na pokus je noc na desítky buněk. Qwen3.5-4B je stejnými příkazy s jiným jménem souboru náhrada, kdyby se 9B ukázal na vybraném vzorku příliš pomalý; nevolíme ho předem, protože měření to nežádá.

## Rozhodnutí

**Volíme variantu 3 kanálu, soubor nastavení a model Qwen3.5-9B. Ovladač je běh sady na vyžádání (`ApiAttemptTest.TheAttemptsAreMadeWhereAsked`) nad adresářem běhu `ORMCONVERTOR_LLM_RUN`, který má vedle `cells.tsv` soubor `api.json`; bez něj případ přeskočí, protože běh je kanálem webového rozhraní. Pro každý pokus bez odpovědi pošle prompt jako jedinou zprávu uživatele — bez systémového promptu a bez nástrojů — na `<url>/chat/completions`, obsah odpovědi zapíše jako `reply.md` a řádek tabulky pokusů vyplní z nastavení, z odpovědi a z vlastních hodin. Kanál pokusu je `api`, třetí hodnota vedle `console` a `chat`, a importér ho přijímá; nic dalšího v cestě, sadách ani souhrnu se nemění. Pilotním modelem srovnání je `Qwen3.5-9B-Q4_K_M.gguf` v serveru llama.cpp z obrazu `ghcr.io/ggml-org/llama.cpp:server`, připnutý otiskem SHA-256 souboru a digestem obrazu, s uvažováním vypnutým, teplotou 0 a pevným semínkem.**

### Ovladač

| Co | Jak |
|---|---|
| vstup | `attempts.tsv` a `attempts/<id>/prompt.md` od generátoru (rozh. 189); pokus s `reply.md` se znovu neposílá, pokus, jehož odpověď import odmítl a odsunul do `refused-<n>.md`, se na dalším spuštění pošle znovu |
| požadavek | `model`, jedna zpráva `user` s promptem beze změny, `temperature`, `max_tokens`, `seed` (je-li v nastavení) a pole `extra` nastavení vložená tak, jak jsou — u llama.cpp `cache_prompt: false`, aby druhý pokus téže buňky nedostal prompt z cache a jeho čas byl čas celého pokusu |
| odpověď | `choices[0].message.content` → `reply.md` s LF; `reasoning_content`, má-li ho model, do `reply.md` nejde — je skryté uvažování, které rozh. 189 počítá do `tokensOut` —, jeho délka jde do poznámky |
| řádek | `model`, `version`, `frontend` z nastavení; `channel` = `api`; `temperature` z nastavení; `seconds` hodinami ovladače kolem celého volání; `tokensIn`, `tokensOut` z `usage` koncového bodu; `costUsd` = tokeny × cena za milion tokenů z nastavení (0 u lokálního modelu); `credits` prázdné; `note` = `finish_reason`, časy serveru (u llama.cpp `timings`: tokeny, sekundy a tok/s promptu a generování) a jméno modelu, jak ho uvedl koncový bod, liší-li se |
| průběh | pokusy po jednom v pořadí identifikátorů; tabulka se zapíše po každém, takže běh ukončený zvenčí pokračuje, kde byl |
| nezdar | odpověď bez `choices[0].message`, stav jiný než 2xx nebo nenastavená proměnná klíče zastaví běh jménem pokusu — to je nezdar kanálu, ne výsledek modelu; odpověď bez souboru nebo mimo tvar je výsledkem modelu a import z ní udělá řádek na stupni 0 jako u obsluhy |

Deterministický model při teplotě 0 dá na tři pokusy téže buňky tytéž bajty; to je výsledek protokolu — rozptyl nula —, ne důvod pokusy krátit, a odpověď odmítnutá importérem pro stopu hledání u něj zůstane odmítnutá i po opakování a záznam ji počítá po modelu (rozh. 189).

### `api.json`

| Pole | Význam | U pilotního modelu |
|---|---|---|
| `url` | základ koncového bodu kompatibilního s OpenAI, končí `/v1` | `http://localhost:8080/v1` |
| `model` | jméno modelu, jak ho koncový bod přijímá; jde do pole `model` záznamu | `Qwen3.5-9B-Q4_K_M` |
| `version` | co záznam uvede jako verzi: otisk souboru vah u lokálního modelu, snapshot u hostovaného | `sha256:03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8` |
| `frontend` | server a jeho sestavení, u obrazu i digest | `llama.cpp server b12030-a91eab67d, ghcr.io/ggml-org/llama.cpp@sha256:e67d88af487390098c76d7982b1761484339fa147b7e41d3c64ae1318759f78b` |
| `temperature` | hlavní nastavení rozh. 175 je 0; výchozí teplota modelu je druhé nastavení a jde do jiného běhu | `0` |
| `maxTokens` | strop výstupu | `8192` |
| `seed` | semínko, je-li koncový bod přijímá | `1` |
| `usdPerMillionTokensIn`, `usdPerMillionTokensOut` | cena za milion tokenů k datu pokusu podle ceníku výrobce; `0` u lokálního modelu — fakt, ne odhad | `0`, `0` |
| `apiKeyVariable` | jméno proměnné prostředí s klíčem; klíč sám v souboru nikdy nestojí | chybí |
| `extra` | pole požadavku navíc, beze změny | `{ "cache_prompt": false }` |

### Model a jeho nastavení

| Co | Hodnota | Proč |
|---|---|---|
| váhy | `Qwen/Qwen3.5-9B` (Apache 2.0), kvantizace `Qwen3.5-9B-Q4_K_M.gguf` z `unsloth/Qwen3.5-9B-GGUF` | nejsilnější otevřený model do 12 GB; kvantizace je převod třetí strany, proto model určuje otisk souboru, a z oficiálních vah se dá připnutou verzí llama.cpp převést znovu |
| server | `ghcr.io/ggml-org/llama.cpp:server` digestem (rozh. 186) | obraz bez nástrojů, webu a paměti; CPU, protože stroj nemá grafickou kartu |
| spuštění | `-c 16384 -t 4 --load-mode none --temp 0 --seed 1 --reasoning off` | kontext 16 k stačí na prompt (~2 k tokenů) i odpověď; `--load-mode none` načte soubor celý do paměti místo mapování přes připojený svazek Windows; uvažování vypnuté výslovně, protože karta modelu a vydavatel kvantizace uvádějí opačný výchozí stav |
| hranice trénovacích dat | výrobce neuvádí | záznam napíše „neuvedeno"; mezera záznamu podle rozh. 175, ne selhání |

### Co se netvrdí

Pilotní model není silný LLM T4; je dolní mezí a první průchodem kanálu, kterým pak týmž ovladačem a týmiž prompty projde model výrobce, až bude klíč a rozpočet. Rozhodnutí nevolí vzorek buněk ani nezapisuje běh — vzorek a jeho důvod jsou věcí záznamu (rozh. 189), rozsah navazující práce. Varianty `rag`, `repair` a `agent` ovladač zatím nenese: potřebují korpus, šablonu kola a nástroje protokolu, které stojí v otevřené položce. T4–T6 zůstávají vyňaté (§9, oblast 6).

## Důsledky

- **Kód:** `Tests/Foreign/ApiAttempt.cs` (`ApiSettings`, `ApiReply`, `ApiAttempts.Make`), `ApiAttemptTest` (běh na vyžádání a případy nad náhradou koncového bodu); `WebAttempts.Channels` přijímá `api`.
- **[`open-items.md`](../open-items.md):** položka *Srovnání s jazykovým modelem nemá model, prompty ani záznam* se zužuje: model a kanál stojí, chybí vzorek, záznam a varianty s nástroji.
- **[`architecture.md`](../architecture.md) §6.2:** kanál `api` vedle webového, tabulka částí o řádek ovladače.
- **`ORMConvertor/README.md`**, *A language model over an API*: příkaz serveru, `api.json`, kroky běhu.
- **[`traceability.md`](../traceability.md), T4:** ovladač ve sloupci implementace; stav `vyňaté` zůstává.
- **[`runs/README.md`](../runs/README.md):** druh běhu *srovnání s jazykovým modelem* nese i kanál `api`; záznam uvádí otisk souboru, digest obrazu a nastavení serveru.
- **Cena.** Pokus pilotního modelu stojí ~10 minut strojového času na MIS3 a nic jiného; vzorek deseti buněk po třech pokusech je jedna noc.
