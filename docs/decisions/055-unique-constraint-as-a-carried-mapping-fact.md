# 055 — Unikátní omezení je nesené mapovací fakt, ne vyňatá oblast

Datum: 2026-08-24
Stav: platí
Požadavky: F2, F4, F5, F11, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [012](012-foreign-key-rendering.md), [015](015-mapping-fact-completion-from-the-catalog.md), [017](017-source-precedence-for-mapping-facts.md) a [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md); nález z 2026-08-23, oddíl 4

## Kontext

Kritérium F4 vyjmenovává, co se má z databázového schématu načíst, a mezi tím jsou **unikátní omezení**. `TestSchema.sql:93` jedno deklaruje (`CONSTRAINT [UQ_Products_Sku] UNIQUE ([Sku])`), `traceability.md` značí F4 jako *nárokované* — a čtečka katalogu se na unikátní omezení neptá. Dotaz na klíč má `WHERE kc.type = 'PK'`, `TableImage` ani `ColumnImage` pro unikátnost nemají člen, `MappingFactCategory` pro ni nemá hodnotu a žádný builder ji nevypisuje. Nárok tedy tvrdí víc, než řešení dělá.

**Ticho není jen v katalogu, je i na vstupní straně.** Prošli jsme všechny tři parsery:

- `NHibernateXMLMappingParser` má u `ReadColumnFacts` výslovný komentář *„Attributes with no counterpart in the model - unique, index, check, default - are skipped"*. `ReadFacetsInto` čte `not-null`, `precision`, `scale` a `length`; zbytek propadne **bez záznamu**.
- `EFCoreEntityParser.ParseClassAttributes` obsluhuje `[Table]` a nic jiného a **nemá větev `default:`** — na rozdíl od vlastnostní úrovně, která nezachycenou anotaci hlásí. `[Index(nameof(Sku), IsUnique = true)]`, jediný způsob, jak EF Core vysloví unikátnost anotací, tedy mizí beze slova.
- Dapper o mapovacích faktech nemluví vůbec, takže tam není co ztratit.

To je přesně to ticho, kterému rozhodnutí [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) předcházelo u vlastnostních faktů — jen se ho ta oprava nedotkla, protože zmíněné atributy se odfiltrují **dřív**, než se k jeho větvi `default:` dostanou. Rozhodnutí 048 tedy platí, ale na tuhle skupinu nedosáhne.

**Unikátnost už v řešení jedno použití má, a je jiné.** `<many-to-one unique="true">` je podle rozhodnutí [012](012-foreign-key-rendering.md) vlastnící strana vztahu 1:1 a `NHibernateXMLMappingParser` ji tak i čte zpátky. To je unikátnost jako **signál o kardinalitě**, ne jako omezení schématu; obojí se v jednom atributu potkává a nesmí se slít.

**Sousedé unikátnosti nejsou stejný případ.** `check` a `default` mají v EF Core jen fluent podobu (`HasCheckConstraint`, `HasDefaultValue`) a náš builder vydává výhradně datové anotace — v celém `EFCoreWrappers` není `OnModelCreating` ani jednou. Jejich obsahem je navíc doslovný SQL výraz, tedy věc dialektu, který `architecture.md` §9 vyjímá ze záruk jako oblast 5. Neunikátní index je výkonnostní artefakt, ne mapovací fakt. Unikátní omezení je proti nim jediné, které F4 jmenuje, které má anotační protějšek v obou anotačních cílech a které je na dialektu nezávislé.

## Zvažované varianty

