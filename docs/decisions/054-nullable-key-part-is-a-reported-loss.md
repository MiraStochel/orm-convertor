# 054 — Jazyková nullabilita části klíče je hlášená ztráta

Datum: 2026-08-24
Stav: platí
Požadavky: F1, F11, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md) a [014](014-language-type-model.md); nález z 2026-08-23, oddíl 3.9

## Kontext

`EFCoreEntityBuilder` i `NHibernateEntityBuilder` mají tentýž řádek:

```csharp
var type = (!isPrimaryKey && langType.IsNullable) ? $"{typeName}?" : typeName;
```

Zdrojové `public int? Id`, které se stane částí klíče, se tedy vypíše jako `public int Id`. **Mění se typ členu třídy** — a nevzniká žádný záznam.

Je to konzistentní přes oba buildery, takže to není asymetrie jako u doslovného SQL typu (rozhodnutí [052](052-literal-sql-type-reaches-the-ef-core-annotation.md)). Je to **nepopsané pravidlo**: mezireprezentace jazykovou nullabilitu podle rozhodnutí [014](014-language-type-model.md) nese na `LangType`, tedy jako plnohodnotný fakt vedle databázové nullability na `PropertyMap` (aby mělo pravidlo E4 co porovnávat), a buildery ji u klíče zahodí bez zmínky. Dapper, který o klíčích neví, ji naopak zachová — takže tentýž model vydá pro dva cíle `int` a pro třetí `int?`, aniž by to cokoli vysvětlovalo.

Rozhodnutí [010](010-diagnostics-as-returned-data.md) a [004](004-unexpressible-facts-as-warnings.md) přitom říkají: co zdroj tvrdil a výstup nenese, se hlásí. F11 to formuluje jako zákaz tichého vynechání.

**Samotné chování je správné a nemění se.** Nullable klíč nedává smysl v žádném z cílů: `<id>` v NHibernate je sloupec, který nesmí být `NULL`, a EF Core klíčovou vlastnost nullable typu odmítne při stavbě modelu. Otázka není *jestli* zploštit, ale *čím ta změna je*.

## Zvažované varianty

1. **Nechat beze slova a dopsat pravidlo do `architecture.md` §5.** Popis by přestal chybět, ale volající, který posílá `int? Id` a dostane zpátky `int Id`, se to z odpovědi nedozví — a odpověď je jediné, co má. F11 mluví o diagnostice převodu, ne o dokumentaci nástroje. Zamítáme.

2. **Hlásit `Convention`.** Lákavé, protože „cíl si vynutil vlastní tvar" zní jako konvence cíle. Jenže `Convention` je podle `architecture.md` §5.1 *„výstup tvrdí něco, co zdroj neřekl"* — dosazená `assigned` za neuvedenou strategii, vynechaný `column` ponechaný konvenci cíle. Tady zdroj **řekl** a výstup říká opak. Zamítáme.

3. **Hlásit `Loss`.**

## Rozhodnutí

**Volíme variantu 3: zploštění jazykové nullability části klíče je `Loss` s kategorií `Nullability`.** Zdroj nesl fakt, výstup ho nenese — což je definice `Loss` podle §5.1 a přesně týž případ jako sousední *„nullable sloupec za nenullovatelnou vlastností u EF Core"*, se kterým se to jinak slévá do jedné dvoukanálové nullability.

**Znění záznamu říká, proč se to stalo, ne jen že se to stalo.** Uvádí, že klíč nemůže být nullable v žádném z cílů, takže volající pozná rozdíl mezi „přišli jsme o to nedopatřením" a „tvůj vstup tvrdil něco, co cílový framework odmítne" — což je u tohohle faktu jediná užitečná informace.

**Hlásí se jednou za část klíče a jen tam, kde se opravdu zplošťuje**, tedy když jazykový typ té části nullabilitu nese. Část klíče bez jazykového typu nebo s nenullovatelným typem záznam nedostane; hlásit „nic se neztratilo" je šum, který podle rozhodnutí 010 do záznamů nepatří.

**Znění je společné oběma builderům.** Pomocná metoda sedí na `AbstractEntityBuilder` vedle ostatních hlásičů (`ReportUnreadableFact`, `ReportInputConflict`), takže se dvě formulace téhož faktu nerozejdou a **příští cíl s plochým klíčem** — JPA podle F7 a F9, které klíč rovněž zplošťují (rozhodnutí [006](006-flat-composite-key-rendering.md)) — **ji dostane hotovou**. Volá ji ale každý builder sám, protože zploštit je jeho volba: Dapper nullabilitu zachovává a záznam by u něj byl nepravdivý.

**Chování emise se nemění.** `int? Id` se dál vypíše jako `int Id`; přibývá jen záznam.

## Důsledky

**Převod, který dosud vypadal beze ztrát, jich nově vykáže jednu — a vždycky ji vykazoval, jen se o ní nemluvilo.** Je to táž třída změny jako u rozhodnutí [045](045-a-conversion-that-produced-nothing-says-so.md): to, co dřív vypadalo jako úspěch, dostává poctivější popis.

**Dvoukanálová nullabilita je tím popsaná celá.** `architecture.md` §5 dosud vyslovovala jen směr „nullable sloupec za nenullovatelnou vlastností"; druhý směr u klíče chyběl, a přitom oba pramení z toho, že model nese nullabilitu na dvou místech schválně.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** přibývá diagnostický záznam, artefakty se nemění.
