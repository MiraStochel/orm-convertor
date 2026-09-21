# 094 — Totožnost entity uvnitř převodu je dvojice jmenný prostor a název

Datum: 2026-09-21
Stav: platí
Požadavky: F5, F11, F14, S1, S2
Podklad: rozhodnutí [001](001-entity-reference-by-name.md), [017](017-source-precedence-for-mapping-facts.md), [049](049-language-facts-under-source-precedence.md) a [066](066-records-attributed-to-the-input-unit.md); článek §6.1, pravidlo E1

## Kontext

Článek vyslovuje pravidlo **E1** takto: *„Each translation unit defines exactly one application-level entity mapped to exactly one database table."* Algoritmus 1 tutéž větu vynucuje krokem `SelectEntityClass(root) // enforces E1`. Dokud jednotka a entita splývají, otázka „kdy mluví dvě deklarace o téže entitě" nevzniká: odpověď je „nikdy", protože entita je právě jedna na jednotku.

Nástroj z E1 vystoupil na obě strany, pokaždé z vlastního dobrého důvodu a pokaždé samostatně:

- **jedna jednotka smí vyslovit víc entit** — sdílený C# parser bere každou deklaraci třídy v jednotce jako entitu převodu a `JavaClassReader` na javové straně stejně; vícetřídní vstup je hotová část F14 ([`architecture.md`](../architecture.md), §5);
- **víc jednotek smí vyslovit jednu entitu** — rozhodnutí [017](017-source-precedence-for-mapping-facts.md) uspořádalo stupně zdrojů a mapovací parsery si podle něj entitu **vyhledávají podle jména**, místo aby zakládaly druhou; [049](049-language-facts-under-source-precedence.md) totéž dopsalo pro jazyková fakta vlastnosti a [066](066-records-attributed-to-the-input-unit.md) z toho vyvodilo větu, kterou dnes nese §9 u F14: *„entita je legitimně vyslovená víc jednotkami"*.

Věta mezi těmi dvěma ale nikde zapsaná není: **podle čeho se pozná, že dvě deklarace mluví o téže entitě.** Článek ji nepotřebuje, protože E1 otázku nepřipouští. My ji potřebujeme pokaždé, když uživatel vloží dva soubory — a právě ten vstup F14 jmenuje jako běžný.

Dnes na ni každá strana odpovídá jinak a ani jedna odpověď nevznikla jako volba.

**C# neodpovídá vůbec.** `CSharpEntityParser` volá na každou deklaraci třídy `BeginEntity()`, takže dvě jednotky `CSharpEntity`, z nichž každá deklaruje `Customer`, vyrobí **dvě mapy téhož jména**. Ověřeno na převodu EF Core → EF Core nad dvěma takovými jednotkami: vydá dva artefakty a ani jeden záznam. Důsledky jsou tři a druhý z nich je ten vážný:

1. **dva artefakty deklarují veřejnou třídu téhož jména** v témž jmenném prostoru a do jednoho konzumentského projektu se nevejdou. Obrazovka je odliší jen zobrazovaným jménem souboru (`Customer.cs`, `Customer-2.cs`), což je zobrazovací heuristika, ne tvrzení serveru (rozhodnutí [033](033-shape-of-the-static-frontend-screens.md));
2. **model druhou mapu neumí ani pojmenovat.** Podle rozhodnutí [001](001-entity-reference-by-name.md) se entity odkazují jménem, ne referencí, a `FindEntityMap` proto vrací první mapu daného prostého jména. Cíl vztahu, nárok klíčové třídy i jméno typu dohledávané mezi entitami převodu tedy sednou na první mapu a druhá je pro model nedosažitelná. To není vada dohledávání — je to důsledek toho, že jméno **je** v mezireprezentaci identita;
3. **nevzniká žádný záznam.** Je to tedy přesně ten tiše špatný výstup, kterému má F11 předcházet.

**Java odpovídá, ale nevysloveně a nesouměrně.** `JavaEntityParser` najdi-nebo-založ má, napsané kvůli `orm.xml`, které podle rozhodnutí [068](068-source-framework-precedence-orders-the-reading.md) stojí před třídou: hledá shodu na dvojici (balík, jméno), a nenajde-li ji, přijme mapu téhož jména **s prázdným balíkem**. Dvě jednotky téhož balíku se proto slučují a dvě jednotky různých balíků zůstávají dvěma entitami — což je chování, které tímhle rozhodnutím volíme. Zapsané ale není, takže se kdykoli změní úpravou jednoho parseru; je nesouměrné (třída bez balíku mapu s balíkem nenajde, opačně ano, takže na pořadí jednotek záleží, a to S2 zakazuje); a o zbylé společné prosté jméno se nestará — dvě entity `Customer` ve dvou balících projdou beze slova, ačkoli model dál odkazuje prostým jménem.

