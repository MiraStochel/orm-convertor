# 206 — Klíč, který je zároveň cizím klíčem, píše vrstva JPA jako `@MapsId` nad zapisovatelným spojovacím sloupcem; složený klíč sdílený celý zůstává bez `@MapsId` s referencí jen pro čtení, v obou profilech stejně

Datum: 2026-11-05
Stav: platí
Požadavky: F7, F9, F10, T2, T3
Podklad: rozhodnutí [012](012-foreign-key-rendering.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md) a [179](179-eclipselink-carries-no-version-over-a-binary-column-so-the-store-generated-version-is-a-read-only-column-with-a-loss.md); [záznam experimentálního běhu 2026-11-05](../runs/2026-11-05-experiment.md); Jakarta Persistence 3.2 §2.4.1 (odvozená identita) a §11.1.40 (`@MapsId`); položka *Klíč, který je zároveň cizím klíčem, píše EclipseLink jen pro čtení* v `open-items.md`

## Kontext

Závislá entita může mít primární klíč, který je zároveň cizím klíčem reference na entitu nadřazenou — `CustomerProfile` s klíčem `CustomerId`, který ukazuje na `Customer`. Rozhodnutí 012 pojmenovalo tvar v NHibernatu (generátor `foreign`, `<one-to-one constrained="true">`) a s ním pravidlo pro sloupec, který mapuje skalár i vztah: sloupec píše jedno mapování a druhé je nad ním jen pro čtení. Sdílená vrstva JPA (rozh. 077) tvar poznává lokálně (`SharesPrimaryKey`: část klíče s generátorem `foreign` jmenující navigaci, nebo vztah 1:1, jehož sloupce jsou přesně sloupce klíče) a psala ho jako `@Id @Column` na základním poli a `@OneToOne @MapsId @JoinColumn(…, insertable = false, updatable = false)` na referenci — pravidlo 012 přenesené doslova, s `@MapsId` navíc.

[Experimentální běh 2026-11-05](../runs/2026-11-05-experiment.md) ukázal, že ten tvar není překlad: EclipseLink 5.0.0 nasazení jednotky odmítne (`EclipseLink-46`, *There should be one non-read-only mapping defined for the primary key field*), Hibernate 7.4.5 ji přijme. Příčinu jsme změřili nad připnutými verzemi (javová sada, `eclipselink/SharedKeyClaimsTest` a `hibernate/SharedKeyClaimsTest`, oba nad šesti ručně napsanými tvary, s nasazením i zápisem řádku):

| Tvar | EclipseLink 5.0.0 | Hibernate 7.4.5 |
|---|---|---|
| `@MapsId` + `@JoinColumn(insertable = false, updatable = false)` (dosavadní zápis) | **odmítne**, `EclipseLink-46` | přijme |
| `@MapsId` + zapisovatelný `@JoinColumn` | přijme; klíč zapíše jednou, z reference | přijme; totéž |
| bez `@MapsId`, `@JoinColumn(insertable = false, updatable = false)`, `@Id` píše sloupec | přijme; zapíše | přijme; zapíše |
| složený klíč (`@IdClass`) s jednou částí sdílenou, `@ManyToOne` jen pro čtení | přijme; zapíše | přijme; zapíše |
| složený klíč sdílený celý, `@MapsId` + zapisovatelné `@JoinColumns` | **odmítne**, `EclipseLink-48` (*Multiple writable mappings exist for the field*) | přijme |
| složený klíč sdílený celý, bez `@MapsId`, `@JoinColumns` jen pro čtení | přijme; zapíše | přijme; zapíše |

Z měření plyne význam `@MapsId`, který specifikace říká slovy (§2.4.1: identita závislé entity je *odvozená* ze vztahu): pod `@MapsId` je základní mapování `@Id` jen pro čtení — hodnotu bere ze vztahu — a **spojovací sloupec reference je to jediné mapování, které klíč zapisuje**. Označit ho `insertable = false, updatable = false` znamená, že klíč nezapisuje nikdo; EclipseLink to při nasazení vysloví, Hibernate příznaky nad odvozenou identitou mlčky nerespektuje. Nad `@IdClass` EclipseLink identitu neodvozuje — části `@Id` zůstávají zapisovatelné — a zapisovatelné spojovací sloupce jsou druhý zapisovatel týchž polí (`EclipseLink-48`). Pravidlo 012 tedy v JPA platí přesně tam, kde `@MapsId` není: skalár vedle vztahu nad týmž sloupcem, včetně složeného klíče, jehož část nebo celek vztah sdílí.

## Zvažované varianty

1. **Nechat zápis a vést rozdíl profilem** jako u rozh. 179 (`RefusesReadOnlyMapsId`): profil EclipseLinku by spojovací sloupec psal zapisovatelně, profil Hibernatu dál jen pro čtení. Rozdíl profilů je ale místo pro změřený rozdíl implementací nad správným zápisem — a tady je špatný zápis, který jedna implementace vymáhá a druhá toleruje: `@MapsId` se spojovacím sloupcem jen pro čtení popírá, co `@MapsId` podle specifikace znamená. Dvě cesty pro jeden tvar by zakryly vadu, ne vedly rozdíl. Zamítáme.

