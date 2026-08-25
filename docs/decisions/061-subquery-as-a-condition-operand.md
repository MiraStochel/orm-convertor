# 061 — Poddotaz jako operand podmínky

Datum: 2026-08-25
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [002](002-is-null-as-comparison-operator.md), [010](010-diagnostics-as-returned-data.md), [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md) a [060](060-pagination-as-a-query-instruction.md); JSS §5.3, pravidla Q1, Q5, Q11, Q13, Q14; audit 2026-08-02, kap. 8

## Kontext

Dotazová mezireprezentace zanoření nese — `SubQueryInstruction` je v slovníku instrukcí od začátku a pravidlo Q11 ho článek vyslovuje —, ale nic ho nevykreslí a skoro nic ho nevyrobí. Na vykreslovací straně nemá `IQueryVisitor` pro poddotaz metodu, `SubQueryInstruction.Accept` vrací prázdný řetězec a `Normalize()` vnořený poddotaz v těle hlásí záznamem o ztrátě. Na čtecí straně je to horší: SQL parser podmínku s `EXISTS`, `IN (SELECT …)` nebo skalárním poddotazem nepřečte a **zahodí celou klauzuli WHERE** se záznamem `Loss`, LINQ parser nemapuje `Contains` ani `Any`. Výstup tedy není chudší než vstup — vrací *jiné řádky*, což je přesně vada, kvůli které rozhodnutí 053 zakázalo tautologie a rozhodnutí 060 nenesené stránkování. Kategorie *poddotazy* je poslední kategorií dotazové matice T2, která nemá co měřit.

Zbývající otázka není „jak napsat kód", ale kde poddotaz v modelu sedí. Podmínkový strom článku (§5.3) odpovídá výslovně: operandy porovnání jsou „constants, attributes, **or nested subqueries**". Typovaný operand rozhodnutí 024 ale zná jen sloupec a konstantu, takže `IN (SELECT …)`, `EXISTS (…)` i `x > (SELECT MAX(…) …)` nemají v modelu místo. K tomu je potřeba říct, ve kterých pozicích se poddotaz vykresluje — operand podmínky, zdroj ve FROM, projekce — a co udělá cíl, který danou pozici nemá.

Pozice přitom nejsou rovnocenné a hranice neurčujeme my, ale cíle: HQL v NHibernate 5.7.0 připouští poddotazy **jen v klauzulích select a where** (dokumentovaná vlastnost jazyka), takže odvozená tabulka ve FROM nemá v jednom ze tří cílů žádný protějšek; LINQ poddotaz v podmínce vyjadřuje řetězem (`Contains`, `Any`, koncový agregát) a T-SQL nese všechno. Identifikátory se navíc podle Q13 rozresolvovávají přes mapovací mezireprezentaci — odvozená tabulka žádnou entitu nemá, takže by ve FROM pozici nebylo k čemu mapovat.

## Zvažované varianty

### 1 — Nechat poddotazy ztrátou se záznamem

Dnešní stav; nejmenší zásah. Zamítáme z téhož důvodu, z jakého rozhodnutí 060 zamítlo tutéž variantu u stránkování: záznam nedělá výstup pravdivým. Dotaz vydaný bez `EXISTS` podmínky vrací řádky, které zdroj vyloučil; ve skutečnosti je dnešní stav ještě horší, protože parser zahodí i tu část konjunkce, která by se přeložila správně. Matice T2 by v poslední kategorii dál měřila nulu a T3 by neměla co srovnávat.

### 2 — Vlastní podmínkové uzly (`ExistsCondition`, `InSubQueryCondition`)

Poddotazové predikáty by dostaly vlastní typy uzlů vedle `ComparisonCondition`. Zamítáme ze tří důvodů. Za prvé to zdvojuje tvar porovnání: skalární poddotaz (`x > (SELECT …)`) *je* porovnání a uzel „porovnání s poddotazem" vedle „porovnání" by nesl tytéž členy ještě jednou. Za druhé by každý nový uzel rozšířil `IQueryVisitor` o metodu ve všech třech visitorech — plocha, kterou S1 slibuje držet stabilní pro čtvrtý framework. Za třetí to odporuje gramatice článku: §5.3 klade poddotaz do pozice **operandu**, ne uzlu, a precedens už máme — rozhodnutí 002 řešilo zvláštní aritu `IS NULL` operátorem s nevyužitou pravou stranou, ne novým typem uzlu.

