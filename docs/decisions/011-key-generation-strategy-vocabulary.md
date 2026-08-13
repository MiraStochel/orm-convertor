# 011 — Slovník strategií generování klíče

Datum: 2026-08-13
Stav: platí
Požadavky: F1, F2, F7–F10, F11, S2
Podklad: `analysis/orm-frameworks-comparison.md`; audit 2026-08-02, nálezy 3.1 a 4.7

## Kontext

Výčet `PrimaryKeyStrategy` vznikl pro dva .NET frameworky a nese sedm hodnot: `None`, `Increment`, `Identity`, `Sequence`, `HiLo`, `Uuid` a `Guid`. Rozhodnutí [006](006-flat-composite-key-rendering.md) postavilo strategii do modelu **per část klíče**, takže se výčtu dotýká každá část kompozitního klíče zvlášť — jeho nedostatky se tím znásobily, ne zmenšily. Máme tři.

**Jedna hodnota nese tři různá tvrzení.** `None` znamená „zdroj neřekl nic" (části kompozitního klíče, Dapper), „hodnotu přiřazuje aplikace" (`assigned`) i „narazili jsme na generátor, kterému nerozumíme": `PrimaryKeyStrategyConvertor.FromNHibernate` odbaví neznámý řetězec stejnou větví jako `assigned`. Vlastní generátor zapsaný názvem typu tedy zmizí beze stopy a od legitimní hodnoty ho nelze odlišit. Je to přesně ta ztráta, kterou má podle rozhodnutí [004](004-unexpressible-facts-as-warnings.md) provázet varování — jenže bez rozlišení není co ohlásit.

**Výčet je seznam generátorů NHibernate, ne mechanismů.** `Uuid` a `Guid` jsou dvě hodnoty pro tentýž záměr, protože dva generátory má NHibernate. Naopak „mechanismus vyber podle dialektu" — `native` u NHibernate, `AUTO` v JPA — hodnotu nemá vůbec, ačkoli jde o samostatný záměr přítomný v obou ekosystémech. Nejostřeji se to projeví u EF Core: `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` navzdory svému názvu netvrdí „sloupec IDENTITY", ale „hodnotu vytvoří úložiště při vložení"; mechanismus volí provider. Dnešní překlad z toho udělá `identity`, tedy silnější tvrzení, než jaké zdroj učinil.

**Parametry generátoru model nenese.** Název sekvence, velikost bloku u hi/lo, tabulka čítače. Bez nich vygenerujeme `<generator class="sequence" />` mířící na výchozí sekvenci, která v cílové databázi nemusí existovat: výstup se přeloží a nepoběží. Táž třída chyby, kvůli které vzniklo rozhodnutí [006](006-flat-composite-key-rendering.md).

Audit navíc nechal otevřenou otázku (nález 3.1), zda má být `Order` na `PrimaryKeyPart` unikátní a souvislý od jedničky. Řazení podle `Order` vynucuje typ `PrimaryKey`, hodnoty ale nikdo nevaliduje.

Tvrzení o frameworcích platí pro NHibernate 5.7.0 a EF Core 10.0.10; u JPA se opíráme o Jakarta Persistence 3.1, kde přibyla hodnota `UUID`. Deklarace cílových verzí je samostatná otevřená položka a tohle rozhodnutí ji nepředjímá.

## Zvažované varianty

1. **Doplnit chybějící hodnoty a jinak výčet nechat být.** Nejlevnější zásah: přidat hodnotu pro `native`/`AUTO` a rozdělit `None`. Neřeší ale parametry ani generátor mimo výčet, takže obě zbylé vady zůstávají — a protože výčet je součástí veřejného modelu, otevírat ho podruhé je dražší než jednou pořádně.

2. **Sjednocení generátorů všech frameworků** — jedna hodnota za každý generátor NHibernate, každou hodnotu `GenerationType` a každou metodu EF Core. Výčet by nabobtnal ke dvaceti hodnotám, z nichž většina by měla protějšek v jediném frameworku, a stejně by nestačil: NHibernate připouští vlastní generátor zapsaný názvem typu, takže množina není konečná. Navíc by se do modelu vrátilo to, čemu se mezireprezentace vyhýbá — API konkrétních frameworků (JSS §5.4).

3. **Zrušit výčet a nést název generátoru ze zdroje jako řetězec**, překlad řešit tabulkou ve wrapperech. Model by byl otevřený, ale přestal by být slovníkem: každý builder by musel rozumět názvosloví každého zdroje, tedy přesně ty párové převody, které parser-builder architektura odstraňuje (JSS §4.3, požadavek S1). Nešlo by ani ověřit úplnost mapování, protože množina vstupních řetězců není známá.

