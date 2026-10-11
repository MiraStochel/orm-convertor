# 130 — Unikátní omezení nad sloupci cizího klíče jmenuje navigaci vztahu

Datum: 2026-10-11
Stav: platí
Požadavky: F3, F4, F5, F11, S1, S2
Podklad: rozhodnutí [001](001-entity-reference-by-name.md), [012](012-foreign-key-rendering.md), [015](015-mapping-fact-completion-from-the-catalog.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [055](055-unique-constraint-as-a-carried-mapping-fact.md) a [127](127-unique-on-a-foreign-key-column-states-what-the-framework-has-no-other-place-for.md), která obě tímto nahrazujeme; NHibernate 5.7.0, `nhibernate-mapping.xsd` (element `many-to-one`, atributy `unique` a `unique-key`) a referenční dokumentace, kapitola o asociacích; Jakarta Persistence 3.2, javadoc `UniqueConstraint.columnNames` a `JoinColumn.unique`; EF Core 10, `IndexAttribute`; položka *Unikátní omezení nad sloupcem cizího klíče, který mapuje jen navigace* v `open-items.md`

## Kontext

Rozhodnutí [055](055-unique-constraint-as-a-carried-mapping-fact.md) zavedlo unikátní omezení jako nesený fakt entity a dalo mu tvar `UniqueConstraint.PropertyNames` — **jména vlastností**, protože EF Core píše `nameof(Sku)` a NHibernate atribut na `<property>`. Rozhodnutí [127](127-unique-on-a-foreign-key-column-states-what-the-framework-has-no-other-place-for.md) pak určilo, co tvrdí `unique` nad sloupcem cizího klíče: v JPA omezení schématu, v NHibernatu kardinalitu. Obě nechala otevřený jeden případ, který mají společný: **omezení nad sloupcem, který v třídě mapuje jen navigace.** Takový sloupec vlastnost nemá — nese ho `Relation.ColumnPairs` —, a omezení nad ním proto dnes končí třemi cestami v témž záznamu `Incompleteness`:

- JPA `unique = true` na `@JoinColumn` či `<join-column>` vztahu `@ManyToOne` (rozhodnutí 127, třetí řádek tabulky);
- `@UniqueConstraint(columnNames = { "CustomerId" })` nad spojovacím sloupcem (`JpaMappingWriter.WriteUniqueConstraint`);
- omezení z katalogu nad sloupcem cizího klíče (`CatalogCompletion.CompleteUniqueConstraints`).

Čtvrtá cesta je horší, protože je tichá: `unique-key` na `<many-to-one>` NHibernate parser vůbec nečte, ačkoli je to tvrzení zdroje, a podle rozhodnutí [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) ticho není přípustné.

**Případ není okrajový.** Omezení nad cizím klíčem je běžný tvar schématu ve dvou podobách. Jednosloupcové nad N:1 („jedna adresa na zákazníka", kde vztah zůstává N:1 a unikátnost hlídá databáze), a hlavně **smíšené** — unikátnost v rámci rodiče: `UNIQUE (CustomerId, OrderNumber)`, „číslo objednávky je jedinečné u každého zákazníka". To druhé je nejčastější vícesloupcové omezení vůbec a smíšené je z definice: jedna jeho část je sloupec cizího klíče, druhá skalár. Kritérium F4 jmenuje unikátní omezení mezi tím, co se z katalogu čte, a F3 vícesloupcové cizí klíče; dnešní mez obojí u takových schémat ruší.

**Všechny tři anotační cíle takové omezení vyjádřit umějí** — mezera je jen v mezireprezentaci. Ověřili jsme to proti zafixovaným verzím:

- **EF Core** `[Index(nameof(A), nameof(B), IsUnique = true)]` jmenuje vlastnosti třídy, navigaci ne. Vlastnost cizího klíče ale builder EF Core už má: kde ji model nenese, dogeneruje ji podle rozhodnutí [012](012-foreign-key-rendering.md) (`{Navigace}{ČástKlíčeCíle}`) a píše nad navigaci `[ForeignKey]`. Index nad ní je tedy jen další použití jména, které builder už odvozuje.
- **Jakarta Persistence** `@UniqueConstraint(columnNames = …)` jmenuje sloupce a o tom, kterému atributu patří, nic neříká; spojovací sloupec je sloupec jako každý jiný.
- **NHibernate** má na `<many-to-one>` vedle `unique` i atribut `unique-key` (schéma `nhibernate-mapping.xsd` 5.7.0). Referenční dokumentace váže kardinalitu jen na `unique` — *„By allowing this to be the target of a property-ref, you can make the association multiplicity one-to-one"* —, kdežto `unique-key` jen přidá sloupce elementu do pojmenovaného unikátního klíče tabulky, tak jako na `<property>`. Sloupec cizího klíče tedy smí být členem omezení, aniž by se tím dotkl významu `unique`, který rozhodnutí 012 vyhradilo vlastnící straně 1:1. Proto rozhodnutí 055 omezení nad navigací v NHibernatu odmítlo: znalo jen `unique` a jeho druhý význam; `unique-key` dvojznačnost nemá.

## Zvažované varianty

1. **Ponechat vyslovenou mez.** Nic nestojí a záznam o mezi už existuje. Zamítáme: vzdává se faktu, který tři ze šesti cílů vyjádřit umějí, a právě u nejčastějšího tvaru vícesloupcového omezení. F4 by tím ve svém důkazním sloupci tvrdilo unikátní omezení, která katalog u běžného schématu nedoplní.

2. **Dogenerovat při čtení skalární vlastnost nad sloupcem cizího klíče** a omezení postavit na ní. Model by se nezměnil. Zamítáme: vlastnost by se objevila v mezireprezentaci, aniž ji zdroj deklaroval — přesně to, co fáze doplňování z katalogu (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) i čtení `@UniqueConstraint` odmítají jako vymýšlení člena. A propsala by se do všech cílů: v JPA a NHibernatu jako druhé, jen čtecí mapování téhož sloupce, které zdroj nenapsal. Dogenerovat vlastnost klíče je odpovědnost builderu cíle, který si ji vynucuje (rozhodnutí [006](006-flat-composite-key-rendering.md) a 012), ne čtení.

3. **Nést vedle jmen vlastností i jména sloupců.** `UniqueConstraint` by dostal druhý seznam pro sloupce bez vlastnosti. Zamítáme: jméno sloupce cizího klíče není během převodu stálé — JPA ho u nevysloveného spojovacího sloupce odvozuje až z klíče cíle, NHibernate z názvu vlastnosti, katalog ho doplní až po čtení — a EF Core chce vlastnost, ne sloupec, takže by každý builder musel sloupec zpátky přeložit na vztah. Ten překlad zná nejlépe čtení, které vztah právě čte; dělat ho pokaždé znovu v builderu je táž práce na horším místě. Smíšené omezení by navíc mělo dva seznamy a ztratilo pořadí svých částí.

4. **Navigace jako člen omezení.** `UniqueConstraint.PropertyNames` smí kromě vlastnosti jmenovat i **navigaci vlastnícího vztahu N:1 nebo 1:1**, a ta zastupuje **všechny sloupce jeho cizího klíče** v pořadí `ColumnPairs`. Model se tvarem nemění; mění se jen to, co smí jméno označovat. Je to týž princip jako u vztahů samých — entita se jmenuje jménem, ne odkazem (rozhodnutí [001](001-entity-reference-by-name.md)), a vztah se v entitě pozná podle své navigace (rozhodnutí [094](094-entity-identity-inside-a-conversion.md)) —, takže jméno přežije rozpouštění klíčových tříd, syntézu spojovacích entit i dohledání sloupců po čtení, které by odkaz na sloupec nepřežil.

## Rozhodnutí

**Volíme variantu 4.** Unikátní omezení nad sloupci cizího klíče jmenuje navigaci vztahu, který ten klíč drží, a navigace v něm zastupuje celý cizí klíč. Omezení tak může být jednočlenné nad navigací, smíšené (navigace a skaláry) i nad několika navigacemi.

**Člen, který je navigací.** Smí jím být jen navigace vztahu s rolí `Owning` a kardinalitou `ManyToOne` nebo `OneToOne` — jen ten drží sloupce v tabulce entity. Zastupuje sloupce cizího klíče všechny, nikdy jen část; pořadí sloupců uvnitř je pořadí párů (rozhodnutí 012), pořadí členů je pořadí zdroje. Skalár nad týmž sloupcem má přednost: kde třída mapuje sloupec cizího klíče i skalárem (idiom EF Core `CustomerId` vedle `Customer`, jen čtecí skalár JPA), omezení jmenuje skalár jako dosud.

**Omezení, jehož jediným členem je navigace vlastnícího 1:1, se nepřidává** a nic nehlásí. Tvrdí unikátnost, kterou nese kardinalita a kterou každý cíl píše z ní znovu (rozhodnutí 127, platí beze změny); přidat ho by vedlo k dvojímu zápisu téhož. Pravidlo stojí v `AbstractEntityBuilder.AddUniqueConstraint`, takže platí pro každý zdroj i pro katalog.

**Čtení.**

| Zdroj | Tvrzení | Co nástroj přečte |
|---|---|---|
| JPA, `orm.xml` | `unique = true` na jediném spojovacím sloupci `@ManyToOne` (i nevysloveného jména) | jednočlenné omezení nad navigací |
| JPA, `orm.xml` | `@UniqueConstraint` / `<unique-constraint>`, jehož sloupce pokrývají celé vyslovené spojovací sloupce vlastnícího `@ManyToOne`/`@OneToOne` | omezení, v němž ty sloupce zastupuje navigace |
| NHibernate | `unique-key` na `<many-to-one>`, nebo shodně na všech jeho vnořených `<column>` | navigace je členem skupiny, smíšené skupiny s `<property>` jsou jedno omezení v pořadí dokumentu |
| NHibernate | `unique="true"` na `<many-to-one>` | vlastnící 1:1 (rozhodnutí 012, beze změny) |
| katalog | `UNIQUE` nad sloupci, z nichž některé jsou celým cizím klíčem vlastnícího vztahu | omezení s navigací, `Supplied`; porovnání a `Conflict` podle 055 |

Katalog proto doplňuje unikátní omezení až po cizích klíčích: navigace se pozná podle párů, které fáze teprve dohledá.

**Zápis.**

| Cíl | Člen-navigace se píše jako |
|---|---|
| EF Core | vlastnosti cizího klíče, tytéž jména, která builder píše do `[ForeignKey]` — existující skalár, jinak dogenerovaná vlastnost podle 012 |
| Hibernate, EclipseLink | spojovací sloupce v `@Table(uniqueConstraints = @UniqueConstraint(columnNames = …))` — z párů, jinak výchozí jméno JPA `atribut_sloupecKlíče`, které builder píše i do `@JoinColumn` |
| NHibernate | `unique-key` na `<many-to-one>`, **i u jednočlenného omezení**, protože `unique` tam znamená 1:1; omezení bez jména dostane odvozené `UQ_{entita}_{členové}` se záznamem `Convention`, jako vícesloupcové podle 055 |
| Dapper, MyBatis | `Loss` z průniku deskriptoru (`NotExpressible`), beze změny |

**Co zůstává mezí, a proč.** Každý z těchto případů dostává `Incompleteness` kategorie `UniqueConstraint`, protože nedostatek je v modelu, ne v cíli:

- **Omezení nad částí vícesloupcového cizího klíče.** Navigace zastupuje celý klíč; jednotlivý sloupec složeného klíče by potřeboval člen na úrovni sloupce, tedy variantu 3 — a za tvar, který schéma téměř nepoužívá, ji neplatíme.
- **Omezení nad sloupcem jiné tabulky**: spojovací sloupce `@JoinTable` a sloupec jednosměrného `@OneToMany` s `@JoinColumn`. Sloupec patří tabulce dítěte nebo spojovací tabulky a omezení entitě, kterou čtení této entity nemá v ruce.
- **`@UniqueConstraint` nad spojovacím sloupcem, jehož jméno zdroj nevyslovil.** Výchozí jméno JPA se odvozuje z klíče cíle až po čtení a sloupec, který by se jen jménem podobal, nesmíme za spojovací vydávat. `@JoinColumn(unique = true)` na takovém sloupci se čte, protože tam o atributu není pochyb.
- **Builder, který sloupce navigace nezná**: EF Core bez párů nemá jméno vlastnosti cizího klíče, kterou by index jmenoval (konvenční stínovou vlastnost anotace pojmenovat neumí), JPA bez párů i bez klíče cíle nemá sloupce. NHibernate tu mez nemá, `unique-key` platí i pro sloupec, který si framework odvodí z názvu vlastnosti.

**Přebíráme beze změny všechno ostatní z rozhodnutí 055 a 127**, a jejich odůvodnění zůstává čitelné tam:

1. Unikátní omezení je fakt entity, jmenuje členy jménem, název je nepovinný, totožnost dává množina (`CoversSameAs`), kategorie `MappingFactCategory.UniqueConstraint` je u EF Core, NHibernatu a JPA `Expressible`, u Dapperu a MyBatisu `NotExpressible` (z 055).
2. NHibernate píše omezení nad skaláry na `<property>`, `unique` u jednočlenného, `unique-key` u vícesloupcového, bezejmenná skupina dostane odvozený token se záznamem `Convention`; omezení nad částí klíče, verzí či transientní vlastností je `Loss` (z 055 — mění se jen to, že navigace mezi takové členy už nepatří).
3. Priorita zdrojů a doplňování z katalogu podle 015 a [017](017-source-precedence-for-mapping-facts.md), sousední atributy (`check`, `default`, neunikátní index) jako `Loss` bez kategorie (z 055).
4. `unique` nad sloupcem cizího klíče je v JPA zkratka za `@UniqueConstraint`, v NHibernatu kardinalita; pod `@OneToOne` je důsledkem kardinality; nad skalárem vedle vztahu se nese jako jeho omezení; zápis 1:1 se nemění (z 127 — mění se jen třetí řádek jeho tabulky, který je nyní druhým řádkem tabulky čtení výš).

## Důsledky

**Rozhodnutí 055 a 127 dostávají stav `nahrazeno 130` celá**, ačkoli se z každého mění jediná věta: nahrazené rozhodnutí se nepřepisuje a čtenář, který v 055 najde „omezení nad navigací je v NHibernatu `Loss`" nebo v 127 „nad sloupcem bez skaláru `Incompleteness`", má z hlavičky poznat, že to neplatí. Místa, která citují 055 nebo 127 jako původ volby přebrané výčtem výš, platí dál.

**Model se tvarem nemění**, mění se dokumentace `UniqueConstraint.PropertyNames` a jedno pravidlo v `AddUniqueConstraint`. Dotčena jsou místa, která omezení čtou a píšou: `JpaMappingWriter`, `NHibernateXMLMappingParser`, `CatalogCompletion`, buildery EF Core, NHibernatu a vrstvy JPA. Deskriptory, orchestrace a REST kontrakt zůstávají; žádný nový framework se tím nedotýká společné vrstvy (S1).

**Převody, které dnes omezení ztrácely, ho nesou**: Hibernate → EF Core dá `[Index(nameof(CustomerCustomerID), IsUnique = true)]` nad dogenerovanou vlastností, EF Core → NHibernate s katalogem `unique-key` na `<many-to-one>`, NHibernate → Hibernate `@UniqueConstraint` nad spojovacím sloupcem. Převod NHibernate → NHibernate jednočlenného omezení nad navigací vrátí `unique-key` místo `unique` — správně, protože `unique` by tvrdil 1:1, který zdroj neřekl.

**Ověření** patří na druhý a třetí stupeň rozhodnutí [016](016-generated-artifact-verification-levels.md): EF Core musí z artefaktu postavit model s unikátním indexem nad vlastností cizího klíče, NHibernate z mapování `SessionFactory`, v jehož konfiguraci je sloupec cizího klíče členem unikátního klíče tabulky a asociace zůstala N:1.

**F4 a F3** se v `traceability.md` zpřesňují o omezení nad cizím klíčem; `subset.md` místo dnešní meze vede užší mez výš. Kontrakt REST se nemění tvarem ani dokumentem; číslo vydání určí rozhodnutí [098](098-the-number-is-decided-once-per-release.md) při vydání.
