# 154 — Mezireprezentaci uživateli nezobrazujeme: je pro něj zbytečná a ruší; co z ní potřebuje, říká kniha tvrzení a záznamy

Datum: 2026-10-19
Stav: platí
Požadavky: F14, F11, T3
Podklad: zadání ([`requirements.md`](../requirements.md), F14), [specifikace](../specifikace.tex) (F14, milník 4, obrázek překladové obrazovky), [`use-cases.md`](../use-cases.md); rozhodnutí [010](010-diagnostics-as-returned-data.md), [033](033-shape-of-the-static-frontend-screens.md), [066](066-records-attributed-to-the-input-unit.md), [098](098-the-number-is-decided-once-per-release.md), [124](124-direction-as-one-row-and-input-beside-output-paired-per-fact.md), [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md), [137](137-text-runs-the-full-width-and-controls-keep-a-fixed-width.md)

## Kontext

F14 zadání žádá, aby rozhraní zobrazilo „vstup, IR, výstup a diagnostiku po jednotlivých souborech". Schválená specifikace to opakuje ve znění F14, jmenuje „zobrazení mezireprezentace" mezi úkoly milníku 4 a v obrázku překladové obrazovky ji kreslí v kroku 5 vedle artefaktů a záznamů. Schválený [záměr](../zamer.tex) zobrazení nejmenuje.

`/convert` mezireprezentaci dnes nevrací. Obrazovka překladu ukazuje vstup vedle výstupu se spárováním po faktech (124), knihu tvrzení — u každého mapovacího faktu entitu, vlastnost, kategorii a hodnotu, původ (`Source` s jednotkou, `Catalog`, `Derived`), osud (`Carried`, `Dropped`, `NotGenerated`) a pozici ve vstupu i ve výstupu (124, 125, 137) —, záznamy o ztrátách, konvencích, konfliktech, neúplnostech, selháních a doplněních přiřazené jednotce (010, 066) a záznam běhu. §9 zobrazení mezireprezentace nenárokuje, kořenové README ho vedlo mezi prací za touto verzí a otevřená položka se ptala, zda bude plnohodnotnou částí odpovědi, nebo výslovně nestabilním náhledem.

Mezireprezentace je vnitřní slovník nástroje: `EntityMap` se seřazenými částmi klíče a strategií generování každé části, vztahy na entitě s rolí `Owning`/`Inverse` a páry sloupců, vlastnosti s jazykovým a databázovým typem, dotazové instrukce se stromem podmínek. Záměrně nevaliduje — připouští kombinace, které by skutečná databáze odmítla, protože validace patří builderu cíle.

## Zvažované varianty

1. **Mezireprezentace jako plnohodnotná část odpovědi `/convert` a třetí pás obrazovky** mezi vstupem a výstupem — čtení specifikace.
2. **Mezireprezentace jako výslovně nestabilní náhled** — ladicí výpis, který kontrakt verzí neváže.
3. **Mezireprezentace se nezobrazuje;** uživateli slouží kniha tvrzení a záznamy.

## Rozhodnutí

**Varianta 3. Mezireprezentaci uživateli nezobrazujeme, ani v dalších verzích.** Pro uživatele je zbytečná a ruší ho.

**Zbytečná: žádný aktér ji nepotřebuje.** Migrující vývojář potřebuje věrný překlad a jmenovitý seznam toho, co neprošlo; architekt před volbou srovnání z měření; autor experimentu dávku přes API, opakovatelnost a záznam běhu; konzumentský projekt artefakt, který o něm nic netvrdí ([`use-cases.md`](../use-cases.md)). Žádný ze scénářů UC1–UC7 nemá krok, ve kterém by uživatel mezireprezentaci četl. Požadavek bez scénáře je podle případů užití nález sám o sobě, a tahle část F14 je přesně takový nález.

**Zbytečná: co z ní uživatel potřebuje, už dostává ve svém jazyce.** Otázka, kterou si uživatel nad překladem klade, zní: co nástroj z mého kódu pochopil a co z toho se dostalo do výstupu. Na ni odpovídá kniha tvrzení — fakt po faktu, s původem, osudem a pozicí ve vstupu i ve výstupu — a záznamy, které jmenují, co se ztratilo, co nástroj předpokládal, co doplnil katalog a kde si zdroje odporují. To je obsah mezireprezentace zúžený na tu otázku a vyslovený slovy vstupu a výstupu. Vlastní tvar mezireprezentace by k tomu přidal jen vnitřní slovník nástroje.

**Ruší: třetí jazyk mezi dvěma, které uživatel zná.** Obrazovka staví vstup vedle výstupu a páruje je po faktech (124). Pás mezireprezentace mezi nimi by uživatele nutil naučit se jazyk, který nikdy nepsal — `EntityMap`, části klíče, role `Owning` a `Inverse`, instrukce a strom podmínek —, aby tytéž fakty přečetl potřetí. Z jednoho překladu by se v jeho hlavě staly dva.

**Ruší: ukazuje stavy, které vypadají jako chyby a chybami nejsou.** Mezireprezentace nevaliduje a připouští kombinace, které databáze odmítne, protože o nich rozhoduje až builder cíle. Uživatel by v ní viděl „vadný" stav, který žádným artefaktem neprojde, a záznamy by musely vysvětlovat vedle překladu i mezireprezentaci.

**Kontrakt by mezireprezentaci zmrazil, nebo by se s ní měnil.** Každý serializovaný tvar se stane součástí REST kontraktu a verzování podle rozh. 098. Mezireprezentace se měnila s většinou rozhodnutí o dotazech (např. výraz jako šestý tvar operandu, rozh. [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), mezivýsledek jmenovaný jednou za dotaz, rozh. [112](112-a-query-as-a-row-source-is-a-named-intermediate-result.md)). Varianta 1 by ji buď zmrazila, nebo udělala z každé takové změny změnu rozhraní. Varianta 2 tomu neunikne: náhled, který čtou skripty experimentů, je kontrakt bez ohledu na nálepku.

**Experimenty ji nepotřebují.** Úplnost přenesených mapovacích vlastností (T3) se počítá z knihy tvrzení — z osudu každého faktu —, ne z mezireprezentace.

**Odchylka od zadání a specifikace.** Rozhodnutí se odchyluje od F14 zadání i od schválené specifikace (F14, milník 4, obrázek obrazovky). Nebereme ho jako mezeru, která se zavře později, ale jako zúžení odůvodněné tady; schválený záměr zobrazení nežádá a text práce ho vysloví s těmito důvody. Mezireprezentaci ukazujeme čtenáři práce — v obrázcích a příkladech textu, kde vysvětluje přístup —, ne uživateli nástroje.

## Důsledky

**`/convert` zůstává bez mezireprezentace; kód se nemění.**

**Zúžení F14 je trvalé.** §9, [`traceability.md`](../traceability.md) a [`subset.md`](../subset.md) říkají, že se zobrazení mezireprezentace nenárokuje podle tohoto rozhodnutí; sekce *Guarantees* kořenového README totéž a mezi prací za touto verzí ho nevede. [`use-cases.md`](../use-cases.md) ho jmenuje mezi tím, co nástroj nedělá.

**Milník 4 specifikace se v téhle části uzavírá zúžením.** [`open-items.md`](../open-items.md) položku maže a v tabulce milníků u ní stojí odkaz sem místo „chybí".
