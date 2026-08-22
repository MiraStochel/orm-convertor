# 030 — Rozsah verze 1.0

Datum: 2026-08-20
Stav: revidováno
Požadavky: F1–F15, S1–S7, T1–T7
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [018](018-work-order-as-item-marker.md) a [029](029-database-connection-is-the-consumer-projects-fact.md); `open-items.md` ke dni rozhodnutí

## Kontext

Nástroj má dnes dvacet dva otevřených položek a k tomu tabulku vzdálenějšího horizontu s pěti bloky ze zadání. Rozhodli jsme se ho uzavřít do vydané verze, která **nebude umět nic pro další frameworky nad dnešní tři .NET ORM** a jejíž součástí bude přepracovaný frontend. Verze má být spustitelná námi a vedoucím, nasazená jako jediná instance na vlastní ručně spravované stránce; kontejnerové nasazení ani cizí čisté prostředí do ní nepatří.

To samo o sobě ale neříká, co se má stihnout. Bez vysloveného kritéria se řez udělá podle nálady — a nálada vybere levné věci, protože ty jdou odbavit rychle, kdežto nebezpečné jsou zrovna ty drahé.

Stav částí je přitom velmi nestejný. Překladová větev je hotová a otestovaná, dotazová hotová ve všech devíti směrech v rozsahu pěti kategorií, doplňování z katalogu implementované. Vedle toho leží `AdvisorBenchmarking`, který si názvy typů tahá z generovaného textu regulárními výrazy a nemá jediný test, a `Advisor`, jehož nativní knihovna se staví jen v Dockeru. A mezi tím řada položek, kde nástroj vydá výsledek chudší nebo neplatný, aniž by to řekl: `NHibernateEntityBuilder` vypisuje kolekce natvrdo jako `<bag inverse="true" cascade="all-delete-orphan">` a vztah N:M jako `<many-to-many>` bez atributu `table`, EF Core builder negeneruje `[Required]`, ačkoli to deskriptor deklaruje jako vyjádřitelné.

Otázka tedy zní, podle čeho se pozná, co do verze patří.

## Zvažované varianty

1. **Odbavit všechny otevřené položky.** Nejde to ani v principu: F7–F13 je samo o sobě práce na další diplomku a několik položek je blokovaných frameworky, které v rozsahu nejsou — kritérium pro širší čtení konvencí zdroje se dá rozhodnout až proti MyBatisu, precedence uvnitř frameworku až proti fluent konfiguraci EF Core. Uzavřít je nelze, jen předstírat. Zamítáme.

2. **Řezat podle ceny — vzít, co je levné.** Cena je vlastnost práce, ne výsledku, takže verze by vznikla jako soubor náhod. Hůř: vypadly by z ní právě ty drahé a nebezpečné položky. Neplatné mapování kolekce je jedna z nejdražších na seznamu a zároveň jediná, která umí vygenerovat kaskádní mazání sirotků, které nikdo netvrdil. Zamítáme.

3. **Řezat podle pravidla „nástroj nesmí lhát".** Dovnitř to, co dnes vede k tichému nebo neplatnému výstupu, ven to, co je už dnes hlášené záznamem. Kritérium plyne z rozhodnutí [004](004-unexpressible-facts-as-warnings.md) a [010](010-diagnostics-as-returned-data.md) a je správné — ale nestačí. Vyrobí verzi, která je slepencem jednotlivě omluvených polotovarů, protože nejlevnější způsob, jak přestat lhát, je u každé mezery napsat záznam. Věta „hlásíme, že neumíme přečíst klíčovou třídu" v práci o kompozitních klíčích je špatná věta. A u několika položek je dodělat je levnější než je opsat. Zamítáme jako nedostatečné.

4. **Řezat podle toho, co je hotové, a zbytek vyjmout ze záruk vcelku.**

## Rozhodnutí

**Volíme variantu 4:**

> **Verze 1.0 je hotová, když je každá její část buď opravdu dodělaná, nebo vcelku a nahlas vyňatá ze záruk. Slepencem jednotlivě omluvených polotovarů být nesmí.**

Kritérium z varianty 3 tím nezaniká, jen se mu určuje místo. Záznam za běhu není omluva za polotovar — je to způsob, jakým se **vyslovená hranice** ozve uživateli, který dokumentaci nečte. Rozdíl je v tom, kde ta hranice leží: kolem celé oblasti, ne kolem jednotlivého prvku.

### Co je dodělané

Tam, kde je hranice menší než práce potřebná k jejímu popsání, se dodělává. Konkrétně bereme zpět tři místa, kde by se dřív hlásilo:

