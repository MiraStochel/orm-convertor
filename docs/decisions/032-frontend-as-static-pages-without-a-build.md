# 032 — Frontend jako statické stránky bez buildu

Datum: 2026-08-20
Stav: platí
Požadavky: F11, F14, S5, S7
Podklad: rozhodnutí [003](003-one-shot-migration.md), [010](010-diagnostics-as-returned-data.md), [025](025-query-language-as-content-type.md) a [030](030-scope-of-version-1-0.md); položky „Frontend zaostal za API a nevaliduje vstup" a „Osud `wwwroot`" v `open-items.md`

## Kontext

Rozhodnutí [030](030-scope-of-version-1-0.md) postavilo přepracovaný frontend do verze 1.0 jako položku 13 a řeklo o něm jediné: že bude. Čím se postaví, zůstalo otevřené. V repozitáři o té volbě není řádka — dnešní podoba je zděděná z původního prototypu a nikdo ji nikdy nezvolil, jen ji nepřepsal.

**Co dnes je.** Angular 19.2, tři obrazovky (`""`, `translation`, `advisor`) a jedno přesměrování, pět komponent, služba nad `HttpClient` a zrcadlo API typů v `src/app/model/`. Globální styl je jediné pravidlo pro `body`: písmo Arial a vnitřní okraje. Kód se zobrazuje i zadává v `<textarea>`, který se dopočítává na výšku obsahu. `package.json` deklaruje patnáct balíčků, `package-lock.json` jich uzamyká tisíc a tři. Přeložený výsledek je commitnutý ve `wwwroot` — 293 kB v `main-JKIRXLUR.js` ze 16. července.

**Co je na tom rozbité.** `ConvertResponse` na serveru má dnes devět polí: identifikátor běhu a zdrojový i cílový framework s verzí (rozhodnutí [013](013-target-framework-versions.md), požadavek S6), artefakty, pole `Records` se strukturovanou diagnostikou (rozhodnutí [010](010-diagnostics-as-returned-data.md)), stav připojení ke katalogu a čas doplňovací fáze měřený odděleně podle S3 (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)). Frontendový model `ConvertResponse` má pole jediné, `sources`. Diagnostika se tedy nezobrazuje ne proto, že by se ji zapomnělo vykreslit, ale proto, že o ní rozhraní vůbec neví — a totéž platí o všem ostatním, co server od té doby začal vracet. Chyby ze serveru čte `main-page.component.ts` z `err.message` místo `err.error`, takže místo hlášky ze serveru — `ConvertHandler` vrací `Results.BadRequest(e.Message)`, tedy holý řetězec — ukáže obecný text HTTP chyby.

A pak je tu doklad, který stojí za všechny úvahy o riziku. Soubor `content-type.ts` byl 19. srpna doplněn o hodnoty výčtu podle rozhodnutí [025](025-query-language-as-content-type.md) a je správně; `main-JKIRXLUR.js` ve `wwwroot` je ze 16. července. **Zdroj je opravený a nasazený artefakt ne.** Není to zanedbání jednoho místa, nýbrž popis toho, jak dvojice „build krok plus commitnutý výsledek" stárne: rozejít se může kdykoli, opravit zdroj nestačí, a pozná se to až tím, že nasazená stránka tvrdí něco jiného než server. Otevřená položka „Osud `wwwroot`" tuhle vadu pojmenovává a nabízí dvě východiska — build zautomatizovat, nebo `wwwroot` z gitu odstranit. Obě předpokládají, že build existuje.

**Co po rozhraní chce verze 1.0.** Požadavek F14 žádá nahrání jedné nebo více celých tříd, volbu zdrojového a cílového frameworku a zobrazení vstupu, výstupu a diagnostiky po jednotlivých souborech. F11 žádá, aby se strukturovaná diagnostika dostala k uživateli, tedy aby se záznamy z rozhodnutí [010](010-diagnostics-as-returned-data.md) vykreslily. S7 žádá validaci před spuštěním, průběžný stav, zvýraznění chyb na úrovni souboru a řádku, stažení kompletního výstupu a základní scénář nejvýše v pěti krocích. K tomu přistupuje stav připojení ke katalogu z položky 12. Je to práce na celém rozhraní, ne záplata — a proto je to zároveň poslední chvíle, kdy se dá technologie zvolit levně.

