# 095 — Datovaný záznam o běhu jmenuje commit; velikost sady tvrdí jediné místo

Datum: 2026-09-21
Stav: platí
Požadavky: S2, S5, S6
Podklad: revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nálezy 2.2 a 2.3; rozhodnutí [058](058-only-the-operational-half-of-the-deployment-view-moves.md) a [069](069-major-marks-a-milestone-not-a-break.md)

## Kontext

[`ORMConvertor/README.md`](../../ORMConvertor/README.md) o sobě v části *How large the suite is, and what it covers* říká:

> **This is the only place that says how large the suite is.** Everything else refers here and does not repeat the number — two figures in two places drifted apart once already, and the only defence against that is not to write them twice.

Pravidlo je správné a je porušené. Číslo **1442 .NET testů a 146 javových, z toho 97 integračních** stálo ke dni revize ještě na čtyřech dalších místech: v [`architecture.md`](../architecture.md) §6.2 a §9, v [`traceability.md`](../traceability.md) u F13 a v sekci *Guarantees* kořenového [`README.md`](../../README.md). Jediná práce — dvě položky odbavené týž den — všechna čtyři naráz zneplatnila.

Obhajoba se nabízí: §6.2 to číslo neuvádí jako velikost sady, nýbrž jako součást **datovaného záznamu o běhu**, tedy jako doklad, že se něco stalo. To je legitimní žánr a bez čísla by doklad nebyl dokladem. Jenže právě ten žánr má druhou vadu, na kterou se přišlo týmž dnem: **datum dva běhy neodliší.** 2026-09-21 proběhly dva — jeden nad 1442 testy, druhý nad 1551 —, oba v zafixovaném prostředí, oba zelené, oba pravdivé. Kdo je čte za sebou, nemá je z čeho srovnat; revize z [2026-08-23](../audits/2026-08-23-post-release-1-1-0-audit.md) narazila na tutéž věc z druhé strany (nálezy 1.1 a 1.2) a rozplést to tehdy šlo jedině přes commity.

Naléhavost dává vydání. Poznámky k vydání bydlí v anotaci značky a **značka se nikdy neposouvá** (rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md)), takže číslo, které do anotace jednou půjde, tam zastará natrvalo a opravit se nedá jinak než dalším vydáním.

## Zvažované varianty

### 1 — Číslo nese jediný dokument, všude jinde se jen odkazuje

Doslovné vynucení dnešního pravidla: `ORMConvertor/README.md` je jediné místo s číslem a §6.2, §9, traceability i kořenový README na něj odkazují. Zamítáme, protože ruší i to, co má cenu: **datovaný záznam o běhu bez čísla přestává být dokladem.** Věta „obě sady proběhly zeleně" je tvrzení bez míry — nedá se z ní poznat, jestli běžela celá sada, nebo její polovina, a přesně tohle byl nález 1.1 revize z 2026-08-23.

### 2 — Číslo smí být kdekoli, jen se u něj uvede datum

Dnešní stav. Zamítáme: právě na něm se ukázalo, že datum nestačí. Dva běhy téhož dne jsou nerozlišitelné a čtyři místa zastarávají naráz.

### 3 — Rozlišit dva žánry: záznam o běhu nese číslo i commit, tvrzení o stavu jen odkazuje

## Rozhodnutí

**Volíme variantu 3. Číslo velikosti sady smí nést jedině *datovaný záznam o běhu*, a ten vedle data jmenuje i commit, nad kterým běh proběhl. Tvrzení o *současném stavu* číslo neopisuje, nýbrž na záznam odkazuje.**

**Rozdíl mezi žánry je ten, který tahle sada dokumentů dělá už jinde.** Záznam o běhu je událost: stalo se to, tehdy, tam, s tímhle výsledkem — a nezastarává, protože netvrdí nic o dnešku. Tvrzení o stavu zastarat může, a proto se opisovat nesmí. Je to táž dělba, jakou rozhodnutí [010](010-diagnostics-as-returned-data.md) udělalo mezi záznamem a stavem na straně diagnostiky: *„záznamy jsou události, ne stav, který by se odvolával"*.

**Záznam o běhu jmenuje commit.** Krátký hash stačí; datum zůstává, protože čte se rychleji. Bez commitu se dva běhy téhož dne neodliší a čtenář nemá jak ověřit, který strom se měřil — a přesně to je informace, kvůli které se záznamy pořizují. Opis typu „po rozhodnutí 085" je tatáž informace vyslovená tak, že se nedá ověřit strojově; nahrazuje se, nedoplňuje.

**Místem záznamů zůstává [`ORMConvertor/README.md`](../../ORMConvertor/README.md)**, jak to stanovilo rozhodnutí [058](058-only-the-operational-half-of-the-deployment-view-moves.md): běh sady je provozní polovina nasazovacího pohledu. `architecture.md` §6.2 si **jeden** takový záznam ponechá — ten, kterým javové požadavky vstoupily do nároku —, protože je to doklad nároku, ne popis provozu; nese proto commit jako každý jiný a nedoplňuje se s každým dalším během.

**Tvrzení o nároku číslo ztrácejí.** Sekce *Guarantees* kořenového [`README.md`](../../README.md), §9 a řádek F13 v [`traceability.md`](../traceability.md) říkají **co** je doložené a **čím** — zeleným během obou sad v zafixovaném prostředí —, a kolik testů to bylo, ponechávají záznamu. Nárok se tím neoslabuje: doložení není číslo, doložením je běh.

**Anotace značky číslo sady nenese.** Je to tvrzení o stavu v dokumentu, který se nikdy neopraví, tedy nejhorší možná kombinace. Co do anotace patří, vymezují tři odstavce rozhodnutí 069 — tvar výstupu, přibylé schopnosti, pohyb hranice záruk —, a velikost sady není ani jedno z nich.

## Důsledky

**Čtyři místa se opravují hned.** Kořenový README, §9 a řádek F13 číslo ztrácejí a odkazují; §6.2 si svůj jediný záznam ponechá a doplní commit. `ORMConvertor/README.md` dostane záznam za dnešní běh — 1551 .NET testů a 146 javových, z toho 97 integračních, nad commitem, který ten běh měřil — a jeho dosavadní záznamy si commit **nedoplňují zpětně**: dohledat ho lze z historie a přepisovat datované záznamy zpětně je právě to, co tahle sada dokumentů jinde odmítá.

**Pokrytí testy se řídí týmž pravidlem**, protože je to číslo téhož druhu: měří běh, ne nástroj. Zůstává v `ORMConvertor/README.md`, dostane commit, a §9 se na ně u vyňaté oblasti 1 odkazuje místo opisování.

**Cena je jeden krok navíc při každém měření.** Kdo běh zapíše, musí u něj mít hash; je to `git rev-parse --short HEAD` a nic víc. Proti tomu stojí, že se čtyři dokumenty přestanou rozcházet pokaždé, když někdo přidá test.

**Co tím rozhodnuté není: počet testů v javové sadě si od rozhodnutí [087](087-an-integration-test-is-a-run-against-the-database.md) hlídá sada sama.** Tohle rozhodnutí na tom nic nemění a je to jiný mechanismus — `SuiteSizeTest` tvrdí **mez** („aspoň 60, z toho aspoň 20 integračních"), ne velikost. Mez zastarat nemůže, protože ji vynucuje build; velikost ano, a proto potřebuje tohle pravidlo. Stejný strážce na .NET straně neexistuje a tímhle rozhodnutím nevzniká: F12 mez vyslovuje jen pro javovou sadu a vymýšlet ji pro .NET stranu by znamenalo tvrdit číslo, které nikdo nezadal.
