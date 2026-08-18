# 018 — Pořadí práce jako značka u položky

Datum: 2026-08-18
Stav: platí
Požadavky: žádné

## Kontext

`open-items.md` dosud vedl dva seznamy: nahoře „Doporučené pořadí" a pod ním samotné položky. Audit z 2026-08-15 na tu dvojici našel tři samostatné nálezy (3.1–3.3): bod pořadí, ke kterému žádná položka neexistovala, bod, který se s položkou neshodl názvem ani kategorií, a bod tvrdící „poslední kus F3", zatímco dvě položky F3 v pořadí nebyly. Audit sám v kapitole 7 poznamenal, že mají společnou příčinu — oba seznamy se udržují nezávisle — a otázku, co s tím, nechal otevřenou.

Opravy z auditu stav srovnaly. Za tři dny se rozešel znovu. Pořadí dnes vede jako bod 1 „Klíčová třída u formy `Embedded`" a značí ji jako rozhodnutí, kdežto v souboru je pod nadpisem „Klíčová třída u kompozitního klíče na straně entity" a leží v sekci `## Otevřená práce`. Šest bodů před ní se odbavilo a ona zůstala — ne proto, že by ji někdo vybral jako další, ale proto, že zbyla. Plán tím přestal být plánem a stal se zbytkem.

Chyba není v nepozornosti. Rozhodnutí [007](007-documentation-structure.md) uložilo, že položka odsud zmizí, jakmile je hotová, a CLAUDE.md k tomu žádá jako povinný uzavírací krok aktualizaci `architecture.md`. Odškrtnout hotovou položku tedy znamená tři úpravy na třech místech, z nichž jedno je duplikát druhého — a duplikát se vynechává právě tehdy, když je práce nejhustší. Pravidlo, které se dodrží jen při klidném dni, není pravidlo.

## Zvažované varianty

1. **Nechat oba seznamy a hlídat je pořádněji.** Audit už jednou přesně tohle udělal a výsledek vydržel tři dny. Nová instance téhož opatření nemá důvod dopadnout jinak, protože příčinou není chyba v provedení, ale to, že se táž informace udržuje dvakrát. Zamítáme.

2. **Očíslovat všechny položky do jednoho úplného pořadí.** Odstraní duplicitu, ale vynutí si úplné uspořádání na věcech, které srovnatelné nejsou — doladění frontendu proti javovému ekosystému —, a každé vložení položky doprostřed znamená přečíslovat zbytek. Čísla by navíc předstírala rozmyšlený plán tam, kde jde o nezařazenou práci.

3. **Zrušit samostatný seznam a pořadí vyjádřit značkou přímo u položky.** Informace zůstane na jediném místě, značka je u textu, který popisuje, o co jde, a hotová položka odchází i se svou značkou.

## Rozhodnutí

Volíme variantu 3. **Samostatný seznam „Doporučené pořadí" zaniká. Pořadí nese značka na řádku vazeb té položky, které se týká.**

Značky jsou dvě a jejich počet je omezený:

- **`Na řadě.`** — nejvýš jedna položka v souboru. To, co se dělá teď.
- **`Potom.`** — nejvýš dvě položky. Co je zařazené hned za ní.

Všechno ostatní je neuspořádané a bere se podle priorit plynoucích z požadavků F/S/T. Značka stojí na začátku kurzívového řádku vazeb, který má podle formátu z rozhodnutí 007 každá položka; preambule souboru konvenci vysvětluje, takže se další krok dá najít hledáním slova, ne jen čtením shora.

Že plán nesahá dál než na tři položky, není ochuzení. Dosavadní pořadí se stejně přepisovalo po každém odbaveném bloku a jeho vzdálenější body sloužily jako přání, ne jako závazek; pro opravdu vzdálený horizont má soubor vlastní tabulku, která k tomu účelu stačí. Co seznam skutečně nesl a značka neunese — proč zrovna tahle položka je další — patří do řádku vazeb, kde už stejně jsou předpoklady a blokátory položky.

## Důsledky

Povinný uzavírací krok se zkracuje. Hotová položka se smaže a značka se přesune; není co srovnávat, protože není druhý seznam.

CLAUDE.md popisuje začátek práce větou, že „the recommended order at its top says where to continue". Ta přestává platit a mění se v témže průchodu. Jiný dokument na pořadí neodkazuje.

Audit dostává mechanickou kontrolu, kterou dřív neměl: značek `Na řadě` smí být nejvýš jedna a `Potom` nejvýš dvě, a každá musí stát u položky, která v souboru existuje. To se ověří hledáním, ne čtením dvou seznamů proti sobě.

Riziko je jediné a přiznáváme ho: čtenář už neuvidí celý plán na jednom místě. Bereme to jako férovou cenu — plán na jednom místě se právě ukázal jako plán, který nesouhlasí sám se sebou.
