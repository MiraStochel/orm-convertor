# 047 — Typ obsahu dojde až do dotazového parseru

Datum: 2026-08-24
Stav: platí
Požadavky: F7–F10, F11, F14, S1, S2, S7
Podklad: rozhodnutí [025](025-query-language-as-content-type.md) a [026](026-home-of-shared-query-reading.md); nález z 2026-08-23, oddíl 3.2

## Kontext

Rozhodnutí [025](025-query-language-as-content-type.md) postavilo dotazovou větev na tom, že **jazyk vstupu určuje typ obsahu**, ne pohled do textu: jednotka přijde označená jako `SqlQuery`, `CSharpQuery` nebo `HqlQuery`, orchestrace se parserů zeptá `CanParse(contentType)` a přesně jeden se přihlásí.

`DapperSqlQueryParser` je jediné místo v řešení, které tohle pravidlo poruší. Dapperový dotaz má dvě podoby — holé T-SQL a T-SQL uvnitř volání `Query<T>(…)` v C# — a parser mezi nimi rozhoduje řetězcovou heuristikou:

```csharp
private static bool LooksLikeCSharp(string source)
    => source.Contains("Query", StringComparison.Ordinal)
       && (source.Contains('(') && source.Contains(';'));
```

Typ obsahu je přitom k dispozici: `Parse` se volá až poté, co `CanParse(contentType)` vrátil `true`, a orchestrace tu hodnotu drží. Jen se dolů nepředává — `IQueryParser.Parse(string, IReadOnlyList<EntityMap>?)` ji nemá v podpisu.

**Důsledek je pozorovatelný, ne teoretický.** `SELECT * FROM QueryLog WHERE (Id = 1);` projde všemi třemi testy heuristiky, jde do `ExtractSql`, žádné volání Dapperu tam Roslyn nenajde a jednotka skončí záznamem `Failure` „No Dapper query call was found in the source." Uživatel poslal platné SQL označené jako SQL a dostal zprávu o chybějícím volání Dapperu — což je přesně ta třída matoucí diagnostiky, které má bránit F11 a jejíž srozumitelnost žádá S7.

Zúžit heuristiku (třeba „nezačíná `SELECT`/`WITH`") problém neodstraní, jen posune: každá taková podmínka je hádání jazyka z textu tam, kde ho vstup deklaruje.

## Zvažované varianty

1. **Nechat heuristiku a zpřesnit ji.** Levné a nic nerozbije. Jenže tím zůstane v řešení jediné místo, které jazyk vstupu hádá, přestože rozhodnutí 025 tvrdí opak; a každý další dotazový jazyk (JPQL, dynamické SQL MyBatisu podle F8) přidá další falešně pozitivní případ, protože všechny se skládají z týchž znaků. Zamítáme.

2. **Rozdělit Dapper parser na dva — jeden pro `SqlQuery`, druhý pro `CSharpQuery`.** Odpovídalo by to tvaru rozhodnutí 025 („přesně jeden parser se přihlásí"). Jenže obě podoby čte tentýž kód a liší se jediným krokem navíc; dva parsery by znamenaly dvě `CanParse` a společného předka jen kvůli tomu, aby se nezdvojil zbytek. Navíc by to nic neřešilo u NHibernate, kde tentýž parser podle F7 poroste o HQL vedle LINQ. Zamítáme.

3. **Předat typ obsahu do `Parse`.**

## Rozhodnutí

**Volíme variantu 3: `IQueryParser.Parse` dostává typ obsahu jednotky jako parametr a `DapperSqlQueryParser` podle něj volí vstupní fázi.** `CSharpQuery` znamená „nejdřív najdi volání Dapperu Roslynem", cokoli jiného znamená „ber vstup jako SQL". `LooksLikeCSharp` mizí bez náhrady.

**Podpis je jeden a povinný.** `Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)`. Typ obsahu stojí jako první parametr, protože je to *o čem* se mluví, ne volitelný doplněk; a je bez výchozí hodnoty, protože výchozí hodnota by byla hádání zpátky ve zdrobnělé podobě.

**Zásah do `AbstractWrappers` je tady na místě, ne proti S1.** S1 tvrdí, že přidání frameworku nevyžaduje změnu rozhraní. Tohle není přidání frameworku — je to oprava rozhraní, které nenese informaci, na které stojí rozhodnutí 025, a která se proto v jednom wrapperu rekonstruuje odhadem. Změna se dotkne dvou implementací (`DapperSqlQueryParser` a `LinqQueryParser`, ze kterého dědí EF Core i NHibernate) a jednoho volajícího v orchestraci; **po ní je nový framework s vlastní gramatikou dotazů levnější, ne dražší**, protože nemusí vymýšlet, jak svůj jazyk poznat z textu. Právě tak to bude potřebovat parser HQL (otevřená položka) i parsery javové strany podle F7–F10.

**Entitní podpis se nemění, jen dostane vlastní rozhraní.** Entitní parsery čtou každý jediný jazyk a orchestrace jim jednotku podává až po `CanParse`; nemají co rozhodovat, takže by parametr nenesl žádné rozhodnutí, a rozšířit obojí kvůli symetrii by znamenalo přidat nepoužitý argument do tří wrapperů. Aby ale dotazový parser nezůstal s `Parse(string)`, na které nesmí odpovídat, dělí se `IParser` na to, co mají obě strany společné (`CanParse`), a na `IEntityParser` s dosavadním `Parse(string)`. Orchestrace tím zároveň přestává entitní parsery vybírat vyloučením (`p is not IQueryParser`) a začíná je vybírat jménem (`OfType<IEntityParser>`), což je totéž pravidlo vyslovené kladně.

## Důsledky

**Chování se mění pro dva vstupy, oba k lepšímu.** SQL, které náhodou obsahuje slovo `Query`, závorku a středník, se nově naparsuje jako SQL. C# volání Dapperu, které slovo `Query` neobsahuje (`ExecuteScalar`), se nově pošle Roslynu, jak má — dosud šlo rovnou do T-SQL parseru a skončilo hlášením o neparsovatelném SQL.

**Determinismus podle S2 se posiluje.** Výsledek přestává záviset na obsahu řetězce a začíná záviset výhradně na deklaraci vstupu, což je hodnota, kterou volající sám posílá.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MAJOR uvnitř řešení a PATCH navenek:** mění se veřejné rozhraní `IQueryParser`, ale REST kontrakt ani tvar odpovědi ne — `/convert` už dnes typ obsahu u každé jednotky vyžaduje.
