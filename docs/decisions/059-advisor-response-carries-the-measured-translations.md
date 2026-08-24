# 059 — Odpověď Advisoru nese změřené překlady

Datum: 2026-08-24
Stav: platí
Požadavky: F15, S2, S7, T7

## Kontext

Běh Advisoru (`POST /advisor/run`) má tři fáze: překlad každého dotazu do každého kandidátského frameworku (`AdvisorRunCoordinator.BuildTranslations`, Q×F volání `ConversionHandler.Convert`), benchmark každého přeloženého páru a ILP řešení nad naměřenými náklady. Artefakty první fáze se po benchmarku zahodí — odpověď nese jen výběr frameworků, přiřazení dotazů a měření.

Obrazovka Advisoru ale artefakty ukázat chce, a tak si je po doběhnutí běhu odvozuje znovu: `advisor.js` vydá jedno `POST /convert` za každý vybraný framework, jen aby měla co vykreslit. To má dva důsledky, jeden nákladový a jeden věcný.

Nákladový: server tytéž převody právě spočítal a zahodil; prohlížeč je nechá spočítat znovu, a navíc jinou cestou — `/convert` jde přes připojení `CatalogDatabase`, takže každý vybraný framework kromě Dapperu stojí další dvě spojení a pět až devět katalogových dotazů (§5.2 architektury).

Věcný, a ten je vážnější: **obrazovka ukazuje jiný kód, než jaký čísla změřila.** Překlady první fáze běží bez připojovacího řetězce, tedy bez doplnění z katalogu (`CatalogState = NotConfigured`); benchmark kompiluje a měří právě je. Dodatečné `/convert` naproti tomu katalogem projde, takže vykreslené artefakty nesou doplněné fakty, které měřený kód neměl. Vedle tabulky měření tak stojí artefakty, o kterých ta tabulka nic netvrdí — a čtenář nemá jak to poznat.

Nalezeno při analýze databázových přístupů 2026-08-24 (podkladová mapa, položka R6).

## Zvažované varianty

1. **Nechat dodatečné `/convert` a stav jen popsat.** Nejlevnější, ale neřeší ani jeden z obou důsledků: redundantní práce zůstává a rozpor mezi měřeným a zobrazeným kódem se nanejvýš přizná. Přiznaná nepoctivost je pořád nepoctivost. Zamítáme.

2. **Odpověď běhu nese artefakty první fáze a obrazovka vykresluje je.** Zobrazené artefakty jsou pak doslova ty, které se kompilovaly a měřily — tvrzení obrazovky a tvrzení čísel se kryjí. Redundantní převody i katalogové dotazy odpadají celé. Cenou je větší odpověď a to, že uživatel u ne-Dapper cílů uvidí překlad bez katalogového doplnění; na úplný, doplněný překlad má dál `/convert`, což je i dělba, která odpovídá účelu obou koncových bodů.

3. **Doplňovat z katalogu už první fázi a nést její artefakty.** Sjednotilo by to měřený kód s tím, co uživatel z `/convert` skutečně dostane — argument, který bereme vážně. Jenže to mění, *co* Advisor měří: doplněné entity nesou jiné atributy a vztahy, takže se mění kompilovaný harness i naměřená čísla. To je změna metodologie měření (T7) uvnitř oblasti bez jediného testu, a zaslouží si vlastní rozhodnutí s vlastním ověřením, ne vedlejší efekt změny kontraktu. Teď zamítáme; otázka je zapsaná v `open-items.md`.

## Rozhodnutí

**Volíme variantu 2. `AdvisorRunResult` dostává pole `translations`: framework → entity artefakty jednou a artefakty každého dotazu pod jeho identifikátorem. Obrazovka Advisoru skládá panely artefaktů z odpovědi a dodatečná volání `/convert` ruší.**

Tvar je klíčovaný stejně jako `measurements` (frameworky všech kandidátů, ne jen vybraných — srovnání je souměrné), a entity artefakty se nesou **jednou za framework**, ne u každého dotazu: každý převod téže sady entit do téhož cíle vydá tytéž entitní artefakty, což není doufání, ale determinismus podle S2 — týž vstup, táž verze, týž artefakt. Dotazové artefakty se od entitních rozlišují typem obsahu (`IsQuery()`), tedy toutéž kategorií, kterou pracuje celá pipeline.

Přidání pole do odpovědi je podle rozhodnutí [041](041-versioning-and-release.md) změna MINOR: schopnost přibývá, dosavadní pole se nemění.

## Důsledky

- **Zobrazené artefakty jsou měřené artefakty.** Obrazovka nově ukazuje překlad bez katalogového doplnění — přesně ten, který se kompiloval a benchmarkoval. Kdo chce překlad doplněný katalogem, použije překladovou obrazovku, respektive `/convert`.
- Odpověď `/advisor/run` je větší o vygenerovaný kód; při Q dotazech a F frameworcích jde o F sad entitních artefaktů a Q×F dotazových, bez duplikace entit po dotazech.
- `advisor.js` ztrácí import `convert` a poběhu nevydává žádné další požadavky; `openapi.json` se obnovuje z běžící instance (§6.5).
- Otázka varianty 3 — má-li Advisor měřit katalogem doplněný překlad — zůstává otevřená v `open-items.md` a rozpor, který by řešila, je nově aspoň vidět: artefakty v odpovědi svým stavem říkají, že katalog nebyl použit.