4. **Slovník mechanismů s únikovou cestou pro to, co se do něj nevejde.** Výčet drží záměry společné víc frameworkům, framework-specifické podrobnosti se zaznamenají vedle něj. Výčet zůstává malý a uzavřený, a přitom se nic neztrácí potichu.

## Rozhodnutí

**Výčet pojmenovává mechanismus přidělení hodnoty, ne generátor konkrétního frameworku. Co se do slovníku nevejde, zaznamenáme vedle něj — ne místo něj.** Volíme variantu 4.

Jména se řídí jedním pravidlem: **neutrální název tam, kde tentýž záměr vyjadřuje víc ekosystémů; slovo zdroje tam, kde mechanismus umí jediný framework.** Bez takového pravidla se výčet skládá nahodile — a přesně tak vznikla dvojice `Uuid` a `Guid`.

| Hodnota | Význam | Co se na ni mapuje |
|---|---|---|
| `Unspecified` | nikdo neřekl, jak hodnota vzniká | zdroj o strategii mlčí; taky nerozpoznaný generátor, jehož název neseme vedle |
| `Assigned` | hodnotu dodá aplikace před vložením | NH `assigned`, EF Core `[DatabaseGenerated(None)]`, JPA `@Id` bez `@GeneratedValue` |
| `Auto` | mechanismus vybere framework podle dialektu | NH `native`, JPA `AUTO`, EF Core `[DatabaseGenerated(Identity)]` |
| `Identity` | auto-increment sloupec, hodnotu dá databáze při vložení | NH `identity`, JPA `IDENTITY`, EF Core `UseIdentityColumn()` |
| `Sequence` | hodnota ze sekvence | NH `sequence`, JPA `SEQUENCE`, EF Core `UseSequence()` |
| `HiLo` | přidělování po blocích z čítače mimo tabulku entity | NH `hilo` a `seqhilo`, EF Core `UseHiLo()`, JPA `TABLE` |
| `Uuid` | globálně unikátní hodnota generovaná mimo databázi | NH `guid`, `guid.comb`, `uuid.hex`, JPA `UUID`, konvence EF Core u klíče typu `Guid` |
| `Increment` | čítač v paměti procesu | NH `increment` |

K jednotlivým volbám patří krátké zdůvodnění, protože se o ně opře i text práce:

