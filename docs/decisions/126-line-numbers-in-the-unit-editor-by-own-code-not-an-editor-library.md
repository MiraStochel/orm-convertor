# 126 — Čísla řádků v editoru jednotky vlastním kódem, ne hotovým editorem

Datum: 2026-10-11
Stav: platí
Požadavky: S5, S7
Podklad: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) (bod f: další vendorovaná knihovna je samostatné rozhodnutí), [033](033-shape-of-the-static-frontend-screens.md) (validace XML s číslem řádku), [066](066-records-attributed-to-the-input-unit.md), [093](093-unreadable-input-is-a-unit-failure.md) (jedna věta s řádkem a sloupcem pro nečitelný vstup), [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md); položka *Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška* v `open-items.md`

## Kontext

S7 žádá zvýraznění chyb na úrovni souboru a řádku. Soubor je hotový: nečitelná jednotka je od rozhodnutí 093 záznam připsaný té jednotce, validace na klientovi ukazuje chybu pod jednotkou, u které vznikla. Řádek chybí. Hlášky ho nesou — `DOMParser` u XML na klientovi, server u XML, SQL, Javy, HQL a JPQL větou „at line *L*, column *C*" —, ale editor jednotky je holý `<textarea>` bez čísel řádků, takže uživatel řádek z věty přečte a pak ho v textu počítá sám. Advisor má tytéž editory a touž vadu.

Obrazovka „co bylo odesláno" vedle výstupu má od rozhodnutí 125 pozice tvrzení, ale jako podtržení nad `pre > code` podle offsetu; editor to není a řádky nečísluje. Čísla řádků jsou tedy práce v editoru, a otázka zní, zda ji dělat vlastním kódem, nebo jestli je to chvíle, kdy se vyplatí hotový editor jako třetí vendorovaná knihovna vedle Pico CSS a highlight.js.

Co by hotový editor přinesl navíc, je třeba vyslovit poctivě: označení chyby **v textu**, i na sloupec, podtržení pozic tvrzení přímo v editovatelném vstupu místo jen v odeslané kopii, a pohodlí při psaní (odsazení, závorky, obarvení při psaní). Nic z toho žádný požadavek nežádá; S7 říká soubor a řádek.

## Zvažované varianty

1. **Vlastní postranní sloupec čísel řádků u `<textarea>`.** Několik desítek řádků JavaScriptu a CSS: sloupec s čísly, který se posouvá s textovým polem, pole bez zalamování, aby řádek textu byl řádek sloupce, a označený řádek, na který se dá skočit. Žádná knihovna, žádné rozhodnutí podle 032 f. Neumí označit rozsah uvnitř textu — `<textarea>` nemá dekorace — a sloupec tedy zůstává ve větě, ne v označení.

2. **CodeMirror 5 (5.65.x).** Jeden soubor JavaScriptu, jeden CSS, módy `clike`, `sql`, `xml`, doplněk `lint` s podtržením na řádku a sloupci; `fromTextArea` zachová formulářové chování. Vendoruje se stejně jako highlight.js. Od roku 2022 je v režimu údržby a dostává jen opravy; pro artefakt práce, který se po odevzdání nemění, je to neškodné, v textu práce by to ale bylo potřeba říct. Slabší přístupnost, zhruba 250 kB.

3. **Ace (ace-builds 1.4x).** Jeden soubor plus soubory módů a motivů, BSD, udržovaný; `setAnnotations` bere řádek, sloupec, text a druh — přesně S7. Módy a motivy nahrává vlastním zavaděčem, světlý a tmavý motiv by musely sledovat `theme.js` zvlášť, zhruba 500 kB s módy.

4. **CodeMirror 6.** Nejlepší editor z nabídky, nativní ES moduly, `@codemirror/lint` je přesně to, co S7 slovy žádá. Distribuuje se jako patnáct balíčků npm bez oficiálního jednosouborového sestavení. Vendorovat ho znamená buď ho jednou sestavit jinde — tedy mít sestavený artefakt, který se může rozejít se zdrojem, což je přesně vada, proti které bylo napsáno rozhodnutí 032 —, nebo ho načítat z CDN, což ruší „instance běží bez sítě". Zamítáme bez ohledu na kvalitu; právě kvalita je to, co by sváděla build zavést znovu.

5. **Monaco.** Několik megabajtů, web workery, zavaděč AMD nebo bundler. Editor VS Code pro nástroj o pěti dokumentech je opak úvahy o přiměřenosti z rozhodnutí 032. Zamítáme.

Pro varianty 2 až 4 platí společně: editor by nahradil `<textarea>` na dvou obrazovkách ve třech šablonách, s vazbou hodnoty, událostí `input`, chybou pod jednotkou a rozpracovaným stavem z rozhodnutí 056 k přepsání; vzhled by se udržoval na druhém místě vedle Pico; a hlavně **žádná knihovna neumí použít, co server posílá**. Záznam nese důvod jako větu, ne řádek a sloupec jako pole. Než editor cokoli označí, musí klient pozici z věty přečíst — jako to dnes dělá u `DOMParser` — nebo by záznam musel pozici nést strukturovaně. Ta práce je stejně velká pro sloupec vlastní i pro knihovnu, a je to ta část, na které S7 doopravdy stojí.

