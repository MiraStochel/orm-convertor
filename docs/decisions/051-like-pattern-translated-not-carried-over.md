# 051 — Vzorec `LIKE` se do LINQ překládá, ne přenáší

Datum: 2026-08-24
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [022](022-native-query-syntax-in-builders.md) a [024](024-typed-query-operand.md); nález z 2026-08-23, oddíl 3.6

## Kontext

`DapperSqlQueryParser` čte `LikePredicate` a uloží vzorek **nedotčený**: z `WHERE CustomerName LIKE '%Ltd%'` je v modelu konstanta `%Ltd%` (bez zdobení, jak žádá rozhodnutí [024](024-typed-query-operand.md)). `EFCoreLinqQueryVisitor` z toho udělá

```csharp
c.CustomerName.Contains("%Ltd%")
```

což je dotaz na řetězec obsahující **dvě doslovná procenta** — EF Core `Contains` překládá na `LIKE '%…%'` a zástupné znaky v argumentu escapuje. Výsledek tedy nevrací podmnožinu ani nadmnožinu zdrojového dotazu; vrací zpravidla **prázdnou** množinu tam, kde zdroj filtroval. Vydaný záznam u toho říká jen *„A LIKE comparison was written as string.Contains, which anchors differently"*, což je slabší tvrzení, než co se stalo: nejde o jiné ukotvení, jde o jiný predikát.

Míří to na T3 (podíl funkčně ekvivalentních výstupů) a na F11: artefakt, který se přeloží, spustí a vrátí jiné řádky, je horší než artefakt, který nevznikne — a rozhodnutí [004](004-unexpressible-facts-as-warnings.md) přesně zakazuje generovat náhražku.

Týká se to jediného směru, **Dapper → EF Core**. `Like` vyrábí jen SQL parser (`LinqParsing` `Contains` nemapuje, viz `architecture.md` §4.4) a HQL si vzorek nese doslova, takže NHibernate cíl je v pořádku.

## Zvažované varianty

1. **Nechat `Contains` a jen zesílit text záznamu.** Volající by se aspoň dozvěděl pravdu, ale artefakt by dál vracel jiné řádky a záznam `Loss` u toho by tvrdil, že výstup je „chudší", zatímco je *jiný*. Zamítáme.

2. **Vždycky `EF.Functions.Like(left, "%Ltd%")`.** Přesné, jednoduché a jednotné. Má ale dvě vady: generovaný výraz přestane být idiomatickým LINQ (`Contains` a `StartsWith` jsou to, co by na tom místě napsal člověk) a hlavně **se tím ztratí i tam, kde překlad být má** — `LIKE 'A%'` má v LINQ přesnou a čitelnou podobu `StartsWith("A")` a psát ji přes `EF.Functions` znamená nepřeložit dotaz, jen ho přenést. Rozhodnutí [022](022-native-query-syntax-in-builders.md) volilo nativní syntaxi cíle právě proto, aby výstup vypadal jako kód toho frameworku. Zamítáme jako jediné pravidlo.

3. **Vzorek rozebrat tam, kde má LINQ přesnou podobu, a jinak `EF.Functions.Like`.**

## Rozhodnutí

**Volíme variantu 3.** Vzorec se rozebere na kotvy a jádro:

| Vzorec | LINQ |
|---|---|
| `%x%` | `left.Contains("x")` |
| `x%` | `left.StartsWith("x")` |
| `%x` | `left.EndsWith("x")` |
| `x` (bez zástupných znaků) | `left == "x"` |

**Podmínkou je, že jádro `x` neobsahuje žádný zástupný znak** — `%`, `_` ani `[`. Jinak by rozklad lhal: `Contains("a_b")` hledá v EF Core doslovné podtržítko, kdežto `LIKE '%a_b%'` jím zastupuje libovolný znak. Rozklad je tedy povolený přesně tam, kde je *přesný*.

**Ve všech ostatních případech se vypíše `EF.Functions.Like(left, vzorec)`.** Sem patří vzorec se zástupným znakem uprostřed (`A%Ltd`), podtržítko, znaková třída, a také případ, kdy pravý operand vůbec není konstanta, ale sloupec — rozložit se nedá to, co při překladu neznáme. `EF.Functions.Like` je nativní EF Core konstrukce a překládá se na `LIKE` beze změny významu; jmenný prostor `Microsoft.EntityFrameworkCore` artefakt stejně už potřebuje kvůli `DbContext` v hlavičce metody, takže žádnou novou vazbu to nezakládá.

**Ani jedna větev nevydává záznam o ztrátě, protože se nic neztrácí.** Rozklad i `EF.Functions.Like` znamenají v cíli totéž co `LIKE` ve zdroji. Dosavadní záznam `Convention` mizí: tvrdil, že výstup říká něco, co zdroj neřekl, a po téhle změně to není pravda. **Co záznam mít musí, je `ESCAPE`** — klauzuli `LIKE … ESCAPE '\'` model nenese a parser ji hlásí jako ztrátu už dnes; to zůstává, protože bez ní se význam vzorce skutečně mění.

**Rozklad je vlastnost EF Core builderu, ne modelu.** Model dál nese `ComparisonOperator.Like` a doslovný vzorec, jak ho napsal zdroj — jednak proto, že HQL a SQL ho v téhle podobě potřebují, jednak proto, že rozklad je otázka *cílové* syntaxe, a tu podle rozhodnutí 022 řeší builder. Kdyby ho model rozložil, ztratil by tvar, který dva ze tří dnešních cílů umějí vypsat přímo.

## Důsledky

**Dapper → EF Core začne u `LIKE` vracet tytéž řádky jako zdroj.** To je posun v číslech T3 a v kategorii *filtrace* matice T2, takže se dá měřit; dosud tam ta kategorie tiše selhávala.

**Výstup se u `LIKE` mění.** Kdo se opíral o dosavadní `Contains("%Ltd%")`, dostane jinou podobu — což je smysl opravy, ne její vedlejší účinek.

**Zpětný převod EF Core → Dapper zůstává, jak byl.** `LinqParsing` `Contains`, `StartsWith` ani `EndsWith` nečte, takže vygenerovaný výraz se zpátky na `LIKE` nepřeloží; je to táž mezera, kterou `architecture.md` §4.4 popisuje u `ComparisonOperator.In`, a tohle rozhodnutí ji nezavírá ani nezhoršuje.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to PATCH:** opravuje se chybný výstup, rozhraní se nemění.
