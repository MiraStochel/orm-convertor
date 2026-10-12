# 134 — Vlastnost bez setteru dostane v cíli nejužší setter, kterým ji framework zapíše; čte se tak, jak ji čte zdrojový framework

Datum: 2026-10-12
Stav: platí
Požadavky: F2, F5, F10, F11, S1, S2
Podklad: rozhodnutí [009](009-target-framework-descriptor.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [049](049-language-facts-under-source-precedence.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [072](072-a-transient-property-is-a-carried-mapping-fact.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md) a [132](132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md); [srovnání .NET frameworků](../analysis/orm-frameworks-comparison.md), §3, řádek „Přístupnost setterů" a odstavec „Důsledek pro IR"; chování zafixovaných verzí EF Core 10.0.10, NHibernate 5.7.0 a Dapper 2.1.79 ověřené 2026-10-12 dočasným testem nad balíčky sady (po měření smazaným); otevřená položka „Vlastnost bez setteru v .NET cíli"

## Kontext

Model nese u vlastnosti dva jazykové fakty o přístupových metodách, `HasGetter` a `HasSetter`; druhý artefakt je smí doplnit, nikdy zrušit (rozhodnutí 049). Javové čtení je bere z přítomnosti metod `getX`, `isX` a `setX` (rozhodnutí 077), C# čtení z přítomnosti akcesorů `get` a `set`. Pole, které javová třída vystavuje jen getterem, je v JPA běžný tvar — při přístupu přes pole zapisuje provider pole přímo a setter nepotřebuje —, a v modelu je to vlastnost bez setteru. Všechny tři .NET buildery ji píšou doslova jako `{ get; }`.

Zafixované verze na takovou vlastnost a na její sousední tvary odpovídají každá jinak:

| | `{ get; }` | `{ get; private set; }` | `{ get; protected set; }` | `{ get; init; }` |
|---|---|---|---|---|
| EF Core 10.0.10 | konvencí nemapuje, ani s `[Key]` či `[Column]` — nad `[Key] … { get; }` odmítne model, protože entita nemá klíč; stejně nemapuje referenční navigaci a primitivní kolekci bez setteru. Kolekční navigaci mapuje; vlastnost, kterou jmenuje fluent `Property`, `HasKey` či `HasIndex`, mapuje přes pomocné pole | mapuje | mapuje | mapuje |
| NHibernate 5.7.0 | `BuildSessionFactory` odmítne (`PropertyNotFoundException`, chybí setter), i u kolekce; s `access="backfield"` přijme | odmítne validátor proxy (`set_X should be 'public/protected virtual'`) | přijme | — |
| Dapper 2.1.79 | naplní přes pomocné pole `<X>k__BackingField` | naplní | naplní | naplní |

Javová třída s gettery bez setterů tak dnes vydá EF Core entitu, kterou EF Core odmítne nebo jejíž sloupce tiše nemapuje, a NHibernate mapování, které NHibernate odmítne — a žádný záznam to neřekne. Je to vada ve smyslu, v jakém ji vede seznam otevřených položek: vstup v nároku, z něhož vznikne artefakt, který cíl odmítne nebo který tiše znamená něco jiného. **Dapper vadný není**, ačkoli otevřená položka to předpokládala; měření předpoklad vyvrací.

Táž tabulka ukazuje, že oprava zápisu sama nestačí, protože vada má i stranu čtení:

- **Get-only auto-vlastnost zdroje EF Core** (`public string Code { get; }`) se dnes čte jako sloupec, přestože ji EF Core sám nemapuje. Vypočtenou vlastnost — getter s tělem — čtení už vynechává jako chování třídy; get-only auto-vlastnost nese hodnotu a čte se dál. Po opravě zápisu by se rozpor ukázal i ve směru EF Core → EF Core: ze vstupu, kde vlastnost sloupcem není, by vyšel výstup se setterem, kde sloupcem je.
- **C# setter s vlastním modifikátorem** (`private set`, `protected set`) se dnes čte jako setter bez rozdílu, takže ho každý cíl vypíše veřejný. Setter, který by oprava psala do NHibernatu, by se round tripem NHibernate → NHibernate rozšířil na `set;`.
- **Akcesor `init`** se dnes nečte jako setter vůbec. EF Core zdroj `public required string Name { get; init; }` tak vyjde jako `required … { get; }`, který se nepřeloží (CS9034), a bez `required` jako vlastnost, kterou EF Core ani NHibernate nezapíše.

Otázka má proto tři části: co builder napíše za setter, který model nenese; co C# čtení považuje za setter; a kdy je get-only auto-vlastnost zdroje EF Core sloupcem.

## Zvažované varianty

### Zápis

**1 — Veřejný `set;` všude.** Nejjednodušší a všechny tři frameworky ho přijmou. Zamítáme, protože mění rozhraní třídy, které zdroj vyslovil: javová třída bez setteru neříká jen „framework zapisuje pole", ale i „volající hodnotu nemění". Veřejný setter by tvrdil opak a cíl by dostal API, které zdroj odepřel. Takové rozšíření by si podle rozhodnutí 004 a 010 žádalo záznam u každé neměnné vlastnosti — a to zbytečně, protože užší cesta existuje.

**2 — Ponechat `{ get; }` a říct frameworku, ať zapisuje pomocné pole.** NHibernate to umí atributem `access="backfield"` a Dapper to dělá sám. EF Core ale nic takového anotací nevysloví — `[Column]` ani `[Key]` na get-only vlastnosti nic nezmění — a jedinou cestou je fluent `Property(e => e.X)`, kterou EF Core builder nepíše: vydává třídu s datovými anotacemi a fluent konfiguraci čte jen jako zdroj (rozhodnutí 132). Pro EF Core by varianta nefungovala vůbec. Pro NHibernate by vázala mapování na jméno pomocného pole, které generuje kompilátor C#, takže by se rozbila, jakmile konzument vlastnost rozepíše s vlastním polem. A NHibernate builder by psal atribut, který NHibernate parser hlásí jako ztrátu (`access`, [subset.md](../subset.md), 2.2), takže by round trip vlastního výstupu přestal být bez záznamů. Zamítáme.

**3 — `init;`.** EF Core i Dapper ho přijmou, ale `init` bez modifikátoru je veřejný: dovoluje každému volajícímu nastavit hodnotu v inicializátoru objektu, což javová třída bez setteru nedovoluje. U NHibernatu, jehož proxy přepisuje virtuální vlastnosti, by šlo o akcesor, který jsme neměřili. Zamítáme.

**4 — Nejužší setter, kterým cílový framework vlastnost zapíše, a jen tam, kde ho potřebuje.** EF Core `private set`, NHibernate `protected set`, Dapper nic. Volajícím zůstává vlastnost jen ke čtení jako ve zdroji a framework ji zapsat smí.

### Co C# čtení považuje za setter

**a — Akcesor `set` s jakýmkoli modifikátorem, `init` ne** (dnešní stav). Zamítáme. Varianta 4 píše akcesor s modifikátorem právě jako zápis „framework zapisuje, volající ne", a čtení, které ho bere jako veřejný setter, by vlastní výstup nástroje při round tripu rozšířilo. Opačná chyba je u `init`: volajícím zápis nabízí, a čtení, které ho nevidí, vydá vlastnost, kterou cíl nezapíše a kterou inicializátor objektu konzumenta nenaplní.

**b — Setter je akcesor `set` nebo `init` bez vlastního modifikátoru přístupu.** Čtení je inverzí zápisu varianty 4: setter, který si třída drží pro sebe a pro svůj framework, není setter nabídnutý volajícím.

### Get-only auto-vlastnost zdroje EF Core

**i — Číst ji dál jako sloupec.** Zamítáme: EF Core ji nemapuje, takže by překlad přidal sloupec, který zdrojový model nemá. Po zápisu podle varianty 4 by to platilo i pro EF Core → EF Core.

**ii — Vynechat ji z čtení jako vypočtenou vlastnost.** Zamítáme: na rozdíl od vypočtené vlastnosti nese hodnotu, přiřazenou v konstruktoru, a model ji umí nést celou — jméno, typ, nullabilitu i inicializátor. Rozhodnutí 072 zamítlo vyřazení nepersistované vlastnosti z modelu právě proto, že třída je aplikační vrstva a člen na ní konzument potřebuje.

**iii — Číst ji jako nepersistovanou**, tedy jako `[NotMapped]`, kromě případů, kdy ji EF Core mapuje: je-li kolekční navigací, nebo ji jmenuje fluent konfigurace.

## Rozhodnutí

Volíme variantu 4 pro zápis, variantu b pro čtení setteru v C# a variantu iii pro get-only auto-vlastnost zdroje EF Core.

**Zápis: vlastnost s getterem a bez setteru dostane v cíli nejužší setter, kterým ji cílový framework zapíše, a jen tam, kde ho framework potřebuje.**

| Cíl | Persistovaná vlastnost | Transientní vlastnost |
|---|---|---|
| EF Core | `{ get; private set; }`; kolekce, kterou vztah naviguje, zůstává `{ get; }`, protože ji EF Core naplní přes pomocné pole | `{ get; }` |
| NHibernate | `{ get; protected set; }` — u skaláru, klíče, verze, reference i kolekce; užší setter odmítne validátor proxy | `{ get; }` |
| Dapper | `{ get; }` — Dapper ji naplní přes pomocné pole | `{ get; }` |

Setter je požadavek frameworku na třídu stejného druhu jako `virtual` u NHibernatu: fakt o cíli, ne o doméně, který builder doplňuje a parser zahazuje (srovnání .NET frameworků, §3, „Důsledek pro IR"; rozhodnutí 076). Zápis proto nevydává záznam — volajícím zůstává vlastnost jen ke čtení jako ve zdroji a nic se neztrácí. Modifikátor `required` EF Core builder takové vlastnosti nepřidává: nesnese setter užší než třída (CS9032) ani chybějící setter (CS9034). Javové cíle se nemění, protože setter nepotřebují: JPA builder anotuje pole, které provider zapisuje přímo, a MyBatis zapisuje pole reflexí, takže vlastnost bez setteru vyjde jako pole s getterem.

**Čtení C#: setter je akcesor `set` nebo `init` bez vlastního modifikátoru přístupu.** Akcesor s modifikátorem — `private set`, `protected set`, `internal set`, `private init` a další — je setter, který si třída drží pro sebe a pro svůj framework. Model ho čte jako žádný (`HasSetter = false`) a cíl napíše svůj nejužší podle tabulky výše. Je to inverze zápisu: `protected set` z NHibernatu a `private set` z EF Core se round tripem vrátí, místo aby se rozšířily na veřejný setter, a překlad mezi .NET frameworky dá každému cíli tvar, který jeho framework vyžaduje — `protected set` z NHibernatu vyjde v EF Core jako `private set`, `private set` z EF Core v NHibernatu jako `protected set`, který jeho proxy přijme. Záznam nevzniká ze stejného důvodu jako při zápisu.

**`init` bez modifikátoru je setter**, protože volajícím zápis nabízí — v inicializátoru objektu. Model rozdíl mezi `set` a `init` nenese, cíle proto napíšou `set;` a omezení na inicializaci se ztrácí; podle rozhodnutí 048 je to záznam `Loss` u vlastnosti. Rozšíření je tu bezpečnější směr než zúžení: inicializátor objektu, který konzument nad vlastností píše, se přeloží dál, kdežto nad `private set` by se nepřeložil.

**Čtení zdroje EF Core: get-only auto-vlastnost je nepersistovaná, ledaže ji EF Core mapuje.** Auto-vlastnost s getterem a bez akcesoru `set` či `init` — s modifikátorem i bez něj, protože EF Core zapíše i soukromý — EF Core konvencí do modelu nebere, ať na ní stojí jakékoli anotace. Parser ji proto čte větví `[NotMapped]` z rozhodnutí 072: jazyková fakta a příznak, anotace sloupce bez záznamu, protože v modelu EF Core nic netvrdí, a `[Key]` vedle příznaku, aby rozpor odmítla brána úplnosti stejně, jako EF Core odmítne model bez klíče. Je to konvence EF Core odvozená z tvrzení třídy — třída setter nedeklaruje — a materializuje se, protože prázdný fakt by putoval jinam, k mapování konvencí cíle; podle rozhodnutí 067 tedy bez záznamu. Výjimky odpovídají tomu, kdy EF Core vlastnost bez setteru mapuje:

- **kolekční navigace** — vlastnost typu kolekce, jejíž prvek není skalár. Primitivní kolekci (`List<string>`) EF Core bez setteru nemapuje, a ta je proto nepersistovaná;
- **vlastnost, kterou jmenuje fluent konfigurace** — lambdou či řetězcem v `Property`, `HasKey`, `HasIndex` a `HasAlternateKey`, jako navigace v `HasOne`, `HasMany`, `WithOne` či `WithMany`, nebo jako člen `HasForeignKey`. EF Core ji pak mapuje přes pomocné pole. `Ignore` jmenováním není, vlastnost vyjímá. Člen `HasForeignKey` jednoho ku mnoha, konfigurovaného ze strany kolekce, patří entitě na druhé straně, jejíž jméno konfigurace nevyslovuje: čte se u třídy, která deklaruje inverzní navigaci ze `WithOne`, a bez ní u kterékoli jiné třídy než konfigurované.

Get-only `Id` bez `[Key]` proto konvenčním klíčem není — EF Core nad takovou třídou model nepostaví — a entita bez klíče jde dál podle rozhodnutí 063. Čtení zdroje Dapper a NHibernate se nemění: Dapper get-only auto-vlastnost naplní, takže je sloupcem, a o sloupcích NHibernatu rozhoduje mapovací dokument (rozhodnutí 072).

**Javové čtení zůstává podle rozhodnutí 077**: `HasSetter` znamená přítomnost metody `setX` bez ohledu na její viditelnost. Javovým protějškem zápisu „framework zapisuje, volající ne" není neveřejný setter, ale chybějící setter při přístupu přes pole — tvar, který JPA builder sám píše —, takže inverze zápisu tu žádnou změnu čtení nežádá. Ručně psaný neveřejný `setX` proto v C# vyjde jako veřejný `set;`. Je to vyslovená mez ([subset.md](../subset.md), 2.2), ne tichý průchod.

## Důsledky

Mění se sdílené C# čtení (`CSharpEntityParser.ReadProperty` a záznam u `init`), EF Core parser (get-only auto-vlastnost větví `[NotMapped]` s výjimkami výše), `EFCoreReadingContext` (jména, která konfigurace vyslovila) a `EFCoreFluentConfigurationParser`, který je do něj zapisuje, a EF Core a NHibernate builder (setter podle tabulky). Dapper builder, `AbstractWrappers` ani orchestrace se nemění (S1).

**Změny chování, které je fér vyslovit:**

- Javová třída s gettery bez setterů vydá EF Core entitu, kterou EF Core přijme i s klíčem, a NHibernate mapování, které NHibernate přijme. Výhrada, s níž platil překlad Java → .NET, padá.
- EF Core `{ get; private set; }` přestane v EF Core cíli vycházet jako `required … { get; set; }` a v NHibernatu vyjde jako `{ get; protected set; }`; do Javy jako pole s getterem bez setteru.
- Get-only auto-vlastnost zdroje EF Core vyjde v EF Core s `[NotMapped]`, v NHibernatu bez elementu mapování a v JPA s `@Transient`; `[Key]` na ní entitu odmítne záznamem `Failure`.
- `{ get; init; }` vyjde jako `{ get; set; }` se záznamem `Loss`; s `required` se nově přeloží.

**Testy.** `Combined/PropertyWithoutSetterTest`: javová třída bez setterů do EF Core (3. stupeň: model mapuje klíč i sloupce), do NHibernatu (3. stupeň: session factory) a do Dapperu (`{ get; }`); kolekce bez setteru do EF Core bez setteru a do NHibernatu s `protected set`; transientní vlastnost bez setteru; čtení `private set`, `protected set` a `init` a round trip NHibernate → NHibernate a EF Core → EF Core; get-only auto-vlastnost zdroje EF Core jako nepersistovaná, `[Key]` na ní jako `Failure`, kolekční navigace a vlastnost jmenovaná konfigurací (`Property`, `HasKey`, `HasIndex`, `HasForeignKey` ze strany kolekce) jako mapované. Test `CrossEcosystemTest.AFieldWithoutASetterIsNotRequiredInEfCore` tvrdí místo `{ get; }` tvar `{ get; private set; }`.