1. **Zúžit nárok.** F4 posunout na *nárokované v užším rozsahu*, vyňatou oblast vyslovit v `architecture.md` §9 a položku dát do `open-items.md`. Kritérium F4 to výslovně připouští (*„odchylky explicitně zaznamenané"*) a je to nejlevnější poctivá cesta. Zamítáme ne proto, že by byla nesprávná, ale proto, že cena druhé varianty je po prozkoumání nižší, než nález odhadoval: rozšíření `MappingFactCategory` je změna, kterou **překladač a inicializátory vynutí na každém dotčeném místě** — `TargetFrameworkDescriptor.Support` vyhodí výjimku se jmenným seznamem chybějících kategorií u každého deskriptoru a oba `switch` v `AbstractEntityBuilder` skončí `ArgumentOutOfRangeException`. Žádné místo nelze opomenout, takže „velká změna" tu neznamená „riziková změna".

2. **Nést unikátnost celou cestou a sousedy hlásit.** Nová kategorie, člen v mezireprezentaci, čtvrtý dotaz do katalogu, emise v obou anotačních cílech, `NotExpressible` u Dapperu — a `check`, `default` a neunikátní index přestanou mizet tiše, aniž by do modelu vstoupily.

3. **Nést všechna čtyři omezení.** Znamenalo by nést doslovný text výrazu `CHECK` a výchozí hodnoty, tedy dialektem vázaný řetězec, který nástroj neumí ověřit ani přeložit — a pro EF Core cíl by neexistovala emise vůbec. Otevřelo by to oblast, kterou `architecture.md` §9 vyjímá záměrně, výměnou za fakt, který by ve dvou ze tří cílů rovnou zemřel. Zamítáme.

## Rozhodnutí

**Volíme variantu 2.**

**Unikátní omezení je fakt entity, ne vlastnosti.** `EntityMap.UniqueConstraints` drží seznam `UniqueConstraint`, protože omezení může pokrývat několik sloupců — týmž důvodem, jakým `Relation` sedí na `EntityMap` a ne na `PropertyMap` (rozhodnutí [001](001-entity-reference-by-name.md)).

**Omezení jmenuje vlastnosti, ne sloupce.** `UniqueConstraint.PropertyNames` nese **jména vlastností**, ne odkazy a ne názvy sloupců. Jména proto, že mezi fází čtení a emisí běží rozpouštění klíčových tříd, syntéza spojovacích entit a překlad jmen, a odkaz by se rozešel se skutečností — přesně důvod, pro který rozhodnutí 001 volí jméno i mezi entitami. Vlastnosti proto, že **oba cíle chtějí jméno vlastnosti**: EF Core `nameof(Sku)` a NHibernate atribut na elementu `<property name="Sku">`. Sloupec zná katalog, a ten se na vlastnost přeloží ve fázi doplňování, kde už `FindPropertyMapForColumn` existuje kvůli primárnímu klíči.

**Název omezení se nese a je nepovinný.** Katalog ho zná vždy, `unique-key` v NHibernate ho vyžaduje jako seskupovací značku a `[Index(Name = …)]` ho umí. `unique="true"` v NHibernate a `[Index(A, B, IsUnique = true)]` bez `Name` ho naopak nemají, a vynutit ho by znamenalo tvrdit o vstupu, co neřekl.

**Kategorie `MappingFactCategory.UniqueConstraint` platí za všechny tři cíle:** EF Core `Expressible` (`[Index(…, IsUnique = true)]` je třídní anotace, tedy přesně to, co builder vydává), NHibernate `Expressible` (`unique` u jednosloupcového, `unique-key` u vícesloupcového), Dapper `NotExpressible`. U Dapperu tím vzniká záznam `Loss` **mechanicky** z průniku podle rozhodnutí [010](010-diagnostics-as-returned-data.md), aniž by ho builder psal rukou.

**Kolize se signálem 1:1 se řeší vyhnutím, ne přetížením.** Unikátní omezení se vypisuje **jen na elementu `<property>`**. Omezení, jehož vlastnost je částí klíče, navigací nebo verzí, se do mapování nedostane a hlásí se záznamem `Loss`: atribut `unique` na `<many-to-one>` už podle rozhodnutí 012 znamená kardinalitu a druhý význam by z výstupu udělal dvojznačnost. Část primárního klíče je navíc unikátní z definice, takže se tím nic netvrdí navíc.

**Vícesloupcové omezení bez názvu dostane odvozený.** NHibernate bez `unique-key` skupinu nevysloví. Odvozuje se deterministicky jako `UQ_{entita}_{vlastnosti}` (S2) a vydává se záznam `Convention` — hodnota `unique-key` je seskupovací značka uvnitř dokumentu, ne jméno omezení v databázi, takže se tím o schématu netvrdí nic.

**Priorita zdrojů platí beze změny (rozhodnutí [017](017-source-precedence-for-mapping-facts.md) a [015](015-mapping-fact-completion-from-the-catalog.md)).** Omezení se ztotožňují **množinou vlastností**, ne názvem: co zdroj řekl, zůstává, katalog doplňuje jen množinu, kterou zdroj nenese, a doplněné se hlásí záznamem `Supplied`. Rozdílný název nad touž množinou je záznam `Conflict` a vítězí zdroj.

**Sousední atributy přestávají mizet tiše, aniž by vstoupily do modelu.** `check`, `default` a `index` u vlastnosti v NHibernate mapování a nezachycená třídní anotace v EF Core (včetně `[Index]` bez `IsUnique`) vydají záznam `Loss` bez `Category` — přesně tvar, který stanovilo rozhodnutí [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) pro fakt, pro který model nemá místo. Do mezireprezentace nevstupují.

## Důsledky

**F4 zůstává nárokované v plném rozsahu.** Čtečka posílá čtvrtý dotaz do `sys.key_constraints` s `type = 'UQ'` a `traceability.md` u F4 unikátní omezení uvádí v důkazním sloupci. Únikovou cestu kritéria F4 tím nepotřebujeme.

**Změna je velká plochou, ne rizikem.** Dotčena jsou: `MappingFactCategory`, tři deskriptory, `UniqueConstraint` a `EntityMap` v modelu, `AddUniqueConstraint` a oba `switch` v `AbstractEntityBuilder`, `TableImage`, `SqlServerCatalogReader`, `CatalogCompletion`, oba anotační parsery a oba anotační buildery. Každé z těch míst si vynutí sám překlad nebo inicializátor deskriptoru.

**Dapperu se výstup nemění a diagnostika roste.** Vstup nesoucí unikátní omezení dostane u Dapperu záznam `Loss` — stejné povahy jako u primárního klíče nebo typu, tedy tvrzení o Dapperu, ne o nástroji.

**Zbývající tři omezení zůstávají mimo model a `architecture.md` §9 to vyslovuje.** Nejsou vyňatou oblastí ve smyslu rozhodnutí [030](030-scope-of-version-1-0.md) — vstup na ně sáhnout smí a dozví se, že se nepřenesly.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR uvnitř řešení i navenek**: přibývá veřejný člen `EntityMap`, hodnota výčtu a kategorie v diagnostice, nic se neruší. REST kontrakt se nemění ani tvarem, ani dokumentem: `MappingFactCategory` jde do odpovědi jako číslo a `openapi.json` ho popisuje jako `integer` bez výčtu hodnot, takže se generovaný dokument nemění. **Číselnou hodnotu ale překládá na popisek frontend** — tabulka `MAPPING_FACT_CATEGORY_LABELS` v `wwwroot/js/api.js` — a ta dvanáctou hodnotu potřebuje, jinak by se záznam zobrazil bez názvu kategorie.
