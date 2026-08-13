# 012 — Vykreslení cizího klíče v cílových frameworcích

Datum: 2026-08-13
Stav: platí
Požadavky: F2, F3, F11, S2
Podklad: NHibernate 5.7.0 — `src/NHibernate/nhibernate-mapping.xsd`, `src/NHibernate/Id/IdentifierGeneratorFactory.cs`, `src/NHibernate/Cfg/XmlHbmBinding/CollectionBinder.cs` a `ValuePropertyBinder.cs`; referenční dokumentace NHibernate, kapitola o vztazích

## Kontext

Cizí klíč z převodu prakticky vypadává. `EFCoreEntityBuilder.BuildForeignKey` vypíše jen navigační vlastnost a atribut `[ForeignKey]` negeneruje vůbec, takže si cílový EF Core klíč odvodí vlastní konvencí bez ohledu na to, co říkal zdroj. `NHibernateEntityBuilder` vypíše `column` odvozený z navigační vlastnosti — a protože ta zpravidla sloupec namapovaný nemá, vyjde `column="Customer"`, tedy jméno sloupce podle navigace. U kolekcí jde do `<key>` výsledek `GetPrimaryKeyColumn`, tedy první část klíče **rodiče** použitá jako jméno sloupce v **dítěti**. Žádná z těch vad nesouvisí s počtem sloupců: špatné je to už u jednosloupcového vztahu.

`Relation.ColumnPairs` existuje právě pro tohle, ale nikdo je neplní ani nečte. Aby měly konzumenta, musí být napřed jasné, jaký tvar výstupu z nich vzniká.

Tvar u obou cílů jsme ověřili proti primárním zdrojům zafixovaných verzí:

- `<one-to-one>` **nemá atribut `column`** a připouští jen potomky `meta` a `formula`. Nese `property-ref` a `constrained`. Dnešní výstup `<one-to-one … column="…" />` je tedy proti schématu neplatný.
- `<many-to-one>` atribut `column` má, k tomu libovolný počet vnořených `<column>` a atributy `unique` a `not-null`.
- `<key>` je na tom stejně: buď atribut `column`, nebo vnořené `<column>`.
- `IdentifierGeneratorFactory` registruje generátor `foreign`, jímž NHibernate vyjadřuje závislou entitu sdílející primární klíč; `ForeignGenerator` k němu čte parametr `property` s názvem vlastnosti, přes kterou se identita přebírá.
- Referenční dokumentace ukazuje obousměrný 1:1 přes cizí klíč jako `<many-to-one unique="true">` na vlastnící straně a `<one-to-one property-ref="…">` na straně inverzní. Tohle je jediné tvrzení, které neplyne ze schématu — schéma popisuje tvar, ne roli.
- EF Core vyjádří vícesloupcový cizí klíč anotací `[ForeignKey("A,B")]` na navigační vlastnosti, kde `A` a `B` jsou **vlastnosti třídy**, ne sloupce, a záleží na jejich pořadí. Zdroj z NHibernate takové vlastnosti nemá — `<many-to-one>` si vystačí se sloupci.

Název elementu v NHibernate popisuje tvar sloupců, ne násobnost: `<one-to-one>` je strana, která žádný cizí klíč nedrží, ať už proto, že ho drží protistrana, nebo proto, že obě entity sdílejí primární klíč. Tvar výstupu tedy nelze určit z kardinality samotné.

## Zvažované varianty

1. **Odvozovat tvar z kardinality**, tedy dnešní stav rozšířený o sloupce. Nejlevnější, ale nerozliší vlastnící stranu od inverzní ani sdílený klíč od vlastního, takže u poloviny vztahů 1:1 vyrobí mapování, které schéma odmítne. Zamítáme.

2. **Rozšířit mezireprezentaci** o explicitní příznak „sdílený primární klíč" a o seznam vlastností tvořících cizí klíč. Nejpřesnější popis, ale duplikuje to, co model už nese — roli vztahu a strategii generování klíče — a vynucuje si zásah do všech parserů kvůli jedinému cíli. Přidávat do mezireprezentace fakt, který z ní jde odvodit, jde proti tomu, jak ji držíme štíhlou (JSS §5.1).

3. **Odvodit tvar z kardinality, role a strategie klíče a chybějící artefakty cíle dogenerovat v builderu.** Model se nemění, protože všechny tři údaje v něm už jsou. Cenou je konvence pojmenování dogenerovaných vlastností, kterou je nutné zapsat, protože se propíše do každého překladu z NHibernate do EF Core.