Vstupní tvar, o který tu jde, není vykonstruovaný. **`partial` třída rozdělená do dvou souborů je běžný .NET idiom** a model `partial` nenese — z hlavičky se čte jen přístupový modifikátor —, takže dvě poloviny jedné třídy jsou dnes dvě entity. Totéž udělá vložená entita se svým rozšířením vedle sebe, nebo tentýž soubor vložený omylem dvakrát.

## Zvažované varianty

1. **Nechat čtení být a řešit až kolizi výstupních artefaktů.** Ta otázka existuje — pojmenování artefaktů je otevřená položka rozhraní —, jenže odpovídá na něco jiného a odpovídá pozdě: v okamžiku emise už jsou vztahy i nároky klíčových tříd navázané na první mapu, protože se rozhodovaly jménem (001). Kolize jmen souborů je následek, ne příčina. Zamítáme.

2. **Odmítnout druhou deklaraci téhož jména záznamem `Failure`.** Deterministické a hlasité, ale je to tvrzení, že vstup je chybný — a on chybný není: dvě jednotky vyslovující jednu entitu jsou přesně to, co 017 a 066 prohlásily za legitimní. Proti rozhodnutí [010](010-diagnostics-as-returned-data.md). Zamítáme.

3. **Slučovat podle prostého jména a jmenný prostor ignorovat.** Sedělo by to přesně na `FindEntityMap`, tedy na to, jak se model ptá. Cena je ale nepřijatelná: `Shop.Customer` a `Billing.Customer` jsou v C# i v Javě dva různé typy a sloučením by vznikla entita, která nebyla v žádném vstupu — sjednocení vlastností dvou tříd a záznam o rozporu u každého faktu, ve kterém se liší. Vydat artefakt, jehož předloha nikde neexistuje, je horší než vydat dva artefakty, které do sebe nezapadnou. Zamítáme.

4. **Povýšit kvalifikované jméno na identitu modelu**, tedy změnit rozhodnutí 001: cíl vztahu, nárok klíčové třídy, jméno spojovací entity i dohledání neznámého typu by nesly jmenný prostor. Nejednoznačnost by tím nezmizela jen ohlášená, ale doopravdy. Je to ale zásah do všech šesti wrapperů a do každého builderu, žádný požadavek ho nežádá a jediný vstup, který by z něj těžil, je ten, o kterém tu mluvíme. Zamítáme dnes; dveře zůstávají otevřené a tohle je místo, kde je čtenář najde.

5. **Totožnost při čtení jako dvojice, slučování pravidlem 017, zbylé společné prosté jméno jako záznam.**

## Rozhodnutí

Volíme variantu 5.

**Totožnost entity uvnitř převodu je dvojice (jmenný prostor, název).** Entitní parser — ten, který čte text třídy, tedy sdílené čtení C# i javové — přestává zakládat mapu na každou deklaraci a stává se najdi-nebo-založ, stejně jako je jím `AddProperty` od rozhodnutí 049 a `GetOrCreatePropertyMap` odjakživa. Dvě deklarace téže dvojice jsou tatáž entita; dvě různé dvojice jsou dvě entity, i když se jejich prostá jména shodují.

**Jmenný prostor, který nikdo nevyslovil, nerozlišuje.** Hledá se nejdřív přesná shoda dvojice a teprve pak shoda jména tam, kde jednu ze stran nikdo neuvedl. Důvod je týž, jaký 017 vyslovilo o mapovacích artefaktech: **mlčení není tvrzení.** `orm.xml` bez `<package>` o balíku neříká nic a třída, která ho uvede, ho doplňuje — to je dnešní chování javové strany a zůstává. Nové je, že pravidlo platí souměrně: mlčí-li naopak třída, najde entitu, kterou založil někdo, kdo jmenný prostor uvedl. Bez souměrnosti by na pořadí jednotek záviselo, jestli vzniknou jedna entita, nebo dvě, a výsledek závislý na pořadí vstupů zakazuje S2 i výslovná věta rozhodnutí 017. Z téhož důvodu **deklarace, která řekne, kde entita je, ji tam umístí i pro další hledání**: co se předtím našlo volně, protože to nikdo neurčil, je od té chvíle určené, takže třetí deklarace odjinud je jiná třída, a ne třetí volná shoda.

