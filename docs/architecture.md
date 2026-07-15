# Architektura aplikace – současný stav

**Účel:** referenční popis architektury tak, jak je dnes implementovaná, nezávisle na akademickém textu diplomky, ze které projekt vzešel.
**Zdroj faktů:** vlastní čtení zdrojového kódu + kapitola *Architecture and Implementation* z původní diplomky (fakta přepsaná vlastními slovy, ne citace). Naposledy ověřeno proti kódu: červenec 2026.

---

## 1. Přehled

Aplikace překládá entity, mapování a dotazy mezi třemi .NET ORM frameworky – Dapper, NHibernate a EF Core – přes společnou frameworko-nezávislou mezireprezentaci. Pipeline má dvě fáze: parser převede zdrojový kód do mezireprezentace, builder z ní vygeneruje kód pro cílový framework. Nad tím běží ještě Advisor – optimalizační modul, který pro danou sadu dotazů doporučí nejvhodnější framework (nebo kombinaci frameworků) na základě reálně naměřeného výkonu.

## 2. Struktura řešení (.NET solution)

Aplikace je .NET 10 solution (povýšená z .NET 8) rozdělená na projekty tří typů:

- **ASP.NET web projekt** – `ORMConvertorAPI`, poskytuje REST API a servíruje zkompilovaný Angular frontend jako statické soubory. API dokumentace je automaticky generovaná přes Swagger; UI je zapnuté jen v Development prostředí, pak je dostupné na `/orm/swagger`.
- **Testovací projekt (xUnit v3)** – `Tests`, testy parserů a builderů pro všechny tři ORM (převod do mezireprezentace a z ní, identity testy) a kombinované end-to-end testy pro dvojice EF Core ↔ NHibernate a EF Core → Dapper (dotazy).
- **Class library projekty** – zbytek, nejsou samostatně spustitelné, používají se jen jako reference.

### Knihovní projekty a jejich zodpovědnost

| Projekt | Zodpovědnost |
|---|---|
| `Model` | centrální doménové modely – entity/mapování mezireprezentace i instrukce pro dotazy; referencovaný téměř všude |
| `Common` | sdílené konvertory nezávislé na frameworku – `AccessModifierConvertor`, `CLRTypeConvertor`; používají je všechny buildery |
| `AbstractWrappers` | rozhraní pro parsery a buildery; buildery mají společnou funkcionalitu řešenou jako abstraktní třídy, parsery jsou čistá rozhraní |
| `DapperWrappers` / `EFCoreWrappers` / `NHibernateWrappers` | konkrétní implementace parserů/builderů pro jednotlivé ORM, každý framework izolovaný ve vlastním projektu |
| `OrmConvertor` | orchestrace – třída `ConversionHandler` přijímá zdrojový vstup, přes factory třídy najde správný parser/builder a vrátí výstup; s konkrétními implementacemi pracuje jen přes rozhraní |
| `SampleData` | ukázkové vstupy (entity, mapování, dotazy) pro frontend – servírované přes endpointy `/samples` a `/samples-advisor`; zároveň se používají v testech |
| `Advisor` | ILP optimalizátor (GLPK), vybírá framework/kombinaci frameworků podle naměřených nákladů – detaily v §7 |
| `AdvisorBenchmarking` | dynamická kompilace a spuštění vygenerovaného kódu (Roslyn) pro reálné změření běhu a paměti |

## 3. Použité návrhové vzory

- **Adapter** – každý wrapper (parser i builder) adaptuje rozhraní konkrétního ORM na společnou mezireprezentaci.
- **Visitor** – generování výstupního kódu z instrukcí dotazu (`IQueryVisitor` a jeho implementace).
- **Builder** – entity/query buildery skládají výstupní kód postupně přes `StringBuilder`.
- **Factory** – výběr správné konkrétní implementace parseru/builderu podle zvoleného ORM.

## 4. Parsery a buildery – jak fungují dnes