### 3 — Třetí tvar operandu, operátor `Exists` a vykreslení šablonou s pomocníkem builderů

## Rozhodnutí

**Volíme variantu 3. `QueryOperand` dostává třetí tovární tvar `Nested(SubQueryInstruction)`, slovník operátorů hodnotu `Exists` s nevyužitou pravou stranou, a poddotaz se vykresluje v pozici operandu podmínky filtru a po-agregačního filtru — ve všech ostatních pozicích se odmítá nebo zůstává ztrátou přesně podle toho, jestli by jeho zahození změnilo vrácené řádky.**

**Operand zůstává uzavřený typ s továrnami** (rozhodnutí 024): sloupec, konstanta, nebo poddotaz; neplatná kombinace není zapsatelná. `IN (SELECT …)` je `ComparisonCondition(sloupec, In, poddotaz)` — operátor `In` tím dostává prvního výrobce; `IN` s výčtem hodnot model dál nenese (operand nese jeden poddotaz, ne seznam konstant) a nově ho brána úplnosti odmítá záznamem `Failure`, místo aby k němu visitor došel a odpovídal každý po svém. `EXISTS (…)` je operátor `Exists` s poddotazem jako levým operandem a nevyužitou pravou stranou — týž tvar, jaký rozhodnutí 002 dalo `IS NULL`; `NOT EXISTS` je `NotCondition` nad ním a nový operátor nepotřebuje. Skalární poddotaz je obyčejné porovnání, jehož jedním operandem je poddotaz.

**Pozice vyslovujeme taxativně.** Vykresluje se jediná: operand porovnání ve WHERE a HAVING. Ostatní pozice:

- **zdroj ve FROM** (odvozená tabulka) se do modelu nepřijímá — parser ji odmítá záznamem `Failure`, jak to už dělá, a nově je to pravidlo, ne náhoda. Důvody jsou dva a oba vnější: HQL tuhle pozici nemá vůbec a Q13 nemá odvozenou tabulku k čemu rozresolvovat.
- **ON klauzule joinu** je `Failure` v bráně úplnosti: LINQ join bere dva klíčové selektory a poddotaz do nich nevepíše, a zahodit join s podmínkou znamená jiné řádky.
- **projekce** zůstává ztrátou se záznamem, jak je: vypuštěný sloupec dělá výstup chudší, řádky nemění.
- **tělo poddotazu tvořené množinovou operací** (`IN (A UNION B)`) je `Failure`: operand nese jeden SELECT.
- **`IN` a skalární porovnání vyžadují poddotaz s právě jednou projekcí** — nula (celá entita) nebo víc sloupců je `Failure`; T-SQL sám takový dotaz za běhu odmítá, my ho odmítáme dřív a se záznamem.

**Vykreslení je práce builderů, `IQueryVisitor` se nemění** — táž věta, jakou má rozhodnutí 060, a tady ze stejného důvodu: složit vnořený rozsah znamená `Normalize`, sedm kroků a závěrečné pořadí textu, což je přesně to, co šablona a `FinalizeQuery` builderů už umějí pro operandy množinových operací. Visitor při setkání s poddotazovým operandem volá zpět do svého builderu; mechanické kontroly — tělo-množinová-operace, počet projekcí — drží jeden sdílený pomocník šablony (`NormalizeSubQueryOperand`), aby tři buildery odpovídaly stejně, protože odpovídá jedno místo (rozhodnutí 023). Tvary cílů:

- **Dapper**: `x IN (SELECT …)`, `EXISTS (SELECT …)`, `x > (SELECT …)` — vnořený SELECT projde týmiž kroky jako operand množinové operace. Řazení uvnitř poddotazu bez stránkování T-SQL nepřipouští a na množinu řádků `IN`/`EXISTS`/agregátu nemá vliv, takže se vypustí se záznamem `Loss`; se stránkováním se nese (`IN (SELECT TOP 5 …)`).
- **EF Core**: `IN` je `řetěz.Select(x => x.Sloupec).Contains(vnějšíOperand)`, `EXISTS` je `řetěz.Any()`, skalární agregát je koncové volání (`řetěz.Max(x => x.Sloupec)`). Skalární poddotaz, který **není** jediným agregátem, věrný tvar LINQ nemá — `First()` by tiše vybral jeden řádek tam, kde SQL víc řádků odmítá — a je `Failure` v místě emise.
- **NHibernate**: `x in (select …)`, `exists (from …)`, `x > (select max(…) …)`. Řazení uvnitř poddotazu HQL nepřipouští — `Loss` a vypustí se; stránkování uvnitř poddotazu nese jedině API `IQuery`, do textu HQL nejde, takže je `Failure` v místě emise — táž věta, jakou rozhodnutí 060 vyslovilo pro operandy množinových operací.

