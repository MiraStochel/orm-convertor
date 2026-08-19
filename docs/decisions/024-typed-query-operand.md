# 024 — Typovaný operand dotazové podmínky

Datum: 2026-08-19
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S2
Podklad: rozhodnutí [002](002-is-null-as-comparison-operator.md), [014](014-language-type-model.md) a [022](022-native-query-syntax-in-builders.md); JSS §6.2, pravidla Q5 a Q13

## Kontext

Porovnávací podmínka je dnes záznam o devíti pozičních členech:

```csharp
public sealed record ComparisonCondition(
    string? LeftTable,  string? LeftProperty,  string? LeftConstant,  string? LeftFunction,
    ComparisonOperator Operator,
    string? RightTable, string? RightProperty, string? RightConstant, string? RightFunction
) : ConditionNode;
```

Dvě čtveřice popisují touž věc — operand —, ale model to nikde neříká, takže se ta shoda musí uhlídat rukou na každé straně zvlášť. Horší je ale konstanta. `LeftConstant` a `RightConstant` nesou **doslovný text tokenu tak, jak ho napsal zdroj**, a mezireprezentace neví, jestli je to číslo, řetězec nebo datum. Důsledky jsou v repozitáři vidět všechny tři:

- `EFCoreLinqQueryParser` bere literál jako `lit.Token.Text`, takže z LINQ `c.CreditLimit > 2000m` vyleze do SQL `2000m` — C# přípona typu doteče až do dotazu.
- `AdvisorBenchmarking` má proti tomu `StripCSharpNumericSuffixes`, tedy opravu cizí chyby na místě, které o původu hodnoty nic neví.
- `DapperSQLQueryVisitor` dělá `constant.Replace('"', '\'')`, tedy hádá, že uvozovky v textu znamenaly řetězec, a přepisuje je na apostrofy.

Dokud byl jediným cílem SQL, dalo se s tím žít: zdrojem byl C# a cílem řetězec, takže stačilo pár záplat na cestě. **Rozhodnutím [022](022-native-query-syntax-in-builders.md) se to mění**, protože cílem se stává i LINQ. Řetězcová konstanta se do SQL píše `'Foo'` a do C# `"Foo"`; celé číslo se do SQL píše `2000` a do C# `2000`, ale decimal se do C# píše `2000m` a do SQL bez přípony. Bez typu hodnoty nemá builder z čeho tu volbu udělat a musel by hádat z tvaru textu — a to je hádání, které se pokaždé rozhodne stejně špatně u prázdného řetězce, u čísla v uvozovkách a u data.

Zároveň přibývá třetí producent konstant. Parser SQL pro Dapper dostane z T-SQL literálu rovnou typovanou hodnotu; kdyby ji musel převádět zpátky na doslovný text C#, zahazoval by informaci, kterou právě získal, jen aby ji další builder znovu hádal.

## Zvažované varianty

### 1 — Nechat konstantu doslovným textem a normalizovat ji v builderech

Nejmenší zásah. Každý builder by při emisi text upravil pro svůj jazyk — právě to dělá dnešní `Replace('"', '\'')`. Zamítáme: normalizace bez typu je nerozhodnutelná. Text `2000` může být číslo i řetězec, text `"2000"` může být řetězec s uvozovkami i uvozovkami zapsané číslo, a text `2026-08-19` může být datum i řetězec. Tři buildery by hádaly každý zvlášť a S2 by platilo jen do prvního nejednoznačného vstupu.

### 2 — Nést vedle textu příznak „je to řetězec"

Levná polovina řešení. Rozhodlo by to o uvozování, což je nejčastější případ. Zamítáme, protože nerozhodne o příponách (`2000m` versus `2000`), o formátu data ani o `null`, a protože příznak vedle textu je přesně ten tvar, který rozhodnutí [014](014-language-type-model.md) odmítlo u jazykových typů: pole, jejichž platné kombinace hlídá zvyk, ne konstruktor.

### 3 — Vytknout operand do vlastního typu a dát konstantě skalární typ z existujícího slovníku

## Rozhodnutí