- **Sloupec verze** dostane vlastní kategorii v `MappingFactCategory` a s ní příznak v mezireprezentaci, `[Timestamp]` u EF Core a `<version>` u NHibernate. Slovník kategorií už existuje a má deset hodnot; přidání jedenácté je práce na půl dne a výsledkem je hotová funkce místo záznamu. U Dapperu zůstane nevyjádřitelná — ale tam je záznam tvrzením o Dapperu, ne o nás.
- **Klíčová třída u kompozitního klíče** se dodělá do podoby, kterou už popisuje rozhodnutí [006](006-flat-composite-key-rendering.md). Třetí ze tří jejích otázek — odkud vzít C# zdroj klíčové třídy — není blokovaná: `ConversionHandler` i `ConvertRequest` už dnes berou seznam zdrojů, takže vícesouborový vstup na úrovni API existuje a F14 je jen dohnání rozhraní. Je to jádro F1 a F2 a v práci o kompozitních klíčích je to špatné místo na hlášení.
- **Druhý a další `<column>` u jedné vlastnosti** je z nepřečtených prvků NHibernate mapování ta část, která se dá dodělat bez zásahu do mezireprezentace.

### Co je vyňato ze záruk

Vyjmutí se vyslovuje **jednou, na úrovni celé oblasti**, v tomto rozhodnutí a v `architecture.md` §9. Za běhu se ozve záznamem tam, kde vstup do vyňaté oblasti sahá; nikde jinde se neomlouvá.

- **Benchmarking a Advisor.** Zůstávají v repozitáři i v rozhraní a spouštět je nezakazujeme — jen na ně není spoleh. Nemají jediný test, `HarnessGenerationUtilities` stojí na tvaru generovaného textu a jednou se s ním už rozešly, a nativní knihovna se staví jen v Dockeru. Verze proto **nenárokuje F15 ani T7** a nenárokuje ani první větu S4, protože jediné místo, kde nástroj spouští cizí kód, leží právě tady (rozhodnutí [029](029-database-connection-is-the-consumer-projects-fact.md)). Sahat na tuhle oblast by znamenalo otevřít novou část místo dokončení rozdělané.
- **Dědičnost, komponenty a spojené tabulky v NHibernate mapování.** Dodělat je znamená rozšířit mezireprezentaci a pak totéž umět vyjádřit i v EF Core, tedy otevřít velkou novou část. Zůstává záznam o ztrátě u každého nepřečteného prvku.
- **Poddotazy, množinové operace a stránkování.** Celé kategorie požadavku T2, dnes hlášené záznamem. T2 stejně nelze naplnit bez javové větve.
- **Textový round-trip NHibernate → NHibernate.** Chybí HQL parser; nesymetrie je vědomá už od rozhodnutí [022](022-native-query-syntax-in-builders.md).
- **Cílový databázový dialekt.** Verze umí jediný dialekt, SQL Server, a říká to.
- **Kontejnerová konfigurace a databáze v CI.** Nasazení řešíme jednou ruční instancí, ne Dockerem. Důsledek přiznáváme: databázově závislé testy se v CI dál přeskakují, takže **kritéria F4 a F6 platí jen tam, kde běh s lokální databází skutečně proběhl** — a tahle věta patří i do textu práce.
- **Javový ekosystém a experimenty.** F7–F13 a T1–T7 verze netvrdí vůbec.

### Co verze nárokuje

**F1–F6** (komplexní identifikátory v mezireprezentaci i ve všech třech frameworcích, cizí klíče na složené klíče, čtení katalogu, slučování zdrojů, úplné mapování z neúplného vstupu), **F11** (validace a strukturovaná diagnostika), **F14** v rozsahu vícesouborového vstupu a diagnostiky po souborech, **S1** (modulární rozšiřitelnost), **S2** (determinismus), **S3** (výkon překladu), **S6** (pozorovatelnost — identifikátor běhu a záznam včetně verzí), **S7** (uživatelská přívětivost) a **druhou větu S4**, tedy že předávaný artefakt nenese přihlašovací údaje.

### Pořadí jako číslovaná značka uvnitř rozsahu

Rozhodnutí [018](018-work-order-as-item-marker.md) zamítlo očíslování položek do úplného pořadí, a to ze tří důvodů: množina položek je otevřená, položky nejsou srovnatelné, a vložení doprostřed si vynutí přečíslování. **Uvnitř rozsahu verze žádný z těch důvodů neplatí.** Množina je uzavřená — je to obsah jednoho vydání. Položky jsou srovnatelné, protože se poměřují jedním cílem. A vložení doprostřed není běžná úprava, nýbrž změna tohoto rozhodnutí.

