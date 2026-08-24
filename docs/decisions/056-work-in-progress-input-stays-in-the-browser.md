# 056 — Rozpracovaný vstup překladové obrazovky zůstává v prohlížeči

Datum: 2026-08-24
Stav: platí
Požadavky: F14, S7
Podklad: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) a [033](033-shape-of-the-static-frontend-screens.md); [`threat-model.md`](../threat-model.md), řádek aktiva „Zdrojový kód, který uživatel vloží"; [`use-cases.md`](../use-cases.md), UC1 a UC2

## Kontext

Rozhodnutí 033 postavilo překladovou obrazovku jako jednu stránku s pěti kroky a jejím stavem je podle 032b jeden obyčejný objekt v paměti stránky. Pro *vykreslování* je to správná volba a nic na ní neměníme; má ale důsledek, který se vykreslování netýká: celý rozpracovaný vstup existuje jen tak dlouho, dokud žije dokument. F5, tlačítko zpět, zavřená záložka nebo pád prohlížeče vezmou s sebou každou vloženou jednotku i obě volby frameworku a znovu se to nedá získat nijak — nic z toho nikde jinde neleží.

Není to teoretická nepříjemnost, protože je to přesně opak toho, na co je obrazovka stavěná. Požadavek F14 z ní dělá plochu na *mnoho* jednotek najednou, takže cena jednoho klepnutí vedle roste s tím, jak dobře je požadavek splněný. UC1 a UC2 jsou dlouhá sezení nad mnoha jednotkami a UC1 navíc popisuje práci, při které se ke vstupu opakovaně vrací a upravuje se — tedy právě tu situaci, ve které se stránka přenačte nejsnáz. A S7 žádá uživatelskou přívětivost: rozhraní, které bez varování zahodí půlhodinu vkládání, ji nemá, ať má kroků kolik chce.

Otázka tedy nezní „udělat obrazovku příjemnější", ale **kde bydlí rozpracovaný vstup** — a to je volba, protože tím vstupem je cizí zdrojový kód. [`threat-model.md`](../threat-model.md) o něm v tabulce aktiv dnes tvrdí: „jen v paměti procesu po dobu požadavku… neukládá se, nikam se neposílá". Ta věta je o serveru a serverová zůstane pravdivá, jenže čtenář ji čte jako větu o nástroji. Jakákoli trvalejší kopie proto musí dostat vlastní řádek, jinak by dokument tvrdil něco, co přestalo platit — a přesně tomuhle druhu tichého rozporu se projekt jinde vyhýbá.

## Zvažované varianty

1. **Nechat to být.** Nulová cena a žádné nové tvrzení o datech. Zamítáme: mezera je v tom místě požadavku, které rozhodnutí 033 ošetřilo nejméně, a nedá se obejít opatrností — přenačtení stránky není chyba uživatele.

2. **`sessionStorage`.** Přežije přenačtení, zemře se záložkou. Vypadá jako kompromis, ale je to kompromis na špatné ose: chrání proti tomu, co bolí míň (F5), a nechrání proti tomu, co bolí víc (zavřené okno, restart stroje, pád prohlížeče). Tvrzení o datech přitom mění stejně — kopie mimo paměť stránky vznikne tak jako tak.

3. **Uložit jen obě volby frameworku.** Nejlevnější varianta, která vůbec něco dělá, a jediná, u které se tabulka aktiv nehne: dvě čísla nejsou zdrojový kód. Zamítáme, protože zachraňuje to levné. Vybrat dvakrát z rozbalovacího seznamu stojí dvě vteřiny, znovu vložit deset souborů stojí sezení.

4. **Uložit celý vstupní stav obrazovky do `localStorage` a dát k tomu tlačítko *Clear*.**

5. **Uložit stav na server.** Zamítáme bez dalšího zvažování. Aplikace nemá sezení ani úložiště, uživatele nerozlišuje a nemá autentizaci; zavést to kvůli pohodlí jedné obrazovky by znamenalo novou odpovědnost serveru, nový vstupní bod v modelu hrozeb a stav, který přežije uživatele. Vstup, který dnes server drží jen po dobu požadavku, by v něm začal ležet — to je zhoršení bezpečnostní vlastnosti, ne cena za pohodlí.

