# 092 — Strop hloubky zanoření vstupu, vynucený nad tokeny před sestupem

Datum: 2026-09-21
Stav: revidováno
Požadavky: F11, S4, S7
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [045](045-a-conversion-that-produced-nothing-says-so.md), [062](062-hql-read-by-a-hand-written-parser.md) a [076](076-java-wrappers-in-csharp-jvm-in-containers.md); [`threat-model.md`](../threat-model.md), hrozba 2; měření z 2026-09-21 uvedené níž

## Kontext

HQL, JPQL a javovou třídu čtou vlastní sestupné parsery (rozhodnutí 062 a 076), takže hloubka zanoření vstupu je u nich hloubkou rekurze a žádný z nich ji neomezuje. Revize 2026-09-21 to změřila na HQL a našla následek, jaký nemá nic jiného v celém nástroji: podmínka s dvěma tisíci závorkami projde a vydá artefakt, hlubší **přeteče zásobník a proces skončí** (`0xC00000FD`). Přetečení zásobníku se v .NET zachytit nedá, takže z toho není ani `Failure`, ani čtyřistovka — instance zmizí i se všemi souběžnými požadavky, které zrovna obsluhuje.

Než jsme se rozhodli, rozšířili jsme měření na **všechny jazyky, které nástroj čte**, protože položka otevřela i otázku, jestli strop patří i cizím gramatikám, „kde horní mez neznáme". Teď ji známe. Měřeno na vývojovém hostiteli (Windows, výchozí zásobník vlákna 1 MB, konfigurace Release), vždy jedním vstupem, jehož hloubka roste:

| Vstup | Poslední hloubka, která prošla | První, která shodila proces |
|---|---|---|
| HQL — závorky v podmínce | 2048 | 3000 |
| HQL — vnořené poddotazy `exists (…)` | 1024 | 2048 |
| JPQL — závorky v podmínce | 2048 | 3000 |
| JPQL — vnořené poddotazy | 1024 | 2048 |
| javová třída — vnořené generické typy | 3000 | 4096 |
| javová třída — vnořené vnitřní třídy | 2048 | 3000 |
| T-SQL, `TSql160Parser` — závorky v podmínce | 1024 | 2048 |
| C#, `CSharpSyntaxTree.ParseText` — závorky ve výrazu | 4096, se záznamem od Roslynu | 6000 |
| XML, `XDocument.Parse` — vnořené prvky | 65536 bez pádu | — |

Plynou z toho čtyři věci a každá mění zadání položky.

**Cizí gramatika není jiný případ.** Na témž tvaru — závorky v podmínce — padá `TSql160Parser` dřív (2048) než HQL a JPQL (3000), a spolu s vnořenými poddotazy je to vůbec nejčasnější pád celé tabulky. Padá přitom týmž způsobem a je to čtecí cesta Dapperu a MyBatisu, tedy nic okrajového. Otázka „dostane strop i cizí gramatika" má odpověď danou měřením, ne úvahou — a ta odpověď je, že ji potřebuje nejnaléhavěji ze všech.

**Roslyn se jediný brání sám, ale ne dost.** Ve hloubce 4096 nevydá tichý strom, nýbrž diagnostiku — tedy přesně to, co chceme dělat i my —, přes šest tisíc ale padá taky. Na cizí zábranu se proto nespoléháme; počítáme s ní jako s druhou vrstvou, ne jako s odpovědí.

**XML do téhle třídy nepatří.** `XDocument.Parse` čte smyčkou, ne rekurzí, a šedesát pět tisíc vnořených prvků přečte bez zaváhání. Mapovací dokumenty tedy strop nepotřebují a nedostanou ho; že se jim jinak vytýká něco úplně jiného (neparsovatelné XML shodí celý převod, viz samostatná otevřená položka), s hloubkou nesouvisí.

**Rozptyl mezi tvary je malý, ale není zanedbatelný.** Nejlevnější z našich tvarů padá na čtyřech tisících, nejdražší na dvou; jedna úroveň poddotazu stojí zhruba dvojnásobek zásobníku oproti jedné závorce, protože na ni připadá celé tělo poddotazu, ne jen řetěz `ParseOr` → … → `ParsePrimary`. Jedno číslo pro všechny tedy musí vyhovět nejdražšímu tvaru, a to i s rezervou na tvary, které teprve přibudou.

Položka klade dvě otázky — kde strop vede a co se stane při jeho dosažení. Měření přidává třetí, kterou už nejde odložit: kde se strop vynutí, když ho potřebují i parsery, jejichž rekurzi nemáme v rukou.

