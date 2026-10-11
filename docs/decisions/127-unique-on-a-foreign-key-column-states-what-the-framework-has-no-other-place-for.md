# 127 — `unique` nad sloupcem cizího klíče tvrdí to, pro co framework nemá jiné místo: v JPA unikátní omezení, v NHibernatu kardinalitu

Datum: 2026-10-11
Stav: nahrazeno [130](130-a-unique-constraint-over-a-foreign-key-names-the-navigation.md)
Požadavky: F2, F5, F11, S2
Podklad: rozhodnutí [012](012-foreign-key-rendering.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [055](055-unique-constraint-as-a-carried-mapping-fact.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md) a [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md); Jakarta Persistence 3.2, javadoc `JoinColumn.unique` a `Column.unique`; referenční dokumentace NHibernate 5.7, atribut `unique` elementu `many-to-one`; položka *`unique` na sloupci cizího klíče jako tvrzení zdroje* v `open-items.md`

## Kontext

JPA `@JoinColumn(unique = true)` a `<join-column unique="true">` se čtou beze slova: `ReadJoinColumn` v obou čtečkách vrstvy JPA bere `name`, `referencedColumnName`, `nullable` a zápisové příznaky a `unique` nechá propadnout, takže ve `JpaJoinColumnFacts` pro něj není člen a `JpaMappingWriter` o něm neví. Je to poslední tiché místo čtení JPA (`subset.md`, 2.13) a podle rozhodnutí [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) má zaznít. Než zazní, musí být jasné, co tvrdí — a tu jsou dvě čtení, která řešení už obě používá, každé jinde:

- **Unikátní omezení nad sloupcem** (rozhodnutí [055](055-unique-constraint-as-a-carried-mapping-fact.md)). Tak čteme `@Column(unique = true)`: jednočlenný `UniqueConstraint` nad vlastností. `UniqueConstraint` ale jmenuje **vlastnosti**, a sloupec cizího klíče v JPA třídě vlastnost nemá — nese ho navigace přes `ColumnPairs` vztahu, leda by vedle ní stál skalár nad týmž sloupcem (idiom jen pro čtení, `insertable = false, updatable = false` na jedné z obou stran).
- **Vlastnící strana 1:1** (rozhodnutí [012](012-foreign-key-rendering.md)). Tak čte `NHibernateXMLMappingParser` `<many-to-one unique="true">`, a tak to NHibernate builder i píše; unikátní omezení nad navigací týž builder hlásí jako `Loss`, protože druhý význam téhož atributu by výstup udělal dvojznačným (055).

Obě čtení mají oporu v tom, co o atributu říkají primární zdroje — a ty se liší právě v tom, co jiného framework o vztahu ví:

- **Jakarta Persistence** definuje `JoinColumn.unique` stejnými slovy jako `Column.unique`: *zda je vlastnost unikátním klíčem; zkratka za anotaci `UniqueConstraint` na úrovni tabulky, užitečná tam, kde omezení pokrývá jediný sloupec*. Je to tvrzení o schématu. Kardinalitu vztahu říká **samotná anotace** — `@ManyToOne`, `@OneToOne` — a `unique` k ní nic nepřidává: javadoc dodává, že u spojovacího sloupce, který je částí klíče a cizího klíče zároveň, ho není třeba psát.
- **NHibernate** popisuje `unique` na `<many-to-one>` jako zapnutí generování unikátního omezení nad sloupcem cizího klíče v DDL — a hned dodává, že tím se násobnost asociace stává 1:1, protože na sloupec může mířit `property-ref`. `<one-to-one>` žádný sloupec nepřipouští (012), takže vlastnící strana 1:1 s vlastním cizím klíčem **nemá v NHibernatu jiný zápis** než `<many-to-one unique="true">`. Atribut je tam jediným nositelem kardinality.

Tedy týž atribut, dva frameworky, a v každém nese právě ten fakt, pro který framework nemá jiné místo. V NHibernatu kardinalitu, v JPA omezení schématu.

Dvě věci jsou pro rozhodnutí podstatné. Za prvé, **unikátnost cizího klíče vztahu 1:1 plyne z kardinality a každý cíl ji z ní odvozuje znovu**: NHibernate builder píše `unique="true"` na `<many-to-one>` právě u `Cardinality.OneToOne` (012), builder vrstvy JPA píše `unique = true` do každého `@JoinColumn` pod `@OneToOne` (`RenderJoinColumn`, 077) a EF Core nad cizím klíčem vztahu 1:1 zakládá unikátní index vlastní konvencí. Fakt „sloupec cizího klíče 1:1 je unikátní" tak mezireprezentace nese v `Cardinality.OneToOne` a nikde se neztrácí. Za druhé, **unikátní omezení nad sloupcem, který mapuje jen navigace, řešení už dvakrát odmítá se záznamem**: `WriteUniqueConstraint` u `@UniqueConstraint(columnNames = { "customer_id" })` hledá atribut třídy nad tím sloupcem, a když nenajde žádný, vydá `Incompleteness` kategorie `UniqueConstraint` („names a column no attribute of the class maps"); `CatalogCompletion.CompleteUniqueConstraints` dělá totéž pro omezení z katalogu („no property matches the column"), s odůvodněním, že vymyslet člen by do třídy vložilo, co zdroj nedeklaroval. Čtení `@JoinColumn(unique = true)` jako zkratky za `@UniqueConstraint` tedy nemá, kam jinam vést, než kam vede ta anotace sama.

## Zvažované varianty

1. **Číst `unique` na spojovacím sloupci JPA jako vlastnící 1:1, jako v NHibernatu.** Sjednotilo by to obě strany jedním pravidlem. Zamítáme ze tří důvodů. Odporuje to specifikaci, která atribut definuje jako zkratku za `@UniqueConstraint`. Kardinalita je v JPA vyslovena anotací, takže `@ManyToOne` s `unique = true` by nástroj přepsal na `@OneToOne` — tvrzení, které zdroj neřekl, a vztah s protistranou `@OneToMany(mappedBy = …)` by tím v cíli přestal být platný, protože `mappedBy` kolekce smí mířit jen na `@ManyToOne`. A nic by se tím nezískalo: pod `@OneToOne` je `unique` beztak důsledkem kardinality, kterou už anotace nese.

2. **Nést omezení nad navigací.** `UniqueConstraint` připouští jakékoli jméno vlastnosti, takže by `@ManyToOne @JoinColumn(unique = true) Customer customer` dal `UniqueConstraint(["customer"])`. Model by fakt podržel, ale žádný cíl ho dnes nepíše správně: NHibernate builder ho hlásí jako `Loss` (055), EF Core builder by vypsal `[Index(nameof(Customer), IsUnique = true)]` nad navigací, což EF Core při stavbě modelu odmítne, a builder JPA by `ColumnOf(navigace)` přeložil na jméno, které není sloupcem. Správná emise — přes sloupce vztahu, u EF Core přes dogenerovanou skalární vlastnost cizího klíče z rozhodnutí 012 — je jiná práce, kterou by ve stejné míře potřebovalo i čtení `@UniqueConstraint` nad spojovacím sloupcem a doplňování z katalogu; obě dnes končí záznamem, a čtení `@JoinColumn(unique)` nemá důvod končit jinak. Zamítáme pro tuto položku a vedeme ji jako samostatnou otázku.

3. **Číst `unique` na spojovacím sloupci jako to, čím podle specifikace je: zkratka za `@UniqueConstraint` nad tím sloupcem — a zacházet s ním přesně tak, jak řešení s tou anotací zachází.** Pod `@OneToOne` je to důsledek kardinality, kterou mezireprezentace nese; jinde je to omezení schématu, které se nese nad skalárem vedle vztahu, a kde takový není, hlásí se týmž záznamem jako `@UniqueConstraint` a katalog. NHibernate čte dál kardinalitu, protože tam je atribut jejím jediným nositelem.

4. **Nechat ticho a zapsat ho jako mez.** Nejlevnější, ale je to právě to, co rozhodnutí 048 zakazuje: fakt, který zdroj vyslovil, by mizel beze slova. Zamítáme.

## Rozhodnutí

**Volíme variantu 3.** `unique` nad sloupcem cizího klíče tvrdí v každém frameworku ten fakt, pro který framework nemá jiné místo, a čte se podle toho:

**V JPA je `unique = true` na `@JoinColumn`, `<join-column>`, i na spojovacích sloupcích `@JoinTable`, unikátní omezení nad tím sloupcem** — zkratka za `@UniqueConstraint(columnNames = { sloupec })`, jak ji specifikace definuje. Čte se stejnou cestou jako ta anotace:

| Kde stojí | Co tvrdí | Co nástroj udělá |
|---|---|---|
| vlastní spojovací sloupec `@OneToOne` | unikátnost cizího klíče vztahu 1:1 — důsledek kardinality, kterou anotace nese | nic nepřidá a nic nehlásí: fakt nese `Cardinality.OneToOne` a každý cíl ho z ní vypíše znovu |
| sloupec, který v téže třídě mapuje i skalár (`@Basic`, `@Id`, `@Version`; idiom jen pro čtení) | unikátní omezení nad tím sloupcem | `AddUniqueConstraint(null, [skalár])` — totéž, co by řekl `@Column(unique = true)` na skaláru; stejná množina se neslévá dvakrát (`CoversSameAs`) |
| sloupec, který žádný atribut třídy nemapuje (`@ManyToOne` bez skaláru, jednosměrné `@OneToMany`, sloupce `@JoinTable`) | unikátní omezení nad sloupcem, který model neumí pojmenovat | `Incompleteness`, kategorie `UniqueConstraint`, na atributu vztahu — týž záznam, jaký dostane `@UniqueConstraint` nad tím sloupcem a omezení z katalogu |

Záznam je `Incompleteness`, ne `Loss`: fakt nepadá na frameworku cíle, ale na tom, že mezireprezentace jmenuje omezení vlastnostmi a sloupec cizího klíče bez skaláru vlastnost nemá — stejná mezera, kterou týmž druhem záznamu hlásí fáze doplňování z katalogu. Tvrzení stojí ve zdroji na `@JoinColumn`, a tam se mu podle rozhodnutí [125](125-positions-of-facts-as-an-optional-layer-over-the-ledger.md) dává i pozice.

**V NHibernatu zůstává `<many-to-one unique="true">` vlastnící stranou 1:1** (rozhodnutí 012 beze změny): `<one-to-one>` sloupec nepřipouští, takže atribut je jediným způsobem, jak vlastnící 1:1 vyslovit, a referenční dokumentace ho tak čte. O unikátnost sloupce se tím nepřichází — plyne z kardinality a NHibernate builder ji z ní píše zpátky. Obě strany se tedy sjednocují **zásadou**, ne pravidlem: týž atribut se čte jako ten fakt, pro který framework nemá jiné místo, a tam, kde framework kardinalitu vyslovuje jinak, je `unique` jen schéma.

**Zápis se nemění.** Builder vrstvy JPA píše `unique = true` do `@JoinColumn` pod `@OneToOne` dál — je to tvrzení, které cíl z kardinality odvozuje (rozhodnutí 067), ne výchozí hodnota, a druhý průchod čtečkou ho vstřebá bez záznamu, protože o kardinalitě už anotace mluví. Unikátní omezení nad skalárem vedle vztahu píšou všechny tři anotační cíle tak, jak píšou každé jiné jednočlenné omezení (055).

## Důsledky

**Mění se jen vrstva JPA a dokumentace.** `JpaJoinColumnFacts` dostává člen `Unique`, obě čtečky ho plní z `unique`, `JpaMappingWriter` ho vyhodnocuje po vztazích vedle jednočlenných omezení z `@Column(unique)`. `AbstractWrappers`, `Model`, orchestrace, deskriptory ani ostatní wrappery se nedotýkají; NHibernate parser i builder zůstávají, jak je nechalo rozhodnutí 012.

**Poslední tiché místo čtení JPA mizí** a `subset.md` 2.13 už žádné nevede u mapování JPA. Místo něj přibývá vyslovená mez: unikátní omezení nad sloupcem, který mapuje jen navigace, se nečte — ať ho vysloví `@JoinColumn(unique)`, `@UniqueConstraint`, nebo katalog — a hlásí se `Incompleteness`. Zda a jak se takové omezení ponese přes sloupce vztahu (a u EF Core vypíše nad dogenerovanou vlastností cizího klíče), je otevřená otázka v `open-items.md`; toto rozhodnutí na ní nestojí, jen se s ní zúží.

**Převod Hibernate → Hibernate nad 1:1 se nemění** a je dál idempotentní: `unique = true`, které builder napsal, čtečka vstřebá jako důsledek `@OneToOne` a builder ho napíše znovu. Převod `@ManyToOne` s `unique = true` do NHibernatu dá `<many-to-one>` bez `unique` — správně, protože zdroj neřekl 1:1 — a o omezení promluví záznam, nebo ho ponese `<property unique="true">` skaláru vedle vztahu.

**F11 se u JPA zpřesňuje v `traceability.md`**: ve výhradě zůstávají jen nehlášené atributy NHibernate mapování. Podle rozhodnutí [041](041-versioning-and-release.md) je to oprava chování čtení bez změny kontraktu — PATCH.
