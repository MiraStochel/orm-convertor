# 053 — Dotaz, který by vrátil jinou množinu řádků, se nevydá

Datum: 2026-08-24
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [022](022-native-query-syntax-in-builders.md) a [023](023-query-builder-template-method.md); nález z 2026-08-23, oddíl 3.8

## Kontext

Na tentýž vadný vstup — porovnávací operátor, který potřebuje pravý operand a nemá ho — odpovídají tři dotazové visitory třemi způsoby:

| Visitor | Odpověď |
|---|---|
| `DapperSqlQueryVisitor` | `throw new QueryBuilderException` |
| `NHibernateHqlQueryVisitor` | záznam `Loss` a `"1 = 1"` |
| `EFCoreLinqQueryVisitor` | záznam `Loss` a `"true"` |

Totéž u operátoru mimo mapovanou množinu: Dapper hází, NHibernate vrací `"in"` (větev `_`), EF Core `"<="` (větev `_`) — obojí **mlčky**.

Nekonzistence je ta menší polovina. **Dosazená tautologie není ztráta, je to změna významu.** `where 1 = 1` a `.Where(c => true)` vrátí *všechny* řádky tam, kde zdroj filtroval, a uvnitř disjunkce (`A OR B`, kde `B` je tautologie) zneplatní celou podmínku, tedy i tu část, která se přeložila správně. Táž vada je u `IN` v EF Core, kde se dnes vydá `Loss` a `"true"`.

To je přímo proti rozhodnutí [004](004-unexpressible-facts-as-warnings.md), které zakazuje generovat náhražky za to, co cíl nevyjádří. Náhražka za filtr je tady navíc nejhoršího druhu: **vypadá jako přeložený dotaz a vrací jinou množinu řádků.** Pro T3 (funkční ekvivalence) je to tichý falešný pozitiv — dotaz se přeloží, zkompiluje, spustí a odpoví špatně.

Vedle toho stojí výjimka u Dapperu, která je v rozporu s `architecture.md` §5.1: *„Výjimky zůstávají vyhrazené chybám programu; stav, se kterým návrh počítá, končí záznamem."* `ConversionHandler.Convert` volá `qb.Build()` bez `try`, takže jeden vadný dotaz shodí celý převod včetně entit, které se přeložily bez potíží.

## Zvažované varianty

1. **Sjednotit na dnešní většinu: záznam `Loss` a tautologie.** Odstranilo by to nekonzistenci a nechalo vadu. Dva ze tří cílů by dál vydávaly dotaz vracející jiné řádky, jen by to dělaly shodně. Zamítáme.

2. **Sjednotit na výjimku.** Konzistentní a hlasité, ale proti rozhodnutí [010](010-diagnostics-as-returned-data.md) a proti dávkovému vstupu podle F14: jeden vadný dotaz by zahodil i to, co se přeložilo. Zamítáme.

3. **Odmítnout dotaz záznamem `Failure` a nevydat artefakt.**

## Rozhodnutí

**Volíme variantu 3 a vyslovujeme pravidlo, které entitní větev už má: `Failure` znamená, že artefakt nevznikne.** Na entitní straně to platí od rozhodnutí 010 — brána úplnosti odmítne entitu se záznamem `Failure` dřív, než se napíše půlka artefaktu. Dotazová větev tutéž větu neměla; nově ji má a znamená totéž. **Není to nový slovník, je to dokončení stávajícího.**

Z toho plynou tři konkrétní věci:

**Podmínkový strom se kontroluje v `Normalize()`, tedy v šabloně, ne ve visitorech.** Prochází se filtr, filtr po agregaci i ON klauzule joinů a hlídají se dvě věci: porovnání, jehož operátor pravý operand vyžaduje a nemá ho (`IsNull`/`IsNotNull` ho podle rozhodnutí [002](002-is-null-as-comparison-operator.md) nevyžadují), a logický uzel bez operandů. Nález končí záznamem `Failure` a `Normalize` vrací `null`, takže se nespustí ani jeden ze sedmi kroků. **Všechny tři cíle tím odpovídají stejně, protože odpovídá jedno místo** — táž úvaha, jakou vede rozhodnutí [023](023-query-builder-template-method.md) u pořadí kroků a rozhodnutí [009](009-target-framework-descriptor.md) u mechanického hlášení ztrát.

