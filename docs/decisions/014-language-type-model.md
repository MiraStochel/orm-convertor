# 014 — Jazykový typový model

Datum: 2026-08-14
Stav: platí
Požadavky: F1, F2, F3, F7–F10, F11
Podklad: JSS §5.2 a Figure 5; Hibernate ORM 7.4.5 (`Dialect.java`, `SQLServerDialect.java`); EF Core 10.0.10 (`SqlServerTypeMappingSource.cs`); NHibernate 5.7.0 (`MsSql2000Dialect.cs`); audit 2026-08-02, kap. 2.2–2.4 a 4.5

## Kontext

Typ vlastnosti dnes nese `CLRTypeModel` s výčtem `CLRType` o dvanácti hodnotách a s řetězcovým `GenericParam`. Proti modelu z Figure 5 se to rozchází v pěti věcech: jméno i obsah jsou ekosystémové, ačkoli §5.2 výslovně žádá deskriptor „rather than CLR- or JVM-specific types"; chybí referenční kategorie, kterou tentýž odstavec uvádí jako jednu ze tří a zdůvodňuje ji právě rozpoznáváním navigačních vlastností; smyčka `LangType` sama na sebe je přetržená, protože prvek kolekce je řetězec bez vlastní kategorie i nullability; nullabilita je jen na databázové straně, takže pravidlo E4 nemá co porovnávat; a neznámý typ končí výjimkou, což je v rozporu s F11.

Nejsou to teoretické výhrady, každá z nich dnes něco blokuje:

- `CLRTypeConvertor.FromString` na názvu entity vyhodí `NotSupportedException` už v `AddProperty`, takže **navigační vlastnost odkazující na jinou entitu neprojde parserem**. Vztahy N:1 a 1:1 proto z C# zdroje vůbec nevzniknou a z XML sice vzniknou, ale generování C# na nich spadne.
- Táž výjimka padá na `Guid`, takže klíč typu `Guid` neprojde, ačkoli parser pro něj strategii `Uuid` odvodit umí (rozhodnutí [011](011-key-generation-strategy-vocabulary.md)).
- Entita ve formě `Embedded` nese vlastnost typu klíčové třídy, tedy opět neznámý typ, a neprojde ani ta.
- Kolekce jsou všechny `CLRType.List`, takže se v modelu ztrácí rozdíl mezi `<set>` a `<bag>`.

Druhá věc, kterou kontext musí říct, je **kde bydlí převod jazykového typu na databázový**. Dnes je v `Common`, tedy jako by byl obecný. Ověřili jsme, že není:

| Framework | Co udělá s prostým řetězcem na SQL Serveru | Zdroj |
|---|---|---|
| Hibernate 7.4.5 | `varchar` | `Dialect.java`: `case VARCHAR -> "varchar($l)"`; `SQLServerDialect.java` drží `nvarchar` jen pro `NVARCHAR`/`NCLOB` |
| EF Core 10.0.10 | `nvarchar` | `SqlServerTypeMappingSource.cs`: pro `typeof(string)` je `isAnsi = mappingInfo.IsUnicode == false`, unicode je výchozí |
| NHibernate 5.7.0 | `nvarchar` | `MsSql2000Dialect.cs`: `DbType.String` → `NVARCHAR`, `DbType.AnsiString` → `VARCHAR` |

Tři frameworky, jedna databáze, dva různé sloupcové typy — a není to rozdíl dialektů, protože všechny tři míří na týž SQL Server 2022. Je to rozdíl výchozího předpokladu frameworku o národních znacích. Sdílený převod v `Common` tedy nese tvrzení platné jen pro .NET.

## Zvažované varianty

1. **Doplnit chybějící skaláry a jinak nechat být.** Levné a odblokuje `Guid`, ale ne referenční navigace — a ty blokují N:1, 1:1, klíčovou třídu i celý javový směr. Řešilo by nejmenší z pěti problémů.

2. **Nést typ jako řetězec** přesně tak, jak ho napsal zdroj, a interpretaci nechat na builderech. Otevřené a nic neztratí, ale deskriptor (rozhodnutí [009](009-target-framework-descriptor.md)) pak nemá nad čím tvrdit, co cíl umí vyjádřit, a každý builder musí rozumět názvosloví každého zdroje — tedy párové převody, kterým se parser-builder architektura vyhýbá (S1, JSS §4.3).

3. **Tři kategorie podle §5.2 s rekurzivním typem prvku a s únikem pro nerozpoznané jméno.**

4. **Neutralizovat obě strany naráz**, tedy i `DatabaseType`. Nejúplnější, ale míchá dvě různě podložené věci: jazyková strana je doložená článkem i konkrétními pády, kdežto databázová má dnes jediný cíl, takže by se rozhodovalo bez důkazů o tom, co je vlastně potřeba.

## Rozhodnutí

Volíme variantu 3. **`LangType` nahrazuje `CLRTypeModel` i `CLRType`** a rozlišuje čtyři kategorie:

