# Architektura aplikace – současný stav

**Účel:** referenční popis architektury tak, jak je dnes implementovaná, nezávisle na akademickém textu diplomky, ze které projekt vzešel.
**Zdroj faktů:** vlastní čtení zdrojového kódu + kapitola *Architecture and Implementation* z původní diplomky (fakta přepsaná vlastními slovy, ne citace).

---

## 1. Přehled

Aplikace překládá entity, mapování a dotazy mezi třemi .NET ORM frameworky – Dapper, NHibernate a EF Core – přes společnou frameworko-nezávislou mezireprezentaci. Pipeline má dvě fáze: parser převede zdrojový kód do mezireprezentace, builder z ní vygeneruje kód pro cílový framework. Nad tím běží ještě Advisor – optimalizační modul, který pro danou sadu dotazů doporučí nejvhodnější framework (nebo kombinaci frameworků) na základě reálně naměřeného výkonu.

## 2. Struktura řešení (.NET solution)

Aplikace je .NET 10 solution (povýšená z .NET 8) rozdělená na projekty tří typů:

- **ASP.NET web projekt** – `ORMConvertorAPI`, poskytuje REST API a servíruje zkompilovaný Angular frontend jako statické soubory. API dokumentace je automaticky generovaná přes Swagger, dostupná na `/orm/swagger`.
- **Testovací projekt (xUnit)** – `Tests`, integrační testy pro každý směr překladu i jejich kombinace.
- **Class library projekty** – zbytek, nejsou samostatně spustitelné, používají se jen jako reference.

### Knihovní projekty a jejich zodpovědnost

| Projekt | Zodpovědnost |
|---|---|
| `Model` | centrální doménové modely – entity/mapování mezireprezentace i instrukce pro dotazy; referencovaný téměř všude |
| `AbstractWrappers` | rozhraní pro parsery a buildery; buildery mají společnou funkcionalitu řešenou jako abstraktní třídy, parsery jsou čistá rozhraní |
| `DapperWrappers` / `EFCoreWrappers` / `NHibernateWrappers` | konkrétní implementace parserů/builderů pro jednotlivé ORM, každý framework izolovaný ve vlastním projektu |
| `OrmConvertor` | orchestrace – třída `ConversionHandler` přijímá zdrojový vstup, přes factory třídy najde správný parser/builder a vrátí výstup; s konkrétními implementacemi pracuje jen přes rozhraní |
| `Advisor` | ILP optimalizátor (GLPK přes P/Invoke), vybírá framework/kombinaci frameworků podle naměřených nákladů |
| `AdvisorBenchmarking` | dynamická kompilace a spuštění vygenerovaného kódu (Roslyn) pro reálné změření běhu a paměti |

## 3. Použité návrhové vzory

- **Adapter** – každý wrapper (parser i builder) adaptuje rozhraní konkrétního ORM na společnou mezireprezentaci.
- **Visitor** – generování výstupního kódu z instrukcí dotazu (`IQueryVisitor` a jeho implementace).
- **Builder** – entity/query buildery skládají výstupní kód postupně přes `StringBuilder`.
- **Factory** – výběr správné konkrétní implementace parseru/builderu podle zvoleného ORM.

## 4. Parsery a buildery – jak fungují dnes

- Všechny entity parsery používají Roslyn syntax analyzer. Dapper parser čte jen strukturu entity (Dapper mapování nepodporuje), EF Core parser navíc interpretuje atributové mapování, NHibernate má dva samostatné parsery – jeden pro entitu (stejně jako Dapper), druhý pro XML mapování (LINQ to XML).
- Parsování dotazů je implementované jen pro EF Core LINQ (rozšiřuje `CSharpSyntaxTree`, přepisuje `Visit` pro analýzu volání metod).
- Generování dotazů je implementované jen pro Dapper SQL (přes `StringBuilder` + dedikovaný visitor).
- Entity buildery fungují stejně napříč frameworky – skládají string šablony; NHibernate builder navíc generuje samostatně XML mapování jako druhý výstup.

## 5. Spuštění a nasazení