Proto: **položka v rozsahu verze 1.0 nese na začátku kurzívového řádku vazeb značku `Verze 1.0 — N.`** Mimo rozsah zůstává rozhodnutí 018 v platnosti beze změny a položky značku nenesou.

Značky `Na řadě` a `Potom` zůstávají a znamenají totéž co dosud, jen se jejich umístění stává odvoditelným: **`Na řadě` nese položka s nejnižším dosud neodbaveným číslem, `Potom` následující dvě.** Není to druhý seznam, je to kontrola — audit ji ověří hledáním.

### Pořadí prací

Číslo je závazek pořadí, ne odhad ceny. Seznam se s odbavováním nepřepisuje: rozhodnutí zaznamenává volbu, ne stav; co zbývá, říká `open-items.md`.

| # | Položka | Proč tady |
|---|---|---|
| 1 | EF Core parser nečte navigaci bez `[ForeignKey]` | rozdělané, běžný zápis EF Core projde jako skalár |
| 2 | NHibernate builder — kolekce jen jako `<bag>` | jediná položka, která vydá neplatné mapování a vymyslí destruktivní kaskádu |
| 3 | EF Core — nullabilita se vyjadřuje jen jazykově | tichá ztráta tvrzeného faktu, a deskriptor mezitím tvrdí opak |
| 4 | Sloupec verze jako mapovací fakt | rozhodnutí i implementace; sahá do slovníku kategorií, ať je hotový před dalšími |
| 5 | NHibernate XML parser čte jen plochou třídu | týž soubor jako 4 (`<version>` a jeho zkratka `<timestamp>` se čtou tady); druhý `<column>` dodělat, zbytek záznamem |
| 6 | Převodní tabulka NHibernate vypisuje dvě neregistrovaná jména typů | vydává neplatné mapování pro platný vstup (`char(n)`, neunicode `text`) — táž třída vady, která zařadila kolekce na 2; hned po rozpracované 5 (viz Historie) |
| 7 | Klíčová třída u kompozitního klíče na straně entity | jádro F1 a F2; vícezdrojový vstup na úrovni API už existuje |
| 8 | Rozresolvování jmen entit — `property-ref` na inverzní straně | poslední zbytek rozhodnutí 001 a 012, úzké a uzavírá vlákno |
| 9 | Priorita zdrojů uvnitř vstupu se nevynucuje | implementace 017; bez ní se S2 obrátí přehozením dvou řádků |
| 10 | Dva entitní parsery jsou totéž | S1; po 1 a 9, aby se sjednocovalo už opravené |
| 11 | Cílová verze v deskriptoru a záznam běhu | jen verze, ne dialekt; ustálí tvar odpovědi před frontendem |
| 12 | Připojení ke katalogu se přes API nedá nastavit | poslední změna API před frontendem |
| 13 | Frontend zaostal za API a nevaliduje vstup | největší blok; F14, záznamy, validace, stav katalogu |
| 14 | Osud `wwwroot` | uzavře frontend build; překlad do `wwwroot` je součást publikace |
| 15 | Centrální správa verzí | mechanické, chrání tabulku verzí, o kterou se opírá S2 |
| 16 | Překladový artefakt nenese přihlašovací údaje — a nikdo to netvrdí | aserce k rozhodnutí 029 |
| 17 | Výkon překladu podle S3 se neměří | potřebuje dávkovou cestu z bodu 13 |
| 18 | Spuštění mimo Docker není ověřené ani popsané | vlastní nasazení tuhle položku ověří tím, že ji použije |
| 19 | Verze nástroje a značka vydání | poslední; S2 i S6 se na verzi nástroje odvolávají |

## Důsledky

**Rozhodnutí 018 zůstává v platnosti.** Nová značka se ho nedotýká: platí jen uvnitř uzavřeného rozsahu, kde jeho námitky neplatí, a mimo něj se nic nemění. Nevzniká druhý seznam — číslo stojí u položky, kterou popisuje, a s hotovou položkou odchází.

**`open-items.md` se rozšiřuje o tři položky, které v něm nebyly.** Aserce k rozhodnutí 029, měření S3 a verze nástroje se značkou vydání. Všechny tři jsou práce, kterou verze potřebuje, a v seznamu chyběly, protože nevznikly z auditu ani z rozhodnutí, nýbrž z otázky „co ještě chybí do vydání".

**`architecture.md` §9 dostane hranici záruk.** Dnes popisuje, co je mimo rozsah implementace; nově má říct i to, na co v rámci implementovaného není spoleh. Píše se to až s prací, ke které patří, ne dopředu.

