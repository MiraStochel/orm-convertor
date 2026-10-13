# 136 — Archiv projektu rozbalí prohlížeč vlastním kódem: jednotkou se stane soubor v jazyce, který zdroj čte, a ostatní soubory obrazovka vyjmenuje

Datum: 2026-10-13
Stav: platí
Požadavky: F14, S5, S7
Podklad: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) (body a a f), [056](056-work-in-progress-input-stays-in-the-browser.md), [066](066-records-attributed-to-the-input-unit.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md) (varianta 5 a odstavec *Ambice*), [126](126-line-numbers-in-the-unit-editor-by-own-code-not-an-editor-library.md), [135](135-an-example-is-the-optional-first-step-and-the-sample-set-is-six-input-examples.md); [`threat-model.md`](../threat-model.md), hrozby 2 a 3; [`use-cases.md`](../use-cases.md), oddíl „Co je ve skutečnosti vstupem"; schválená specifikace ([`specifikace.tex`](../specifikace.tex)), F14; položka *Vstupní archiv projektu, který F14 připouští* v `open-items.md`

## Kontext

**F14 jmenuje archiv a verze ho nenárokuje.** Požadavek zní „vložení či nahrání jedné nebo více celých tříd, případně archivu projektu" a schválená specifikace k němu slibuje, že scénář zpracuje projekt s alespoň deseti entitami a pěti dotazy **bez ručního zadávání každého artefaktu**. Nástroj dnes bere jen soubory, které uživatel vybere v dialogu nebo vloží do okna, takže archiv musí rozbalit sám a soubory z něj vybrat ručně. `architecture.md` §9 to vede jako zúžení F14 a záruky v kořenovém `README.md` to říkají slovy „no archive".

**Po rozhodnutí 111 chybí jediný krok.** Jednotka je obsah jednoho souboru v jednom jazyce a vstupní hodnoty typu obsahu odpovídají příponám jedna k jedné. Převod souboru na jednotku je tedy funkcí přípony a klient ho pro vybraný soubor dělá už dnes: jméno souboru se stane jménem jednotky, přípona předvyplní jazyk, uživatel obojí vidí a může změnit, a přípona, která jazyk nejmenuje, nechá jazyk nevybraný. Archiv k tomu přidává jen krok „archiv → soubory". Zbývá rozhodnout, kde ten krok proběhne, které soubory se stanou jednotkami, co se stane s ostatními, jak se jednotka z archivu jmenuje a jestli tím zúžení F14 padá.

**Volbu svazují čtyři dřívější rozhodnutí.** Rozhodnutí 032 postavilo frontend bez buildu a bez správce balíčků a v bodě f stanovilo, že schopnost, která by si v prohlížeči vyžádala knihovnu, patří na server. Kvůli tomu dnes ZIP výstupu balí koncový bod `/archive`. Rozhodnutí 056 drží rozpracovaný vstup v `localStorage` prohlížeče. Rozhodnutí 066 udělalo ze jména jednotky popisek, ze kterého server nic neodvozuje. A rozhodnutí 111 zamítlo variantu, ve které by jazyk určovalo jméno souboru, s větou, že převod přípony na jazyk patří do klienta jako předvyplnění, které uživatel vidí a může změnit.

**Prohlížeč umí rozbalovat sám.** Formát ZIP se skládá z centrálního adresáře na konci souboru, z místních hlaviček před daty položek a z dat uložených buď beze změny (metoda 0, *stored*), nebo komprimovaných algoritmem deflate (metoda 8). Ostatní metody se v archivech zdrojového kódu prakticky nevyskytují. Struktura je popsaná ve veřejné specifikaci PKWARE (APPNOTE) a čte se po bajtech. Složitou část, dekompresi deflate, mají současné prohlížeče v sobě: rozhraní `DecompressionStream` ze standardu Compression Streams s formátem `deflate-raw`. Rozbalení v prohlížeči tedy knihovnu nepotřebuje.

## Zvažované varianty

### 1 — Archiv dál nenárokovat

Ponechat zúžení F14 a dát uživateli rozbalit archiv sám. Zamítáme. Po rozhodnutí 111 je archiv nejlevnější část F14, která ještě chybí, a zároveň ta, kterou specifikace pro scénář slibuje výslovně: projekt bez ručního zadávání každého artefaktu. Zúžení, které by stálo jen na tom, že se práce neudělala, by v textu práce nešlo obhájit.

### 2 — Archiv rozbalí server jako nový vstup `/convert`

