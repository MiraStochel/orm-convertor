# 026 — Kde bydlí sdílené čtení dotazů

Datum: 2026-08-19
Stav: revidováno
Požadavky: F7–F10, S1, S2
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md) a [025](025-query-language-as-content-type.md); S1; JSS §4.3

## Kontext

Doplnit dotazovou matici na devět směrů znamená dva nové parsery a každý si nese vlastní otázku o tom, kam patří jeho kód.

**NHibernate jako zdroj.** Rozhodnutí [022](022-native-query-syntax-in-builders.md) zvolilo HQL jako *cílový* tvar a zároveň konstatovalo, že jako *zdroj* se NHibernate bude číst z LINQ — HQL parser by znamenal buď vlastní gramatiku, nebo referenci na NHibernate uvnitř wrapperu. NHibernate LINQ (`session.Query<T>()`) a EF Core LINQ (`ctx.Set<T>()`) jsou ale tentýž jazyk: `Where`, `Join`, `Select`, `OrderBy`, `GroupBy` jsou rozšiřující metody `System.Linq` se shodnou sémantikou u obou poskytovatelů. Když se `EFCoreLinqQueryParser` přečte řádek po řádku, je frameworkově specifické jen trojí: rozpoznání kořene dotazu, odvození jména entity z kořene (v EF Core přes název `DbSet`u, v NHibernate je jméno v kořeni přímo) a jedna zmínka téhož názvu proměnné jinde. Zbytek — průchod řetězem volání, převod predikátu na podmínkový strom, čtení operandů — je `System.Linq`.

Kdyby NHibernate parser vznikl kopií, opakuje se přesně to, co v repozitáři už jednou nastalo: `DapperEntityParser` a `NHibernateEntityParser` jsou dnes bezmála znak po znaku totéž a každá oprava čtení vlastností se musí udělat dvakrát. Kdyby naopak NHibernate wrapper dědil z třídy v `EFCoreWrappers`, porušilo by to S1 doslova — přidání frameworku by znamenalo závislost na parseru jiného frameworku.

**Dapper jako zdroj.** Číst T-SQL znamená mít parser jazyka, a otázky jsou dvě. První: vzít hotový parser, nebo napsat vlastní tokenizer a parser na tu podmnožinu SQL, kterou dotazová mezireprezentace unese. Druhá, padne-li volba na hotový: jestli se takový balíček smí objevit uvnitř wrapperu, když je S1 postavené na tom, že wrappery nezávisejí na frameworku, pro který generují.

## Zvažované varianty

### 1 — Zkopírovat čtení LINQ do NHibernate wrapperu

Nejrychlejší a nejhorší. Dvě kopie zhruba pěti set řádků, které se rozejdou při první opravě; a oprav je před sebou celá řada, protože dnešní parser tiše zahazuje predikáty a projekce. Zamítáme.

### 2 — Dát sdílené čtení LINQ do `AbstractWrappers`

Sedělo by to na první pohled: `AbstractWrappers` je společný základ, který wrappery už referencují. Zamítáme, a důvod je konkrétní: `AbstractWrappers` dnes nemá závislost na Roslynu. Kdyby ji dostal, poteče `Microsoft.CodeAnalysis.CSharp` do každého konzumenta překladového kontraktu — včetně budoucího javového wrapperu, který C# nikdy neuvidí. Základ má popisovat překlad, ne jazyk jednoho ekosystému.

### 3 — Dát sdílené čtení do `Common`

`Common` je určený pro sdílené věci nezávislé na frameworku. Zamítáme ze dvou důvodů. Technicky: `AbstractWrappers` referencuje `Common`, takže `Common` nemůže referencovat `AbstractQueryBuilder`, do kterého parser zapisuje. Významově: `Common` drží převodníky, ne parsery, a je referencovaný odevšad — přilepit k němu Roslyn má tentýž následek jako varianta 2.

### 4 — Vlastní knihovna pro čtení LINQ; parser jazyka bydlí ve wrapperu, který ten jazyk čte

### 5 — Vlastní tokenizer a parser T-SQL místo hotového balíčku