**Co po něm chceme nad rámec požadavků.** Dvě věci, a je poctivé říct rovnou, že ani jednu nežádá žádné číslo z F/S/T. První je vzhled: rozhraní má být moderní a responzivní, ne jen funkční. Druhá je výkladová část — příklady, které ukazují, jak překlad probíhá, doprovázené obrázky. Obojí plyne z toho, čím verze 1.0 podle rozhodnutí [030](030-scope-of-version-1-0.md) je: instancí, kterou uvidí vedoucí a kterou budeme v textu práce popisovat čtenáři, jenž ji nikdy nespustí. Nástroj, který umí překládat mezi třemi ORM a neumí to na sobě ukázat, se špatně obhajuje. Do volby technologie to patří proto, že výkladová část je obsahově něco úplně jiného než nástrojová obrazovka, a ne každá technologie ji nese stejně dobře.

**Dvě okolnosti, které volbu rozhodují.**

Frontend není předmětem téhle práce. Je to plocha, na které se ukazuje, co překladač umí. Jeho hodnota je v tom, že po odevzdání půjde spustit a že ho v textu práce popíšeme. Tomu odpovídá jiná míra technologie než produktu s týmem a s CI: každá vrstva, kterou by bylo potřeba vysvětlit, je vrstva navíc.

Nasazení je jediná ručně spravovaná instance (rozhodnutí [030](030-scope-of-version-1-0.md)). V takovém uspořádání je každý build krok ruční krok, který někdo provede na stroji, jehož stav neřídíme, a jehož vynechání se pozná až z chování nasazené stránky. Přesně to se už jednou stalo.

## Zvažované varianty

1. **Zůstat u Angularu a dohnat ho.** Zdánlivě nejlevnější: doplnit modely o zbylých osm polí, opravit čtení chyby, přidat obrazovky pro dávkový vstup. Zamítáme, a ne kvůli vkusu.

   Nejprve je třeba opravit úvahu, která tuhle variantu obvykle drží — že framework umožní udělat stránku hezčí. Neumožní. Vzhled dělá HTML a CSS a ty jsou v každé variantě tytéž; Angular renderuje týž DOM a aplikuje totéž CSS a nemá jediný vizuální prostředek navíc, protože žádný neexistuje. Co nabízí, je **knihovna komponent** — Material nebo PrimeNG — s hotovým designovým systémem, a to je výhoda skutečná: stepper pro pětikrokový scénář z S7, tabulka se řazením, dialog, upload. Jenže tu dnes nemáme. Současný frontend nemá Material ani PrimeNG a globální styl má jedno pravidlo. „Zůstat u Angularu a udělat to hezké" tedy neznamená pokračovat, nýbrž **postavit rozhraní znovu, uvnitř Angularu, a přibrat k tomu velkou novou závislost a cizí designový systém.** Srovnání pak nezní „hotový Angular proti novému statickému webu", ale „nový Angular plus knihovna komponent proti novým statickým stránkám plus CSS", a v tom srovnání je Angular dražší, ne levnější.

   Zbytek argumentu je týž jako dosud. Z toho, co Angular umí — vstřikování závislostí, detekce změn, RxJS, router, AOT překlad —, tenhle frontend nepoužívá nic, co by prostý dokument neuměl také; platí se za to tisícem balíčků v uzamčeném souboru, uzlem Node v cestě k nasazení a nutností překládat po každé změně. Položka „Osud `wwwroot`" existuje jedině kvůli téhle konstrukci. A závislost té velikosti je pro artefakt diplomové práce riziko na roky dopředu: sestavení, které se dnes podaří, se za dva roky nemusí podařit vůbec — což je přesně opak toho, oč usiluje S5.

   Přitěžující okolností je výkladová část. Stránka s prózou, nadpisy, obrázkem a dvěma sloupci kódu je nejstatičtější obsah, jaký existuje, a pro jednostránkovou aplikaci je to nejhorší případ: text se buď vepíše do TypeScriptových šablon, kde se špatně píše i čte, nebo se dotáhne knihovna na markdown a s ní další závislost. Obrázky se z assetů kopírují buildem, tedy přes týž mechanismus, který do bundlu vpisuje nasazovací cestu ručním přepínačem `--base-href`/`--deploy-url`.

   Namítnout se dá jediné: TypeScript by hlídal tvar DTO. Hlídal by ale shodu s ručně psaným zrcadlem, ne se serverem — a právě to zrcadlo je dnes osm polí pozadu, aniž by to překlad zachytil. Typová kontrola proti nesprávnému zrcadlu je sebekontrola, ne kontrakt.