Server by přijal archiv (multipart, nebo base64 v JSON), rozbalil ho a každou položku převedl na jednotku. Varianta má dvě skutečné výhody. Pravidlo, které položky se stanou jednotkami, by bylo jednou, v C# a pod testy xUnit, a sloužilo by každému klientovi API, i skriptu experimentu podle UC5. A vynechaná položka by mohla být záznamem v odpovědi, tedy strojově čitelným dokladem podle S6.

Zamítáme ji ze čtyř důvodů. **Server by musel odvodit jazyk ze jména položky**, protože archiv jiný nosič jazyka nemá. Je to přesně varianta 5 rozhodnutí 111, zamítnutá proto, že jméno je podle 066 popisek, ze kterého se nic neodvozuje. Obejít by to šlo jen manifestem v archivu, tedy krokem navíc, který žádný skutečný archiv nemá. **Přibyl by vstupní bod se vším, co k němu patří**: binární tělo požadavku, limit velikosti, limit počtu položek a ochrana proti archivu, který se rozbalí do obřích rozměrů. To jsou limity, které model hrozeb u hrozby 2 dosud nenárokuje, a tahle varianta by je udělala nutnými. **Uživatel by obsah archivu uviděl až po převodu**: nemohl by opravit předvyplněný jazyk ani odebrat soubor, který do převodu nepatří, a rozpracovaný vstup podle 056 by nesl binární archiv, ne text oken. **A výhoda testovaného pravidla je tu menší, než vypadá**, protože pravidlo „přípona → jazyk → čte ho zdroj?" klient už má a uplatňuje ho na každý vybraný soubor. Na serveru by existovalo podruhé.

### 3 — Server archiv rozbalí a vrátí jednotky klientovi

Nový koncový bod (`/unpack`) by archiv rozbalil a vrátil seznam jednotek, které klient ukáže a pošle na `/convert` jako dnes. Uživatel by obsah viděl před převodem a čtečka ZIPu by měla testy. Zamítáme. Vstupní bod s limity z varianty 2 zůstává, archiv putuje sítí dvakrát (jednou celý a podruhé jako jednotky) a převod přípony na jazyk by existoval dvakrát: na serveru pro položky archivu a v klientu pro vybrané soubory. Jediné, co varianta proti variantě 5 přináší, jsou automatické testy čtečky formátu, a ta cena za ně je vysoká.

### 4 — Prohlížeč rozbalí archiv vendorovanou knihovnou

JSZip nebo fflate ve `wwwroot/vendor/` s verzí a licencí, stejně jako Pico CSS a highlight.js. Zamítáme. Rozhodnutí 032 f knihovnu ve frontendu připouští jen tam, kde schopnost bez ní nejde. Tady jde: dekompresi dělá prohlížeč a knihovna by přidala jen čtení centrálního adresáře a hlaviček. To je dobře popsaná binární struktura o několika desítkách řádků. Třetí vendorovaná knihovna by nesla vlastní verzi, licenci a důvod, proč se v ní za dva roky nikdo nevyzná, a to za funkci, kterou si přečte kdokoli. Je to tatáž úvaha a tentýž výsledek jako u čísel řádků v rozhodnutí 126.

### 5 — Prohlížeč rozbalí archiv vlastní čtečkou ZIPu nad nativním `DecompressionStream`

## Rozhodnutí

**Volíme variantu 5.**

> **Archiv ZIP, který uživatel přidá na překladové obrazovce, rozbalí prohlížeč vlastním modulem `zip.js`. Ten čte centrální adresář a místní hlavičky, položky uložené beze změny i komprimované algoritmem deflate a rozšíření ZIP64; dekompresi dělá `DecompressionStream` prohlížeče a žádná knihovna nepřibývá. Jednotkou se stane každý soubor z archivu, jehož přípona jmenuje jazyk, který zvolený zdrojový framework čte. Jménem jednotky je cesta v archivu a jazyk se předvyplní podle přípony stejně jako u vybraného souboru. Každý další soubor archivu obrazovka vyjmenuje se svým důvodem ve sbaleném seznamu nad vstupními jednotkami. Server ani REST kontrakt se nemění.**

Argumenty pro tuto variantu a proti ostatním:

