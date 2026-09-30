# 106 — Holý parametr za `IN` je kolekční parametr Dapperu a wrapper ho odloupne před gramatikou

Datum: 2026-09-30
Stav: platí
Požadavky: F10, F11, F13, T2, S1, S2
Podklad: rozhodnutí [025](025-query-language-as-content-type.md), [028](028-assembly-name-is-not-ours-to-invent.md), [040](040-boundary-of-the-handed-over-artifact.md), [047](047-content-type-reaches-the-query-parser.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [083](083-parameter-as-the-fifth-operand-shape.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [092](092-input-nesting-depth-capped-before-the-descent.md), [102](102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md) a [105](105-a-query-formulates-its-own-demand-on-the-catalog.md); JSS §4.3; README Dapperu, oddíl *List Support*; první běh zdrojové varianty `CollectionParameter` z Dapperu na 4. stupni (2026-09-30) a poznámka nad touž sekcí v `Tests/Database/QueryShapes/categories.txt`; [`use-cases.md`](../use-cases.md), věta o dotazu uvnitř service třídy

## Kontext

**Kolekční parametr má v mezireprezentaci tvar a každý zdroj ho hláskuje po svém.** Rozhodnutí 083 zavedlo parametr jako pátý tvar operandu a kolekci jako jeho příznak (`QueryParameter.IsCollection`), s jediným povoleným místem — pravou stranou `IN`, kam ho rozhodnutí 074 poslalo jmenovitě. Zdroje ho vyslovují každý svým slovem a parser každého z nich to slovo zná: sdílený LINQ parser čte holý identifikátor jako příjemce `Contains` (`ids.Contains(ol.ProductId)`), HQL a JPQL parser `in (:ids)`, MyBatis wrapper `<foreach>` v kanonickém tvaru. Skalár prvků nikdo z nich neuvádí; doplní ho brána šablony ze sloupce na druhé straně porovnání (083), a kde zdroj jmenuje tabulku bez vazby na třídu, dodá vazbu katalog (105).

**Dapper má slovo také, a je zapsané na obou stranách nástroje.** README Dapperu v oddílu *List Support* dokumentuje tvar `WHERE Id IN @Ids` — holý parametr za `IN`, bez závorek — a říká, co s ním Dapper udělá před odesláním na server: `@Ids` nahradí výčtem `(@Ids1, @Ids2, @Ids3)`, závorky včetně. Rozhodnutí 083 tenhle tvar uvedlo ve své tabulce jako hláskování Dapperu a sdílený zapisovač T-SQL ho tak píše (`SqlQueryVisitor`, s poznámkou, že „je to tvar, který Dapper rozepíše do seznamu"): kolekční parametr z kteréhokoli zdroje vyjde do Dapperu jako `IN @ids` a do signatury metody jako `IEnumerable<int> ids`. Strana zápisu tedy Dapperovo slovo zná od 2026-09-18.

**Strana čtení ho nezná.** Dapper wrapper vezme text — buď celou jednotku `SqlQuery`, nebo řetězcový literál z volání `Query<T>` v jednotce `CSharpQuery` (047) — a předá ho sdílenému čtení tak, jak je, s prázdnými fakty o parametrech (`statedParameters: null`). Sdílené čtení je gramatika T-SQL (ScriptDom, rozhodnutí 082) a ta o Dapperu neví nic, takže s oběma možnými texty dopadne špatně, pokaždé jinak:

| Text ve zdroji Dapper | Co s ním udělá Dapper za běhu | Co přečte nástroj dnes |
|---|---|---|
| `WHERE ol.ProductId IN @ids` | rozepíše na `IN (@ids1, @ids2, …)` a dotaz běží | syntaktická chyba gramatiky, `Failure`; artefakt nevzniká |
| `WHERE ol.ProductId IN (@ids)` s jednou hodnotou v `ids` | dotaz běží jako porovnání s jednou hodnotou | jednoprvkový výčet hodnot s vázaným skalárem (102), metoda bere `int ids` — správně |
| `WHERE ol.ProductId IN (@ids)` s kolekcí v `ids` | rozepíše na `IN ((@ids1, @ids2, …))`, což není platné T-SQL, a dotaz spadne | totéž co řádek výš: `int ids` |

Druhý a třetí řádek říkají, že gramatika nemůže nic jiného: text je v obou týž a rozdíl leží v hodnotě, kterou dodá volající. Přesně to zapsala revize rozhodnutí 084: „`IN (@ids)` je pro gramatiku jednoprvkový výčet hodnot a pro každého jiného čtenáře jazyka přesně to; odkud by se kolekčnost vzala, není v textu." Proto vznikl kanál `SqlParameterFacts` — fakt o textu, který wrapper ze zdroje odloupne dřív, než ho gramatika uvidí, a který „stojí v téže pozici jako hlásicí kanál". MyBatis wrapper jím kolekčnost posílá z `<foreach>`, a jeho dokumentační poznámka to říká otevřeně: nic jiného dnes mapu nenaplňuje.

**První řádek je ten, který nás bolí, a našla ho diferenční matice.** Kategorie `CollectionParameter` měla zdroj Dapper v manifestu od začátku, se souborem `… IN (@ids)` a s odmítnutím `refusedFrom = Dapper:QueryParameter`, a nikdy se nepřečetla dál než po to odmítnutí. Rozhodnutí 105 odmítnutí parametru zdroje Dapper s katalogem zrušilo, zdrojová varianta se 2026-09-30 poprvé rozběhla — a harness, který kategorii váže s argumentem `ids:int[]=2;4`, spadl na generované metodě s `int ids`. Řádek z kategorie odešel s poznámkou nad sekcí manifestu, protože zdroj, který kategorii nevysloví, v ní podle pravidla manifestu není. Od té chvíle:

- řádek `CollectionParameter` stojí v matici kategorií i v diferenční matici na pěti zdrojích, a je to jediná kategorie z devatenácti, kde zdroj chybí z důvodu nástroje, ne jazyka;
- Dapper → Dapper s kolekčním parametrem není doložený nikde: nástroj text `IN @ids` vydává a týž text nepřečte, takže důkaz rozhodnutí 082, že „týž text SQL přečtený z Dapper jednotky, ze `<sql-query>` a z mapperu MyBatisu dá tutéž mezireprezentaci", má u téhle konstrukce díru;
- §9 o kolekčním parametru zdroje Dapper mlčí, ač o skalárním parametru téhož zdroje mluví.

**Otevřená položka otázku položila jako čtení C# volání.** Fakt, že `ids` je seznam, v jednotce `CSharpQuery` zpravidla je: `connection.Query<ShopOrderLine>("…", new { ids })` uvnitř metody s parametrem `IEnumerable<int> ids`. Po vzoru 084, kde MyBatis wrapper čte skalár ze signatury rozhraní mapperu, by Dapper wrapper mohl číst kolekčnost i skalár z typu vázané hodnoty. Otázka tedy zní, odkud má fakt o kolekčnosti přijít — a teprve potom, kdo ho gramatice předá.

## Zvažované varianty

### 1 — Nechat to tak a zapsat jako vyslovenou mez

Žádný kód; katalog podmnožiny by řekl, že kolekční parametr se ze zdroje Dapper nečte. Zamítáme. Nástroj by dál vydával text, který sám nepřečte, což je opak toho, co má round-trip dokládat (037, 082); tabulka rozhodnutí 083 by u Dapperu jmenovala hláskování, které platí jen na cestě ven; a matice T2 i F13 by nesly díru u jediného .NET zdroje s doslovným SQL, zaviněnou nástrojem. Mez podmnožiny má být hranice jazyků, ne hranice toho, na co došlo (viz úvod kategorie Dotazy v `open-items.md`).

### 2 — Číst fakta o parametrech z C# volání

Varianta, kterou položka navrhla: wrapper by si z `new { ids }` dohledal deklaraci `ids` — parametr obalující metody, lokální proměnnou — a z typu `IEnumerable<int>` vzal kolekčnost i skalár, jako MyBatis wrapper ze signatury mapperu. Zamítáme jako odpověď na tuhle otázku, z pěti důvodů, z nichž první dva stačí každý sám.

**Fakt o kolekčnosti v textu je.** Dapper hláskuje seznam holým parametrem a jednu hodnotu parametrem v závorkách; obojí je vidět ve větě `IN @ids` proti `IN (@ids)` bez jediného pohledu do C#. Volání by k odpovědi nepřidalo nic, co by text neřekl sám.

**Volání není v každé jednotce, a v důkazu není nikdy.** Jazyk jednotky je deklarace klienta (025, 047) a Dapper wrapper čte i holé `SqlQuery`. Tak vstupuje Dapper do matice kategorií i do diferenční matice (`dapper/*.sql`) a tak ho píše vzorek `CustomerSampleDapper`. Varianta by kategorii ze zdroje Dapper vyslovila jedině jednotkou `CSharpQuery`, tedy jiným druhem jednotky, než jakým Dapper vyslovuje osmnáct ostatních kategorií, a holé SQL by zůstalo na prvním řádku tabulky výš.

**Volání je kód volajícího, ne artefakt.** Hranice jednotky vede kolem artefaktu (040) a `use-cases.md` říká výslovně, že nástroj „nepozná dotaz uvnitř service třídy". Jednotka `CSharpQuery` je fragment, který wrapper obalí syntetickou třídou a čte bez kompilace; dohledat z použití `ids` jeho typ znamená vázat jména, a kde vazba selže — pole třídy, `var` bez inicializátoru, `DynamicParameters`, vlastnost DTO předaného místo anonymního objektu —, wrapper by buď mlčel, nebo hádal. Signatura rozhraní MyBatisu je něco jiného: je to deklarace, podle které MyBatis sám váže, nástroj ji má celou a je to „jediné místo, kde typ parametru v MyBatisu vůbec žije" (084).

**Typ by četl záměr volajícího, ne dotaz.** Text `IN (@ids)` s kolekcí v `ids` je podle tabulky výš dotaz, který u Dapperu spadne. Přečíst ho podle typu jako kolekční parametr by znamenalo vydat do každého cíle běžící dotaz z textu, který ve zdroji neběží — nástroj by tiše opravoval vstup, což jde proti 053 a 070, která chrání množinu řádků zdrojového dotazu, ne dotazu zamýšleného.

**Skalár z volání nepotřebujeme.** Skalár parametru zdroje Dapper dává od rozhodnutí 105 brána ze sloupce s vazbou tabulky z katalogu, tedy z faktu schématu. Typ odečtený z použití v C# fragmentu by byl třetí zdroj téhož faktu bez záruky, že je to typ, kterým Dapper skutečně váže; druhou větev rozhodnutí 083 — uvedený skalár s předností a `Conflict` při rozdílu — má naplňovat deklarace, a takovou Dapper nemá.

### 3 — Naučit holý tvar sdílené čtení T-SQL

Sdílená čtečka by `IN @ids` přijímala sama. Zamítáme podle rozhodnutí 082 a 084: gramatika je gramatika T-SQL a čteme ji jejím vlastním nástrojem, a `IN @ids` T-SQL není, je to Dapper. Sdílená čtečka slouží třem wrapperům a u zbylých dvou by přijetí holého tvaru bylo chybou: MyBatis text `IN #{ids}` se po substituci stane `IN @ids`, jenže MyBatis za `#{}` seznam nerozepíše a na server odešle `IN ?`, které spadne — dnešní syntaktická chyba je u něj správná odpověď a kolekční parametr by z něj udělal dotaz, který MyBatis neumí spustit. Rozhodnutí 084 navíc zásuvný bod gramatiky výslovně odmítlo: fakt o parametru cestuje vedle textu, ne skrz gramatiku.

### 4 — Dapper wrapper odloupne své hláskování před gramatikou

## Rozhodnutí

**Volíme variantu 4. Holý parametr bezprostředně za `IN` nebo `NOT IN` je v textu, který čte Dapper wrapper, kolekční parametr Dapperu. Wrapper ho před gramatikou přepíše na tvar se závorkami, který gramatika přijme, a sdílenému čtení předá vedle textu fakt, že parametr váže seznam — týmž kanálem `SqlParameterFacts` a týmž mechanismem, kterým MyBatis wrapper předává `<foreach>`. Z C# volání wrapper nečte nic: fakta o parametrech zdroje Dapper jsou fakta jeho SQL textu.**

Konkrétně:

- **Poznává se nad tokeny, ne nad textem.** Wrapper projde token stream lexeru ScriptDomu — týž, nad kterým rozhodnutí 092 hlídá hloubku zanoření, takže nic nového nevzniká — a hledá klíčové slovo `IN` následované tokenem proměnné (`@ids`), s ničím než bílými znaky a komentáři mezi nimi. Závorka mezi nimi je druhý tvar a řetězcový literál je jeden token, takže `'… IN @x'` uvnitř konstanty se nedotkne. Text se přepíše jen v tom místě, `@ids` na `(@ids)`, a do mapy faktů přibude `ids → SqlParameterFacts(IsCollection: true)`; pak jde text gramatice jako dosud. Je to přesně krok `AppendForEach` MyBatis wrapperu, jen o jednu značku kratší: MyBatis nahradí celou značku `(@ids)` a zapne příznak, Dapper wrapper doplní závorky a zapne příznak.
- **Platí pro obě cesty do wrapperu.** Holá jednotka `SqlQuery` i literál vytažený z volání v jednotce `CSharpQuery` projdou týmž krokem, protože oba jsou týž jazyk — T-SQL s jedním Dapperovým slovem navíc. Volání samo wrapper dál nečte; z `new { ids }` nebere nic, stejně jako dosud.
- **Tvar se závorkami se čte dál podle rozhodnutí 102.** `IN (@ids)` zůstává výčtem hodnot s vázaným skalárem a metoda bere `int ids`. Není to ústupek gramatice, je to pravda o Dapperu: parametr v závorkách je u něj jedna hodnota a dotaz s ní běží, kdežto seznam v závorkách spadne. Nástroj tak oba texty čte jako dotaz, který Dapper skutečně provede, a žádný neopravuje. Totéž jméno jednou holé a jednou v závorkách nebo mimo `IN` odmítne brána už dnes („jedno jméno pro dvě vazby"; kolekční parametr mimo pravou stranu `IN`), nic se k tomu nepřidává.
- **Skalár se neuvádí.** Wrapper posílá jen kolekčnost; skalár prvků doplní brána ze sloupce (083) a vazbu tabulky, kde chybí, katalog (105) — stejně jako u `ids.Contains(ol.ProductId)` z LINQ, kde parser také posílá příznak bez typu. Bez katalogu se kolekční parametr zdroje Dapper netypuje, ze stejného důvodu a s týmž záznamem jako skalární.
- **Záznam nevzniká.** Holý tvar je dokumentovaná syntaxe zdrojového frameworku, čtená tak, jak je napsaná; není to konvence ve smyslu 067 ani odhad, a MyBatis wrapper u `<foreach>` také nic nehlásí. Že jde o Dapperovo slovo a ne T-SQL, je vidět z toho, kde krok bydlí.
- **Bydlí v `DapperWrappers` a nikde jinde.** `TransactSql`, `Model`, `AbstractWrappers` ani orchestrace se nemění; kanál faktů z 084 dostává druhého producenta, kterého jeho dokumentační poznámka předvídala. Krok zápisu se nemění vůbec, protože sdílený zapisovač holý tvar píše už od 083 — čtení se jen dorovnává zápisu, a Dapper → Dapper tím kolekční parametr převede na text totožný se vstupem.

Věta pro §9 zní: *kolekční parametr zdroje Dapper je holý parametr za `IN`, jak ho Dapper dokumentuje a rozepisuje; parametr v závorkách je jedna hodnota. Skalár prvků se bez katalogu netypuje.*

## Důsledky

**Dapper se vrací jako šestý zdroj kategorie `CollectionParameter`.** Řádek manifestu dostane zpět soubor `dapper/FindCollectionParameter.sql`, tentokrát s textem `… IN @ids`, a odmítnutí vázané na běh bez katalogu (`refusedWithoutCatalog = Dapper:QueryParameter`), stejně jako řádek `ScalarParameter`; poznámka nad sekcí, která nepřítomnost vysvětlovala, odejde. Matice kategorií i diferenční matice tím u téhle kategorie stojí na šesti zdrojích a věta o hotovém 4. stupni v kategorii Dotazy ztrácí poslední výjimku zaviněnou nástrojem. Zdrojová varianta, na které to 2026-09-30 spadlo, dostane metodu s `IEnumerable<int> ids`, kterou harness s `2;4` naváže a kterou Dapper za běhu rozepíše.

**Pro uživatele se čte, co se dosud odmítalo.** `WHERE ol.ProductId IN @ids` ze zdroje Dapper vyjde do EF Core jako `ids.Contains(ol.ProductId)`, do NHibernate jako `in (:ids)` se `SetParameterList`, do Hibernate a EclipseLinku jako `in (:ids)`, do MyBatisu jako `<foreach>` a do Dapperu beze změny; `NOT IN @ids` jako negace téhož (074). Text `IN (@ids)` se čte jako dosud, a je to správně.

**Důkaz rozhodnutí 082 se uzavírá i na téhle konstrukci.** Týž dotaz vyslovený pěti slovy — `IN @ids` z Dapperu, `<foreach>` z MyBatisu, `in (:ids)` z HQL a JPQL, `ids.Contains` z LINQ — dá tutéž mezireprezentaci; test identity čtení přes čtenáře SQL dostane řádek s kolekčním parametrem u obou zdrojů, které ho v textu vysloví — Dapper holým tvarem, MyBatis značkou `<foreach>` —, zatímco `<sql-query>` NHibernate ho v textu nevysloví, protože kolekčnost tam říká až volání `SetParameterList`, které v jednotce není.

**Dokumentace.** §9 dostane větu výš a řádky F13 a T2 trasovatelnosti ztratí u `CollectionParameter` výhradu ke zdroji Dapper. `architecture.md` §5 popíše krok Dapper wrapperu vedle kroku MyBatisu a dokumentační poznámka `SqlParameterFacts` přestane říkat, že mapu naplňuje jen MyBatis.

**Pro sedmý framework se nemění nic** (S1). Mění se jediný wrapper. Stejný vstup dá stejný výstup, protože krok je čistě textový a deterministický (S2).

**Číslo vydání** se podle rozhodnutí [098](098-the-number-is-decided-once-per-release.md) určí až při vydání; povahou je to změna, po níž vstupy, které dosud končily `Failure`, vydají artefakt, a rozhraní ani tvar odpovědi se nemění.

**Co tohle rozhodnutí neurčuje.** Nic o parametru jako argumentu řetězcové metody LINQ; to je samostatná položka a smí skončit jinak. Nic o tom, jestli by `IN (@ids)` měl nést záznam upozorňující, že Dapper by seznam v tomhle tvaru nerozepsal: text je platný a s jednou hodnotou běží, takže dnes nic nehlásíme.

**Testy.** Čtení `IN @ids` z holé jednotky i z jednotky `CSharpQuery` dá kolekční parametr se skalárem ze sloupce; `NOT IN @ids` jeho negaci; `IN (@ids)` dál výčet s vázaným skalárem; `IN @ids` uvnitř řetězcového literálu nechá literál na pokoji a žádný parametr nezaloží; totéž jméno holé a v závorkách dá `Failure` brány. Round-trip Dapper → Dapper vrátí text s `IN @ids` beze změny. Test identity čtení přes čtenáře SQL dostane řádek s kolekčním parametrem, kde Dapper píše holý tvar a MyBatis `<foreach>`. Doložení, kvůli kterému rozhodnutí vzniklo, nesou obě matice: Dapper jako šestý zdroj `CollectionParameter` v `categories.txt` a v `Differential/matrix.txt`, v .NET sadě i v javové.