4. **Vypsat jen to, co jde bez dohadů**, tedy u 1:1 vždy bezsloupcový `<one-to-one>` a u EF Core nic, a zbytek nahlásit. Zachová čistotu, ale zahodí fakt, který oba cíle vyjádřit umějí — to je proti F2 i proti smyslu rozhodnutí [004](004-unexpressible-facts-as-warnings.md), které hlášení vyhrazuje faktům skutečně nevyjádřitelným.

## Rozhodnutí

Volíme variantu 3. **Tvar výstupu určuje kardinalita spolu s rolí vztahu, a u vztahu 1:1 navíc strategie generování klíče závislé entity.** Pro NHibernate:

| Vztah | Strana | Výstup |
|---|---|---|
| N:1 | `Owning` | `<many-to-one>` se sloupci cizího klíče |
| 1:1 s vlastním cizím klíčem | `Owning` | `<many-to-one unique="true">` se sloupci |
| 1:1 s vlastním cizím klíčem | `Inverse` | `<one-to-one>` bez sloupců, s `property-ref` je-li známý |
| 1:1 přes sdílený primární klíč | `Owning` (závislá entita) | `<one-to-one constrained="true">` bez sloupců |
| 1:1 přes sdílený primární klíč | `Inverse` (nadřazená entita) | `<one-to-one>` bez sloupců |
| 1:N a N:M | `Inverse` | `<bag>` s `<key>`, sloupce z `ColumnPairs` |

Role je v modelu podle rozhodnutí [001](001-entity-reference-by-name.md) definovaná jako strana držící fyzický cizí klíč, takže kolekce jsou vždy `Inverse` — `AddForeignKey` to tak i přiděluje a builder `<bag>` vypisuje s `inverse="true"`.

**Sdílený primární klíč se pozná lokálně a jen tam, kde na tom záleží.** Závislá entita ho vyjadřuje generátorem `foreign`, což je po rozhodnutí [011](011-key-generation-strategy-vocabulary.md) hodnota `Unspecified` se `SourceStrategyName = "foreign"` a s parametrem `property` v `StrategyParameters`. To je tvrzení zdroje, ne dohad z prázdných sloupců, a leží ve stejné entitě, která se právě generuje. Na straně nadřazené entity rozdíl mizí: obě varianty tam vypisují `<one-to-one>` bez sloupců a liší se jen přítomností `property-ref`. Rozlišovat tedy stačí na straně závislé, kde je signál po ruce.

**Jeden sloupec se píše atributem `column`, víc sloupců vnořenými `<column name="…" />` v uloženém pořadí. Obojí zároveň nikdy** — schéma sice připouští atribut i potomky vedle sebe, ale sémantiku takové kombinace nemáme z čeho doložit.

**`ColumnPairs` jsou uspořádané a autoritativní.** Pořadí odpovídá pořadí částí klíče, na který se odkazuje, a ukládá ho ten, kdo páry plní — parser nebo databázový katalog. Builder je nepřeskládává, ačkoli by to technicky svedl: pár nese i cílovou vlastnost, takže by šlo řadit podle klíče cílové entity. Nedělá to proto, že by tím zakryl vadu producenta. Rozpor mezi pořadím párů a pořadím klíče cíle je chyba úplnosti mezireprezentace, tedy případ pro kontrolu před generováním a strukturovanou diagnostiku podle rozhodnutí [010](010-diagnostics-as-returned-data.md), ne něco, co se má potichu spravit při emisi.

**Sloupce `<key>` uvnitř `<bag>` bere builder z `ColumnPairs` téhož, tedy inverzního vztahu.** Naplnit je ale nelze při čtení samotného rodiče: `ColumnPair` drží na obou stranách `PropertyMap` a sloupec z `<key column="…">` patří dětské tabulce, jejíž vlastnosti parser rodiče nevidí. Páry proto vznikají teprve tam, kde jsou obě entity součástí téhož převodu — samostatná otevřená položka. Do té doby vypisujeme sloupec klíče vlastníka, tak jako dosud; vynechat atribut zde není mlčení, protože `CollectionBinder` doplní sloupec pojmenovaný `id` (`Collection.DefaultKeyColumnName`), a mapování by tak tvrdilo něco jiného, ne méně.

