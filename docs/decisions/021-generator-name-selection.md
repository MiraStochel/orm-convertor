# 021 — Výběr názvu generátoru ve výstupu

Datum: 2026-08-19
Stav: platí
Požadavky: F2, F3, F7–F10, F11, S1, S2
Podklad: rozhodnutí [011](011-key-generation-strategy-vocabulary.md) a [012](012-foreign-key-rendering.md); NHibernate 5.7.0 — generátory `hilo`, `seqhilo`, `guid.comb` a `foreign`; `architecture.md` §5.1

## Kontext

Rozhodnutí [011](011-key-generation-strategy-vocabulary.md) přiřklo poli `SourceStrategyName` roli záznamu pro diagnostiku, ne vstupu generování. Builder proto vypisuje výhradně kanonický název z `ToNHibernate`, a při překladu z NHibernate do NHibernate to znamená tohle:

| Zdroj napsal | Vyjde | Co se stalo |
|---|---|---|
| `seqhilo` | `hilo` | čítač se přestěhoval ze sekvence do tabulky |
| `guid.comb` | `guid` | z hodnot uspořádaných pro index se staly náhodné |
| `foreign` | `assigned` | identita se přestala přebírat z protistrany |

Ani jeden z těch tří názvů cílový framework neodmítá — jsou to jeho vlastní generátory. Fakt se tedy neztrácí proto, že by ho cíl neuměl vyjádřit, ale proto, že mu ho nenabídneme. To je jiná situace než ta, pro kterou vzniklo rozhodnutí [004](004-unexpressible-facts-as-warnings.md): tam se hlásí fakt, který cíl vyjádřit **neumí**, a hlášení je jediné, co lze udělat. Tady je hlášení náhradou za výstup, který jsme udělat mohli.

**U `foreign` to není otázka odstínu.** Rozhodnutí [012](012-foreign-key-rendering.md) na něm postavilo rozpoznání vztahu 1:1 přes sdílený primární klíč: závislá entita ho vyjadřuje generátorem `foreign` s parametrem `property`, což je po rozhodnutí 011 hodnota `Unspecified` se zachovaným názvem, a `SharesPrimaryKeyThrough` v builderu NHibernate ten název čte. Výstup tedy dnes dostane `<one-to-one constrained="true">` správně, ale generátor `assigned` k němu — a NHibernate identitu z protistrany nepřevezme, takže výsledek popisuje jiný vztah, než jaký byl na vstupu. Rozšířit kvůli tomu mezireprezentaci o příznak sdíleného klíče zamítlo právě rozhodnutí 012 (varianta 2) s tím, že by duplikovalo fakty, které model už nese; tahle volba tedy stojí, a otázka je jen, jestli je smí builder použít.

`seqhilo` má oproti tomu vlastnost, které si všimlo až rozhodnutí [020](020-canonical-generator-parameter-vocabulary.md): rozdíl mezi ním a `hilo` **je od té chvíle faktem modelu**, protože jeden nese `SequenceName` a druhý `CounterTable`. Rozhodnutí 011 to samo předjímalo, když odmítlo dělit `HiLo` na dvě hodnoty s odůvodněním, že umístění čítače je parametr, ne jiný mechanismus. Jenže builder z toho parametru nic nevyvozuje a vypisuje `hilo` vždy.

Otázka tedy zní obecněji, než jak byla položená: **podle čeho vybírá builder název generátoru** — a název ze zdroje je jen jedním ze tří možných zdrojů té volby vedle kanonického slovníku a vedle faktů, které model nese.

## Zvažované varianty

1. **Zůstat u kanonického názvu.** Dnešní stav podle 011. Má jednu velkou přednost: název ve výstupu je funkcí modelu, takže je předvídatelný a nezávislý na tom, odkud model přišel. Cenou jsou tři degradace výše, z nichž jedna mění vztah mezi entitami. Zamítáme, protože překlad má reprodukovat význam zdroje tam, kde ho cíl unese, a tady ho cíl unese.

2. **Použít název ze zdroje vždy, když je k dispozici.** Nejjednodušší oprava a nejhorší. `SourceStrategyName` nese i vlastní generátorovou třídu zapsanou jménem typu a po rozšíření o javový ekosystém i jména, která v .NET neexistují; builder by je vypsal a NHibernate by při startu selhal na nenačtený typ. Z hlášené ztráty by se stal rozbitý artefakt. Zamítáme.

