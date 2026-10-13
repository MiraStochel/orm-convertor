# 135 — Příklad je volitelný první krok překladové obrazovky, kroky nejsou číslované a sada vzorků je šest příkladů vstupu; kód roste s oknem

Datum: 2026-10-12
Stav: platí
Požadavky: F14, S7
Podklad: rozhodnutí [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md), které tímto nahrazujeme, a přes ně [124](124-direction-as-one-row-and-input-beside-output-paired-per-fact.md), [100](100-interactive-comparison-as-a-frozen-mockup.md), [099](099-examples-are-content-not-a-choice.md) a [033](033-shape-of-the-static-frontend-screens.md); dále [032](032-frontend-as-static-pages-without-a-build.md), [056](056-work-in-progress-input-stays-in-the-browser.md), [098](098-the-number-is-decided-once-per-release.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md); schválená specifikace (`docs/specifikace.tex`, oddíl *Uživatelské rozhraní*); [`architecture.md`](../architecture.md), §6.3

## Kontext

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných kroků, 124 je stáhlo na čtyři — směr, vstupní soubory, převod, výsledek — a 125 to převzalo beze změny. Hotový vstup se dá na obrazovku dostat dvěma cestami: tlačítkem *Load sample set*, které naplní vzorek Customer zvoleného zdrojového frameworku z `/samples` a směr nechá, jak je, a výběrem příkladu z `/examples` (099), který nastaví směr i jednotky. Výběr příkladu stojí ve druhém kroku, pod směrem.

Procházka obrazovkou ukázala, že tvar odporuje tomu, jak se obrazovka používá:

1. **Příklad přepíše směr, který mu předchází.** Kdo začíná příkladem — a kdo nástroj vidí poprvé, začíná skoro vždy jím —, volí v prvním kroku směr, který druhý krok hned zahodí.
2. **Čísla kroků slibují pořadí, které příklad porušuje.** Volitelný krok by čísla buď posunul, nebo by stál mimo ně.
3. **Dvě cesty k hotovému vstupu dělají napůl totéž a míchají dvě různé věci.** Pro EF Core, Dapper a MyBatis je vzorek obsahově týž jako první, druhý a pátý příklad, jen jinak rozdělený do souborů. Seznam příkladů přitom obsahuje jen **popsané příklady**: výkladová stránka každý vykládá pro jeden směr — „atributy se stanou XML mapováním" platí pro EF Core → NHibernate a pro nic jiného —, takže cíl je součástí výkladu. Vzorek je jiný druh: **příklad vstupu**, který žádný směr nevykládá a dá se poslat do kteréhokoli cíle. Dávat mu doporučený cíl nemá opodstatnění.
4. **Oddálení stránky neukáže víc kódu.** Kódový panel má výšku `30rem`; `rem` se se zoomem zmenšuje spolu s písmem, takže panel ukazuje pořád asi dvacet čtyři řádků. Pico omezuje šířku obsahu na 1450 px, takže při oddálení rostou jen okraje. Kdo chce porovnat delší vstup s výstupem vedle sebe, nemá jak.

## Zvažované varianty

**Kde stojí výběr příkladu.** (a) Ve druhém kroku jako dosud — krok, který se zahodí. (b) Jako krok číslovaný mezi ostatními: buď „krok 0", nebo pět kroků, z nichž první je volitelný; obojí vyžaduje vysvětlovat číslo, které nic nevyjadřuje. (c) Jako volitelný první oddíl nad směrem. Volíme (c).

