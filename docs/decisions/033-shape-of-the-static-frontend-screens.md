# 033 — Podoba obrazovek statického frontendu

Datum: 2026-08-20
Stav: platí
Požadavky: F11, F14, S6, S7
Podklad: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md), [010](010-diagnostics-as-returned-data.md), [013](013-target-framework-versions.md), [025](025-query-language-as-content-type.md), [029](029-database-connection-is-the-consumer-projects-fact.md) a [030](030-scope-of-version-1-0.md); položka „Frontend zaostal za API a nevaliduje vstup" v `open-items.md`

## Kontext

Rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) zvolilo technologii — statické stránky bez buildu, čtyři dokumenty, Pico CSS a highlight.js vendorované ve `wwwroot` — a výslovně nechalo otevřenou podobu obrazovek: co je na kterém kroku vidět, jak se skládá dávkový vstup podle F14, co přesně se validuje před odesláním, jak se seskupí diagnostické záznamy, kde se ukáže stav připojení ke katalogu a které vzorky výkladová stránka ukáže. Tohle rozhodnutí ty otázky uzavírá; píše se spolu s implementací položky 13, jak 032 žádá.

Vstupy jsou dané. `/convert` bere zdrojový a cílový framework a plochý seznam jednotek `(typ obsahu, obsah)`; typ obsahu nese jazyk jednotky (rozhodnutí [025](025-query-language-as-content-type.md)) a `/required-content` říká, které typy který zdrojový framework čte. Odpověď má devět polí: identifikátor běhu, zdrojový i cílový framework s verzí z deskriptoru (S6, rozhodnutí [013](013-target-framework-versions.md)), artefakty, záznamy podle rozhodnutí [010](010-diagnostics-as-returned-data.md), stav připojení ke katalogu a odděleně měřený čas doplňovací fáze. Záznam nese druh, entitu, vlastnost, artefakt, kategorii mapovacího faktu nebo dotazovou schopnost a důvod — **entitu, ne zdrojový soubor**; server soubory nezná, protože jednotky žádné jméno nenesou. S7 žádá validaci před spuštěním, průběžný stav, zvýraznění chyb na úrovni souboru a řádku, stažení kompletního výstupu a základní scénář nejvýše v pěti krocích.

## Zvažované varianty

U celku není co větvit — obrazovky plynou z požadavků a z tvaru API. Zvažovalo se jediné podstatné rozcestí: **průvodce (wizard), který kroky scénáře vodí po jednom, proti jedné stránce, kde jsou všechny kroky vidět najednou.** Průvodce zamítáme: pět kroků S7 se vejde na jednu obrazovku, uživatel se při opravě chyby vrací ke vstupu bez přepínání, a průvodce by potřeboval stav „kde v průvodci jsem", tedy přesně ten druh mechaniky, kterou rozhodnutí 032 volbou bez frameworku odmítlo platit. Druhé menší rozcestí bylo posílat každý soubor zvláštním voláním `/convert`, aby výstup a záznamy přišly po souborech samy od sebe; to zamítáme, protože by se rozpadlo mezientitní rozresolvování — vztahy odkazují entity jménem napříč soubory (rozhodnutí [001](001-entity-reference-by-name.md)) a dvojice entita + XML mapování u NHibernate patří do jednoho převodu.

## Rozhodnutí

**Čtyři dokumenty mají tyto role:** `index.html` je rozcestník — říká, co nástroj dělá, a vede na tři ostatní; `translation.html` je nástrojová obrazovka překladu; `advisor.html` nese Advisor v dosavadním rozsahu; `examples.html` je výkladová část podle 032e. Navigace je společná hlavička se čtyřmi odkazy; adresy jsou relativní, žádná stránka nezná nasazovací cestu.

**Překladová obrazovka je jedna stránka a základní scénář má přesně pět kroků shora dolů (S7):** volba zdrojového frameworku, volba cílového frameworku, složení vstupních souborů, tlačítko převodu, čtení výsledku. Kroky jsou očíslované sekce viditelné najednou, ne průvodce. Průběžný stav podle S7 je zablokované tlačítko s indikátorem běhu po dobu volání.

**Dávkový vstup podle F14 je seznam pojmenovaných jednotek.** Jednotka má jméno, typ obsahu a obsah. Vzniká trojím způsobem: nahráním jednoho či více celých souborů (jméno a obsah dá soubor, typ se předvyplní podle přípony — `.cs` entita, `.xml` mapování, `.sql` SQL dotaz, `.hql` HQL dotaz — a dá se změnit), přidáním prázdné jednotky a vepsáním, nebo naplněním vzorky přes `/samples`. Nabídka typů je omezená na to, co zdrojový framework podle `/required-content` čte; u `.cs` rozhoduje uživatel mezi entitou a dotazem, protože přípona to nerozliší. Jednotky lze odebírat a je jich libovolně mnoho. Odesílá se vše jedním voláním `/convert`; jméno jednotky zůstává na klientovi, protože server jména nezná a nemá znát — je to popisek zobrazení, ne fakt převodu.