**Korelace se nese aliasy, ne mechanismem.** Vnořený rozsah smí jmenovat aliasy vnějšího dotazu; SQL a HQL je vypisují doslova (slovník aliasů vnořeného rozsahu se slévá s vnějším a vnitřní stíní), LINQ je vidí jako parametr vnější lambdy — koliduje-li jméno vnitřního parametru s vnějším, přejmenuje se, protože C# stínování parametrů lambd zakazuje.

**Vnořený poddotaz, který zbude v těle dotazu jako instrukce** — pozice, kterou po tomhle rozhodnutí žádný parser nevyrábí —, přestává být ztrátou a je `Failure`: instrukce bez vykreslované pozice znamená, že by výstup vracel jiné řádky, a záznam `Loss` s vydaným artefaktem byl přesně tvar, který otevřená položka o mezích pravidla 053 vedla jako spornou poddotazovou půlku. Tímhle se ta půlka zavírá a položka zbývá jen o joinu.

## Důsledky

**Kategorie *poddotazy* matice T2 začíná měřit překlad** — poslední prázdná kategorie. `EXISTS`, `IN (SELECT …)` a skalární agregátový poddotaz se překládají ve všech devíti směrech; co se nést nedá, se počítá jako odmítnutí, ne jako úspěch, což je pro T3 totéž zlepšení jako u rozhodnutí 053 a 060.

**Parsery přestávají zahazovat celé WHERE u tvarů, které nově čtou.** SQL parser čte `ExistsPredicate`, `InPredicate` s poddotazem a `ScalarSubquery` v operandu; sdílený LINQ parser čte `Contains` a `Any` nad kořenem dotazu (`Any(predikát)` jako `Where(predikát).Any()`, `Count(predikát)` obdobně) a koncové agregáty `Max`/`Min`/`Sum`/`Average`/`Count` jako skalární poddotaz. Podmínka, kterou strom pořád neunese, se zahazuje jako dosud — hranice mezi `Loss` a `Failure` u zahozeného filtru je otevřená položka o mezích pravidla 053 a tohle rozhodnutí ji nerozhoduje.

**Slovník se nerozšiřuje nad článek** — na rozdíl od stránkování (060) tu `SUBQUERY` i operand-poddotaz v gramatice podmínek článek má (Q11, §5.3); doplňujeme jen operátor `Exists`, který je predikátem nad poddotazem stejně, jako je `IsNull` predikátem nad sloupcem.

**Pro čtvrtý framework se nemění nic** (S1): JPQL nese poddotazy právě ve WHERE a HAVING a ve FROM je nepřipouští, takže hranice pozic sedí beze změny; javový builder dostane od šablony tytéž mechanické kontroly.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR**: vstupy, které dřív vydaly artefakt bez poddotazu se záznamem `Loss`, nově vydají buď úplný artefakt, nebo žádný s `Failure`; veřejná plocha se nemění. Vyjde v 1.2.0.

**Testy.** Překlad `IN (SELECT …)`, korelovaného `EXISTS` a skalárního `MAX` poddotazu v obou směrech čtení (SQL i LINQ) do všech tří cílů; `NOT EXISTS` přes `NotCondition`; `Any(predikát)` jako `Where(…).Any()`; odmítnutí — množinová operace v těle operandu, poddotaz s nula i dvěma projekcemi u `IN`, poddotaz v ON klauzuli, skalární neagregátový poddotaz u EF Core, stránkování uvnitř poddotazu u NHibernate — pokaždé prázdný výstup a `Failure`; řazení uvnitř poddotazu vypuštěné se záznamem `Loss`; stránkování uvnitř poddotazu nesené u Dapperu a EF Core. Třetí stupeň ověření: vygenerovaný řetěz s `Contains` a `Any` přeloží provider EF Core, vygenerované SQL s poddotazem projde parserem a rozresolvováním.