- **`Unspecified` místo `None`.** `None` čte každý jako „žádné generování neprobíhá", což je ale význam `Assigned`; ta záměna je dnešní vadou. `Unknown` zamítáme, protože naznačuje neúspěšný pokus o pochopení, kdežto většina případů je prosté mlčení zdroje. `Default` by tvrdilo, že platí výchozí chování frameworku — to je samostatný záměr s vlastní hodnotou `Auto`. `Unspecified` navíc drží nulu, takže nevyplněné pole neznamená tvrzení, které nikdo neudělal.
- **`Assigned`.** `Manual` říká, kdo hodnotu napsal, ne odkud pochází; `Application` pojmenovává aktéra, zatímco ostatní hodnoty pojmenovávají mechanismus. `Assigned` je slovo NHibernate i článku (JSS §5.2 uvádí „identity, sequence, or assigned").
- **`Auto`.** `Native` je slovo NHibernate, `StoreGenerated` by neodlišilo nic — identita i sekvence jsou také generované databází. Bez této hodnoty nelze poctivě přeložit EF Core, viz kontext.
- **`Identity`.** Jediná hodnota, na jejímž slově se shodnou všechny tři ekosystémy. `AutoIncrement` by pojmenovalo povrchový rys jednoho DBMS.
- **`HiLo`.** Rozdělení na `HiLo` a `Table` podle umístění čítače zamítáme: umístění je parametr, ne jiný mechanismus, a rozdělení by si vynutilo volbu u `seqhilo`, který je hi/lo nad sekvencí. JPA `TABLE` sem patří jako týž vzorec; při velikosti bloku 1 degeneruje na prostý čítač, což zaznamená parametr.
- **`Uuid`, ne `Guid`.** `Guid` je pravopis jednoho ekosystému, `Uuid` standardní termín a slovo JPA. Mezireprezentace má být ekosystémově neutrální — táž úvaha, ze které vzejde `CLRType` → `LangType`.
- **`Increment` ponechat.** Zdroj tu hodnotu tvrdit umí; bez ní by ji parser musel přemapovat a fakt by tiše zmizel, což je defekt, který opravujeme. Že hodnota funguje jen v jednom procesu, patří do komentáře a do diagnostiky, ne do názvu.

Hodnotu `Computed` pro `[DatabaseGenerated(Computed)]` do slovníku nedáváme: počítaný sloupec je fakt o sloupci, ne o vzniku identifikátoru, a patří na stranu mapování vlastnosti.

**Únikovou cestu tvoří dvě pole na `PrimaryKeyPart`.** `SourceStrategyName` nese, co zdroj napsal, když to slovník nezachytí — vlastní generátorovou třídu, `foreign`, `select`, ale i `guid.comb` vedle rozpoznaného `Uuid`. `StrategyParameters` nese parametry generátoru jako dvojice klíč–hodnota: název sekvence, velikost bloku, tabulku čítače. Obojí je záznam o zdroji vedle definice, ne její součást — táž role, jakou má `SourceKeyClass` u klíčové třídy (rozhodnutí [006](006-flat-composite-key-rendering.md)), a tvar klíč–hodnota odpovídá „flexible constraint set" z JSS §5.2. Samostatnou hodnotu `Unrecognized` proto nezavádíme: rozdíl mezi „zdroj mlčel" a „zdroj řekl něco, čemu nerozumíme" už nese přítomnost názvu.

**Název výčtu `PrimaryKeyStrategy` zůstává.** Přesnější `KeyGenerationStrategy` zamítáme kvůli papírové stopě: odkazují na něj rozhodnutí 006 a 009, audit i kategorie `MappingFactCategory.PrimaryKeyStrategy`, a rozhodnutí se nepřepisují. Že jde o strategii části, říká už umístění — `PrimaryKeyPart.Strategy`.

**`Order` musí být v rámci klíče unikátní, souvislý od jedničky být nemusí.** Duplicitní hodnota dělá výsledné pořadí závislým na pořadí vstupu, protože `OrderBy` je stabilní — a pořadí sloupců klíče určené vstupem místo modelem je porušení S2. Souvislost naopak nevyžadujeme: nese se jen relativní pořadí a zdroje číslují různě, EF Core `[Column(Order)]` od nuly, NHibernate pozicí prvku. Validaci umístíme do typu `PrimaryKey`, kde už bydlí řazení, aby platila na každé konstrukční cestě.

**Konvenci zdrojového frameworku čte parser.** Podle rozhodnutí [008](008-database-as-metadata-source.md) se konvence čte tam, kde by její neznalost změnila význam, což je tento případ: strategie odvozená z typu klíče je tvrzení o tom, jak hodnota vzniká, a jeho ztráta mění chování cílového kódu. Konkrétně u EF Core se čte `[DatabaseGenerated]` a u klíče bez něj se strategie odvodí z typu — celočíselný klíč `Auto`, klíč typu `Guid` `Uuid`, řetězcový klíč `Assigned`. Části kompozitního klíče dostávají `Assigned` u obou frameworků: `<composite-id>` v NHibernate generátor nepřipouští a EF Core u kompozitního klíče hodnoty negeneruje, takže v obou případech je hodnota věcí aplikace — to je tvrzení frameworku, ne mlčení.

## Důsledky

Přepis je jednorázový a bez přechodného období (rozhodnutí [003](003-one-shot-migration.md)). Dotkne se výčtu, obou směrů konvertoru u NHibernate, obou parserů, obou builderů, `SampleData` i testů.

`PrimaryKeyStrategyConvertor.ToNHibernate` musí přestat končit větví `NotImplementedException`. Mapování bude úplné a hodnota, kterou cíl vyjádřit neumí, se stane záznamem diagnostiky podle rozhodnutí [010](010-diagnostics-as-returned-data.md), ne pádem uprostřed generování. Pro `Unspecified` vypíšeme u NHibernate `assigned` jako konvenci cíle a zaznamenáme, že hodnota nebyla tvrzená — konvence třetího stupně nesoucí svůj původ podle rozhodnutí 008.

Diagnostika dostane u této kategorie tři konkrétní případy. NHibernate neumí generátor u kompozitního klíče vůbec, takže per-part strategie je v tomto cíli nevyjádřitelný fakt — model ji per-part drží záměrně a rozhodnutí 006 se tím nemění. EF Core anotacemi vyjádří jen `Auto` a `Assigned`, zbytek je dostupný pouze fluent API. A `Increment` nemá protějšek nikde mimo NHibernate. Kategorie `MappingFactCategory.PrimaryKeyStrategy` v deskriptoru už existuje, takže deskriptor se strukturálně nemění.

Parametry generátoru tím dostávají místo v modelu, ne zdroj. Kde je zdroj neuvádí, zůstanou prázdné; doplnění z databázového katalogu (rozhodnutí 008) je samostatná práce a týká se hlavně identity sloupce a výchozí hodnoty odkazující na sekvenci.

Slovník je zároveň prvním případem obecnějšího vzorce, na který narazíme znovu u typového modelu: **mezireprezentace nese průnik záměrů, framework-specifické podrobnosti nese vedle něj jako záznam o zdroji.** Kdybychom místo toho volili sjednocení, model by rostl s každým dalším frameworkem — což je přesně to, čemu se parser-builder architektura vyhýbá.