3. **Kritérium „název se zpětně mapuje na tutéž hodnotu výčtu".** Tak byla otázka zapsaná v otevřených položkách a na první pohled je to správná formulace. **Je ale prázdná pro `Unspecified`:** `FromNHibernate` mapuje každý neznámý řetězec na `Unspecified`, takže vlastní třída `MyApp.MyGenerator` kritérium splní úplně stejně jako `foreign`. Prošlo by tedy přesně to, co má varianta 2 zakázáno. Záměr je správný, kritérium samo nikoli.

4. **Rozšířit výčet o `SeqHiLo`, `GuidComb` a `Foreign`.** Odpadla by potřeba jakéhokoli kritéria, protože fakt by nesl model. Je to ale návrat varianty 2 z rozhodnutí 011: výčet by se stal seznamem generátorů NHibernate, rostl by s každým dalším frameworkem a stejně by nebyl úplný, protože množina vlastních generátorů konečná není. Do mezireprezentace by se vrátilo API konkrétního frameworku (JSS §5.4).

5. **Odvodit název z faktů, které model nese, a název ze zdroje připustit jen jako rozhodčího mezi zápisy, které model nerozlišuje — a jen ze slovníku cílového frameworku.**

## Rozhodnutí

Volíme variantu 5. **Název generátoru vybírá builder ve třech krocích a v tomto pořadí: nejdřív z kanonických faktů, pak z názvu ze zdroje, pokud ho cílový framework zná a znamená týž mechanismus, a teprve nakonec z kanonického slovníku se záznamem o ztrátě.**

**Krok první — fakty před jmény.** Kde mechanismus spolu s parametry určuje generátor cíle jednoznačně, vybírá se z nich a název ze zdroje se neptáme. Pro NHibernate to znamená `HiLo` se `SequenceName` jako `seqhilo` a `HiLo` s `CounterTable` nebo bez parametrů jako `hilo`. Tenhle krok není ústupek — je to náprava toho, že builder dosud parametr ignoroval, a platí i pro model, který žádný název ze zdroje nenese, tedy i pro překlad z javového ekosystému.

**Krok druhý — název ze zdroje jako rozhodčí.** Zbudou zápisy, které se v modelu neliší ničím: `guid`, `guid.comb` a `uuid.hex` jsou všechny `Uuid`; `assigned`, `foreign` a `select` jsou všechny `Unspecified`. Tam builder použije `SourceStrategyName`, splňuje-li **obě** podmínky:

1. **Cílový framework ten název zná** — je v seznamu generátorů, který wrapper cílového frameworku o sobě vyslovuje.
2. **Znamená týž mechanismus** — vlastní převod cíle z toho názvu vrací tutéž hodnotu, jakou nese `Strategy` části klíče.

První podmínka je to, co variantě 3 chybělo, a druhá je to, co zabraňuje názvu odporovat modelu. Společně dávají pravidlo, které si zaslouží být vysloveno samostatně, protože se o něj opře i text práce: **název ze zdroje nikdy nerozhoduje o tom, co se vygeneruje, jen o tom, jak se to zapíše mezi zápisy, které model nerozlišuje.** Role, kterou rozhodnutí 011 poli přiřklo, se tím zužuje, nikoli obrací — a protože jde o změnu volby, ne o doplnění případu, patří do nového rozhodnutí, tedy sem.

**Krok třetí — kanonický název a záznam o ztrátě**, kdykoli obě podmínky neplatí. Vlastní generátorová třída, generátor cizího ekosystému i název, který znamená jiný mechanismus, než jaký část klíče nese, končí zde. Záznam `Loss` podle rozhodnutí [010](010-diagnostics-as-returned-data.md) zůstává, ale nově řekne proč — že cílový framework ten název nezná —, místo dnešního obecného konstatování, že kanonický zápis se od zdroje liší.

**Seznam známých názvů bydlí ve wrapperu cílového frameworku, ne v deskriptoru.** Deskriptor (rozhodnutí [009](009-target-framework-descriptor.md)) popisuje vztah ke **kategoriím** mapovacích faktů a jeho hrubost je záměrná — jedna hodnota za fakt, který umí dodat katalog a vypsat builder. Seznam generátorů je oproti tomu slovník frameworku, tedy přesně to, co podle S1 a JSS §4.3 nemá opouštět svůj wrapper. Prakticky je to rozšíření `PrimaryKeyStrategyConvertor`, který tabulku názvů už drží; dnes jen slévá neznámý řetězec a `assigned` do jedné větve, takže rozdíl „neznám" versus „znám a mapuji na `Unspecified`" musí umět vyslovit.

