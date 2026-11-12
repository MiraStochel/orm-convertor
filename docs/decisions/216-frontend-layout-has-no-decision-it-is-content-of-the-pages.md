# 216 — Rozvržení stránek frontendu nemá rozhodnutí: šířky, okraje a rozmístění prvků jsou obsahem stránek

Datum: 2026-11-12
Stav: platí; nahrazuje rozvržení stránek rozh. 137
Požadavky: S7
Podklad: rozhodnutí [137](137-text-runs-the-full-width-and-controls-keep-a-fixed-width.md), jehož část tímto nahrazujeme, [214](214-the-tutorial-has-no-decision-its-shape-and-content-are-content-of-the-page.md), [099](099-examples-are-content-not-a-choice.md) a [032](032-frontend-as-static-pages-without-a-build.md); [`architecture.md`](../architecture.md) §6.3

## Kontext

Rozhodnutí 137 zapsalo, jak jsou stránky frontendu rozvržené: která stránka zabírá celé okno a která stojí v kontejneru, jak široký je text, jak široké jsou výběry a pole a kde stojí šipka složeného oddílu. Když se šířka stránek sjednotila — všechny stojí v kontejneru s okraji po stranách —, muselo by se rozhodnutí o rozvržení nahradit novým, a stejně by dopadla každá další úprava vzhledu: okraj, šířka pole, velikost písma.

## Zvažované varianty

(a) **Nahrazovat rozhodnutí o rozvržení po částech s každou změnou vzhledu.** Drží pravidlo, že změněné rozhodnutí dostane nový soubor, ale plní rejstřík soubory, které nezaznamenávají volbu o nástroji: čtenář, který hledá, proč nástroj překládá tak, jak překládá, by mezi nimi listoval šířkami stránek.

(b) **Rozvržení stránek je obsah.** Šířka stránky, okraje, šířky ovládacích prvků a rozmístění prvků jsou totéž co podoba výukové stránky, kterou rozhodnutí 214 prohlásilo za obsah, a co výběr příkladů podle rozhodnutí 099: mění se s tím, jak se stránky zkoušejí na čtenářích, nic na nich nezávisí — REST kontrakt, překladač ani nárok S7 se rozvržením nemění — a na otázku „proč takhle" odpovídá stránka sama. Současný stav popisuje `architecture.md` §6.3, změny nese historie repozitáře.

Volíme (b).

## Rozhodnutí

> **Rozvržení stránek frontendu není důležité rozhodnutí a soubor v `decisions/` nemá. Šířka stránek, okraje, šířky textu a ovládacích prvků a rozmístění prvků jsou obsahem stránek: mění se bez rozhodnutí, současný stav popisuje `architecture.md` §6.3. Z rozhodnutí 137 se tím nahrazuje vše, co určuje rozvržení.**

## Důsledky

**Z rozhodnutí 137 přestává vázat** výběr stránek, které zabírají celé okno, šířka textu, tři šířky ovládacích prvků a poloha šipky složeného oddílu. To, co 137 výčtem převzalo ze starších rozhodnutí o chování obrazovek — příklad jako volitelný první krok, směr jako jeden řádek, vstup vedle výstupu s párováním výrokem serveru, kniha tvrzení —, platí dál podle něj.

**Co frontend váže dál, jsou rozhodnutí o něm jako celku:** statické stránky bez sestavení (rozh. 032) a to, které obrazovky existují a co umějí. Jak vypadají, je věc `app.css`.

**Rozhodnutí 137 zůstává čitelné** s tehdejší úvahou o šířkách; jako závazek o rozvržení se necituje.
