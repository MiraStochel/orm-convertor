# Audit stavu po kroku 4: nálezy, opravy a dopady na návrh

Dokument shrnuje výsledky auditu provedeného po dokončení kroků 1–4 z design docu 001, po zafixování verzí frameworků a po analýze tří .NET ORM frameworků. Slouží jako pracovní plán dalšího postupu.

Struktura: nejdřív zjištění, pak dopady rozšíření na Javu, pak konkrétní seznam oprav, pak návazné úpravy návrhu a nakonec přehled dřívějších položek, které dosud nebyly zapracované.

## Zafixované verze

Veškerá tvrzení v dokumentu platí pro tyto verze. Ověřeno průchodem všech 35 projektů: každý balíček se v repozitáři vyskytuje právě v jedné verzi.

| Komponenta | Verze |
|---|---|
| .NET | 10 (`net10.0`) |
| NHibernate | 5.7.0 |
| Microsoft.EntityFrameworkCore(.SqlServer) | 10.0.10 |
| Dapper | 2.1.79 |
| Microsoft.Data.SqlClient | 7.0.2 |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | 5.6.0 |
| SQL Server | 2022 |

Původní verze v upstreamu měl pět balíčků roztříštěných; upgrade na .NET 10 to srovnal. Verze `System.Data.SqlClient` 4.9.1 v `benchmarks/Common` je jediná výjimka a je nosná, viz nález 3.4.3.

---

## 1. Kritický nález: výstup pro NHibernate s kompozitním klíčem není použitelný

### Co se generuje

`NHibernateEntityBuilder` vyprodukuje pro entitu s kompozitním klíčem korektní XML:

```xml
<composite-id>
  <key-property name="OrderID" column="OrderID" type="Int32" />
  <key-property name="CompanyID" column="CompanyID" type="Int32" />
</composite-id>
```

ale C# třídu bez override `Equals`, bez `GetHashCode` a bez atributu `[Serializable]`.

### Proč to selže

Referenční dokumentace NHibernate u `<composite-id>` požaduje, aby persistentní třída přepsala `Equals()` a `GetHashCode()` kvůli implementaci rovnosti kompozitního identifikátoru, a aby byla označená atributem `Serializable`. Důvod je věcný: v této podobě se persistentní objekt stává svým vlastním identifikátorem, takže NHibernate nemá jak rozhodnout rovnost jinak než přes `Equals`.

Chyba nastane při kompilaci mapování, tedy při stavbě session factory:

```
NHibernate.MappingException: Could not compile the mapping document
  ---> composite-id class must override Equals(): <plné jméno třídy>
```

Po doplnění `Equals` následuje tatáž chyba pro `GetHashCode()`. Není to tichá degradace ani chyba za běhu dotazu — je to tvrdý pád při startu.

### Proč to neodhalily testy

`Tests/Combined/CompositeKeyTest.cs` ověřuje u NHibernate větve pouze XML: přítomnost `<composite-id>`, obou `<key-property>`, jejich pořadí a nepřítomnost `<generator>`. Generovaný C# kód pro NHibernate netestuje. U EF Core větve přitom test kód kontroluje — asertuje `[PrimaryKey(...)]` a nepřítomnost `[Key]`.

Testy tedy ověřují tvar výstupu, ne jeho použitelnost. U EF Core to stačí, protože atribut je jediný požadovaný artefakt. U NHibernate ne, protože mapování klade požadavky i na třídu, kterou popisuje.

### Rozsah

Dotčená je každá cesta X → NHibernate, kde entita nese kompozitní klíč: EF Core → NHibernate i identitní NHibernate → NHibernate. Z Dapperu to nastat nemůže, protože jeho parser klíče nevytváří vůbec.

Podstatnější je dopad dopředu: junction entita pro N:M nese typicky přesně kompozitní klíč. Krok 5a má podle design docu 002 generovat junction entitu explicitně, takže po jeho dokončení spadne pod tuto chybu **každý překlad N:M do NHibernate**. Není to tedy jen doplatek na krok 3b, ale blokátor kroku 5a.

### Proč je to díra v návrhu, ne jen v implementaci