**Operátor, který cíl neumí vypsat, je `Failure` v místě emise.** Sem patří `In` v EF Core, kde LINQ výraz nad hodnotami, které model nenese, neexistuje. Větve `_ =>` v převodních tabulkách operátorů mizí ve prospěch úplného `switch`u, takže nová hodnota `ComparisonOperator` se v žádném visitoru nepřeloží tiše na sousední operátor — což je táž pojistka, jakou má `Carries` na entitní straně.

**`Failure` kdekoli v dotazové větvi ruší artefakt.** `AbstractQueryBuilder` si zaznamenaná selhání pamatuje a `Build()` vrátí prázdný seznam, i když se text mezitím složil. Kanál je jediný a už dnes ho používají parsery i šablona, takže se tím **zpětně uzavírají i dosavadní `Failure` záznamy**, po kterých se stejně nic nevydávalo — jen to nebylo pravidlo, ale shoda okolností. Visitor tedy hlásí `Failure` a vrátí libovolný text; ten se nikam nedostane.

**Tautologie se nedosazují nikde.** Ani jako mezikrok: dosazená `true` se do výstupu nedostane, protože výstup nevznikne, a to je bezpečnější než spoléhat na to, že si ji někdo za ni vezme zpět.

**Výjimka `QueryBuilderException` z dotazové větve mizí úplně.** Nesprávný podmínkový strom je stav, se kterým návrh počítá — může ho vyrobit parser cizího jazyka —, takže do něj podle rozhodnutí 010 výjimka nepatří; po téhle změně ji nehází žádná cesta. **Typ samotný zůstává v `Model`**, protože otázka „co je vlastní typ výjimky řešení zač" je širší než tohle rozhodnutí: týká se i entitní strany, kde `SetKeyStrategyDetails` hází `InvalidOperationException` a drží to test. Zodpovědět ji tady mimochodem by znamenalo rozhodnout o obou stranách bez rozvahy.

**Co tím pokryté není: náhrady vyslovené jinde.** Rozhodnutí mluví o podmínkovém stromu a o operátoru, tedy o místech, kde se dosud dosazovala tautologie nebo se házelo. Vedle nich stojí náhrady, které množinu řádků mění také, ale jsou popsané a zvolené jinde: **plný vnější join** vypsaný jako vnitřní u EF Core i NHibernate (zúžení uvnitř kategorie `JoinKind` hlášené v místě emise, [`architecture.md`](../architecture.md) §5.1) a **zahozený poddotaz** hlášený `Normalize()` (otevřená položka). Argument „dosazená náhražka není ztráta, je to jiný dotaz" na ně sedí doslova, takže je namístě otázka, jestli se tohle pravidlo má vztáhnout i na ně; odpověď ale není mechanická — u joinu by znamenala nevydat artefakt tam, kde ho dnes uživatel dostane se srozumitelným záznamem, a u poddotazu se překrývá s prací, která už má vlastní položku. Otevíráme ji proto jako samostatnou položku v [`open-items.md`](../open-items.md), ne mimochodem tady.

## Důsledky

**Volající místo špatného dotazu dostane žádný dotaz a důvod.** To je pro T3 měřitelné zlepšení dvakrát: neúspěch se nově počítá jako neúspěch, a nepočítá se jako úspěch to, co odpovídá jinou množinou řádků.

**Převod, ve kterém selže jediný dotaz, doběhne.** Entity i ostatní dotazy se vydají; dosud u Dapperu spadl celý požadavek. Kombinuje se to s rozhodnutím [045](045-a-conversion-that-produced-nothing-says-so.md): pokud pak nevyjde vůbec nic, řekne to orchestrace záznamem o běhu.

**Stavový kód zůstává 200**, ze stejného důvodu jako u rozhodnutí 045 — částečný převod musí vydat, co vyrobil, a důvod patří do záznamů.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** vstup, který dřív vydal (chybný) artefakt, ho nově nevydá a přibude záznam; veřejné rozhraní ani tvar odpovědi se nemění.