1. **Server se nemění, a s ním ani model hrozeb.** Na `/convert` přijdou tytéž jednotky, jako kdyby uživatel tytéž soubory vybral v dialogu. Nepřibývá vstupní bod, binární tělo ani nový limit. Hrozba 2 se nemění svou povahou: nepřátelský klient uživatelské rozhraní nikdy nepotřeboval, poslat tisíc jednotek mohl a může přímo. Archiv jen usnadní poctivý velký vstup, jaký už dnes vznikne výběrem tří set souborů v dialogu. Archiv, který se rozbalí do obřích rozměrů, vyčerpá jen záložku uživatele, který ho přidal, ne proces serveru.
2. **Jazyk zůstává tam, kam ho posadilo rozhodnutí 111.** Přípona předvyplní jazyk v klientu, uživatel ho vidí a může změnit a server ze jména dál nic neodvozuje (066). Serverové rozbalení by tuhle hranici překročilo (varianta 2).
3. **Uživatel vidí, co z archivu vzešlo, ještě před převodem.** Každý soubor dostane okno s předvyplněným jazykem, které jde upravit i odebrat, a soubory, které jednotkou nebyly, stojí nad okny i s důvodem. Krok „nahrát projekt" ze scénáře S7 je tím doslova jeden krok a slib specifikace „bez ručního zadávání každého artefaktu" platí.
4. **Bez knihovny a bez buildu (032 a, f; S5).** Nic, co by zastaralo nebo potřebovalo licenci, nepřibývá. Vlastní kód nemá verzi ani režim údržby a čte se bez znalosti cizího API.
5. **Rozpracovaný vstup funguje beze změny (056).** Jednotky z archivu jsou text jako každé jiné okno a ukládají se stejně. Překročenou kvótu `localStorage` u velkého projektu obrazovka hlásí jako dosud a pracuje dál.

**Které soubory se stanou jednotkami.** Zvažovali jsme tři pravidla:

- (a) *Jednotkou je každý soubor, jehož přípona jmenuje některý jazyk.* Pravidlo je nezávislé na směru převodu, ale archiv projektu EF Core obvykle nese i XML a SQL soubory (dokumentaci, konfiguraci, skripty), které EF Core nečte. Validace před odesláním by pak převod zablokovala kvůli každému z nich a uživatel by je odebíral jeden po druhém, což je přesně ruční práce, kterou má archiv ušetřit.
- (b) *Jednotkou je soubor v jazyce, který zvolený zdroj čte.* Tohle volíme. Pravidlo je totéž, kterým obrazovka označuje okno „not read by" a kterým validace odmítá jednotku v cizím jazyce, jen se uplatní dřív. Výsledek závisí na zdroji zvoleném v okamžiku přidání. Směr stojí na obrazovce nad vstupními soubory a seznam nepoužitých souborů jmenuje framework, který jazyk nečetl, takže závislost je vidět. Kdo zdroj změní až po rozbalení, přidá archiv znovu.
- (c) *Navíc vynechat složky výstupu sestavení a nástrojů podle jména* (`bin`, `obj`, `target`, `.git`, `node_modules`). Zamítáme. Je to odhad o uspořádání projektu a v Javě klame: `target` nebo `bin` může být segment balíčku, a soubor `com/acme/target/Order.java` by tak tiše zmizel. Nástroj projekt nezkoumá (rozhodnutí 111, *Ambice*). Archiv je výběr uživatele i se vším, co do něj zabalil, a ZIP z GitHubu výstup sestavení nenese. Jedinou výjimkou je složka `__MACOSX/`. Ta není součástí projektu, ale vedlejším produktem archivátoru macOS: nese binární soubory `._Order.cs` s toutéž příponou, ze kterých by jinak vznikly nečitelné jednotky. Vynecháme ji jako fakt o původci formátu, ne jako odhad o projektu.

**Proč soubor jednotkou není, obrazovka říká jednou větou.** Důvody jsou: přípona nejmenuje žádný jazyk; zdroj jazyk nečte (věta jmenuje framework i jazyk, stejně jako validace); soubor je prázdný, protože prázdnou jednotku by validace odmítla; metadata macOS; položka je šifrovaná; položka je komprimovaná metodou, kterou prohlížeč neumí (Deflate64, BZip2, LZMA, Zstandard); rozbalení selhalo (věta nese chybu dekomprese). Archiv, který se nedá přečíst vůbec, protože nemá centrální adresář, je rozdělený do více souborů nebo je poškozený, stojí v témž seznamu jako jediný řádek s důvodem. Složky nejsou soubory a do seznamu nepatří.

**Seznam je jednoduchý a dočasný.** Je to sbalený prvek `<details>` nad okny jednotek. Souhrn říká, kolik souborů kterého archivu se nepoužilo, a rozbalený seznam uvádí cestu a důvod po řádcích. Popisuje poslední přidání archivu: další archiv ho nahradí, *Clear* a načtení příkladu ho skryjí. Do `localStorage` se neukládá. Rozhodnutí 056 tam drží vstup, a tohle je zpráva o tom, jak vstup vznikl, ne vstup.