Design doc 001 §3 řeší pořadí částí klíče (§3.2) a strategii generování per-part (§3.4). Slovo `Equals` v něm není. Návrh modeluje kompozitní klíč jako strukturu — seřazený seznam sloupců — ale nezachytil, že u NHibernate a JPA nese kompozitní klíč navíc **sémantiku identity**, která se propisuje do tvaru třídy, ne jen do mapování. Implementace kroku 3b naplnila návrh přesně; chyba je v tom, co návrh neřekl.

---

## 2. Nálezy v typovém modelu

### 2.1 Slévání typů data a času je závislé na verzi a dialektu

`NHibernateWrappers/Convertors/DatabaseTypeConvertor.cs` mapuje tři různé IR typy na jeden typ NHibernate:

```
DatabaseType.DateTime      => "DateTime"
DatabaseType.DateTime2     => "DateTime"
DatabaseType.SmallDateTime => "DateTime"
```

a zpětně `"datetime"` i `"datetime2"` na `DatabaseType.DateTime2`.

Na NHibernate 5.7.0 s `MsSql2012Dialect` produkuje typ `DateTime` sloupec `datetime2`; dialekty pro SQL Server 2008+ ho používají od NHibernate 5.0.0 a typ `DateTime2` je od téže verze označený jako obsoletní ve prospěch `DateTime`. Důsledky:

- `DatabaseType.DateTime` znamená v IR `datetime`, ale výstup vyrobí `datetime2` — tiché rozšíření typu
- `DatabaseType.SmallDateTime` skončí rovněž jako `datetime2` a ztratí minutovou přesnost i rozsah
- round-trip NHibernate → IR → NHibernate převede `DateTime` na `DateTime2`

Na starší verzi NHibernate nebo se zapnutým `sql_types.keep_datetime` by se to chovalo jinak. Správnost té tabulky je tedy vázaná na verzi a dialekt a nikde to není zapsané. Rozlišení by šlo udržet přes `sql-type` na `<column>`, což builder nedělá.

### 2.2 `CLRType` je užší než `DatabaseType`

`DatabaseType` má 30 hodnot, `CLRType` třináct. V `CLRType` chybí `Guid`, `byte[]`, `TimeSpan`, `DateTimeOffset`, `short`, `DateOnly` a `TimeOnly`. `GuessFromPropertyType` na cokoli mimo seznam vyhodí `NotImplementedException`.

Zafixování verzí sem přineslo konkrétní případ: NHibernate 5.7.0 nově podporuje `DateOnly` a `TimeOnly`, EF Core 10 je podporuje také. Vznikla tedy situace, kdy oba cílové frameworky umí typ, který IR neumí reprezentovat. Dosud byla asymetrie vždy opačným směrem.

### 2.3 `DatabaseType` není databázově neutrální

Tento nález dosud nebyl nikde zaznamenaný. `DatabaseType` sice vypadá jako obecný seznam SQL typů, ale ve skutečnosti je to **seznam typů T-SQL**: `NVarChar`, `NText`, `UniqueIdentifier`, `Money`, `SmallMoney`, `SqlVariant`, `RowVersion`, `Image`. Většina z nich v PostgreSQL, MySQL ani Oracle neexistuje.

IR tedy nemá jen jazykově specifický typový model (`CLRType`), ale i databázově specifický (`DatabaseType`). JSS článek adresuje jen první z nich, konceptem `LangType`. Druhý zatím adresovaný není.

### 2.4 Chybné položky v převodní tabulce typů

`NHibernateWrappers/Convertors/DatabaseTypeConvertor.cs` obsahuje tři vady, které mění výsledný SQL typ. Všechny jsou překlepy v převodní tabulce, ne koncepční problém.

**`CLRType.Byte` míří na `DatabaseType.Int`.** V `GuessFromPropertyType` je `CLRType.Byte => ToNHibernate(DatabaseType.Int)`, tedy `"Int32"`. Přitom `DatabaseType.TinyInt` existuje, mapuje se na `"Byte"` a zpětný převod `"byte"` vrací `TinyInt` — chybí jen tento směr. Vlastnost typu `byte` tak dostane typ `Int32`.

**`CLRType.Float` míří na `DatabaseType.Float`.** Výsledkem je typ `"Double"`. Je to past v pojmenování SQL typů: `DatabaseType.Float` je osmibajtový SQL `float`, tedy protějšek C# `double`; čtyřbajtovému C# `float` odpovídá `DatabaseType.Real`, který se správně mapuje na `"Single"`. Opačný směr to má v `FromNHibernate` správně (`"single" or "float" => DatabaseType.Real`).

