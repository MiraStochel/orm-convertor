# 097 — Vlastní typ výjimky bydlí tam, kde se vyhazuje; `Model` žádný nenese

Datum: 2026-09-21
Stav: platí
Požadavky: S1
Podklad: revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nález 5.1; rozhodnutí [010](010-diagnostics-as-returned-data.md) a [053](053-a-query-that-would-return-other-rows-is-not-emitted.md)

## Kontext

Rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md) zrušilo poslední cestu, která `QueryBuilderException` vyhazovala, a samo o typu napsalo:

> **Typ samotný zůstává v `Model`**, protože otázka „co je vlastní typ výjimky řešení zač" je širší než tohle rozhodnutí: týká se i entitní strany, kde `SetKeyStrategyDetails` hází `InvalidOperationException` a drží to test. Zodpovědět ji tady mimochodem by znamenalo rozhodnout o obou stranách bez rozvahy.

Bylo to správné odložení a od té doby se nic nestalo — až na to, že se odložená otázka **zodpověděla praxí**. Revize z 2026-09-21 na to narazila z druhé strany: `QueryBuilderException` nemá v celém řešení, v produkčním kódu ani v testech, jediný další výskyt. Adresář `Model/Exceptions/` neobsahuje nic jiného.

Mezitím řešení vlastní typy výjimek dostalo, a všechny čtyři vznikly jinde a jinak:

| Typ | Kde bydlí | Viditelnost |
|---|---|---|
| `JavaSyntaxError` | `JavaEntityParsing/JavaLexer.cs` | veřejný, vedle lexeru, který ho hází |
| `JavaInputTooDeep` | `JavaEntityParsing/JavaLexer.cs` | veřejný, tamtéž (rozhodnutí [092](092-input-nesting-depth-capped-before-the-descent.md)) |
| `JpqlParseError` | `JakartaPersistence/JpqlQueryParser.cs` | `protected sealed`, **uvnitř** parseru |
| `HqlParseError` | `NHibernateWrappers/NHibernateHqlQueryParser.cs` | `private sealed`, **uvnitř** parseru |

Všechny čtyři mají vyhazovače a čtenáře, a všechny čtyři sedí u něj. Obecná selhání, se kterými návrh nepočítá, přitom používají typy z BCL — dvaadvacetkrát `InvalidOperationException`, dvacetkrát `ArgumentException`, čtrnáctkrát `ArgumentOutOfRangeException` —, takže vlastní typ je v tomhle řešení vždy **signál pro konkrétního čtenáře**, ne rodová kategorie chyb.

Otázka, kterou 053 odložilo, tedy už odpověď má; chybí jen zapsaná, a spolu s ní jediná deklarace, která se jí vymyká.

## Zvažované varianty

### 1 — Nechat `QueryBuilderException` tam, kde je

Nulová práce a nulové riziko. Zamítáme, protože cena není nulová: je to veřejný typ v `Model`, tedy v projektu, který referencuje skoro všechno, a **navádí proti platnému rozhodnutí** — výjimka z builderu je přesně ten kanál, který rozhodnutí [010](010-diagnostics-as-returned-data.md) nahradilo vrácenými záznamy. Kdo ho v `Model` najde, přečte si ho jako pozvánku. Dvě revize po sobě ho navíc našly jako deklaraci bez čtenáře, což je samo o sobě signál.

### 2 — Zavést rodovou základní výjimku řešení (`OrmConvertorException`) a odvozovat od ní

Tvar, ke kterému `QueryBuilderException` v `Model` ukazuje. Zamítáme: rodová základna má smysl tam, kde ji volající **chytá** jako kategorii — „cokoli z téhle knihovny" —, a tady takový volající není. Překladová cesta výjimky nepoužívá k ničemu, co by volající odlišoval: buď je to stav, se kterým návrh počítá, a pak je to záznam (rozhodnutí 010), nebo je to stav, se kterým nepočítá, a pak má skončit tak, jak končí každá jiná chyba programu. Společný předek by tedy nesl jedinou informaci — „je to naše" — a za ni bychom platili tím, že by každé nové místo muselo volit mezi ním a BCL.

### 3 — Vlastní typ bydlí u vyhazovače a `Model` nenese žádný

## Rozhodnutí

**Volíme variantu 3. Vlastní typ výjimky vzniká jedině tam, kde ho někdo hází, bydlí v témže projektu jako vyhazovač a je tak úzký, jak to jde — nejlépe `private` nebo `protected` uvnitř třídy, která ho hází. Rodová základna řešení nevzniká a `Model` nenese žádný typ výjimky. `QueryBuilderException` i s adresářem `Model/Exceptions/` mizí.**

**Kritérium je čtenář, ne původ.** Vlastní typ je namístě, když ho někdo **chytá** a podle typu se rozhoduje jinak — tak to dělá všechna čtyři dnešní: `JavaSyntaxError` a `JavaInputTooDeep` chytá entitní parser a každou převádí na jiný záznam (rozhodnutí [093](093-unreadable-input-is-a-unit-failure.md) a [092](092-input-nesting-depth-capped-before-the-descent.md)), `JpqlParseError` a `HqlParseError` chytá jejich vlastní parser a dělá z nich `Failure` s řádkem a sloupcem. Typ bez chytače je typ bez důvodu.

**Kde návrh se stavem počítá, výjimka nevzniká vůbec.** To říká rozhodnutí 010 a tohle rozhodnutí ho jen nepřepisuje: nesprávný podmínkový strom, nečitelná jednotka, fakt, který cíl nevysloví — to všechno je záznam. Výjimka zůstává pro stavy, které návrh **vylučuje**, a na ty stačí BCL, protože je nikdo nechytá.

**`Model` nenese žádný, a je to tvrzení o `Model`, ne o výjimkách.** `Model` je mezireprezentace: datové typy, továrny a jejich invarianty. Výjimka, kterou hází `AbstractEntityBuilder` nebo wrapper, je fakt překladové cesty, ne modelu, a umístit ji do `Model` znamená dát ji na dohled všem šesti wrapperům, které ji nepotřebují. Invariant S1 mluví o tom, že nový framework je nový projekt; jeho protějškem směrem dolů je, že sdílený projekt nenese nic, co sdílené není.

**Co tím rozhodnuté není:** které konkrétní BCL typy se kde hází. `SetKeyStrategyDetails` s `InvalidOperationException`, kterou 053 jmenovalo, zůstává, jak je — je to stav, který návrh vylučuje, a test ho drží.

## Důsledky

**Smaže se jeden soubor a jeden adresář.** `Model/Exceptions/QueryBuilderException.cs` a s ním `Model/Exceptions/`, který jinak prázdný zbude. Nic se nepřekládá jinak: typ nemá výrobce ani čtenáře, takže po odstranění se sestavení ani sada nezmění.

**Rozhodnutí 053 se nepřepisuje.** Jeho věta o tom, že typ v `Model` zůstává, platila v den svého vzniku a snímky se zpětně neopravují; tohle rozhodnutí na ni navazuje a otázku, kterou vědomě odložila, zodpovídá. Stav 053 se nemění — není to změna volby, kterou 053 udělalo, nýbrž odpověď na otázku, kterou výslovně nechalo otevřenou.

**Sedmý framework má pravidlo dopředu.** Až bude jeho parser potřebovat výjimku, odpověď existuje bez rozvahy: vlastní typ uvnitř parseru, chycený tímtéž parserem nebo jeho entitním obalem, převedený na záznam. Přesně to udělaly JPQL a HQL parser každý zvlášť a shodně, jen to nikde nestálo.
