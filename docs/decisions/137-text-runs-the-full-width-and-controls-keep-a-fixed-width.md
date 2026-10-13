# 137 — Text běží přes celou šířku stránky, výběry a pole mají pevnou šířku vlevo, šipka složeného oddílu stojí u popisku a výkladová stránka zabírá celé okno

Datum: 2026-10-13
Stav: platí
Požadavky: F14, S7
Podklad: rozhodnutí [135](135-an-example-is-the-optional-first-step-and-the-sample-set-is-six-input-examples.md), které tímto nahrazujeme, a přes ně [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md), [124](124-direction-as-one-row-and-input-beside-output-paired-per-fact.md) a [099](099-examples-are-content-not-a-choice.md); dále [032](032-frontend-as-static-pages-without-a-build.md) (vlastní vrstva `app.css`, dva sloupce dotazem na kontejner); [`architecture.md`](../architecture.md), §6.3

## Kontext

Rozhodnutí 135 roztáhlo překladovou obrazovku na celou šířku okna, aby oddálení stránky ukázalo víc kódu, a délku řádku odstavců na ní omezilo na 100 znaků, aby text zůstal čitelný. Ovládací prvky nechalo tak, jak je kreslí Pico: výběr i textové pole mají `width: 100%` a v řádcích `flex` rostly, dokud řádek nevyplnily. Ostatní stránky zůstaly v kontejneru Pico, nejvýš 1450 px širokém. Procházka hotovou obrazovkou ukázala tři vady, které z té kombinace plynou, a čtvrtou na výkladové stránce:

1. **Odstavec omezený na 100 znaků vypadá na široké stránce jako chyba rozvržení.** Pod ním stojí panely kódu přes celou šířku okna, takže úvodní odstavec, poznámka o párování a výklad pásu *Facts* tvoří úzký sloupec u levého okraje a vpravo zbývá prázdná plocha širší než text. Omezení v jednotkách `ch` se navíc se zoomem zmenšuje spolu s písmem, okno ne: čím víc stránku oddálíme — a kvůli tomu je široká —, tím menší díl šířky text zabere. Na ostatních stránkách měly úvodní odstavec a poznámky omezení ještě užší, 60 a 70 znaků.
2. **Výběr přes celou šířku okna odtrhne hodnotu od své šipky.** Jméno frameworku má nejvýš jedenáct znaků; na stránce široké dva tisíce pixelů stojí na levém konci pole a šipka výběru na pravém. Totéž platí pro seznam příkladů, dialekt zdrojového SQL a jméno souboru v hlavičce jednotky, které v řádku `flex` vyplnilo všechno místo mezi sebou a tlačítkem *Remove*. Šířka pole přestala říkat cokoli o tom, co do něj patří.
3. **Šipka složeného oddílu stojí na druhém konci řádku.** Pico ji v `<summary>` posouvá k pravému okraji (`float: right`). Na výkladové stránce má složený oddíl vzorek každého z šesti frameworků i vstup a výstup každého příkladu, a popisek „EF Core 2 files" dělí od šipky, která říká, že se oddíl dá otevřít, celá šířka textu.
4. **Výkladová stránka staví vstup vedle výstupu nejvíc ze všech,** a přesto zůstala v kontejneru Pico: příklady o desítkách souborů ukazují panely ve dvou sloupcích (`.pane-grid`), a oddálení na ní ukáže jen širší okraje — tutéž vadu, kvůli které 135 roztáhlo překladovou obrazovku.

## Zvažované varianty

**Šířka textu.** (a) Ponechat čitelnou délku řádku, jak ji typografie doporučuje pro souvislý text, případně odstavce vycentrovat. Vycentrovaný úzký sloupec nad panely zarovnanými vlevo by stránku rozdělil na dvě osy a nadpisy oddílů by stály jinde než text pod nimi; sloupec u levého okraje je vada 1. (b) Text přes celou šířku obsahu, stejně jako nadpisy, panely a tabulky. Doporučená délka řádku platí pro dlouhé čtení; texty těchto stránek jsou krátké poznámky o několika řádcích, které se přečtou jednou, kdežto obrazovka se používá dlouho, a stránku, kterou si uživatel kvůli kódu oddálí, má text sledovat, ne se v ní ztrácet. Volíme (b), na všech stránkách.

