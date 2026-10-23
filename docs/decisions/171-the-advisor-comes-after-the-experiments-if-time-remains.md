# 171 — Advisor není podmínkou odevzdání a přijde na řadu až po experimentech, zbude-li čas

Datum: 2026-10-23
Stav: platí
Požadavky: F15, T1–T7, S4
Podklad: schválený [záměr](../zamer.tex) (*Úkoly řešitele*, *Harmonogram*, *Využití pro výzkumnou činnost*); schválená [specifikace](../specifikace.tex) (F15; kap. *Experimentální požadavky* s T1–T7; *Vztah k dalším projektům*, odstavec o výzkumném přínosu; *Zbývající práce a postup*; *Vyhodnocení*; *Rizika*, odstavce *Metodologie měření pro Javu* a *Pracnost a čas*; *Milníky / Výstupy*; *Harmonogram*); zadání ([`requirements.md`](../requirements.md), F15, T7); rozhodnutí [018](018-work-order-as-item-marker.md), [098](098-the-number-is-decided-once-per-release.md), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md), [147](147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md) a [160](160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md); JSS článek, sekce 8 (výběr frameworku modelem ILP nad měřením); [`architecture.md`](../architecture.md) §8 a §9, vyňaté oblasti 1 a 6

## Kontext

**Zadání vede Advisor bez výhrady.** F15 žádá doporučení cílového frameworku z měřeného času, paměti, vah dotazů a omezení počtu frameworků, T7 srovnání ILP s každým jednotlivým frameworkem, nejméně jednou ruční heuristikou a případně greedy strategií ([`requirements.md`](../requirements.md)). Prototyp Advisor má: model ILP nad GLPK a měření dvou frameworků ze šesti, Dapperu a EF Core ([`architecture.md`](../architecture.md) §8). Ze záruk je vyňatý vcelku (§9, oblast 1).

**Schválené dokumenty, které zadání zpřesnily, mu dávají jinou váhu než experimentům.** Záměr Advisor mezi pěti úkoly řešitele nejmenuje: pátým úkolem je „připravit testovací a běhové prostředí pro automatizované ověřování a experimenty" a harmonogram dává poslednímu měsíci „Experimenty, dokumentace a odevzdání". Specifikace vede F15 jako „sekundární cíl": Advisor bude rozšířen „podle časových možností" a funkcionalita je „doplňková vůči hlavnímu cíli". T7 je „sekundární experimentální cíl; nesmí omezit dokončení hlavního cross-language cíle F7–F13" a kapitola *Vyhodnocení* opakuje, že dokončení hlavního cíle má „před touto částí prioritu". Experimenty naopak specifikace váže bez výhrady: „Projekt musí připravit reprodukovatelné testovací prostředí, základní cross-language experimenty a měřicí infrastrukturu"; T1–T3 jsou formulované jako povinnost, u T4–T6 je povinná příprava a nepovinné jsou teprve plné experimenty navazující diplomové práce. Milník odevzdání jmenuje „cross-language experimentální výsledky", Advisor ne, a obhajoba má ukázat „výsledky experimentů".

**Specifikace přitom obojí spojuje do jednoho cíle.** Kapitola *Zbývající práce a postup* má „Cíl 3 — Advisor, benchmarking a experimenty (F15, T1–T7)" a harmonogram dává do listopadu „experimenty (T2, T3, T7)". Pořadí uvnitř cíle neurčuje; [`open-items.md`](../open-items.md) vedly otázku jako položku *Advisor v odevzdání*.

**Obě oblasti mají otevřenou práci, kterou nejde udělat naráz.** V experimentech neexistuje pipeline matice T2 s metrikami T3 (§9, oblast 6), nejsou rozhodnutá referenční data T1 a artefakt, který nevyrobil nástroj, nemá kudy vstoupit do stupňů ověření, na nichž stojí příprava T4–T6. V Advisoru se měří dva frameworky ze šesti, javová větev ([146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md)) a buňka měření jako proces ([147](147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md)) jsou rozhodnuté, ale nestojí, izolace spouštění cizího kódu (první věta S4) nemá limity, srovnání s variantami podle T7 chybí a oblast nemá testy chování. Konec projektu je podle záměru 2026-12-01.

## Zvažované varianty

1. **Advisor a experimenty jako jeden celek, který se dokončí najednou**, jak je spojuje kapitola *Zbývající práce a postup*. Odevzdání by se tak vázalo i na část, kterou záměr nejmenuje a specifikace na třech místech označuje za sekundární, a povinné experimenty by čekaly na nepovinnou práci — přesně to T7 zakazuje slovy „nesmí omezit". Zamítáme.

2. **Obě oblasti souběžně, pořadí po položkách jen podle značek** (rozh. [018](018-work-order-as-item-marker.md)). Pružné, ale „podle časových možností" se tak nedá vyhodnotit: zda na Advisor čas zbývá, lze posoudit jen ve chvíli, kdy je povinná část hotová, a každá položka Advisoru vzatá dřív je čas odebraný experimentům. T7 navíc potřebuje workload nad referenčními daty a místo, kde měřicí běh žije se záznamem — a obojí rozhoduje až experimentální oblast. Advisor vzatý dřív by tyto volby buď předjímal, nebo by se dělaly dvakrát. Zamítáme.

