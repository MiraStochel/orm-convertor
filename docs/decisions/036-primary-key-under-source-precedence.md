# 036 — Primární klíč pod pravidlem priority zdrojů

Datum: 2026-08-21
Stav: platí
Požadavky: F5, F11, F14, S2
Podklad: audit 2026-08-21, nález 1.2

## Kontext

Rozhodnutí [017](017-source-precedence-for-mapping-facts.md) uspořádalo zdroje faktů uvnitř vstupu a jeho implementace dala zápisovým cestám builderu jednotný režim: vyplňuje se jen prázdný fakt, shodné opakování není událost a odlišné pozdější tvrzení končí záznamem `Conflict` se zachovanou první hodnotou, takže mezi artefakty téže úrovně deterministicky vítězí hodnota zapsaná jako první (S2). `AddPrimaryKey` z toho režimu implementace vědomě vynechala: jeho kontrakt říká, že celý klíč vzniká jediným voláním a opakované volání ho i s detaily strategie nahradí.

Důsledek je přesně ta vada, kterou pravidlo 017 mělo zavřít: dva vstupní artefakty téhož frameworku, které o téže třídě tvrdí různý klíč, skončí u toho pozdějšího a převod o rozporu neřekne nic — proti F5, který hlášení konfliktů žádá, i proti S2, protože výsledek závisí na pořadí artefaktů. Dosažitelné je to už dnes: verze 1.0 nárokuje vícesouborový vstup pod F14, `ConvertRequest` i `ConversionHandler` seznam zdrojů berou a dvě mapovací XML NHibernate nad toutéž třídou jsou platný vstup. S prvním parserem fluent konfigurace EF Core přibude druhý artefakt, který klíč tvrdí, uvnitř jediného frameworku.

Katalogová strana hlídaná je: fáze doplnění klíč doplňuje jen tam, kde žádný není, jinak ho porovnává a rozdíl hlásí (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), `architecture.md` §5.2). Nehlídaný zůstal právě první stupeň — a spolehnout se na katalog nejde, protože je to nepovinná fáze: převod bez připojení musí rozpor ohlásit sám.

Vynechání mělo důvod a ten důvod je třeba unést, ne zamlčet. Klíč není skalární fakt jako název tabulky: je to uspořádaný seznam částí, každá se strategií, k tomu záznam klíčové třídy zdroje — a kolem definice žijí dvě věci, které nahrazovací kontrakt obsluhoval. Zaprvé vnitřní volání builderu: syntéza junction entit (rozhodnutí [005](005-many-to-many-as-explicit-junction-entity.md)) klíč spojovací entity také definuje voláním `AddPrimaryKey`. Zadruhé detaily strategie: `SetKeyStrategyDetails` zapisuje název generátoru ze zdroje a jeho parametry k částem až po definici klíče, takže každé pravidlo pro opakované `AddPrimaryKey` musí říct, co se stane s detaily, které po zahozeném tvrzení teprve přijdou.

## Zvažované varianty

1. **Zúžit nárok verze u F5 a S2 na jediný mapovací artefakt na framework.** Vada by přestala být uvnitř zaručované oblasti a kód by se nemusel měnit. Jenže verze nárokuje vícesouborový vstup pod F14, a hranice „víc souborů ano, ale ať netvrdí totéž dvakrát jinak" se nedá vyslovit jako oblast — je to podmínka na obsah vstupu, kterou čtenář nemá jak předem ověřit. Vada by navíc nezmizela, jen by se přejmenovala. Zamítáme.

2. **Ponechat nahrazení a nechat rozpor hlídat porovnáním s katalogem.** Nefunguje dvakrát: bez připojeného katalogu se neporovnává nic, a i s katalogem se hlásí rozpor sjednoceného výsledku s databází, ne rozpor dvou vstupních artefaktů mezi sebou — záznam by jmenoval špatného viníka. Závislost na pořadí artefaktů by trvala v obou případech. Zamítáme.

3. **Vztáhnout pravidlo 017 na klíč po jednotlivých polích a části dvou klíčů slévat.** Mechanická obdoba ostatních cest: sjednotit množiny částí, po polích vyplnit a rozdíly hlásit. Jenže části klíče do sebe zapadají — pořadím, strategií, záznamem klíčové třídy — a sjednocení dvou různých klíčů je klíč, který netvrdil nikdo. Je to týž důvod, proč 017 zamítlo rovnocenné zdroje: bez pravidla, která strana platí, by se do mezireprezentace zapisoval výmysl. Invariant modelu (jednoznačné `Order` částí) by navíc u pozičního slévání neměl smysluplné řešení. Zamítáme.

4. **Klíč jako jeden složený fakt pod pravidlem 017.** Identitou klíče je uspořádaný seznam jeho částí. První definice platí; opakované volání se porovnává místo nahrazení — shodné je ne-událost, tvrzení nad týmiž částmi smí doplnit, co první nechalo prázdné, a odlišný seznam částí je `Conflict` se zachovaným prvním klíčem vcelku. Detaily strategie přecházejí na týž režim a detail zahozeného tvrzení padá spolu s ním.