## Rozhodnutí

**Volíme variantu 1.**

> **Editor jednotky je `<textarea>` s postranním sloupcem čísel řádků, psaným vlastním kódem. Chyba, která nese řádek, ten řádek ve sloupci označí a nabídne na něj skok. Žádná knihovna editoru nepřibývá.**

1. **Přiměřenost (rozhodnutí 032).** Rozhraní je plocha, na které se ukazuje, co umí překladač; každá vrstva, kterou by bylo potřeba vysvětlit, je vrstva navíc. S7 říká soubor a řádek, a soubor s řádkem dá sloupec čísel za několik desítek řádků kódu, které si za dva roky přečte kdokoli. Editor by přinesl to, co nikdo nežádá, za cenu třetí knihovny, druhého místa pro vzhled a přepisu tří šablon.

2. **Trvanlivost (S5).** Vlastní kód nemá verzi, která by zastarala, ani režim údržby, který by bylo třeba v textu práce obhajovat. Z knihoven, které by 032 připustilo, je jedna ve stavu „jen opravy" a druhá nese vlastní zavaděč; ta nejlepší z nabídky se bez buildu vendorovat nedá.

3. **Pozice se čte z věty, protože věta je jedna.** Rozhodnutí 093 dalo nečitelnému vstupu ve všech jazycích jeden tvar důvodu, „at line *L*, column *C*", a to právě proto, aby ho stroj uměl přečíst. Klient ho čte týmž výrazem u validace XML (`DOMParser` v Chromu i Firefoxu píše řádek a sloupec svými slovy, obojí výraz zná) a u záznamu připsaného jednotce (rozhodnutí 066). Strukturované pole v záznamu by bylo druhé místo pro týž fakt; dokud věta drží tvar, není potřeba.

Konkrétně:

- **Sloupec** (`attachLineNumbers` v `ui.js`) obalí `<textarea>` mřížkou se sloupcem čísel; pole nezalamuje (`wrap="off"`), takže řádek textu je řádek sloupce; sloupec se posouvá s polem a dorovnává jeho vodorovný posuvník; přepočítá se při každé změně textu. Sloupec je pro čtečku skrytý — přístupná cesta ke skoku je tlačítko ve větě pod jednotkou.
- **Označení** (`markLine`): číslo řádku dostane třídu a titulek s důvodem; kliknutí na něj i tlačítko *go to line L* ve větě pod jednotkou vyberou celý řádek v poli a posunou ho do zorného pole. Sloupec, nese-li ho věta, zůstává ve větě.
- **Odkud řádek je.** Z validace před odesláním (XML přes `DOMParser`) a po převodu ze záznamu, který jmenuje jednotku a jehož důvod nese pozici: záznam se připíše jednotce téhož odkazu (jméno, jinak *unit N* po pořadí, rozhodnutí 066), věta se ukáže pod ní a řádek se označí. Selhání celého požadavku pod tlačítkem *Convert* jednotku nejmenuje a řádek se z něj nečte.
- **Obě obrazovky.** Advisor dostane sloupec čísel u svých editorů; označení ne, protože jeho odpověď žádný záznam s pozicí nenese.

## Důsledky

**Položka *Editor jednotky nemá čísla řádků* z `open-items.md` mizí** a s ní sekce *Rozhraní*, která nesla jen ji.

**S7 v §9 a v `traceability.md`:** zvýraznění chyby na úrovni souboru a řádku je od teď v editoru, ne jen ve větě, pro každý jazyk, jehož věta řádek nese — tedy pro všechny kromě C#, kde pozici parser nevydává (otevřená položka beze změny). Validace na klientovi zůstává pomocníkem, ne bránou (rozhodnutí 033).

**Tvar věty z rozhodnutí 093 je od teď kontrakt, který čte i klient.** Změna slov „at line *L*, column *C*" by označení v editoru potichu zrušila; testy nečitelného vstupu (`UnreadableXmlTest`, `NestingDepthCapTest`) tvar věty drží a jsou místem, kde by se taková změna projevila.

**Rozhodnutí 032 platí beze změny:** vendorované knihovny zůstávají dvě. Kdyby někdy bylo důvodem pro editor označení na sloupec v textu nebo podtržení pozic tvrzení v editovatelném vstupu, je to nové rozhodnutí a jeho jediný tvar slučitelný s 032 je jednosouborová knihovna (Ace nebo CodeMirror 5); CodeMirror 6 by v něm měl být odmítnut výslovně, protože jeho kvalita je přesně to, co by sváděla build zavést znovu.

**Co tohle rozhodnutí nerozhoduje:** pozici u chyby C# (otevřená položka), strukturované pole pozice v záznamu, zobrazení mezireprezentace.
