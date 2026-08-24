# 058 — Do provozní příručky patří jen provozní polovina nasazovacího pohledu

Datum: 2026-08-24
Stav: platí
Požadavky: žádné
Podklad: rozhodnutí [057](057-deployment-view-in-the-operating-manual.md), rozhodnutí [007](007-documentation-structure.md)

## Kontext

Rozhodnutí [057](057-deployment-view-in-the-operating-manual.md) přesunulo do `ORMConvertor/README.md` celý §6 a je naimplementované commitem `888c946`. Přečtení výsledku ukázalo, že jeho argument pokrývá jen polovinu toho, co povolil.

**Šev, na kterém se dokumentace opravdu rozešla, vede jen mezi úvodem §6 a §6.4.** Právě ty dva měly v README protějšek, právě u nich je příkaz sám tvrzením, a právě tam vznikla dvě čísla „celé sady" k témuž dni (audit 2026-08-23, nálezy 1.1 a 1.2). Argument 057 na tuhle dvojici sedí přesně.

**§6.2, §6.3 a §6.5 na žádném švu nestály.** V README neměly protějšek, takže se přesunem nic nesloučilo — jen změnily adresu a jazyk. 057 vzal jeden platný důvod a natáhl ho přes tři podkapitoly, na které nedopadá; tak se to v něm dnes čte, jako by platil všude.

**Cena je měřitelná.** README vyrostlo z 2 449 na 8 907 slov a zhruba 4 200 z nich popisuje nástroj, ne jeho provoz: proč je u ověření EF Core vypnutá cache service provideru, jak Pico zachází s vnitřní šířkou prvků, co tvrdí a netvrdí testy REST kontraktu. Adresátem provozní příručky je ale ten, kdo chce mít instanci spuštěnou za dvě minuty; takový text mu stojí v cestě, i když je správný.

**A druhá cena je jazyková.** §6.2 je podkladem pro český text práce (T3, F11, S2) a stal se dostupným jen anglicky. Odstavec s tvrzeními, který po něm v `architecture.md` zůstal, to nenahrazuje: shrnutí není zdrojový materiál.

## Zvažované varianty

1. **Ponechat 057 a README zpřehlednit uvnitř** — rozdělit ho na víc souborů nebo zkrátit.
2. **Vrátit do architektury to, co popisuje nástroj**, a v příručce nechat provozní polovinu.
3. **Vrátit celý §6** a 057 zrušit jako celek.

## Rozhodnutí

**Varianta 2.** Do `ORMConvertor/README.md` patří úvod původního §6 (běhové cesty, publikovaný artefakt, prostředí, konfigurace), §6.1 (testovací databáze), §6.4 (kontejnerová konfigurace) a měření velikosti sady a pokrytí z konce §6.2. Do `architecture.md` se vracejí §6.2 v části o stupních ověření, §6.3 a §6.5, česky a v původním znění.

**Dělící čára je žánr, ne téma.** Co čtenář potřebuje, aby mohl *jednat* — příkazy, proměnné, předpoklady a doklad, co který běh prokázal —, patří do příručky. Co potřebuje, aby nástroji *rozuměl nebo ho změnil*, patří do architektury. Ta čára dělí §6 nesouměrně a je to v pořádku: nejde o obsah jedné kapitoly, ale o dvě různá publika, a číslování podkapitol nikdy nebylo tvrzením o tom, že jde o jeden žánr.

**Kde se obě strany dotýkají, vyhrává příručka a nese s sebou doklad.** Naměřená čísla — velikost sady, pokrytí, věty „ověřeno spuštěním" — jsou výsledkem provozu, ne popisem nástroje; zůstávají proto tam, kde stojí příkaz, který je vyrobil. Stupně ověření samotné, tedy co který dokazuje a proč je čtvrtý bez zástupce, se vracejí do architektury, protože to je tvrzení o nástroji a odkazuje se na ně §5.

**§6.1 zůstává v příručce vcelku.** Většina té podkapitoly je návod — kam zapsat připojovací řetězec, kdy se test přeskočí a kdy selže —, a její popisná část o životním cyklu fixture a hranici transakce je krátká a od návodu neoddělitelná. Rozdělit i ji by znamenalo založit přesně takový šev, před jakým 057 správně varuje.

**Čísla podkapitol se nepřečíslovávají.** Vrácené podkapitoly nesou dál 6.2, 6.3 a 6.5 a v kapitole zůstává mezera po 6.1 a 6.4. Odkazy auditů a hotových rozhodnutí tak míří na totéž, co popisovaly, a ta mezera je sama sdělením, kam se obsah poděl. Kapitola se přejmenovává, protože „Spuštění a nasazení" už neodpovídá tomu, co drží.

Varianta 1 řeší délku, ne příslušnost: rozdělené README by bylo pořád provozní příručkou, ve které stojí popis architektury, a čeština by se nevrátila. Varianta 3 zahazuje i tu část 057, která je správná — jeden dokument pro nasazení, ve kterém příkaz stojí vedle svého důvodu.

**057 se nahrazuje, nereviduje.** Podle rozhodnutí 007 se opravovat na místě smí jen tam, kde podle rozhodnutí ještě nevznikl kód; podle 057 vznikl commit `888c946`, takže čtenář potřebuje obě znění vedle sebe.

## Důsledky

README má 5 228 slov: proti stavu před 057 je delší o provozní materiál, který v něm chyběl, a proti 057 kratší o všechno, co popisuje nástroj. Kapitola 6 v `architecture.md` se jmenuje „Ověření artefaktů, frontend a rozhraní" a začíná odkazem do příručky a shrnutím toho, co nasazovací pohled tvrdí.

Vracený text je původní české znění z commitu `ed55413`, ne zpětný překlad, takže se okružní cestou nic neztratilo; jediné doplnění z 24. srpna — odstavec o manifestu a vývojové smyčce v §6.3 — jelo s ním.

Věty vázané na jeden stroj zůstávají v README, protože jsou výsledkem běhů. Úkol pro druhý stroj se tímhle rozhodnutím nemění.

Co z 057 platí dál: nasazovací pohled má jediné místo, README nese razítko „naposledy ověřeno proti kódu", má obsahovou tabulku a spojené části *Deployment* a *Tests*. Riziko, které 057 pojmenoval — příručka bez data ověření —, zůstává ošetřené.