2. **Vyměnit Angular za menší framework s buildem (Svelte, Vue, Preact).** Levnější varianta téhož druhu: pořád npm, pořád build krok, pořád artefakt ve `wwwroot`, který se může rozejít se zdroji. Snižuje cenu, nemění povahu vady. Zamítáme.

3. **Serverem vykreslené rozhraní (Razor Pages, případně htmx nad fragmenty HTML).** Lákavé tím, že by z projektu zmizel JavaScriptový ekosystém úplně a validace by mohla být tatáž jako serverová. Zamítáme ze tří důvodů. Rozhraní je klientem REST API — tak je nakreslené i v článku, kde „User Interface (Frontend)" stojí jako vlastní pruh nad API —, a serverové vykreslení by k dokumentovanému JSON API přidalo druhou, HTML vracející sadu koncových bodů pro tytéž operace. Interakce je navíc klientská: několik editovatelných panelů, přidávání a ubírání souborů, diagnostika u řádku; celostránkové odeslání je pro to špatný tvar a htmx by byl další závislost. A konečně by se tím do `ORMConvertorAPI` přesunula podstatná část uživatelského rozhraní, kterou dnes drží mimo.

4. **Vanilla JS s malou knihovnou bez buildu (Alpine.js, lit-html, htm + Preact).** Zachovává „bez buildu" a přidává deklarativní vykreslování, které se hodí právě na dynamické seznamy z F14. Zamítáme: knihovna, která nepotřebuje build, pořád přináší vlastní idiomy, vlastní verzování a vlastní důvod, proč se v ní za rok nikdo nevyzná — a množství vykreslování je tu malé (čtyři dokumenty, desítky souborů, překreslení na akci uživatele, ne průběžně). Přínos je menší než cena vysvětlování.

5. **Statické stránky: HTML, nativní ES moduly a vendorované CSS.**

## Rozhodnutí

**Volíme variantu 5:**

> **Frontend verze 1.0 je sada statických souborů, které prohlížeč spustí přesně tak, jak leží v repozitáři. Žádný framework, žádný balíčkovací nástroj, žádný build krok: `wwwroot` přestává být výstupem překladu a stává se zdrojem.**

Volba má šest částí.

**a) Bez buildu.** HTML, CSS a `*.js` jako nativní ES moduly (`<script type="module">`). Co je v gitu, to se servíruje; nasazení je zkopírování souborů. Tím se ruší celá třída vad, do které patří dnešní rozpor mezi bundlem a serverem, a s ní i předpoklad položky „Osud `wwwroot`". Dosavadní složka `frontend/` se smaže naráz, ne postupně (rozhodnutí [003](003-one-shot-migration.md)).

**b) Bez frameworku, s `<template>` a překreslením oblasti.** Stav obrazovky je jeden obyčejný objekt; každá obrazovka má vykreslovací funkce, které z klonovaných `<template>` prvků sestaví celou oblast znovu. Žádné porovnávání stromů, žádná reaktivita, žádný stavový automat. Je to obhajitelné právě tady: objem dat je desítky souborů a překresluje se na akci uživatele, takže úplné překreslení oblasti je levnější než jakýkoli mechanismus, který by mu předcházel.

**c) Čtyři samostatné dokumenty místo klientského routeru.** `index.html`, `translation.html`, `advisor.html` a `examples.html`, každý se svým modulem. Zpětné tlačítko, hluboké odkazy i obnovení stránky pak fungují bez jediného našeho řádku a odpadá i záložní pravidlo `MapFallbackToFile` v `Program.cs`, které existuje kvůli klientskému směrování. Všechny adresy uvnitř stránek jsou relativní, takže nasazovací cestu `/orm` nenese žádný soubor — dnes ji do bundlu vpisuje ruční přepínač `--base-href`/`--deploy-url`.

**d) Vzhled: vendorovaná bezetřídní knihovna Pico CSS 2.1.1 (MIT) jako základ a vlastní vrstva tokenů nad ní.**

Nejdřív to, co je potřeba vyslovit, protože se to jinak schová za volbu technologie: **vzhled je naše práce v CSS, ne vlastnost knihovny.** Žádná volba ji neudělá za nás. Angular by ji jen skryl za cizí designový systém a zaplatil za to tím, že by výsledek vypadal jako Material, ne jako tenhle nástroj. Rozdělení odpovědnosti je proto tohle: **knihovna nese nudný základ, rozpoznatelnou podobu nese `app.css`.**

