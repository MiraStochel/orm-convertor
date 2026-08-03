# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

---

## Doporučené pořadí

1. **Členy vynucené frameworkem** (rozhodnutí) — junction entita nese typicky kompozitní klíč, takže na tuhle kategorii narazí hned další krok.
2. **Junction entita v builderech** (práce) — první bod, kterým se N:M propíše do generovaného kódu.
3. **Diagnostika jako kategorie** a **deklarace cílových verzí frameworků** (rozhodnutí) — druhé jmenované je předpoklad S6.
4. **Neutralizace typového modelu** (rozhodnutí, rozsahem na samostatný dokument) — předpoklad javové větve, blokuje F7–F10.
5. **Zbytek** podle priorit vyplývajících z požadavků F/S/E.

---

## Otevřená rozhodnutí

### Členy vynucené frameworkem jako pojmenovaný koncept
*Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md). Podklad: audit 2026-08-02, kap. 4.4.*

Zavést do architektury explicitní pojem pro boilerplate, který cílový framework vyžaduje a který není faktem o doméně — `Equals`/`GetHashCode`/`[Serializable]` u NHibernate, ID třída u JPA, `virtual` na mapovaných členech. Cíl: každý nový builder má jedno místo, kde deklaruje, co musí ke generované třídě přidat, a existuje na to společný test.

Dnes to buildery řeší ad hoc a `virtual` je zadrátovaný v `BuildPropertySignature`. Při pěti a více cílových frameworcích si to jinak každý nový builder objeví znovu a na něco zapomene.

### Diagnostika jako kategorie
*Zobecňuje rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md). Podklad: audit 2026-08-02, kap. 2.1 a 4.6.*

Přeformulovat mechanismus „fakt nevyjádřitelný v cílovém frameworku" tak, aby byl parametrizovaný frameworkem, ne dapperovskou větví. Seznam faktů pro Dapper už existuje ve srovnávací analýze, pro MyBatis bude analogický.

Do stejné kategorie patří i zúžení uvnitř typového modelu, nejen fakty, které cíl neunese — konkrétně slévání `DateTime`, `DateTime2` a `SmallDateTime` do jediného typu NHibernate. Diagnostika by měla ohlásit obojí a rozlišit to: první případ je vlastnost cílového frameworku, druhý vlastnost převodu a dá se odstranit.

### Deklarace cílových verzí frameworků
*Předpoklad S6. Podklad: audit 2026-08-02, kap. 3.3.*

Rozhodnout, kde se deklaruje cílová verze frameworku pro převod — v IR, nebo v konfiguraci převodu. Zpřesní generování: volba mezi `[PrimaryKey]` a `HasKey` podle verze EF Core, dostupnost `DateOnly` podle verze NHibernate, dialekt pro odvození SQL typu.

Dnes wrappery nereferencují žádný ORM balíček, jen Roslyn, takže cílová verze není nikde uvedená a existuje jen implicitní předpoklad o cílové syntaxi.

### Neutralizace typového modelu
*Rozsahem na samostatné rozhodnutí, ne na odstavec. Podklad: audit 2026-08-02, kap. 2.2–2.4 a 4.5.*

Nejrozsáhlejší otevřená položka, ve dvou rovinách:

1. `CLRType` → jazykově neutrální reprezentace (`LangType` podle JSS §5.2), s doplněním chybějících typů a s vyřešením případu `CLRType.Char`, který dnes nelze namapovat na správný typ NHibernate, protože v `DatabaseType` chybí hodnota pro jednotlivý unicode znak.
2. `DatabaseType` → databázově neutrální reprezentace, případně s vrstvou pro dialekty. Dnešní výčet je fakticky seznam typů T-SQL.

Je to **předpoklad** pro F7–F10, ne jejich příprava: javová ID třída se neobejde bez otypovaných polí, a ta vezme builder odsud.

### Revize `PrimaryKeyStrategy` a sémantiky `Order`
*Podklad: audit 2026-08-02, kap. 3.1 a 4.7.*

Projít výčet `PrimaryKeyStrategy` proti generátorům NHibernate, mechanismům EF Core a `@GeneratedValue` z JPA. Ve stejném průchodu uzavřít otázku, zda má být `Order` na `PrimaryKeyPart` unikátní a souvislý od jedničky, a podle rozhodnutí případně doplnit validaci.

### Centrální správa verzí

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně. Dnes se může nepozorovaně rozejít.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s E-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce.

### Osud `wwwroot`
*Souvisí s S5.*

Automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit. Dnes je to commitnutý build, takže po každé změně frontendu hrozí, že nasazený bundle neodpovídá zdrojákům.

---

## Otevřená práce

### Junction entita v builderech
*Rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md).*

Generování explicitní junction entity a vícesloupcový rendering cizích klíčů. Testovací vstupy je nutné skládat ručně přes builder API, protože `ColumnPairs` se automaticky neplní.

### Detekce N:M v parserech
*Blokováno.*

Detekce N:M na vstupu, syntéza junction entity a naplnění `ColumnPairs`. Cílové sloupce nejdou určit z jedné translation unit, takže to stojí na metadatech z databáze (F4/F5). Totéž místo je předznamenané i v NHibernate builderu, kde chybějící typ vlastnosti nese TODO „query database for the missing type".

### Strukturovaná varování pro nevyjádřitelné fakty
*Rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md); rozsah upřesní rozhodnutí o diagnostice jako kategorii.*

Dapper builder dnes klíče a vztahy zahazuje potichu. Do vzniku diagnostické infrastruktury z F11 stačí prostý seznam varování ve výsledku převodu.

### Dotazová větev
- `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec — poddotazy projdou, ale výsledek se nikam neskládá.
- `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu).
- `BuildSQL()` z původního návrhu neexistuje; rozlišení nativní syntaxe od syrového SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate.

### EF Core — strategie primárního klíče

Nepropaguje se do výstupu (TODO v `EFCoreEntityBuilder.BuildPrimaryKey`). Rozdíl v paritě, ne regrese. Souvisí s revizí `PrimaryKeyStrategy`.

### Parser NHibernate — varianta s klíčovou třídou
*Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md) a na neutralizaci typového modelu. Podklad: audit 2026-08-02, kap. 3.2.*

`NHibernateXMLMappingParser` čte `<composite-id>` jen přes `<key-property>`; atributy `name` a `class`, které označují variantu se samostatnou klíčovou třídou, neřeší. Bez toho nelze číst vstupy, které klíč vyjadřují klíčovou třídou — a analogicky pak `@EmbeddedId` na javové straně.

### Mrtvé stuby v builderech

Bezparametrické `protected override`metody nesou komentář `// unused in multi-entity flow` a jsou prázdné — čtyři v Dapper builderu, šest v EF Core a šest v NHibernate; Dapper navíc nechává `BuildPrimaryKey` a `BuildForeignKey` házet `NotImplementedException`. Buď z `Build()` udělat šablonovou metodu nad overloady s parametry, nebo stuby odstranit.

### NHibernate builder — schéma se nepropisuje do mapování

`BuildTableSchema` čte `em.Schema`, ale při prázdné hodnotě dosadí prázdný řetězec a dál s ním nepracuje (TODO v kódu). Mapování tak vzniká bez `schema` atributu i tam, kde ho zdroj nese, což u databází s víc schématy vyrobí mapování mířící do výchozího schématu.

### NHibernate builder — kolekce jen jako `<bag>`

Kolekční vlastnost se generuje natvrdo jako `<bag>` a ostatní kolekční tvary (`set`, `list`, `map`) ani další kolekční vlastnosti builder neřeší (dva TODO v kódu). Volba tvaru kolekce je v NHibernate sémantická — `set` vylučuje duplicity, `list` nese pořadí — takže dnešní stav mění chování, ne jen zápis.

### Frontend

Odloženo do cílenější přestavby, současný stav je funkční:

1. V `advisor-page.component.ts` přeskočit prázdné dotazové jednotky v `convert()` před odesláním — server je striktní záměrně, tolerance patří do UI.
2. V `main-page.component.ts` zobrazovat chyby ze serveru přes `err.error`, ne `err.message`; vzor je v `advisor-page.component.ts`.
3. Validace před odesláním podle S7, tedy chyby na úrovni souboru a řádku.

### Ověřit docker-compose

`ConnectionStrings__AdvisorDatabase` je commitnutá deklarace, kterou zatím nikdo nespustil. Ověří se při prvním reálném běhu Advisoru v Dockeru.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne:

| Blok | Co odblokuje |
|---|---|
| **F4–F6** metadata z databáze | `ColumnPairs`, detekci N:M v parserech, úplné mapování z neúplného vstupu |
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech, kontrolu úplnosti IR |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; stojí na neutralizaci typového modelu |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání |
| **E1–E7** experimenty | E7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání zůstává rozpracovaná: chybí NHibernate LINQ parser, Dapper SQL parser a query buildery pro EF Core i NHibernate.