**Volíme variantu 3. Operand se stává vlastním typem a konstanta v něm nese hodnotu bez zdobení spolu se skalárním typem ze slovníku `ScalarType`. `ComparisonCondition` se zkracuje na trojici levý operand, operátor, pravý operand.**

**Slovník se nezavádí nový.** Skalární typ už v modelu je — `ScalarType` z rozhodnutí [014](014-language-type-model.md), uzavřený výčet od `Bool` po `Guid`, ekosystémově neutrální. Konstanta v dotazu a hodnota vlastnosti jsou tentýž druh faktu, takže druhý slovník pro totéž by byl přesně ta duplicita, kterou 014 odstraňovalo na jazykové straně. `ScalarType` navíc dovoluje builderu rozhodnout obojí, co potřebuje: jak hodnotu uvozovat a jakou příponu nebo formát jí dát.

**Hodnota se nese bez zdobení.** Do modelu jde `Foo`, ne `"Foo"` ani `'Foo'`; jde `2000`, ne `2000m`. Uvozování a přípony jsou vlastnost cílového jazyka a patří do emise. Parser tedy zdobení **odstraňuje**, builder ho **přidává** — táž dělba, jakou má rozhodnutí [021](021-generator-name-selection.md) mezi kanonizací v parseru a překladem zpět v builderu. Neznámý nebo nerozpoznaný typ konstanty zůstává bez skaláru a hlásí se záznamem; builder ho pak vypíše doslova, protože mlčení by znamenalo neúplný artefakt místo neúplného tvrzení.

**Operand je jeden typ se čtyřmi poli** — tabulka, vlastnost, konstanta, funkce —, takže obě strany porovnání jsou tentýž tvar a nelze je zaměnit. Instance vznikají továrními metodami, aby neplatná kombinace (konstanta i vlastnost zároveň) nebyla zapsatelná; je to týž postup, jakým 014 uzavřelo `LangType`.

**Pravidlo o `NULL` se nemění.** Test na `NULL` zůstává operátorem `IsNull`/`IsNotNull` s nevyužitým pravým operandem podle rozhodnutí [002](002-is-null-as-comparison-operator.md). Typovaný operand na tom nic nemění a `null` se nestává konstantou — kdyby se jí stal, měli bychom na jeden fakt dvě cesty a rozhodnutí 002 by přestalo platit samo od sebe.

**Přepis je jednorázový, bez přechodného období** (rozhodnutí [003](003-one-shot-migration.md)). Konstrukčních míst je řádově dvacet a všechna jsou uvnitř repozitáře: dva parsery, jeden visitor a testy dotazové větve.

## Důsledky

**Dělá se teď, ne až potom.** Dokud existuje jeden dotazový builder, je to refaktoring jednoho spotřebitele; po rozhodnutí 022 jich budou tři a každý by si mezitím napsal vlastní hádání typu, které by se pak muselo odstraňovat ze tří míst. Cena je nejnižší v tuhle chvíli a od teď jen roste.

**`AdvisorBenchmarking.StripCSharpNumericSuffixes` zaniká.** Přípona se do dotazu nedostane, takže není co strhávat. Totéž platí pro `Replace('"', '\'')` v dapperovském visitoru.

**Pravidlo Q5 dostává ostřejší tvar.** „Podmínky jsou strom zachovávající logickou strukturu" platilo dosud jen o uzlech; nově platí i o listech, protože list přestává být řetězcem neznámého významu.

**Parametrizace dotazu tím vyřešená není.** Dapper i oba plnohodnotné frameworky parametrizují, mezireprezentace pojem parametru nemá a tohle rozhodnutí ho nezavádí — konstanta je konstanta. Zůstává to otevřené a s prvním dotazem, který má hodnotu dodat volající, to bude potřebovat vlastní rozhodnutí; do té doby je parametr ve zdroji ztrátou se záznamem.

**Testy.** Pro každý cíl jedna podmínka s řetězcovou a jedna s číselnou konstantou, aby bylo vidět, že se táž hodnota do SQL a do C# vypisuje jinak; a test, že konstanta bez rozpoznaného typu projde doslova a se záznamem.
