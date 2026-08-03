# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

---

## Doporučené pořadí

1. **Dokončit přepracování dokumentace** (práce) — bez toho není repozitář konzistentní.
2. **R2 — členy vynucené frameworkem** (rozhodnutí) — dotýká se kroku 5a, protože junction entita s kompozitním klíčem naráží přesně na tuto kategorii.
3. **Krok 5a — junction entita v builderech** (práce) — poslední krok původního plánu; odblokovaný opravou O1.
4. **R3, R4** (rozhodnutí) — R4 je předpoklad S6.
5. **Ú2 — neutralizace typového modelu** (rozhodnutí, vlastní design doc) — předpoklad javové větve, blokuje F7–F10.
6. **Zbytek** podle priorit vyplývajících z požadavků F/S/E.

---

## Otevřená rozhodnutí

### Členy vynucené frameworkem jako pojmenovaný koncept
*Audit 2026-08-02, R2 (zjištění 4.4). Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md).*

Zavést do architektury explicitní pojem pro boilerplate, který cílový framework vyžaduje a který není faktem o doméně — `Equals`/`GetHashCode`/`[Serializable]` u NHibernate, ID třída u JPA, `virtual` na mapovaných členech. Cíl: každý nový builder má jedno místo, kde deklaruje, co musí ke generované třídě přidat, a existuje na to společný test.

### Diagnostika jako kategorie
*Audit 2026-08-02, R3. Zobecňuje rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md).*

Přeformulovat mechanismus „fakt nevyjádřitelný v cílovém frameworku" tak, aby byl parametrizovaný frameworkem, ne dapperovskou větví. Seznam faktů pro Dapper už existuje ve srovnávací analýze, pro MyBatis bude analogický.

Do stejné kategorie patří i zúžení uvnitř typového modelu, nejen fakty, které cíl neunese — konkrétně slévání `DateTime`, `DateTime2` a `SmallDateTime` do jediného typu NHibernate. Diagnostika by měla ohlásit obojí a rozlišit to: první případ je vlastnost cílového frameworku, druhý vlastnost převodu a dá se odstranit.

### Deklarace cílových verzí frameworků
*Audit 2026-08-02, R4. Předpoklad S6.*

Rozhodnout, kde se deklaruje cílová verze frameworku pro převod — v IR, nebo v konfiguraci převodu. Zpřesní generování: volba mezi `[PrimaryKey]` a `HasKey` podle verze EF Core, dostupnost `DateOnly` podle verze NHibernate, dialekt pro odvození SQL typu.

### Neutralizace typového modelu
*Audit 2026-08-02, Ú2 (zjištění 2.2, 2.3, 2.4, 4.5). Zaslouží si vlastní design doc.*

Nejrozsáhlejší otevřená položka, ve dvou rovinách:

1. `CLRType` → jazykově neutrální reprezentace (`LangType` podle JSS §5.2), s doplněním chybějících typů a s vyřešením případu `CLRType.Char`, který dnes nelze namapovat na správný typ NHibernate, protože v `DatabaseType` chybí hodnota pro jednotlivý unicode znak.
2. `DatabaseType` → databázově neutrální reprezentace, případně s vrstvou pro dialekty.

Je to **předpoklad** pro F7–F10, ne jejich příprava.

### Revize `PrimaryKeyStrategy` a sémantiky `Order`
*Audit 2026-08-02, Ú1 (zjištění 3.1).*

Projít výčet `PrimaryKeyStrategy` proti generátorům NHibernate, mechanismům EF Core a `@GeneratedValue` z JPA. Ve stejném průchodu uzavřít otázku, zda má být `Order` na `PrimaryKeyPart` unikátní a souvislý od jedničky, a podle rozhodnutí případně doplnit validaci.

### Centrální správa verzí
*Audit 2026-08-02, Ú4 (zjištění 3.4.1).*

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně.

### Sjednocení ADO.NET provideru v benchmarcích
*Audit 2026-08-02, Ú5 (zjištění 3.4.2). Souvisí s E-požadavky.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce.

### Osud `wwwroot`
*Souvisí s S5.*

Automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit. Dnes je to commitnutý build, takže po každé změně frontendu hrozí, že nasazený bundle neodpovídá zdrojákům.

---

## Otevřená práce

### Dokončit přepracování dokumentace
Zbývá: zapsat samotný refaktor jako rozhodnutí 007, přejmenovat `current-state.md` na `baseline.md`, smazat `changelog.md`, přesunout audit do `analysis/`, zrušit složku `design/` (obsah je rozebraný do `decisions/`, `architecture.md` a tohoto souboru) a aktualizovat příručku.

### Krok 5a — junction entita v builderech
*Rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md). Odblokováno opravou O1.*

Generování explicitní junction entity a vícesloupcový rendering cizích klíčů. Testovací vstupy je nutné skládat ručně přes builder API, protože `ColumnPairs` se automaticky neplní.

### Krok 5b — detekce N:M v parserech
*Blokováno.*

Detekce N:M na vstupu, syntéza junction entity a naplnění `ColumnPairs`. Cílové sloupce nejdou určit z jedné translation unit, takže to stojí na metadatech z databáze (F4/F5).

### Strukturovaná varování pro nevyjádřitelné fakty
*Rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md), rozsah upřesní R3.*

Dapper builder dnes klíče a vztahy zahazuje potichu. Do vzniku diagnostické infrastruktury z F11 stačí prostý seznam varování ve výsledku převodu.

### Dotazová větev
- `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec — poddotazy projdou, ale výsledek se nikam neskládá.
- `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu).
- `BuildSQL()` z původního návrhu neexistuje; rozlišení nativní syntaxe od syrového SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate.

### EF Core — strategie primárního klíče
Nepropaguje se do výstupu (TODO v `EFCoreEntityBuilder.BuildPrimaryKey`). Rozdíl v paritě, ne regrese. Souvisí s revizí `PrimaryKeyStrategy`.

### Parser NHibernate — varianta s klíčovou třídou
*Audit 2026-08-02, Ú3 (zjištění 3.2). Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md) a na neutralizaci typového modelu.*

Bez ní nelze číst vstupy, které klíč vyjadřují klíčovou třídou — a analogicky pak `@EmbeddedId` na javové straně.

### Mrtvé stuby v builderech
Bezparametrické `protected override` metody jsou ve všech třech builderech prázdné a nesou komentář `// unused in multi-entity flow`. Buď z `Build()` udělat šablonovou metodu nad overloady s parametry, nebo stuby odstranit.

### Frontend
Odloženo do cílenější přestavby, současný stav je funkční:

1. Přeskočit prázdné dotazové jednotky v `convert()` před odesláním — server je striktní záměrně, tolerance patří do UI.
2. Zobrazovat chyby ze serveru přes `err.error`, ne `err.message`.
3. Validace před odesláním podle S7, tedy chyby na úrovni souboru a řádku.

### Ověřit docker-compose
`ConnectionStrings__AdvisorDatabase` je commitnutá deklarace, kterou zatím nikdo nespustil. Ověří se při prvním reálném běhu Advisoru v Dockeru.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne:

| Blok | Co odblokuje |
|---|---|
| **F4–F6** metadata z databáze | `ColumnPairs`, krok 5b, úplné mapování z neúplného vstupu |
| **F11** validace a strukturovaná diagnostika | varování z R3, kontrolu úplnosti IR |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; stojí na neutralizaci typového modelu |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání |
| **E1–E7** experimenty | E7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání zůstává rozpracovaná: chybí NHibernate LINQ parser, Dapper SQL parser a query buildery pro EF Core i NHibernate.