**Číslování.** Číslo v nadpisu neříká nic, co neřekne pořadí oddílů shora dolů. S7 počítá kroky scénáře („nahrát projekt → zvolit cíl → přeložit → zobrazit chyby"), ne čísla v nadpisech, a scénář má dál čtyři kroky; příklad je zkratka, která vyplní dva z nich najednou. Čísla proto mizí. Schválená specifikace mluví o „pěti očíslovaných krocích" a její makety je kreslí; makety jsou podle jejího vlastního textu schematické a od pěti kroků se obrazovka odchýlila už rozhodnutím 124. Tady se odchylka v číslování vyslovuje stejně otevřeně.

**Sada vzorků.** (a) Ponechat tlačítko — dvě cesty k témuž vstupu, z nichž jedna směr nastaví a druhá ne. (b) Tlačítko smazat a vzorek nenahradit, protože tři z šesti už jsou popsanými příklady — NHibernate, Hibernate a EclipseLink by pak přišly o malý vstup, kterým se začíná. (c) Udělat ze vzorků **šest příkladů vstupu** na začátku téhož seznamu, jeden na framework, odlišených od popsaných příkladů. Volíme (c): jeden výběr pro obě cesty a rozdíl mezi oběma druhy příkladů vyslovený v seznamu samém.

**Jaký cíl nastaví příklad vstupu.** (a) Cíl nechat, jak je — pak ale týž řádek dává pokaždé jiný převod podle toho, co bylo na obrazovce předtím. (b) Týž framework jako zdroj. Volíme (b): je to jediný cíl, který nic nedoporučuje — převod do sebe sama ukáže, co nástroj ze vstupu přečetl a co z toho napíše zpět — a skutečný cíl se pak přepne jedním výběrem. Převod do téhož frameworku nástroj umí ve všech šesti.

**Odkud se příklady vstupu vezmou.** (a) Šest nových položek `/examples` se zdrojem rovným cíli. Smíchalo by to popsané příklady se vstupy v jednom seznamu, který výkladová stránka i `ExampleCatalogTest` čtou jako popsaný. (b) Z `/required-content` a `/samples`, které obrazovka už čte a které jsou přesně tímto vstupem: vzorek pro každou jednotku každého frameworku. Volíme (b): rozdíl mezi popsaným příkladem a příkladem vstupu se kryje s rozdílem mezi `/examples` a `/samples`, server ani REST kontrakt se nemění a `/samples` neztrácí na obrazovce jediného čtenáře.

**Výška a šířka kódu.** (a) Pevná výška v `rem` jako dosud — oddálení nic nepřidá. (b) Panel, jehož výšku si uživatel táhne — u každého panelu zvlášť a po každém převodu znovu. (c) Výška odvozená od výšky okna (`vh`): oddálení zmenší písmo, ale okno ne, takže se do panelu vejde víc řádků. K tomu šířka: překladová obrazovka se roztáhne na celé okno a omezí se jen délka řádku odstavců, aby text zůstal čitelný. Přepínání mezi jedním a dvěma sloupci zůstává na dotazu na kontejner, ne na šířce okna (032). Volíme (c) s plnou šířkou.

## Rozhodnutí

> **Překladová obrazovka má oddíly bez čísel, shora dolů: volitelný výběr příkladu, směr, vstupní soubory, převod, výsledek. Výběr příkladu nabízí dvě skupiny. Nejprve šest příkladů vstupu — vzorek Customer každého frameworku, složený z `/required-content` a `/samples` —, z nichž každý nastaví zdroj i cíl na svůj framework. Pod nimi popsané příklady z `/examples`, které nastaví směr, pro který je výkladová stránka vykládá. Tlačítko *Load sample set* mizí. Výkladová stránka začíná týmž vstupem: šest vzorků, popsaných jen jako základ, který jde do kteréhokoli cíle. Kódové panely mají výšku odvozenou od výšky okna a překladová obrazovka zabírá celou šířku okna, takže oddálení stránky ukáže víc kódu; vstup a výstup stojí ve dvou sloupcích, jakmile je na ně místo, jinak pod sebou.**

**Obrazovka.** Oddíl *Start from an example* je první a je nepovinný; seznam má skupinu *Inputs* (jeden řádek na framework, cíl týž) a skupinu *Described on the Examples page*. Načtení obojího nahradí jednotky na obrazovce, s potvrzením, je-li na ní práce, a dialekt zdrojového SQL nevysloví — tak jako dosud příklad. Jednotky příkladu vstupu dostanou jména odvozená z obsahu, jak je dostávala sada vzorků.

**Výkladová stránka.** Před popsanými příklady stojí oddíl o výchozím vstupu: že je to týž zákazník v každém z šesti frameworků, základ pro kterýkoli cíl, a kde ho najde překladová obrazovka. Vstupní soubory ukazuje složené, bez převodu. Nic dalšího o něm nevykládá. Jedinou výhradu říká jednou větou: Dapper a MyBatis nemají pro klíč místo, takže do NHibernate, Hibernate a EclipseLink nese klíč katalog databáze.

**Co se nemění.** Všechno ostatní z rozhodnutí 125 a přes ně ze 124 platí beze změny: směr jako jeden řádek s prohozením, vstup vedle výstupu s párováním výrokem serveru, kniha tvrzení s pozicemi jako volitelnou vrstvou, pět dokumentů frontendu, dávkový vstup podle F14 (jednotky ze souborů, prázdné a nově jen z příkladu), validace před odesláním, záznamy po entitách se `Failure` první, hlavička výsledku se stavem katalogu, `POST /archive`, Advisor v dosavadním rozsahu. `/samples` zůstává v REST kontraktu beze změny.

## Důsledky

**REST kontrakt se nemění,** do čísla vydání se změna zařadí při vydání (rozhodnutí 098).

**Rozhodnutí 125 dostává stav `nahrazeno 135` celé,** jako řada 033 → 099 → 100 → 124 → 125 před ním: mění se jen tvar překladové obrazovky a výkladové stránky popsaný výš, vše ostatní je výčtem převzaté.

**Nárok výkladové stránky hlídá test.** Věta „základ pro kterýkoli cíl" je tvrzení o nástroji, ne o stránce, a `ApiContentContractTest` ji drží nad celou maticí 6 × 6: každá sada vzorků projde do každého cíle; jediné odmítnutí, které matice připouští, je klíč u zdroje Dapper nebo MyBatis bez katalogu, a do téhož frameworku, kam řádek příkladu vstupu míří, neodmítne nic.

**S7 beze změny:** scénář má čtyři kroky jako po rozhodnutí 124, příklad dva z nich vyplní jedním výběrem.

**Co tohle rozhodnutí nerozhoduje:** obsah vzorků a popsaných příkladů (obsah stránek podle 099, ne volba) a tvar ostatních obrazovek.
