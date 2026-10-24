# 177 — Vlastnost, kterou mapuje konstrukce vyňaté oblasti, opouští model s jedinou ztrátou té konstrukce; navigaci, kterou EF Core bez klíče hlavní strany nezmapuje, píše builder jako `[NotMapped]` se záznamem

Datum: 2026-10-24
Stav: platí
Požadavky: F11, F10, T3, F6
Podklad: zadání ([`requirements.md`](../requirements.md), F11: „ověřit … omezení cílového frameworku … Nepodporované konstrukce nesmí být potichu vynechány", T3: „úplnost přenesených mapovacích vlastností"); [`use-cases.md`](../use-cases.md), UC1; rozhodnutí [010](010-diagnostics-as-returned-data.md), [014](014-language-type-model.md), [015](015-mapping-fact-completion-from-the-catalog.md), [030](030-scope-of-version-1-0.md), [031](031-key-class-as-declaration-of-key-parts.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [063](063-stated-keylessness-as-a-carried-fact.md), [072](072-a-transient-property-is-a-carried-mapping-fact.md), [075](075-unknown-language-type-is-a-reported-incompleteness.md), [124](124-direction-as-one-row-and-input-beside-output-paired-per-fact.md) a [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md); [`subset.md`](../subset.md), VO 2 a řádek *entita bez primárního klíče*; záznam kontroly [2026-10-24](../runs/2026-10-24-experiment-examples.md); měření `EFCore/EFCoreKeylessNavigationProviderTest` a `EFCore/EFCorePrimitiveCollectionProviderTest` nad EF Core 10.0.10; otevřená položka *Vlastnost vyňaté konstrukce a navigace bez klíče vycházejí do artefaktu, který cíl nepřijme*

## Kontext

Kontrola experimentálního běhu nad příklady (záznam 2026-10-24) našla dvě místa, kde nástroj vydá artefakt, o kterém sám ví, že ho cíl nepřijme, a řekne to nanejvýš záznamem. Vstupy, které na ně narazily, záznam upravil; chování nástroje zůstalo otevřené. Obě místa mají společné jádro: model drží člen třídy, ke kterému cíl nemá co napsat, a builder ho napíše, jako by to byl obyčejný sloupec nebo obyčejná navigace.

### Vlastnost vyňaté konstrukce

Model nese jen plochou třídu (rozh. 030); komponenty, kolekce hodnot, spojené tabulky a hodnotové typy jsou vyňatá oblast 2 a čtenáři je hlásí záznamem `Loss` (rozh. 048). Ztráta ale stojí u **anotace nebo prvku**, ne u vlastnosti: vlastnost zůstane v modelu s typem, který napsal zdroj, a s tím, co o ní zdroj řekl mimo konstrukci, a builder cíle ji vypíše jako každou jinou. Co z toho dnes vzniká, jsme změřili na výstupu nástroje:

| Zdroj → cíl | Vstup | Artefakt | Co z artefaktu udělá cíl |
|---|---|---|---|
| Hibernate → EF Core (výňatek Keycloaku) | `@ElementCollection @Column(name="VALUE") @CollectionTable(name="WEB_ORIGINS") Set<String> webOrigins`, totéž `redirectUris` nad `REDIRECT_URIS` | `[Column("VALUE")] public HashSet<string>? webOrigins` a `redirectUris` | primitivní kolekce ve sloupci `VALUE` tabulky `CLIENT` — sloupec, který zdroj umístil do kolekčních tabulek; dvě takové odmítnou celý model („both mapped to column 'VALUE' in 'CLIENT'") |
| Hibernate → EclipseLink | `@ElementCollection @Column(name="VALUE") Map<String, String> attributes` | `@Column(name = "VALUE") private Map<String, String> attributes` | artefakt tvrdí základní sloupec `VALUE` tabulky `CLIENT` |
| Hibernate → EclipseLink | `@ElementCollection Set<String> webOrigins` | `@Transient private Set<String> webOrigins` a `Incompleteness` | artefakt tvrdí, že člen se nepersistuje |
| Hibernate → C# cíle (výňatek Keycloaku) | `@ElementCollection Map<String, …>` | `Map<,>`, `Integer` opsané do C# | `CS0246` (záznam kontroly) |
| EF Core → NHibernate | `[Owned] class Address`, `Customer.Address` | `<many-to-one name="Address" class="Address" />` a entita `Address` odmítnutá `Failure` pro chybějící klíč | vztah a tabulka, které zdroj nevyslovil |
| NHibernate → NHibernate | `<component name="Address">` nad třídou mimo převod | člen `Address` a `<property name="Address" not-null="true" />` (rozh. 072 ho výslovně nečte jako nepersistovaný) | artefakt tvrdí sloupec `Address` tabulky entity tam, kde zdroj mapuje sloupce komponenty |

Jedna mezera tak dává podle tvaru typu čtyři různé výstupy, z nichž ani jeden neříká, co zdroj — sloupec jiné tabulky, nepersistovaný člen, vztah, nebo text, který cíl nezkompiluje —, a ke ztrátě konstrukce přibývají záznamy o každé anotaci pod ní a o nezařazeném typu (075): čtyři až pět záznamů o jedné věci. Záznam `Loss` přitom podle rozhodnutí 048 a podle svého druhu znamená, že „artefakt je platný, jen chudší než vstup". Tady chudší není, je jiný.

Dopadá to i na T3. Referencí úplnosti mapování je kniha tvrzení převodu (rozh. 124, 173) — fakta, která model drží při generování. Sloupec `VALUE` z `@Column` nad kolekcí prvků v modelu je, kniha ho vede jako nesený a zpětné čtení ho v artefaktu najde: metrika započte jako přenesený fakt, který zdroj o sloupci entity nikdy netvrdil.

### Navigace bez klíče

Dapper a MyBatis klíč nevysloví; bez katalogu vyjdou jejich entity do EF Core jako `[Keyless]` (rozh. 063), a to UC1 počítá: „bez katalogu doběhne jen zčásti". Navigační člen ale builder napíše jako obyčejnou vlastnost a konvence EF Core z něj udělá navigaci. Výňatek Chinooku z Dapperu dává `[Keyless] Album` s `Artist Artist` a `List<Track> Tracks` a `[Keyless] Artist` s `List<Album> Albums`; žádný záznam neříká, že navigace cíl nepřijme. Co s tím EF Core 10.0.10 udělá, jsme změřili jeho vlastní validací modelu (`EFCoreKeylessNavigationProviderTest`):

| Navigace | EF Core 10.0.10 |
|---|---|
| reference, kterou entita bez klíče drží jako závislá strana, na entitu s klíčem | zmapuje (cizí klíč `ArtistId`) |
| reference nebo kolekce **na** entitu bez klíče | odmítne celý model („Unable to determine the relationship represented by navigation 'Album.Artist'") |
| kolekce **na** entitě bez klíče | mlčky vynechá navigaci i cizí klíč |
| inverzní reference na entitě bez klíče (cizí klíč stojí na cíli) | vymyslí stínový sloupec cizího klíče na straně bez klíče |
| `[NotMapped]` nad navigací | model se postaví, vlastnost cizího klíče zůstává sloupcem |
| `Join` nad sloupci klíčů mezi dvěma entitami bez klíče | přečte (`INNER JOIN`) |

Pravidlo za tabulkou je jedno: vztah potřebuje klíč hlavní strany. Výňatek Chinooku tak do EF Core neskončí „zčásti", ale vůbec — odmítnutý model shodí každý dotaz převodu, i nad entitami, které žádnou navigaci nemají —, a F11 přitom žádá právě opak: omezení cílového frameworku ověřit před předáním výsledku.

## Zvažované varianty

### Vlastnost vyňaté konstrukce

1. **Vypisovat jako dnes.** Zamítáme z důvodů kontextu: výstup tvrdí něco jiného než zdroj, ztráta tvrdí, že fakt ve výstupu není, a kniha i zpětné čtení ho započtou jako přenesený.

2. **Vypisovat jako nepersistovanou** — `[NotMapped]`, `@Transient`, člen mimo mapování NHibernatu —, tedy odpověď, kterou buildery už dávají kolekci bez vztahu. Zamítáme. Zdroj vlastnost persistuje, jen jinam, než model umí nést; „nepersistováno" je tvrzení, které zdroj popírá, a rozhodnutí 072 ho proto u vlastnosti pod `<component>` výslovně odmítlo. Kniha by ho navíc vedla jako nesený fakt `TransientProperty`. A typ by zůstal zdrojový: `Map<String, String>` v C# nezkompiluje ani nepersistovaný.

3. **Přečíst konstrukci do ploché třídy** — komponentu rozpustit do sloupců vlastníka, kolekci prvků přečíst jako spojovací entitu. Zamítáme zde, ne navždy: je to zvednutí hranice VO 2, tedy změna toho, co verze nárokuje, a model by musel nést komponentu nebo kolekční tabulku bez entity — samostatné rozhodnutí o podmnožině, ne volba o tom, co nástroj dělá na její hranici.

4. **Vlastnost s konstrukcí z modelu vypustit a ztrátu konstrukce nechat jediným záznamem, který to říká.**

### Navigace bez klíče

1. **Nechat odmítnutý model jako obraz toho, že klíč má dodat katalog.** Zamítáme. Builder omezení cíle zná a F11 mu ho ukládá ověřit; odmítnutý model nedává UC1 „zčásti", ale nic; a pro Dapper je to navíc nepravdivý obraz zdroje — Dapper navigaci nemapuje vůbec, plní ji nanejvýš ruční multi-mapping v dotazu.

2. **`Failure` entity, která takovou navigaci nese.** Zamítáme. Entita sama zmapovatelná je — bezklíčová entita je v EF Core legitimní typ pro čtení (rozh. 063) — a odmítnout ji kvůli jednomu členu by vzalo i všechny dotazy nad ní; je to přísnější než NHibernate, který entitu bez klíče odmítá, protože ji jinak napsat nemůže.

3. **Navigaci ze třídy vypustit jako vlastnost vyňaté konstrukce.** Zamítáme. Navigace do ploché třídy patří, model ji nese a její typ jmenuje entitu převodu, takže se do C# napsat dá; chybí jen klíč, který katalog dodá. EF Core má pro „člen třídy, který model nečte" vlastní zápis, `[NotMapped]` (rozh. 072), a vypuštění by konzumentský kód, který člen plní, rozbilo kvůli faktu, který doplnění z katalogu vrátí.

4. **`[NotMapped]` nad navigací, kterou EF Core bez klíče nezmapuje, se záznamem.**

## Rozhodnutí

Volíme v obou částech variantu 4.

### Vlastnost vyňaté konstrukce opouští model

**Čtenář, který u vlastnosti pozná konstrukci vyňaté oblasti 2, vlastnost označí, a fáze sestavení ji z modelu odstraní i se vším, co o ní řekl kterýkoli zdroj.** Fáze běží po rozpuštění klíčových tříd a před konvenčními navigacemi, tedy dřív, než vlastnost přečte kterákoli další fáze nebo katalog; volá ji jako druhý krok fáze doplnění z katalogu, a kde katalog není, `Build()` — označení je nezávislé na pořadí jednotek jako `MarkTransient`. **Záznamem je jediná ztráta konstrukce** — táž událost, kterou čtenáři hlásili dosud, teď jedním důvodem pro všechny —, který jmenuje konstrukci a říká, že vlastnost z výstupu vypadla i s tím, co se o ní pod konstrukcí uvádí; anotace a prvky pod konstrukcí vlastní záznam nedostávají a nezařazený typ vypuštěné vlastnosti nedostane záznam rozhodnutí 075, protože k fázi rozresolvování jmen se nedostane.

| Zdroj | Konstrukce u vlastnosti |
|---|---|
| JPA (Hibernate, EclipseLink), anotace i `orm.xml` | `@ElementCollection` / `<element-collection>`, `@Embedded` / `<embedded>`, `@Column(table)` / `<column table>` |
| NHibernate | `<component>`, `<dynamic-component>`, `<idbag>`, `<array>`, `<primitive-array>`, `<any>`, kolekce bez `<one-to-many>` a `<many-to-many>` (hodnoty, komponenty) a každá vlastnost, kterou mapuje `<join>` |
| EF Core | primitivní kolekce (`List<string>`: prvek skalár či klíčové slovo C#) |

**Hodnotový typ není entita převodu.** Třída, kterou zdroj prohlásí za hodnotu jiných entit — EF Core `[Owned]` a `[ComplexType]`, JPA `@Embeddable` —, a třída, kterou drží komponentní konstrukce jiné entity — `<component>`, kolekce komponent, `@Embedded`, `@ElementCollection`, v konfiguraci EF Core `OwnsOne`, `OwnsMany`, `ComplexProperty` a `ComplexCollection` —, opouští tutéž fází seznam entit a vlastnosti, jejichž typ nebo prvek ji jmenuje, odcházejí jako vlastnosti vyňaté konstrukce. Fáze běží po rozpuštění klíčových tříd, takže `@Embeddable` klíčová třída (rozh. 031) se pravidla netýká. Třída dostane ztrátu u sebe, ať ji prohlásil zdroj, nebo ji drží konstrukce; vlastnost, kterou žádná konstrukce u sebe neoznačila (`Address Address` nad třídou `[Owned]`), dostane vlastní. Třída, kterou jen drží konstrukce jiné entity a která nese mapování vlastní — tabulku, klíč, vztah —, zůstává entitou, jako klíčová třída s vlastním mapováním v rozhodnutí 031.

**Co pravidlo nebere.** Vlastnost, kterou konstrukce mapuje jako sloupec vlastní tabulky entity — `<natural-id>`, `@Enumerated`, `@Lob` —, je sloupcem ploché třídy; ztrácí se jen to, co konstrukce dodává, a hranice rozhodnutí 030 zůstává, jak je. Dědičnost a `@MappedSuperclass` jsou konstrukce třídy, ne vlastnosti, a jejich záznamy platí beze změny.

### Navigaci bez klíče hlavní strany EF Core builder nemapuje

**EF Core builder napíše `[NotMapped]` u navigace, kterou EF Core 10.0.10 bez klíče nezmapuje: u každé navigace, jejíž cílová entita nemá klíč, a u navigace entity bez klíče, která není referencí její vlastnící strany — kolekce nebo inverzní reference.** „Bez klíče" je přesně podmínka, za které builder píše `[Keyless]`: prázdný klíč modelu. Navigace stojící na vztahu i navigace bez vztahu (typ jmenuje entitu převodu, vztah nevyslovil nikdo) se posuzují stejně. Vlastnosti cizího klíče se píšou dál jako sloupce, `[ForeignKey]` ne, a model se postaví; dotazy pravidlo nemění, protože LINQ builder spojuje přes sloupce klíčů, ne přes navigace, a takový join EF Core nad entitami bez klíče přečte.

**Pravidlo píše jeden záznam na navigaci a jeho druh dává to, co model drží.** Stojí-li za navigací vztah modelu, je to `Loss` kategorie `ForeignKeyColumns`: fakt modelu z výstupu vypadl, a kniha tvrzení ho proto jako nesený nevede — jako padlý, má-li vztah spárované sloupce, jinak vůbec, protože sloupce se nemají s čím spárovat. Nestojí-li, je to `Incompleteness` bez kategorie: modelu nic nepadlo, chybí mu klíč, který by výstup potřeboval. Důvod v obou jmenuje entitu bez klíče, říká, že EF Core bez klíče hlavní strany vztah nezmapuje, a že klíč dodá katalog (rozh. 015). S klíčem pravidlo mlčí, takže artefakt převodu s katalogem se nemění.

## Důsledky

**Kód.** `AbstractEntityBuilder` dostává označení vlastnosti a hodnotového typu a fázi, která je spotřebuje; fáze doplnění z katalogu ji volá po rozpuštění klíčových tříd. Čtenáři JPA (anotace, `orm.xml`), NHibernate a EF Core (třída i konfigurace) konstrukci, kterou poznají, označí místo vlastního záznamu, a záznam píše jednotným důvodem báze; EF Core builder dostává pravidlo navigace. Model se nemění, žádný druh záznamu ani kategorie nepřibývá a REST kontrakt zůstává; nový framework nic neplatí — jeho čtenář konstrukci označí, fázi zdědí (S1).

**Změna chování, kterou je fér vyslovit.** Převod se vstupem nesoucím vyňatou konstrukci vrací artefakt bez jejího členu a méně záznamů: jednu ztrátu místo ztrát po anotacích a záznamu o nezařazeném typu. Převod uvnitř ekosystému, který člen dosud nesl, ho ztrácí; konzumentský kód, který člen používá, přestane kompilovat a záznam řekne proč — místo aby kompiloval nad členem, který framework tiše nepersistuje nebo mapuje na cizí sloupec. Převod Dapperu nebo MyBatisu bez katalogu do EF Core vydá model, který se postaví; s katalogem je beze změny.

**T3.** Vlastnost vyňaté konstrukce do knihy tvrzení nevstupuje, a tedy ani do jmenovatele úplnosti mapování: T3 se ptá po přenesení *podporovaných* mapovacích vlastností a VO 2 podporovaná není; počitatelnou stopou zůstává ztráta. Vztah za nemapovanou navigací kniha jako nesený nevede a zpětné čtení ho nenajde, takže kniha a míra se o něj nepřou.

**Dokumenty.** [`subset.md`](../subset.md): řádek VO 2 a řádek *entita bez primárního klíče*; [`architecture.md`](../architecture.md): fáze sestavení, čtení JPA, NHibernate a EF Core, emise EF Core a §9; kořenový `README.md`, *Guarantees*, oblast 2; [`traceability.md`](../traceability.md), řádek F11. Výňatky převzatých projektů zůstávají, jak je upravil záznam kontroly; vrátit do nich vypuštěné kolekce prvků by znovu změnilo vstupy experimentu.

**Co rozhodnutí nerozhoduje.** Referenční navigaci v mapperu MyBatisu (vlastní otevřená položka); navigaci bez vztahu mezi dvěma entitami s klíčem, kterou EF Core doplní svou konvencí — tu zpětné čtení rozhodnutí 173 počítá jako vymyšlenou; a vlastnosti pod `<natural-id>`, jejichž sloupcová fakta parser nečte.