Rozsah vypadá zvládnutelně: článek omezuje scope na dotazy jen pro čtení (§3.2) a mezireprezentace nese jen kategorie Q2–Q12, takže z celého T-SQL je relevantní `SELECT`. Zvládnutelný ale není. Priorita operátorů a závorkování, na kterých stojí podmínkový strom; citované a schématem kvalifikované identifikátory (`[Sales].[Orders]`, `"Order"`); aliasy s `AS` i bez něj; parametry Dapperu (`@id`); řetězcové literály s escapováním; komentáře; funkce s argumenty; poddotazy; `IN` a `BETWEEN`. Každá z těch věcí je jednotlivě triviální a dohromady je to místo, kde ručně psané parsery hnijí — chyba se neprojeví výjimkou, ale tiše jiným významem dotazu, což je přesně ta třída selhání, kterou překlad mít nesmí.

Zamítáme ze dvou důvodů. Parser jazyka není přínosem téhle práce a spolkne víc času než celá dotazová matice. A tvrzení o syntaktické a strukturní korektnosti překladu (článek §3.2) je s referenčním parserem doložené jednou větou, kdežto s vlastním parserem se musí doložit pokrytím testů a každá díra v gramatice je naše.

Ze stejné úvahy zamítáme i multidialektovou knihovnu třetí strany (`SqlParserCS` jako port `sqlparser-rs`, gramatika z `grammars-v4` nad ANTLR): pro jediný dialekt, který dnes máme, dává slabší záruky než parser od výrobce toho dialektu. Vrátí se to jako otázka teprve s druhým dialektem — viz Důsledky.

## Rozhodnutí

**Volíme variantu 4. Sdílené čtení LINQ dostane vlastní knihovnu, kterou referencují wrappery EF Core a NHibernate. Parser konkrétního dotazovacího jazyka bydlí ve wrapperu frameworku, který v tom jazyce píše — a smí si na něj přinést balíček.**

**Sdílená knihovna je vrstva ekosystému, ne jádra.** Referencuje `Model`, `AbstractWrappers` a Roslyn a nabízí abstraktní parser LINQ se dvěma zásuvnými body: rozpoznej kořen dotazu a řekni jméno entity, které kořen odpovídá. `EFCoreWrappers` a `NHibernateWrappers` z něj dědí a vyplňují ty dva body. Nikdo jiný ho nereferencuje a javový wrapper ho nikdy neuvidí, takže Roslyn zůstává tam, kde se C# opravdu čte.

**S1 se tím naplňuje, ne obchází.** Požadavek chrání cenu přidání dalšího frameworku: nový framework se přidává parserem a builderem, ne zásahem do cizích. Vytknutí těla parseru z `EFCoreWrappers` je zásah do cizího wrapperu, který se dělá **jednou a teď**, a od té chvíle je přidání dalšího .NET frameworku čistě přírůstkové. Kdybychom ho neudělali, platil by se ten zásah znovu při každém dalším .NET frameworku, nebo — hůř — neplatil by se a kopie by se rozcházely.

**Parser jazyka patří k frameworku, který ten jazyk používá.** T-SQL čte jedině Dapper, takže balíček s parserem T-SQL patří do `DapperWrappers`. **Není to porušení S1 a je třeba to vyslovit, protože to tak vypadá.** S1 zakazuje wrapperu záviset na *frameworku, pro který generuje* — tedy `DapperWrappers` nesmí referencovat Dapper. Parser T-SQL není Dapper; je to parser jazyka, přesně jako Roslyn je parser C#, a Roslyn v tom projektu už dávno je. Rozdíl mezi „čtu jazyk, ve kterém je vstup napsaný" a „volám framework, pro který generuji" je ten, na kterém stojí celá dosavadní stavba wrapperů: generují text a k jeho čtení potřebují parser, ne runtime.

**Tím balíčkem je `Microsoft.SqlServer.TransactSql.ScriptDom`** — parser T-SQL od Microsoftu, na kterém stojí DacFx, SqlPackage i SSDT, tedy referenční parser toho jazyka, ne komunitní alternativa. Nemá tranzitivní závislosti a cílí mimo jiné na `netstandard2.0`, takže na .NET 10 běží beze změny. Otázku „dá se na něj spolehnout dlouhodobě" nezodpovídáme příslibem dodavatele, ale licencí: zdrojový kód je od roku 2023 otevřený pod MIT (`github.com/microsoft/SqlScriptDOM`) a parser je generovaný z formální gramatiky, takže i kdyby vydávání balíčku ustalo, jde ho převzít k sobě. Ověřeno 2026-08-19.