Nudný základ je typografická škála, formulářové prvky ve všech stavech (fokus, zakázáno, neplatné), rytmus tabulek a světlý i tmavý motiv přes `color-scheme`. Je to velké množství úmorného CSS, které nemá cenu psát znovu, a Pico ho pokrývá — bez jediného řádku JavaScriptu. Bezetřídní je proto, že se pak značkuje sémanticky (`form`, `label`, `select`, `textarea`, `table`, `details`, `article`, `figure`) a knihovna nepronikne do značek; její výměna nebo odstranění je zásah do jednoho souboru, ne do všech stránek. U výkladové části se tahle vlastnost vyplácí dvakrát: próza označkovaná nadpisy, odstavci a `figure` je nastylovaná, aniž bychom napsali cokoli. Bootstrap by znamenal třídy v každém prvku ručně psaného HTML a vlastní JS pro komponenty; Tailwind potřebuje build, což je spor s bodem (a).

Vlastní vrstva je `app.css` a nese tři věci. **Tokeny** — barvy, poloměry, stíny, škálu odsazení — zapsané jako vlastní vlastnosti, které přebíjejí `--pico-*`; Pico 2 je takhle navržené konfigurovat, takže to není boj s knihovnou, nýbrž její zamýšlené použití. **Rozvržení** aplikace: dva panely vedle sebe, seznam souborů, pás záznamů pod výstupem. To knihovna neumí a umět nemá, a neumí to ani žádný framework. **Responzivitu**, postavenou na CSS gridu a container queries — panely vedle sebe na širokém displeji, pod sebou na úzkém, podle šířky kontejneru, ne podle šířky okna. Angular Material řeší breakpointy JavaScriptem přes `@angular/cdk/layout`, tedy víc mechaniky pro týž výsledek.

Ke čtecím panelům přidáváme zvýrazňovač syntaxe highlight.js 11.12.0 (BSD-3-Clause) s gramatikami pro C#, XML a SQL. HQL vlastní gramatiku nemá a zvýrazní se tou pro SQL; je to přiblížení, ne tvrzení o jazyce — typy obsahu odlišilo rozhodnutí [025](025-query-language-as-content-type.md) a zvýraznění je kosmetika.

Obě knihovny se vendorují, ne načítají z CDN. Soubory leží ve `wwwroot/vendor/` i s číslem verze a licencí, takže instance funguje bez sítě, její vzhled nezávisí na dostupnosti cizí služby a verze je zafixovaná stejně jako ostatní verze v tabulce v `architecture.md`, §1.

**e) Výkladová část jako čtvrtý dokument, se dvěma pravidly na obsah.**

`examples.html` ukazuje, jak překlad probíhá: co je vstup, co z něj vznikne v mezireprezentaci, co vydá cílový builder a proč některá tvrzení skončí záznamem místo v artefaktu. Ve zvolené variantě je to nejlevnější věc na seznamu — nový soubor HTML a nic víc —, a je to zároveň důvod, proč tahle varianta vyhrála nad frameworkem.

**Příklad je živý, ne snímek obrazovky.** API má `/samples`, které vrací vzorky, a `/convert`, které je přeloží. Stránka tedy vzorek načte, pošle ho na `/convert` a ukáže skutečný výstup vedle skutečného vstupu i se skutečnými záznamy. Takový příklad se s nástrojem nemůže rozejít, protože je jím vyroben — kdežto snímek obrazovky zastará při první změně rozvržení a nikdo se to nedozví. Je to tatáž úvaha, kterou tohle rozhodnutí vede proti commitnutému bundlu, jen aplikovaná na dokumentaci.

**Obrázky jsou diagramy procesu, ne snímky rozhraní.** Schéma `parse → doplnění z katalogu → build` stárne řádově pomaleji než jakákoli obrazovka a je to zároveň ten obrázek, který v textu práce stejně musí být. Zdroje jsou v `diagrams/` (draw.io), formátem je SVG vložené přímo do dokumentu: ostré v každém rozlišení, malé, verzovatelné jako text a bez dalšího souboru ke stažení. **Snímky vlastního rozhraní do repozitáře nedáváme** — je to obrázek, který nikdo nepřekreslí, až se rozhraní změní.

**f) Pravidlo pro další závislosti.** Do frontendu nepřibude nic, co by potřebovalo build krok nebo správce balíčků. Když si nějaká schopnost takovou knihovnu vyžádá, patří na server — tam správce balíčků i fixaci verzí projekt už má. Nejbližší případ je stažení kompletního výstupu podle S7: sbalit soubory umí koncový bod v `ORMConvertorAPI` jedním voláním standardní knihovny, kdežto v prohlížeči by to znamenalo další vendorovanou knihovnu.