## Zvažované varianty

### 1 — Nechat to popsané a nic neměnit

Dnešní stav: hrozbu popisuje threat model a verze na tu oblast spoleh neslibuje. Zamítáme. Vyjmutí ze záruk je poctivý nástroj u schopnosti, kterou nástroj neumí; tady o schopnost nejde. Verze nárokuje S7, jehož větou je chyba na úrovni souboru a řádku, a F11, jehož větou je, že se nic nevynechá potichu — pád procesu není ani jedno. Neobhájí se ani tím, že si o něj vstup řekl: následek nenese ten, kdo ho poslal, nýbrž každý, kdo byl obsloužen ve stejné chvíli. Od ostatních položek hrozby 2 se tenhle vstup liší právě tím, že není o pomalé odpovědi, a jediná vyňatá oblast §9, do které by se dal vecpat, neexistuje.

### 2 — Větší zásobník

Parsovat na vlákně založeném s explicitní velikostí zásobníku (`new Thread(…, maxStackSize)`), řekněme šestnáctinásobnou. Zamítáme: posouvá číslo, neohraničuje ho. Mez by pak ležela někde u třiceti tisíc úrovní, pořád by nebyla vyslovená a pořád by za ní byl pád procesu. K tomu vlákno navíc na každou jednotku a mez závislá na tom, jak byl proces spuštěn — tedy přesně ten druh čísla, který se nedá napsat do dokumentace ani doložit testem.

### 3 — Přepsat sestup na explicitní zásobník

Učebnicová odpověď: rekurzi nahradit vlastním zásobníkem na haldě a hloubku neomezovat vůbec. Zamítáme *pro teď*, ze tří důvodů. Za prvé by to přepsalo tři parsery, které jsou zrovna přibité round-trip testy (rozhodnutí 062), a to kvůli vstupu, jaký nikdo nenapíše. Za druhé by mez nezmizela, jen by se přesunula: halda je větší než zásobník, ale konečná taky, a vyčerpání paměti je hrozba 2 stejně jako vyčerpání zásobníku. Za třetí — a to rozhoduje — by to nepomohlo `TSql160Parser`u ani Roslynu, tedy dvěma z pěti jazyků — a podle měření zrovna tomu, který snese na jednu závorku nejmenší hloubku ze všech. Řešení, které pokryje tři pětiny problému za cenu přepsání tří parserů, není lepší než řešení, které pokryje pět pětin za cenu jedné smyčky.

### 4 — Jeden strop, vynucený nad tokenovým proudem, dřív než sestup začne

## Rozhodnutí

**Volíme variantu 4. Nástroj má jediný strop hloubky zanoření vstupu — ve výchozím stavu 128 úrovní — a vynucuje ho nad tokenovým proudem, který každý parser stejně vyrábí dřív, než sestup začne. Překročení je `Failure` s řádkem a sloupcem tokenu, který mez překročil. Strop platí pro všech pět jazyků čtených rekurzivně, tedy i pro obě cizí gramatiky. Hodnotu smí změnit — a strop i úplně vypnout — **provozovatel** v nastavení aplikace; **požadavek** ji změnit nesmí.**

**Proč 128.** Číslo má dvě meze a obě jsou změřené. Shora: nejdražší tvar prošel naposled na 1024 a padl na 2048, takže 128 leží osmkrát pod poslední změřenou úspěšnou hloubkou a šestnáctkrát pod prvním změřeným pádem. Rezerva není luxus — velikost rámce se mění s každou úpravou parseru, linuxový kontejner má jiný výchozí zásobník než Windows, na kterých se měřilo, a tvar dražší než poddotaz může teprve přibýt. Zdola: ručně psaná podmínka se zanořuje v jednotkách úrovní a strojově složená ve vyšších jednotkách, protože řetěz `a or b or c` parser neřeší rekurzí, nýbrž smyčkou se zploštěním — hloubku tedy dělají jedině **vypsané** závorky a poddotazy. Vnořený generický typ javové entity má hloubku dvě až tři, vnitřní třída jednu. 128 je zhruba geometrický střed mezi tím, co je ještě dotaz (jednotky až desítky), a tím, co zabije proces (tisíce), a to je přesně poloha, jakou strop mít má: nedosažitelný pro vstup, který někdo myslel vážně, a s rezervou nedosažený tam, kde začíná pád.