**Případ `"datetimenoMs"` je nedosažitelný.** `FromNHibernate` provádí `switch` nad `type.Trim().ToLowerInvariant()`, ale tato větev má velké `M`. Nemůže tedy nikdy zabrat a typ `DateTimeNoMs` propadne do `NotImplementedException`. Ostatní větve casing dodržují.

Čtvrtý případ stejné kategorie je v tomto souboru rovněž, ale opravit ho stejným způsobem nelze: `CLRType.Char` míří na `DatabaseType.Char`, což dá typ `"AnsiStringFixedLength"`. Podle referenční tabulky NHibernate je ale výchozím typem pro `System.Char` typ `Char` (DbType.StringFixedLength) a `AnsiChar` se musí uvést explicitně. C# `char` tedy končí jako neunicode `char(1)` místo `nchar(1)`. Přesměrovat ho na jinou hodnotu `DatabaseType` nejde, protože žádná z nich typ `"Char"` nevrací — `DatabaseType.NChar` dává `"StringFixedLength"`, což je typ pro řetězec pevné délky, ne pro jeden znak. Oprava proto vyžaduje rozšíření typového modelu a spadá pod Ú2, ne pod O3.

---

## 3. Drobnější nálezy

### 3.1 Invariant `Order` na částech klíče není vynucený

`AbstractEntityBuilder.AddPrimaryKey` řadí části podle `Order` při konstrukci, ale je to jediné místo, kde invariant platí. Buildery pak iterují `PrimaryKey.Parts` a spoléhají, že je seznam setříděný. `SampleData/CustomerSampleNHibernate.cs:134` a `SampleData/CustomerSampleEFCore.cs:138` ale konstruují `PrimaryKey` přímo přiřazením, mimo `AddPrimaryKey`, takže tam invariant neplatí.

Dnes to nevadí, protože jde o jednoprvkový klíč. Design doc 001 §3.2 ale argumentuje, že `Order` má být první-třídní hodnota, a s kompozitními klíči z kroku 5a se seznam stane vícepoložkovým. Invariant patří do typu `PrimaryKey`, ne do jedné z cest, které ho konstruují — pak bude platit i pro přímé přiřazení a buildery mohou iterovat seznam bez vlastního řazení. Přepsat `SampleData` na `AddPrimaryKey` není alternativa: konstruuje `EntityMap` přímo a žádný builder po ruce nemá.

S tím souvisí otázka, kterou §3.2 neuzavírá: zda má být `Order` unikátní a souvislý od jedničky. Duplicitní hodnoty dnes k nedeterminismu nevedou, protože `OrderBy` je v LINQ stabilní a zachová pořadí vstupu, ale výsledné pořadí pak není dané modelem. Validace duplicit proto není součástí vynucení invariantu a patří k Ú1.

### 3.2 Parser NHibernate neumí variantu s klíčovou třídou

`NHibernateXMLMappingParser` čte `<composite-id>` tak, že projde `<key-property>` a jména mapuje na vlastnosti entity. Atributy `name` a `class` na `<composite-id>`, které označují variantu se samostatnou klíčovou třídou, neřeší vůbec. Vstup s touto variantou by tedy parser zpracoval nesprávně nebo by na něm selhal.

### 3.3 Cílové verze ORM nejsou nikde deklarované

Wrappery v `ORMConvertor` nereferencují žádný ORM balíček, pouze Roslyn. Cílové verze frameworků tedy nejsou nikde uvedené a existuje jen implicitní předpoklad o cílové syntaxi. Konkrétně:

- `EFCoreEntityBuilder` generuje `[PrimaryKey]`, což vyžaduje EF Core 7 a vyšší; jediná zmínka je komentář v `EFCoreEntityParser.cs:119`
- `NHibernateEntityBuilder` generuje `urn:nhibernate-mapping-2.2`

Požadavek S6 přitom vyžaduje verze frameworků zaznamenávat ve strojově čitelném záznamu překladu. Dnes není co zaznamenat.

### 3.4 Verzní hygiena repozitáře

