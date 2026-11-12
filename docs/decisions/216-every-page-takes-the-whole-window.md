# 216 — Na celou šířku okna se roztahuje každá stránka

Datum: 2026-11-12
Stav: platí; nahrazuje výběr stránek, které zabírají celé okno, rozh. 137
Požadavky: F14, S7
Podklad: rozhodnutí [137](137-text-runs-the-full-width-and-controls-keep-a-fixed-width.md), jehož část tímto nahrazujeme, [032](032-frontend-as-static-pages-without-a-build.md) a [033](033-shape-of-the-static-frontend-screens.md)

## Kontext

Rozhodnutí 137 roztáhlo na celou šířku okna dvě stránky — překladovou obrazovku a výkladovou stránku —, protože jen ty stavějí kód vedle kódu, a ostatní nechalo v kontejneru Pico, nejvýš 1450 px širokém. Otázku, zda se roztáhne i Advisor nebo stránka LDBC, výslovně nechalo otevřenou: „dnes jim chybí důvod".

Od té doby přibyly stránky katalogů FIBEN a SEDE a výuková stránka a stránky katalogů dostaly tabulku *každý dotaz v každém cíli*: šest sloupců cílových frameworků vedle sloupce s dotazem a pod ní dotazy s texty ve všech cílech. Důvod, který 137 postrádalo, tedy mají. Vedle toho se ukázala vada, kterou 137 nevidělo, protože se týká přechodu mezi stránkami, ne jedné stránky: navigace v záhlaví je na všech stránkách táž, ale na roztažené stránce stojí u okrajů okna a na neroztažené o stovky pixelů uvnitř, takže při každém přechodu mezi překladem a katalogem poskočí záhlaví i levý okraj textu.

## Zvažované varianty

**(a) Roztáhnout jen stránky katalogů.** Odstraní úzkou tabulku, ale poskakování záhlaví zůstane mezi nimi a rozcestníkem, Advisorem a výukovou stránkou; výběr roztažených stránek by dál byl seznam, který je třeba udržovat s každou novou stránkou.

**(b) Vrátit všechny stránky do kontejneru Pico.** Sjednotí šířku, ale zruší to, kvůli čemu 135 a 137 překladovou obrazovku roztáhly: oddálení stránky by znovu jen rozšiřovalo okraje, místo aby ukázalo víc kódu.

**(c) Roztáhnout každou stránku.** Jedno pravidlo bez výjimky a bez třídy na `<body>`. Námitka 137 proti této variantě — rozcestník a Advisor by získaly jen delší řádky — platí dál pro jejich odstavce, ale 137 samo rozhodlo, že text běží přes celou šířku obsahu a délka řádku se neomezuje; ovládací prvky mají pevnou šířku vlevo a diagram potrubí svou velikost, takže roztažením se na těch stránkách nerozpadne nic.

## Rozhodnutí

> **Záhlaví, obsah i zápatí každé stránky zabírají celou šířku okna, s okrajem `--pico-block-spacing-horizontal` po stranách. Strop šířky kontejneru Pico se neuplatní na žádné stránce a třída `wide` na `<body>` zaniká.**

Volíme (c), protože jako jediná dává všem stránkám tutéž šířku a zároveň zachovává důvod, proč se překladová obrazovka roztáhla. Stejná šířka všech stránek je vlastnost rozhraní jako celku (S7): navigace je jeden prvek, který má při přechodu mezi stránkami zůstat na místě, a čtenář nemá z rozdílné šířky vyvozovat rozdíl v povaze stránek, který neexistuje. Pravidlo bez výčtu stránek navíc platí samo i pro stránku, která teprve vznikne.

## Důsledky

**Z rozhodnutí 137 se nahrazuje jen výběr stránek** — věta „na celou šířku okna se roztahuje překladová obrazovka a nově i výkladová stránka; rozcestník, Advisor a stránka LDBC zůstávají v kontejneru" a k ní odstavec *Která stránka zabírá celé okno*. Text přes celou šířku obsahu, tři tokeny šířky ovládacích prvků, šipka složeného oddílu u popisku a velikost diagramu potrubí platí dál podle 137.

**Mění se jen `app.css` a dvě stránky**, které ztrácejí třídu `wide`. REST kontrakt, server a scénáře S7 beze změny.