**Šířka ovládacích prvků.** (a) Ponechat roztažení podle Pico — vada 2. (b) Šířka podle obsahu (`width: auto`): výběr se pak řídí nejdelší možností, takže se řádek posune pokaždé, když server změní seznam příkladů, a prázdné textové pole nemá obsah, podle kterého by se měřilo. (c) Pevná šířka u levého okraje podle toho, co do pole patří, v `rem`, aby rostla s písmem: krátká (12 rem) pro jméno frameworku a číslo, výchozí (24 rem) pro hodnotu, která je frází — dialekt zdrojového SQL —, dlouhá (40 rem) pro název příkladu a cestu souboru z archivu. Na displeji užším než tato šířka pole ustoupí (`max-width: 100%`) a řádky `flex` se dál zalamují, takže responzivita podle 032 zůstává. Volíme (c). Výběr jazyka jednotky a výběr cíle na stránce LDBC už šířku podle obsahu mají a ponechávají si ji: jejich možnosti jsou krátké, pevně dané a stojí v řádku s dalšími prvky.

**Šipka složeného oddílu.** (a) Přesunout šipku před popisek, jak ji kreslí prohlížeč bez Pico — složené oddíly by se tím odlišily od podoby, kterou jim dává knihovna. (b) Zúžit `<summary>` na šířku jeho textu (`width: fit-content`); šipka zůstane za popiskem, jen u něj. Plocha, na kterou se kliká, pak odpovídá tomu, co je vidět, a obrys fokusu obepne popisek, ne prázdný řádek. Volíme (b), u všech složených oddílů v obsahu stránek.

**Která stránka zabírá celé okno.** (a) Jen překladová obrazovka, jak určilo 135 — vada 4. (b) Všechny stránky. Rozcestník je text a čtyři karty, Advisor formulář, který ukáže kód až po běhu, a stránka LDBC tabulku a jeden dotaz po druhém; žádná z nich nestaví kód vedle kódu, takže by roztažením získala jen delší řádky. (c) Překladová obrazovka a výkladová stránka — ty dvě, které vstup a výstup stavějí vedle sebe. Volíme (c). Diagram potrubí na výkladové stránce si přitom ponechá svou velikost, nejvýš 60 rem: roztažený přes okno by měl popisky větší než nadpisy.

## Rozhodnutí

> **Text stránek — úvodní odstavec, odstavce a poznámky — běží přes celou šířku obsahu, na všech stránkách. Výběr a textové či číselné pole mají pevnou šířku u levého okraje svého řádku: krátkou pro jméno frameworku a číslo, výchozí pro frázi, dlouhou pro název příkladu a jméno souboru; na užším displeji ustoupí. Souhrn složeného oddílu je široký jako jeho text, takže šipka stojí u popisku. Na celou šířku okna se roztahuje překladová obrazovka a nově i výkladová stránka; rozcestník, Advisor a stránka LDBC zůstávají v kontejneru Pico.**

**Co se nemění.** Všechno ostatní z rozhodnutí 135 a přes ně ze 125 a 124 platí beze změny: oddíly bez čísel s výběrem příkladu jako volitelným prvním krokem, šest příkladů vstupu a popsané příklady v jednom seznamu, žádné tlačítko *Load sample set*, výkladová stránka začínající výchozím vstupem, výška kódového panelu z výšky okna (`max(16rem, 75vh)`), dva sloupce dotazem na kontejner, směr jako jeden řádek s prohozením, vstup vedle výstupu s párováním výrokem serveru, kniha tvrzení s pozicemi jako volitelnou vrstvou a vše ostatní, co 135 výčtem převzalo.

## Důsledky

**Rozhodnutí 135 dostává stav `nahrazeno 137` celé,** jako 125 před ním: mění se jen šířky popsané výš, vše ostatní je výčtem převzaté.

**Šířky jsou tři tokeny v `app.css`** (`--app-control-width`, `--app-control-width-short`, `--app-control-width-long`), ne čísla rozptýlená po pravidlech; pole, které potřebuje jinou šířku, dostane jeden z nich.

**REST kontrakt, server a S7 beze změny.** Mění se jen `app.css` a třída `wide` výkladové stránky; scénář má dál čtyři kroky.

**Co tohle rozhodnutí nerozhoduje:** zda se na celé okno roztáhne i Advisor nebo stránka LDBC — dnes jim chybí důvod, který mají obě roztažené stránky, kód vedle kódu.