**Proč nad tokeny, a ne uvnitř rekurze.** Počitadlo v sestupu by bylo přesnější a u našich tří parserů triviální — jedno pole a inkrement na vstupu do `ParseCondition`. Volíme přesto scan nad tokeny, ze dvou důvodů. Cizí gramatiky nemají svou rekurzi v naší moci, ale **svůj lexer nabízejí odděleně** (`TSql160Parser.GetTokenStream`, `SyntaxFactory.ParseTokens`), a lexikální fáze je u všech pěti smyčka, ne rekurze; je to tedy jediné místo, kde se všech pět chová stejně a kam se jedno pravidlo vůbec dá umístit. A za druhé: zábrana, která se ptá před sestupem, se nedá obejít tvarem sestupu, na který jsme při jejím psaní nepomysleli.

Počítají se **kulaté a složené závorky**, protože ty jsou ve všech pěti jazycích jednoznačné. Javové `<` a `>` jednoznačné nejsou — v inicializátoru je to porovnání, v deklaraci typový argument —, takže hloubku generických typů si počítá javová čtečka sama v `ReadTypeArguments`, kde kontext zná, proti témuž číslu.

**Proč `Failure`, a ne čtyřistovka.** Je to týž tvar, jakým všech pět parserů hlásí syntaktickou chybu (rozhodnutí 062; u T-SQL je to věta, kterou S7 dostává od `TSql160Parser`), takže nepřibývá druh hlášení, který by se uživatel musel učit. Vazba na jednotku a pozice v ní je přesně to, co žádá S7. A protože je to záznam, ne výjimka, platí rozhodnutí 045: ostatní jednotky téhož převodu se přeloží a částečný výsledek odejde. Výjimka by naopak sebrala výsledek i jim — je to vada, kterou u neparsovatelného XML nese samostatná otevřená položka, a tohle rozhodnutí ji nepředjímá, jen se k ní nepřidává.

**Proč je nastavitelný, a proč jedině provozovatelem.** Strop odmítne i vstup, který by se přeložil — to je jeho cena (viz Důsledky) — a nástroj, který takovou cenu účtuje, musí mít pro ten případ východisko jinde než v překladu nástroje. Platforma to dělá všude stejně: `XmlReaderSettings.MaxDepth`, `JsonSerializerOptions.MaxDepth` i limit velikosti těla v Kestrelu jsou čísla s dokumentovanou výchozí hodnotou, ne konstanty. Nastavení je tedy součástí volby, ne ústupkem od ní, a smí dojít až k vypnutí: kdo strop vypne, přebírá zpátky hrozbu 2 celou i s pádem procesu, a je to jeho informované rozhodnutí o vlastní instanci.

Rozhodující je, **odkud** ta hodnota smí přijít. Čte se jednou při startu z nastavení aplikace a k parserům putuje touž cestou, jakou k nim chodí deklarovaný dialekt zdroje (rozhodnutí [088](088-a-declared-foreign-source-dialect-is-not-read.md)) — parametrem `ConversionHandler.Convert` do `ParserFactory` —, ale na rozdíl od dialektu **nepochází z těla požadavku a nesmí v něm mít pole**. Kdyby ho tam mělo, byl by strop ozdoba: vstup, proti kterému má chránit, by si s sebou nesl i svolení ho překročit. Provozovatel a odesílatel jsou v předpokladu nasazení (`threat-model.md`, důvěryhodná síť) často tentýž člověk, ale to je fakt o provozu, ne o kontraktu, a kontrakt ty dvě role rozlišovat musí.

Výchozí hodnota je 128 a je to hodnota, proti které se vyslovuje všechno ostatní: testy, věta v `architecture.md` i záruky §9. Nastavení číslo posouvá, netvrdí nic nového.

**Kde číslo bydlí.** V `AbstractWrappers`, které referencuje každý projekt, jenž něco čte: `TransactSql`, `CSharpEntityParsing`, `LinqParsing`, `JavaEntityParsing`, `JakartaPersistence` i oba wrappery s vlastním dotazovým parserem. „Vysloveno jednou pro všechny tři" z položky je tím splněné a rozšířené — drží se ho i ty dva parsery, které naše nejsou.

## Důsledky

**Ohraničí se i emisní strana, aniž bychom na ni sáhli.** Podmínkový strom vzniká jedině čtením a buildery ho obcházejí rekurzivními visitory (§5 architektury). Strom, který prošel zastropovaným parserem, je hluboký nejvýš tolik, kolik strop dovolil, takže visitor nemá jak přetéct; vnořování `Push()`/`Pop()` v `AbstractQueryBuilder` se drží téhož čísla, protože poddotaz je v textu závorka. Platí to ale jen potud, pokud strop platí — na instanci s vypnutým stropem přetéká emisní strana zrovna tak jako čtecí, a je to další důvod, proč je vypnutí volba provozovatele, ne výchozí stav.