**Vlastní generátorovou třídu vědomě nepropouštíme, i když by v překladu z NHibernate do NHibernate fungovala.** Builder neví, ze kterého frameworku model přišel — mezireprezentace původ faktu nenese a rozhodnutí 020 na tom postavilo celý svůj výběr —, takže by musel vypisovat libovolný řetězec naslepo. Vyměňujeme věrnost v jednom případě za jistotu, že nevznikne artefakt, který se nespustí; ztráta se přitom hlásí, takže není tichá.

**Pořadí uvnitř generování: nejdřív název, pak parametry.** Který generátor se vypíše, určuje, pod jakými názvy se vypíšou jeho parametry — `seqhilo` chce `sequence` a `max_lo`, `hilo` chce `table` a `column`. Denormalizační tabulka rozhodnutí 020 se tedy klíčuje vybraným generátorem, ne mechanismem. Pro strategii, která zůstala na únikové cestě, platí totéž, co uložilo 020: parametry jdou ven doslova, takže `foreign` si s sebou nese `property` beze změny a rozpoznání sdíleného klíče podle rozhodnutí 012 se uzavře i na výstupu.

**Pravidlo je oprávnění, ne povinnost.** Cíl, který pro název generátoru nemá kam psát, ho nepíše: EF Core vyjadřuje strategii anotací `[DatabaseGenerated]`, která žádné jméno generátoru nezná, takže se pro něj nemění nic.

## Důsledky

Přepis je jednorázový a bez přechodného období (rozhodnutí [003](003-one-shot-migration.md)).

**Rozhodnutí 011 zůstává v platnosti a nedostává stav `nahrazeno`.** Mění se jediná věta o roli jednoho pole; slovník mechanismů, úniková cesta i pravidlo pojmenování stojí beze změny a odkazuje se na ně kód, `architecture.md` i rozhodnutí 012, 019 a 020. Označit celé 011 za nahrazené by čtenáře poslalo hledat slovník do souboru, který ho neobsahuje. Vazbu proto nese tenhle směr: kdo čte 011, najde zúžení přes index a přes odkazy odsud.

**Překlad z NHibernate do NHibernate se stává věrným u `seqhilo`, `guid.comb` i `foreign`**, tedy u tří případů, které dnes mění chování výstupu. U `foreign` se tím zároveň dorovnává rozhodnutí 012: signál, který builder už čte, se poprvé propíše i do generátoru.

**Sdílený primární klíč napříč ekosystémy tohle rozhodnutí neřeší.** Signál, který zvolilo 012, je vyslovený slovem NHibernate, takže parser JPA, který totéž čte z `@MapsId`, nemá co do modelu zapsat, aniž by tam vložil cizí slovník. Zůstává to tam, kde to nechalo 012, a s prvním javovým parserem to bude potřebovat vlastní rozhodnutí.

**Determinismus podle S2 se nemění.** Odvození z faktů i seznam známých názvů jsou vlastnosti modelu a cílového frameworku, ne pořadí nebo podoby vstupu; tentýž model dá tentýž výstup.

**Ověření generovaných artefaktů zůstává záchytnou sítí, ne obranou.** Třetí stupeň podle rozhodnutí [016](016-generated-artifact-verification-levels.md) by neplatný název generátoru odhalil, ale odhalit ho jako selhání je horší výsledek než ohlásit ztrátu a vydat platný artefakt. Proto je podmínka prvního kroku uzavřený seznam, ne pokus a kontrola.

**Testy.** Obousměrný převod NHibernate pro `seqhilo` (nesmí vyjít `hilo`), `guid.comb` a `foreign` včetně kontroly, že u `foreign` vyjde vedle generátoru i `<one-to-one constrained="true">`; a test, že vlastní generátorová třída skončí kanonickým `assigned` se záznamem o ztrátě, protože právě tenhle případ varianty 2 a 3 propouštěly.

**Seznam známých generátorů je nutné při implementaci ověřit proti zafixované verzi** (NHibernate 5.7.0 podle rozhodnutí [013](013-target-framework-versions.md)); rozhodnutí 011 už jednou doplňovalo, že jich framework registruje víc, než uvádí srovnávací analýza. Rozhodnutí fixuje pravidlo a pořadí kroků, ne obsah seznamu — jeho doplnění je oprava tabulky, ne změna volby.