3. **Advisor z projektu vyřadit** — kód zůstane, jak je, rozšíření se neplánuje. Zamítáme: specifikace drží sekundární cíl otevřený „podle časových možností" a JSS článek staví na Advisoru druhou polovinu přístupu, výběr frameworku modelem ILP nad měřením (sekce 8). Vyřadit ho by bylo víc, než schválené dokumenty dovolují, a zahodilo by připravenost rozhodnutí 146 a 147.

4. **Advisor není podmínkou odevzdání a přijde na řadu po experimentech, zbude-li čas.**

## Rozhodnutí

**Volíme variantu 4.**

- **Odevzdání projektu nese experimenty, ne Advisor.** Experimenty jsou T1–T3 a příprava T4–T6 v rozsahu specifikace. Rozšíření Advisoru o další frameworky a jeho srovnání s jednotlivými frameworky a heuristikou (F15, T7) se začne až po nich, a jen tehdy, zbude-li do konce projektu čas.
- **Do té doby zůstává Advisor, jak je:** měří Dapper a EF Core, obrazovka i koncové body běží, ze záruk je vyňatý vcelku (§9, oblast 1) a nenárokuje F15, T7 ani první větu S4; druhá věta S4 v něm platí (rozh. [160](160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)).
- **Položky oblasti Advisor nedostanou značky pořadí, dokud nejsou experimenty hotové.**

**Pořadí sleduje závaznost.** Záměr je schválený jako to, co projekt slibuje, a Advisor v něm není; specifikace ho třikrát podřizuje hlavnímu cíli a experimenty naopak třikrát váže k odevzdání — povinností „musí", milníkem odevzdání a obhajobou. Odchylkou od specifikace to není: ta nechává rozšíření Advisoru časovým možnostem a jen nevyslovuje, kdy se o nich soudí. Tohle rozhodnutí ten okamžik určuje: po experimentech, kdy je povinná část hotová a zbývající čas je známý. Spojení „Advisor, benchmarking a experimenty" v jednom cíli kapitoly *Zbývající práce a postup* tím nepopíráme, jen pořadí uvnitř něj stanovujeme.

**Pořadí sleduje i závislost.** T7 srovnává ILP s jednotlivými frameworky a heuristikou nad workloadem a nad daty; nad jakými a v jakém běhu, rozhoduje experimentální oblast (referenční databáze, která dnes pro T7 znamená WideWorldImporters, a pipeline se záznamem běhu podle S6). Po experimentech dostane Advisor obojí hotové; před nimi by si to musel rozhodnout sám.

**Pořadí sleduje výzkumný přínos.** Specifikace vidí přínos projektu „především" v ověření cross-language použitelnosti mezireprezentace a v přípravě reprodukovatelného experimentálního prostředí, které má být deterministickou baseline pro srovnání s metodami založenými na velkých jazykových modelech. Obojí nesou experimenty. Advisor jako přístup JSS článek už popisuje a prototyp ho předvádí; jeho rozšíření o čtyři frameworky přidá kandidáty, ne novou výzkumnou otázku.

**Pořadí sleduje riziko.** Specifikace v *Rizicích* jmenuje metodologii měření Javy (JIT, startovní režie, správa paměti) a nevyřešenou izolaci spouštění (S4) jako nejistoty právě Advisoru, a javová větev rozhodnutí 146 je jeho největší otevřená položka. V odstavci *Pracnost a čas* řadí cíle tak, aby jádro zadání šlo před experimenty; tohle rozhodnutí prodlužuje totéž řazení o krok dál — experimenty před Advisorem.

## Důsledky

- **[`open-items.md`](../open-items.md)**: položka *Advisor v odevzdání* mizí a úvod oblasti Advisor vyslovuje pořadí; v přehledu nemá žádná položka Advisoru vyplněný sloupec *Nutné pro*.
- **Kořenové [`README.md`](../../README.md)** v *Beyond this version* říká, že Advisor přijde po experimentech, a věta o otevřené otázce, co uzavře příští milník, mizí.
- **Číslování verzí se tohle rozhodnutí netýká.** Rozhodnutí [098](098-the-number-is-decided-once-per-release.md) platí celé a o čísle rozhoduje vydání.
- **Nárok se nemění.** F15, T7 a první věta S4 zůstávají vyňaté s oblastí 1, takže §9, [`traceability.md`](../traceability.md) ani [`subset.md`](../subset.md) se neupravují. Rozhodnutí 146 a 147 zůstávají platná a nevykonaná, dokud na Advisor nedojde.
- **Specifikace se nepřepisuje** — je to schválený dokument; text práce pořadí vyloží tímto rozhodnutím. Pokud na Advisor čas nezbyde, odchází do odevzdání ve stavu prototypu s vyslovenou hranicí oblasti 1.