**U vnořené třídy patří do totožnosti i obalující třída — a jedině do ní.** Mezireprezentace staví vnořenou třídu *vedle* obalující, ne dovnitř ([`architecture.md`](../architecture.md), §5), takže obalující třída není součástí jména entity a model ji nikde nenese. Jenže `Customer.Key` a `Order.Key` jsou dva typy, a kdyby je čtení sloučilo do jedné entity, dostala by jedna z nich klíčové části té druhé — což je přesně ten druh vady, kvůli kterému tohle rozhodnutí vzniká. Obalující třída proto vstupuje do klíče, podle kterého se entita **hledá**, a je to fakt builderu, ne pole modelu: vydaný artefakt se nemění a mapovací artefakt, který o vnořování nic neví, si entitu dál hledá jmenným prostorem.

**Slučuje se po jednotlivých faktech pravidlem 017, ne přepisem.** Nepotřebujeme k tomu nic nového: zapisovací cesty builderu — `AddTable`, `AddSchema`, `AddPrimaryKey`, `AddProperty`, `SetPropertyDatabaseMapping`, `SetPropertyDatabaseType`, `MarkTransient`, `AddUniqueConstraint` — už dnes plní jen prázdný fakt a rozdíl hlásí `Conflict`em. Rozhodnutí k nim doplňuje dvě chybějící:

- **`AddNamespace`** plní jen prázdný fakt a jiný jmenný prostor hlásí `Conflict`em, místo aby ho tiše přepsal;
- **`AddRelation`** je najdi-nebo-přidej. Navigační vlastnost nese právě jeden vztah, takže druhá deklarace téže vlastnosti vztah nezdvojuje: týž cíl a táž kardinalita jsou jedna věta pronesená dvakrát a nejsou událost, jiný cíl nebo jiná kardinalita jsou `Conflict` s platným prvním vztahem. Bez toho by sloučení vyrobilo novou vadu — dvě stejné relace by vydaly dvě anotace cizího klíče a syntéza spojovací entity by u N:M proběhla dvakrát.

**Přístupový modifikátor třídy je jediný fakt hlavičky, který se dál přepisuje, a je to bezpečné.** Dvě deklarace téže dvojice jsou v C# i v Javě dvě deklarace jednoho typu a jazyk sám žádá, aby se v přístupnosti shodly — části `partial` třídy různou přístupnost deklarovat nesmějí. Vstup, ve kterém se liší, tedy není platný program zdrojového jazyka; mezireprezentace nevaliduje (invarianty) a první výjimku z toho tady nezavádíme.

**Dvě entity, kterým zbylo společné prosté jméno, jsou záznam `Conflict` — jeden na jméno.** Nese ho převod, ne jedna jednotka: záznam o sloučené entitě žádné jedné jednotce nepatří (rozhodnutí 066). Jmenuje, kde je která deklarovaná — jmenným prostorem, u vnořené třídy i obalující třídou —, a říká, co z toho plyne: model odkazuje entitu jménem (001), takže odkaz na to jméno (cíl vztahu, nárok klíčové třídy, jméno typu) sedne na první přečtenou entitu a artefakt vydaný pro každou z nich nese třídu téhož jména. Píše se, i když na to jméno nic neodkazuje: nejednoznačnost je vlastnost převodu, ne jednoho odkazu. Proč `Conflict`: dva zdroje prvního stupně nárokují jedno jméno a nástroj ten spor rozhodnout neumí, jen ho nezamlčí, což je podle rozhodnutí 010 a [015](015-mapping-fact-completion-from-the-catalog.md) definice toho druhu záznamu. `Incompleteness` to není — chybějící fakt smí doplnit katalog a tenhle katalog doplnit neumí.