## Rozhodnutí

**Volíme variantu 4: překladová obrazovka si po každé změně ukládá celý svůj vstupní stav — obě volby frameworku a seznam jednotek se jménem, typem obsahu i obsahem — do `localStorage` prohlížeče a při načtení ho odtamtud obnoví. K tomu dostane tlačítko *Clear*, které smaže stav i uložení naráz.**

Argument pro tuhle variantu a proti ostatním plyne z toho, čemu má obrazovka odpovídat. F14 z ní dělá plochu na dávku, takže jedinou variantou, která cenu ztráty odstraňuje celou, je ta, která uloží všechno: varianta 3 by zachránila dvě čísla a nechala padnout to, co uživatel psal, varianta 2 by ochránila jednu ze čtyř cest, kterými se stránka ztrácí. Zároveň to není zvětšení plochy nástroje — `localStorage` je vlastnost prohlížeče, takže nepřibývá závislost ani build krok (rozhodnutí 032a a 032f), nepřibývá koncový bod a serverového kontraktu se to nedotkne vůbec.

**Rozsah je překladová obrazovka a nic jiného.** Výkladová stránka žádný vstup nemá a Advisor je ze záruk vyňatý vcelku ([`architecture.md`](../architecture.md), §9, oblast 1); jeho vstup se navíc plní z ukázek jediným tlačítkem, takže ztráta je tam obnovitelná krokem, který na překladové obrazovce neexistuje. Kdyby Advisor z vyňaté oblasti vystoupil, je to táž úvaha a stačí ji rozšířit.

**Selhání úložiště není chyba nástroje.** Anonymní okno, zakázané úložiště webu i překročená kvóta jsou legitimní stavy prohlížeče. Zápis i čtení proto stojí v `try`/`catch`: když se nepovedou, obrazovka funguje dál a jednou řekne, že si vstup nepamatuje. Nic se kvůli tomu neodmítá a nic se neopakuje — pamatování je pohodlí, ne součást převodu.

**Tlačítko *Clear* je součástí volby, ne ozdoba.** Data, která přežijí zavření záložky, potřebují způsob, jak je odstranit, a ten musí být na téže obrazovce, kde vznikla; jinak zbývá jen nastavení prohlížeče. Ze stejného důvodu se *Load sample set*, které dnes stav mlčky přepíše, nejdřív zeptá, pokud nějaké jednotky na obrazovce jsou.

## Důsledky

**[`threat-model.md`](../threat-model.md) dostává upravený řádek aktiva.** „Zdrojový kód, který uživatel vloží" už neleží jen v paměti procesu po dobu požadavku: kopie rozpracovaného vstupu leží v `localStorage` prohlížeče, pod původem té instance, na stroji uživatele. Na server se tím nedostane nic navíc — `/convert` dostává týž obsah jako dřív a serverová půlka věty („neukládá se, nikam se neposílá, do logu se nedostane") platí beze změny. Mění se to, že na sdíleném nebo cizím stroji zůstane vložený kód čitelný dalšímu uživateli téhož prohlížečového profilu, dokud ho někdo nesmaže — a proto to tlačítko.

**Je to tvrzení o jedné instanci, ne o síti.** `localStorage` je vázaný na původ (schéma, host, port), takže dvě instance nástroje si do dat nevidí a žádná jiná stránka na ně nedosáhne. Odpovídá to předpokladu nasazení z modelu hrozeb — jedna ručně spravovaná instance v důvěryhodné síti.

**Kompatibilita uloženého tvaru se neverzuje.** Uložený objekt je detail obrazovky, ne kontrakt: čtení je defenzivní, co nesedí, se zahodí a začne se s prázdnou obrazovkou. Kdyby se tvar někdy změnil, správnou odpovědí je starý obsah zahodit, ne migrovat — jde o rozepsaný vstup, ne o data nástroje.

**Nárok se nemění.** `architecture.md` §6.3 to popíše jako současné chování a `traceability.md` to připíše k S7, ale S7 zůstává nárokované v užším rozsahu ze stejných důvodů jako dosud: validace na klientovi je pomocník, ne brána, a chyba na úrovni řádku platí pro vstupní syntaxi.
