# 089 — Diferenční ověření je čtvrtý stupeň nad dotazem a porovnává se proti zapsanému výsledku

Datum: 2026-09-21
Stav: revidováno
Požadavky: F12, F13, T2, T3, S2, S5
Podklad: rozhodnutí [014](014-language-type-model.md), [016](016-generated-artifact-verification-levels.md), [019](019-neutral-database-type-vocabulary.md), [027](027-query-artifact-verification.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [078](078-java-suite-as-a-client-of-a-running-instance.md), [083](083-parameter-as-the-fifth-operand-shape.md) a [087](087-an-integration-test-is-a-run-against-the-database.md); JSS článek, §3 (vymezení rozsahu), pravidlo Q15 a tabulka příbuzných prací (řádek *ORM differential testing*); [`threat-model.md`](../threat-model.md), hrozba 1; [`architecture.md`](../architecture.md) §6.2 a §9, vyňatá oblast 6

## Kontext

Požadavek F13 žádá spustit **zdrojovou i přeloženou** variantu dotazu a porovnat normalizované výsledky, s pořadím zohledněným jen tam, kde ho dává dotaz, a s konfigurovatelnou prací s numerickou přesností a nully. Ověřovací kritérium k tomu žádá **nejméně třicet dvojic dotazů se shodným výsledkem** a aby **záměrně chybný překlad byl odhalen**.

**Dnes se neporovná ani jedna dvojice.** Čtyři stupně rozhodnutí 016 končí u toho, že artefakt vznikne, přeloží se, cílový framework ho přijme a proti databázi se spustí; rozhodnutí 027 doplnilo, čím se u dotazu naplní druhý a třetí stupeň, a **čtvrtý nechalo výslovně otevřený** s větou, že „nově má smysl i pro dotazy a je to přímá cesta k F13: spustit zdrojovou i přeloženou variantu a porovnat normalizované výsledky". Tohle rozhodnutí tu větu dokončuje.

**Jeden kus toho už existuje a ukazuje tvar.** Javová sada spouští generovaný dotaz proti databázi v testu `aBoundRowCountSlicesTheResultOfTheGeneratedQuery`: zapíše dva řádky, zavolá generovanou metodu s vázaným počtem a tvrdí, že se vrátil jeden. Je to čtvrtý stupeň nad dotazem provedený ručně, pro jeden dotaz a s aserci na počet řádků místo na jejich obsah — tedy přesně ta věc, kterou F13 žádá systematicky a v porovnání proti druhé variantě.

**Co tenhle nárok smí znamenat, vymezuje článek, ne my.** JSS článek v §3 říká, že přístup **necílí na plnou sémantickou ekvivalenci** mezi frameworky a že rozdíly v běhové sémantice — sledování změn, strategie načítání, cachování, správa transakcí a session, volba prováděcího plánu — jsou výslovně mimo rozsah; pravidlo Q15 tutéž mez vyslovuje o zárukách překladu, které jsou omezené na syntaktický a strukturní záměr. Článek přitom diferenční testování zná a cituje ho jako příbuznou práci, která **porovnává několik ORM, ne překlad mezi nimi** (Sotiropoulos et al., ICSE 2021). F13 je tedy přesně to místo, kde tenhle fork jde za článek — ale jen o jeden krok, a ten krok musí být úzce vymezený: **shodné řádky jednoho read-only dotazu nad jednou sadou dat**, nikoli tvrzení o chování frameworků.

**Čtyři věci dělají z otázky rozhodnutí.**

**Co je shodný výsledek.** Normalizace, kterou požadavek jmenuje třemi slovy, musí dostat tvar, a k tomu patří otázka, kterou požadavek nejmenuje vůbec: srovnávají se jen **hodnoty**, nebo i **typy**?

**Kde běží zdrojová varianta.** Tohle je jádro a je to otázka architektury, ne testů. Porovnat dvojici znamená spustit i dotaz zdrojového frameworku, tedy mít ten framework za běhu. Pro .NET zdroj to umí `Tests`, pro javový jedině `JavaTests` — a **osmnáct z šestatřiceti směrů vede napříč ekosystémy**, takže každá půlka takové dvojice běží v jiném běhovém prostředí.

**Jak se pozná záměrně chybný překlad.** Kritérium žádá mutaci a nechává na nás, co se mutuje a kdo ji vyrábí.

**Čím to je proti stupňům ověření.** Pátý stupeň téže řady, nebo jiná osa? Na odpovědi visí, co si javová sada o sobě počítá (`SuiteSizeTest`) a jak se F13 zapíše do [`traceability.md`](../traceability.md).

## Zvažované varianty

### 1 — Porovnávat obě poloviny přímo, přes koncový bod, který spustí cizí kód

Nejvěrnější čtení požadavku: jedna sada běží, druhou půlku si vyžádá po běžící instanci nástroje a výsledky srovná v jednom procesu. Tvar by navazoval na rozhodnutí 078, podle kterého je javová sada klientem běžící instance.

Zamítáme, a je to zamítnutí zásadní, ne cenové. Aplikace dnes dotaz nespouští; „spusť tenhle vygenerovaný artefakt a vrať řádky" by byl **nový koncový bod, který kompiluje a spouští cizí kód** — tedy druhý výskyt téhož, co dělá Advisor a co [`threat-model.md`](../threat-model.md) vede jako hrozbu 1: kompilace a běh cizího kódu ve vlastním procesu aplikace, bez limitů času, paměti a práv. Advisor je přesně kvůli tomu ze záruk vyňatý vcelku (§9, oblast 1) a první věta S4 s ním. Postavit **nárokovaný** požadavek na mechanismu, který je vyňatý a vede se jako hrozba, by tu hranici zrušilo z druhé strany — a dopadlo by to na F13 i na S4 naráz. Kdyby takový koncový bod někdy vznikl, vznikne z rozhodnutí o izolaci, které má vlastní položku, ne jako vedlejší produkt testovací infrastruktury.

### 2 — Omezit dvojice na směry uvnitř jednoho ekosystému

Devět .NET směrů a devět javových dá osmnáct, s entitní i dotazovou půlkou snadno přes třicet — a **kritérium F13 cross-ecosystem dvojice nežádá**, to je věta F10.

Zamítáme. Splnilo by to literu a minulo smysl: sada by ekvivalenci dokládala tam, kde je překlad nejbližší, a mlčela by přesně tam, kde je nejvzdálenější a kde tedy o ní jde nejvíc. Matice T2 žádá .NET → Java, Java → .NET i Java → Java rozdělené podle kategorií dotazů; doložit z toho jen třetí by z T3 udělalo metriku měřenou na snazší polovině vzorku. Varianta je navíc zbytečná, jakmile existuje varianta 5.

### 3 — Porovnávat místo řádků text vydaného SQL

Nechat každý cíl vyrobit SQL, které by poslal — u EF Core `ToQueryString()`, u ostatních obdobně — a porovnávat ty řetězce.

Zamítáme. Není to běh a není to výsledek: dva různé, stejně správné dotazy vydají různé SQL, takže by test hlásil rozdíl tam, kde žádný není, a naopak by neviděl rozdíl, který vznikne až nad daty. Navíc je to jen silnější verze toho, co už dělá **třetí** stupeň podle rozhodnutí 027, a F13 žádá právě ten krok dál.

### 4 — Mutovat překladač, ne artefakt

Chybný překlad vyrábět mutací generátoru, aby se měřila citlivost celého řetězce.

Zamítáme. Je to vlastní nástroj s vlastní údržbou a vlastními vadami, a většina mutací překladače vydá artefakt, který se ani nepřeloží — což o porovnávání výsledků nedokazuje nic. Předmětem téhle části není generátor, nýbrž **citlivost porovnání**, a tu je třeba měřit na vstupu, který projde až k řádkům.

### 5 — Čtvrtý stupeň nad dotazem, se zapsaným výsledkem jako společným měřítkem

## Rozhodnutí

**Volíme variantu 5. Diferenční ověření je čtvrtý stupeň rozhodnutí 016 provedený nad dotazem; obě varianty dvojice se spouštějí, každá ve svém běhovém prostředí, a potkávají se nad jedním kanonickým výsledkem zapsaným v repozitáři, ne přes hranici procesu.**

### Stupňů zůstává čtyři

Řada rozhodnutí 016 je uspořádaná tím, co každý stupeň **předpokládá**: tvar → překlad → přijetí frameworkem → běh proti databázi. Diferenční ověření předpokládá čtvrtý stupeň u obou artefaktů a nic dalšího nepřidává do řady — je to **způsob, jakým se u dotazu tvoří verdikt čtvrtého stupně**, tak jako u entity je tím způsobem „uloží se a načte se stejnou identitou". Pátý stupeň proto nezavádíme a rozhodnutí 027 to předjalo, když čtvrtý stupeň pro dotazy nechalo otevřený místo toho, aby otevřelo nový.

Plyne z toho jedna věc, která by se jinak tiše rozbila: **diferenční test nese `@Tag("integration")`** stejně jako ostatní testy čtvrtého stupně, protože jeho verdikt závisí na tom, co se skutečně stalo v databázi. Definice rozhodnutí 087 se nemění, `SuiteSizeTest` počítá dál totéž a počet integračních testů F12 dvojicemi **roste**, nikoli klesá.

### Kanonický výsledek je společným měřítkem

Dvojici nelze porovnat v jednom procesu, kdykoli půlky patří různým ekosystémům, a koncový bod, který by to umožnil, zamítla varianta 1. Měřítko proto nestojí mezi procesy, nýbrž v repozitáři: **ke každému dotazu matice patří jeden kanonický výsledek — textový soubor s řádky, které dotaz nad fixturou vrací.**

- **Vzniká během, ne rukou.** Výsledek je fixován tím, že se spustí **zdrojová** varianta dotazu v sadě, která zdrojový framework vlastní. Kdo ho zapisuje, zapisuje výstup běhu.
- **Každá varianta se proti němu poměřuje vlastním během.** Zdrojová půlka v sadě svého ekosystému, přeložená v sadě ekosystému cíle. Obě tedy **skutečně běží**, jak žádá F13; soubor je médium porovnání, ne náhrada druhého běhu.
- **Patří dotazu, ne směru.** Tohle je ta vlastnost, bez které by měřítko bylo k ničemu: jeden soubor poměřuje všechny cíle naráz, takže **rozbitý překlad v jednom směru se nedá spravit úpravou měřítka** — ta by okamžitě shodila všechny ostatní směry téhož dotazu. Porovnání je tím n-cestné, což je přesně tvar, jaký matice T2 potřebuje.

**Kde soubory bydlí, je už rozhodnuté.** Testovací schéma je dnes jediný skript `Tests/Database/TestSchema.sql`, který si .NET sada vkládá do sestavení a javová ho čte jako testovací zdroj z `../Tests/Database`, aby schéma zůstalo jedno (rozhodnutí 076). Kanonické výsledky i popis matice jdou **vedle něj a touž cestou**; nevzniká žádný nový mechanismus sdílení a obě sady čtou tytéž soubory.

### Fixtura je read-only a společná

Dnešní scénáře čtvrtého stupně si řádky zapisují samy a transakci na konci rolují zpět. Diferenční scénář nic nezapisuje: **čte sadu dat, kterou vytvoří skript vedle `TestSchema.sql`.** Tím odpadá závislost na pořadí testů i celý transakční tanec a výsledek dotazu je funkcí jedině dotazu.

Každá sada si tu sadu dat vytvoří **ve svém schématu** — `ormconvertor_test`, respektive `ormconvertor_java_test` —, takže obě půlky nečtou tytéž řádky, nýbrž řádky vyrobené týmž skriptem. Pro porovnání výsledků je to totéž a je to výrazně méně křehké než dvě běhová prostředí sdílející jedno schéma; „stejná databáze a stejná data" z F12 zůstává splněné v tom smyslu, v jakém to sada plní dnes.

**Data nejsou libovolná a je to podmínka, ne detail:** fixtura musí být zvolena tak, aby **každá mutace ze seznamu níž nad ní změnila výsledek**. Sada, na které by vypuštěný filtr vrátil tytéž řádky, dělá z negativní poloviny prázdné gesto.

### Co je shodný výsledek

Kanonický tvar je text a jeho pravidla jsou vyjmenovaná, protože determinismus téhle podoby je tím, co S2 na testech stojí.

**Řádek je sekvence polí oddělených tabulátorem**, v pořadí, které dává projekce dotazu; u dotazu bez projekce v pořadí vlastností entitní mapy. Hodnoty se vykreslují takto:

- celá čísla holá; **desetinná na počtu míst, který uvádí matice** u toho dotazu (výchozí je měřítko, které tvrdí mapování, a není-li žádné, šest);
- **čísla s pohyblivou řádovou čárkou na počtu platných číslic, který uvádí matice** (výchozí dvanáct) — rodiny `Real` a `DoublePrecision` se mezi ekosystémy liší v posledních bitech a trvat na jejich shodě by znamenalo měřit ovladač, ne překlad;
- temporální hodnoty v ISO 8601 na počet zlomkových míst, který tvrdí mapování (rozhodnutí [079](079-fractional-second-precision-as-second-precision.md));
- řetězce v uvozovkách, s escapováním uvozovky, zpětného lomítka, tabulátoru a konce řádku;
- **boolean jako `true`/`false`** (doplněno revizí, viz Historie);
- **null jako holé `NULL`**, což je tvar, který se s žádnou vykreslenou hodnotou nesrazí, protože řetězec je vždy v uvozovkách.

**Pořadí rozhoduje mezireprezentace, ne cíl.** Nese-li dotaz řazení, porovnávají se řádky v pořadí; nenese-li, obě strany se před porovnáním seřadí podle vykresleného řádku. Že o tom rozhoduje IR, není formalita: je to jediné místo, které o dotazu tvrdí totéž ve všech šesti cílech, a přesně tak zní věta požadavku „pořadí zohlednit jen je-li dané dotazem".

**Konfigurace patří dotazu, ne běhu.** Přesnost je vlastnost toho, co dotaz počítá — `AVG` nad peněžním sloupcem snese jiné měřítko než holý sloupec —, ne vlastnost hostitele, na kterém se sada spouští. Nastavení proto stojí v položce matice u toho dotazu, kde ho vidí obě sady a kde ho vidí i čtenář výsledku.

**Porovnávají se hodnoty, ne typy**, a je to volba, ne opomenutí. Jazyková strana typového modelu je neutralizovaná právě proto, že se slovníky ekosystémů nekryjí (rozhodnutí 014): týž sloupec přijde z jednoho cíle jako `Integer` a z druhého jako `int` nebo `Long`, aniž by se hodnota lišila. Trvat na shodě typů by tedy shazovalo správné překlady. Typové tvrzení se tím neztrácí — ověřuje se na vlastní ose, deskriptorem a třetím stupněm —, jen se neměří tam, kam nepatří.

### Jak se pozná chybný překlad

Mutuje se **vydaný artefakt**, ne překladač, a množina mutací je vyjmenovaná, ne generovaná:

| Mutace | Co by prošlo bez ní |
|---|---|
| vypustit filtr | dotaz vracející víc řádků, než má |
| obrátit porovnávací operátor | dotaz vracející doplněk |
| vypustit řazení u dotazu, který ho nese | porovnání, které pořadí ve skutečnosti nekontroluje |
| změnit počet řádků stránkování | výřez, který nikdo neměří (rozhodnutí 060 a 085) |
| prohodit dvě projektovaná pole | shoda hodnot bez shody sloupců |

Každá z nich musí u **každého** cíle skončit rozdílem proti kanonickému výsledku; mutace, která projde, je nález o porovnávání, ne o překladu. Poslední dvě řádky tabulky přitom testují samotné porovnání — vypuštěné řazení odhalí jedině porovnání, které pořadí bere vážně tam, kde ho dotaz dává, a prohozená pole jedině porovnání, které nesrovnává hodnoty přeházeně.

### Co do dvojice nepatří

**Parametrizovaný dotaz do ní patří** a hodnoty argumentů nese položka matice; od rozhodnutí 083 a 085 je parametr prvotřídním tvarem operandu i počtu řádků, takže vynechat parametrizované dotazy by z ověření vypustilo právě tu část dotazové matice, která přibyla naposledy.

**Nepatří do ní tři věci.** Dotaz nad tabulkou, kterou fixtura nemá. Dotaz, jehož zdrojovou variantu nelze v její sadě spustit. A směr, ve kterém cíl překlad **odmítne** — `Failure` podle rozhodnutí 053 znamená, že artefakt nevznikl, takže není co porovnávat; je to doložení F11, ne mezera v F13, a matice to musí umět říct tímhle slovem místo chybějícího řádku.

### Počítá se sama

Počet dvojic si tvrdí sada, ne dokument — týž mechanismus, jaký u F12 zavedlo rozhodnutí 087 a ze stejného důvodu: číslo přepsané do [`traceability.md`](../traceability.md) se s realitou tiše rozejde, číslo kontrolované proti vlastním datům ne. Rozděluje se to na dvě tvrzení, protože ani jedna sada nevidí do druhé: **popis matice tvrdí, že dvojic je aspoň třicet**, a **každá sada tvrdí, že proběhla každá dvojice, kterou jí matice přiděluje.** Dohromady to dává kritérium a ani jednu půlku nelze splnit vynecháním druhé.

## Důsledky

**Čtvrtý stupeň nad dotazem začne existovat na obou stranách.** Dnes neběží proti databázi ani jeden generovaný .NET dotaz — `QueryVerificationTest` je celý nasucho (stupně 2 a 3) — a na javové straně je takový běh jediný. Po implementaci je to systematický povrch v obou sadách a je to zároveň první místo, kde se generovaný dotaz spouští proti datům.

**F13 se tím dá vyslovit a F12 poroste.** Diferenční test čtvrtého stupně nese `@Tag("integration")`, takže dvojice se počítají i do druhé meze kritéria F12; nárok se ale u javových požadavků podle dosavadní praxe vysloví až po zeleném běhu v zafixovaném prostředí, ne po běhu na vývojovém hostiteli.

**Pro T2 a T3 vzniká to, co dosud chybělo.** Matice T2 měla buňky se stavem „přeloží se"; nově mají stav „vrací tytéž řádky". Metriky korektnosti T3 jmenují funkční ekvivalenci jako podíl a ten podíl dosud nešlo spočítat, protože čitatel nebyl definovaný; kanonický výsledek ho definuje.

**Hranice nároku se tím nesmí přepsat šířeji, než rozhodnutí unese.** Sada nedokazuje sémantickou ekvivalenci a §3 JSS článku i pravidlo Q15 ji výslovně nenárokují. Dokazuje, že **read-only dotaz vrací nad jednou sadou dat tytéž řádky** — o sledování změn, načítacích strategiích, cachování, transakcích ani o prováděcích plánech neříká nic. Až se bude psát věta do §9 a do sekce *Guarantees*, musí to být vidět v ní, ne až v tomhle souboru.

**Na co se při implementaci narazí.** Fixtura musí být navržená proti seznamu mutací, ne proti pohodlí — sada dat, nad kterou vypuštěný filtr nic nezmění, dělá z negativní poloviny prázdné gesto. Vykreslování hodnot musí být v obou ekosystémech **prokazatelně totéž**, a je to vlastní past: `decimal` a `BigDecimal` tisknou nuly za desetinnou čárkou jinak, `LocalDateTime` a `DateTime` jinak formátují půlnoc, a řetězec s tabulátorem rozbije oddělovač, pokud se escapování zapomene na jedné straně. Odsud plyne test, který musí vzniknout dřív než dvojice: **týž řádek vykreslený oběma sadami dá bajtově týž text**. A konečně kanonické soubory podléhají konci řádků podle kořenového `.gitattributes`, takže porovnání musí být po řádcích, ne po bajtech celého souboru.

**Co to neotevírá.** Koncový bod, který by spustil cizí kód, nevzniká (varianta 1), takže hrozba 1 ani vyňatá oblast 1 se tímhle rozhodnutím nehýbe. Advisor zůstává jediným místem, kde nástroj cizí kód spouští, a jeho izolace je dál vlastní otevřená položka.

**Testy.** Že vykreslení téhož řádku dá v obou sadách týž text — to první, protože na něm stojí všechno ostatní. Že každá dvojice matice proběhla v sadě, které přísluší, a že matice jich má aspoň třicet. Že každá z pěti mutací u každého cíle skončí rozdílem. Že dotaz s řazením porovnává pořadí a dotaz bez řazení ne — obojí doložené případem, ve kterém by druhé chování dalo jiný verdikt. Že dvojice, jejíž překlad skončil `Failure`, je v matici zapsaná jako odmítnutá a nepočítá se mezi třicet.

## Historie

**2026-09-21 — pět případů doplněných implementací.** Volba se nemění: diferenční ověření zůstává čtvrtým stupněm nad dotazem a měřítkem zůstává zapsaný kanonický výsledek. Doplňuje se, co při psaní nebylo vidět a co by jinak zůstalo jen v kódu.

**Boolean měl pravidlo vykreslení a neměl ho vyslovené.** Výčet pravidel jmenoval celá čísla, desetinná, přibližná, temporální, řetězce a null; `bit` v databázi a `boolean` v obou jazycích tam chyběl, přestože ho první dotaz matice hned potřeboval. Zní `true`/`false` — nikoli `1`/`0`, protože to by se nedalo odlišit od celého čísla.

**Mutace je odhalená i tehdy, když artefakt vůbec neproběhne.** Text mluvil o tom, že mutace „musí skončit rozdílem proti kanonickému výsledku", a počítal tedy s tím, že mutovaný artefakt poběží. Část mutací běh vůbec nedovolí: projekce čtená v obráceném pořadí podá Dapperu jméno tam, kde čeká číslo, a filtr vypuštěný z HQL nechá na dotazovém objektu parametr, který nikdo neváže. Obojí je chybný překlad **odhalený**, jen hlasitěji; nepřijatelné by bylo jedině ticho. Sady to tak počítají a mez zůstává tam, kde byla: selhání aserce se za odhalení považovat nesmí.

**Read-only fixtura si nese vlastní tabulku.** Rozhodnutí říkalo „sada dat, kterou vytvoří skript vedle `TestSchema.sql`", a nechávalo otevřené, kam ta data přijdou. Osít některou z tabulek schématu nejde: scénář, který uloží jeden řádek a tvrdí, že tabulka drží jeden řádek, začne padat kvůli datům, o která nežádal — tři testy MyBatisu to při implementaci předvedly. Fixtura proto zakládá `DifferentialProducts` a nesahá na nic, do čeho jiné scénáře zapisují.

**Žádný dotaz matice nesmí řadit podle clusterovaného klíče.** Věta, že se fixtura navrhuje proti mutacím, platí, ale jednu její podmínku bylo vidět teprve na běhu: nad dotazem seřazeným podle primárního klíče vrací i neseřazené čtení tytéž řádky v témž pořadí, takže mutace vypuštěného řazení nad ním projde nepozorovaně. Dva dotazy matice se kvůli tomu přeřadily na neklíčový sloupec a fixtura si tu podmínku píše u sebe.

**Které mutace dotaz nese, vyslovuje matice.** Odvozovat to z artefaktu za běhu vypadalo lacině, ale znamená to, že mutace, která na artefakt některého cíle přestane sahat, se tiše promění v přeskočený případ. Matice to proto jmenuje a test tvrdí, že vyjmenovaná mutace artefakt opravdu změnila — přesně tak se našlo, že sestupné řazení vychází z EF Core jako `.OrderByDescending` a pravidlo hledající `.OrderBy(` ho minulo.
