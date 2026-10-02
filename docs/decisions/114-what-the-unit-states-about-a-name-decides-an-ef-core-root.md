# 114 — Člen jména je kořenem dotazu EF Core, jen když jednotka neuvádí opak: navigace načteného řádku dotazem není a mapování převodu smí místo jen odmítnout

Datum: 2026-10-02
Stav: platí
Požadavky: F7–F10, F11, F14, S1, S2
Podklad: rozhodnutí [026](026-home-of-shared-query-reading.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [083](083-parameter-as-the-fifth-operand-shape.md), [101](101-a-join-along-an-association-path-is-derived-from-the-relation.md), [109](109-a-code-unit-carries-every-query-it-hands-over.md) a [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md); sonda přes `ConversionHandler.Convert` z 2026-10-02; dokumentace EF Core, *Navigations* a *How Queries Work*; dokumentace .NET ke třídám `System.Linq.Queryable` a `System.Linq.Enumerable`

## Kontext

**EF Core poznává kořen dotazu podle místa a místo neříká, co je za jménem.** Sdílené čtení LINQ bere za kořen EF Core člen čehokoli zapsaného jménem — `ctx.Customers` je `DbSet` na čemkoli, co je `ctx` (rozhodnutí 026) —, protože nástroj čte bez referencí a typ jména nezná. Dokud jednotka nesla jen dotazový fragment, bylo jméno na začátku řetězu vždy kontext. Rozhodnutí 109 začalo číst každý řetěz jednotky a vyňalo z místa člen proměnné prvku — parametru lambdy a proměnné `foreach` —, protože prochází řádky, které program už načetl. Rozhodnutí 111 udělalo z jednotky celý soubor a vyňalo člen vlastní instance (`Lines`, `this.Lines`) všude mimo kontext. Parametr metody, lokální proměnná a pole, které drží načtenou entitu, vyňaté nejsou.

**Sonda z 2026-10-02 ukázala, co z toho vychází.** Soubor s entitami `SalesOrder` (s vlastností `List<OrderLine> Lines`) a `OrderLine` a se třídou

```csharp
public class Totals
{
    public List<OrderLine> Big(SalesOrder order) => order.Lines.Where(l => l.Amount > 100).ToList();
}
```

vydá do Dapperu dotaz `SELECT * FROM Lines AS l WHERE l.Amount > 100` nad tabulkou, která v převodu není, se záznamem jen o tom, že typ výsledku `Line` je odvozený z názvu tabulky. Je to dotaz, který zdroj neklade vůbec — krajní případ toho, co rozhodnutí 053 a 070 zakazují, totiž vydat dotaz, který vrací jiné řádky než zdroj. Třída `Totals` přitom podle výjimky 1 rozhodnutí 111 přestane být entitou, protože v ní leží „místo předání". Totéž dělá `order.Lines` nad proměnnou `var order = ctx.SalesOrders.First(…)`: ze služby, která objednávku načte a pak projde její řádky, vyjdou dva dotazy, z nichž druhý je vymyšlený. Sonda ukázala ještě jedno tiché místo téhož druhu, z opačné strany: `this._ctx.Customers.Where(…)` se nečte vůbec, takže dotaz zmizí beze slova a třída kolem vyjde jako prázdná entita — místo kořen poznává jen nad holým jménem. NHibernate tím netrpí, jeho kořenem je volání `Query<T>()`.

**Co je dotaz, neříká v C# místo, ale typ.** `x.M.Where(f)` je volání rozšiřující metody a překladač ho naváže podle statického typu `x.M`: je-li to `IQueryable<T>`, zvolí `Queryable.Where`, která přidá krok do stromu výrazu pro poskytovatele, jinak `Enumerable.Where`, která spustí delegát nad objekty v paměti (dokumentace .NET ke třídám `Queryable` a `Enumerable`). Kořen EF Core je `DbSet<T>`, který `IQueryable<T>` implementuje; kolekční navigace entity je podle dokumentace EF Core *Navigations* `ICollection<T>`, `List<T>` nebo `HashSet<T>`, tedy sekvence v paměti, a řetěz nad ní v kódu mimo dotaz EF Core nepřekládá — překládá jen strom, který dostane (*How Queries Work*). Typ členu tedy není znak, podle kterého by se dotaz hádal; je to přesně ten fakt, na který se otázka ptá. Nástroj ho nezná celý, protože čte bez referencí (S1), ale zná, co uvádí jednotka: od rozhodnutí 109 si čtení LINQ staví nad jednotkou sémantický model Roslynu, a ten jméno naváže na deklaraci a uvede jeho deklarovaný typ, i když ten typ jednotka sama nedeklaruje.

Rozhodnout je třeba, jestli příjemce, jehož typ je entitou, dělá z kořene navigaci; co platí pro příjemce typu, který jednotka nedeklaruje; a co pro proměnnou `var` naplněnou jiným voláním. Odpověď musí platit pro oba průchody: entitní průchod se podle rozhodnutí 111 ptá na předání týmž hledáním jako dotazový, a co jeden přečte jako dotaz, nesmí druhý přečíst jako entitu.

## Zvažované varianty

### 1 — Ponechat místo a jen vyslovit mez

Kořen by se dál poznával podle místa a mez by šla do katalogu podmnožiny. Zamítáme. Nejde o konstrukci, kterou nástroj nepřečte, ale o dotaz, který si vymyslí — artefakt nad tabulkou pojmenovanou po navigaci, bez záznamu, který by na to upozornil. Rozhodnutí 053 a 070 nedovolují vydat dotaz, který vrací jiné řádky, než klade zdroj, a F11 zakazuje nepodporovanou konstrukci potichu vynechat; vydat místo ní dotaz, který zdroj neklade, je horší než obojí. A od rozhodnutí 111 to stojí ještě třídu: entita s metodou, která projde řádky jiné entity, přestane být entitou.

### 2 — Kořenem je jen to, co jednotka ukáže jako kontext

Kladné pravidlo: člen jména je kořenem jen tehdy, když jednotka deklaruje typ jména jako kontext — třídu s bází `DbContext` nebo s vlastností `DbSet<T>` (rozhodnutí 111) — a člen jako jeho `DbSet`. Proti vymyšleným dotazům bezpečné a oběma průchodům společné. Zamítáme, protože by vzalo překlad nejčastějšímu vstupu. Kontext stojí obvykle ve vlastním souboru, takže služba s parametrem `ShopContext ctx` jeho deklaraci nenese, a dotazový fragment — tvar, jaký píšou dotazové buildery nástroje a jaký nesou všechny matice, vzorky i katalog LDBC — nedeklaruje `ctx` vůbec. Rozhodnutí 111 slíbilo, že žádná dnes platná jednotka platnou být nepřestane; tahle varianta by ten slib porušila u téměř všech dotazových jednotek EF Core.

### 3 — Rozhoduje mapování převodu

Dotazový průchod dostává mapy entit celého převodu a podle nich by se rozhodlo: je-li typ příjemce entitou převodu, je člen navigací. Pokrylo by to i entitu z jiného souboru, což zvolená varianta neumí. Zamítáme ze dvou důvodů.

**Co je v jednotce dotaz, by záviselo na sousedech.** Řetěz `order.Lines.Where(…)` by byl dotazem, dokud uživatel nepřidá do převodu soubor `SalesOrder.cs`, a pak by jím přestal být. Je to vada, pro kterou rozhodnutí 111 zamítlo kladné pravidlo entity („přidání dotazu do převodu by změnilo, které třídy jsou entitami") a 109 jméno dotazu podle obalující metody (jméno by záviselo na sousedovi): role a dotazy jednotky jsou výpovědí jednotky, ne převodu.

**Rozbilo by to souměrnost obou průchodů.** Entitní průchod čte jednotky jednu po druhé dřív, než mapování převodu existuje, takže se na ně ptát nemůže — nanejvýš na mapy z jednotek, které četl před ní, a pak by výsledek závisel na pořadí jednotek v požadavku; S2 by platilo jen v tom slabém smyslu, že totéž pořadí dá totéž. Bez map by entitní průchod třídu kolem řetězu vyloučil jako kód, který dotazy předává, dotazový průchod by v ní žádný dotaz nenašel a výstup by nesl záznam o předání, ze kterého nic nevyšlo — přesně ten rozchod dvou rozpoznávačů, kvůli kterému 111 trvá na jednom.

### 4 — Rozhoduje typ, který uvádí jednotka; kde ho neuvádí, platí místo, a mapování převodu smí místo jen odmítnout

## Rozhodnutí

**Volíme variantu 4. Kořen, který je členem jména — `x.M`, s `x` zapsaným holým jménem nebo přes `this` —, je kořenem dotazu EF Core jen tehdy, když jednotka neuvádí opak. Opak uvádí, drží-li jméno to, co program už načetl, nebo deklaruje-li jednotka typ jména i jeho člen `M` a člen jiným typem než `DbSet<T>`. Kde jednotka neuvádí nic, platí místo jako dosud. Mapování převodu o tom, co je dotaz, nerozhoduje; smí jen odmítnout místo, jehož příjemce má podle jednotky typ, který převod mapuje jako entitu.**

**Pořadí otázek.** O kořeni `x.M` rozhoduje první odpověď z této řady:

1. **`x` je proměnná prvku** — parametr lambdy nebo proměnná `foreach` —, a `M` je proto navigace (rozhodnutí 109, beze změny).
2. **`x` je lokální proměnná přiřazená jedinkrát a její hodnota končí krokem, který dotaz vykoná** — materializací, jedním řádkem, agregací (seznam rozhodnutí 109) nebo `Find`, kterým EF Core načte jednu entitu podle klíče —, ať s `await`, nebo bez něj. Pak `x` drží objekty v paměti a `M` je navigace. Pole se tu neptáme: přiřazuje se v konstruktoru i jinde, často přes `this`, a hodnotu, kterou drží v okamžiku řetězu, text neurčuje tak jednoznačně jako u lokální proměnné; pole s typem odpovídá otázka 3. Rozhodnutí 109 to řeklo o proměnné samé („po vykonání drží proměnná řádky, ne dotaz"); tady se totéž říká o jejích členech. Zdroj hodnoty přitom nerozhoduje: `orders.First()` nad seznamem drží objekt v paměti stejně jako `ctx.Orders.First()`.
3. **Jednotka deklaruje typ `x` i jeho člen `M`.** Typ `x` je ten, který uvádí deklarace jména — parametru, lokální proměnné, pole nebo vlastnosti —, u `var` ten, který sémantický model odvodí z inicializátoru (`new T(…)`, volání metody, kterou jednotka deklaruje). Člen se hledá v typu i v jeho bázových třídách, pokud je jednotka deklaruje. Rozhoduje deklarovaný typ členu, a to tak, jak by rozhodl překladač:
   - `DbSet<T>` — kořen dotazu, jako dosud;
   - `IQueryable<T>` nebo `IOrderedQueryable<T>` — dotaz, ale složený přes člen: poskytovatel dostane strom, který začíná tím, co člen vrací, a čtení člen nesleduje. Místo se odmítne záznamem `Failure`, který člen jmenuje, stejně jako proměnná, jejíž hodnotu text nefixuje (109) — jinak by z něj vyšel dotaz nad tabulkou pojmenovanou po členu;
   - cokoli jiného — navigace nad objekty v paměti, jako `this.Lines` v rozhodnutí 111.
4. **Jinak platí místo** (rozhodnutí 026): `x` je jméno, které jednotka nedeklaruje (`ctx` fragmentu), jméno typu, který jednotka uvádí, ale nedeklaruje (`ShopContext ctx` s kontextem v jiném souboru, `var ctx = factory.CreateDbContext()`), nebo typu, který člen `M` sám nedeklaruje (zděděný z báze mimo jednotku). Kořen se čte jako dosud.

Odpovědi 1–3 jsou výpovědí jednotky, a dávají je proto oba průchody stejně: řetěz, který je podle nich navigací, dotazem není a třída kolem něj tím entitou být nepřestává. Navigace dotazem není beze slova, stejně jako u 109 a 111: nástroj nepřekládá kód kolem dotazů a procházení načtených objektů je takový kód — převod o něm nic netvrdí, a nic se tedy neztrácí.

**Mapování převodu rozhoduje o překladu, ne o tom, co je dotaz.** U odpovědi 4 může dotazový průchod vědět víc než jednotka. Uvádí-li jednotka pro `x` typ, který převod mapuje jako entitu, je `M` členem entity, a entita `DbSet` nemá: `DbSet` je znak, který nese jen kontext (rozhodnutí 111), a kontext entitou převodu nikdy není, protože ho entitní průchod vylučuje. Místo zůstává místem — dostane pořadové číslo jako každé jiné a entitní průchod třídu kolem něj vyloučí stejně —, ale dotazový průchod ho odmítne záznamem `Failure`, který jmenuje jméno, typ i člen, a artefakt nevznikne. Mapování tak smí zabránit vymyšlenému dotazu, ne udělat nebo zrušit místo. Tutéž roli hraje v dotazovém průchodu už dnes: join po asociační cestě (rozhodnutí 101) i parametr, který nejde otypovat (083), odmítají podle mapování, ale co je dotaz, neurčují. Záznam navíc vysvětlí, proč třída kolem místa entitou není, ačkoli z ní žádný dotaz nevyšel.

**Jméno přes `this` je totéž jméno.** `this._ctx.Customers` je `_ctx.Customers` zapsané jinak a čte se stejně — místo i otázky 1–4. Pravidlo, které rozhoduje podle jména, by jinak dávalo dvěma zápisům téhož jména různé odpovědi, a to rozhodnutí 111 vyloučilo: „tvar textu rozhoduje jen o tom, jak se text rozparsuje, nikdy o roli". Člen vlastní instance (`Customers`, `this.Customers`) zůstává, jak ho vymezilo 111: uvnitř kontextu kořenem, jinde navigací.

**Sémantický model zůstává omezený na jednotku.** Navázat jméno na deklaraci a přečíst deklarovaný typ jména i členu reference nepotřebuje: model nad samotnou jednotkou dá typ, který jednotka nedeklaruje, jako chybový typ se jménem, které deklarace uvádí, a typ, který deklaruje, i s jeho členy. Framework v modelu není (S1) a výsledek je při každém běhu týž (S2). Model se postaví jen tam, kde jednotka jméno příjemce deklaruje — u fragmentu nad `ctx`, který nedeklaruje nic, se nestaví, takže cena matic i katalogu se nemění (S3).

**Co je fakt C# a co fakt EF Core.** Že o dotazu rozhoduje statický typ, je tvrzení jazyka a `System.Linq`, a bydlí proto ve sdíleném čtení LINQ (rozhodnutí 026), jako pravidlo proměnné prvku rozhodnutí 109. Wrapper EF Core dodává, co je jeho: že `DbSet<T>` je jeho kořen. `Find` patří do seznamu kroků, které dotaz vykonají, vedle `Load`, `ExecuteDelete` a `ExecuteUpdate` EF Core, které v něm jsou od rozhodnutí 109. NHibernate se nemění: jeho kořen je volání a otázka se u něj nepoloží.

## Důsledky

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR navenek.** Pole odpovědi ani požadavku nepřibývá ani neubývá. Řetěz nad navigací, který dosud vydal vymyšlený dotaz, nevydá nic, nebo odmítnutí; třída, která jen procházela navigace entity, kterou jednotka deklaruje, zůstává entitou; řetěz přes `this._ctx` se čte; řetěz nad členem typu `IQueryable<T>` se odmítá místo vydání dotazu nad tabulkou pojmenovanou po členu. Všechno jsou to opravy vad, ne změny kontraktu. Uvnitř řešení se mění jen sdílené čtení LINQ a rozpoznání kořene EF Core; orchestrace, `AbstractWrappers` ani entitní průchod se nemění — ten dostane novou odpověď týmž hledáním předání.

**Chování ostatních vstupů se nemění, a tím se změna ověří.** Matice směrů, matice kategorií, diferenční matice i katalog LDBC nesou dotazové fragmenty nad jménem, které jednotka nedeklaruje, nebo celé soubory s kontextem, jehož `DbSet` jednotka deklaruje — odpověď 4, resp. 3 s `DbSet<T>`. Jejich artefakty i záznamy musí zůstat stejné.

**Vyslovené meze.** *Entita z jiného souboru*: jednotka, která typ příjemce jen uvádí, sama na navigaci neukáže, takže `order.Lines.Where(…)` nad parametrem `SalesOrder order`, jehož třída v převodu není, zůstává dotazem podle místa, jako dnes. Je-li v převodu, dostane odmítnutí, ale třída kolem místa zůstává vyloučená jako kód — u služby nebo pomocné třídy správně (rozhodnutí 111 by ji jinak četlo jako entitu), u entity, jejíž metoda prochází navigaci jiné entity z jiného souboru, chybně; oba záznamy to ukážou. *Proměnná naplněná voláním, které jednotka nedeklaruje*: `var order = repository.Load(id)` nemá typ, který by jednotka uváděla, a hodnota není vykonaný dotaz, takže `order.Lines` je dotazem podle místa — a musí jím zůstat, protože týž tvar má `var ctx = factory.CreateDbContext()`, které je kontextem. Obě meze patří do katalogu podmnožiny.

**Co tohle rozhodnutí neřeší.** *Explicitní načtení navigace* — `ctx.Entry(order).Collection(o => o.Lines).Query()` a `Reference(…).Query()` — je API EF Core, které vrací dotaz nad navigací omezený na řádky jedné načtené entity, a poskytovatel ho pošle databázi. Sonda z 2026-10-02 ukázala, že se dnes nečte vůbec: hlava řetězu je volání `Entry` na kontextu, ne kořen, a třída kolem vyjde jako entita. Je to tiché místo čtení a volba o tom, jak takový dotaz nést, protože jeho filtr je klíč entity, kterou drží program, ne text; položka k tomu je v [`open-items.md`](../open-items.md).

**Testy.**

- Parametr typu entity, kterou jednotka deklaruje, s `Where` nad její kolekcí: žádný dotaz, žádný záznam a třída kolem zůstává entitou.
- `var order = ctx.SalesOrders.First(…)`, totéž s `await … FirstAsync(…)` a s `Find(id)`, pak `order.Lines.Where(…)`: žádný dotaz nad `Lines`; `var first = orders.First()` nad seznamem: totéž.
- Pole typu kontextu, který jednotka deklaruje: `_ctx.Customers` i `this._ctx.Customers` se čtou; `this._ctx` typu, který jednotka nedeklaruje, se čte podle místa.
- Člen kontextu jiného typu než `DbSet<T>`: žádný dotaz.
- Člen typu `IQueryable<T>` třídy jednotky: odmítnutí, které člen jmenuje.
- Parametr typu, který jednotka nedeklaruje a převod mapuje jako entitu: odmítnutí, které jmenuje typ; bez mapovací jednotky dotaz podle místa.
- Fragment nad nedeklarovaným `ctx` a služba s parametrem typu kontextu, který jednotka nedeklaruje: beze změny.
- Všechny matice a katalog LDBC beze změny očekávaných výstupů.