- Všechny entity parsery používají Roslyn syntax analyzer. Dapper parser čte jen strukturu entity (Dapper mapování nepodporuje), EF Core parser navíc interpretuje atributové mapování, NHibernate má dva samostatné parsery – jeden pro entitu (stejně jako Dapper), druhý pro XML mapování (LINQ to XML).
- Parsování dotazů je implementované jen pro EF Core LINQ (`EFCoreLinqQueryParser` dědí z Roslyn `CSharpSyntaxWalker`, přepisuje `Visit*` metody pro analýzu řetězených volání).
- Generování dotazů je implementované jen pro Dapper SQL (přes `StringBuilder` + dedikovaný visitor `DapperSQLQueryVisitor`).
- Entity buildery fungují stejně napříč frameworky – skládají string šablony; NHibernate builder plní dva `StringBuilder`y najednou a vrací dva výstupy (C# entitu + XML mapování).

## 5. Spuštění a nasazení

**Přes Visual Studio:** otevřít `ORMConvertor.sln`, nastavit `ORMConvertorAPI` jako startup projekt, spustit (`F5` / `Ctrl+F5`). Otestovaný je jen `http` launch profil.

**Přes .NET CLI:**

```
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

Aplikace pak běží typicky na `http://localhost:5072/orm/`.

**Testy:**

```
dotnet test Tests/Tests.csproj --configuration Release
```

Testy běží i v GitHub Actions (konfigurace ve `.github`) – při push na `main` a u pull requestů, ale jen pokud se změnilo něco uvnitř `ORMConvertor/**`; změny v `docs/`, `benchmarks/` apod. workflow nespustí.

**Frontend (Angular):** je potřeba zkompilovat zvlášť a zkopírovat do `wwwroot`, odkud ho servíruje ASP.NET:

```
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/"
# a zkopírovat dist/browser/* do ../wwwroot/
```

**Kontejnerizované nasazení:** v repozitáři je i `docker-compose.yml` (aplikace + SQL Server s WideWorldImporters přes `database.Dockerfile`) a vícestupňový `ORMConvertorAPI/Dockerfile`, který sestaví frontend (Node), nativní knihovnu Advisoru (`libadvisor.so`, gcc + GLPK) i .NET aplikaci. Soubor `ecosystem.config.js` je konfigurace pro proces manager PM2 (nasazení mimo Docker). Tahle cesta zatím není podrobněji zdokumentovaná – viz úklidové úkoly v `docs/current-state.md`.

## 6. Rozhraní parserů a builderů

`AbstractEntityBuilder` odděluje dvě sady metod. **Naplnění mezireprezentace** řeší veřejné metody implementované přímo v abstraktní třídě (framework-nezávislé): `BeginEntity` (začátek další entity – builder umí držet víc entit najednou v `EntityMaps`), `AddNamespace`, `AddClassHeader`, `AddSchema`, `AddTable`, `AddProperty`, `SetPropertyDatabaseMapping` (databázové detaily mapování – sloupec, typ, délka, precision/scale, nullabilita, ostatní klíč–hodnota), `AddPrimaryKey`, `AddForeignKey`.

**Generování výstupu** deklaruje abstraktní třída jako jedinou veřejnou abstraktní `Build()` a šest `protected abstract` metod (`BuildImports`, `BuildTableSchema`, `BuildPrimaryKey`, `BuildForeignKey`, `BuildProperties`, `FinalizeBuild`). Skutečnost je ale taková, že tohle rozdělení je pozůstatek staršího návrhu pro jednu entitu: každý konkrétní builder si `Build()` implementuje sám – iteruje přes `EntityMaps` a volá **vlastní privátní statické overloady** týchž metod s parametry `(EntityMap, StringBuilder…)`. Bezparametrické `protected override` verze jsou ve všech třech builderech prázdné stuby s komentářem `// unused in multi-entity flow`. Reálné pořadí kroků v EF Core i NHibernate builderu je: `BuildImports` → `BuildTableSchema` → `BuildPrimaryKey` → `BuildProperties` → `BuildForeignKey` → `FinalizeBuild` (primární klíč se generuje **před** vlastnostmi). Dapper builder kroky `BuildPrimaryKey` a `BuildForeignKey` vynechává úplně, protože Dapper mapování klíčů nemá. Kandidát na refaktoring: buď z `Build()` udělat šablonovou metodu nad overloady s parametry, nebo mrtvé stuby odstranit.

`IParser` (entity/mapování) definuje jen dvě metody: `CanParse(contentType)` – zjistí, jestli parser umí daný vstupní formát (důležité tam, kde je mapování rozdělené do víc souborů, typicky NHibernate XML), a `Parse(source)`, která nemá návratovou hodnotu a místo toho naplňuje mezireprezentaci přes volání na `AbstractEntityBuilder`. `IQueryParser` je stejné, jen `Parse` navíc bere referenci na už naparsované mapování entit, protože dotaz sám o sobě často neobsahuje název tabulky/sloupce (typicky LINQ).

Původní návrh pro `AbstractQueryBuilder` (v `thesis/chapters/04_query_translation.tex` v původním repozitáři) počítá s `Push()`/`Pop()` metodami pro vstup/výstup z vnořeného poddotazu a se dvěma abstraktními výstupními metodami – `Build()` pro nativní syntaxi cílového frameworku (LINQ apod.) a `BuildSQL()` pro syrové SQL. Ověřeno proti kódu: `Push()`/`Pop()` implementované jsou (zásobník značek, `Pop()` obalí nasbírané instrukce do `SubQueryInstruction`; `SetOperation` je bez předchozího Push/Pop odmítnutá výjimkou). `BuildSQL()` ale **neexistuje** – je jen jediná abstraktní `Build()` vracející `List<ConversionSource>`; rozlišení nativní syntaxe vs. SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate. Poddotazy jsou navíc ve visitor vrstvě dotažené jen napůl: `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` obsahuje `TODO` a vrací prázdný string (instrukce uvnitř poddotazu projde, ale výsledek se nikam neskládá). Tvar `SelectInstruction` se od návrhu odchýlil taky – řešeno v `docs/design/001-query-model-and-composite-keys.md`.

## 7. Advisor – implementační detaily

ILP model je napsaný v C (`Advisor/ilp.c`) přímo přes GLPK C API (ne přes vyšší úroveň abstrakce): `glp_create_prob()` založí úlohu, `glp_add_cols()`/`glp_set_col_kind()` definují binární proměnné $x_{q,f}$ a $y_f$, `glp_set_obj_coef()` nastaví účelovou funkci, `glp_add_rows()`/`glp_set_row_bnds()` definují omezení, `glp_load_matrix()` nahraje řídkou matici koeficientů. Řešení spouští `glp_init_iocp()` (parametry solveru) a `glp_intopt()` (branch-and-bound). Výsledek se čte zpět přes `glp_mip_col_val()`.

C# strana (`Advisor.Solve`) volá tenhle wrapper přes P/Invoke – `[LibraryImport("libadvisor.so")]`. Knihovna `libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`: gcc + `libglpk-dev`); název je natvrdo linuxový, takže Advisor mimo Linux/Docker neběží – překladová část aplikace na tom nezávisí a funguje všude. Build krok pro Windows (`advisor.dll`) neexistuje, i když `ilp.c` má exportní makra připravená.

## 8. Co je záměrně mimo rozsah dnešní implementace

Podrobný seznam a zdůvodnění je v [`docs/current-state.md`](./current-state.md). Stručně: podpora jen tří ORM, jednosměrný překlad dotazů (jen EF Core → Dapper), Advisor omezený na Dapper a EF Core, žádné napojení na databázi pro doplnění chybějících metadat, žádné vnořené/OR podmínky v dotazech, žádná podpora kompozitních klíčů ani vícesloupcových vztahů.