1. Neexistuje `Directory.Packages.props` ani `global.json`. Sjednocení verzí je udržované ručně a může se nepozorovaně rozejít.
2. V `benchmarks/ORMComparison.sln` není jednotný ADO.NET provider. Dapper, EF Core, linq2db a RepoDB jedou na `Microsoft.Data.SqlClient` 7.0.2, ale NHibernate (přes `db.Driver<SqlClientDriver>()`) a EF6 jedou na `System.Data.SqlClient` 4.9.1, který k nim teče přes `benchmarks/Common`. Reference v `Common` je tedy nosná, ne mrtvá. U PetaPoco není provider dohledaný. Pro srovnání expresivity to nevadí, pro měření výkonu je to metodologický confound.
3. `AdvisorBenchmarking` referencuje Dapper a EF Core, ale ne NHibernate. Není ověřeno, zda je to záměr.

### 3.5 Dokumentace nepokrývá aktuální stav

1. Z dvojice `current-state.md` + `changelog.md`, která má podle konvence tvořit aktuální stav, **nelze zjistit, že design doc 002 existuje**. `current-state.md` je zmražený snímek a zmiňuje jen 001; changelog má jediný záznam, který na 002 neodkazuje.
2. Chybí záznam v changelogu o normalizaci konců řádků (commit `abb777f`, 27 souborů, kořenový `.gitattributes`).

---

## 4. Co plyne z rozšíření na Javu

Analýza JPA mění závěry, ke kterým by se dalo dojít z pohledu na dva .NET frameworky.

### 4.1 Klíčová třída není u JPA volitelná

JPA nabízí pro kompozitní klíče dvě možnosti, `@IdClass` a `@EmbeddedId`. **Obě vyžadují samostatnou třídu** reprezentující klíč; poskytovatel perzistence a implementace cache ji interně používají k identifikaci objektu entity. Třída musí být public, mít bezparametrický konstruktor, definovat `equals()` a `hashCode()` a být `Serializable`.

Bezklíčová varianta, jakou má NHibernate ve formě vnořeného identifikátoru, v JPA neexistuje. Klíčová třída tedy není artefakt vykreslení, který si builder případně syntetizuje — pro Javu je nevyhnutelná.

### 4.2 Volba formy zasahuje do dotazů

U `@IdClass` se sloupce uvádějí dvakrát, v ID třídě i v entitě; u `@EmbeddedId` ne. Tyto odlišné struktury ovlivňují psané JPQL dotazy: `@IdClass` dává `o.orderId`, `@EmbeddedId` dává `o.id.orderId`.

Rozhodnutí o kompozitním klíči tedy není lokální pro entitní větev — sahá do dotazové větve, která je celá ještě před námi.

### 4.3 `@EmbeddedId` nedokáže vyjádřit generování hodnot

`@GeneratedValue` lze použít jen na atributu anotovaném `@Id`. U `@EmbeddedId` není žádná část klíče anotovaná `@Id`, takže kompozitní klíče s generovanými hodnotami podporuje jedině mapování přes `@IdClass`.

Design doc 001 §3.4 přitom rozhodl, že model nese strategii generování **per-part**. `@EmbeddedId` tuto strategii nedokáže vyjádřit vůbec. Volba `@IdClass` jako výchozí formy tedy není preference, ale je vynucená dřívějším rozhodnutím.

### 4.4 Opakující se kategorie: členy vynucené frameworkem

Nález z kapitoly 1 není ojedinělý překlep, ale instance opakující se kategorie: **framework vyžaduje boilerplate, který není faktem o doméně**. Příklady napříč cíli:

- NHibernate: `virtual` na mapovaných členech, bezparametrický konstruktor, nefinální třída, u kompozitního klíče `Equals`/`GetHashCode`/`[Serializable]`
- JPA: bezparametrický konstruktor, nefinální třída, u kompozitního klíče celá ID třída s `equals`/`hashCode`/`Serializable`
- EF Core: nic z toho, dokud se nezapnou lazy-loading proxy

Dnes to buildery řeší ad hoc — `virtual` je zadrátovaný v `BuildPropertySignature`. Při pěti a více cílových frameworcích to potřebuje být pojmenovaný koncept, jinak si ho každý nový builder objeví znovu a na něco zapomene, přesně jako se stalo u kroku 3b.

### 4.5 Typový model je předpokladem, ne přípravou

Aby šla vygenerovat JPA ID třída, musí builder vypsat otypovaná javová pole. Typy vezme z typového modelu. `LangType` tedy není příprava na F7–F10, ale **předpoklad podpory kompozitních klíčů v Javě vůbec**. Totéž platí pro junction entitu z kroku 5a, jakmile půjde do Javy.