**Hrozba 2 se zmenší, nezmizí.** Strop odpovídá na pád procesu, ne na pomalou odpověď: počet a velikost jednotek zůstávají neomezené a horní mez jim dává pořád jen výchozí nastavení Kestrelu. Vlastní limit velikosti je dál otevřená položka a tohle rozhodnutí ji neřeší. Mění se to, že z hrozby 2 odchází jediná její část, která končila jinak než pomalu.

**Co se vysloví, až bude práce hotová.** Rozhodnutí zapisuje volbu, ne její provedení. Do té doby to [`open-items.md`](../open-items.md) nese jako práci a `threat-model.md` popisuje dnešek pravdivě. Po implementaci se v hrozbě 2 nahradí věta o pádu procesu větou o odmítnutém vstupu, §5.1 architektury dostane nový důvod `Failure` a S7 začne platit i pro tuhle třídu vstupu. Žádná z šesti vyňatých oblastí §9 se nehýbe — tahle vada v žádné z nich není a nikdy nebyla, a to je samo o sobě důvod neodkládat ji donekonečna.

**Záznam běhu musí strop vydat, a je to kvůli S2.** Determinismus podle S2 je slíbený „při téže verzi nástroje", a nastavitelný strop je druhá věc, na které výsledek závisí: táž jednotka projde na jedné instanci a na druhé skončí `Failure`, aniž by se verze lišila. `ConversionResult` proto nese účinnou hodnotu stropu vedle `ToolVersion` a `TargetDatabaseDialect` — týmž způsobem a ze stejného důvodu, z jakého tam rozhodnutí [086](086-target-database-dialect-declared-by-the-descriptor.md) a 088 daly dialekt. Kdo výsledek dostane, ať vidí, proti jakému stropu vznikl; bez toho by nastavitelnost tiše podrývala S2 místo toho, aby ho jen doplnila o druhý parametr.

**Cena, kterou platíme.** Vstup hlubší než nastavený strop, který by se dnes přeložil, se odmítne. Připouštíme to a za ztrátu to nepovažujeme: takový vstup nikdo nenapsal rukou a žádný z našich builderů ho nevydá — a kdyby se přece objevil, není to slepá ulička, nýbrž důvod sáhnout do nastavení. Že je cena vratná, ovšem neznamená, že je nulová; výchozí chování nástroje takový vstup odmítne a tvrdíme to o něm nahlas, ne až v poznámce pod čarou. K tomu přibývá druhá lexikální fáze u jednotek C# a SQL; lexer je lineární a vstupy malé, takže na měřeném limitu S3 (100 entit a 100 dotazů do 30 s) se to nemá jak projevit — a kdyby se projevilo, je to měřitelné a vratné u jednoho konkrétního parseru, protože číslo je jedno, ale místa vynucení jsou na sobě nezávislá.

**Zkouší se to tam, kde se to změřilo.** K rozhodnutí patří test na každý zastropovaný tvar z tabulky výše: hloubka 128 projde, hloubka 129 je `Failure` s pozicí. K tomu dva testy na nastavení — snížený strop odmítne dřív a záznam běhu nese jeho hodnotu — a žádný na strop vypnutý, protože ten se dokládá jedině vstupem, který by proces shodil. Výš než na výchozí hodnotu sada sáhnout nesmí: přetečení zásobníku nezhasne jeden test, nýbrž celý běh.

## Historie

**2026-09-21 — strop je nastavitelný provozovatelem.** Původní znění říkalo, že strop nastavitelný není, a odůvodňovalo to tím, že by si jím provozovatel mohl instanci shodit. Text jsme opravili na místě, protože rozhodnutí ještě není naimplementované a volba sama — jedno číslo, vynucené nad tokeny před sestupem, překročení jako `Failure` s pozicí — platí beze změny; mění se jedna věta o tom, odkud hodnota přichází. Doplnil se případ, se kterým původní znění nepočítalo: vstup, který strop odmítne a přeložit by se dal, nemá kam jít, a nástroj pro to musí mít východisko jinde než ve vlastním překladu. Spolu s tím přibyla hranice, kterou nastavitelnost potřebuje, aby nebyla ozdobou (hodnotu smí změnit provozovatel při startu, ne požadavek), a dva důsledky, které z ní plynou: účinná hodnota patří do záznamu běhu kvůli S2 a na instanci s vypnutým stropem není ohraničená ani emisní strana.
