# 070 — Parser odmítá dotaz, jehož nepřečtená část by změnila množinu řádků

Datum: 2026-09-14
Stav: platí
Požadavky: F8, F11, T2, T3, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [024](024-typed-query-operand.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [061](061-subquery-as-a-condition-operand.md) a [065](065-row-set-as-the-boundary-of-rule-053.md); otevřená položka „Nepřečtený filtr se zahazuje a dotaz se vydá"; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §15

## Kontext

Rozhodnutí 053 vyslovilo, že dotaz, který by v cíli vrátil jinou množinu řádků, se nevydá, a rozhodnutí 065 určilo hranici toho pravidla: množina řádků, ne podmínka. Obě ale mluví o builderu — o bráně v `Normalize()` a o visitorech v místě emise. Čtecí strana zůstala tam, kde byla před nimi. Všechny tři dotazové parsery — T-SQL, LINQ i HQL — odpovídají na konstrukci, kterou do podmínkového stromu nedostanou, záznamem `Loss` a čtou dál: dotaz odejde bez svého filtru, a stejně tak bez joinu s nepřečtenou ON klauzulí (T-SQL i HQL) nebo s klíčovými selektory, které se nespárují (LINQ), jen s prvním ze zdrojů oddělených čárkou, bez seskupovacího klíče, který není sloupec, a bez `distinct`. T-SQL `SELECT DISTINCT` parser nečte vůbec, takže mizí i bez záznamu.

Je to přesně vada, kvůli které 053 vzniklo, jen o krok dřív: dosazená tautologie a zahozený filtr vrátí tytéž řádky navíc. Záznam `Loss` přitom o artefaktu tvrdí, že „vzniká a je platný, jen chudší než vstup" (`architecture.md`, §5.1) — a to tu neplatí, výstup není chudší, je jiný. Pro T3 je to tichý falešný pozitiv, pro F11 obejití vlastního slibu a pro README nepravda: věta „a query that would return a different set of rows is not emitted at all" platí od rozhodnutí 065 doslova na straně builderu a vůbec ne na straně parseru.

Nejběžnějším spouštěčem je parametr dotazu. Skutečný Dapper dotaz má `WHERE Id = @id`, HQL `:id` nebo `?`, LINQ porovnává sloupec s hodnotou z okolního scope — a mezireprezentace pojem parametru nemá. Rozhodnutí 024 to zapsalo vědomě: „do té doby je parametr ve zdroji ztrátou se záznamem", s vlastním rozhodnutím až „s prvním dotazem, který má hodnotu dodat volající". Psalo se pět dní před 053 a 053 se k parserům nevyjádřilo. Dnes tedy všechny tři parsery dělají totéž:

| Parser | Parametr ve zdroji | Co udělá |
|---|---|---|
| T-SQL | `@id` (`VariableReference`) | operand `null`, klauzule vydá `Loss` kategorie `Filtering`, dotaz odejde bez filtru |
| HQL | `:id`, `?` | totéž |
| LINQ | holý identifikátor (`c.Id == id`) | totéž |

Záznam přitom neříká, že šlo o parametr: kategorie je `Filtering` a text zní „a construct the condition tree cannot carry". Kategorie `QueryParameter` v `QueryFeature` existuje od rozhodnutí 022 a všechny tři deskriptory ji uvádějí jako *umím vyjádřit* — o cílech to platí, ale nikdo ji nevyrábí, takže se uživatel z diagnostiky nedozví to jediné, co by mu pomohlo.

Položka tedy klade dvě otázky: jestli se pravidlo 053 rozšiřuje i na parsery, a jestli mezireprezentace dostane operand parametru. Druhá otázka má i javovou stranu: F8 žádá dynamicky parametrizované dotazy MyBatisu a bez operandu parametru je přečíst nejde (srovnání javových frameworků, §15).

## Zvažované varianty

### 1 — Nechat parsery u `Loss`

Nejmenší zásah a dnešní stav. Zamítáme z téhož důvodu, z jakého rozhodnutí 053 zamítlo tautologii a rozhodnutí 061 zahozený poddotaz: záznam nedělá výstup pravdivým. Artefakt bez filtru se přeloží, zkompiluje, spustí a odpoví jinými řádky — a protože záznam je `Loss`, tvrdí o sobě, že je platný. Pravidlo o množině řádků by pak platilo jen pro to, co parser přečte, což je hranice vedená tam, kde ji náhodou nechal kód, ne úvaha.

### 2 — Rozšířit pravidlo na parsery a zavést operand parametru hned

Odstranilo by to nejběžnější spouštěč, místo aby ho jen poctivě odmítlo, a je to směr, kterým rozhodnutí 024 ukázalo. Zamítáme ale *pro teď*, ze tří důvodů.

Za prvé operand není jen jméno. Každý ze tří cílů vydává vedle holého dotazu spustitelnou C# metodu (rozhodnutí 025) a parametr v ní musí být **typovaný parametr metody**: Dapper ho předá v anonymním objektu, NHibernate přes `SetParameter`, EF Core jako proměnnou zachycenou lambdou — a `int == object` se v LINQ nepřeloží. Typ by builder musel odvodit z druhé strany porovnání přes mapovací mezireprezentaci, a tam, kde druhou stranou je agregát, vzorek `LIKE`, poddotaz nebo jiný parametr, potřebuje odvození vlastní pravidla. To je návrh, ne přepnutí druhu záznamu.

Za druhé tvar operandu určují producenti, které dnes nemáme. U .NET trojice je parametr vždycky vázaná hodnota. MyBatis rozlišuje vázaný `#{id}` od textové substituce `${id}`, jeho dynamické SQL je rodina příkazů, z níž `getBoundSql` vrací jednoho člena, a JPA nese pojmenovanou i poziční formu (srovnání javových frameworků, §15). Rozhodnutí o operandu má tyhle tvary rozsoudit najednou — jinak vznikne slovník, který se s prvním javovým producentem přepisuje, tedy přesně to, čemu rozhodnutí 020 u parametrů generátoru předešlo tím, že slovník navrhlo proti celé množině producentů. Položka o vstupu javové strany je `Na řadě`, takže ten okamžik není daleko.

Za třetí cena odkladu je malá a poctivá. T2 dělí matici na projekci, filtraci, joiny, agregaci, stránkování, poddotazy a množinové operace — parametr mezi kategoriemi není, takže odmítnutí nevyprázdní žádnou buňku matice; a odmítnout se záznamem, který jmenuje důvod, je totéž, co 053 dělá se vším ostatním, co model nenese.

### 3 — Rozšířit pravidlo na parsery; parametr je odmítnutí s vlastní kategorií; operand přijde s prvním producentem, který ho vyžaduje

## Rozhodnutí

**Volíme variantu 3. Parser, který konstrukci nedostane do modelu, si klade touž otázku jako šablona builderu: vrátil by artefakt bez ní jiné řádky? Pokud ano, je to `Failure` v místě čtení a artefakt nevzniká; pokud ne, zůstává to `Loss` jako dosud.** Kritérium je to z rozhodnutí 065, jen se posouvá o krok dřív; mechanismus už existuje — `Failure` kdekoli na dotazovém kanálu ruší artefakt (rozhodnutí 053) — a model, `AbstractWrappers` ani orchestrace se nemění (S1).

Konkrétně to znamená pět věcí:

**Nepřečtený filtr, join, zdroj, seskupení a `DISTINCT` odmítají artefakt.** Filtr je `WHERE`/`Where`/`where` i filtr po agregaci; join je u T-SQL join na něco jiného než tabulku a nepřečtená ON klauzule, u HQL join po asociační cestě, join bez `with` a nepřečtená `with` podmínka, u LINQ `Join` s málo argumenty, se selektory, které nejsou lambdy, nebo které se nespárují sloupec za sloupcem; zdroj je křížový join čárkou v T-SQL i HQL; seskupení je klíč, který není sloupec, a v T-SQL `GROUP BY ROLLUP`/`CUBE`/`GROUPING SETS`; `DISTINCT` je `SELECT DISTINCT`, `select distinct` i `Distinct()`, včetně `Distinct()` za množinovou operací. K nim patří klauzule `ESCAPE` u `LIKE`, protože vzorek čtený bez ní vybere jiné řádky. Parser po `Failure` čte dál, aby uživatel dostal všechny důvody najednou; artefakt zadrží kanál.

**`Loss` s vydaným artefaktem zůstává tam, kde je výstup chudší, ne jiný:** nepřečtená projekce, nepřečtený klíč řazení, řazení za množinovou operací a uvnitř poddotazu, `fetch` a `Select` za množinovou operací. Zůstává i u kroku řetězu LINQ, který parser nezná: tam nemá z čeho soudit, co by krok změnil, a odmítnout každé neznámé volání by odmítlo i `Include` nebo `TagWith`, které řádky nemění. Je to vyslovená mez — kroky, o kterých parser ví, že řádky mění, jsou vyjmenované (`Distinct`, filtr, join, seskupení, stránkování), a nový takový krok se do výčtu přidá, jakmile se objeví; záznam neznámého kroku proto říká jen, že volání vypadlo, a netvrdí, že výstup je chudší.

**Parametr je `Failure` kategorie `QueryParameter` a záznam ho jmenuje.** Každý parser pozná svůj tvar — `VariableReference` v T-SQL, token `:id` nebo `?` v HQL, v LINQ holý identifikátor v pozici operandu, tedy hodnota z okolního scope — a klauzule, která kvůli němu spadla, vydá záznam ve tvaru „The WHERE clause uses the parameter '@id', for which the query representation has no operand, and a query emitted without its filter would return different rows; no artifact was generated." Kategorie `QueryParameter` tím dostává prvního výrobce. Deskriptory ji dál uvádějí jako *umím vyjádřit*, protože to o cílech platí — všechny tři parametr zapsat umějí —; záznam pojmenovává chybějící operand v modelu, ne neschopnost cíle, a mechanické hlášení podle pravidla Q14 by se stejně nespustilo. `IN` se seznamem hodnot dostává touž jmenovitou větu s kategorií `Filtering`, ve shodě s branou v `Normalize()`, která tentýž tvar odmítá na straně builderu.

**Věta rozhodnutí 024 o parametru přestává platit; 024 samo platí dál.** Jeho volba — typovaný operand se skalárem — se nemění; mění se jen osud parametru, který 024 zapsalo mezi důsledky jako ztrátu se záznamem. Nový soubor je tu proto, že 024 je naimplementované a revize na místě by čtenáři vzala starší znění, ne proto, že by se volba obracela — týž vztah, jaký má 065 k 053.

**Operand parametru dostane vlastní rozhodnutí s prvním producentem, který ho vyžaduje, a tím je F8.** To rozhodnutí bude muset zodpovědět nejméně čtyři věci: typovaný parametr metody artefaktu a odvození jeho typu přes mapovací mezireprezentaci; vázanou hodnotu proti textové substituci; pojmenovanou proti poziční formě a jejich převod mezi cíli; a co s parametrem v holém textovém artefaktu (`SqlQuery`, `HqlQuery`), který metodu nemá. Ukazatel drží položka o vstupu javové strany v `open-items.md`; tahle položka tím zaniká.

## Důsledky

**T3 se narovnává i na čtecí straně.** Dotaz, který parser nepřečte celý, se nově počítá jako nepřeložený se záznamem; dřív se počítal jako přeložený a neekvivalentní. Čtení T2 se nemění — žádná kategorie se nevyprázdní, parametr mezi nimi není — a buňka, ve které se odmítá, říká proč.

**Nárok README platí doslova na obou stranách.** „A query that would return a different set of rows is not emitted at all" přestává mít tichou výhradu čtení.

**Dotaz s parametrem se do žádného cíle nepřeloží — a je to vyslovené.** Většina reálných Dapper dotazů je parametrizovaná, takže je nástroj do doby, než operand přijde, odmítá. To je viditelná cena varianty 3 a je poctivá: dosavadní výstup pro ně byl špatně, ne chudší.

**Vzorový dotaz EF Core se mění.** Filtroval na `new System.DateTime(2025, 1, 1)`, což sdílený LINQ parser nečte — čte literály, sloupce, poddotazy a nově pojmenuje hodnotu ze scope —, takže vzorek odcházel bez toho filtru: sama vada tohoto rozhodnutí v dodávaných datech. Vzorek o ten predikát přichází, řazení podle data zůstává. Číst `new DateTime(r, m, d)` jako konstantu `DateTime` by bylo malé rozšíření — slovník i všechny tři visitory jsou na ni připravené —, ale je to schopnost, ne součást tohoto pravidla, a dostane vlastní krok, bude-li o ni stát.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to PATCH:** mění se jen výstup, který byl špatně — dotaz, který vracel jiné řádky, se nevydá —, a žádná schopnost nepřibývá. Rozhodnutí 053 a 065 zapsala obdobnou změnu jako MINOR; kritérium 041 je ale jediná otázka — byl předchozí výstup správný? — a tady nebyl. Vyjde s nejbližším vydáním cíle 2.

**Testy.** U každého ze tří parserů parametr: žádný artefakt, jediný záznam `Failure` kategorie `QueryParameter`, který parametr jmenuje. Křížový join čárkou, `SELECT DISTINCT` (dosud bez záznamu), `GROUP BY ROLLUP`, `ESCAPE` (T-SQL), join po asociační cestě, `in` se seznamem hodnot a `select distinct` (HQL), nepřečtený predikát (LINQ): pokaždé žádný artefakt a `Failure`. A mez z druhé strany: klíč řazení, který strom neunese, zůstává `Loss` s vydaným artefaktem.