**Pro EF Core vypisujeme `[ForeignKey]` na navigační vlastnosti se jmény vlastností cizího klíče.** Nejsou-li takové vlastnosti v mezireprezentaci, **dogeneruje je builder** — stejnou úvahou jako u členů, které si vynucuje kompozitní klíč (rozhodnutí [006](006-flat-composite-key-rendering.md)): co si cíl vynucuje, vyrábí builder, ne mezireprezentace. Jméno vzniká složením `{Navigace}{NázevČástiKlíčeCíle}`, tedy například `OrderOrderID`; existuje-li vlastnost toho jména už teď, použije se. Jméno sloupce k tomu nepoužíváme, protože nemusí být platný C# identifikátor a nemusí být znám. Dogenerovaná vlastnost nese `[Column("…")]` se jménem sloupce ze zdroje a je nullable tehdy, je-li nullable navigace.

**Neznámé sloupce nevydáváme za známé — pokud mlčení znamená totéž.** Jsou-li `ColumnPairs` prázdné, u EF Core `[ForeignKey]` nevypíšeme a odvození necháme konvenci frameworku. U `<many-to-one>` vypustíme atribut `column`, protože NHibernate doplní sloupec podle názvu vlastnosti (`NamingStrategy.PropertyToColumnName`) — tedy přesně to, co jsme dosud psali ručně. U `<key>` uvnitř kolekce to ale neplatí: výchozí jméno je tam pevné `id` (`Collection.DefaultKeyColumnName`), takže vynechání význam mění a sloupec klíče vlastníka vypisujeme dál. Rozdíl mezi oběma případy je vlastností cíle, ne naší volbou, a plyne z něj obecné pravidlo: mlčet smíme jen tam, kde cíl doplní totéž, co bychom napsali sami. Kde ne, jde o konvenci cíle třetího stupně podle rozhodnutí [008](008-database-as-metadata-source.md) a hlásí se jako taková.

**Čtení musí být symetrické.** Parser NHibernate čte `unique` na `<many-to-one>` jako vztah 1:1, `property-ref` na `<one-to-one>` jako inverzní stranu a `constrained` jako stranu závislou. Bez toho by se obousměrný 1:1 v převodu tam a zpět degradoval na N:1, protože kardinalita i role se dnes odvozují jen z názvu elementu. Hodnotu `property-ref` model neuchovává — ukazuje na navigační vlastnost cizí entity a pro takový odkaz nemá místo. Vypsat ji zpátky proto umíme jen tam, kde je protistrana součástí téhož převodu, což předpokládá rozresolvování jmen entit slíbené v důsledcích rozhodnutí [001](001-entity-reference-by-name.md).

## Důsledky

Mění se oba buildery a parser NHibernate; mezireprezentace zůstává, jak je. V NHibernate builderu končí `GetPrimaryKeyColumn` jako zdroj sloupce pro `<key>` a přibývá volba značky podle role. V EF Core builderu přibývá `[ForeignKey]` a generování skalárních vlastností klíče. Součástí je oprava neplatného `<one-to-one column="…">`, protože právě zde se tvar značky rozhoduje.

Dogenerované vlastnosti mají důsledek, který je potřeba přiznat: jakmile vzniknou ve výstupním artefaktu, jsou při dalším čtení běžnými vlastnostmi entity. Převod NHibernate → EF Core → mezireprezentace tedy vydá bohatší model než původní. Není to ztráta ani nekonzistence, je to vlastnost cílového frameworku, která se stala součástí artefaktu — ale znamená to, že rovnost modelů nelze používat jako kritérium korektnosti obousměrného převodu mezi frameworky s různou explicitností.

Diagnostika dostane čtyři nové kategorie: neznámé sloupce cizího klíče, pořadí párů neodpovídající klíči cílové entity, N:M bez spojovací entity a nezachovaná hodnota `property-ref` na inverzní straně.

Zůstává jedna nedořešená vazba. Dokud platí, že builder nepoužívá `SourceStrategyName`, vypíše NHibernate builder generátor `foreign` jako `assigned` — a s ním zmizí jediný signál, podle kterého se sdílený primární klíč pozná. Převod NHibernate → NHibernate tedy vztah 1:1 přes sdílený klíč udrží v mezireprezentaci, ale ne v druhém průchodu přes vygenerované mapování. Rozhodnout to patří otevřené položce „Smí builder použít název strategie ze zdroje?"; tohle rozhodnutí na jejím výsledku nestojí, jen se s ním zpřesní.

Mimo rozsah zůstává `property-ref` mířící na jiný než klíčový sloupec — legacy konstrukce, kterou dokumentace sama nedoporučuje — a párování navigací na inverzní straně u EF Core (`[InverseProperty]`).

Rozhodnutí je zároveň předpokladem spojovací entity pro N:M: ta stojí na tom, že vícesloupcový cizí klíč umíme vypsat v obou cílech.