## Rozhodnutí

Volíme variantu 4. **Primární klíč spadá pod pravidlo rozhodnutí 017 celý, jako jeden složený fakt: první definice platí, shodné opakování není událost, tvrzení nad týmiž částmi jen doplňuje mezery a odlišné tvrzení je záznam `Conflict` se zachovaným prvním klíčem vcelku — včetně detailů strategie.**

Konkrétně, po větvích `AddPrimaryKey`:

- **Prázdný fakt vyplní první volání** — dnešní chování beze změny. Tudy vzniká i klíč syntetizované junction entity (čerstvě založená entita klíč nemá, takže vnitřní volání builderu na pravidlo nenarazí) a klíč doplněný z katalogu (fáze doplnění volá `AddPrimaryKey` jen nad entitou bez klíče, jinak porovnává — svou polovinu pravidla drží od rozhodnutí 015).
- **Opakované volání se stejným seznamem částí** — porovnáno po jménech vlastností v pořadí klíče, ordinálně — není událost. Po částech se doplní strategie, kterou první tvrzení nechalo `Unspecified`: `Unspecified` nic netvrdí, stejně jako `CollectionKind.Unspecified` u druhu kolekce a stejně, jako strategii identity doplňuje katalog. Dvě odlišné vyslovené strategie téže části jsou `Conflict` (kategorie `PrimaryKeyStrategy`) se zachovanou první. Záznam klíčové třídy se doplní, kde chyběl; dvě odlišná jména třídy jsou `Conflict` (kategorie `PrimaryKey`).
- **Opakované volání s jiným seznamem částí** je `Conflict` (kategorie `PrimaryKey`) a zahazuje se vcelku: první klíč platí dál i s detaily strategie. Po částech se nevybírá — viz zamítnutí varianty 3.

**Detail strategie padá se svým tvrzením.** `SetKeyStrategyDetails` následuje v každém parseru bezprostředně po `AddPrimaryKey` téhož zdroje; builder si proto u posledního volání `AddPrimaryKey` nad entitou pamatuje, čí tvrzení o kterých částech zahodil, a detaily k zahozenému tvrzení mlčky pouští — rozpor je už zaznamenaný u klíče a druhý záznam by tutéž událost hlásil dvakrát. Mimo tento případ zůstává vlastnost mimo klíč programátorskou chybou a výjimkou jako dosud. Detaily samotné drží týž režim jako ostatní fakty: název strategie ze zdroje a parametry generátoru vyplňují jen prázdný fakt — parametry po jednotlivých klíčích slovníku, jako ostatní klíč–hodnota fakty —, odlišné pozdější tvrzení je `Conflict` (kategorie `PrimaryKeyStrategy`) se zachovanou první hodnotou.

Pravidlo řídí zápisovou cestu builderu pro vstupní zdroje, stejně jako 017. Kdo model vlastní — přestavba části v `SetKeyStrategyDetails`, doplnění strategie z katalogu — přiřazuje `EntityMap.PrimaryKey` přímo a pravidlo ho neomezuje.

Vůči rozhodnutí 017 je to doplnění v jeho vlastní ose, ne změna volby: 017 dál platí ve všem, co říká, včetně výhrady o frameworku s vlastní dokumentovanou precedencí. Až fluent parser EF Core přinese explicitní klíč nad konvenčním klíčem třídy, bude rozpor nově deterministický a hlášený — ale jestli má precedence zdrojového frameworku uvnitř prvního stupně naše pořadí obrátit, zůstává otevřenou položkou, kterou tohle rozhodnutí nezavírá.

## Důsledky

Mezireprezentace se nemění a původ faktu se dál neeviduje (rozhodnutí [010](010-diagnostics-as-returned-data.md)): „už tvrzeno" znamená „klíč je při příchodu dalšího tvrzení neprázdný" a u strategie části „není `Unspecified`" — táž rovnost, o kterou se opírá 017.

Dva mapovací XML NHibernate nad toutéž třídou: shodné klíče projdou beze záznamu (běžný případ — tentýž vstup rozdělený do souborů), odlišné skončí prvním klíčem a záznamem. Vlastnosti, které nese jen zahozené tvrzení, se do entity nezakládají — nahrazovací kontrakt je zakládal, takže výstup je nově i o tenhle vedlejší účinek čistší.

Syntéza junction entit ani katalogová fáze se nemění; obě stojí na větvi prázdného faktu, což bylo ověřeno čtením obou cest. Očekávané chování drží testy v `SourcePrecedenceTest`: zachování prvního klíče i jeho detailů při odlišném pozdějším tvrzení, doplnění `Unspecified` strategie, ne-událost shodného opakování, konflikt parametrů generátoru a tichý pád detailů zahozeného tvrzení, včetně průchodu dvou XML artefaktů celým převodem.

Otevřená položka „Priorita zdrojů se nevztahuje na primární klíč" tímto mizí; položka „Framework s vlastní precedencí mezi svými artefakty" zůstává beze změny.
