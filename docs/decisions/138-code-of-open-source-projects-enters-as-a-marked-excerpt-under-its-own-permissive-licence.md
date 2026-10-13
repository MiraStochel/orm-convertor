# 138 — Kód cizích open-source projektů vstupuje do repozitáře jako výňatek pod vlastní permisivní licencí, se změnou vyznačenou v každém souboru, a ukazuje se jako příklad

Datum: 2026-10-13
Stav: platí
Požadavky: T1, F11, F14, S2, S5
Podklad: katalog zdrojů [`analysis/real-world-sources.md`](../analysis/real-world-sources.md); rozhodnutí [016](016-generated-artifact-verification-levels.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [099](099-examples-are-content-not-a-choice.md), [133](133-an-add-on-package-over-a-framework-is-not-that-framework.md); článek *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection*, oddíl 10; MIT License a Apache License 2.0, §4

## Kontext

**Každý vstup, nad kterým nástroj dosud běžel, jsme napsali my.** Vzorky šesti frameworků, vstupy matic napříč frameworky, kategorie dotazů, domény příkladů i texty katalogu LDBC — ty sice podle cizí specifikace, ale v podobě, kterou jsme volili. Článek hodnotí stejně: šest ručně psaných dotazů nad WideWorldImporters, které škáluje syntaktickou replikací, a rozšíření o složitější konstrukce nechává na později. Zadání to vidí jako mezeru: T1 žádá případovou studii nad open-source aplikací s netriviálním přístupem k datům — nejméně 15 entit, vztahy, složený klíč, 20 čtecích dotazů —, a otevřená položka *Referenční databáze experimentů a případová studie T1* nemá aplikaci, na kterou by ukázala.

**Kód, který nástroj nikdo nepsal pro nástroj, ho zkouší jinak.** Rešerše veřejných projektů ([`analysis/real-world-sources.md`](../analysis/real-world-sources.md)) a zkušební převod pěti z nich to ukázaly hned: našly šest vad, o kterých žádný test nevěděl — mezi nimi dotaz, který vrátí jiné řádky bez jediného záznamu, a složený klíč, který se tiše zúží. Žádná z nich není exotická; jsou to tvary, jimiž běžné projekty své dotazy a klíče píšou. Takový vstup proto patří do repozitáře natrvalo, ne do jednorázové zkoušky: vadu, kterou našel, má držet test, a čtenář výkladové stránky má vidět, co nástroj s kódem udělá, když ho nepsal autor nástroje.

**Převzít cizí kód znamená vyřešit tři věci, které vlastní vzorky nemají.** Za prvé *licenci*: repozitář je pod MIT a sekce *License* kořenového README slibuje, že tak smí kdokoli naložit s celým jeho obsahem, s výjimkou vyjmenovaných cizích souborů. Cizí kód nese vlastní podmínky — MIT žádá text licence a copyright u každé kopie, Apache License 2.0 navíc kopii licence, převzatý soubor NOTICE a „výrazné oznámení" v každém změněném souboru (§4) —, a copyleftové licence (LGPL, GPL, AGPL) by svou část repozitáře podřídily vlastním podmínkám, AGPL i síťové službě, která ji vydává. Za druhé *změnu*: projekty, které jsme zkoušeli, nástroj jako celek nepřečte — Keycloak a Ambari drží dotazy v `@NamedQuery`, které nástroj nečte; Chinook i JPetStore zapisují; Keycloak píše složený klíč tvarem, s nímž nástroj tiše zúží klíč. Výňatek se bez úprav neobejde, a úprava musí být vidět. Za třetí *místo*: kde výňatek leží, jak se dostane k testům a kde ho uvidí čtenář.

## Zvažované varianty

**Kde cizí kód leží.** Čtyři cesty.

1. **Jen odkazem a stahovat ho při testu** (klon v CI, stažení souboru při sestavení). Repozitář by nenesl nic cizího a licence by nebyla otázkou. Zamítáme: test by závisel na síti a na tom, že projekt na GitHubu zůstane, kde je, a kód takový, jaký byl (S5 — reprodukce jedním příkazem); úpravy by se musely aplikovat při každém stažení jako záplata, která se při první změně projektu rozpadne; a výkladová stránka instance, která běží bez sítě, by neměla co ukázat.
2. **Git submodul celého projektu.** Zamítáme: přináší statisíce řádků, z nichž příklad potřebuje desítky souborů, nedovolí úpravu bez vlastní větve cizího projektu a licence submodulu se k sestavení nedostane.
3. **Přepsat výňatek do řetězcových konstant C# jako vlastní vzorky v `SampleData`.** Testy i stránka by ho četly jako dosud. Zamítáme: soubor by přestal být souborem projektu — hlavička licence, cesta a jméno by žily v komentáři konstanty, ne v textu, který jednotka předá —, javové textové bloky (`"""`) by se v raw řetězcích C# musely přeescapovat a každá další úprava by se míchala s naší úpravou formy.
4. **Výňatek jako soubory projektu ve vlastní složce `SampleData/ThirdParty/<projekt>/`, vedle nich licence a README se seznamem změn, vše vložené do sestavení `SampleData` jako prostředky a vyjmuté z překladu.** Tuhle volíme.

**Které licence.** (a) Libovolná licence, která dovoluje redistribuci — copyleft by se pak vztahoval na svou složku. (b) Jen permisivní licence: MIT, Apache License 2.0, BSD. Volíme (b). Slib sekce *License* zůstává pravdivý, jak je napsaný — vše mimo vyjmenované složky je MIT a vyjmenované složky žádají jen to, co MIT a Apache žádají vždy —, a nikdo, kdo nástroj převezme, nemusí zkoumat, zda výkladová stránka nevydává síťovou službou kód pod AGPL. Projekt pod copyleftem (bitwarden/server, testovací sada NHibernatu, SonarQube, Jellyfin) zůstává v katalogu zdrojů s odkazem a popisem úprav, které by potřeboval; nepřebírá se.

**Jak se vyznačí změna.** (a) Jen v README složky. (b) Jen v souborech. (c) V obou: každý soubor otevře — za vlastní hlavičkou licence projektu, má-li ji — poznámka `ORMConvertor:` s projektem, commitem, cestou a buď větou *Unchanged apart from this note*, nebo seznamem změn; soubor, který projekt nemá, to řekne sám o sobě; README složky vypíše všechny změny a důvody. Volíme (c): Apache §4 žádá oznámení *v souboru*, jednotka převodu je soubor (rozh. [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md)), takže poznámka cestuje s textem do editoru překladové obrazovky i do staženého archivu, a README dává přehled, který z jednotlivých souborů neposkládá nikdo.

**Kde se výňatek ukáže.** (a) Jen v testech. (b) Na samostatné stránce. (c) Jako příklady výkladové stránky ve vlastní skupině. Volíme (c): příklady jsou obsah stránky (rozh. 099), stránka už umí převod s mnoha jednotkami, sbalené panely a záznamy, a skupina *Real projects* leží tam, kde čtenář vidí ostatní příklady, a na překladové obrazovce v seznamu, odkud se načte do editoru a přepne na jiný cíl.

## Rozhodnutí

> **Kód cizího open-source projektu vstupuje do repozitáře jako výňatek v `ORMConvertor/SampleData/ThirdParty/<projekt>/`: soubory projektu z jednoho commitu, vedle nich jeho licence (a NOTICE, má-li ji) a README se zdrojem, commitem a seznamem změn; vše vložené do sestavení `SampleData` a vyjmuté z překladu. Převezme se jen projekt pod MIT, Apache License 2.0 nebo BSD. Každý soubor otevírá poznámka `ORMConvertor:`, která jmenuje projekt, commit a cestu a řekne, zda a jak se soubor změnil. Změna smí jen tři věci: vypustit, co nástroj nepřekládá (zápis a kód, který slouží jen jemu), přepsat konstrukci, kterou nástroj čte jen v jiném tvaru, do tvaru, který vrací tytéž řádky, a opravit chybu projektu, kvůli níž kód neběží. Výňatky se ukazují jako příklady výkladové stránky ve skupině *Real projects*.**

**Co se smí změnit a co ne.** Změna drží význam: tytéž tabulky, tytéž klíče, tytéž řádky. Kde přepis význam v okrajovém případě mění, poznámka u něj to řekne (dva dotazy Keycloaku nad atributy uživatele vrátí uživatele se dvěma shodnými atributy jednou, ne dvakrát). Změna, která obchází **vadu** nástroje — tvar, který nástroj přečte, ale přeloží jinak, než znamená —, má vždy položku v [`open-items.md`](../open-items.md) a jméno v katalogu zdrojů; přepis ji nesmí schovat. Změna, která obchází **vyslovenou mez** ([`subset.md`](../subset.md), část 2), odkazuje na řádek, který mez vyslovuje. Chyby projektu, kvůli nimž kód neběží, se opravují; ostatní zůstávají, jak jsou, a README je vyjmenuje.

**Co hlídá test.** `Combined/ExampleCatalogTest` platí pro výňatky jako pro každý příklad: ve směru, který stránka ukazuje, se přeloží bez `Failure` a dají entitní i dotazové artefakty. `Combined/ThirdPartySourcesTest` drží tohle rozhodnutí: každý projekt nese README a licenci, která je MIT, Apache nebo BSD, a každý kódový soubor otevírá poznámka o původu, která říká, zda se změnil.

**Obsah v den rozhodnutí** — popis stavu, ne součást volby; platnou podobu nese [`architecture.md`](../architecture.md), §6.3, a katalog zdrojů. Keycloak (Hibernate → NHibernate; 21 entit, deset složených klíčů, 47 pojmenovaných čtecích dotazů), Apache Ambari (EclipseLink → EF Core; šest entit upozornění, 35 dotazů), Chinook Web API dvakrát — vrstva Dapper → EF Core a vrstva EF Core → Hibernate nad týmiž entitami — a MyBatis JPetStore 6 (MyBatis → Dapper). NHibernate jako zdroj chybí: jediný bohatý zdroj `hbm.xml` s dotazy, testovací sada NHibernatu, je pod LGPL.

## Důsledky

**T1 má poprvé aplikaci, která splní čísla.** Výňatek Keycloaku je open-source aplikace s 21 entitami, vztahy, deseti složenými klíči a 47 čtecími dotazy. Které čtení T1 platí a nad čím experimenty běží, dál rozhoduje položka *Referenční databáze experimentů a případová studie T1*; výňatek je kandidát, ne rozhodnutí. [`traceability.md`](../traceability.md) to u T1 říká, stav požadavku zůstává `vyňaté`.

**Nalezené vady jsou položky.** Šest vad, které výňatky ukázaly, vede [`open-items.md`](../open-items.md) jako práci a [`subset.md`](../subset.md) 2.13 jako tichá místa; přepisy ve výňatcích na ně ukazují. Kdo vadu opraví, smí přepis ve výňatku vrátit k původnímu textu projektu — příklad pak ukazuje víc původního kódu.

**Kořenové README.** Sekce *License* jmenuje vedle `wwwroot/vendor/` i `SampleData/ThirdParty/`; obě nesou cizí podmínky ve vlastních souborech.

**Další projekt ani nový commit rozhodnutím nejsou.** Přidat, vyměnit či obnovit výňatek je práce, dokud drží pravidla výš — tak jako příklady podle rozh. 099. Rozhodnutím je změna pravidel: jiná licence, jiný druh změny, jiné místo.

**Cena.** Repozitář nese asi 0,6 MB cizího textu v 94 souborech, sestavení `SampleData` totéž jako prostředky. Výkladová stránka spouští při načtení o pět převodů víc; největší, Keycloak do NHibernatu (182 artefaktů), vrátí zahřátá instance přes HTTP za 0,05 s, první převod po startu za 4 s (měřeno 2026-10-13 na MIS3 bez katalogu).

**Co tohle rozhodnutí nerozhoduje:** schémata převzatých projektů pro katalog. Výňatky běží bez katalogu jako ostatní příklady (rozh. 099); kde projekt své schéma zveřejňuje a zda se načte do databáze katalogu, říká katalog zdrojů a otevřená položka *Schémata převzatých projektů v katalogu instance*.
