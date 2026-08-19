# 027 — Ověření generovaných dotazů

Datum: 2026-08-19
Stav: platí
Požadavky: F11, F13, T2, T3, S2
Podklad: rozhodnutí [016](016-generated-artifact-verification-levels.md), jehož stupně sem rozšiřujeme, a [022](022-native-query-syntax-in-builders.md); JSS §6.2, pravidla Q13 a Q15

## Kontext

Rozhodnutí [016](016-generated-artifact-verification-levels.md) uznalo čtyři stupně ověření — tvar, překlad, přijetí frameworkem, běh proti databázi — a celé je postavilo na entitních artefaktech. Dotazů se nedotklo, protože dotazový builder byl jeden a jeho výstup se soudil řetězcovou asercí.

Rozhodnutí [022](022-native-query-syntax-in-builders.md) to mění. Cíle mají generovat nativní dotazový tvar, a právě u něj je rozdíl mezi „napsali jsme, co jsme čekali" a „framework to vezme" největší: řetěz LINQ může být syntakticky bezvadné C#, které EF Core nedokáže přeložit na SQL, a HQL může být bezvadný řetězec odkazující na vlastnost, kterou namapovaná třída nemá. Ani jedno neodhalí žádná aserce nad textem.

Zároveň platí, co říká pravidlo Q13: dotaz je úplný, jen když jsou jeho zdroj i všechny odkazované atributy rozresolvovatelné přes mapování. To je tvrzení, které je čím ověřit — a u jednoho ze tří cílů je to jediné, co ověřit lze.

## Zvažované varianty

Otázka není, jestli dotazy ověřovat — 016 tu odpověď dalo pro artefakty obecně. Otázka je, **co u dotazů znamená třetí stupeň**, protože „framework mapování přijme" se na dotaz nepřenáší doslova.

1. **Zůstat u tvarových asercí a spolehnout se na entitní ověření.** Zamítáme: entitní ověření neřekne o dotazu nic a právě dotazy jsou to, co T2 měří po kategoriích.
2. **Ověřovat spuštěním proti databázi.** Nejsilnější, ale je to čtvrtý stupeň, ne třetí; zdědil by všechny výhrady, které proti němu 016 vzneslo, a nešel by použít tam, kde databáze není.
3. **Najít u každého cíle nejsilnější verdikt, který nepotřebuje spojení.**

## Rozhodnutí

**Volíme variantu 3 a zapisujeme, co u kterého cíle je nejsilnějším dostupným verdiktem bez databáze. Stupně zůstávají čtyři a jejich význam se nemění; mění se jen to, čím se u dotazu naplní.**

| Cíl | 2. stupeň — překlad | 3. stupeň — přijetí |
|---|---|---|
| EF Core | generovaný C# se zkompiluje | `ToQueryString()` přeloží výraz na SQL |
| NHibernate | generovaný C# se zkompiluje | plán dotazu se zkompiluje proti namapovanému modelu |
| Dapper | generovaný C# se zkompiluje **a** SQL se rozparsuje | odkazované tabulky a sloupce se najdou v mezireprezentaci |

**EF Core: `ToQueryString()`.** Metoda vrátí SQL, které by se poslalo, aniž by se dotaz provedl nebo otevřelo spojení. Selže na všem, co poskytovatel nedokáže přeložit, tedy přesně na třídě chyb, kterou tvarová aserce nevidí. Aby to šlo, musí mít generovaný artefakt daný tvar: **metoda s jediným parametrem typu `DbContext` vracející `IQueryable`**, s kořenem psaným `ctx.Set<T>()`. Obojí je součástí zadání builderu, ne až ověření — kdyby artefakt vracel `List<T>`, ověření by dotaz muselo provést, a kdyby se odkazoval na pojmenovaný `DbSet`, potřeboval by vygenerovanou třídu kontextu, kterou dotazový builder nemá odkud vzít. Týž tvar už mimochodem předpokládá `AdvisorBenchmarking`, když si generovaný dotaz hledá reflexí.

**NHibernate: kompilace plánu dotazu.** Sestavení session factory běží v tomhle řešení bez spojení už dnes (dialekt, ovladač, vypnuté `hbm2ddl.keywords`); kompilace plánu HQL nad hotovou factory potřebuje navíc jen mapovací metadata. Verdikt je přísnější než u EF Core, protože odmítne i nenamapovanou entitu a neexistující vlastnost — je to tedy **kontrola pravidla Q13 provedená samotným cílovým frameworkem**.

**Dapper: 3. stupeň splývá s 2. a doplňuje se o kontrolu proti mezireprezentaci.** Dapper dotaz neposuzuje; je to materializátor, kterému je řetězec lhostejný. Tvrdit u něj „framework to přijal" by bylo prázdné. Nejsilnější, co se dá říct, je dvojí: SQL se rozparsuje parserem jazyka, a každá tabulka a každý sloupec, na které se odkazuje, se najdou v mapovacích faktech převodu. Druhá polovina je totéž pravidlo Q13, jen provedené námi, protože to za nás nikdo neudělá. **Že se stupně u jednoho cíle slévají, zapisujeme jako fakt o frameworku, ne jako mezeru** — je to táž třída konstatování jako prázdné kroky Dapper builderu v entitní větvi.

**Negativní polovina je povinná u každého nástroje.** Platí beze změny, co řeklo 016: stupeň, který nikdy neřekne ne, nedokazuje nic. Ke každému verdiktu patří případ, na kterém je vidět, jak odmítá — LINQ výraz, který se nepřeloží; HQL nad nenamapovanou vlastností; SQL se syntaktickou chybou; SQL nad sloupcem, který mezireprezentace nezná.

**Čtvrtý stupeň zůstává otevřený**, ale nově má smysl i pro dotazy a je to přímá cesta k F13: spustit zdrojovou i přeloženou variantu a porovnat normalizované výsledky. Prostředí pro to existuje — testovací schéma i přeskočení, když databáze není. Že se to nedělá teď, je rozsah, ne překážka.

## Důsledky

**Vzniká materiál pro diferenční ověření dřív, než se F13 začne řešit.** Pro tentýž dotaz vydá EF Core přes `ToQueryString()` SQL a NHibernate přes plán dotazu rovněž SQL, a Dapper builder generuje SQL přímo. Tři SQL věty pro jeden model, získané bez databáze, jsou první doklad funkční ekvivalence podle T3 — a zároveň místo, kde bude vidět, co pravidlo Q15 znamená v praxi: shodná struktura neznamená shodný text.

**Tvrzení se opět váže na verze balíčků** v `Tests.csproj` a musí souhlasit s tabulkou v `architecture.md`, dokud verzi nenese deskriptor (rozhodnutí [013](013-target-framework-versions.md)).

**Testy bez databáze zůstávají rychlé.** Žádný z tří verdiktů spojení neotevírá, takže dotazová část sady běží i v CI, kde databáze není — na rozdíl od scénářů se zdrojem v Dapperu, které se tam přeskakují.
