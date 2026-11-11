# 214 — Výuková stránka nemá rozhodnutí: její podoba a obsah jsou obsahem stránky

Datum: 2026-11-11
Stav: platí; nahrazuje rozh. 213
Požadavky: S7
Podklad: rozhodnutí [213](213-the-tutorial-is-a-page-of-its-own-that-walks-one-live-conversion-through-the-steps-of-the-translation-screen.md), [099](099-examples-are-content-not-a-choice.md), [032](032-frontend-as-static-pages-without-a-build.md) a [056](056-work-in-progress-input-stays-in-the-browser.md); [`architecture.md`](../architecture.md) §6.3

## Kontext

Rozhodnutí 213 zavedlo výukovou stránku `tutorial.html` a spolu s její existencí zapsalo i to, jak vypadá: kroky viditelné najednou, lepivý pruh s krokem podle polohy rolování, který příklad stránka vede, co který krok říká. Den nato se podoba změnila — stránka ukazuje jeden krok a pruh kroků je její navigace — a rozhodnutí o rozvržení stránky by se muselo nahradit novým. Stejně by dopadla každá další úprava: velikost písma, pořadí kroků, výměna příkladu.

## Zvažované varianty

(a) **Nahrazovat rozhodnutí 213 po částech s každou změnou podoby.** Drží pravidlo, že změněné rozhodnutí dostane nový soubor, ale plní rejstřík soubory, které nezaznamenávají volbu o nástroji: čtenář, který hledá, proč nástroj překládá tak, jak překládá, by mezi nimi listoval rozvržením jedné stránky nápovědy.

(b) **Výuková stránka je obsah.** Co stránka ukazuje a jak je uspořádaná, je totéž co výběr příkladů výkladové stránky, který rozhodnutí 099 prohlásilo za obsah, ne za volbu: mění se s tím, jak se stránka zkouší na čtenářích, nic na ní nezávisí — REST kontrakt, překladač ani nárok S7 se jejím rozvržením nemění — a na otázku „proč takhle" odpovídá stránka sama. Současný stav popisuje `architecture.md` §6.3, změny nese historie repozitáře.

Volíme (b).

## Rozhodnutí

> **Výuková stránka `tutorial.html` nemá rozhodnutí. Její podoba, text, kroky a příklad jsou obsahem stránky: mění se bez souboru v `decisions/`, současný stav popisuje `architecture.md` §6.3. Rozhodnutí 213 se tím nahrazuje celé.**

## Důsledky

**Co stránku váže dál, jsou rozhodnutí o frontendu jako celku**, ne o ní: statické stránky bez sestavení (rozh. 032), rozpracovaný vstup překladače, do kterého jiná stránka nesahá (rozh. 056), a příklady jako výrok serveru (rozh. 099).

**Parametr `?example=<klíč>` překladače zůstává**, jak ho popisuje řádek `translation.html` v `architecture.md` §6.3: příklad v seznamu jen předvybere, načtení zůstává na uživateli.

**Rozhodnutí 213 zůstává čitelné** s tehdejší úvahou o tom, proč je výuka samostatná stránka a proč vede živý převod; jako závazek se necituje.