**Advisor se nasazenou instancí nerozbije viditelně.** `libadvisor.so` na hostiteli nebude a P/Invoke selže, ale `AdvisorRunHandler` výjimku zachytává a vrací její text, takže uživatel dostane hlášku, ne pětistovku. Nic se kvůli tomu upravovat nemusí.

**Připojení ke katalogu zůstane v serverové konfiguraci a rozhraní ukáže jen stav.** Pole pro připojovací řetězec v uživatelském rozhraní by z veřejně dostupné instance udělalo čtečku cizích schémat: kdokoli by nástroj namířil na libovolnou dosažitelnou databázi a nechal si vypsat její metadata. Klíč `CatalogDatabase` v `appsettings.json` už existuje, takže F4 i F6 přes rozhraní platí, jakmile se na instanci vyplní — a je to zároveň méně práce než formulářové pole.

**Co přijde po verzi 1.0, se nezačíná od nuly.** Slovníkové předpoklady javové větve jsou hotové: jazykový typový model (rozhodnutí [014](014-language-type-model.md)), neutrální databázové typy ([019](019-neutral-database-type-vocabulary.md)) i kanonické parametry generátoru ([020](020-canonical-generator-parameter-vocabulary.md), [021](021-generator-name-selection.md)). Vyňaté oblasti se vracejí do hry v pořadí, které si určí až ta verze.

## Historie

**2026-08-20 — revidováno.** Volba — kritérium hotovosti, vyňaté oblasti i nároky verze — se nemění; do uzavřeného seznamu přibyla položka, kterou v den rozhodnutí nikdo neznal. Implementace položky 4 poprvé provedla ověření emitovaných jmen typů NHibernate proti registru `TypeFactory` verze 5.7.0, které žádá závěr rozhodnutí [019](019-neutral-database-type-vocabulary.md), a našla čtyři jména, která framework neregistruje: dvě opravila hned (`binary`, `XmlDoc`), dvě zbývají (`AnsiStringClob`; `StringFixedLength` a `AnsiStringFixedLength`) a mapování s nimi NHibernate odmítne jako neurčitelný typ. To je neplatný výstup pro platný vstup uvnitř zaručované oblasti — přesně třída vady, podle které tenhle seznam řadí a která nesmí zůstat mimo vydání. Položka je zařazená jako 6, hned po rozpracované 5; dosavadní položky 6–18 se posouvají na 7–19 a s nimi dva vnitřní odkazy tabulky (u sjednocení parserů „po 1 a 8" na „po 1 a 9", u výkonu „z bodu 12" na „z bodu 13"). Revize na místě je bezpečná: hotové položky 1–4 se nemění a pořadí zbylých není ztělesněné v žádném kódu.

**2026-08-20 — revidováno podruhé (téhož dne).** Položka 5 čte vedle `<version>` i jeho zkratku `<timestamp>`: NHibernate ji dokumentuje jako `<version type="timestamp">`, takže protějškem v mezireprezentaci je týž příznak sloupce verze a přečtení je totéž dorovnání parseru, ne rozšíření modelu — hlásit ji záznamem o ztrátě by znamenalo hlásit fakt, který nástroj nést umí, tedy přesně omluvu za polotovar, kterou tohle rozhodnutí zakazuje. Původní znění ji řadilo do „zbytek záznamem" a krátce tak byla i implementovaná; oprava mění jen závorku v řádku 5, na záznamu nic nestálo a kritérium, vyňaté oblasti ani pořadí se nemění, takže je revize na místě bezpečná.

**2026-08-22 — ukazatel, ne revize.** Volba se nemění: kritéria hotovosti, pořadí položek ani zbylých pět vyňatých oblastí se tenhle řádek nedotýká a stav zůstává `revidováno`. Vyňatou oblast *kontejnerová konfigurace a databáze v CI* ale mezitím odbavilo rozhodnutí [039](039-container-configuration-of-the-environment.md): prostředí i testovací sadu popisuje jediný `docker-compose.yml` a CI běží proti skutečnému SQL Serveru s `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, takže věta „databázově závislé testy se v CI dál přeskakují" ve výčtu výše už neplatí a s ní odpadá i podmínka u kritérií F4 a F6. Slovník stavů umí vyslovit jen změnu celého rozhodnutí, ne jedné vyňaté oblasti — `nahrazeno` by tvrdilo víc, než se stalo —, a proto tenhle řádek volbu nepřepisuje, jen ukazuje směr: **aktuální nárok verze říká [`architecture.md`](../architecture.md), §9**, ne výčet v tomhle rozhodnutí.
