# 049 — Jazyková fakta vlastnosti pod pravidlem priority zdrojů

Datum: 2026-08-24
Stav: platí
Požadavky: F5, F11, F14, S2
Podklad: rozhodnutí [017](017-source-precedence-for-mapping-facts.md), [031](031-key-class-as-declaration-of-key-parts.md) a [036](036-primary-key-under-source-precedence.md); nález z 2026-08-23, oddíl 3.4

## Kontext

Rozhodnutí [017](017-source-precedence-for-mapping-facts.md) uspořádalo zdroje faktů a vyslovilo pravidlo *„vyšší stupeň platí, nižší smí jen doplnit, co chybí, a neshodu hlásí"*. Rozhodnutí [036](036-primary-key-under-source-precedence.md) ho dopsalo pro primární klíč, rozhodnutí [031](031-key-class-as-declaration-of-key-parts.md) pro deklarace klíčové třídy. Zapisovací cesty builderu to dnes dodržují: `AddTable`, `AddSchema`, `AddPrimaryKey`, `SetKeyStrategyDetails`, `SetCollectionKind`, `SetPropertyDatabaseMapping` i `SetPropertyDatabaseType` nejdřív kontrolují, jestli fakt už někdo netvrdil, a `GetOrCreatePropertyMap` je výslovně najdi-nebo-založ.

**`AddProperty` je mezi nimi jediná výjimka.** Přidá do `Entity.Properties` i do `PropertyMaps` bezpodmínečně:

```csharp
EntityMap.Entity.Properties.Add(property);
EntityMap.PropertyMaps.Add(new PropertyMap { Property = property });
```

Dnes je to latentní. Každá třída se parsuje jednou a `ParseProperties` navštíví každou deklaraci právě jednou, takže duplikát nevznikne. Naráží se na to až u otevřené položky *Framework s vlastní precedencí mezi svými artefakty*: **první framework, který tutéž vlastnost přečte ze dvou artefaktů** — javová třída plus `orm.xml` u JPA (F7, F9), třída plus XML mapper u MyBatisu (F8), třída plus fluent konfigurace u EF Core — **dostane dvě `Property` téhož jména**. Obě projdou branou úplnosti, obě se vypíšou, a vygenerovaná třída se nezkompiluje.

Mezera není jen v tom, že chybí kontrola. **Chybí pravidlo, co se má stát**, a to je věcná otázka: jazyková fakta vlastnosti nejsou jeden fakt, ale sada (typ, přístupový modifikátor, ostatní modifikátory, přítomnost getteru a setteru, inicializátor, jazyková nullabilita), a druhý artefakt může některá z nich znát a jiná ne. `orm.xml` u JPA typ vlastnosti obvykle neuvádí; `resultMap` MyBatisu ho uvádí. Bez pravidla by se první doplňoval, druhý přepisoval a rozdíl by mizel.

## Zvažované varianty

1. **Odmítnout druhou deklaraci výjimkou.** Deterministické a hlasité, ale proti rozhodnutí [010](010-diagnostics-as-returned-data.md): dvě deklarace téže vlastnosti nejsou chyba programu, jsou to legitimní vstupní data dvou artefaktů. Zamítáme.

2. **Poslední zápis vyhrává.** Nejjednodušší a nejhorší: výsledek by závisel na pořadí, ve kterém `ParserFactory` vydá parsery, a rozhodnutí 017 to pořadí sice fixuje, jenže pak by *pomocný mapovací artefakt přebíjel vstupní text frameworku*, což je přesně obráceně, než 017 rozhodlo. Zamítáme.

3. **Ignorovat druhou deklaraci celou.** Splňuje 017 v tom, že první tvrzení platí, ale zahodí i fakta, která první artefakt neuvedl — typ vlastnosti známé z `orm.xml` u třídy bez anotace by se ztratil, přestože ho nikdo netvrdil jinak. To je proti F5, která žádá *sloučení* metadat z více zdrojů. Zamítáme.

4. **Doplnit po jednotlivých faktech, rozdíl hlásit.**

## Rozhodnutí

**Volíme variantu 4 a rozšiřujeme pravidlo rozhodnutí 017 z mapovacích faktů na jazyková.** `AddProperty` se stává najdi-nebo-založ, stejně jako `GetOrCreatePropertyMap`:

- **Vlastnost, kterou entita nemá, se přidá** — to je dnešní chování a mění se jen tím, že se před ním hledá.
- **Vlastnost, kterou entita už má, se doplní po faktech.** Doplňuje se jen prázdný fakt: `Type`, `AccessModifier`, `DefaultValue`, které jsou `null`; `OtherModifiers`, které jsou prázdné; `HasGetter` a `HasSetter`, které jsou `false`. Přítomný fakt se nikdy nepřepisuje.
- **Neprázdný fakt tvrzený jinak je `Conflict`** — týž druh záznamu, jaký 017 zavedlo pro mapovací fakty, se stejnou větou o tom, že platí dříve přečtená hodnota. Záznam nese název vlastnosti a to, co tvrdila pozdější úroveň.

**Jazyková nullabilita cestuje s typem, ne zvlášť.** Podle rozhodnutí [014](014-language-type-model.md) sedí `IsNullable` na `LangType`, takže nemá vlastní pravidlo: přijde-li typ, přijde nullabilita s ním, a nepřijde-li typ, nullabilita nemá nositele.

**`HasGetter` a `HasSetter` se doplňují, nikdy neruší.** `false` je „nikdo netvrdil", ne „tvrdil, že není" — což je stejná asymetrie, jakou má `IsVersion` u mapovacích faktů (rozhodnutí [030](030-scope-of-version-1-0.md)) a doplňování z katalogu vůbec (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)). Druhý artefakt tedy může přístupovou metodu přidat, ale ne odebrat, a rozdíl v tomhle směru není konflikt.

**`OtherModifiers` se sjednocují, ne porovnávají.** Je to seznam a ne fakt: `virtual` v jednom artefaktu a `partial` v druhém nejsou rozpor, jsou to dvě tvrzení o téže deklaraci. Přidávají se ty, které tam ještě nejsou, v pořadí, v jakém přišly (S2).

**Rovnost se posuzuje po hodnotě, ne po referenci.** U `LangType` to znamená srovnání kategorie, skaláru nebo jména a nullability včetně prvku kolekce — tedy tak, jak se typ vypíše. Bez toho by dva shodné popisy téhož typu z různých artefaktů vyrobily falešný konflikt.

## Důsledky

**Dnešní chování se nemění.** Žádný dnešní framework nečte tutéž vlastnost dvakrát, takže větev doplnění se dnes nespustí. Zapisujeme ji dřív, než se na ni narazí, právě proto, že se na ni při vyzkoušení nepřijde — je to tentýž důvod, jaký uvádí rozhodnutí 017 pro překryv úrovní.

**Otevřená položka *Framework s vlastní precedencí mezi svými artefakty* tím nezaniká.** Tohle rozhodnutí říká, co se stane, když dva artefakty tvrdí totéž o jazykové straně; ta položka se ptá na něco jiného — jestli má precedence, kterou si zdrojový framework sám dokumentuje (fluent API nad anotacemi u EF Core), přebít naše pořadí uvnitř prvního stupně. Odpověď na to zůstává otevřená a tohle rozhodnutí ji nepředjímá: mění se jen to, že do té doby nevzniká duplikát a rozdíl se nezamlčí.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** přibývá diagnostický záznam a chování v dosud nedosažitelné větvi, dosavadní artefakty se nemění.