**Jméno jednotky je cesta v archivu**, s oddělovačem `/` a se složkou, kterou archiv začíná (`shop-main/src/Model/Order.cs`). Je to popisek podle 066, takže na jeho tvaru nic nestojí. Dva soubory téhož jména v různých složkách (`Model/Order.cs` a `Dto/Order.cs`) se ale v záznamech dají rozlišit, a to holé jméno souboru neumí. Jméno položky se čte jako UTF-8, a to i tam, kde archivátor příznak UTF-8 nenastavil. Jméno ze zdrojového kódu bývá v ASCII a to se čte stejně v každém kódování. Případná odchylka u jiného jména se týká jen popisku.

**Text souboru se čte stejně jako u vybraného souboru**, týmž voláním `FileReader.readAsText`, takže kódování a značka BOM se chovají v obou cestách shodně.

**Formát je ZIP.** ZIP vydává Průzkumník Windows, Finder, GitHub („Download ZIP"), Visual Studio i IntelliJ. Šifrovanou položku, jinou metodu komprese než *stored* a deflate a archiv rozdělený do více souborů čtečka nečte a říká to. Formáty tar.gz a 7z nenárokujeme. Kdyby se další formát přidával, platí pro něj totéž pravidlo jako zde: jen pokud ho prohlížeč rozbalí sám.

## Důsledky

**REST kontrakt i server zůstávají beze změny.** `/convert` dostává jednotky jako dosud a `/archive` dál balí výstup. Do čísla vydání se změna zařadí při vydání (rozhodnutí 098).

**Zúžení F14 se zmenšuje.** Archiv mizí z výčtu toho, co se nenárokuje, v `architecture.md` §9, v `subset.md`, v `traceability.md` i v zárukách kořenového `README.md`. F14 zůstává nárokované v užším rozsahu kvůli zobrazení mezireprezentace, procházení repozitáře a aplikačnímu kódu bez předání (rozhodnutí 111). Do `subset.md` přibývá vyslovená mez archivu: jiný formát než ZIP, šifrovaná položka, nepodporovaná metoda komprese a soubor v jazyce, který zdroj nečte, se nepoužijí a obrazovka je jmenuje. [`threat-model.md`](../threat-model.md) dostává větu, že rozbalení probíhá v prohlížeči a vstupní bod nepřibývá. Položka z `open-items.md` mizí.

**Cena 1: čtečka nemá automatický test.** Frontend automatické testy nemá (rozhodnutí 032), a nárok je proto nárokem na tvar rozhraní ověřený projitím scénáře, stejně jako u ostatních nároků obrazovky. Selhání čtečky ale neskončí tiše: archiv nebo položka, které nepřečte, stojí v seznamu s důvodem. Pravidlo, které soubory se stanou jednotkami, je přitom totéž, které obrazovka uplatňuje na vybraný soubor, takže mimo testy nepřibývá žádná znalost o frameworcích. Co je v souboru entita a co dotaz, rozhoduje dál server (111).

**Cena 2: klienti API z rozhodnutí nemají nic.** Skript experimentu (UC5) a javová sada rozbalí archiv samy, což je v kterémkoli jazyce pár řádků, a pošlou jednotky jako dosud. Kontrakt je pro ně týž jako pro obrazovku.

**Archiv zviditelní mez rozhodnutí 111 ve větším měřítku.** Aplikační kód, který dotaz sám nepředává (kontroler, služba volající repozitář, DTO, migrace EF Core, `Program.cs` s třídou), se čte jako entita. Archiv celého projektu takových souborů přinese víc než ručně vybrané soubory. Uživatel jejich okna odebere, nebo zabalí jen datovou vrstvu. Archiv nemění, co je jednotka, a vyslovená mez platí, jak je.

**Kód.** Nový modul `wwwroot/js/zip.js` (čtení centrálního adresáře přes řezy `Blob`, takže v paměti jsou jen položky, které se berou). V `translation.js` přidání souborů, které archiv rozbalí, a vykreslení seznamu. V `translation.html` přípona `.zip` ve výběru souborů, seznam nad okny a věta nápovědy. Styl seznamu v `app.css`.

**Co tohle rozhodnutí nerozhoduje:** výběr celé složky v dialogu (`webkitdirectory`), procházení repozitáře, rozbalování na serveru pro klienty API, jiné formáty archivu a obrazovku Advisoru, která soubory nenahrává.