Z toho plyne, že krok 5a má do Javy větší záběr než do .NET: potřebuje jak rozhodnutí o kompozitních klíčích, tak neutralizovaný typový model.

### 4.6 Rozhodnutí 7.4 je kategorie, ne dapperovská větev

MyBatis je javový protějšek Dapperu: SQL-first, bez deklarativního mapování. Diagnostika z F11 by proto měla vzniknout jako „fakty nevyjádřitelné v cílovém frameworku", parametrizovaná frameworkem, ne jako větev specifická pro Dapper. Není to zvrat rozhodnutí 7.4, ale kdyby se implementovalo dapperovsky, přepisovalo by se to při příchodu MyBatisu.

### 4.7 `PrimaryKeyStrategy` chce revizi

Výčet vznikl pro dva .NET frameworky. NHibernate má kolem dvanácti generátorů, EF Core identity/sequence/HiLo/výchozí hodnotu, JPA `@GeneratedValue(AUTO|IDENTITY|SEQUENCE|TABLE|UUID)`. Vzhledem k tomu, že §3.4 postavil strategii per-part do modelu a `@EmbeddedId` na ni doplácí, stojí za to výčet zrevidovat dřív než později.

### 4.8 Co obstálo

Rozhodnutí z design docu 002 o generování N:M jako explicitní junction entity zůstává v platnosti. Hibernate má `@ManyToMany` s `@JoinTable` i explicitní junction entitu, tvarově totéž co .NET, takže argumentace se nemění.

Rozhodnutí 7.1 (řetězcová identifikace entit místo objektových referencí) rozšíření na Javu posiluje. Rozhodnutí 7.2 (IS NULL jako operátor) se nemění, JPQL má `IS NULL`. Rozhodnutí 7.3 (bez `[Obsolete]` přechodných období) se rozšíření netýká.

---

## 5. Seznam oprav

Řazeno podle závislostí, ne podle závažnosti.

### O1 — Identitní členy pro NHibernate u kompozitního klíče

**Blokuje:** krok 5a. **Závisí na:** rozhodnutí R1 (viz kapitola 6).

`NHibernateEntityBuilder` musí u entity s kompozitním klíčem vygenerovat:

- override `Equals(object)` porovnávající části klíče
- override `GetHashCode()` kombinující části klíče
- atribut `[Serializable]` na třídě

Pozor na past s proxy: naivní `Equals` s porovnáním `GetType() != obj.GetType()` se rozbije, protože NHibernate proxy je potomek entity a typy si nebudou rovny. Referenční dokumentace to ve svém příkladu obchází přetypováním přes `as` a porovnáním klíčových vlastností; generátor by měl jít touto cestou.

U `[Serializable]` je vzhledem ke změnám serializace v NHibernate 5.7.0 na .NET 10 potřeba ověřit na skutečném buildu, že se kontrola chová podle dokumentace.

### O2 — Test, který ověří použitelnost, ne jen tvar

**Závisí na:** O1.

`CompositeKeyTest.cs` doplnit o asserce nad generovaným C# pro NHibernate — přítomnost obou override a atributu. Zároveň zvážit obecnější test, který u každého cílového frameworku ověří, že generovaný kód nese členy vynucené tím frameworkem; to je přímý důsledek zjištění 4.4.

### O3 — Chybné položky v převodní tabulce typů

Tři samostatné jednořádkové opravy v `NHibernateWrappers/Convertors/DatabaseTypeConvertor.cs`, bez závislostí: `CLRType.Byte` na `DatabaseType.TinyInt`, `CLRType.Float` na `DatabaseType.Real` a oprava casingu ve větvi `"datetimenoMs"`. Případ `CLRType.Char` sem nepatří — viz zjištění 2.4 a položka Ú2.

### O4 — Vynutit invariant `Order`

Přesunout řazení částí klíče z `AbstractEntityBuilder.AddPrimaryKey` do typu `PrimaryKey`, aby platilo na každé konstrukční cestě, a zdvojené řazení v builderu odstranit. `SampleData` se nemění. Validace duplicitních hodnot `Order` sem nepatří — viz zjištění 3.1 a položka Ú1.

### O5 — Doplnit chybějící záznamy do changelogu