**Vysloví se nad seznamem, jak ho zanechalo čtení.** Záznam vzniká na začátku první fáze, která seznam entit po čtení otevírá — v rozpouštění klíčových tříd, ať ho volá fáze doplnění, nebo `Build` u převodu, který katalog nepotkal —, a jen jednou. Později už by nebylo o čem mluvit: klíčová třída ze seznamu odchází, jakmile se rozpustí do klíče (rozhodnutí [031](031-key-class-as-declaration-of-key-parts.md)), a **právě dvě klíčové třídy téhož jména jsou ten pár, o kterém nástroj mlčel nejtíž** — nárok obou entit dohledává třídu prostým jménem, takže druhá entita dostávala klíč té první a nikdo to neřekl.

**Pravidlo bydlí v `AbstractEntityBuilder`, ne ve wrapperech.** Je to tvrzení o modelu — o tom, co je entita —, ne o zdrojovém frameworku, a builder je jediné místo, které seznam map vlastní. Wrapperů se to proto netýká (S1): parser řekne, jakou třídu v jakém jmenném prostoru čte, a mapu dostane.

**Mapovací parsery se nemění.** `NHibernateXMLMappingParser`, `JpaOrmXmlParser` i `MyBatisMappingParser` si entitu dál hledají vlastním postupem, jehož poslední krok je shoda samotného jména; pro ně je jmenný prostor mlčení a jejich pořadí vůči třídě stanovilo 017, respektive 068. Tohle rozhodnutí mluví o čtení textu třídy, tedy o tom vstupu, který jmenný prostor tvrdí.

## Důsledky

**Tentýž vstup vydá jiný výstup, a to je jádro téhle změny.** Dvě jednotky deklarující jednu třídu vydají místo dvou artefaktů jeden, se sjednocenými vlastnostmi a s fakty podle priority zdrojů. Konzument, který dosud dostával dva soubory, které se do jednoho projektu nevejdou, dostane jeden. Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je změna tvaru výstupu PATCH a nový druh diagnostického záznamu MINOR, takže **celkem MINOR**.

**Javová strana se mění ve dvou bodech**, přestože rozhodnutí do značné míry zapisuje to, co už dělá: hledání je nově souměrné, takže třída bez balíku najde entitu s balíkem stejně, jako to dosud fungovalo jen opačným směrem, a o dvě entity téhož prostého jména ve dvou balících se nově hlásí záznam. Trojkrok mapovacích parserů zůstává beze změny.

**Sloučení odhalilo jednu mezeru v porovnávání jazykových faktů a zavírá se tady.** Podle 049 je neprázdný fakt tvrzený jinak `Conflict`, jenže navigační vlastnost má v modelu dvě podoby téhož tvrzení: jméno tak, jak ho zdroj napsal (`Unknown`), a odkaz na entitu, na který ho první čtení povýšilo, jakmile mapování navigaci nárokovalo (rozhodnutí [014](014-language-type-model.md)). Druhá deklaruje tutéž vlastnost znovu v té nepovýšené podobě, takže by bez úpravy vznikl `Conflict` hlásící vstup proti němu samému. Porovnání proto zná obě podoby jednoho jména a nechává platit tu povýšenou; jiná neshoda typu je `Conflict` dál.

**Jeden druh ticha mizí i tam, kde s vkládáním dvou souborů nesouvisí.** Dvě klíčové třídy téhož jména pod dvěma entitami nástroj neuměl rozlišit už předtím — nárok obou entit je dohledává prostým jménem —, a druhá entita tak přišla ke klíči té první beze slova. Nové je, že se to ohlásí. Rozhodnout tu nejednoznačnost umí až kvalifikované jméno v modelu, tedy zamítnutá varianta 4.

**Zúžení F14 v §9 se nemění, jen dostává druhou polovinu.** Věta „entita je legitimně vyslovená víc jednotkami" dosud platila jen tam, kde entitu založil entitní parser a dohledal ji mapovací; nově platí i pro dvě jednotky téhož jazyka. Záznamy fáze doplnění a generování jednotku dál nenesou, a záznam o společném prostém jménu je jedním z nich.

**Čeho se rozhodnutí nedotýká:** fáze doplnění z katalogu (pracuje nad mapami, ať jich je kolik chce), dotazové větve, ani obrazovky — ta si zobrazovaná jména artefaktů odlišuje sama a nově jich bude míň. Artefakty výstupu dál nenesou jméno jednotky, ze které vzešly; párování vstupu s výstupem zůstává otevřenou položkou rozhraní. A `FindEntityMap` dál vrací první mapu daného jména — po tomhle rozhodnutí je to nejednoznačné jedině tam, kde to záznam vyslovil.