2. **Vypustit `@MapsId` a psát sdílený klíč jako obyčejný skalár vedle reference jen pro čtení** (třetí řádek tabulky), v obou profilech. Nasazení i zápis projdou v obou. Ztratí se ale fakt, který zdroj vyslovil: generátor `foreign` NHibernatu říká, že hodnota klíče *pochází z asociace*, a jediné slovo JPA pro to je `@MapsId`; bez něj musí aplikace klíč přiřadit sama a parser JPA čte vztah zpátky jen podle rovnosti sloupců, ne jako odvozenou identitu. Je to zápis z doby JPA 1.0, který specifikace od 2.0 nahradila. Zamítáme pro jednoduchý klíč; pro složený klíč je to jediný tvar, který oba cíle přijmou (viz rozhodnutí).

3. **`@MapsId` nad zapisovatelným spojovacím sloupcem pro klíč o jedné části; složený klíč sdílený celý bez `@MapsId` s referencí jen pro čtení — v obou profilech stejně.**

## Rozhodnutí

**Volíme variantu 3.** Sdílený builder (`AbstractJpaEntityBuilder.AppendOwningReference`) píše:

- **Klíč o jedné části sdílený s referencí** (`SharesPrimaryKey` a `PrimaryKey.Parts.Count == 1`): `@Id @Column(name)` na základním poli bez `@GeneratedValue`, na referenci `@MapsId` a `@JoinColumn(name = <sloupec klíče>, referencedColumnName = <klíč cíle>, nullable, unique)` **bez** `insertable = false, updatable = false`. Reference klíč zapisuje, identifikátor je odvozený — to je význam `@MapsId` a tvar, který obě implementace přijmou a zapíší.
- **Generátor `foreign` bez vysloveného sloupce** (NHibernate `<one-to-one constrained="true">` páry sloupců nemá): spojovací sloupec je vlastní sloupec klíče entity, párovaný s klíčem cíle o jedné části — totéž, co `@MapsId` znamená a co parser JPA čte zpátky (`JpaMappingWriter`: pod `@MapsId` jsou sloupce vztahu sloupce `@Id`). Dosud sem padalo odvozené `@JoinColumn(<navigace>_<sloupec>)` se záznamem `Convention`, tedy sloupec, který tabulka nemá; teď je to tvrzení zdroje bez záznamu.
- **Složený klíč, jehož část nebo celek vztah sdílí**: beze změny — `@IdClass`, části `@Id` píšou sloupce, reference je nad nimi `insertable = false, updatable = false` (pravidlo 012), `@MapsId` se nepíše. Jediný tvar, který EclipseLink nad `@IdClass` přijme, a v Hibernatu platný též.

Profil (`JpaImplementationProfile`) se nemění: není co vést jako rozdíl, oba profily píšou totéž. **Měření drží testy vedle zápisu:** `eclipselink/SharedKeyClaimsTest` (odmítnutí `46` a `48`, nasazení a zápis ostatních tvarů) a `hibernate/SharedKeyClaimsTest` (Hibernate přijme všech šest; zápis z reference), takže upgrade, který fakt změní, selže tam; xUnit drží zápis obou profilů a všech tří tvarů (`Hibernate/HibernateSharedKeyTest`).

## Důsledky

- **Tabulka T3 ztrácí nepřijatý řádek** `marketplace-fluent` ze zdroje EF Core do EclipseLinku; artefakt vrstvy JPA přijmou obě implementace. Měření úplnosti mapování (rozh. 173) čte spojovací sloupec pod `@MapsId` jako sloupce klíče, jak četlo dosud; čtení příznaků jen pro čtení (`ReportReadOnlyFlags`) mlčí nad sloupcem, který mapuje i jiný atribut třídy, takže oba tvary jdou zpět beze ztráty.
- **F7 i F9 platí pro sdílený klíč bez výhrady**; [`subset.md`](../subset.md) vede `@MapsId` v kladné polovině u sloupců cizího klíče a složený sdílený klíč jako tvar bez `@MapsId`.
- **Překlad NHibernate → JPA** s generátorem `foreign` dostává správný spojovací sloupec místo odvozeného jména; záznam `Convention`, který ho doprovázel, mizí.
- **Pravidlo 012 se nemění**, zpřesňuje se jeho hranice v JPA: jen pro čtení je skalár vedle vztahu *nebo* vztah vedle skaláru nad týmž sloupcem — ne vztah, který identitu odvozuje. NHibernate builder (`<one-to-one constrained="true">` bez sloupců) a EF Core builder (`[ForeignKey]`) zůstávají, jak jsou.
- **Dotčená místa:** `AbstractJpaEntityBuilder.AppendOwningReference`; `architecture.md` (§5, zápis vrstvy JPA), `subset.md` (sloupce cizího klíče), `traceability.md` (F7, F9).
