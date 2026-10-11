# 128 — Jednotka C# se syntaktickou chybou se odmítá s pozicí, ne překládá ze stromu, který Roslyn dopsal

Datum: 2026-10-11
Stav: platí
Požadavky: F11, F14, S4, S7
Podklad: rozhodnutí [093](093-unreadable-input-is-a-unit-failure.md), které případ nechalo stranou; dále [010](010-diagnostics-as-returned-data.md), [025](025-query-language-as-content-type.md), [045](045-a-conversion-that-produced-nothing-says-so.md), [066](066-records-attributed-to-the-input-unit.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [092](092-input-nesting-depth-capped-before-the-descent.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) a [126](126-line-numbers-in-the-unit-editor-by-own-code-not-an-editor-library.md); položka *Syntaktickou chybu v C# nehlásí nikdo s pozicí* v `open-items.md`; [`use-cases.md`](../use-cases.md), oddíl „Co je ve skutečnosti vstupem"

## Kontext

Rozhodnutí 093 vyslovilo jedno pravidlo pro pět jazyků: jednotka, jejíž text čtečka nepřečte, je `Failure` připsaný té jednotce a nesoucí řádek a sloupec, na kterém čtení skončilo; jednotka nevydá nic, ostatní jednotky převodu se přeloží. SQL, Java, HQL a JPQL tak mluvily už před ním, XML od něj. C# nechalo stranou s tím, že u něj nejde o volbu mezi výjimkou a záznamem — na straně záznamu stojí už dnes —, nýbrž o to, *jak přesně čtečka mluví*. Tohle rozhodnutí tu otázku zodpovídá.

**Jak C# čteme dnes.** Tři místa berou strom z `CSharpSyntaxTree.ParseText` a nikdo z nich se Roslynu neptá, zda text přečetl: `CSharpEntityParser.Parse` na entitním průchodu a sdílené `CSharpUnit.Parse` (`Common.Reading`), přes které čtou dotazový průchod LINQ i Dapperu. Roslyn přitom o chybě ví — strom nese diagnostiky s řádkem a sloupcem — a navíc **zotavuje**: z textu s chybou postaví strom, jaký by podle něj autor nejspíš chtěl, a chybějící nebo přebývající tokeny do něj dopíše nebo z něj vyhodí. Otevřená položka popisovala stav tak, že rozbitá třída skončí obecným záznamem orchestrace bez pozice. To je jen krajní případ. Nad Roslynem 5.6.0, verzí, kterou nástroj váže, vypadá skutečný stav takto (pozice nad textem, jak stojí):

| Vstup | Co z něj nástroj dnes čte | Co Roslyn ví |
|---|---|---|
| `this is not C#` | žádná třída → obecný `Failure` rozh. 045 („nothing came of it"), bez pozice | řádek 1, sloupec 14 |
| chybějící `;` za vlastností | obě vlastnosti, artefakt vznikne, **žádný záznam** | řádek 4, sloupec 35 |
| chybějící `}` na konci třídy | obě vlastnosti, artefakt vznikne, žádný záznam | řádek 4, sloupec 37 |
| `publc string Name { get; set; }` | `Name` jako vlastnost **bez modifikátoru**, `publc` zahozeno, žádný záznam | řádek 4, sloupec 11 |
| cizí řádek mezi členy | řádek zahozen, obě vlastnosti, žádný záznam | řádek 4, sloupec 5 |
| neukončený řetězec SQL ve volání Dapperu | volání ztraceno → obecný `Failure` rozh. 045, bez pozice | řádek 3, sloupec 52 |

Dnešní chování tedy není „záznam bez pozice", nýbrž **překlad stromu, který Roslyn vymyslel, beze slova**. Čtvrtý a pátý řádek jsou podstatné: zotavení nemění jen to, *zda* se jednotka přeloží, ale *co* z ní. Vlastnost bez modifikátoru, zahozený člen, zahozený atribut před ním — artefakt z takového stromu není chudší, je **jiný**, a nikdo o tom neřekl ani větu. Přesně proti tomu stojí rozhodnutí 070 u dotazů (co se nepřečte a změnilo by výsledek, se odmítá, místo aby se vydalo něco jiného) a F11, které žádá strukturovanou diagnostiku každého převodu. Testy, které dnes rozbité C# posílají (`Combined/UnitAttributionTest`, `Combined/DiagnosticsTest`, `Api/ConvertEndpointTest`), pokrývají jen první řádek tabulky.

**Co je vstupem.** `use-cases.md` říká, že skutečným vstupem je projekt a jednotka je jeden jeho soubor (rozh. 111). Soubor projektu, který se překládá, syntaktickou chybu nemá — jinak by nepřeložil ani svůj vlastní projekt. Jednotka se syntaktickou chybou je tedy buď text vložený do editoru s překlepem, nebo fragment, který uživatel slepil ručně; v obou případech chce vědět, **kde** je chyba, ne dostat artefakt, který ji obešel. Javová čtečka (`JavaClassReader`) se už dnes zastaví na prvním neočekávaném tokenu a nikdo neprosil o shovívavost.

**Ne každá chyba, kterou Roslyn ohlásí, je chybou textu.** Část chyb vzniká z toho, *jak* nástroj text parsuje, ne z toho, co v něm stojí. Fragment se čte uvnitř třídy obalu (rozh. 111), soubor tak, jak stojí, a o tom rozhoduje, zda text na vrcholu deklaruje typ (`CSharpUnit.IsFile`). Tohle dělení se netrefí do čtyř tvarů, které jsou platné C# a nástroj je dnes přeloží — jen díky tomu, že se nikdo na chyby neptá:

| Tvar | Proč ho Roslyn odmítne v tvaru, který vybere `IsFile` |
|---|---|
| metoda vedle třídy — tak píše artefakt únikové cesty EF Core metodu a třídu jejích řádků (rozh. 113) | `IsFile` vidí soubor a Roslyn odmítne modifikátor `public` u člena mimo typ; uvnitř třídy obalu se týž text přečte bez chyby |
| samotné příkazy (`return …;`, `using var …;`), které rozh. 111 drží platnou jednotkou | ve třídě obalu příkaz stát nemůže; jako příkazy nejvyšší úrovně se text přečte bez chyby |
| metoda vložená i s direktivami `using` nad sebou | direktiva `using` uvnitř třídy obalu stát nemůže; Roslyn hlásí „} expected" na řádku 1, sloupci 1 |
| syntaxe novější než výchozí verze jazyka Roslynu 5.6.0 (C# 14), třeba deklarace `union` | parser ji zná, ve výchozí verzi ji odmítne |

Odmítnout tyhle jednotky by nebyl nárok o textu, nýbrž o tvaru, ve kterém ho nástroj čte; pravidlo rozhodnutí musí chybu textu od chyby tvaru oddělit.

**Zotavení není naše pravidlo.** Jak Roslyn opraví text, určuje verze Roslynu, ne nástroj: tabulka výš platí pro 5.6.0 a jiná verze může týž text opravit jinak. Artefakt, jehož obsah závisí na tom, jak si knihovna domyslela chybějící token, je artefakt bez pravidla, které by šlo vyslovit v `subset.md` nebo obhájit v textu práce. Nástroj nárokuje, co čte; co Roslyn dopsal, nečetl. Tím se liší od obalu fragmentu: obal je doplnění, které nástroj vyslovuje a které je stejné pro každý vstup, kdežto zotavení je domněnka knihovny o jednom konkrétním textu.

**Pozice je po ruce.** `SyntaxTree.GetDiagnostics()` vrací diagnostiky syntaktické analýzy — bez sémantiky, bez kompilace, takže druhá věta S4 (překladová cesta cizí kód nekompiluje) platí dál — a každá nese `Location` s řádkem a sloupcem. Frontend od rozh. 126 čte z věty záznamu „at line *L*, column *C*" a označí řádek ve sloupci čísel editoru; pro C# nepotřebuje nic nového.

## Zvažované varianty

### 1 — Nechat, jak to je, a nést zúžení S7 v §9

Nejlevnější. Zúžení „řádek a sloupec chybí u C#" v §9, v `subset.md` a v `traceability.md` je vyslovené a vstup s překlepem nikdo neposílá schválně.

Zamítáme, a je to totéž zamítnutí, které padlo v rozh. 093 nad jeho variantou 1: schopnost je hotová a pětkrát použitá, jen se na šestý jazyk nevztahuje, a „pro všechny jazyky kromě C#" není hranice, nýbrž popis vady. K tomu přibývá to, co položka neviděla: zúžení v §9 mluví o chybějící pozici, ale mlčí o tom, že se jednotka s chybou **přeloží jinak**. Takové zúžení by muselo znít „u C# nástroj překládá i text, který nepřečetl, podle toho, jak ho Roslyn opraví" — a to není tvrzení, které by šlo nárokovat.

### 2 — Hlásit pozici a přeložit, co se zotavilo, pod vlastním druhem záznamu

Jednotka by dostala záznam s řádkem a sloupcem a artefakt by vznikl dál, takže vstupy, které dnes projdou, by prošly i nadále, jen se slovem navíc. Žádný ze stávajících druhů na to nesedí: `Failure` znamená, že artefakt nevznikne (§5.1, rozh. 010); `Loss`, že cíl nevyjádří fakt zdroje, takže je artefakt platný a jen chudší — a fakt se záznamem `Loss` kniha tvrzení počítá za zahozený (rozh. 124); `Incompleteness`, že modelu chybí fakt, který by šlo doplnit, kdežto tady fakt neschází, nýbrž byl nahrazen domněnkou knihovny. Varianta proto počítá s novým druhem — „čtečka text opravila, artefakt překládá opravený text" —, který by do výčtu událostí (rozh. 010) seděl.

Zamítáme, protože nový druh by seděl na událost, ale ne na artefakt, který s ní odejde. Záznam umí říct, **kde** Roslyn hádal, ne **co** jeho odhad v artefaktu změnil, a změna nebývá v místě chyby. Nad Roslynem 5.6.0:

| Vstup | Co z opraveného stromu vznikne |
|---|---|
| SQL ve volání Dapperu rozdělené do dvou řádků obyčejného řetězce | SQL zní `SELECT * FROM Sales.Customers` — klauzule `WHERE` z druhého řádku zmizí a zbytek řádku se stane třemi argumenty navíc; dotaz vrátí všechny řádky |
| `publc string Name { get; set; }` | vlastnost `Name` bez modifikátoru, tedy soukromá, kterou EF Core konvencí nemapuje |
| chybějící `}` za první třídou | následující třída `Order` je vnořená do `Customer`, takže má jinou totožnost (rozh. 094) |

První řádek je přesně dotaz, který podle rozh. 070 a věty sekce *Guarantees* kořenového README („a query that would return a different set of rows is not emitted") vyjít nesmí, a záznam vedle něj ho nenapraví — jen přenese na čtenáře povinnost domyslet si, co oprava změnila. Nárok by se musel u C# zúžit na „překládá, jak Roslyn text opraví", což je zamítnutí varianty 1 znovu, jen s jiným druhem záznamu.

### 3 — Odmítnout jen chybu, která se dotýká toho, co se čte

Chyba v těle metody, které entitní průchod nečte, by prošla; chyba v hlavičce třídy nebo člena by jednotku odmítla.

Zamítáme. Hranici nejde vyslovit jednou větou: dotazový průchod čte právě ta těla metod, která entitní průchod přeskakuje (rozh. 109, 111), takže by se tatáž chyba u téže jednotky jednou tolerovala a jednou ne, podle toho, který průchod se na ni dívá. A jak ukazuje třetí řádek tabulky u varianty 2, oprava zasáhne i to, co od místa chyby leží daleko. Které diagnostiky Roslynu jsou „neškodné", by navíc byl seznam kódů chyb jedné verze knihovny, ne pravidlo nástroje. Jedna věta pro celou jednotku — jak ji má Java, SQL, HQL, JPQL i XML — je jediná, kterou lze udržet.

### 4 — Odmítnout jednotku, kterou Roslyn nepřečte bez chyby v žádném tvaru, který nástroj vyslovuje, touž větou jako Java

## Rozhodnutí

**Volíme variantu 4. Jednotka C#, jejíž text Roslyn nepřečte bez chyby v žádném z tvarů, které nástroj vyslovuje, je `Failure` připsaný té jednotce a nesoucí řádek a sloupec první chyby v textu, jak ho poslal klient; jednotka nevydá nic, ostatní jednotky téhož převodu se přeloží a odpověď zůstává 200. Z textu se nepřekládá nic, co Roslyn dopsal.** Tím platí věta rozh. 093 pro všech šest jazyků, které nástroj čte, bez výjimky. Doplnit text smí jen nástroj, a to pravidlem, které vyslovuje; domněnka knihovny doplněním není.

**Co je chyba.** Diagnostika `SyntaxTree.GetDiagnostics()` se závažností `Error`. Jsou to diagnostiky syntaktické analýzy a nic víc — žádná `CSharpCompilation`, žádné reference, žádná sémantika; překladová cesta cizí kód dál nekompiluje (S4). Varování se neberou: zotavení, které stojí za rozhodnutím, Roslyn hlásí jako chybu, a varování o textu nic nevyvrací. Z několika chyb mluví **první podle pozice**, jako u Javy, HQL a JPQL — jedna jednotka, jedna věta; ostatní chyby jsou většinou následek první a klient je uvidí, až tu první opraví.

**Tvary, ve kterých se text čte.** Text se čte buď tak, jak stojí, nebo jako členy třídy obalu (`FragmentWrapper`, rozh. 111). Který tvar se zkusí první, rozhoduje jako dosud neobalený strom (`CSharpUnit.IsFile`): soubor se čte, jak stojí, fragment v obalu. Má-li ten strom chybu a druhý tvar ne, čte se text ve druhém — tak se přečte metoda vedle třídy jejích řádků i samotné příkazy. Odmítá se jen text, který se nepřečte ani v jednom tvaru, a pozici nese první chyba tvaru zkoušeného první. Druhý tvar se bere, jen když chybu nemá, takže se ani tak nečte nic, co Roslyn dopsal. K obalu patří dvě pravidla, která oddělují chybu tvaru od chyby textu:

- **Direktivy `using` (a `extern alias`), kterými fragment začíná, patří souboru**, takže stojí před obalem, ne v jeho třídě, kde direktiva stát nemůže. Metoda vložená i se svými direktivami se tak přečte.
- **Text se parsuje ve verzi jazyka *preview*.** Nástroj čte syntaxi a nic nekompiluje, takže syntaxe novější než výchozí verze Roslynu není důvodem odmítnout jednotku, kterou její projekt přeloží; verze jazyka rozhoduje o tom, co parser přijme, ne o tom, co text znamená.

Třídy, které text čtený v obalu deklaruje, jsou třídami jednotky, ne členy obalu: obal není jejich kontejnerem (rozh. [094](094-entity-identity-inside-a-conversion.md)) a jejich pozice se počítají v textu klienta (rozh. [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md)). Čtení v obalu ani v druhém tvaru se nehlásí žádným záznamem, stejně jako dosud obal fragmentu: je to vyslovené pravidlo čtení, ne událost převodu.

**Jak věta zní.** „The C# source could not be read at line *L*, column *C*: *zpráva Roslynu*." — týž tvar jako u Javy („The Java source could not be read at line…"), aby `ui.js` četl pozici bez nové výjimky. Zprávu Roslynu neseme doslova a v invariantní kultuře, jako nese `XmlSource` zprávu `XmlException`: je to text platformy, nikoli náš, a jinak by stejný vstup na serveru s českým prostředím dostal jinou větu (S2). I tam, kde zpráva zní zvláštně (nad `publc string Name` Roslyn hlásí neplatný token `string`, ne překlep před ním), stojí vedle ní naše pozice, tedy ta polovina, kterou umí přečíst stroj.

**Pozice ukazuje do textu klienta.** Diagnostiky se čtou nad stromem, ze kterého se čte, u fragmentu tedy nad obaleným; obal LINQ předřazuje fragmentu šest řádků, obal Dapperu dva, a direktivy fragmentu stojí před nimi. Pozice za hlavou obalu se o ni sníží zpět — totéž, co `XmlSource` dělá s ořezaným začátkem, jen opačným směrem. Chyba, kterou Roslyn umístí až do závěru obalu (chybějící `}` fragmentu hlásí tam, kde obal končí), se připíše konci posledního neprázdného řádku textu klienta; řádek, který klient neposlal, záznam nikdy nejmenuje.

**Věta bydlí na jednom místě.** Tři čtecí místa dostanou společnou čtečku vedle `CSharpUnit` v `Common.Reading` (`CSharpSource`) — strom, nebo důvod, ve tvaru `XmlReadResult` z rozh. 093 —, a `CSharpEntityParser` přestane volat `ParseText` sám. Vedle `NestingDepthGuard` a `XmlSource` v `AbstractWrappers` čtečka stát nemůže: `AbstractWrappers` Roslyn nereferencuje a referencovat nemá, kdežto `Common` ho má kvůli `Common.Compilation` už dnes a `CSharpEntityParsing` k němu dosáhne přes `AbstractWrappers`. Důvod, proč jedno místo, je týž jako v rozh. 093: tři věty napsané třikrát by se rozešly — a tady by se rozešly i tvary, takže by jeden průchod četl text, který druhý odmítl.

**Když jednotku čtou dva průchody, mluví ten první.** Jednotku C# čte entitní průchod a po něm dotazový (LINQ u EF Core a NHibernate, SQL ve volání u Dapperu), oba nad týmž textem. Rozbitost je jeden fakt o jednotce, ne dva, takže ji vysloví **entitní parser** a dotazový parser nad toutéž jednotkou vrátí prázdno a mlčí — přesně tak, jak oba průchody už dnes zacházejí se stropem hloubky (rozh. 092: „The query pass reads the same text after this one and keeps quiet about it: one fact, one record"). Prázdná jednotka není tvrzení (rozh. 025) a žádný záznam nedostane; o ni se stará orchestrace.

**Obecný záznam o neplodné jednotce zůstává vedle toho konkrétního.** Jako u Javy, SQL a XML: jeden záznam říká proč, druhý (rozh. 045), že z jednotky nic nevzešlo. Testy, které dnes očekávají u `this is not C#` jediný `Failure` o jednotce, se tím **mění**: záznamy o ní budou dva, a ten s pozicí je nový. Test, který tím textem zkouší C# bez entity, dostane C#, které se přečte.

**Co tohle rozhodnutí nepokrývá.** Text, který Roslyn přečte bez chyby a který jen nenese nic, co by nástroj četl (třída bez vlastností, metoda bez volání frameworku), dál vrací prázdno s obecnou větou rozh. 045, případně se záznamy rozh. 111 o třídě, která není entitou — stejně jako XML se správným tvarem a cizím kořenem. Nemění se ani strop hloubky (rozh. 092), který běží *před* Roslynem a dál má přednost: vstup nad stropem je odmítnut větou o hloubce, ne o syntaxi. A nemění se nic na tom, co se čte ze stromu, který chybu nemá.

## Důsledky

**Vstup, který se dnes přeloží potichu z opraveného stromu, bude napříště odmítnut s pozicí.** To je jediná cena rozhodnutí a nese ji jen text, který není C# v žádném tvaru, jaký nástroj vyslovuje — tedy text, který by nepřeložil ani vlastní kompilátor. Komu dnes prošel chybějící středník, dostane místo artefaktu větu, na kterém řádku chybí, a sloupec čísel řádků ji označí (rozh. 126). Cena není v tom, co nástroj přestane umět, ale v tom, co přestane předstírat. Tvary z tabulky v kontextu se naopak čtou bez opravy, takže artefakt únikové cesty EF Core se dál čte zpět, jak žádá rozh. 113.

**Pro číslo verze je to MINOR.** Podle rozh. [069](069-major-marks-a-milestone-not-a-break.md) je MINOR nová schopnost uvnitř cíle, a schopnost tu přibývá: chyba na úrovni souboru a řádku (S7) platí i pro C#. Že některý vstup napříště projít přestane, žádné nárokované tvrzení nebere — §9 nikdy netvrdil, že se text se syntaktickou chybou překládá, tvrdil jen, že u něj chybí pozice. Číslo se stanoví při vydání (rozh. [098](098-the-number-is-decided-once-per-release.md)); tímhle rozhodnutím se nehýbe.

**Testy.** Vedle zdravé jednotky téhož běhu jednotka C# s chybou, pro každý zdrojový framework, který C# čte (Dapper, EF Core, NHibernate): právě jeden záznam o nečitelném textu s řádkem a sloupcem, připsaný té rozbité jednotce (rozh. 066), a artefakty té zdravé. K tomu to, co je na rozhodnutí choulostivé: že chybějící středník **už neprojde** (dnešní tiché zotavení, důvod celého rozhodnutí); že entitní a dotazový průchod nevydají o téže chybě dva stejné záznamy; že pozice u fragmentu jmenuje řádek textu klienta, ne řádek obalu, i za direktivami, které stojí před obalem, a že chyba na konci fragmentu nejmenuje řádek, který klient neposlal; že strop hloubky mluví dřív než syntaxe; a že se každý tvar z tabulky v kontextu přečte — třída vedle metody jako třída jednotky se svou totožností a pozicemi. Kontraktní test podle rozh. [043](043-rest-contract-guarded-over-http.md) nový není třeba: stav odpovědi se nemění, jen přibývá záznam téhož tvaru.

**Fixtury, které dnes stojí na zotavení, jsou vada fixtury.** Běh obou sad po implementaci řekne, zda některý vstup testů nebo vzorků (`SampleData`) procházel jen díky tomu, že mu Roslyn něco dopsal — typicky holý výraz bez středníku. Takový vstup se opraví na text, který se přečte; rozhodnutí se kvůli němu nerozvolňuje. Vstup, který odmítne jen tvar čtení, vadou fixtury není: patří do tabulky v kontextu a pravidlo tvarů se o něj rozšíří.

**Dokumentace po implementaci.** `architecture.md` §5 (jak se jednotka C# čte) a §5.1: v tabulce druhů záznamů platí „nečitelný text jednotky" pro všech šest jazyků a za větou o `XmlSource` přibude věta o čtečce C# a o tom, že mluví entitní průchod; §9 S7 ztratí zúžení „C# pozici nedostane" a věta „chyba na úrovni souboru a řádku platí pro všechny jazyky kromě C#" se škrtne; totéž v sekci *Guarantees* kořenového README. `subset.md`, řádek *nečitelný text jednotky*: C# přejde mezi jazyky s řádkem a sloupcem. `traceability.md` S7: poznámka „řádek a sloupec chybí u C#" se škrtne a mezi doklady přibude nový test. Položka v `open-items.md` je prací podle tohoto rozhodnutí a po implementaci zmizí.

**Frontend nepotřebuje zásah.** Záznam přijde v témž tvaru, `positionIn` v `ui.js` z něj přečte řádek a sloupec a `markLine` označí řádek editoru jednotky (rozh. 126). Klientská validace C# nevzniká: zůstává pomocníkem jen u XML a server zůstává autoritou (rozh. 033, 093).

**Model hrozeb se nemění.** Zpráva Roslynu je text o vstupu klienta, ne o serveru; nový kanál, kterým by do odpovědi proniklo jméno stroje nebo instance (hrozba 4), nevzniká.
