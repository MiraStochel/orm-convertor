# 107 — Výraz je šestý tvar operandu a stojí všude, kde stojí operand

Datum: 2026-09-30
Stav: platí
Požadavky: F7–F10, F11, T1, T2, T3, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [028](028-assembly-name-is-not-ours-to-invent.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [051](051-like-pattern-translated-not-carried-over.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [061](061-subquery-as-a-condition-operand.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [073](073-distinct-as-a-flag-of-the-query-scope.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [083](083-parameter-as-the-fifth-operand-shape.md), [085](085-a-row-count-is-a-number-or-a-parameter.md), [086](086-target-database-dialect-declared-by-the-descriptor.md), [102](102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md), [103](103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md), [104](104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md) a [105](105-a-query-formulates-its-own-demand-on-the-catalog.md); JSS §5.3 a §5.4, pravidla Q3, Q5, Q14 a Q15; schválený záměr projektu ([`zamer.tex`](../zamer.tex), „explicitně vymezená společná podmnožina konstrukcí"); otevřené položky „Řetězcová metoda LINQ s parametrem jako argumentem se nečte, protože vzorek by byl výraz" a „Výrazy nad sloupci nemají v mezireprezentaci místo"

## Kontext

Dvě položky `open-items.md` popisují touž mez ze dvou stran.

**Ta menší** vyšla najevo 2026-09-30, když sdílený LINQ parser začal číst řetězcové metody jako `LIKE` (tabulka rozhodnutí 051 obráceně). `c.Name.StartsWith(prefix)` s hodnotou z okolního scope je první parametrizovaný filtr nad řetězcem, který v EF Core i NHibernate někdo napíše, a provider ho překládá na `LIKE @prefix + '%'`. Pravá strana `Like` je v mezireprezentaci operand a operand má pět tvarů — sloupec, konstanta, poddotaz, výčet hodnot, parametr —, z nichž žádný není *parametr spojený se zástupným znakem*. Přečíst tvar jako holý `LIKE @prefix` by vybralo jiné řádky (volající zadá `W` a dotaz najde jen `W`, ne `Widget`), takže parser tvar odmítá záznamem `Failure` kategorie `QueryParameter`; s literálem se táž metoda čte.

**Ta větší** stojí od 2026-09-29 ve Zbytcích jako nejširší vyslovená mez dotazové podmnožiny: **výrazy nad sloupci nemají v mezireprezentaci místo.** Funkce mimo agregace (`UPPER`, `CONCAT`, datumové), aritmetika (`Price * Quantity`), `CASE` a `COALESCE` nemají kam. Co s nimi nástroj dělá dnes, je čitelné z kódu, a v každé pozici jinak:

| Pozice | Co čtečky dělají | Záznam |
|---|---|---|
| operand podmínky (T-SQL, LINQ, HQL, JPQL) | `ReadOperand` vrátí `null` pro cokoli, co není sloupec, literál, poddotaz, agregát nad sloupcem nebo parametr; podmínka se nepřečte | `Failure` podle rozhodnutí 070 („uses a construct the condition tree cannot carry"), artefakt nevzniká |
| projekce (T-SQL) | „The projected expression '…' is not a column or an aggregate and was dropped." | `Loss`, projekce spadne na celou entitu |
| projekce (LINQ) | člen anonymního typu, který není sloupec, se nepřečte | `Loss`, celý řádek |
| klíč seskupení (T-SQL) | „A grouping key that is not a column reference cannot be carried" | `Failure` |
| klíč řazení (T-SQL) | „An ordering key that is not a column reference was dropped." | `Loss` — a platí to i pro `ORDER BY COUNT(*) DESC`, protože `OrderByInstruction` nese jen tabulku a atribut a agregát se do něj nevejde |

Menší položka je nejmenší instance větší: `concat(:prefix, '%')` je výraz jako každý jiný. Rozhodovat o kotvách na parametru zvlášť by znamenalo vyřešit jeden tvar konstrukcí, kterou žádný ze šesti jazyků nemá, a příští tvar — `'%' + @p + '%'`, `@a + @b`, `UPPER(Name) = :n` — by potřeboval obecný operand stejně.

**Kde dnes leží tvar „věc, kterou dotaz spočítá".** Rozhodnutí 024 dalo oběma stranám porovnání jeden uzavřený typ `QueryOperand` s továrnami, aby neplatná kombinace nebyla zapsatelná, a rozhodnutí 061, 074 a 083 ho rozšířila na pět tvarů. Projekce ale zůstala mimo: `ProjectInstruction` je volná pětice (`Table`, `Attribute`, `Alias`, `Function`, `Distinct`) — přesně ten tvar, který 024 z porovnání odstranilo —, `OrderByInstruction` a `GroupByInstruction` jsou dvojice tabulka a atribut. I kdyby operand výraz unesl, projekce a řazení by neměly kam ho dát. Otázka po výrazu je proto zároveň otázkou, kde všude má operand stát.

**Co říká článek.** §5.3 vyjmenovává operandy podmínky jako „constants, attributes, or nested subqueries", pravidla Q1–Q15 výraz nezmiňují a tabulka 2 pro něj řádek nemá. Je to rozšíření nad článek téhož druhu jako poddotaz (061), výčet (074), parametr (083), stránkování (060) a `DISTINCT` (073), opřené o §5.4, který rozšiřitelnost deklaruje — čtvrté rozšíření operandu a zdaleka největší, jak Zbytky zapsaly.

**Proč je to rozhodnutí, ne psaní kódu.** Čtyři věci.

*Typ výrazu není ve zdroji.* Parametr uvnitř výrazu (`Price * @factor`, `concat(:prefix, '%')`) potřebuje skalár do signatury generované metody (rozhodnutí 083) a ten plyne z pozice: v konkatenaci je to `String`, v aritmetice skalár druhé strany, jako argument funkce skalár, který funkce bere. Skalár sloupce zná jen builder přes mapovací mezireprezentaci (083, 105). Kam odvození patří, je tím dané.

*Slovník funkcí se mezi cíli rozchází, a poprvé uvnitř jedné kategorie.* Agregační funkce se nese jako řetězec a funguje to jen proto, že pět agregátů se ve všech šesti cílích jmenuje stejně. Skalární funkce ne: T-SQL `LEN`, HQL a JPQL `length`, LINQ `.Length`; T-SQL `ISNULL` i `COALESCE`, JPQL jen `coalesce`, LINQ `??`; rok z data je `YEAR(d)`, `year(d)`, `extract(year from d)` a `d.Year`. Deskriptor dnes deklaruje podporu po kategoriích `QueryFeature` a všech šest cílů má u všech dvanácti *vyjádřitelné*, takže mechanická kontrola Q14 nikdy nespustí; u funkcí by to přestalo platit hned s první, kterou některý cíl nemá.

*T-SQL i C# přetěžují `+`.* `a + b` je sčítání nebo konkatenace podle typů operandů a čtečka typy nezná. Kdo o tom rozhoduje, je otázka modelu: buď nese dva operátory a parser hádá, nebo nese jeden a rozhodne brána, která typy má.

*Výraz v projekci bez aliasu nemá jméno.* `SELECT Price * Quantity FROM …` je legální T-SQL a vrací sloupec bez jména; anonymní typ LINQ člen bez jména mít nemůže a jméno vymyšlené nástrojem zakazuje rozhodnutí 028.

**Proč teď.** Ručně psané SQL (F8) i případová studie T1 jsou výrazů plné, takže mez je vidět v číslech T3 — a je to mez zvolená kvůli ceně, ne kvůli sémantice, protože všech šest cílů každou z těch konstrukcí zapíše. Katalog podmnožiny, poslední práce kategorie Dotazy, má zapisovat rozhodnuté meze, ne zděděné; a řádek přidaný do matice po rozhodnutí se měří jednou, kdežto katalog přepsaný po pozdějším rozhodnutí dvakrát.

## Zvažované varianty

### 1 — Ponechat mez a zapsat ji do katalogu

Dnešní stav, zvolený implicitně tím, že na výrazy nedošlo, a od 2026-09-29 vyslovený ve Zbytcích. Zamítáme. Cena je v T3 a v F8 a není to cena za sémantiku: žádná z konstrukcí nemá v žádném cíli nejednoznačný překlad, každý cíl ji zapíše, jen jiným slovem. Mez zvolená proto, že překlad je pracný, je přesně to, co záměr chce mít zdokumentované jako *případ bez jednoznačného překladu* — a tenhle případ to není. A položka o řetězcové metodě by skončila „ne" u prvního parametrizovaného řetězcového filtru, který kdo napíše.

### 2 — Kotvy na parametru vzorku

Příznak „začátek / konec / uvnitř" u parametru na pravé straně `Like`, který by každý cíl vypsal svým slovem. Zamítáme. Řeší jeden tvar konstrukcí, která v žádném ze šesti jazyků neexistuje — každý píše konkatenaci —, tedy vkládá do modelu fakt, který zdroj nevyslovil. Druhý tvar téže meze (`'%' + @p + '%'` s literálem uprostřed, `@a + @b`, `UPPER(Name) = :n`) potřebuje obecný operand tak jako tak, a pak by model nesl konkatenaci dvakrát: jednou jako příznak, jednou jako výraz.

### 3 — Výraz jako text konstanty

`QueryConstant.Unrecognized("UPPER(Name)")` a visitory ho vypíšou doslova. Zamítáme třemi důvody rozhodnutí 083 u téže varianty pro parametr, a každý sám stačí: zdobení zdroje by doteklo do všech cílů (`UPPER(Name)` do LINQ, `.ToUpper()` do JPQL); model by nemohl říct, co je hodnota, takže brána parametrů by parametr uvnitř textu neviděla a metoda by ho neměla v signatuře; a cíl by nemohl odmítnout funkci, kterou nemá, protože by nevěděl, že tam je — mlčení, které rozhodnutí 004 zakazuje.

### 4 — Otevřený slovník: funkce jako řetězec

Tvar `Call("UPPER", args)` po vzoru dnešního `Function` u agregátů. Zamítáme. Agregát jako řetězec drží jen díky tomu, že pět jmen je ve všech cílích totéž slovo; skalární funkce se rozcházejí (viz kontext) a překladová tabulka by musela být v každém visitoru s větví `_ =>` pro neznámé jméno — přesně ten tvar tiché náhrady, který rozhodnutí 053 odstranilo z tabulek operátorů. Uzavřený výčet dovolí deskriptoru říct, co cíl umí, a bráně odmítnout jednou pro všechny.

### 5 — Uzavřený slovník výrazů jako šestý tvar operandu; projekce a řazení nad operandem

## Rozhodnutí

**Volíme variantu 5. `QueryOperand` dostává šestý tovární tvar `Expression`, jehož obsahem je nový uzavřený typ `QueryExpression` se třemi tvary — binární operace, volání funkce z uzavřeného slovníku a `CASE` —, jejichž listy jsou opět operandy. Projekce a klíč řazení přestávají být volné n-tice a stojí nad `QueryOperand` s aliasem, resp. se směrem, takže výraz stojí všude, kde stojí operand: na obou stranách porovnání včetně vzorku `LIKE`, v projekci, v klíči řazení, v `ON` a v `HAVING`, a jako argument agregátu. Čtyři čtečky ho čtou ze svého tvaru, sdílená brána v `Normalize()` mu doplní skalár z mapovací mezireprezentace a odmítne funkci, kterou deskriptor cíle neuvádí, a čtyři visitory ho vypíšou slovem svého cíle. `IQueryVisitor` se nemění, orchestrace se nemění.**

### Tvar

**Operand zůstává uzavřený typ s továrnami** (024, 061, 074, 083): sloupec, konstanta, poddotaz, výčet hodnot, parametr, nebo výraz. Šestý tvar přidává jedno pole, `Expression`, po vzoru `Constant` a `Parameter`. Továrna `QueryOperand.Expression(QueryExpression expression, string? function = null, bool distinct = false)` bere agregační funkci a její příznak z týchž důvodů jako `Column` a `Bound`: agregát stojí nad čímkoli, co má hodnotu, takže `SUM(Price * Quantity)` a `COUNT(DISTINCT UPPER(Name))` mají místo, a `Function` s `Distinct` zůstávají tam, kam je dalo rozhodnutí 102.

**`QueryExpression` je uzavřený typ se třemi továrnami:**

- **`Binary(ExpressionOperator op, QueryOperand left, QueryOperand right)`** — operátory `Concat`, `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`. Konkatenace je vlastní operátor, ne `Add` nad řetězci, protože HQL, JPQL i LINQ ji vyslovují vlastním slovem (`||`, `concat`, `string.Concat`) a model nese, co zdroj řekl; co s přetíženým `+`, říká *Čtení* a *Brána* níž. Binární, ne n-ární: `concat(a, b, c)` se čte jako vnoření a visitor, jehož jazyk má n-ární tvar, ho smí zase zploštit — je to pravopis cíle (022), ne model.
- **`Call(QueryFunction function, IReadOnlyList<QueryOperand> arguments)`** — funkce z uzavřeného výčtu `QueryFunction` (slovník níž). Arita je vlastnost funkce a továrna ji hlídá, jako `ValueList` hlídá neprázdnost.
- **`Case(IReadOnlyList<(ConditionNode When, QueryOperand Then)> branches, QueryOperand? Else)`** — jen *searched* `CASE`; podmínky větví jsou týž `ConditionNode` jako filtr, takže je vypisuje týž kód a brána parametrů do nich sestupuje. *Simple* `CASE x WHEN 1 THEN …` se čte jako searched s rovností — přesný přepis, tedy čtení stejného druhu, jakým rozhodnutí 103 čte dotazový výraz LINQ jako jeho řetěz. `Else` smí chybět: SQL i HQL vracejí `NULL` a model nevaliduje.

Listy jsou `QueryOperand`, takže vnoření vzniká rekurzí a nový tvar nepřidává nic, co by visitor nevypsal už dnes. Poddotaz jako list výrazu (`(SELECT MAX(…)) - 1`) je zapsatelný a čtečky ho vyrábějí, kde ho zdroj napíše; výčet hodnot a kolekční parametr jako list výrazu zapsatelné nejsou (továrna), protože žádný cíl je v té pozici nezapíše — táž ochrana, jakou 074 dalo prvkům výčtu.

**Model nenese skalár výrazu.** Konstanta svůj skalár nese, protože ho parser přečetl z literálu; skalár výrazu z ničeho takového neplyne a musel by se odvodit z mapovací mezireprezentace, kterou parser nemá (083). Nese se tedy tam, kde se odvozuje, u builderu, a tvar je týž jako u parametru: typovaný pohled na straně builderu, strom od parseru se nepřepisuje.

### Kde výraz stojí

**Projekce se stává operandem s aliasem.** `ProjectInstruction(QueryOperand Operand, string? Alias)` nahrazuje pětici (rozhodnutí 003: jednorázový přepis, žádné přechodné období). Sloupec s agregační funkcí a příznakem je `Column(table, attribute, function, distinct)`, celá entita je sloupcový operand s vlastností `*`, jak ho pozice operandu nese už dnes, a výraz je `Expression(…)`. Rozhodnutí 102 platí beze změny: příznak sedí u funkce, jen funkce sedí na operandu. Rozhodnutí 104 platí beze změny: projekce do SQL cíle se materializuje jako netypovaný řádek, ať je sloupec nebo výraz. `AbstractQueryBuilder.Project` bere operand a alias, aby parsery instrukci dál nestavěly samy.

**Klíč řazení se stává operandem se směrem.** `OrderByInstruction(QueryOperand Operand, bool Asc)`. Řazení podle aliasu projekce zůstává, jak ho drží `Projects()` z rozhodnutí 073 — sloupcový operand bez tabulky, jehož vlastnost je alias —, a nově se nese řazení podle výrazu (`ORDER BY LEN(Name)`, `OrderBy(p => p.Name.Length)`) a **podle agregátu bez aliasu** (`ORDER BY COUNT(*) DESC`), které dnes padá jako ztráta, ač je to nejběžnější řazení seskupeného dotazu.

**Podmínky výraz dostávají zadarmo.** Filtr, `HAVING` a `ON` joinu jsou stromy nad operandy, takže `UPPER(Name) = 'W'`, `Price * Quantity > 100`, `COALESCE(Nick, Name) = :n` a `ON a.Id = b.ParentId + 1` stojí bez další instrukce. Vzorek `LIKE` je operand jako každý jiný: `Like(col, Concat(param, '%'))` je tvar položky, která tohle rozhodnutí otevřela, a `Escape` na porovnání (102) platí i nad ním.

**Kde výraz nestojí, a proč.** Tři pozice zůstávají a katalog podmnožiny je zapíše jako vyslovené meze:

- *Klíč seskupení.* `GroupByInstruction` zůstává sloupec. Seskupení podle výrazu (`GROUP BY YEAR(Date)`) nutí týž výraz do projekce a do `HAVING` a v LINQ cíli do tvaru klíče `g.Key`, ze kterého se pak čte zpět; je to jediná pozice, kde by výraz změnil, *které řádky* dotaz vrací, a nese s sebou vlastní pravidla shody mezi třemi místy. Čtečky ho odmítají dál jmenovitě podle 070, nově pod kategorií `Expression`. Dostane vlastní rozhodnutí, ukáže-li případová studie T1, že chybí.
- *Prvek výčtu hodnot.* `IN (1, Price * 2)` nezapíše žádný cíl idiomaticky a 074 s 102 výčet vymezila na konstanty a skalární parametry; továrna to drží.
- *Počet řádků stránkování.* `RowCount` zůstává číslo nebo parametr (085); `TOP (@n + 1)` je tvar, který 085 vědomě vyloučilo.

### Typ výrazu a parametr uvnitř něj

Brána v `Normalize()` odvozuje skalár každého výrazu zdola nahoru, z listů: skalár sloupce z mapovací mezireprezentace přes tutéž vazbu tabulky, kterou si dotaz poptá u katalogu (105), skalár konstanty, jak ji parser přečetl, skalár parametru, jak ho uvedl zdroj nebo jak ho brána už odvodila.

| Tvar | Skalár výrazu | Skalár parametru v pozici |
|---|---|---|
| `Concat(a, b)` | `String` | `String` |
| `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo` | skalár typované strany; dvě typované strany z jedné číselné rodiny podle pravidla 074 (celočíselné s `Decimal` → `Decimal`, celočíselné s `Float`/`Double` → `Double`); `Decimal` s plovoucí čárkou se odmítá jmenovitě | skalár druhé strany |
| `Upper`, `Lower`, `Trim`, `Substring` | `String` | argument řetězce `String`, pozice a délka `Int` |
| `Length`, `Year`, `Month`, `Day` | `Int` | `String`, resp. `DateTime` |
| `Abs` | skalár argumentu | ze druhé strany porovnání, ve kterém volání stojí |
| `Coalesce(a, b, …)` | společný skalár argumentů podle pravidla 074 pro výčet | skalár typovaných sousedů |
| `CurrentTimestamp` | `DateTime` | — |
| `Case` | společný skalár větví a `Else` podle téhož pravidla | skalár ostatních větví |

Je to tabulka rozhodnutí 083 rozšířená o pozice, ne třetí cesta odvození: parametr dostane skalár z místa, kde stojí, a kde ho nedostane — `@a + @b` bez typované strany, `Coalesce(:x, :y)` —, odmítá artefakt táž věta jako u každého jiného parametru. **`Add` nad řetězcovým operandem je pro zápis konkatenace:** T-SQL i C# ji píší `+`, takže čtečky obou jazyků nemohou bez typů rozhodnout, a rozhodne to brána, která typy má — visitor dostane od builderu typovaný pohled, jak dostává vyřešené parametry, a HQL i JPQL cíl napíšou `concat`. Model tak nese, co zdroj řekl (`+`), a význam doplní jediné místo, které ho zná.

### Slovník a jeho zápis

Do výčtu `QueryFunction` vstupuje funkce jen s vyplněným řádkem pro všechny čtyři visitory, ověřeným proti zafixovaným verzím; co řádek nemá, do slovníku nevstupuje a čtečka to odmítá jmenovitě. Výchozí slovník:

| Slovník | T-SQL (Dapper 2.1.79, MyBatis 3.5.19) | HQL (NHibernate 5.7.0) | JPQL (Hibernate 7.4.5, EclipseLink 5.0.0) | LINQ (EF Core 10.0.10) |
|---|---|---|---|---|
| `Concat` | `a + b` | `concat(a, b)` | `concat(a, b)` | `a + b` |
| `Add` … `Divide` | `a + b`, `a - b`, `a * b`, `a / b` | totéž | totéž | totéž |
| `Modulo` | `a % b` | `mod(a, b)` | `mod(a, b)` | `a % b` |
| `Upper`, `Lower` | `UPPER(a)`, `LOWER(a)` | `upper(a)`, `lower(a)` | `upper(a)`, `lower(a)` | `a.ToUpper()`, `a.ToLower()` |
| `Length` | `LEN(a)` | `length(a)` | `length(a)` | `a.Length` |
| `Trim` | `TRIM(a)` | `trim(a)` | `trim(a)` | `a.Trim()` |
| `Substring` | `SUBSTRING(a, s, n)` | `substring(a, s, n)` | `substring(a, s, n)` | `a.Substring(s - 1, n)` |
| `Coalesce` | `COALESCE(a, b, …)` | `coalesce(a, b, …)` | `coalesce(a, b, …)` | `a ?? b ?? …` |
| `Abs` | `ABS(a)` | `abs(a)` | `abs(a)` | `Math.Abs(a)` |
| `Year`, `Month`, `Day` | `YEAR(a)`, `MONTH(a)`, `DAY(a)` | `year(a)`, `month(a)`, `day(a)` | `extract(year from a)`, … | `a.Year`, `a.Month`, `a.Day` |
| `CurrentTimestamp` | `CURRENT_TIMESTAMP` | `current_timestamp()` | `current_timestamp` | `DateTime.Now` |
| `EscapePattern` | `REPLACE(REPLACE(REPLACE(REPLACE(a, '!', '!!'), '%', '!%'), '_', '!_'), '[', '![')` | `replace(replace(replace(replace(a, '!', '!!'), '%', '!%'), '_', '!_'), '[', '![')` | totéž | `a.StartsWith(…)` v kotvě vzorku, jinak `a.Replace("!", "!!").Replace("%", "!%")…` |
| `Case` | `CASE WHEN c THEN a ELSE b END` | `case when c then a else b end` | totéž | `c ? a : b` |

Poznámky, které z tabulky nejsou vidět:

- **`Substring` se překládá, nepřenáší** (týž princip jako 051 u vzorku): model nese pozici od jedné, jak ji píší tři ze čtyř jazyků, LINQ čtečka k pozici jedničku přičte a LINQ visitor ji odečte. S konstantou je to číslo, s parametrem vypíše LINQ cíl `p - 1` — pravopis cíle, ne nový uzel modelu.
- **`Length` platí jen nad SQL Serverem**, ke kterému všech šest deskriptorů dialektem ukazuje (086): T-SQL `LEN` nepočítá koncové mezery a provider EF Core i dialekt Hibernate `length` na `LEN` mapují, takže se všech šest shoduje. Druhý dialekt by řádek musel ověřit znovu — je to táž výhrada, kterou 086 zapsalo pro literální typy sloupců.
- **`CurrentTimestamp` čte T-SQL čtečka z `CURRENT_TIMESTAMP` i `GETDATE()`**, které jsou týž datový typ; `SYSDATETIME()` je jiná přesnost a zůstává odmítnuté jmenovitě. LINQ čte `DateTime.Now`; `DateTime.UtcNow` je jiný okamžik a zůstává odmítnutý.
- **`CASE` bez `ELSE` v LINQ cíli** vypíše `: (T?)null` se skalárem větví, který brána odvodila; v JPQL, jehož gramatika `ELSE` vyžaduje, vypíše `else null`. *Ověřit při implementaci proti Hibernate 7.4.5 a EclipseLink 5.0.0, že literál `null` ve větvi `else` oba přijmou; nepřijme-li ho některý, odmítá JPQL visitor `CASE` bez `ELSE` jmenovitě v místě emise, jak 053 odmítá operátor, který cíl nezapíše.*
- **`Year`, `Month`, `Day` v JPQL** se píší standardním `extract`, ne hibernateovským `year()`, aby je četl i EclipseLink. *Ověřit při implementaci proti specifikaci Jakarta Persistence 3.2 a oběma implementacím; totéž pro `mod` v HQL 5.7, které dialekt MsSql registruje jako `%`.*
- **`Concat` má sémantiku SQL a JPQL:** operand `NULL` dává `NULL`. C# `+` nad řetězci bere `null` jako prázdný řetězec a provider EF Core to za něj do SQL dopisuje. Model rozdíl nenese (048), takže LINQ čtečka u konkatenace, jejíž operand je sloupec, vydá `Loss`, který to jmenuje; literál ani vzorek `LIKE` z řetězcové metody `null` nedají a záznam nedostanou.
- **`EscapePattern` je jediná funkce slovníku, kterou žádný jazyk nemá jako slovo, a je tu proto, že provider EF Core escapuje argument řetězcové metody za běhu:** `StartsWith(p)` s hodnotou `50%` hledá doslovné `50%`, kdežto `LIKE @p + '%'` by bral `%` jako zástupný znak. Sdílený parser ten fakt u literálu už nese (příznak `ProviderEscapesStringMethodArguments` a únikový znak `!` podle 102); u hodnoty od volajícího ho nese tahle funkce. Významem je „každý zástupný znak dialektu v hodnotě je doslovný", zápisem řetěz `replace` nad zástupnými znaky SQL Serveru (`%`, `_`, `[`) a nad únikovým znakem samým, s `ESCAPE '!'` na porovnání — visitor ho při zápisu vzorku zná, protože únikový znak sedí na porovnání. Do LINQ cíle se `Like(col, Concat(EscapePattern(x), '%'))` vypisuje jako `col.StartsWith(x)`: rozklad 051 nad parametrem místo literálu, přesný, protože provider escapuje; tvar mimo kotvy jde jako `EF.Functions.Like` nad řetězem `Replace`. Výstup SQL cílů je tím ošklivější, než co by kdo napsal rukou, a je to poctivá cena za sémantiku zdroje: hezčí `LIKE @p + '%'` by pro hodnotu se zástupným znakem vrátil jiné řádky, což 053 a 065 zakazují. *Ověřit při implementaci, že `replace` čte Hibernate 7.4.5 i EclipseLink 5.0.0 (Jakarta Persistence 3.2 ho zavádí) a že NHibernate 5.7.0 ho nad dialektem MsSql registruje; a proti EF Core 10.0.10, které znaky provider escapuje.*

### Čtení

**T-SQL** (sdílený projekt, tedy Dapper i MyBatis): `BinaryExpression` s `+ - * / %`, `ParenthesisExpression` odzávorkované, `FunctionCall` se jménem ze slovníku (`ISNULL(a, b)` je `Coalesce`, `CONCAT(a, b, c)` vnořený `Concat`), `SearchedCaseExpression` a `SimpleCaseExpression` jako searched, `CoalesceExpression`. **`+` se čte jako `Concat`, je-li některá strana řetězcový literál nebo výraz, jehož skalár je z tabulky výš `String`; jinak jako `Add`** a rozhodne brána. `CAST`, `CONVERT`, okenní funkce, `DATEADD`, `DATEDIFF`, `REPLACE`, `LEFT`, `RIGHT`, `ROUND` a všechno ostatní mimo slovník zůstává odmítnuté, resp. v projekci vypuštěné, jak dnes — jmenovitě, nově pod kategorií `Expression`.

**LINQ** (EF Core a NHibernate): `BinaryExpressionSyntax` s `+ - * / %`, `??` jako `Coalesce`, `?:` jako `Case` (vnořené ternáry jako další větve), `string.Concat(…)`, `.ToUpper()`, `.ToLower()`, `.Trim()`, `.Substring(s[, n])`, `.Length`, `.Year`, `.Month`, `.Day`, `Math.Abs(…)`, `DateTime.Now`. **Řetězcová metoda s argumentem, který není literál, se čte jako `LIKE` nad konkatenací:** `StartsWith(x)` je `Like(col, Concat(x', '%'))`, `EndsWith(x)` `Like(col, Concat('%', x'))`, `Contains(x)` nad sloupcem `Like(col, Concat(Concat('%', x'), '%'))`, kde `x` je parametr nebo výraz a `x'` je `EscapePattern(x)` s únikovým znakem `!` na porovnání tam, kde provider argument escapuje (EF Core), a holé `x` tam, kde ne (NHibernate) — týž příznak, kterým se dnes čte literál. `EF.Functions.Like(col, prefix + "%")` je `Like(col, Concat(prefix, '%'))` bez escapování, protože tam vzorek skládá volající sám. Obojí je přesné a žádný záznam nevzniká; zpět do EF Core se první tvar vypíše jako `StartsWith(prefix)` a druhý jako `EF.Functions.Like(col, prefix + "%")`, takže oba přežijí identitní směr. Rozhodnutí 053 a 065 platí doslova: hodnota se zástupným znakem vrací v každém cíli tytéž řádky jako ve zdroji.

**HQL** (NHibernate 5.7.0): aritmetika, `||` i `concat()`, funkce slovníku malými písmeny, `case when`. **JPQL** (Hibernate 7.4.5, EclipseLink 5.0.0): totéž, `||` z Jakarta Persistence 3.2 i `concat()`, `extract(year from …)`; hibernateovské `year()` se v JPQL jednotce nečte, protože ho EclipseLink nezná. **MyBatis** dědí sdílené čtení T-SQL.

### Brána

`Normalize()` drží čtyři pravidla, jednou pro všechny cíle (023):

1. **Typuje každý výraz** podle tabulky výš a odmítá jmenovitě, co netypuje tam, kde na typu závisí zápis nebo parametr: `+` bez typované strany, `Coalesce` nad nesjednotitelnými skaláry, `Decimal` s plovoucí čárkou v aritmetice. Výraz bez parametru a bez `+`, jehož typ neplyne — třeba `Abs(x)` nad sloupcem mimo mapování —, se vypíše bez typu, jako se vypisuje konstanta bez rozpoznaného skaláru (024).
2. **Odmítá funkci, kterou deskriptor cíle neuvádí** — kontrola Q14 na jemnějším zrnu než kategorie —, záznamem `Failure` kategorie `Expression`, který funkci a cíl jmenuje.
3. **Odmítá výraz v projekci bez aliasu.** Sloupec výsledku bez jména nepřečte po jménu žádný z cílů, které čtou po jménu — Dapper mapuje po jménu, takže nepojmenovaná projekce ve zdroji Dapper nemapuje nic —, a jméno vymyšlené nástrojem zakazuje 028. Klíč řazení alias nepotřebuje.
4. **Odmítá agregát nad agregátem** (`SUM(COUNT(*))`), který nezapíše žádný cíl, a — jako dosud — modifikátor `Distinct` nad `*`.

Brána parametrů (083) sestupuje do výrazů, do větví `CASE` i do jejich podmínek, takže parametr uvnitř výrazu přibude do signatury metody v pořadí prvního výskytu.

### Deskriptor

`QueryFeature` dostává třináctou hodnotu `Expression` — kategorii záznamů o výrazech; všech šest deskriptorů ji uvádí jako vyjádřitelnou, jako `QueryParameter`, protože každý cíl výraz zapíše a co zaznamenává, je mez modelu nebo slovníku, ne neschopnost cíle. Vedle ní dostává `TargetFrameworkDescriptor` množinu **`Functions`**: hodnoty `QueryFunction`, které cíl vyslovuje. Množina, ne slovník s vynucenou úplností jako u kategorií, protože tady je mlčení bezpečná strana: funkce, kterou deskriptor neuvede, brána odmítne záznamem, ne tiše. Že dnes všech šest deskriptorů uvádí celý slovník, je fakt o dnešku držený testem, ne typem (086) — a je to plocha, na které sedmý framework řekne, co neumí, aniž se sáhne do `AbstractWrappers` (S1).

### Co se nemění

`IQueryVisitor` — operand vypisuje každý visitor uvnitř sebe, jako u 074 a 083, a podmínky větví `CASE` procházejí přes `Accept`, který existuje. Orchestrace. Kategorie T2 a jejich matice: přibývají řádky, žádný se nemění. `SubQueryInstruction` dostane výraz v projekci přes `ProjectInstruction`. Vzorek `LIKE` s literálem se dál rozkládá podle 051 a únikový znak podle 102.

## Důsledky

**Věty dřívějších rozhodnutí, které přestávají platit; rozhodnutí sama platí dál.** Věta 070, že výraz v podmínce je nepřečtená konstrukce a `Failure`, a věta 083 „parametr, ne konkatenace" zanikají, protože model tvar nese; volby obou — odmítnout, co by změnilo řádky; parametr jako pátý tvar — se nemění. Pětice `ProjectInstruction` a dvojice `OrderByInstruction` zanikají ve prospěch operandu; 102 a 073, které na nich stály, platí doslova, jen příznak a alias sedí o úroveň níž. Nový soubor, ne revize, protože všechno jmenované je naimplementované.

**Dvě položky `open-items.md` tímhle zanikají a jedna práce vzniká.** Rozhodnutí o řetězcové metodě LINQ s parametrem je zodpovězené konkatenací; položka Zbytků o výrazech nad sloupci přestává být mezí a stává se prací kategorie Dotazy s trojí vyslovenou hranicí (klíč seskupení, prvek výčtu, počet řádků). Katalog podmnožiny dostává kladnou půlku výrazů hotovou v tabulce slovníku výš a zápornou v seznamu mezí: funkce mimo slovník (jmenovitě `CAST`/`CONVERT`, okenní funkce, `DATEADD`/`DATEDIFF`, `REPLACE`, `LEFT`/`RIGHT`, `ROUND`, `SYSDATETIME`, `DateTime.UtcNow`), seskupení podle výrazu, výraz ve výčtu, agregát nad agregátem, projekce výrazu bez aliasu a `null` v konkatenaci C#.

**Řadit podle agregátu jde.** `ORDER BY COUNT(*) DESC` seskupeného dotazu, dnes `Loss` a vypuštěné řazení, se nese a vypisuje ve všech šesti cílích; je to oprava vedlejším účinkem sjednocení, ne cíl rozhodnutí, a v matici dostane vlastní řádek.

**Matice kategorií dostává nové řádky, každý z každého zdroje, který ho vysloví** (úvod kategorie Dotazy), a diferenční matice s nimi: `LikeWithABoundPrefix` (LINQ `StartsWith(prefix)`, T-SQL `LIKE @p + '%'`, HQL a JPQL `like concat(:p, '%')`), `ArithmeticInAProjection` (`Price * Quantity AS Total`), `FunctionInAFilter` (`UPPER(Name) = 'W'`), `CoalesceInAFilter`, `CaseInAProjection` a `OrderingByAnAggregate`. Každý se měří na všech čtyřech stupních ve všech směrech, které ho vysloví a nevydají odmítnutí; řádek `LIKE` s literálem zůstává, jak je.

**§9 a *Guarantees* dostávají meze, žádné zúžení:** seznam výš. Nárok „dotaz, který by vrátil jiné řádky, se nevydá" platí dál doslova, i pro hodnotu se zástupným znakem v parametru řetězcové metody. Řádky F8 a T2 trasovatelnosti dostanou nové řádky matice jako důkaz.

**Cena je největší ze všech rozšíření operandu a je vyslovená:** dva typy v `Model`, dva přepsané záznamy instrukcí, čtyři čtečky, čtyři visitory, brána o čtyřech pravidlech a typování, množina v šesti deskriptorech, krok `Project` v šabloně, šest řádků matice ve dvou sadách. Nic z toho nezasahuje `IQueryVisitor` ani orchestraci. Pro sedmý framework je to deskriptor s množinou a visitor s vlastním vypsáním operandu — táž plocha jako dosud (S1).

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR:** přibývá schopnost, vstupy, které dosud končily `Failure` nebo `Loss`, nově vydají artefakt; veřejné rozhraní ani tvar odpovědi se nemění.

**Testy.** Za každý tvar výrazu a každou čtečku čtení do modelu a zápis do všech šesti cílů včetně round tripu: konkatenace s parametrem ze všech čtyř jazyků s parametrem `String` v signatuře; `StartsWith(prefix)` z EF Core do všech cílů s řetězem `replace` a `ESCAPE '!'` a zpět do EF Core jako `StartsWith(prefix)`, diferenčně i s hodnotou, která zástupný znak nese; z NHibernate LINQ totéž bez escapování; `EF.Functions.Like(col, p + "%")` bez záznamu; `Price * Quantity AS Total` v projekci do všech cílů a `SUM(Price * Quantity)`; `+` nad dvěma sloupci typovaný bránou jako konkatenace i jako sčítání; `UPPER`, `LEN`/`length`/`.Length`, `COALESCE`/`ISNULL`/`??`, `SUBSTRING` s posunem pozice, `YEAR`/`extract`/`.Year`; `CASE` s `ELSE` i bez něj do LINQ a JPQL; `ORDER BY COUNT(*) DESC`. Odmítnutí, pokaždé prázdný výstup a `Failure` kategorie `Expression`: funkce mimo slovník v podmínce, funkce, kterou deskriptor testovacího cíle neuvádí, `+` bez typované strany, `Decimal` s `Float`, výraz v projekci bez aliasu, agregát nad agregátem, seskupení podle výrazu. Ztráta se záznamem: funkce mimo slovník v projekci, C# `+` nad sloupcem. A šest nových řádků matice ve všech směrech na 1.–4. stupni v obou sadách.