**Verze balíčku patří do tabulky zafixovaných verzí** v `architecture.md`, ze stejného důvodu jako verze Roslynu: tvrzení o tom, co nástroj přečte, je tvrzení o verzi parseru (S2).

**Dialekt zůstává jediný.** Parser T-SQL čte T-SQL, což je v souladu s tím, že jediným cílovým dialektem je zatím SQL Server. Až se dialekt začne deklarovat v deskriptoru — otevřená položka —, bude volba verze parseru další věcí, kterou ta deklarace určí; do té doby je konstantou na jednom místě.

## Důsledky

**Řešení se nevztahuje na entitní parsery, ale ukazuje na ně.** `DapperEntityParser` a `NHibernateEntityParser` jsou dnes duplicitou téhož druhu, jaké se tímhle rozhodnutím vyhýbáme na dotazové straně. Nespravujeme je při téhle příležitosti — je to jiná větev a jiný rozsah —, ale zapisujeme to jako otevřenou položku, aby se na to nepřišlo potřetí.

**Přepis `EFCoreLinqQueryParser` je jednorázový** (rozhodnutí [003](003-one-shot-migration.md)) a spadá do něj i to, že parser dnes stojí na `CSharpSyntaxWalker` a řetěz volání zpracovává vedlejším účinkem pořadí návštěv. Sdílený parser rozkládá řetěz explicitně, jak to popisuje algoritmus 2 článku; tím zaniká i zvláštní případ, kterým se dnes pozná `HAVING` jako `Where` navěšené na `GroupBy`.

**Pro čtvrtý framework se nemění nic.** Javový wrapper si přinese vlastní parser svého jazyka do vlastního projektu, přesně jako Dapper. Nic v `AbstractWrappers`, v `Common` ani ve sdílené knihovně LINQ kvůli němu měnit nejde a nemusí.

**Verze gramatiky je jiný fakt než verze balíčku.** Kód volí `TSql160Parser`, tedy gramatiku SQL Serveru 2022, a `initialQuotedIdentifiers: true`; balíček přitom nese i novější třídy. Obojí je tvrzení o tom, co nástroj přečte (S2), takže do tabulky zafixovaných verzí v `architecture.md` patří obojí, ne jen číslo balíčku.

**T-SQL je jediný jazyk, který tenhle parser umí, a týká se to i zdrojové strany.** Cílový dialekt je otevřená položka a do jejího vyřešení je jediným dialektem SQL Server; zdrojová strana má ale vlastní termín splatnosti. MyBatis je podle Table 2 článku rovněž „SQL only" (F8) a v javovém ekosystému běžně nad jiným systémem než SQL Server, kde `LIMIT` touhle gramatikou neprojde. Javový wrapper si proto přinese vlastní parser SQL do vlastního projektu — což je přesně to, co tohle rozhodnutí ukládá, takže je to důsledek, ne rozpor s ním. Teprve tam bude namístě znovu zvážit multidialektovou knihovnu z varianty 5.

**Dotaz, který v době překladu není řetězcovým literálem, přečíst nejde.** Konkatenace, interpolace nebo řetězec vytažený z proměnné či resource nemají v době překladu tvar, který by šlo parsovat; parser to hlásí záznamem o neúplnosti a dál nejde. Je to omezení vstupu, ne parseru — platilo by stejně u varianty 5.

**Testy.** Že týž dotaz zapsaný v LINQ EF Core a v LINQ NHibernate dá tutéž mezireprezentaci — to je nejpřímější důkaz, že se sdílené čtení chová shodně a že rozdíl je opravdu jen v kořeni.

## Historie

2026-08-19 — Doplněna volba parseru T-SQL, která v původním znění byla jen předpokladem Kontextu, ne argumentovanou variantou. Přibyla varianta 5 (vlastní tokenizer a parser), jmenovitě uvedený balíček i důvod, proč se na něj lze spolehnout, a tři důsledky, na které se při psaní nemyslelo: verze gramatiky vedle verze balíčku, zdrojová strana dialektu u F7–F10 a dotaz, který není řetězcovým literálem. Volba sama se nemění.
