# 168 — Metodika běhů má místo v přehledu `docs/runs/`: druhy běhů, kdy a v jakém prostředí se spouštějí, co se před nimi hlídá a v jakém pořadí jdou

Datum: 2026-10-23
Stav: platí; nahrazuje výčet obsahu přehledu ve větě *Její `README.md` je přehled …* odstavce *Rozhodnutí* rozh. 167
Požadavky: žádné
Podklad: rozhodnutí [058](058-only-the-operational-half-of-the-deployment-view-moves.md) (provozní polovina nasazovacího pohledu v příručce), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (měřicí prostředí), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (běh FIBEN na vyžádání), [164](164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md) (dva režimy serveru), [166](166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md) (souběžné čtení soudce), [167](167-records-of-runs-and-measurements-have-a-folder-of-their-own.md) (složka `runs/`); záznamy v [`runs/`](../runs/README.md) ke dni rozhodnutí

## Kontext

Záznam běhu je doklad jen vůči metodě, kterou byl pořízen: dvě čísla téhož dotazu lze srovnat, jen když čtenář ví, v jakém režimu stál server, zda měl procesor turbo a zda vedle běhu nic nejelo. Rozhodnutí 167 dalo záznamům složku a předepsalo, co jejich hlavička nese. Metodika sama — které běhy projekt má, kdy se který spouští, v jakém prostředí a co se před ním na hostiteli zkontroluje — místo nemá. Ke dni rozhodnutí je rozptýlená:

- příručka `ORMConvertor/README.md` nese příkazy a k nim věty o tom, kdy se běh pouští (*on demand, for a version*) a že sady jdou po sobě;
- `architecture.md` §8 popisuje měřicí prostředí a dva režimy serveru tak, jak je nastavuje obraz a compose;
- rozhodnutí 145, 149, 164 a 166 nesou volby i jejich odůvodnění;
- kontrola hostitele — pozastavené aktualizace Windows, které jinak restartují stroj kolem čtvrté ráno, vypnuté uspání, volná paměť, stav procesoru přečtený před startem — v repozitáři není; je vidět jen v záznamech, které říkají, že platila.

Běh, který trvá hodiny až dny, stojí právě na té nezapsané části: soudce LDBC nad celou sadou běžel dvacet a pětadvacet hodin ([záznam](../runs/2026-10-12-ldbc-judge.md)), běh FIBEN v zafixovaném prostředí přes pět hodin ([záznam](../runs/2026-10-22-fiben-2.md)) a zafixovaný běh sad z 2026-10-18 se zastavil pro nedostatek paměti hostitele ([přehled](../runs/README.md#velikost-sady-a-pokrytí)). Experimentální kapitola práce (T2, T3, T7) bude metodiku popisovat a záznamy citovat; potřebuje ji česky a na jednom místě.

## Zvažované varianty

1. **Příručka `ORMConvertor/README.md`.** Příkazy tam už jsou a metodika by stála hned u nich. Zamítáme ze stejného důvodu, ze kterého 058 a 167 z příručky vyvedly všechno, podle čeho se nic nespouští: adresát příručky chce instanci spuštěnou za dvě minuty a kontrolní seznam vícedenního běhu mu stojí v cestě. Příručka je navíc anglicky, kdežto metodika je podkladem textu práce — tatáž jazyková cena, kvůli které 167 převedlo záznamy do `docs/`.
2. **`architecture.md` §6.2 a §8.** Zamítáme: architektura popisuje, jak funguje nástroj, a měřicí prostředí v §8 je jeho částí — co nastaví obraz, co compose. Kdy se který běh pouští, co se zkontroluje na hostiteli a v jakém pořadí běhy jdou, je praxe kolem nástroje, ne jeho vlastnost; v §8 by se míchala s implementací a měnila by se z jiných důvodů než ona.
3. **Samostatný soubor `docs/runs/metodika.md`.** Zamítáme: soubory v `runs/` jsou datované záznamy pojmenované `RRRR-MM-DD-<druh>.md` a po zapsání neměnné. Nedatovaný živý soubor mezi nimi by pravidlo složky rozbil, a přehled je jediný soubor složky, který živý už je.
4. **Oddíl *Metodika* v přehledu `docs/runs/README.md`.**

## Rozhodnutí

**Volíme variantu 4. Přehled `docs/runs/README.md` nese vedle pravidel záznamu, rejstříku a krátkých záznamů oddíl *Metodika*: tabulku druhů běhů — kdy se který spouští, v jakém režimu serveru a stavu procesoru, co se z něj zapisuje a který záznam nese jeho dobu —, kontrolu hostitele před dlouhým během a pořadí úplného běhu. Oddíl je živý a mění se s praxí; záznamy pod ním zůstávají neměnné.**

**Co oddíl nenese.** Příkazy, proměnné a nastavení zůstávají v příručce (167), implementace prostředí v §8, odůvodnění voleb v rozhodnutích, na která oddíl odkazuje. Doba běhu se do oddílu neopisuje: je změřenou hodnotou s datem, jiný strom ji posune, a stojí proto v záznamu, na který tabulka ukazuje.

**Pořadí úplného běhu.** Nejdřív se sestaví všechny obrazy a proběhnou obě sady v kontejnerech, pak běh FIBEN v měřicím režimu; soudce LDBC nad celou sadou, je-li vyžádán, jde poslední. Sady jdou první, protože trvají minuty a jejich selhání — i počet testů, který prozradí zastaralý obraz — zastaví běhy, které trvají hodiny. Běh FIBEN jde před soudcem, protože měří, a měří jen v měřicím režimu, který je výchozí; soudce trvá dny a přepíná server do režimu soudce, z něhož se po něm server vrací. Dva běhy nikdy nejdou najednou: měření jednoho je šumem druhého (149) a přehrání soudce obou sad sdílejí zámek (166).

## Důsledky

- `docs/runs/README.md` dostává oddíl *Metodika* a úvod přehledu ho jmenuje vedle příručky; příručka na něj odkazuje z oddílu *Tests*.
- [`docs/README.md`](../README.md): řádek `runs/` odpovídá i na otázku, jak se běží, a cyklus zní „metodika živá, záznamy neměnné, rejstřík přibývá"; pravidlo o tom, kde končí změna, jmenuje u `runs/` i metodiku.
- Stav rozh. 167 jmenuje nahrazenou větu; ostatní z 167 platí beze změny.
- Změna praxe — nový druh běhu, jiná kontrola hostitele, jiné pořadí — se zapíše do oddílu; volba, kterou by čtenář zpochybnil, jde napřed do rozhodnutí jako dosud.
