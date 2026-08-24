# 048 — Mapovací fakt, pro který model nemá místo, je ztráta, ne slovník

Datum: 2026-08-24
Stav: platí
Požadavky: F5, F11, S1
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md) a [019](019-neutral-database-type-vocabulary.md); nález z 2026-08-23, oddíl 3.3

## Kontext

`PropertyMap.OtherDatabaseProperties` je `Dictionary<string, string>` popsaný v `architecture.md` §4.1 jako *„volný slovník pro ostatní metadata ve tvaru klíč–hodnota"*. V celém řešení má vedle deklarace **dva výskyty a oba jsou zápis** — větev `default:` v `SetPropertyDatabaseMapping`. Žádný builder, visitor, čtečka katalogu ani test ten slovník nečte.

Horší než nevyužitost je to, co z ní plyne pro diagnostiku. Fakt, který do slovníku spadne, **přežije do modelu a zemře při emisi bez jediného záznamu**: nemá `MappingFactCategory`, takže ho mechanické `ReportLosses` (průnik faktů modelu s kategoriemi *neumím vyjádřit* deskriptoru) nevidí, a žádný builder ho nehlásí, protože o něm neví. To je přesně to ticho, kterému rozhodnutí [004](004-unexpressible-facts-as-warnings.md) a [010](010-diagnostics-as-returned-data.md) předcházejí — a je o to zákeřnější, že dokumentace slovník popisuje slovesem „nese", jako by se ta metadata někam nesla.

**Dnes tam nespadne nic.** Prošli jsme všechny producenty: `EFCoreEntityParser` posílá `Length`, `IsVersion`, `Nullable`, `ColumnName`, `Precision` a `Scale`, `NHibernateXMLMappingParser` posílá `column`, `nullable`, `precision`, `scale`, `length` a `IsVersion`. Každý z těch klíčů má v `switch` vlastní větev. Slovník je tedy prázdný v každém dnešním běhu — což znamená, že rozhodujeme dřív, než se vada projeví, ne poté.

**Model má únikové cesty a tahle mezi ně nepatří.** `SourceSqlType` u typu a `SourceStrategyName` se `SourceStrategyParameters` u strategie klíče jsou také „co zdroj řekl doslova", ale obojí má **čtenáře**: NHibernate builder je vypisuje zpátky (`sql-type`, název generátoru, jeho parametry) a rozhodnutí [019](019-neutral-database-type-vocabulary.md) a [021](021-generator-name-selection.md) přesně popisují, kdy. Úniková cesta bez čtenáře nenese nic — je to místo, kde fakt zmizí tiše místo hlasitě.

## Zvažované varianty

1. **Nechat slovník a dát mu čtenáře.** Znamenalo by to rozhodnout, co si z libovolného klíč–hodnota páru má počít NHibernate builder, EF Core builder a příští javový builder — tedy překládat mezi frameworky slovník, jehož obsah není nikde definovaný. To je opak toho, čemu se rozhodnutí [020](020-canonical-generator-parameter-vocabulary.md) věnovalo u parametrů generátoru: nekanonizovaný pár se přeložit nedá, protože nikdo neví, co znamená. Zamítáme.

2. **Nechat slovník a při zápisu do něj vydat záznam `Loss`.** Ticho by zmizelo, ale zůstal by člen modelu, který nic nenese a jehož existence tvrdí opak. Model je podle §4.1 slovník pro cílový stav — místo pro to, co ještě nikdo nevyrábí, ale co je definované. Bezejmenný pár definovaný není. Zamítáme.

3. **Slovník zrušit a nezachycený fakt hlásit záznamem `Loss`.**

## Rozhodnutí

**Volíme variantu 3.** `PropertyMap.OtherDatabaseProperties` mizí a větev `default:` v `SetPropertyDatabaseMapping` místo zápisu vydá záznam `Loss` s uvedením klíče i hodnoty, jak je zdroj napsal.

Tím se **mapovací fakt bez místa v modelu zařadí tam, kam patří podle rozhodnutí 004**: mezi to, co zdroj nesl a cíl nevyjádří, tedy vedle nenaparsovatelné facety (`length="MAX"`) a kolekčního elementu bez `<one-to-many>`. Artefakt vzniká a je platný, jen chudší než vstup — a volající se o tom dozví.

**Záznam nemá `Category`.** Kategorie mapovacího faktu je uzavřený výčet a nezachycený klíč do žádné z nich z definice nespadá; dosadit nejbližší by znamenalo tvrdit o něm víc, než víme. `Property` a `Reason` s doslovným klíčem stačí k tomu, aby uživatel poznal, co se ztratilo — což je právě to, co F11 žádá po strukturované diagnostice.

**Rozdíl proti `ReportUnreadableFact` vyslovujeme.** Ten hlásí fakt, jehož *místo* model má, ale jehož *hodnotu* nedokáže přečíst (`nullable="ano"`). Tenhle hlásí fakt, pro který model nemá místo vůbec. Pro volajícího jsou to dvě různé zprávy: v prvním případě pomůže opravit vstup, ve druhém ne.

**Kanál slovníku zůstává, jak je.** `SetPropertyDatabaseMapping` bere dál `Dictionary<string, string>` — rozhodnutí 019 z něj vyňalo jen typovaná fakta a zbytek (sloupec, číselné a pravdivostní hodnoty) je v téhle podobě v pořádku. Ruší se jen odkladiště pro to, co `switch` nezná.

## Důsledky

**Dnešní výstupy se nemění vůbec.** Žádný parser dnes klíč mimo `switch` neposílá, takže nová větev je zatím pojistka. Přesně tak ji chceme: je zapsaná dřív, než ji první javový parser (F7–F10) nebo čtení fluent konfigurace potká.

**Model se zmenšuje o člen, který nikdo nečetl** — a `architecture.md` §4.1 přestává tvrdit, že se ta metadata někam nesou.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MAJOR uvnitř řešení** (mizí veřejný člen `PropertyMap`) **a PATCH navenek**: `PropertyMap` se do odpovědi API neserializuje, mezireprezentace přes REST nechodí.
