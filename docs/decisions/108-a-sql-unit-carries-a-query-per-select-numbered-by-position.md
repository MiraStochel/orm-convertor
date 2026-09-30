# 108 — Holá jednotka SQL nese dotaz za každý `SELECT` a nepojmenované dotazy čísluje pořadím

Datum: 2026-09-30
Stav: platí
Požadavky: F8, F11, F14, S1, S2, S7
Podklad: rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md), [047](047-content-type-reaches-the-query-parser.md), [066](066-records-attributed-to-the-input-unit.md), [069](069-major-marks-a-milestone-not-a-break.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [082](082-t-sql-read-and-written-by-a-shared-project.md) a [084](084-mybatis-wrapper-over-the-shared-sql-reading.md); oprava z 2026-09-21 ([`architecture.md`](../architecture.md), §5); README Dapperu, oddíl *Multiple Results*; dokumentace MyBatisu k atributu `resultSets` prvku `<select>`

## Kontext

**Sdílená čtečka T-SQL přečte z textu jeden `SELECT` a cokoli vedle něj odmítne.** Do 2026-09-21 brala první `SELECT` a zbytek textu ignorovala, takže `DELETE FROM T; SELECT …` odcházelo jako artefakt pro čtení a o smazání neřeklo nic. Oprava z toho dne udělala z každého příkazu vedle překládaného `SELECT`u záznam `Failure`, který příkaz jmenuje klíčovým slovem, a artefakt nevzniká (`SqlQueryReader.Read`, podle rozhodnutí 070). To je bezpečná polovina odpovědi. Jenže táž věta odmítá i text, ve kterém nic nepíše: `SELECT … FROM Customers; SELECT … FROM Orders` skončí týmž záznamem — „translating the SELECT alone would hand over less than the source says" — a z jednotky, jejíž každou část nástroj umí přeložit, nevyjde nic.

**Rozhodnutí 081 dalo jednotce právo nést víc dotazů a pojmenovávání vyřešilo jen pro zdroje, které jméno nesou.** `IQueryParser.Parse` vrací builder na každý přečtený dotaz a hbm.xml i mapper MyBatisu toho využívají. Pevný název metody `Query` se sám se sebou nesrazí proto, že podle 081 „jednotka s víc než jedním dotazem je právě ta jednotka, jejíž dotazy zdroj pojmenoval" (`<query name>`, `@NamedQuery(name)`, `<select id>`); kde jméno je, vypíše ho builder místo `Query` a záznam z dotazu ho nese v poli `Query`. `SELECT` jméno nenese. Kdyby se dva `SELECT`y jedné jednotky četly jako dva dotazy, jméno by jim musel dát nástroj — a rozhodnutí 028 zakázalo nástroji vymýšlet fakta, která nezná.

**Text SQL přichází čtyřmi cestami a jen v jedné z nich je sám jednotkou.** Sdílenou čtečku volají čtyři parsery (rozhodnutí 082 a 084):

| Cesta | Kdo text spouští | Co se stane s druhým výsledkem |
|---|---|---|
| holá jednotka `SqlQuery` (Dapper wrapper) | nikdo — jednotka je obsah souboru `.sql` | — |
| řetězcový literál volání `Query<T>` v jednotce `CSharpQuery` (Dapper wrapper, rozhodnutí 047) | jedno volání Dapperu | `Query<T>` mapuje první výsledkovou sadu a ostatní dočte naprázdno; víc sad čte jedině `QueryMultiple` (README Dapperu, *Multiple Results*) |
| tělo `<select>` nebo anotace mapperu MyBatisu | jeden příkaz MyBatisu | mapuje se první sada, pokud `<select>` atributem `resultSets` nejmenuje další |
| `<sql-query>` v hbm.xml NHibernate | jeden pojmenovaný dotaz | jeden výsledek na dotaz |

V posledních třech cestách je text argumentem jedné konstrukce hostitelského frameworku, která ho pošle serveru jako jeden příkaz a přebírá jeden výsledek. V první cestě žádný hostitel není: jednotka je soubor, a jazyk T-SQL říká, co soubor s několika příkazy znamená — dávku, jejíž příkazy běží v pořadí a jejíž každý `SELECT` vrací vlastní výsledkovou sadu.

**Z téhož pravidla jazyka plyne, kdy příkazy skriptu nezávislé nejsou.** Příkaz, který mění stav, mění i to, co čtou příkazy za ním: `SELECT … INTO #t FROM …; SELECT * FROM #t` čte tabulku, kterou teprve první příkaz vytvořil, `DECLARE @id int = 5; SELECT … WHERE Id = @id` čte lokální proměnnou, ne parametr dotazu. Mezi takové příkazy patří i `SELECT`, který přiřazuje do proměnné (`SELECT @x = MAX(Id) FROM T`) — a ten dnes neodmítá nikdo: čtečka zahodí prvek, který není skalárním výrazem, se záznamem `Loss` kategorie `Projection`, a protože jiná projekce nezbyla, vyjde `SELECT * FROM T`. Zdroj přitom žádné řádky nevrací, takže je to přesně jiná množina řádků, kterou rozhodnutí 070 zakazuje.

Otázka tedy zní, jestli se `SELECT`y jedné jednotky mají číst jako samostatné dotazy, v kterých cestách, a čím se pojmenují.

## Zvažované varianty

### 1 — Nechat odmítnutí, jak je

Dnešní stav; jednotka SQL nese právě jeden `SELECT`. Zamítáme. Odmítnutí bylo odpovědí na tichý zahozený příkaz a svou práci dělá, ale jeho zdůvodnění platí jen pro text, ve kterém se něco píše, nebo pro čtení, které by vzalo `SELECT` jediný. Pro soubor několika čtecích dotazů — typicky sadu reportů v jednom `.sql` — odmítá vstup, jehož každou část nástroj přeloží, a nutí uživatele soubor rozřezat. F14 přitom žádá vkládat, co uživatel má, S7 dokončit základní scénář bez ručních mezikroků a 081 už jednou odmítlo řezání na straně klienta jako cestu, jak obejít, co má umět nástroj.

### 2 — Číst víc dotazů a jméno odvodit z obsahu

Nástroj by dotaz pojmenoval podle toho, co dělá — podle tabulky klauzule `FROM` (`FindCustomers`) — nebo podle komentáře před ním, jak to dělají knihovny, které jména dotazů v souborech SQL vedou (`-- name: find-by-id` u yesql, HugSQL či sqlc). Zamítáme obojí. Jméno z obsahu je odhad o záměru autora, přesně to, co 028 zakázalo, a ani nerozlišuje: dva dotazy nad touž tabulkou, v souboru reportů pravidlo, dostanou totéž jméno a číslo stejně potřebují. Konvence komentářů není vlastnost T-SQL ani Dapperu, nýbrž cizích nástrojů; číst ji znamená přijmout dialekt, který jednotka nedeklarovala (rozhodnutí 047) — a soubor bez ní by stejně potřeboval náhradní jméno, tedy odpověď varianty 4.

### 3 — Číst víc dotazů ve všech čtyřech cestách

Dva `SELECT`y v literálu volání Dapperu, v těle `<select>` nebo v `<sql-query>` by se také rozpadly na dva dotazy. Zamítáme. Tam text spouští jedna konstrukce hostitele a ta vrací jeden výsledek; dvě vygenerované metody by tvrdily, že zdroj klade dva dotazy, kde klade jeden, a výsledek druhého `SELECT`u nikdo nečte. Přeložit místo toho jen první sadu by zase byl výrok o tom, kterou sadu každý hostitel mapuje — u Dapperu první, u MyBatisu podle `resultSets`, u NHibernate podle poskytovatele —, a ten bychom tvrdili za tři frameworky bez ověření. Takový text nikdo nepíše schválně a odmítnutí se jmenovaným důvodem je u něj pravdivá odpověď, kterou dává už dnes.

### 4 — Holá jednotka je skript; nepojmenovaný dotaz dostane pořadí

## Rozhodnutí

**Volíme variantu 4. Holá jednotka SQL je skript a každý její `SELECT` je samostatný dotaz. Nese-li jednotka víc než jeden dotaz, dostane každý jméno `Query` s pořadovým číslem na dvě desetinné číslice — `Query01`, `Query02`, … Příkaz, který mění stav, odmítá celou jednotku. Text vložený do konstrukce hostitelského frameworku zůstává jedním příkazem.**

**Skript se čte tak, jak ho čte jazyk.** Čtení jednotky `SqlQuery` se řídí tím, co o dávce říká T-SQL: příkazy v pořadí textu, i přes oddělovač `GO`, každý `SELECT` s vlastní výsledkovou sadou. Dotazy skriptu jsou proto nezávislé, stejně jako `<select>`y jednoho mapperu: každý má vlastní builder z továrny orchestrace a vlastní čtečku (081), a `SELECT`, který čtení odmítne — nad CTE, s `FOR XML`, s konstrukcí mimo slovník —, odmítá jen sám sebe. Jednotka je jalová jen tehdy, když z ní nevyšel žádný dotaz (rozhodnutí 066 ve znění 081). Syntaktická chyba kdekoli v textu zůstává odmítnutím celé jednotky se svým řádkem a sloupcem: gramatika vrací chyby za skript a o hranici příkazů za chybou nic spolehlivého neříká.

**Pořadí je fakt zdroje, a proto jméno z něj složené 028 neporušuje.** Rozhodnutí 028 zakázalo vypsat název sestavení, protože by tvrdil něco o konzumentském projektu, co převod neví. `Query01` netvrdí nic o světě: říká „první dotaz téhle jednotky" a pořadí příkazů je vlastnost textu, kterou uživatel napsal a kterou vidí. Pevné jméno `Query` přitom nástroj volí od začátku a 081 ho přijalo jako náhradu za jméno, které zdroj neuvedl; číslo k té náhradě přidává jedinou informaci, která dotazy jedné jednotky odlišuje. Kde zdroj jméno uvedl, platí dál jeho jméno a číslování se ho netýká.

**Dvě číslice, protože řazení podle jména má dát pořadí v textu.** Artefakty se uživateli ukazují a stahují jako soubory a metody seřazené podle jména (S7); s jednou číslicí by `Query10` stálo před `Query2`. Dvě číslice drží lexikální pořadí shodné s pořadím v textu do devadesáti devíti dotazů. Nad tou hranicí číslo prostě přeteče na tři číslice — jméno zůstává jednoznačné a jen řazení přestane sedět, což je pro soubor o stovce dotazů cena, kvůli které nemá smysl jednotku odmítat.

**Číslo se přiděluje před čtením, ne po něm.** Číslují se všechny `SELECT`y jednotky od jedničky v pořadí textu, včetně těch, které čtení odmítne. Jméno dotazu tak nezávisí na tom, co nástroj umí přečíst: když druhý dotaz spadne, třetí zůstane `Query03`, a když budoucí verze konstrukci druhého dotazu přečte, třetí se nepřejmenuje. S2 to žádá mezi běhy jedné verze; napříč verzemi je to rozdíl mezi výstupem, který se rozšíří, a výstupem, který se přečísluje.

**Jednotka s jediným `SELECT`em se nemění.** Její dotaz jméno nedostane a metoda se jmenuje `Query` jako dnes. Číslo slouží k rozlišení a u jediného dotazu není co rozlišovat; číslovat ho by změnilo každý artefakt zdroje Dapper v maticích i ve vzorcích a nepřidalo by nic. Cena je vědomá: soubor, do kterého uživatel připíše druhý `SELECT`, přejmenuje metodu prvního z `Query` na `Query01`. Je to jiný vstup, takže S2 to neodporuje.

**Záznam jmenuje dotaz týmž jménem, pod kterým stojí ve výstupu.** Pole `Query` záznamu nese u číslovaného dotazu `Query01` — ne proto, že by to jméno napsal zdroj, ale proto, že pole slouží ke spárování záznamu s artefaktem a u nepojmenovaného dotazu je tohle jediné jméno, pod kterým ho uživatel najde. Cíl zapíše jméno svým pravopisem jako u každého jiného (`QueryMethodNaming`): .NET cíle `Query01`, javové `query01`, stejně jako se `find-by-id` zapisuje `FindById` a `findById`.

**Příkaz, který mění stav, odmítá celou jednotku, a zjistí se dřív, než se přečte kterýkoli dotaz.** Nezávislost dotazů skriptu platí jen tehdy, když v něm nic nepíše. Jednotku proto odmítá — jedním záznamem `Failure`, který příkazy jmenuje klíčovým slovem, jako dnes — každý příkaz, který není `SELECT`, a každý `SELECT`, který píše: `SELECT … INTO` a `SELECT` s přiřazením do proměnné. Hranice je „není čtecí `SELECT`", ne výčet nebezpečných příkazů: `SET NOCOUNT ON` řádky nemění, `USE` mění databázi, ze které čtou všechny následující dotazy, a udržovat seznam neškodných voleb by byla práce bez užitku pro převod dotazů. Pravidlo platí ve všech čtyřech cestách a zavírá i jednopříkazový případ z kontextu: `SELECT @x = MAX(Id) FROM T` přestane odcházet jako `SELECT * FROM T`.

**Skript od příkazu rozlišuje wrapper, ne čtečka.** Zda je text celou jednotkou, nebo argumentem konstrukce hostitele, je fakt zdrojového frameworku, a podle S1 ho tedy vyslovuje wrapper: sdílené čtení dostane text i s údajem, jestli smí nést víc `SELECT`ů. Dapper wrapper ho předá u jednotky `SqlQuery` a nepředá u `CSharpQuery`; MyBatis wrapper a hbm.xml parser NHibernate ho nepředávají nikdy. Ve všech třech vložených cestách proto druhý `SELECT` končí dnešním odmítnutím.

## Důsledky

**Podle rozhodnutí 069 je to MINOR navenek.** Jednotka, která dosud skončila `Failure`, vydá artefakty; pole odpovědi ani požadavku nepřibývá ani neubývá. Uvnitř řešení se mění `SqlQueryReader`: rozdělí se na krok nad celým textem — stráž dialektu (rozhodnutí [088](088-a-declared-foreign-source-dialect-is-not-read.md)), stráž hloubky ([092](092-input-nesting-depth-capped-before-the-descent.md)), gramatika, kontrola příkazů, které mění stav — a na čtení jednoho `SELECT`u do jednoho builderu, které si dnešní tvar „jedna čtečka na dotaz" nechá. Dapper wrapper pak pro každý `SELECT` vyžádá builder z továrny a nastaví mu jméno, jen je-li jich víc než jeden.

**Pravidlo 081 o jménech se rozšiřuje o jeden případ a 081 platí dál.** Jednotka s víc než jedním dotazem je buď jednotka, jejíž dotazy pojmenoval zdroj, nebo holá jednotka SQL, jejíž dotazy čísluje pořadí; pevné `Query` se v obou případech samo se sebou nesrazí, protože stojí jedině u jednotky s jediným dotazem. Číslované jméno se nesrazí ani s pojmenovaným, protože jednotka SQL pojmenované dotazy nenese. Srážky mezi jednotkami se nemění: dvě holé dotazové jednotky vydávají `Query` obě už dnes, a to je odložená položka o párování výstupu se vstupní jednotkou, kterou 081 jmenuje. Dokumentační komentáře `QueryMethodNaming` a `AbstractQueryBuilder.QueryName`, které větu 081 opakují, se při implementaci přepíší.

**Chování ostatních vstupů se nemění.** Jednotka s jediným čtecím `SELECT`em vydá tentýž artefakt i tytéž záznamy, takže matice směrů, matice kategorií i diferenční matice procházejí beze změny očekávaných výstupů — což je zároveň první ověření změny. Mění se jen dvě skupiny vstupů: skript několika `SELECT`ů, který dosud skončil odmítnutím, a `SELECT` s přiřazením do proměnné, který dosud vyšel jako jiný dotaz.

**Testy.** Skript dvou `SELECT`ů vydá do Dapperu, EF Core i javového cíle dva dotazy pojmenované `Query01`/`Query02`, resp. `query01`/`query02`; odmítnutý druhý ze tří nepřečísluje třetí a jeho záznam nese `Query02`; `SELECT` vedle `DELETE`, `SELECT … INTO` před `SELECT`em a samotný `SELECT @x = …` jsou odmítnutí celé jednotky bez artefaktu; jediný `SELECT` dál vydá metodu `Query`; dva `SELECT`y v literálu volání Dapperu, v `<select>` MyBatisu a v `<sql-query>` NHibernate končí dnešním odmítnutím.

**Co tohle rozhodnutí neřeší.** Jednotku `CSharpQuery` s víc voláními Dapperu: wrapper z ní dnes vezme literál prvního volání, na které narazí, a o dalších mlčí. Je to táž otázka o jiném jazyce — víc nepojmenovaných dotazů v jedné jednotce — a číslování pořadím by na ni odpovědělo stejně, ale volání v C# fragmentu nejsou příkazy dávky a jejich pořadí a nezávislost se musí posoudit zvlášť. Neřeší ani celý vložený zdroj, ve kterém je vedle entity repozitář s dotazy; to je samostatné rozhodnutí o tom, kdo rozřeže soubor s dvěma rolemi.