1. Normalizace konců řádků (commit `abb777f`).
2. Vznik design docu 002, aby existence dokumentu byla odvoditelná z `current-state.md` + `changelog.md`. Součástí je rozhodnutí, zda se vznik design docu obecně do changelogu zaznamenává; dosavadní praxe to nedělá, ale pak dvojice zdrojů pravdy nestačí.

### O6 — Zdokumentovat slévání typů data a času

Zaznamenat do `architecture.md` §4, že převod typů přes NHibernate není bijektivní: `DateTime`, `DateTime2` i `SmallDateTime` se slévají do jednoho typu NHibernate a zpětný převod je vrací jako `DateTime2`. Uvést i příčinu — `type` v mapování NHibernate není SQL typ — a vazbu na verzi a dialekt. Oprava, tedy vyjádření rozdílu přes `sql-type` na `<column>`, sem nepatří: vyžaduje znát cílový dialekt (R4) a rozhodnout, kdy `sql-type` emitovat (Ú2).

---

## 6. Návazné úpravy návrhu

Rozhodnutí, která je potřeba zapsat, než se pokračuje v implementaci.

### R1 — Forma kompozitního klíče ve výstupu

Do design docu 001 doplnit sekci k §3, která zachytí:

**Výchozí vykreslení je všude ploché.** EF Core `[PrimaryKey]`/`HasKey`, NHibernate vnořený identifikátor, JPA `@IdClass`. Tvar entity a cesty k vlastnostem tak zůstávají jednotné napříč všemi cíli a dotazová IR může dál odkazovat vlastnosti plochým jménem.

Odůvodnění: `@EmbeddedId` nedokáže vyjádřit strategii generování per-part, kterou §3.4 postavil do modelu (zjištění 4.3); volba formy zasahuje do syntaxe dotazů (zjištění 4.2); a ploché vykreslení udrží jednotný tvar entity napříč pěti a více cíli.

**Identitní členy generuje builder, ne IR.** IR nese kompozitní klíč jako seřazený seznam sloupců; `Equals`, `GetHashCode`, `Serializable` a případná ID třída jsou odpovědnost builderu, ve stejné kategorii jako doplňování `virtual` pro NHibernate.

**Volitelný signál pro alternativní formu.** Analogicky k `IsJunctionTable` z design docu 002 zavést nepovinný údaj o názvu a formě klíčové třídy. Parser ho vyplní, pokud ho zdroj nese; builder ho použije, pokud je přítomný, jinak odvodí konvencí. Bez toho by překlad javového projektu s `@EmbeddedId` model potichu přetvaroval a nebylo by co ohlásit podle F11.

### R2 — Členy vynucené frameworkem jako pojmenovaný koncept

Zavést do architektury explicitní pojem pro boilerplate, který cílový framework vyžaduje a který není faktem o doméně (zjištění 4.4). Cíl je, aby každý nový builder měl jedno místo, kde deklaruje, co musí ke generované třídě přidat, a aby na to existoval společný test.

### R3 — Diagnostika jako kategorie

Přeformulovat rozhodnutí 7.4 tak, aby popisovalo obecný mechanismus „fakt nevyjádřitelný v cílovém frameworku" parametrizovaný frameworkem, ne dapperovskou větev. Seznam faktů pro Dapper už existuje ve srovnávací analýze; pro MyBatis bude analogický.

Do stejné kategorie patří i zúžení uvnitř typového modelu, nejen fakty, které cíl neunese. Konkrétně slévání `DateTime`, `DateTime2` a `SmallDateTime` do jediného typu NHibernate (zjištění 2.1): zdroj i cíl ten typ vyjádřit umí, ale převod ho zúží. Diagnostika by měla ohlásit obojí a rozlišit to, protože první případ je vlastnost cílového frameworku, kdežto druhý je vlastnost převodu a dá se odstranit (Ú2, R4).

### R4 — Deklarace cílových verzí frameworků

Rozhodnout, kde se deklaruje cílová verze frameworku pro převod: v IR, nebo v konfiguraci převodu. Je to předpoklad splnění S6 a zároveň to zpřesní generování — například volba mezi `[PrimaryKey]` a `HasKey` podle verze EF Core, nebo dostupnost `DateOnly` podle verze NHibernate.

---

## 7. Úpravy s delším horizontem

### Ú1 — Revize `PrimaryKeyStrategy` a sémantiky `Order`

Projít výčet `PrimaryKeyStrategy` proti generátorům NHibernate, mechanismům EF Core a `@GeneratedValue` z JPA. Souvisí s R1 a §3.4.