**Validace před odesláním (S7)** je trojí a poctivá v tom, co klient umí: seznam nesmí být prázdný, žádná jednotka nesmí mít prázdný obsah a typ mimo nabídku zdroje; XML mapování se parsuje `DOMParser`em a chyba se ukáže u jednotky s číslem řádku z hlášky parseru. C# a SQL klient neparsuje — syntaktickou chybu hlásí server (u SQL s řádkem a sloupcem, které vydává `TSql160Parser`) a jeho hláška se zobrazí doslova: `api.js` čte tělo chybové odpovědi, ne text HTTP stavu, čímž vada položky 13 mizí konstrukcí.

**Výsledek se zobrazí po souborech a záznamy po entitách.** Každý artefakt má vlastní panel se zvýrazněnou syntaxí a jménem odvozeným na klientovi z obsahu — název třídy nebo mapované entity, s příponou podle typu obsahu; kde odvození selže, nastoupí pořadové číslo. Je to heuristika zobrazení, ne tvrzení serveru. Záznamy stojí v pásu pod výstupem: souhrn počtů podle druhu, pak tabulka druh–entita–vlastnost–artefakt–kategorie–důvod, seskupená po entitách a s `Failure` první, protože znamená, že artefakty entity chybí. Po souborech je tedy vstup i výstup; záznamy se vážou k entitě, protože nic víc nenesou — a tahle hranice se vyslovuje tady, ne obchází.

**Hlavička výsledku nese záznam běhu a stav katalogu.** Identifikátor běhu, zdroj → cíl s verzemi z deskriptorů (S6) a stav připojení ke katalogu slovně: nenakonfigurováno, nepoužito, přečteno (s časem čtení), nedosažitelné — čtyři stavy z rozhodnutí [030](030-scope-of-version-1-0.md). Připojení samo zůstává v konfiguraci serveru (rozhodnutí [029](029-database-connection-is-the-consumer-projects-fact.md)); rozhraní ukazuje jen stav.

**Stažení kompletního výstupu (S7) balí server.** Nový koncový bod `POST /archive` přijme seznam dvojic jméno–obsah a vrátí ZIP; klient pošle pojmenované artefakty a nabídne soubor ke stažení. Balení patří na server podle 032f — v prohlížeči by znamenalo další vendorovanou knihovnu.

**Advisor se přepisuje beze změny rozsahu:** zdrojový framework, entitní jednotky, dotazy s vahami, limit paměti, počet vybíraných frameworků a nepovinná podmnožina cílů; výsledkem vybrané frameworky, přiřazení dotazů s naměřeným časem a pamětí a artefakty převedené pro vybrané frameworky. Žádná nová schopnost — položka 13 je o dohnání překladové obrazovky, ne o rozšíření Advisoru, který je mimo záruky verze 1.0.

**Výkladová stránka ukazuje diagram pipeline a dva živé příklady.** Diagram parse → doplnění z katalogu → build je inline SVG podle 032e. Příklady jsou dva a oba živé — stránka načte vzorek přes `/samples`, pošle ho na `/convert` a ukáže skutečný vstup, výstup i záznamy: **EF Core → NHibernate** (entita s anotacemi + LINQ dotaz; ukazuje překlad anotací do XML mapování a LINQ do HQL) a **Dapper → EF Core** (entita + SQL dotaz; ukazuje směr, kde zdroj tvrdí málo a výstup se opírá o konvence, takže je na něm nejlíp vidět, k čemu záznamy jsou). Dva stačí: pokrývají oba směry „zdroj tvrdí hodně" i „zdroj tvrdí málo" a každý další by opakoval už ukázané.

## Důsledky

**`POST /archive` je nová odpovědnost `ORMConvertorAPI`** — jediný koncový bod, který nic nepřekládá; přijímá jména a obsahy, jak je pojmenoval klient, a vrací je zabalené. Do záruk překladu nevstupuje.

**Jména souborů výstupu jsou vlastnost zobrazení.** Server je nevydává a žádný test je netvrdí; změna heuristiky pojmenování není změna kontraktu.

**F14 se plní v rozsahu, který data unesou:** vstup a výstup po souborech, diagnostika po entitách. Kdyby měl záznam nést zdrojový soubor, musela by jednotka `/convert` dostat jméno — to by byla změna kontraktu API a samostatné rozhodnutí.

**Validace na klientovi je pomocník, ne brána.** Autoritativní odmítnutí vstupu zůstává serveru; klient jen šetří okružní cestu tam, kde chybu umí najít sám. Nic z toho netvrdí F/S/T víc, než co říká S7.
