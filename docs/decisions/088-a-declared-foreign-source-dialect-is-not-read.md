# 088 — Deklarovaný cizí dialekt zdroje se nečte

Datum: 2026-09-21
Stav: platí
Požadavky: F5, F6, F7–F10, F11, F14, S1, S2, S6
Podklad: rozhodnutí [013](013-target-framework-versions.md), [014](014-language-type-model.md), [019](019-neutral-database-type-vocabulary.md), [026](026-home-of-shared-query-reading.md), [028](028-assembly-name-is-not-ours-to-invent.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [052](052-literal-sql-type-reaches-the-ef-core-annotation.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [082](082-t-sql-read-and-written-by-a-shared-project.md) a [086](086-target-database-dialect-declared-by-the-descriptor.md); [`architecture.md`](../architecture.md) §9, vyňatá oblast 5; otevřená položka „Druhý databázový dialekt"

## Kontext

Rozhodnutí 086 deklarovalo **cílový** databázový dialekt a samo vyslovilo, co neřeší: zdrojovou stranu. Doslovný typ v `[Column(TypeName=…)]`, v `columnDefinition` i v `sql-type` je napsaný v dialektu zdrojového projektu, o kterém nástroj nemá žádnou deklaraci, a `SqlTypeSpelling.Read` proto dialekt vůbec nebere — čte jediné SQL, které nástroj zná. Otevřená položka „Druhý databázový dialekt" z toho udělala jednu ze tří otázek, které musí zodpovědět ten, kdo do slovníku přidá druhou hodnotu.

**Tohle rozhodnutí bere z té položky druhou otázku a odpovídá jen na ni:** kde se dialekt zdroje bere a co se stane, když se rozejde s tím, který nástroj čte. Čím se volí **cílový** dialekt a jestli padá zamítnutí multidialektové knihovny, neřeší; obojí zůstává v položce a Důsledky říkají proč.

### Tři třídy cizího vstupu, dvě z nich viditelné

Věta rozhodnutí 082, že SQL psané pro jiný systém „touhle cestou neprojde", platí pro dvě třídy vstupu ze tří. Ta třetí je ta, kvůli které tohle rozhodnutí vzniká.

**(a) Syntaxe, kterou T-SQL nemá — dnes viditelná.** MyBatis mapper psaný pro MySQL:

```sql
SELECT name FROM customer LIMIT 10 OFFSET 20
SELECT first_name || ' ' || last_name FROM customer
```

`TSql160Parser` obojí odmítne a `SqlQueryReader` z toho udělá `Failure` s řádkem a sloupcem. Nic tichého se neděje.

**(b) Jméno, které T-SQL nezná — dnes viditelná.** Hibernate entita z Oracle projektu, `columnDefinition = "VARCHAR2(50)"` nebo `"NUMBER(10,2)"`, spadne v `SqlTypeSpelling.Read` do poslední větve: rodina chybí, doslovný pravopis jde na únikovou cestu a volající o tom vydá záznam (rozhodnutí 019 a 048). Výstup je chudší a nic nepředstírá.

**(c) Jméno legální v obou dialektech s jiným významem — dnes neviditelná.** Tabulka `SqlTypeSpelling.Read` je tabulka jmen, ne významů, takže jméno, které v obou systémech existuje, se přečte bez zaváhání a bez záznamu:

| Zdroj | Co to znamená v jeho systému | Jak to nástroj přečte |
|---|---|---|
| `timestamp` (PostgreSQL, Oracle, standard) | okamžik v čase | `VarBinary`, délka 8 — v T-SQL je `timestamp` synonymum `rowversion` |
| `money` (PostgreSQL) | pevná desetinná podle `lc_monetary` | `Decimal(19,4)`, pevný tvar T-SQL |
| `float` (MySQL) | jednoduchá přesnost | `DoublePrecision` — holý `float` je v T-SQL `float(53)` |

Táž třída existuje na dotazové straně, protože **gramatika filtruje syntaxi, ne slovník**. `SELECT SUBSTR(name, 1, 3) FROM customer` je pro `TSql160Parser` obyčejné volání funkce; čtečka si z něj vezme první argument jako sloupec, jméno funkce nese doslova dál a visitor cíle ho zase vypíše. `SUBSTR` není jméno té funkce v T-SQL a zbylé dva argumenty nikam nedošly.

### Třetí třída se nejen špatně přečte, ona se i vypíše

Rozhodnutí 052 dává doslovnému typu **zdroje** přednost před odvozeným a 086 to potvrdilo. Cizí doslovný typ tedy putuje na únikové cestě až do výstupu: `columnDefinition = "NUMBER(10,2)"` vyjde z NHibernate builderu jako `sql-type="NUMBER(10,2)"` v mapování, o kterém deskriptor tvrdí, že je psané pro SQL Server 2022. Takové mapování se postaví a spadne teprve při exportu nebo validaci schématu.

**Proti F11 to stojí hůř než vynechání.** Požadavek zní, že nepodporované konstrukce nesmějí být potichu vynechány. Tady se nic nevynechává: u třídy (b) se cizí jméno **opíše** do artefaktu psaného pro jiný systém, u třídy (c) se **přeloží na jinou typovou rodinu** — a artefakt o obojím mlčí. Tiché vynechání je méně škodlivé než tiché tvrzení.

### Z textu to poznat nelze

`timestamp` je platné T-SQL. Neexistuje analýza vstupu, která by rozhodla, jestli ho autor myslel jako `rowversion`, nebo jako okamžik v čase: obě čtení jsou syntakticky i typově v pořádku a liší se jedině tím, pro jaký systém byl projekt psaný. Jediný, kdo tu odpověď má, je autor vstupu. Deklarace je proto nevyhnutelná a otázka zní jedině, **co ta deklarace řídí**.

### Co to znamená pro měření, ne jen pro nasazení

Nástroj není produkční překladač; je to nástroj, jehož výstupy se měří v zafixovaném prostředí (rozhodnutí 013, §9, požadavky T). Tichý přečet třídy (c) proto nezkresluje nasazení, nýbrž **měření**: `timestamp` přečtený jako `VarBinary(8)` projde všemi čtyřmi stupni ověření rozhodnutí [016](016-generated-artifact-verification-levels.md) — přeloží se, framework ho přijme, proti databázi poběží — a v matici T2 z toho vznikne zelená buňka pro překlad, který správný není. Odstranit to ticho je tedy zájem experimentu, ne pohodlí uživatele.

## Zvažované varianty

### 1 — Nechat stav, jak je

Zdroj nic nedeklaruje, všechno se čte jako T-SQL, třída (c) zůstává tichá.

Zamítáme. Do rozhodnutí 086 to byla obhajitelná pozice, protože nástroj nedeklaroval dialekt ani na jedné straně a mlčel souměrně. Od 086 tvrdí dialekt o cíli a mlčí o zdroji, což je ze tří možných pozic ta nejméně konzistentní: záznam běhu jmenuje systém, pro který se píše, a nemá co říct o systému, ze kterého se četlo. K tomu stojí proti F11 ne mlčením o vynechání, nýbrž tichým tvrzením — a proti měření tak, jak popisuje kontext.

### 2 — Deklarace jako nálepka

Zdroj svůj dialekt vysloví, čte se dál všechno stejně a výsledek dostane poznámku „zdroj byl PostgreSQL".

Zamítáme týmž důvodem, kterým 086 zamítlo svou variantu 1: **pole, které nic nevybírá, netvrdí nic.** Nálepka nezmění, co se přečetlo — `timestamp` pořád vyjde jako `VarBinary(8)` —, takže výstup je pořád špatně a nově je označkovaný. V jednom ohledu je to horší než mlčení: nálepka tvrdí, že nástroj o zdrojovém dialektu věděl, a zve tím k důvěře ve výsledek, který se nezměnil.

### 3 — Deklarace jako přepínač čtení

Deklarovaný dialekt vybírá gramatiku a tabulku jmen, tedy multidialektová knihovna rozhodnutí 082 s deklarací navrch.

Zamítáme, a ne navždy. Argument, kterým ji 082 zamítlo — že jiný dialekt by se přeložil tiše —, deklarací skutečně padá, jak přiznává i otevřená položka. Cena ale zůstává celá: druhá gramatika, druhá tabulka jmen a ověření, proti kterému nemá co stát, dokud v repozitáři není druhý databázový systém. A hlavně: touhle variantou se otevírá otázka, kterou tohle rozhodnutí nebere — co nástroj dělá, když se dialekt zdroje **rozejde** s cílovým, tedy skutečný překlad dialektu, ne jeho čtení. To je třetí otázka otevřené položky a patří k ní, ne sem.

### 4 — Tabulka kolizních jmen bez deklarace

Vyjmenovat jména, která v T-SQL a v ostatních rozšířených systémech znamenají různé věci (`timestamp`, `money`, `float`, …), a u každého vydat záznam.

Zamítáme třemi důvody. Pokrývá jména, ne syntaxi a ne slovník funkcí, takže `SUBSTR` by dál prošlo beze slova. Hlásilo by se u **každého** legitimního T-SQL vstupu, který ta jména použije — tedy u vstupu, o kterém není co říct —, a to je přesně záznam popisující formát místo nálezu o vstupu, jak ho zamítá úvaha rozhodnutí 028. A tabulka sama by byla tvrzením o systémech, které nástroj nečte, tedy tvrzením, které nemá nikdo proti čemu držet.

### 5 — Deklarace jako zábrana

Zdroj deklaruje, v dialektu kterého systému je jeho doslovné SQL napsané, a deklarace jiného systému, než který tahle verze čte, čtení toho SQL **zastaví**.

## Rozhodnutí

**Volíme variantu 5. Zdroj smí deklarovat dialekt svého doslovného SQL, a deklarace jiného systému, než který tahle verze čte, čtení zastaví: dotaz se nevydá a doslovný typ sloupce se nepřečte. Nepřekládá se nic, co by se muselo dopředu uhodnout, a nečte se nic, o čem zdroj řekl, že to naší gramatikou není.**

### Co se deklaruje

`Model` dostává výčet `SourceSqlDialect` se dvěma hodnotami:

```csharp
public enum SourceSqlDialect
{
    SqlServer2022 = 10,
    AnotherSystem = 1000,
}
```

**Není to `DatabaseDialect` a nesmí to být on.** Hodnota `DatabaseDialect` vybírá tabulku pravopisu a každá jeho hodnota musí být zapsatelná; `PostgreSql` v tom výčtu by udělalo vyjádřitelným `TargetFrameworkDescriptor.Dialect = PostgreSql` a `SqlTypeSpelling.Name` by na něj za běhu vyhodil výjimku. Test, který drží, že hodnota toho slovníku je právě jedna, je přesně ta čára a zůstává, jak je.

**Není to ani seznam jmen systémů.** Nástroj se ke všem nečteným systémům chová **totožně** — nečte je —, takže jméno by nevybíralo nic, a to je důvod, kterým 086 zamítlo volný řetězec i prázdnou deklaraci. Slovník jmen systémů, které nečteme, je navíc slib, který nedržíme: na hodnotu `Oracle19c` je rozumná otázka „takže čtete Oracle?" a odpověď je ne. Diagnostická ztráta je malá a vyváží ji přesnost: záznam neřekne „zdroj byl Oracle", řekne „zdroj tvrdí, že jeho SQL není to, které tahle verze čte", což je celý obsah toho, co nástroj ví.

**Dvě hodnoty jsou tvar pro dnešek, který přežije zítřek.** Až bude čitelných dialektů víc — třetí otázka otevřené položky —, rozpadne se `AnotherSystem` na pojmenované hodnoty a **totéž pole se ze zábrany promění v přepínač**. Neroste tvar, roste slovník; `1000` stojí mimo řadu desítek právě proto, aby se mezi pojmenované systémy dalo zařazovat bez přečíslování.

### Kde se deklarace vysloví

V požadavku převodu, jednou za převod, vedle zdrojového frameworku: `ConvertRequest` dostává nepovinné pole, `ConversionHandler.Convert` nepovinný parametr.

**Ne v deskriptoru.** Deskriptor popisuje cílový framework; dialekt zdroje je fakt o vstupu, o kterém žádný deskriptor nic neví. Souměrnost s 086 je tím úplná: cílový dialekt deklaruje ten, kdo píše, zdrojový ten, kdo předkládá.

**Ne na jednotce.** Dialekt je vlastnost databáze, se kterou zdrojový projekt mluví, ne vlastnost souboru. Pole na jednotce by po klientovi chtělo klasifikaci, kterou typicky nemá — kdo neví, že `timestamp` znamená v každém systému něco jiného, netuší ani, které ze svých jednotek má označit —, a otevřelo by tvar „půl projektu je Oracle", o kterém nástroj neumí říct nic užitečného. Smíšený vstup má proto jedinou odpověď: rozdělit ho na dva požadavky. Je to mez a je vyslovená.

### Nevyslovený dialekt je dnešní chování

`null` znamená, že zdroj netvrdí nic, a nástroj čte přesně jako dnes. Tři důvody:

- **Nevyslovené není tvrzení.** Totéž pravidlo, kterým NHibernate parser čte `not-null` trojstavově: nepřítomný atribut netvrdí ani jedno, ani druhé (rozhodnutí 067).
- **Opak by odmítl všechno.** „Nevysloveno = cizí" by zneplatnilo každý dnešní vstup včetně vzorků a obou testových sad.
- **A poznámka o tom by nebyla nálezem.** Vydat u každého běhu záznam „četli jsme to jako T-SQL, ačkoli to nikdo netvrdil" je popis formátu, ne nález o vstupu (rozhodnutí 028). Mez se vyslovuje jednou, v §9 a v deskriptoru.

**Zábrana proto chrání jen toho, kdo ji vysloví,** a je to vědomá cena varianty, ne opomenutí. Co se ale mění i pro nevyslovený dialekt, je **viditelnost**: záznam běhu nese `DeclaredSourceDialect` vedle `TargetDatabaseDialect` (S6), takže z výsledku je poznat rozdíl mezi „zdroj řekl T-SQL" a „nikdo neřekl nic". Ten rozdíl dnes neexistuje a pro měření je to přesně ten údaj o původu, který u zelené buňky chybí.

### Co zábrana zastaví

| Kde zdroj vyslovuje SQL databázového systému | Dnes | Při `AnotherSystem` |
|---|---|---|
| jednotka `SqlQuery`, literál v `Query<T>` (Dapper) | čte `SqlQueryReader` jako T-SQL | `Failure`, artefakt dotazu nevzniká |
| `<sql-query>` v `hbm.xml` (NHibernate) | totéž | totéž |
| `<select>` a spol., anotační forma, fragmenty `<sql>` (MyBatis) | totéž | totéž |
| `[Column(TypeName=…)]` (EF Core) | `SqlTypeSpelling.Read` + doslovný typ do cíle (052) | nečte se; `Loss` kategorie `DatabaseType` |
| `columnDefinition` (Hibernate, EclipseLink) | totéž | totéž |
| `sql-type` na `<column>` (NHibernate) | doslovně na únikovou cestu a do cíle | totéž |

A co **nezastaví**, protože to dialekt databázového systému není:

| | Proč |
|---|---|
| LINQ (EF Core, NHibernate) | jazyk .NET, ne systému; provider z něj SQL teprve vyrábí |
| HQL, JPQL | jazyky frameworku; tytéž nad každým systémem |
| `jdbcType` MyBatisu | jmenuje rodinu JDBC, ne typ jednoho systému (086) |
| doplnění z katalogu | čte připojenou databázi, ne zdrojový artefakt (015) |

Hranice je úzká a schválně: zábrana se týká **doslovného SQL napsaného autorem zdroje**, tedy dvou věcí — textu dotazu a názvu typu sloupce. Rozšířit ji na dotazové jazyky frameworků by znamenalo odmítat vstup, který na dialektu nezávisí.

### Proč je dotaz `Failure` a doslovný typ `Loss`

Rozdíl není libovůle, je to pravidlo, které repozitář už má.

**Dotaz, který se nepřečte, se nevydá** (rozhodnutí 053 a 070). Artefakt bez svého filtru nebo bez svého joinu vrací jinou množinu řádků, a to je horší výsledek než žádný artefakt. Cizí dotaz nemá čím být nahrazen — chudší tvar téhož dotazu neexistuje —, takže je to `Failure` a artefakt nevzniká. Je to týž kanál, jakým dnes odchází syntaktická chyba `TSql160Parser`u; mění se jedině důvod.

**Doslovný typ sloupce, který se nepřečte, čím nahradit má.** Cíl si typ sloupce odvodí z jazykového typu sám (rozhodnutí 014) a mapování drží dál: výstup je **chudší**, ne jiný, a chudší výstup se hlásí ztrátou (rozhodnutí 048). Entita se tedy přeloží celá, jen o jedno tvrzení míň, a záznam kategorie `DatabaseType` řekne které a proč. Rodina se z cizího názvu nesmí číst **ani jako odhad**: rodina je právě to, co je u třídy (c) špatně.

**U třídy (b) je to zároveň zlepšení proti dnešku.** Dnes doputuje cizí `VARCHAR2(50)` po únikové cestě až do cílového artefaktu (rozhodnutí 052); se zábranou se zahodí, takže z mapování zmizí jméno typu, které cílový systém nezná. Rozhodnutí 052 se tím neruší — jeho pravidlo platí pro doslovný typ, který **nástroj přečetl jako svůj**; typ, o kterém zdroj řekl, že náš není, do jeho rozsahu nepatřil nikdy.

### Kam zábrana v kódu patří

Míst, kde nástroj čte doslovné SQL zdroje, je **dvě**, a obě jsou od rozhodnutí 082 a 086 jednodomá:

- `SqlQueryReader` v `TransactSql` — jediná gramatika T-SQL v řešení, kterou skládají tři wrappery (Dapper, NHibernate pro `<sql-query>`, MyBatis).
- `SqlTypeSpelling.Read` v `Common.Sql` — jediné čtení doslovného názvu typu; volá ho `EFCoreWrappers.DatabaseTypeConvertor` a `JakartaPersistence.JpaMappingWriter`. Třetí místo název nečte, jen ho nese dál: `NHibernateXMLMappingParser` předává `sql-type` na únikovou cestu beze změny, takže tam je zábrana jediná podmínka před `SetPropertyDatabaseType`.

**Zábrana patří k nim, ne do wrapperů, a to je věc S1:** přidání sedmého frameworku nesmí znamenat sedmou kopii pravidla. Deklarace proto teče od orchestrace k parserům jako vstup jejich konstrukce (`ParserFactory.Create`) a čtou ji jen ti, kdo doslovné SQL čtou. **Na entitní builder se nevěší**, ačkoli ho každý parser drží a ačkoli by to byla nejkratší cesta: builder patří cílovému frameworku a parser zdrojovému (S1), a fakt o zdroji putující přes cílový objekt by tu hranici obrátil.

### Co se nemění

`DatabaseDialect` ani jeho jediná hodnota, deskriptory, typový model rozhodnutí 019, dotazová mezireprezentace, rozhraní parserů a builderů ani fáze doplnění z katalogu. Orchestrace se mění o jeden nepovinný parametr a o nic víc: **který dialekt zdroj vyslovil, nerozhoduje o ničem jiném než o tom, jestli se doslovné SQL čte.**

## Důsledky

### Co se zablokuje

Deklarace `AnotherSystem` je tvrzení s cenou a ta cena je nerovnoměrná. Ze šesti zdrojových frameworků nesou tři doslovné SQL v dotazech a čtyři vyslovují doslovný typ sloupce:

- **Dapper.** Všechny dotazové jednotky se odmítnou; entity projdou beze změny, protože entitní parser Dapperu čte prosté třídy C# a žádné SQL v nich není. Oracle projekt nad Dapperem tedy přeloží entity a přijde o celou dotazovou půlku.
- **MyBatis.** Bije to tady nejvíc, a to ze dvou důvodů: celý dotazový povrch MyBatisu je SQL systému a projekt nad MySQL nebo Oracle je u něj běžný případ, ne kuriozita. Mapper je přitom dvojakou jednotkou (rozhodnutí 081), takže `<resultMap>` se přečte a každý příkaz se odmítne — z jednoho dokumentu vyjde mapování bez dotazů.
- **NHibernate.** `<sql-query>` se odmítne, `<query>` s HQL ne. Mapovací půlka `hbm.xml` se čte dál.
- **EF Core, Hibernate, EclipseLink.** Entity se přeloží celé; zmizí z nich doslovný typ sloupce a na jeho místě je záznam o ztrátě. Typ si cíl odvodí z jazykového typu, případně ho doplní katalog (F6).

Souhrnně: **deklarovaně cizí projekt stojí svou dotazovou půlku u Dapperu a MyBatisu a jedno tvrzení na vlastnost u ostatních.** To je poctivá cena za to, že se cizí SQL nepřekládá — a je to výsledek, který se dá přečíst z diagnostiky, na rozdíl od dnešního.

### Na co se při implementaci narazí

Stojí za to je vyjmenovat předem, protože žádný z nich není v samotném pravidle vidět.

1. **Průchod deklarace k parserům.** Fakt musí dojít na dvě čtecí místa přes šest konstrukčních míst ve wrapperech. Nejkratší cesta — pověsit ho na `AbstractEntityBuilder`, který každý parser stejně drží — je ta, která poruší S1; skutečná cesta vede `ParserFactory.Create` a konstruktory těch parserů, které SQL čtou. Vzor už existuje: `SqlQueryReader` bere nepovinné `SqlParameterFacts` jako „fakta, která zdroj o dotazu vyslovil" (rozhodnutí [084](084-mybatis-wrapper-over-the-shared-sql-reading.md)), a dialekt je fakt téhož druhu.
2. **Dvojaká jednotka.** Odmítnutí musí padnout na dotazové půlce a nechat mapovací naživu. Napsané jako odmítnutí **jednotky** by tiše zahodilo mapování MyBatis mapperu i `hbm.xml`. Odsud plyne i to, že zábrana sedí v čtečce, ne v orchestraci: orchestrace vidí jen typ obsahu a ten u dvojaké jednotky nic nerozlišuje. Účetnictví rozhodnutí 066 a 081 přitom musí zůstat celé — jednotka **byla** nárokovaná a na entitním průchodu **něco vydala**, takže se nesmí objevit mezi nenárokovanými ani mezi jalovými.
3. **Rozhraní to musí umět vyslovit.** Pole, které se dá poslat jedině na API a obrazovky rozhodnutí [033](033-shape-of-the-static-frontend-screens.md) ho nastavit neumějí, je přesně ta nálepka, kvůli které jsme zamítli variantu 2 — a proti F14 i S7. Frontend je ruční, bez build kroku (rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md)), takže je to jeden ovládací prvek a jedno pole v požadavku: málo práce, ale ne volitelné.
4. **Katalog zábrana neřeší a nesmí to předstírat.** `SqlServerCatalogReader` čte SQL Server z principu a připojení je fakt konfigurace serveru, ne požadavku (rozhodnutí [029](029-database-connection-is-the-consumer-projects-fact.md) a [030](030-scope-of-version-1-0.md)), takže nástroj nemá jak zjistit, na jaký systém připojení míří. Deklarovaně cizí zdroj s připojeným katalogem tedy znamená fakta ze SQL Serveru o projektu psaném pro něco jiného. Může to být legitimní (migrovaná databáze), může to být omyl, a nástroj ty dva případy nerozliší. Zůstává to otevřené a patří to do `open-items.md`, ne sem.
5. **Kategorie záznamů.** Odmítnutí dotazu není vlastností dotazu, takže jde bez kategorie — tvar rozhodnutí 048. Ztráta doslovného typu kategorii `DatabaseType` má, protože fakt, který nedošel, je databázově typový a kategorie je to, podle čeho ho konzument najde. Obojí je součást testovaného povrchu, proto se to zapisuje sem.
6. **Ani jeden dnešní výstup se nesmí změnit.** Nic ve `SampleData` ani v obou testových sadách dialekt nedeklaruje, takže celá matice musí zůstat bajtově táž; to je definice toho, že je pole přírůstkem. Testy zábrany jsou nové vstupy, ne úpravy starých.
7. **Pokušení rozšířit hranici.** Zábrana se týká doslovného SQL, ne dotazových jazyků frameworků. Rozšířit ji na LINQ, HQL nebo JPQL — „vždyť je to taky dotaz" — by odmítalo vstup, který na dialektu nezávisí, a zúžilo by matici F10 bez jediného důvodu. Hranici drží tabulka výše.

### Co tím zodpovězené není

Otevřená položka „Druhý databázový dialekt" přichází o svou **druhou** otázku a obě zbývající drží dál: čím se volí cílový dialekt (deskriptor dnes nese jednu hodnotu a volba z rozhraní neexistuje — táž otevřená věta jako u verze frameworku, rozhodnutí 013) a jestli padá zamítnutí multidialektové knihovny. Položka k tomu dostává **čtvrtou** větu, kterou dřív nešlo vyslovit: až bude čitelných dialektů víc, promění se totéž pole ze zábrany v přepínač a teprve tehdy vznikne skutečná otázka, co dělat, když se dialekt zdroje s cílovým rozejde. Dnes vzniknout nemůže — čitelný dialekt je jeden a zapisovatelný taky.

### Ostatní

**REST kontrakt** dostává nepovinné pole v požadavku a nové pole v odpovědi, obojí přírůstkem, takže podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to **MINOR**.

**S2 se nemění.** Odmítnutí je deterministické: týž vstup s touž deklarací odmítne stejně, a bez deklarace se nemění vůbec nic.

**Hranice záruk se nezmenší, ale ztichne uvnitř.** Vyňatá oblast 5 říká dál, že dialekt je jeden; nově ale platí souměrně na obou stranách — cíl svůj dialekt deklaruje a zdroj ho deklarovat smí, přičemž cizí se odmítne místo uhodnutí. Oblast se tím nezužuje, mizí z ní ticho.

**Testy.** Že deklarovaně cizí zdroj nevydá ani jeden dotazový artefakt a že záznam jmenuje důvod — u všech tří frameworků, které SQL čtou. Že u dvojaké jednotky MyBatisu a u `hbm.xml` mapovací půlka projde a jednotka se neobjeví mezi nenárokovanými ani jalovými. Že doslovný typ sloupce u čtyř cílů, které ho vyslovují, ve výstupu není a nahradil ho záznam o ztrátě, kdežto zbytek mapování je beze změny. Že tentýž vstup s deklarací `SqlServer2022` a bez deklarace dá **bajtově týž výstup** jako dnes — to je test, který drží, že je pole přírůstkem. A že `DeclaredSourceDialect` v záznamu běhu rozlišuje vyslovené od nevysloveného.