Ve stejném průchodu uzavřít otázku z §3.2, zda má být `Order` na `PrimaryKeyPart` unikátní a souvislý od jedničky, a podle rozhodnutí případně doplnit validaci (zjištění 3.1).

### Ú2 — Neutralizace typového modelu

Nejrozsáhlejší položka. Zahrnuje obě roviny:

1. `CLRType` → jazykově neutrální reprezentace (`LangType` podle JSS §5.2), s doplněním chybějících typů (zjištění 2.2) a s vyřešením případu `CLRType.Char`, který dnes nelze namapovat na správný typ NHibernate, protože v `DatabaseType` chybí hodnota pro jednotlivý unicode znak (zjištění 2.4)
2. `DatabaseType` → databázově neutrální reprezentace, případně s vrstvou pro dialekty (zjištění 2.3)

Je to **předpoklad** pro F7–F10 i pro javovou část kroku 5a, ne jejich příprava (zjištění 4.5). Zaslouží si vlastní design doc.

### Ú3 — Podpora varianty s klíčovou třídou v parseru NHibernate

Navazuje na R1 a na Ú2. Bez ní nelze číst vstupy, které klíč vyjadřují klíčovou třídou (zjištění 3.2), a analogicky pak `@EmbeddedId` na javové straně.

### Ú4 — Centrální správa verzí

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně (zjištění 3.4.1).

### Ú5 — Sjednocení ADO.NET provideru v benchmarcích

Přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6, přeměřit — nebo confound explicitně popsat v textu práce (zjištění 3.4.2). Souvisí s E-požadavky.

---

## 8. Položky z dřívějška, které nebyly zapracované

Přehled toho, co je rozhodnuté nebo zaznamenané, ale dosud neprovedené. Není to nový nález; slouží k tomu, aby se na to při plánování nezapomnělo.

### Z rozhodnutí 7.4 design docu 001

Dapper builder tiše zahazuje klíče a relace. Podle 7.4 má pro každý nevyjádřitelný fakt vydat strukturované varování; do vzniku diagnostické infrastruktury z F11 stačí prostý seznam varování ve výsledku převodu. Zatím neimplementováno. Nově k tomu přibývá požadavek z R3, aby mechanismus nebyl dapperovsky specifický.

### Dotazová větev

- `SubQueryInstruction` a `IQueryVisitor` jsou nedokončené: chybí `Visit(SubQueryInstruction)`, `Accept` vrací prázdný řetězec. Zaznamenáno v `architecture.md` §6.
- `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu).

### EF Core

- Strategie primárního klíče se nepropaguje do výstupu (TODO v `EFCoreEntityBuilder.BuildPrimaryKey`). Je to rozdíl v paritě, ne regrese. Souvisí s Ú1.

### Frontend

Odloženo do cílenější přestavby, současný stav je funkční:

1. Přeskočit prázdné dotazové jednotky v `convert()` před odesláním
2. Zobrazovat chyby ze serveru přes `err.error`
3. Validace před odesláním podle S7 (chyby na úrovni souboru a řádku)

### Infrastruktura

- `docker-compose` s `ConnectionStrings__AdvisorDatabase` není ověřený, Docker zatím nebyl spuštěn
- Otevřené rozhodnutí: automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit (souvisí s S5). Dnes je `wwwroot` commitnutý build.

---

## 9. Doporučené pořadí

1. **R1** — zapsat rozhodnutí o formě kompozitního klíče do design docu 001. Bez něj nemá O1 zadání.
2. **O1 + O2** — implementovat identitní členy a doplnit testy. Odblokuje krok 5a.
3. **O3, O4, O5, O6** — drobné opravy a doplnění dokumentace, bez závislostí, lze kdykoli.
4. **Krok 5a** — generování junction entity a vícesloupcových FK. Nadále platí, že `ColumnPairs` zůstávají prázdné a testovací vstupy je nutné skládat ručně přes builder API.
5. **R2, R3, R4** — zapsat zbývající rozhodnutí. R4 je předpoklad S6.
6. **Ú2** — design doc k typovému modelu. Předpoklad pro javovou větev; do té doby blokuje F7–F10.
7. **Ú1, Ú3, Ú4, Ú5** a položky z kapitoly 8 podle priorit vyplývajících z požadavků F/S/E.
