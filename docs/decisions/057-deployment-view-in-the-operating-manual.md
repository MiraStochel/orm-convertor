# 057 — Nasazovací pohled bydlí v provozní příručce, ne v architektuře

Datum: 2026-08-24
Stav: nahrazeno 058
Požadavky: žádné
Podklad: rozhodnutí [007](007-documentation-structure.md), audit 2026-08-23, nálezy 1.1 a 1.2

## Kontext

`architecture.md` §6 „Spuštění a nasazení" je dnes největší kapitola dokumentu a jediná, která má vedle sebe druhý dokument o témž: `ORMConvertor/README.md`. Dělba mezi nimi je vyslovená hned v druhém odstavci §6 — *příkazy bydlí v provozní příručce, popis toho, proč je každá cesta postavená takhle, v architektuře* — a je myšlená dobře. V praxi ale drží hůř, než jak zní.

**Dvě místa o týchž bězích se rozejdou.** Audit 2026-08-23 to zachytil na číslech: dokument nesl k témuž dni dvě velikosti „celé sady", 419 v §6.2 a 392 v §6.4, obě správné a každá o jiné sadě. Odpovědí tehdy bylo pravidlo *velikost sady se píše na jediném místě*. Pravidlo drží, ale ošetřuje jeden údaj, ne příčinu: kdykoli se nasazovací cesta změní, sahá se na dva dokumenty a jeden z nich se zapomene.

**Hranice mezi „proč" a „jak" navíc není v tomhle pohledu ostrá.** §6 sám si musel vyhradit výjimku pro případy, kdy je příkaz předmětem tvrzení — znění obou compose profilů, na kterém stojí nárok S5, a to, co spouští úlohy workflow. Výjimka není nedopatření: u nasazovacího pohledu *je* příkaz často tím tvrzením. „`docker compose --profile test run --rm tests` obraz sám nepřestavuje" není ani „jak", ani „proč", je to obojí najednou, a rozdělit tu větu mezi dva dokumenty znamená, že v jednom z nich bude neúplná.

**Adresát je stejný, a není to čtenář `docs/`.** Kdo pohled potřebuje, je ten, kdo nasazuje instanci nebo reprodukuje testy — a ten sáhne po `README.md` vedle řešení, ne po české kapitole v `docs/`. Tabulka pohledů v `architecture.md` to u nasazovacího pohledu už dnes připouští, když k němu jako provozní referenci připisuje README. §9 tutéž úvahu dotáhl do konce u nároku verze: kanonické znění je anglická sekce v kořenovém `README.md`, protože adresátem je konzument nástroje, který se do českého dokumentu nedostane.

Dva dokumenty, které odpovídají na tutéž otázku témuž čtenáři, nejsou dva pohledy. Je to jeden pohled ve dvou kopiích.

## Zvažované varianty

1. **Ponechat dělbu a zostřit pravidlo** — vypsat, co je „jak" a co „proč", a hlídat to při každé změně.
2. **Přesunout nasazovací pohled celý do `ORMConvertor/README.md`** a v `architecture.md` nechat §6 jako rozcestník.
3. **Přesunout ho a kapitolu zrušit**, s přečíslováním §7–§9.

## Rozhodnutí

**Varianta 2.** Nasazovací pohled — dnešní §6 včetně §6.1 až §6.5 — se stěhuje celý do `ORMConvertor/README.md`; v `architecture.md` zůstává §6 jako krátký rozcestník.

**Celý pohled, ne jeho polovina.** Nabízí se přestěhovat jen běhové cesty a kontejnerovou konfiguraci a nechat ověřování artefaktů, frontend a REST kontrakt v architektuře. Znamenalo by to ale jen posunout šev, a šev je právě to, co nefunguje: velikost testovací sady, na které se ta dvojkolejnost poprvé ukázala, leží v §6.2, kdežto běh, který ji má doložit, v §6.4. Rozdělit je znovu, jen jinudy, je oprava, která si příčinu bere s sebou.

**Rozcestník, ne smazání.** Číslo kapitoly je adresa, na kterou odkazují audity a hotová rozhodnutí, a ty se podle rozhodnutí 007 nepřepisují. Přečíslování §7→§6 by z každého takového odkazu udělalo tichou nepravdu — čtenář auditu 2026-08-23 by pod „§6.1" našel rozhraní parserů. Rozcestník drží adresu platnou a stojí čtenáři v cestě právě jednou.

**Rozcestník ale není holý odkaz.** Nese česky, v jednom odstavci, co nasazovací pohled tvrdí — že S3 je změřené s řádovou rezervou, že S5 platí v rozsahu, který v repozitáři existuje, že stupňů ověření jsou čtyři a čtvrtý nemá zástupce —, a teprve pak odkazuje na místo, kde je to rozvedené. Důvod je věcný, ne kosmetický: text práce je český a tahle tvrzení do něj patří, takže mizet z české větve dokumentace nesmí ani ta jedna věta, na kterou se v práci odkážeme. Detail, který je návodem, ať je tam, kde se podle něj jedná.

**Jazykem přestěhovaného textu je angličtina.** Repozitář je mimo `docs/` anglický a `README.md` je jeho součástí; překlad je tedy součástí přesunu, ne jeho vedlejším efektem. Je to táž volba jako v §9, jen s opačným rozdělením rolí: tam nese kanonické znění angličtina a čeština k němu dodává odůvodnění, tady nese angličtina celý pohled a čeština jen jeho tvrzení.

Varianta 1 řeší příznak. Vypsat, co je „jak" a co „proč", jde — jenže výjimka, kterou si §6 vyhradil, ukazuje, že u tohohle pohledu ta čára místy nevede. A pravidlo, které se musí hlídat rukou při každé změně, je přesně ten druh záruky, jaký si projekt jinde odepírá.

Varianta 3 je čistší v jediné věci, totiž že nezůstane prázdné místo, a platí za to rozbitím odkazů v dokumentech, které se nesmějí opravit.

## Důsledky

`ORMConvertor/README.md` se stává jediným místem, kde se říká, jak se nástroj spouští, nasazuje, testuje a co je na těch cestách ověřené. Poroste tím zhruba na dvojnásobek a přestane být čtením na pět minut; rychlý start proto zůstává nahoře a delší části za ním, v pořadí nasadit → nakonfigurovat → otestovat → ověřit → rozhraní.

Odstavec §6 o tom, že „příkazy bydlí v provozní příručce", mizí spolu s kapitolou — dělba, kterou popisoval, přestala existovat. Tabulka pohledů v `architecture.md` u nasazovacího pohledu nově odkazuje rovnou na README.

Živé dokumenty, které na §6.x odkazují — `open-items.md` a `traceability.md` —, se přesměrují na README. Audity a hotová rozhodnutí se nepřepisují: jejich odkazy na §6.x popisují, co dokument nesl tehdy, a čtenář se přes rozcestník dostane dál.

Věty, které se v přestěhovaném textu vážou na jeden stroj — verze systému, procesoru a paměti, verze Docker Desktopu, naměřené časy —, se stěhují beze změny. Jestli mají zůstat, nebo je nahradit tvrzení nezávislé na stroji, rozhoduje běh na druhém stroji, ne tenhle přesun.

Riziko je jedno a stojí za vyslovení: nasazovací pohled se tím dostává mimo dokument, který má na sobě datum posledního ověření proti kódu. README takové razítko nemá a bude ho potřebovat — jinak se z „ověřeno spuštěním" stane tvrzení bez data.
