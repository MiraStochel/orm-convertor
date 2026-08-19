# 025 — Dotazový jazyk jako typ obsahu

Datum: 2026-08-19
Stav: platí
Požadavky: F7–F10, F11, F14, S1, S2, S7
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md); JSS Table 2; `ConversionContentType`, `IParser.CanParse`

## Kontext

Typ obsahu má dnes tři hodnoty: `CSharpEntity`, `CSharpQuery` a `XML`. Je to jediný rozlišovač, podle kterého `IParser.CanParse` rozhoduje, jestli daný vstup umí přečíst, a jediné, čím `RequiredContent` říká rozhraní, co po uživateli chtít.

Pro dotazovou větev to stačilo, dokud byl dotazový parser jeden. Rozhodnutí [022](022-native-query-syntax-in-builders.md) uložilo doplnit matici na devět směrů, a tím vstupují do hry dotazy, které nejsou C#:

- **Dapper** dotaz je T-SQL. V praxi bývá uvnitř `connection.Query<T>("…")`, tedy uvnitř C#, ale samotný dotaz je SQL a uživatel ho typicky má jako SQL.
- **NHibernate** má dva dotazovací jazyky, LINQ a HQL, a rozhodnutí 022 uložilo generovat HQL. HQL je řetězec, ne C#.

Otázka zní, podle čeho se pozná, v jakém jazyce je dotaz na vstupu napsaný.

## Zvažované varianty

### 1 — Ponechat `CSharpQuery` a rozlišovat uvnitř parserů

Dotaz Dapperu i dotaz NHibernate by přišel jako C# a parser daného frameworku by si z něj vytáhl, co potřebuje. Vypadá to jako varianta bez nákladů: žádná změna modelu, žádná změna drátu, žádná změna frontendu.

Zamítáme, a rozhoduje o tom NHibernate. Jakmile vedle LINQ parseru vznikne i HQL parser, budou oba tvrdit, že umějí `CSharpQuery`, a `ConversionHandler` si dnes bere `queryParsers.First()` — o výsledku by tedy rozhodlo pořadí v seznamu, který vrací `ParserFactory`. To je přesně ta tichá závislost na pořadí řádků, kterou `open-items.md` už jednou vytkla u priority zdrojů, a odstranit ji jde jedině hádáním z obsahu, což je nedeterminismus zakázaný S2. Vedle toho by uživatel Dapperu musel svůj SQL obalit do C#, aby ho nástroj vzal — umělá překážka proti S7 a proti F14, kde se má nahrávat, co uživatel má.

### 2 — Přidat na `ConversionSource` samostatné pole pro jazyk

Typ obsahu by zůstal a vedle něj by přibyl dialekt. Zamítáme: `IParser.CanParse` bere jen typ obsahu, takže nový rozlišovač by byl neviditelný právě tam, kde se o parseru rozhoduje. Buď by se musel měnit i `IParser`, nebo by pole bylo dekorativní.

### 3 — Rozšířit typ obsahu o hodnoty pojmenované podle jazyka

## Rozhodnutí

**Volíme variantu 3. `ConversionContentType` dostává `SqlQuery` a `HqlQuery`. Hodnoty pojmenovávají jazyk, ne framework.**

**Jazyk, ne framework.** Je to rozdíl, na kterém stojí udržitelnost výčtu. Hodnot podle frameworku by přibývala jedna s každým wrapperem; hodnot podle jazyka přibývá jedna s každým *jazykem*, a těch je málo a sdílejí se — javový ekosystém přidá `JavaEntity` a `Jpql`, přičemž `Jpql` obslouží Hibernate i EclipseLink. Výčet tedy neroste s S1, roste s ekosystémy, a to je růst, který je vyslovitelný a konečný.

**Přidání je aditivní.** Dosavadní hodnoty ani jejich čísla se nemění, takže starý klient nikdy neposlal to, co nezná, a odpověď v dosavadním tvaru zůstává platná.

**Cíl vydává i holý dotaz, ne jen obalující C#.** Kde je nativní dotazový tvar řetězec, generuje builder dva artefakty: spustitelnou C# metodu (`CSharpQuery`) a týž dotaz samostatně (`SqlQuery` u Dapperu, `HqlQuery` u NHibernate). EF Core vydává jen `CSharpQuery`, protože jeho dotaz *je* ten C#. Není to zdvojení výstupu ve smyslu varianty 2 rozhodnutí 022 — obojí je tentýž dotaz v témž jazyce, jednou zabalený a jednou ne. Získáváme tím troje: převod Dapper → Dapper se uzavírá jako round-trip přes holý SQL, ověření dostává artefakt, který nemusí z C# nic vydolovat, a **`AdvisorBenchmarking` může zahodit regulární výraz, kterým dnes SQL z generované metody vytahuje zpátky**.

**Prázdný vstup není fakt.** Rozhraní posílá i nevyplněné jednotky; dotazový zdroj složený jen z bílých znaků se proto přeskočí bez záznamu. Záznam `Failure` patří neprázdnému textu, který nikdo neumí přečíst. Řeší se to na serveru, protože tam se to dá vyřešit jednou pro všechny klienty.

## Důsledky

**`ConversionHandler` má tři místa, kde je `CSharpQuery` natvrdo** — výběr entitních parserů negací, výběr dotazových zdrojů a výběr dotazových parserů. Všechna tři se musí ptát „je to dotaz?", jinak by nový typ obsahu propadl entitní větví a nikdy se nepřeložil. Rozpoznání dotazového typu obsahu dostává jedno místo v modelu.

**Výběr dotazového parseru přestává být `First()`** a stává se výběrem podle typu obsahu vstupu. Tím zaniká závislost na pořadí v `ParserFactory`, kvůli které jsme zamítli variantu 1.

**`RequiredContent` a ukázky.** Dapper deklaruje `SqlQuery`, NHibernate `CSharpQuery` a EF Core `CSharpQuery`; ke každé nové jednotce patří ukázka, aby šlo rozhraní vyzkoušet bez psaní vstupu. Při té příležitosti se rovná i dosavadní nesoulad, kdy tři různá čísla ukázek ukazují na tentýž dotaz EF Core a jedno z nich nemá v `RequiredContent` protějšek.

**Frontend a `wwwroot`.** Zrcadlo výčtu v Angularu se doplňuje o obě hodnoty a popisek pro ně. Protože `wwwroot` je commitnutý build, musí se po změně přeložit — jinak nasazený bundle nezná typ, který API vrací.

**HQL parser tohle rozhodnutí nezavádí.** `HqlQuery` je hodnota, kterou zatím jen produkujeme; číst ji zpět bude umět až parser, jehož existence je otázka mimo tohle rozhodnutí. Do té chvíle je `HqlQuery` na vstupu neznámý jazyk a končí záznamem, ne mlčením.

**Testy.** Že Dapper přijme dotaz jako `SqlQuery` i jako `CSharpQuery` a dá týž výsledek; že dotazový zdroj v jazyce, pro který zdrojový framework nemá parser, vydá `Failure`; a že prázdný dotazový zdroj neprodukuje ani artefakt, ani záznam.
