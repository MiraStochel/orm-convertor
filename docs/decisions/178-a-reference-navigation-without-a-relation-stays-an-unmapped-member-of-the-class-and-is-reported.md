# 178 — Referenční navigace bez vztahu zůstává členem třídy bez mapování a cíl ji hlásí; mapper MyBatisu ji nepíše ani jako `<result>`, ani jako `<association>`

Datum: 2026-10-24
Stav: platí
Požadavky: F11, F10, F6, T3
Podklad: zadání ([`requirements.md`](../requirements.md), F11: „Nepodporované konstrukce nesmí být potichu vynechány", F6: doplnění z katalogu, T3: úplnost přenesených mapovacích vlastností); rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [009](009-target-framework-descriptor.md), [010](010-diagnostics-as-returned-data.md), [012](012-foreign-key-rendering.md), [015](015-mapping-fact-completion-from-the-catalog.md), [040](040-boundary-of-the-handed-over-artifact.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [072](072-a-transient-property-is-a-carried-mapping-fact.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md) a [177](177-a-property-of-an-excluded-construct-leaves-the-model-and-ef-core-does-not-map-a-navigation-without-a-principal-key.md); měření javové půlky kontroly experimentálního běhu nad případovými studiemi (2026-10-24): továrna MyBatisu 3.5.19 odmítla mapper výňatku Chinooku i JPetStore větou „Error parsing Mapper XML … No typehandler found for property Artist", resp. `product`; otevřená položka „Mapper MyBatisu s referenční navigací MyBatis odmítne"

## Kontext

Javová půlka kontroly experimentálního běhu nad případovými studiemi našla artefakt, který nástroj vydá bez `Failure` a cíl odmítne: mapper MyBatisu výňatku Chinooku (vrstva Dapper) nese `<result column="Artist" property="Artist" />` a mapper JPetStore totéž pro `Item.product`. Továrna MyBatisu dokument odmítne už při čtení — pro doménovou třídu nemá type handler —, takže entita s referenční navigací neprojde třetím stupněm a všechny dotazy nad ní s ní.

Tvar, který to způsobí, je **referenční navigace bez vztahu**: vlastnost, jejíž typ jmenuje jinou entitu převodu, za kterou model nenese žádný `Relation`. Vzniká ze dvou míst a v MyBatisu z obou najednou:

- **Zdroj vztah nevysloví.** Dapper navigaci nemapuje vůbec, čtení bere strukturu třídy (rozh. 048); prostá javová třída MyBatisu ji nenese také, a mapper JPetStore `<resultMap>` nemá — vnořené mapování dělá aliasem `"product.productId"` v SQL, které rozhodnutí 084 mapováním nečte. EF Core by z holé `public Artist Artist` udělal konvenční navigaci (rozh. 067); Dapper a MyBatis žádnou takovou konvenci nemají, a navigace tedy do modelu vstupuje jako obyčejná vlastnost s referenčním typem.
- **Katalog vztah nezaloží.** Fáze doplnění (rozh. 015) umí z cizího klíče tabulky založit N:1 nad referenční navigací — Chinook do NHibernatu tak dostane `<many-to-one name="Artist" column="ArtistId" />`. Poptává ale jen fakta, která cíl *vyžaduje* nebo *umí* (rozh. 009, 015), a deskriptor MyBatisu má `ForeignKeyColumns` jako `NotExpressible` (rozh. 084): pro cíl MyBatis se cizí klíč nepoptá, vztah nevznikne a navigace dojde až do kroku vlastností. V cílech NHibernate, Hibernate a EclipseLink zbývá týž tvar jen tam, kde katalog není, cizí klíč v něm není nebo cílová entita nemá klíč.

V kroku vlastností pak každý ze čtyř builderů s mapováním udělal totéž: napsal navigaci jako sloupec pojmenovaný po ní. MyBatis `<result column="Artist" property="Artist" />`, NHibernate `<property name="artist" />`, JPA `@Column(name = "artist") private Artist artist;`. Každý z těch zápisů **tvrdí sloupec, který žádný zdroj nevyslovil a žádná tabulka nemá**, a žádný z nich nenese záznam — jméno sloupce se bere jako univerzální výchozí hodnota rozhodnutí 067, která záznam nemá, protože obvykle nic netvrdí. Zde tvrdí. To je přesně vada ve smyslu `open-items.md`: artefakt, který cíl odmítne (MyBatis, měřeno) nebo který tiše znamená něco jiného (sloupec `artist` nad entitou).

Kolekční navigaci bez vztahu tytéž čtyři buildery řeší už dnes jedním způsobem ([`subset.md`](../subset.md), řádek *kolekční vlastnost bez vztahu*; rozh. 048 a 177): člen na třídě zůstává, uzavřené mapování ho nenese (JPA `@Transient`) a cíl hlásí `Incompleteness`. Referenční navigace bez vztahu je sourozenec téhož tvaru a dosud jediný, který odpověď neměl. Rozhodnutí 177 ji výslovně nechalo stranou („referenční navigaci v mapperu MyBatisu — vlastní otevřená položka").

## Zvažované varianty

1. **Psát ji jako dnes, sloupec pojmenovaný po navigaci.** Zamítáme z důvodů kontextu: výstup tvrdí sloupec, který neexistuje, bez záznamu; MyBatis ho odmítne a vezme s sebou dotazy nad entitou; F11 zakazuje právě tohle.

2. **V MyBatisu `<association>` nad cizím klíčem.** Je to tvar, který otevřená položka nabízela, a zamítáme ho ze tří důvodů, z nichž každý stačí sám. Za prvé není z čeho: za navigací nestojí vztah, takže builder cizí klíč nezná — a kdyby ho znal, je to vztah, který rozhodnutí 084 do mapperu entity nepíše vůbec (`vztah → nevypisuje se`, s `Loss`), protože MyBatis vztah vyslovuje jen uvnitř tvaru výsledku příkazu. Psát `<association>` jedině v případě, kdy vztah *chybí*, by obrátilo pořadí informací: méně známé by dostalo bohatší zápis. Za druhé `<association>` bez vnořených sloupců potřebuje buď `select` (vnořený příkaz, který by jmenoval tabulku, již deskriptor MyBatisu nevyjádří, a mapper entity žádný příkaz nenese — rozh. 084), nebo `resultMap` jiné entity s `columnPrefix`, který platí jen pro příkaz, jenž skutečně připojí druhou tabulku; mapper entity o příkazech neví. Za třetí by to byla konstrukce, kterou MyBatis → MyBatis zpětně čte jako navigaci bez sloupců (rozh. 084), takže by round-trip nic nezískal.

3. **Poptat cizí klíč u katalogu i pro cíl, který ho nevyjádří,** aby vztah vznikl a navigace šla cestou rozhodnutí 084. Zamítáme: porušuje dělbu rozhodnutí 015 (katalog se ptá jen na to, co cíl vyžaduje nebo umí) kvůli výsledku, který je co do artefaktu totožný — člen na třídě, nic v mapování; lišil by se jen druh záznamu (`Loss` kategorie `ForeignKeyColumns` místo `Incompleteness`). A nepomohlo by to tam, kde katalog není nebo cizí klíč v něm chybí, tedy přesně v případech, které zbývají ostatním třem cílům.

4. **`<result>` s `typeHandler`.** Zamítáme: type handler je třída, kterou by artefakt nenesl a nástroj by si ji musel vymyslet; hranice předaného artefaktu (rozh. 040) ji vylučuje, a sloupec, nad nímž by handler pracoval, tabulka stejně nemá.

5. **Člen ze třídy vypustit.** Zamítáme ze stejného důvodu jako rozhodnutí 177 u navigace bez klíče: navigace do třídy patří, její typ jmenuje entitu převodu a konzumentský kód, který člen plní, by se rozbil kvůli faktu, který nástroj ví vyslovit jinak.

6. **Navigaci bez vztahu nechat členem třídy, mapování ji nenese a cíl ji hlásí — odpověď, kterou kolekce bez vztahu už má.**

## Rozhodnutí

**Referenční navigace bez vztahu se v cílech s mapováním — MyBatis, NHibernate, Hibernate, EclipseLink — chová jako kolekce bez vztahu: člen na třídě zůstává, uzavřené mapování ho nenese a builder cíle hlásí `Incompleteness` u vlastnosti se jménem cílové entity.** MyBatis nepíše `<result>`, NHibernate nepíše `<property>`, oba buildery JPA píší `@Transient`. Záznam říká, že za odkazem na entitu nestojí vztah, takže mapování není z čeho postavit, a že člen zůstává na třídě nemapovaný; `Incompleteness`, ne `Loss`, protože se nic neztrácí — zdroj o navigaci nevyslovil žádný fakt, který by výstup mohl zahodit, chybí fakt, který by vyslovit měl a který katalog umí dodat (F6).

**Vztah, který za navigací stojí, se nemění.** Rozhodnutí 084 (MyBatis `vztah → nevypisuje se` s `Loss`), 012 (NHibernate `<many-to-one>`) a vrstva JPA (`@ManyToOne`) platí beze změny; tohle rozhodnutí přidává jen chybějící větev pro vlastnost, kterou žádný vztah nenárokuje. Skalární sloupec cizího klíče vedle navigace (`ArtistId`) zůstává obyčejným sloupcem, jak byl.

**Nerozhoduje se nic o typu mimo slovník.** Vlastnost, jejíž typ jmenuje třídu *mimo* převod, zůstává neznámým typem ([`subset.md`](../subset.md), řádek *typ mimo slovník*; rozh. 014 a 075): výstup opakuje jméno a hlásí to, a že to cíl v druhém ekosystému nezkompiluje, měří druhý stupeň. Tohle rozhodnutí se týká jen reference, kterou fáze rozřešení povýšila na entitu převodu.

**Dapper a EF Core se netýká.** Dapper mapování nemá a člen píše jako dosud; EF Core z holé navigace mezi entitami s klíčem dělá vztah svou konvencí a bez klíče hlavní strany ji nemapuje podle rozhodnutí 177.

## Důsledky

**Kód.** Tři buildery dostávají v kroku vlastností větev pro `LangTypeCategory.Reference` vedle větve pro `Collection`: `MyBatisEntityBuilder.BuildProperties`, `NHibernateEntityBuilder.BuildProperties`, `AbstractJpaEntityBuilder` (pro Hibernate i EclipseLink). Žádná změna v `AbstractWrappers`, v orchestraci ani ve fázi doplnění (S1).

**Změna chování, kterou je fér vyslovit.** Převod se zdrojem, který navigaci nese bez vztahu, vrací artefakt bez sloupce pojmenovaného po navigaci a o jeden záznam `Incompleteness` navíc na navigaci a cíl. V MyBatisu je to běžný případ každého zdroje Dapper a MyBatis s navigací, s katalogem i bez něj; v NHibernatu a v JPA jen případ bez katalogu, bez cizího klíče v něm nebo bez klíče cílové entity. Výňatky Chinooku a JPetStore tím v MyBatisu projdou továrnou; měří to javová půlka experimentálního běhu.

**T3.** Navigace bez vztahu nenese žádný fakt zdroje, takže v knize tvrzení není a jmenovatel úplnosti se nemění; zpětné čtení artefaktu JPA ji přečte jako `TransientProperty`, který zdroj nevyslovil — totéž, co dnes kolekce bez vztahu, a měření to vede jako fakt tvrzený artefaktem, ne referencí.

**Testy.** `MyBatis/MyBatisBuilderTest` (zdroj Dapper tvaru Chinooku), `Combined/UnreadMappingFactsTest` (zdroj JPA s holým polem entity do Hibernatu, EclipseLinku a NHibernatu); javová sada třetí stupeň MyBatisu nad případovými studiemi.

**Dokumenty.** [`subset.md`](../subset.md): řádek *kolekční vlastnost bez vztahu* se rozšiřuje o referenční navigaci; [`architecture.md`](../architecture.md): zápis NHibernatu (§5, kolekce), vrstvy JPA a MyBatisu; položka mizí z [`open-items.md`](../open-items.md); [`traceability.md`](../traceability.md), řádek F11.