**Tvar API zná jediný modul.** Cesty koncových bodů a tvary DTO — včetně všech devíti polí `ConvertResponse` a zrcadel výčtů `ORMEnum` a `ConversionContentType` — bydlí v jednom `api.js`. Je to táž úvaha jako u `DatabaseCatalog` v rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md): fakt, který se může rozejít s jiným místem, má mít jedinou adresu. Právě rozejití API a rozhraní je obsahem položky 13 a tohle je ta část odpovědi, která se dá udělat konstrukcí.

Předpokladem je současný prohlížeč — ES moduly, `fetch`, `<template>`, `<dialog>`, container queries. Nepřekládá se do starších verzí jazyka a nedoplňují se polyfilly; instanci používáme my a vedoucí, takže je to předpoklad, který stačí vyslovit.

## Důsledky

**Položka „Osud `wwwroot`" se nestává bezpředmětnou, mění se její obsah.** V číslovaném pořadí verze 1.0 zůstává jako 14 a rozhodnutí [030](030-scope-of-version-1-0.md) se nemění. Volba mezi automatizací buildu a odstraněním `wwwroot` z gitu ale odpadá — obojí odpovídalo na otázku, co s výstupem překladu, a ten už nevznikne. Zbývá odstranit starý bundle a složku `frontend/` a popsat nové uspořádání.

**Zákaz čtení `wwwroot` v `CLAUDE.md` a v `.claude/settings.local.json` padá.** Existuje proto, že `wwwroot` byl commitnutý build; jakmile jsou to ručně psané zdroje, je to nejdůležitější složka frontendu. Vypustí se spolu s pravidlem pro `frontend/dist/`, které přestane mít předmět. Je to změna pravidla, takže se provede v `CLAUDE.md`, až podle tohoto rozhodnutí vznikne kód.

**`Dockerfile` přijde o uzel Node.** Vícestupňové sestavení dnes staví frontend Angularem; nově je to kopie souborů. Kontejnerové nasazení je mimo záruky verze 1.0 (rozhodnutí [030](030-scope-of-version-1-0.md)), ale soubor v repozitáři zůstává a stavěl by něco, co neexistuje.

**`architecture.md` se dotkne na dvou místech**, až podle tohoto rozhodnutí vznikne kód: §1 popisuje ASP.NET projekt jako toho, kdo „servíruje zkompilovaný Angular frontend", a §6 uvádí postup `npm install` a `ng build` s ručním kopírováním do `wwwroot`. Obojí se nahradí popisem statických stránek; verze vendorovaných knihoven patří do tabulky zafixovaných verzí v §1.

**Vizuální kvalita se stává naší prací a je potřeba s ní počítat jako s prací.** Volba bez frameworku nezhoršuje výsledek, ale ani ho nedodá zadarmo: `app.css` ponese tokeny, rozvržení a responzivitu a bude to nejobjemnější soubor frontendu. Je to vědomá výměna za to, že rozhraní nebude vypadat jako cizí designový systém a nebude na něm záviset. Kdyby se ukázalo, že je ta práce nad síly zbývajícího času, není odpovědí návrat k frameworku — knihovna komponent by se dala vendorovat také —, nýbrž skromnější `app.css`.

**Automatické testy frontend mít nebude, a je to vyslovená hranice.** Testovací běh v prohlížeči by znamenal správce balíčků a s ním spor s bodem (f). Kontrakt API je testovaný na serveru, kde také bydlí; rozhraní se ověřuje projitím základního scénáře podle S7. Nárok verze na S7 je tím nárokem na tvar rozhraní, ne na jeho automatické ověření — a tohle je to místo, kde se to říká nahlas, jak žádá kritérium hotovosti z rozhodnutí [030](030-scope-of-version-1-0.md).

**Výkladová část nezakládá nárok na žádný požadavek.** Není v F/S/T a tvrdit se o ní bude jen to, že existuje. Do rozsahu verze ji staví tohle rozhodnutí, ne požadavek, a v textu práce patří do popisu nástroje, ne do vyhodnocení.

**Co tohle rozhodnutí nerozhoduje.** Podobu obrazovek, tedy co je na kterém kroku vidět, jak se skládá dávkový vstup podle F14, co přesně validujeme před odesláním, jak se seskupí záznamy k souboru a řádku, kde se ukáže stav připojení ke katalogu a které vzorky výkladová stránka ukáže. To jsou zásady uživatelského rozhraní, ne volba technologie, a patří do samostatného rozhodnutí sepsaného spolu s implementací položky 13.
