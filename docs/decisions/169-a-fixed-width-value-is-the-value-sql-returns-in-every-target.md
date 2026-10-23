# 169 — Hodnota sloupce pevné šířky je hodnota, kterou vrátí SQL: cíl EclipseLink ji vrací konkatenací s prázdným textem a konvertorem doplnění, zdroj EclipseLink se čte bez koncových mezer

Datum: 2026-10-23
Stav: platí
Požadavky: F9, F10, F13, T2, T3, S1
Podklad: rozhodnutí [155](155-sql-sum-in-ef-core-linq-is-the-projection-of-one-group-and-ef-core-sum-is-coalesce.md) (vzor: hodnota SQL jako měřítko, „píše se i čte"), [157](157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md), [040](040-boundary-of-the-handed-over-artifact.md), [024](024-typed-query-operand.md), [028](028-assembly-name-is-not-ours-to-invent.md), [086](086-target-database-dialect-declared-by-the-descriptor.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md); [`subset.md`](../subset.md) 2.2 (ořez `CHAR` v EclipseLinku); běhy FIBEN z 2026-10-19 a 2026-10-22 (q63 v EclipseLinku); měření 2026-10-23 proti zafixovaným verzím — EclipseLink 5.0.0 a Hibernate 7.4.5 nad databází (javová sada, `eclipselink/CharTrimmingClaimsTest`), EF Core 10.0.10 přes `ToQueryString()` (`EFCore/EFCoreTrimEndProviderTest`), NHibernate 5.7.0 jeho vlastním plánem dotazu (`NHibernate/NHibernateLinqProviderTest`, `NHibernate/NHibernateHqlProviderTest`)

## Kontext

Smyslem nástroje je, aby vývojář mohl vyměnit celé ORM za jiné a jeho program se choval stejně: dotazy vrátí tytéž řádky a tytéž hodnoty ([`use-cases.md`](../use-cases.md)). U sloupce pevné šířky to dnes neplatí. SQL Server hodnotu `CHAR(n)` i `NCHAR(n)` doplní mezerami na šířku `n`, JDBC i ADO.NET ji tak vrátí a pět ze šesti frameworků ji tak předá programu. EclipseLink 5.0.0 ji ve výchozím nastavení relace (platforma `shouldTrimStrings`) ořeže. Změřeno nad tabulkou se sloupci `char(5)` a `nchar(5)`:

| Uloženo | SQL vrátí | Hibernate, JDBC | EclipseLink | `RTRIM` |
|---|---|---|---|---|
| `'YES'` | `YES··` | `YES··` | `YES` | `YES` |
| `'AB CD'` | `AB·CD` | `AB·CD` | `AB·CD` | `AB·CD` |
| `'A' + CHAR(9)` | `A\t···` | `A\t···` | `A` | `A\t` |
| `'A' + CHAR(160)` | `A\u00A0···` | `A\u00A0···` | `A\u00A0` | `A\u00A0` |
| `N'A' + NCHAR(12288)` | `A\u3000···` | `A\u3000···` | `A\u3000` | `A\u3000` |
| `NULL` | `NULL` | `null` | `null` | `NULL` |

(`·` je mezera.) Z měření plyne, co přesně EclipseLink dělá:

- **Řídí se typem výsledného sloupce, ne výrazem.** Ořeže každý sloupec výsledku typu `CHAR` nebo `NCHAR`: holý sloupec, `MIN` a `MAX` nad ním, `COALESCE`, `CASE` i `NULLIF` nad hodnotami pevné šířky, v JPQL i v nativním SQL, ve čtení entity i v projekci. Sloupec proměnné šířky ani text, který databáze spočítá konkatenací, neořeže.
- **Ořezává jinou množinu znaků než `RTRIM`.** Odřízne všechny koncové znaky do U+0020 včetně, tedy i tabulátor a řídicí znaky. Nezlomitelnou mezeru a ideografickou mezeru nechá. `RTRIM` SQL Serveru odřízne jen mezery.
- **Řádky nemění.** Ořez se děje až po vyhodnocení dotazu. Podmínky, joiny, seskupení i řazení počítá databáze nad doplněnými hodnotami a porovnání v SQL Serveru koncové mezery přehlíží.

Rozdíl tedy stojí jen v hodnotách, zato v obou směrech. Program psaný proti Hibernatu, který hodnotu exportuje v pevné šířce nebo volá `substring(0, 5)`, dostane po převodu do EclipseLinku kratší řetězec. Program psaný proti EclipseLinku, který testuje `"YES".equals(…)`, po převodu kamkoli jinam test potichu nesplní. [`subset.md`](../subset.md) 2.2 to dosud vedlo jako vyslovenou mez a rozhodnutí 157 kvůli ní dovolilo soudci FIBEN vzniknout i s neshodou q63 v EclipseLinku. Je to ale rozdíl překladu, ne vlastnost nepřeložitelné konstrukce. Rozhodnutí 155 řešilo `Sum` EF Core, který nad prázdnou množinou dává jinou hodnotu než `SUM` jazyka SQL, a ukázalo, že takový rozdíl patří vyrovnat na hranici frameworku, který ho vnáší.

## Zvažované varianty

1. **Ponechat vyslovenou mez** (dnešní stav). Typ `CHAR(n)` je běžný ve starších schématech: kódy, příznaky, klíče pevné šířky. Rozdíl mění logiku programu v obou směrech a čtvrtý stupeň ověření (089) by ho musel donekonečna vykazovat jako známý. Zamítáme ze stejného důvodu jako variantu 1 rozhodnutí 155.

2. **Vypnout ořez v relaci.** Vlastnost persistence unit `eclipselink.target-database-properties = shouldTrimStrings=false` vrátí doplnění všude, v JPQL, v nativním SQL i při čtení entity (změřeno). Je to přesné a úplné řešení, jenže persistence unit je soubor konzumentského projektu a artefakt ho nevydává (rozh. 040). Nástroj by tak nemohl zaručit, že nastavení platí, a zdroj EclipseLink by nevyřešil vůbec: tam je ořez vlastností programu, který se překládá. Jako mechanismus zamítáme. Zůstává jako rada v záznamu, protože s ní se konvertor níže stane prázdnou operací a výsledek je přesný i v okrajových případech.

3. **Ořezat hodnoty v kanonickém tvaru čtvrtého stupně.** Zamítlo už rozhodnutí 157: `YES` a `YES··` jsou pro program různé řetězce. Ověření by přestalo vidět rozdíl, který program vidí.

4. **Za měřítko vzít ořezanou hodnotu.** Pět frameworků i samotné SQL by se musely přizpůsobit jednomu nastavení jedné relace a každý cíl kromě EclipseLinku by musel ořezávat každý dotaz. Zamítáme.

5. **Měřítkem je hodnota SQL a vyrovnání nese hranice EclipseLinku v obou směrech** — týž tvar jako rozhodnutí 155. Uvnitř varianty bylo potřeba zvolit pravopis „hodnoty, kterou SQL vrátí, ale ne jako `CHAR`":
   - `cast(x as varchar(n))` je rozšíření JPQL EclipseLinku (změřeno) a záměr pojmenuje. Mezireprezentace ale v dotazech nese typy modelu, nikdy databázové typy (rozh. 024, `QueryFunction.Cast`). Délka `n` a rozdíl `varchar`/`nvarchar` by do ní musely vstoupit jen kvůli jednomu cíli. Standardní `cast(x as String)` EclipseLink na SQL Serveru pošle jako `CAST(x AS String)` a databáze ho odmítne (změřeno).
   - **`x || ''`, konkatenace s prázdným textem**, je výraz, který mezireprezentace už nese (rozh. 107). Jeho hodnota v SQL je hodnota sloupce, `NULL` zůstává `NULL` a jeho typ je text proměnné šířky. EclipseLink ho pošle jako `(code + ?)` a doplnění vrátí (změřeno, i nad `MAX`, se seskupením podle téhož sloupce, pod `DISTINCT`, ve členu `UNION` i nad atributem s konvertorem). V nativním SQL je to `code + ''`. Každá čtečka ho čte zpět jako tentýž výraz a každý cíl ho vrátí doplněný, takže zpětný překlad je správný bez zvláštního pravidla. Volíme tento pravopis. Jeho cenou je, že záměr z textu dotazu nevyčteš; říká ho záznam.

   Pro čtení entity se nabízel `@ColumnTransformer(read = "rtrim(…)")` Hibernatu. Funguje (změřeno), ale přepíše i podmínky na `where rtrim(code) = ?`. Databáze pak vyhodnocuje jiný predikát, než napsal zdroj, a nepoužije index, proto ho zamítáme. Konvertor JPA (`AttributeConverter`) SQL nemění a působí na čtení entity, na projekci cesty i na `MAX` nad ní (změřeno v obou implementacích).

## Rozhodnutí

**Volíme variantu 5. Hodnotou čtení sloupce pevné šířky je v mezireprezentaci hodnota, kterou vrátí SQL, tedy hodnota doplněná mezerami. Cíl, jehož framework ji ořezává, ji píše tak, aby ji vrátil doplněnou. Zdroj, jehož framework ji ořezával, se čte jako hodnota bez koncových mezer a každý jiný cíl ji tak vrátí.**

- **Fakt zdroje.** `PropertyMap.TrimmedOnRead` říká, že program zdroje dostává hodnoty sloupce bez koncových znaků do U+0020, pokud je sloupec pevné šířky. Fakt je jen pozitivní. Čtečka EclipseLinku ho nastaví každé vlastnosti typu `String`, protože to dělá výchozí relace. Čtečka relaci nevidí (040) a předpokládá výchozí nastavení. Zda je sloupec pevné šířky (`DatabaseType.Char` s facetou `IsUnicode`), rozhoduje až builder, protože typ často dodá katalog (rozh. 015) teprve po čtení.
- **Fakt cíle.** `TargetFrameworkDescriptor.TrimsFixedWidthOnRead` je pravda u EclipseLinku a nepravda u ostatních pěti (S1: sedmý framework fakt vysloví ve svém deskriptoru).
- **Slovník.** Přibývá funkce `TrimEnd`, hodnota bez koncových mezer: T-SQL `RTRIM(x)`, JPQL i HQL `trim(trailing from x)`, LINQ `x.TrimEnd()`. Všechny čtyři pravopisy jsou změřené: EclipseLink a Hibernate z JPQL píší `RTRIM`/`rtrim`, NHibernate z HQL `rtrim`, EF Core a NHibernate z LINQ `RTRIM`/`rtrim`. NHibernate odmítá `rtrim(x)` v HQL a EF Core `TrimEnd(' ')` nepřeloží, proto se píše bez argumentu. Čtečky čtou `RTRIM(x)` s jedním argumentem, `trim(trailing from x)` a `TrimEnd()` bez argumentu zpět jako `TrimEnd`.
- **Projekce.** Šablona dotazového builderu (rozh. 023) přepíše projekce nejvnějšího seznamu výběru a členů množinové operace na nejvyšší úrovni, tedy to, co program dostane. Nedělá to v poddotazech, v definicích mezivýsledků, v podmínkách ani v klíčích:
  - **cíl ořezává a vlastnost `TrimmedOnRead` nenese:** holý sloupec pevné šířky, `MIN` či `MAX` nad ním a `COALESCE` či `CASE`, jehož hodnotou takový sloupec je, se píše jako `x || ''`;
  - **cíl neořezává a vlastnost `TrimmedOnRead` nese:** holý sloupec pevné šířky a `MIN` či `MAX` nad ním se píše jako `TrimEnd(x)`;
  - alias projekce zůstává. Holý sloupec bez aliasu dostane jméno, pod kterým ho cíl vracel: sloupec v SQL, vlastnost v JPQL, HQL a LINQ. Není to vymyšlené jméno (028), je to jméno, které projekce měla. Agregát bez aliasu zůstává bez jména jako dřív;
  - pod `DISTINCT`, kde se řadí podle téže projekce, se nepřepisuje a vzniká `Loss`: SQL Server páruje klíč řazení se seznamem výběru podle textu a odmítne holý sloupec vedle konkatenace i konkatenaci, jejíž prázdný text EclipseLink na každém místě váže jako jiný parametr, v JPQL i v nativním SQL (změřeno).
- **Entita.** Builder JPA píše u sloupce pevné šířky s délkou bez doslovného typu zdroje `columnDefinition = "char(n)"`, v Hibernatu u unicode sloupce `"nchar(n)"` (rozh. 086; EclipseLink unicode píše už nationalizací). Artefakt tak fakt pevné šířky nese a jeho čtení ho najde i bez katalogu. Dále:
  - **EclipseLink** u sloupce pevné šířky s délkou bez `TrimmedOnRead` přidá `@Convert(converter = <Entita>.FixedWidth<n>.class)` a vnořenou třídu `FixedWidth<n>`, která hodnotu doplní mezerami zpět na `n`. Konzument ji uvádí v persistence unit vedle entity, jinak ji EclipseLink nenajde (změřeno);
  - **Hibernate** u vlastnosti s `TrimmedOnRead` přidá `@Convert(converter = <Entita>.TrimmedFixedWidth.class)` a vnořenou třídu, která odřízne koncové znaky do U+0020 přesně jako EclipseLink. Hibernate ji najde bez registrace (změřeno);
  - **EF Core, NHibernate, Dapper a MyBatis** u vlastnosti s `TrimmedOnRead` nad sloupcem pevné šířky vydají `Loss`: jejich artefakt nemá kudy ořez při čtení entity vyslovit. EF Core 10.0.10 nemá datovou anotaci konverze hodnoty (změřeno: jen `[EntityTypeConfiguration]` odkazující na konfigurační třídu), sloupec NHibernate 5.7.0 nemá čtecí výraz (schéma `nhibernate-mapping`), Dapper a MyBatis čtou celý řádek bez místa pro funkci. Konfigurační třída, uživatelský typ a obsluha typu by byly nové druhy artefaktu; toto rozhodnutí je nezavádí.
- **Záznamy.** Každý přepis projekce a každý konvertor je `Convention` s odkazem na toto rozhodnutí. Záznam konvertoru doplnění jmenuje i meze níže a přepínač relace z varianty 2.

## Důsledky

- **Meze, které zůstávají** (vede je [`subset.md`](../subset.md) 2.2):
  1. sloupec, o kterém nástroj neví, že má pevnou šířku: chybí katalog a zdroj typ nevysloví. Dapper ho nevysloví nikdy, takže bez katalogu se nic nemění, v obou směrech;
  2. konvertor doplnění vrací jen mezery. Hodnota, která před doplněním končí znakem do U+0020 (tabulátor, řídicí znak), se při čtení entity v EclipseLinku vrátí s mezerami místo něj. Projekce přes `x || ''` je přesná;
  3. `CHAR(n)` v kolaci s vícebajtovou kódovou stránkou (`_UTF8`, dvoubajtové kódové stránky): šířka je v bajtech, kdežto konvertor doplňuje znaky (plyne z pravidla, neměřeno). `NCHAR(n)` je přesný, protože jednotka UTF-16 v Javě je jednotka `NCHAR`;
  4. sloupec pevné šířky bez délky nemá konvertor a vzniká `Loss`;
  5. hodnota pevné šířky ze skalárního poddotazu v projekci a ze sloupce mezivýsledku se nepřepisuje;
  6. ze zdroje EclipseLink se `COALESCE` a `CASE` nad hodnotami pevné šířky neořezávají, ačkoli je EclipseLink ořezal. Nástroj neodvozuje, zda by výsledným typem byl `CHAR`;
  7. ze zdroje EclipseLink `RTRIM` v projekcích odřízne jen mezery, EclipseLink i řídicí znaky do U+0020. Konvertor Hibernatu je přesný;
  8. ze zdroje EclipseLink se při čtení entity v cílech EF Core, NHibernate, Dapper a MyBatis vrací hodnota doplněná (`Loss`);
  9. zdroj EclipseLink s vypnutým ořezem v relaci: nástroj předpokládá výchozí nastavení;
  10. klíčový sloupec pevné šířky: Jakarta Persistence na identifikátor konvertor nepoužije, takže ho EclipseLink vrátí ořezaný a Hibernate ze zdroje EclipseLink doplněný (`Loss`);
  11. platí pro deklarovaný dialekt SQL Server 2022 (rozh. 086) a jeho výchozí nastavení `CONCAT_NULL_YIELDS_NULL ON`. S vypnutým `CONCAT_NULL_YIELDS_NULL` by `x + ''` z `NULL` udělalo prázdný text. Jiný dialekt doplnění, konkatenaci i ořez pravopisně i významově mění (PostgreSQL podle své dokumentace převádí `char(n)` na text bez koncových mezer), takže by se všechno měřilo znovu.
- **Kód.** `Model`: `PropertyMap.TrimmedOnRead`, `QueryFunction.TrimEnd`. `AbstractWrappers`: `TargetFrameworkDescriptor.TrimsFixedWidthOnRead`, přepis projekcí v šabloně, `Loss` u vlastnosti, kterou entitní artefakt cíle neořeže. Pravopis a čtení `TrimEnd` v `TransactSql`, `JakartaPersistence`, `LinqBuilding`, `LinqParsing` a v HQL NHibernatu. Čtečka EclipseLinku nastaví fakt. `AbstractJpaEntityBuilder` píše doslovný typ pevné šířky a oba konvertory podle profilu.
- **Testy.** Fakta frameworků drží `eclipselink/CharTrimmingClaimsTest` (co EclipseLink ořezává a co ne, konkatenace, konvertor doplnění a jeho místo v persistence unit, `trim(trailing from …)`, přepínač relace), `hibernate/TrimmedFixedWidthClaimsTest` (konvertor ořezu, podmínky beze změny, `@ColumnTransformer`) a provider testy EF Core a NHibernate vedle háčků, které na nich stojí. Šablonu a buildery drží xUnit (`Combined/FixedWidthReadTest`). Čtvrtý stupeň nad FIBEN q63 v EclipseLinku.
- **FIBEN.** q63 se v EclipseLinku má shodovat se zdrojem a záznam běhu už neuvádí žádnou výjimku. Pravidlo rozhodnutí 157 platí dál pro každou jinou vyslovenou mez.
