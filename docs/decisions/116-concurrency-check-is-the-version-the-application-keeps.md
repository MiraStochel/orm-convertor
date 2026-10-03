# 116 — `[ConcurrencyCheck]` nad jedinou celočíselnou či datočasovou vlastností je sloupec verze, jehož hodnotu udržuje aplikace: mezireprezentace nese, kdo verzi zvyšuje, a cíl, který ji zvyšuje sám, převzetí ohlásí

Datum: 2026-10-03
Stav: platí
Požadavky: F5, F10, F11, S1, T3
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [019](019-neutral-database-type-vocabulary.md), [030](030-scope-of-version-1-0.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [072](072-a-transient-property-is-a-carried-mapping-fact.md) a [075](075-unknown-language-type-is-a-reported-incompleteness.md); dokumentace EF Core, *Handling Concurrency Conflicts* (oddíly *Native database-generated concurrency tokens* a *Application-managed concurrency tokens*); referenční dokumentace NHibernate 5.7, element `version` a `timestamp`; specifikace Jakarta Persistence 3.2, §3.4.2 *Version Attributes*

## Kontext

**Sloupec verze je od rozhodnutí 030 mapovací fakt s jedním příznakem, `PropertyMap.IsVersion`.** Každý framework ho vyjadřuje svým mechanismem: EF Core anotací `[Timestamp]`, NHibernate elementem `<version>` nebo jeho zkratkou `<timestamp>`, JPA anotací `@Version`. Příznak říká, že sloupec nese token optimistické souběžnosti — hodnotu, kterou framework při zápisu porovná s tou, kterou četl, a při neshodě zápis odmítne. Co příznak neříká, je **kdo vyrábí další hodnotu**. A právě v tom se mechanismy liší: `[Timestamp]` značí sloupec, jehož hodnotu vyrábí databáze (`rowversion` SQL Serveru; EF Core ho čte jako generovaný při vložení i aktualizaci a nikdy ho nezapisuje), `<version>` a `@Version` značí sloupec, který **zvyšuje framework sám** — NHibernate i poskytovatel JPA do něj při každé aktualizaci zapíší hodnotu o jedna vyšší nebo aktuální čas, a aplikace se ho nemá dotýkat (specifikace JPA §3.4.2 to zakazuje výslovně). Buildery dnes rozdíl odvozují z typové rodiny: binární verze je tvar `rowversion`, kterou vyrábí databáze, všechno ostatní zvyšuje framework. EF Core builder proto nebinární verzi píše jako `[ConcurrencyCheck]` se záznamem `Loss`, protože jeho anotace inkrementaci vyslovit neumějí a `[Timestamp]` by nad `int` tvrdil, že hodnotu vyrábí databáze — čímž by EF Core sloupec přestal zapisovat a souběžnost by nechránila nic.

**EF Core má vedle toho třetí mechanismus, a ten dnes nečteme.** `[ConcurrencyCheck]` dělá z vlastnosti token souběžnosti: EF Core její původní hodnotu přidá do `WHERE` každého `UPDATE` a `DELETE` a při nule dotčených řádků vyhodí `DbUpdateConcurrencyException`. Novou hodnotu **nevyrábí nikdo než aplikace** — dokumentace tomu říká *application-managed concurrency token* a ukazuje aplikaci, která před uložením dosadí nový `Guid`, nebo program, který sám zvyšuje číslo. Anotace smí stát na libovolném počtu vlastností libovolného typu: `[ConcurrencyCheck] public string LastName` je legitimní zápis, který chrání jeden sloupec před souběžnou změnou, a verzí v žádném smyslu není. Parser EF Core ji dnes nechává propadnout do větve neznámé anotace a vydá `Loss` „nemá protějšek v mezireprezentaci" (rozhodnutí 048). Převod není tichý, takže F11 drží; ale dvě věci nedrží:

- **Náš vlastní výstup nejde přečíst zpět.** Verze, kterou zdroj zvyšuje sám, odejde do EF Core jako `[ConcurrencyCheck]` nad `int` nebo `DateTime`; ten samý artefakt vložený jako zdroj verzi ztratí do každého cíle. Převod NHibernate → EF Core → NHibernate skončí bez `<version>`, a to je přesně druh nestability, kterou T3 měří a kterou kategorie dotazů v [`open-items.md`](../open-items.md) u mapování staví jako hotovou: co nástroj píše, má také číst.
- **Záznam lže o důvodu.** „Anotace nemá protějšek v mezireprezentaci" neplatí: protějšek — sloupec verze — model má a v opačném směru ho na tutéž anotaci používá. Pravý důvod je, že anotace je víceznačná a čtení si musí vybrat; to má záznam říkat.

Otázka tedy není, jestli `[ConcurrencyCheck]` do modelu patří, ale **kdy je verzí a co se stane s tím, kdo ji zvyšuje**, až z ní cíl udělá `<version>` nebo `@Version`, které zvyšuje framework.

## Zvažované varianty

### 1 — Nechat ztrátu a zapsat ji do `subset.md` jako vyslovenou mez

Nejlevnější: změnit jen text záznamu, aby jmenoval pravý důvod, a přidat řádek do záporné půlky katalogu. Zamítáme. Vyslovená mez je nástroj pro konstrukce, které cíl nevyjádří nebo které by změnily význam; tady cíl protějšek má — všech pět ostatních frameworků umí nést verzi nad `int` — a náš vlastní EF Core builder tu anotaci píše. Mez, která platí jen proto, že parser nečte, co builder píše, není mez podmnožiny, ale nedodělek, a T3 by ji měřil jako nestabilitu zpětného převodu, kterou bychom si způsobili sami.

### 2 — Číst ji jako verzi, tedy nastavit jen `IsVersion`

Varianta (a) z otevřené položky: stojí-li `[ConcurrencyCheck]` na jediné číselné či datočasové vlastnosti, nastavit `IsVersion` a vyslovit, že cíl inkrementaci přidá. Zamítáme, protože model by nesl tvrzení, které zdroj neřekl. `IsVersion` nad `int` dnes v každém builderu znamená „verze, kterou zvyšuje framework", a EF Core řekl opak — zvyšuje aplikace. Následky by byly dva. Zpětný převod EF Core → EF Core by vydal `[ConcurrencyCheck]` se záznamem `Loss` „zdroj uvádí verzi, kterou framework zvyšuje sám, EF Core inkrementaci nevysloví" — záznam o ztrátě tam, kde se nezměnilo ani písmeno, a nepravdivý v první větě. A NHibernate i JPA by z tokenu udělaly verzi, kterou zvyšují samy, **beze slova**: výstup by říkal něco, co zdroj neřekl, což je podle rozhodnutí 004 a slovníku druhů záznamu událost `Convention` — jenže builder by o ní nevěděl, protože model mu rozdíl nepřinesl. Záznam by musel vydat už parser, který ale neví, do kterého cíle se jde, a hlásil by tedy něco, co se v daném běhu třeba vůbec nestane.

### 3 — Samostatný fakt tokenu souběžnosti bez inkrementace, nezávislý na verzi

Varianta (b) z položky doslova: nový příznak „token souběžnosti" vedle `IsVersion`, který by nesl každý `[ConcurrencyCheck]`, kolik jich na entitě je a jakéhokoli typu. Zamítáme. Token bez inkrementace nemá v žádném jiném frameworku protějšek po sloupcích: NHibernate má `optimistic-lock="all|dirty"` na třídě a `optimistic-lock="false"` na vlastnosti, Hibernate `@OptimisticLocking(type = DIRTY)` s `@DynamicUpdate`, obojí je politika celé entity, ne vybraného sloupce, a JPA, EclipseLink, Dapper ani MyBatis nemají nic. Fakt by se nesl jen proto, aby ho každý cíl kromě EF Core vydal jako `Loss` — a protože by anotace smělo být víc, model by potřeboval seznam místo příznaku. Jediný případ, který protějšek má — jediný token nad celočíselnou či datočasovou vlastností — je přitom verze ve všem kromě toho, kdo ji zvyšuje. Pro něj je samostatný fakt zbytečně široký a pro zbytek zbytečně prázdný.

### 4 — Verze s příznakem, že ji udržuje aplikace; cíl, který ji zvyšuje sám, převzetí ohlásí

## Rozhodnutí

**Volíme variantu 4.** `PropertyMap` dostává vedle `IsVersion` druhý kladný příznak, **`IsApplicationManagedVersion`**: hodnotu verze nevyrábí databáze ani framework, ale aplikace. Je to upřesnění faktu verze, ne fakt nový — sám o sobě neznamená nic a buildery se na něj ptají teprve u vlastnosti s `IsVersion`. Jako každý příznak modelu je kladný: `false` znamená, že to nikdo netvrdil, a model nic nevaliduje.

**Parser EF Core čte `[ConcurrencyCheck]` jako verzi, kterou udržuje aplikace, právě tehdy, když:**

1. anotaci nese **jediná vlastnost třídy** — EF Core dovolí víc tokenů, ale verze je na entitě jedna (NHibernate i JPA ji tak definují, rozhodnutí 030), a dva tokeny nejsou dvě verze, ale ochrana dvou sloupců;
2. ve třídě **není `[Timestamp]`** — kde verzi vyrábí databáze, je token navíc ochranou dalšího sloupce, ne druhou verzí; `[ConcurrencyCheck]` na téže vlastnosti jako `[Timestamp]` neříká nic nového a čte se mlčky;
3. jazykový typ vlastnosti je **`short`, `int`, `long` nebo `DateTime`**, s otazníkem i bez — přesně typy, nad kterými cílové frameworky verzi vedou (NHibernate `Int16`, `Int32`, `Int64`, `Timestamp`; JPA `short`, `int`, `long` a jejich obaly, `Timestamp`, `LocalDateTime`). `Guid` z kanonického příkladu dokumentace, řetězec, `decimal`, `bool` ani pole bajtů verzí v žádném cíli nejsou; pole bajtů je navíc tvar `rowversion`, u kterého EF Core píše `[Timestamp]`, a token nad ním, který udržuje aplikace, by cíl EF Core převedl na hodnotu z databáze.

Splněné podmínky nastaví `IsVersion` i `IsApplicationManagedVersion` a **nevydají žádný záznam**: čtení je přesné, model nese právě to, co zdroj řekl. Nesplněná podmínka vydá `Loss` kategorie `VersionColumn`, jehož důvod jmenuje anotaci a podmínku, která padla — tím záznam přestane tvrdit, že model protějšek nemá.

**Co s ní udělá cíl, říká cíl, protože tam se něco mění:**

| Cíl | Verze, kterou udržuje aplikace | Záznam |
|---|---|---|
| EF Core | `[ConcurrencyCheck]`, typ a sloupec jako u každé vlastnosti | žádný — zpětný převod je doslovný |
| NHibernate | `<version>` bez `generated="always"` i nad binární rodinou; `<timestamp>` se nepíše, protože ten NHibernate razítkuje sám | `Convention`, kategorie `VersionColumn`: NHibernate zvyšuje `<version>` sám, inkrementace přechází z aplikace na framework |
| Hibernate, EclipseLink | `@Version` | `Convention` téhož znění: poskytovatel verzi zvyšuje sám a aplikace ji podle specifikace nesmí měnit |
| Dapper, MyBatis | nic | `Loss` z deskriptoru jako u každé verze (rozhodnutí 004) |

Záznam `Convention` je tu přesně na místě podle definice druhu: *výstup říká něco, co zdroj neřekl, protože mezeru vyplnila konvence cíle*. Zdroj řekl „porovnej při zápisu", cíl k tomu přidal „a zvyšuj", protože jinak verzi nenese. Není to ztráta — nic ze zdroje nechybí — a není to tichá změna, protože ten, kdo ve zdrojové aplikaci verzi zvyšoval ručně, se dozví, že po převodu to dělá framework a jeho kód by ji zvyšoval dvakrát (NHibernate) nebo by poskytovatel jeho zápis zahodil či odmítl (JPA).

**Verze, kterou zvyšuje framework, se do EF Core píše jako dosud:** `[ConcurrencyCheck]` se záznamem `Loss`, protože anotace EF Core inkrementaci vyslovit neumějí a aplikace ji musí převzít. Rozhodnutí tedy rozděluje dosavadní jednu větu na dvě pravdivé: z frameworku na aplikaci je to zúžení a hlásí se jako `Loss` v EF Core builderu; z aplikace na framework je to doplnění a hlásí se jako `Convention` v builderu NHibernate a JPA. Binární verze zůstává tím, čím je — hodnotou z databáze, `[Timestamp]` a `generated="always"` — a příznak aplikace ji z toho vyjímá jen tam, kde by ho někdo do modelu vložil ručně; parser EF Core ho nad polem bajtů nenastaví (podmínka 3).

**Proč příznak, a ne výčet „kdo vyrábí hodnotu".** Tři výrobce — databáze, framework, aplikace — by vyslovil výčet lépe než dva příznaky. Nezavádíme ho, protože by vznikla hodnota, kterou žádný zdroj nevysloví přesně: NHibernate `<version>` bez `generated` znamená podle dokumentace „zvyšuje NHibernate", ale nad binární rodinou je to hodnota z databáze, ať atribut říká cokoli, protože binární typ zvýšit nelze — NHibernate sám v `IVersionType.Next` vrací původní hodnotu. Parser hbm.xml by tedy musel typovou rodinu znát dřív, než ji sdílený builder rozřeší, nebo vyslovit nepravdu, kterou by builder zase přebíjel rodinou. Dnešní odvození „binární = databáze, jinak framework" je správné pro každý zdroj kromě EF Core s `[ConcurrencyCheck]`, a právě ten jediný rozdíl příznak nese. Výčet může přijít, až ho bude mít kdo vyslovit — druhý dialekt (rozhodnutí 086) nebo `source="db"` u `<timestamp>`.

**Hranice.** Rozhodnutí nečte `[ConcurrencyCheck]` z fluent API (`IsConcurrencyToken()`), protože `OnModelCreating` se nečte vůbec (rozhodnutí 015, §5); nečte `[Timestamp]` nad jiným než binárním typem jinak než dosud (rozhodnutí 075); a nemění nic v doplňování z katalogu, které dodává verzi jen nad sloupcem `rowversion` a o tom, kdo zvyšuje číselný sloupec, nemá odkud vědět.

## Důsledky

- `Model.AbstractRepresentation.PropertyMap` dostává `IsApplicationManagedVersion`; `AbstractEntityBuilder.SetPropertyDatabaseMapping` čte klíč `ApplicationManagedVersion` stejně kladně jako `IsVersion`.
- `EFCoreEntityParser` čte `[ConcurrencyCheck]` podle tří podmínek výše; nesplněná podmínka vydá `Loss` kategorie `VersionColumn` s jmenovaným důvodem místo záznamu o neznámé anotaci.
- `EFCoreEntityBuilder` píše verzi, kterou udržuje aplikace, jako `[ConcurrencyCheck]` bez záznamu; `[Timestamp]` a ztrátu inkrementace nechává verzi, kterou vyrábí databáze nebo zvyšuje framework.
- `NHibernateEntityBuilder` a `AbstractJpaEntityBuilder` ji píší jako `<version>` a `@Version` se záznamem `Convention` kategorie `VersionColumn`.
- `Tests/Combined/VersionColumnTest` pokrývá čtení, zpětný převod, všechny tři podmínky a oba cíle, které inkrementaci přebírají; `architecture.md` §4 a §5 a `subset.md` 1.5, 1.6 a 2.2 popisují nový stav.
- Co se nemění: binární verze, verze zvyšovaná frameworkem, doplnění z katalogu, Dapper a MyBatis.