**Přes Visual Studio:** otevřít `ORMConvertor.sln`, nastavit `ORMConvertorAPI` jako startup projekt, spustit (`F5` / `Ctrl+F5`). Nakonfigurovaný je jen `http` launch profil.

**Přes .NET CLI:**

```
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

Aplikace pak běží typicky na `http://localhost:5072/orm/`.

**Testy:**

```
dotnet test Tests/Tests.csproj --configuration Release
```

Testy se navíc spouští automaticky na každý commit přes GitHub Actions (konfigurace ve `.github`).

**Frontend (Angular):** je potřeba zkompilovat zvlášť a zkopírovat do `wwwroot`, odkud ho servíruje ASP.NET:

```
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/"
# a zkopírovat dist/browser/* do ../wwwroot/
```

## 6. Rozhraní parserů a builderů

`AbstractEntityBuilder` odděluje dvě sady metod: veřejné metody pro naplnění mezireprezentace (implementované přímo v abstraktní třídě, framework-nezávislé) a privátní abstraktní metody pro generování výstupu (implementuje každý konkrétní builder podle sebe), volané z jediné veřejné `Build()`.

Naplnění mezireprezentace: `AddNamespace`, `AddClassHeader`, `AddSchema`, `AddTable`, `AddProperty`, `AddPropertyMapping`, `AddPrimaryKey`, `AddForeignKey`.

Generování výstupu: `BuildImports`, `BuildTableSchema`, `BuildProperties`, `BuildPrimaryKey`, `BuildForeignKey`, `FinalizeBuild` – volané v tomto pořadí z `Build()`.

`IParser` (entity/mapování) definuje jen dvě metody: `CanParse(contentType)` – zjistí, jestli parser umí daný vstupní formát (důležité tam, kde je mapování rozdělené do víc souborů, typicky NHibernate XML), a `Parse(source)`, která nemá návratovou hodnotu a místo toho naplňuje mezireprezentaci přes volání na `AbstractEntityBuilder`. `IQueryParser` je stejné, jen `Parse` navíc bere referenci na už naparsované mapování entit, protože dotaz sám o sobě často neobsahuje název tabulky/sloupce (typicky LINQ).

Původní návrh pro `AbstractQueryBuilder` (v `04_query_translation.tex`) počítá s `Push()`/`Pop()` metodami pro vstup/výstup z vnořeného poddotazu (obalí nasbírané instrukce do `SUBQUERY`), a se dvěma abstraktními výstupními metodami – `Build()` pro nativní syntaxi cílového frameworku (LINQ apod.) a `BuildSQL()` pro syrové SQL, které umí vygenerovat i frameworky, jež primárně nepoužívají SQL napřímo. Při implementaci query builderů pro EF Core a NHibernate stojí za to ověřit, jestli se toho dnešní `AbstractQueryBuilder` skutečně drží, nebo se od návrhu odchýlil (podobně jako se odchýlil tvar `SelectInstruction` – řešeno v `docs/design/001-query-model-and-composite-keys.md`).

## 7. Advisor – implementační detaily

ILP model je implementovaný přímo přes GLPK C API (ne přes vyšší úroveň abstrakce): `glp_create_prob()` založí úlohu, `glp_add_cols()`/`glp_set_col_kind()` definují binární proměnné $x_{q,f}$ a $y_f$, `glp_set_obj_coef()` nastaví účelovou funkci, `glp_add_rows()`/`glp_set_row_bnds()` definují omezení, `glp_load_matrix()` nahraje řídkou matici koeficientů. Řešení spouští `glp_init_iocp()` (parametry solveru) a `glp_intopt()` (branch-and-bound). Výsledek se čte zpět přes `glp_mip_col_val()`.

## 8. Co je záměrně mimo rozsah dnešní implementace

Podrobný seznam a zdůvodnění je v [`docs/current-state.md`](./current-state.md). Stručně: podpora jen tří ORM, jednosměrný překlad dotazů (jen EF Core → Dapper), Advisor omezený na Dapper a EF Core, žádné napojení na databázi pro doplnění chybějících metadat, žádné vnořené/OR podmínky v dotazech, žádná podpora kompozitních klíčů ani vícesloupcových vztahů.