| Kategorie | Co nese | Příklad |
|---|---|---|
| `Scalar` | hodnotu výčtu `ScalarType` | `int`, `String`, `Guid` |
| `Reference` | název cílové entity | `Customer` |
| `Collection` | typ prvku jako **rekurzivní `LangType`** a druh kolekce | `List<Order>`, `Set<Order>` |
| `Unknown` | název typu tak, jak ho napsal zdroj | `OrderLineId`, `uint` |

K tomu `IsNullable` na jazykové straně vedle té databázové na `PropertyMap`, aby pravidlo E4 (`¬NullableLang ⇒ ¬NullableDB`) mělo co porovnávat. Neplatné kombinace nejsou zapsatelné: typ se zakládá továrními metodami `LangType.Scalar`, `LangType.Reference`, `LangType.Collection` a `LangType.Unknown`, ne inicializátorem.

**`ScalarType` je uzavřený výčet** — `Bool`, `Byte`, `Short`, `Int`, `Long`, `Float`, `Double`, `Decimal`, `Char`, `String`, `DateTime`, `Guid`, `Object`. Uzavřený proto, že jen nad konečnou množinou umí deskriptor říct, co cíl vyjádřit umí. Bezznaménkové celočíselné typy do něj **nepatří**: v Javě protějšek nemají, takže by je javový cíl stejně nevyjádřil, a jako `Unknown` se zachovají věrněji než převodem na širší typ.

**Druh kolekce** nese `CollectionKind` s hodnotami `Unspecified`, `List` a `Set`. Mapy zůstávají mimo rozsah, protože potřebují i typ klíče, a to je samostatná úvaha.

**`Reference` vzniká, když to zdroj tvrdí**, tedy když vlastnost označí mapování nebo anotace (`<many-to-one>`, `<one-to-one>`, kolekce s parametrem, navigační konvence EF Core), nebo když se název typu rozresolvuje proti entitám téhož převodu. Jinak vzniká `Unknown` se zachovaným názvem. Druhá polovina pravidla se opírá o rozresolvování jmen entit, které je zatím otevřená položka; do té doby platí jen první.

**`Unknown` se nikdy neprojeví výjimkou.** Parser ho zaznamená, builder ho **vypíše jménem ze zdroje** a diagnostika ho ohlásí jako fakt, který cíl nemusí přijmout. Vypsání jménem není totéž co použití `SourceStrategyName` u generátorů, kde existuje kanonická alternativa: tady žádná není, a mlčení by znamenalo neúplný artefakt místo neúplného tvrzení.

**`Object` zůstává skalární hodnotou** a znamená, že zdroj napsal `object`. Je to jiné tvrzení než `Unknown`, které znamená, že zdroj napsal něco, co neumíme zařadit.

**Převod jazyk ↔ databáze se stěhuje z `Common` do wrapperů.** V `Common` zůstává jen převod mezi názvem typu v daném jazyce a `LangType`; tabulka, která z `String` dělá `nvarchar` nebo `varchar`, patří ke konkrétnímu frameworku, protože je to jeho výchozí předpoklad, ne vlastnost typu.

**Databázová strana se v tomto rozhodnutí nemění.** `DatabaseType` popisuje sloupec a náš sloupec je v obou ekosystémech na SQL Serveru 2022 (rozhodnutí [013](013-target-framework-versions.md)), takže slovník T-SQL zatím nic nelže. Její neutralizace zůstává otevřená a je to samostatná otázka s jinými důkazy.

## Důsledky

Migrace proběhne jednorázově bez přechodného období (rozhodnutí [003](003-one-shot-migration.md)) a dotkne se modelu, obou entitních parserů, obou builderů (`BuildPropertySignature`, `AppendPropertyToCode`, `ResolveNhType`), deskriptoru, `SampleData` i testů. Rozpadne se do čtyř kroků — model, konvertory, parsery, buildery — z nichž každý končí zeleným buildem.

Odblokuje čtyři věci, které dnes stojí: referenční navigace, a s nimi vztahy N:1 a 1:1 od parseru až po výstup; klíče typu `Guid`; formu `Embedded` u klíčové třídy; a rozlišení `<set>` od `<bag>`.

Pro javovou stranu tím vzniká to, co F7–F10 potřebují: tentýž výčet `ScalarType`, ale jiná tabulka na databázovou stranu. Rozdíl mezi `varchar` a `nvarchar` z kontextu je přesně ten případ, který by při sdíleném převodu tiše měnil schéma při každém překladu mezi ekosystémy.

Zůstává jedna asymetrie, kterou je fér přiznat: `Unknown` vypsaný jménem ze zdroje projde do cílového artefaktu i tam, kde jméno v cílovém jazyce neexistuje — `OrderLineId` v Javě, `uint` kdekoli mimo .NET. Výsledek se nepřeloží, ale to je viditelné selhání s diagnostikou, ne tichá ztráta; alternativou by bylo negenerovat vlastnost vůbec, což je horší.