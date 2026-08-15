# 015 — Doplňování chybějících mapovacích faktů z databáze

Datum: 2026-08-15
Stav: platí
Požadavky: F2, F4, F5, F6, F11, S1, S3
Podklad: JSS článek, §7.1 a pravidlo E9; audit 2026-08-15, nálezy 1.1 a 1.2

## Kontext

Toto rozhodnutí nahrazuje [008](008-database-as-metadata-source.md) a přebírá jeho volbu. Vzniká proto, že 008 přestalo být pravdivé ve dvou bodech: rozhodnutí [010](010-diagnostics-as-returned-data.md) změnilo, kde žije původ doplněného faktu, a zrušilo odečítání už známých faktů z poptávky do katalogu. Obě změny 010 samo pojmenovalo jako změnu volby, ale 008 zůstalo ve stavu `revidováno` a jeho text dál tvrdil opak. Slovník stavů částečné nahrazení nezná — rozhodnutí buď platí, nebo je nahrazené — takže platnou verzi nese tento dokument a 008 zůstává v repozitáři jako historie. Zbytek 008 se nemění; kdo hledá původní znění a jeho argumentaci, najde je tam a v git historii.

Mezireprezentace rozlišuje „fakt chybí" od „fakt je" — `ColumnName`, `Type`, `Length`, `IsNullable` i `PrimaryKey` jsou nepovinné a prázdná hodnota znamená, že ji zdrojový framework nevyjádřil. Co se s takovou mezerou stane dál, ale dnes nikdo neřídí. Každý builder si ji řeší po svém a vždy potichu.

Bezprostředním podnětem je entita bez primárního klíče. Dapper parser klíč nevytvoří nikdy, protože Dapper nemá čím ho zapsat. Oba buildery na `PrimaryKey == null` reagují prostým `return`: EF Core vygeneruje třídu bez `[Key]`, NHibernate `<class>` bez `<id>`. Ani jeden framework takový výstup nepřijme. Je to přesně ta třída chyby, kterou popsalo rozhodnutí [006](006-flat-composite-key-rendering.md): výstup je syntakticky správný a přitom nespustitelný.

Klíč je jen nejviditelnější případ. `NHibernateEntityBuilder.ResolveNhType` hádá databázový typ z typu CLR, `ColumnName ?? Property.Name` dosazuje název vlastnosti místo názvu sloupce, a u klíčového sloupce se délka, přesnost a měřítko negenerují vůbec. Ve všech případech vzniká hodnota, kterou nikdo netvrdil, a od hodnoty přenesené ze zdroje ji ve výstupu nelze odlišit.

**Mezera v mezireprezentaci ale nemusí být mezerou ve zdroji.** `EFCoreEntityParser` čte klíč jen z atributu `[Key]` a z třídního `[PrimaryKey]`. EF Core přitom klíč odvozuje i konvencí: vlastnost pojmenovaná `Id` nebo `<název typu>Id` se primárním klíčem stane sama. Entita, která v EF Core klíč prokazatelně má, tedy do mezireprezentace dorazí bez klíče — a nelze ji odlišit od entity, která klíč opravdu nemá. Není to okrajový případ: mapování odvozené z konvence je běžný způsob, jak se v EF Core píše, a u Dapperu a MyBatisu je to jediný způsob, jak se mapování vyjadřuje vůbec.

**Kde se dnes o cíli ví.** `ConversionHandler.Convert` dostává zdrojový i cílový framework spolu se všemi vstupy, tedy dřív, než se cokoli naparsuje. Instanci cílového builderu vytváří jako první krok a parsery plní mezireprezentaci přímo do ní. Znalost cíle je k dispozici od začátku převodu.

**Precedens v kódu.** Čtení katalogu v repozitáři už je: `AdvisorBenchmarking.HarnessGenerationUtilities.QualifyEntityTableNames` se ptá `INFORMATION_SCHEMA` na schéma, kterým kvalifikuje název tabulky. Výsledek ale nikam do mezireprezentace neuloží, pošle samostatný dotaz na každou entitu a selhání spojení odchytí prázdným `catch`. Tentýž problém se přitom v repozitáři řeší dvakrát a jinak: chybějící schéma doplňuje `EFCoreLinqQueryParser.ResolveQualifiedTableName` heuristikou nad `EntityMaps`, zatímco `HarnessGenerationUtilities.ResolveQualifiedTableName` dotazem do katalogu. Dvě odpovědi na tutéž otázku ve dvou vrstvách, které o sobě nevědí.

Je proto třeba rozhodnout pět věcí: kdo se databáze ptá, kdy, s jakou prioritou vůči ostatním zdrojům, co se počítá za tvrzení zdroje, a co se stane, když odpověď nepřijde.

## Zvažované varianty

Rozbor variant se proti 008 nemění; nová je jen granularita čtení uvnitř varianty E, kterou popisuje sekce Rozhodnutí.

### A — Konvence v builderu

Dnešní stav: chybějící fakt si každý builder nahradí odhadem ve chvíli generování. Nejlevnější varianta, nevyžaduje připojení k databázi. Platí za to tím, že odhad se nikde nezaznamená: dva cílové frameworky dostanou z téže mezireprezentace různá data, protože každý hádá jinak, a F11 nemá co ohlásit, protože v okamžiku generování už není poznat, co bylo tvrzení a co domněnka.

### B — Dotaz z parseru

Parser by při čtení zdrojového artefaktu rovnou doplňoval, co v něm není. Mezireprezentace by byla úplná hned po parsování. Logika doplňování by ale žila v každém parseru zvlášť a s každým dalším frameworkem se opakovala — proti smyslu S1. Navíc parser podle článku (§4.3) analyzuje artefakty konkrétního frameworku; databázový katalog žádný takový artefakt není.

### C — Úplné doplnění mezireprezentace před generováním

Samostatný krok nad hotovou mezireprezentací doplní z katalogu vše, co v ní chybí, bez ohledu na to, kdo si o to řekl. Drží čtení na jednom místě a mezireprezentace je po něm úplná pro libovolný cíl. Cenou je, že rozsah **zápisu** neurčuje nikdo: do modelu se dostanou i fakta, která cílový framework nevyjádří. Při převodu do Dapperu, který nemá čím zapsat ani klíč, ani typ sloupce, je celý krok zbytečný.

### D — Dotaz z builderu v místě spotřeby

Builder se ptá katalogu ve chvíli, kdy při generování narazí na chybějící fakt. Rozsah dotazů je minimální ze všech variant. Cena je vysoká ve čtyřech ohledech. Dotazy vznikají po jednom během generování, takže z jedné dávky je N drobných dotazů — přesně vzorec, jehož výskyt v aplikacích postavených na ORM měříme jako problém. Logika čtení se opakuje v každém builderu, což je proti S1. Protože se mezera odhalí až uprostřed psaní výstupu, nemá F11 co validovat předem. A protože se dotazy prolínají s generováním, nelze splnit S3, který žádá měřit načtení metadat odděleně od času překladu.

### E — Poptávku formuluje cíl, čtení obstará jedna komponenta

Cílová strana převodu deklaruje, které kategorie mapovacích faktů vyžaduje a které umí vyjádřit. Z deklarace vznikne jediná dávková poptávka, ta se položí katalogu před generováním a jejím výsledkem se doplní mezireprezentace. Odděluje se tím mechanismus od řízení: čte jedno místo, ale samo neurčuje, co se použije.

### F — Poptávku formuluje celé zadání převodu

Rozsah neurčuje jen cílový framework, ale konkrétní úloha: které entity a dotazy se skutečně převádějí, jakým směrem, a zda jde o jeden cíl, nebo o sadu kandidátů posuzovaných Advisorem. Poptávka je nejužší možná. Zadání ale plně neurčuje potřebu předem: co se bude vypisovat, se ví až během generování, takže varianta vyžaduje buď suchý průchod, který požadavky posbírá, nebo konzervativní nadhad, který ji zpět přibližuje variantě E.

## Rozhodnutí

**Varianta E: katalog čte jediná komponenta, ale co se z přečteného použije, určuje cílová strana převodu — jednou dávkou před generováním.**

Klíčové je, že „jedno místo pro čtení" a „ptát se jen na to, co má odběratele" nejsou protiklady. Jsou to odpovědi na dvě různé otázky a rozhodnutí je odděluje:

- **Mechanismus** — připojení, dialekt, konkrétní dotazy do katalogu, dávkování a případné cachování — žije na jednom místě, v komponentě nezávislé na frameworku. Nový parser ani nový builder ho neimplementuje znovu (S1) a při rozšíření na jinou databázi se mění jedno místo. Dvojí řešení kvalifikace názvu tabulky popsané v kontextu je doklad, že bez tohoto pravidla vzniká druhá odpověď sama od sebe.
- **Řízení** — co se z odpovědi zapíše do mezireprezentace — formuluje strana, která fakta spotřebuje. Poptávka je datová struktura, ne volání: cíl řekne, co potřebuje, komponenta to obslouží jednou dávkou.

**Poptávka řídí, co se použije, ne co se načte.** Tady se rozhodnutí liší od 008, které poptávku stavělo jako „vyžaduji ∪ umím vyjádřit, mínus to, co v mezireprezentaci už je". Ta formulace znemožňuje splnit F5: fakt dodaný zdrojem v mezireprezentaci nechybí, takže by se na něj katalogu nikdo nezeptal a rozpor mezi zdrojem a schématem by nemohl vzniknout nikdy. Dotaz do katalogu přitom vrací sloupce tabulky vcelku a omezovat ho na podmnožinu sloupců nic neušetří. Načítá se proto **celý sloupcový obraz dotčených tabulek**; poptávka rozhoduje o tom, které fakty se z něj do mezireprezentace zapíšou, a hodnoty, které zdroj už nese, se s katalogem porovnají.

Není to návrat k variantě C. C nemá řízení vůbec: zapíše do modelu všechno, co katalog vrátí. Zde se řízení zachovává, jen se přesouvá z okamžiku dotazu do okamžiku zápisu — dotčené jsou pouze tabulky entit, o které cíl stojí, a do modelu se zapíše jen to, co cíl umí vyjádřit. Prázdná poptávka tedy dál znamená nulový dotaz.

**Iniciativa na straně cíle je čtením článku, ne odklonem od něj.** Popis parsování entit v §7.1 říká výslovně, že když zdroj mapovací informaci nemá, parser zaznamená jen to, co má, a doplnění z konvencí i z metadat — s odkazem na pravidlo E9 — *delegates convention- and metadata-based completion to the builder*. Odpovědnost za doplnění tedy článek přiřazuje builderu; varianty B a C jdou proti němu. Pravidlo E9 samo je permisivní a jeho jedinou tvrdou podmínkou je nepřepsat explicitní mapování; o okamžiku ani o rozsahu neříká nic, a právě ten prostor toto rozhodnutí zaplňuje.

**Proti doptávání během generování mluví S3.** Požadavek žádá, aby se načtení metadat měřilo a reportovalo odděleně od času překladu. Odděleně měřit lze jen ohraničenou fázi; dotazy rozeseté mezi vypisováním vlastností by šlo nanejvýš sečíst. Táž logika plyne z §9.2 článku: prototyp slučuje informace z více parserů do jedné instance mezireprezentace *před generováním*.

**Napětí, které rozhodnutí nese vědomě.** Obrázek 3 vede šipku z databázových metadat do frameworkově agnostické reprezentace, tedy metadata plní mezireprezentaci, ne builder, a §4.3 uvádí, že se frameworkově závislá omezení vynucují výhradně při generování. Čteno doslovně by z toho nejlépe vyšla varianta D. Ve skutečnosti si tato místa neodporují: §7.1 říká, **kdo doplnění iniciuje**, obrázek 3, **kam výsledek patří**. Varianta E drží obojí.

**Rozsah řízený cílem není v rozporu s F4.** Požadavek žádá umět načíst „alespoň" tabulky, sloupce, datové typy, nullabilitu, primární a cizí klíče, unikátní omezení, délku, přesnost a měřítko, a ověřuje se podílem správně získaných metadat proti testovací databázi. To je test schopnosti čtečky, ne požadavek, aby se při každém převodu do modelu zapsalo všechno.

**Rozsah řízený cílem nedělá mezireprezentaci frameworkově závislou.** Cíl neurčuje, co mezireprezentace umí nést, ani jaký tvar doplněný fakt má — určuje jen, co se vyplatí zapsat. Název sloupce dohledaný kvůli NHibernate je týž název sloupce, jaký by si vyžádal EF Core. Mezireprezentace zůstává neutrální v obsahu a řízená v úplnosti. Že výsledek patří do ní, a ne jen do generovaného výstupu, žádá F5 přímo: slučovat se má do jediné mezireprezentace.

**Dapper vystupuje v obou rolích a v každé jinak.** Jako **cíl** nevyvolá žádný dotaz — nemá čím vyjádřit ani klíč, ani mapování sloupce, takže jeho poptávka je prázdná a žádná tabulka není dotčená. Jako **zdroj** je naopak nejsilnějším případem pro čtení katalogu: F6 žádá právě z frameworku s implicitním mapováním vyrobit úplné mapování cíle doplněním z databáze.

Cenou za řízený rozsah je, že mezireprezentace doplněná pro jeden cíl nemusí stačit pro jiný. **Zápis je proto přírůstkový a idempotentní**: fakt už jednou získaný se nepřepisuje a druhá poptávka zapíše jen to, co dosud chybí. Čtení idempotentní být nemusí — obraz tabulky se přečte znovu a je to jeden dotaz. Pro běh Advisoru nad sadou kandidátů se poptávky kandidátů sjednotí do jedné dávky.

**Priorita zdrojů**, sestupně:

1. co tvrdí zdroj — explicitně v kódu, anotacích a mapovacích souborech, i konvencí zdrojového frameworku,
2. databázový katalog,
3. konvence cílového frameworku.

Databáze tedy nikdy nepřepíše to, co zdroj tvrdí. Odpovídá to pravidlu E9, které doplnění z metadat připouští výslovně s podmínkou nepřepsat explicitně definovaná mapování: zdrojový projekt může mít důvod mapovat jinak, než jak vypadá dnešní schéma, a překladač není od toho, aby ho opravoval. Rozpor mezi zdrojem a katalogem se proto nemlčky neřeší ve prospěch jednoho z nich — **ohlásí se** a překlad pokračuje se zdrojovou hodnotou. Hlášení konfliktů vyžaduje F5 přímo a je to důvod, proč se čte celý obraz tabulky.

**Konvence zdrojového frameworku patří do prvního stupně, ne mezi domněnky.** Pravidlo E9 mluví o „explicitně definovaných mapováních" a doslovně čteno by konvenci vylučovalo. Explicitnost je tu ale vlastnost zápisu, ne tvrzení: entita EF Core s vlastností `Id` primární klíč **má**, protože to tak EF Core definuje, a to, že ho nezapisuje atributem, na tom nic nemění. Dohad by to byl jen tehdy, kdyby se odvozovalo něco, co zdrojový framework neurčuje. Zdroj tedy vítězí nad katalogem i tam, kde fakt vyjádřil konvencí — jinak by se pravidlo o prioritě zdrojů dalo obejít prostě tím, že se mapování napíše idiomaticky.

**Konvenci zdrojového frameworku čte parser.** Je to jediná komponenta, která ví, ze kterého frameworku vstup pochází; builder ví jen to, kam se překládá. Kdyby konvenci vyhodnocoval builder, aplikoval by pravidla *cílového* frameworku a převod EF Core → NHibernate by klíč ztratil úplně. Nejde o rozšíření odpovědnosti parseru, ale o její naplnění: parser podle §4.3 extrahuje strukturní informaci z artefaktů konkrétního frameworku, a konvenční mapování je součástí toho, co artefakt sděluje.

**Čte se ta konvence, jejíž neznalost mění význam.** Konvence zdrojového frameworku je víc než klíč — implicitní je i název sloupce, název tabulky nebo nullabilita odvozená z jazykového typu. Materializovat je všechny by ale škodilo: prázdné `ColumnName` dnes znamená „nikdo neřekl" a každý cílový builder za něj dosadí tentýž název vlastnosti, takže vyplněním se nic nezíská. U klíče je to jinak: jeho neznalost mění entitu s klíčem na bezklíčový typ, což je jiný objekt. Pravidlem tedy je číst konvenci tam, kde by se jinak změnil význam, a jinde ji nechat na výchozím chování cíle.

**Původ faktu je událost, ne stav v mezireprezentaci.** Tady se rozhodnutí liší od 008 podruhé. 008 z prioritního řetězce vyvodilo, že model musí u každé hodnoty nést, odkud pochází. Rozhodnutí 010 to zúžilo a toto rozhodnutí jeho volbu přebírá: původ se vydává jako záznam v okamžiku, kdy fakt dodá katalog nebo konvence, a v modelu se neukládá. S6 chce zdroje metadat ve strojově čitelném **záznamu běhu**, což je log, ne pole v modelu; idempotentní zápis si vystačí s testem na prázdnou hodnotu; rozpor se vyhodnocuje v okamžiku dodání; a konvence cílového frameworku se do modelu nikdy neukládá, protože ji builder aplikuje až při generování. Alternativa by znamenala obalit každou hodnotu `PropertyMap` typem s původem, tedy podstatný zásah do modelu bez odběratele.

Konvence z varianty A tím nezaniká, jen klesá na třetí místo — bez připojené databáze je pořád lepší než nic. Mění se její postavení: uplatní se až po zdroji i katalogu a její uplatnění je vidět v diagnostice, ne v modelu.

**Primární klíč**, který je podnětem tohoto rozhodnutí, se řídí týmž řetězem, s jedním upřesněním na jeho konci. Pokud klíč nedá zdroj — ani atributem, ani konvencí — a nedá ho ani katalog, není to mezera, ale fakt: tabulka klíč nemá, nebo ji nemáme s čím ověřit. Konvenční dopočet klíče na straně **cíle** proto nepřipadá v úvahu: vymyslet klíč z vlastnosti jménem `Id`, aniž by to zdroj tvrdil, je horší než klíč nemít, protože chybu přesune z okamžiku překladu do okamžiku běhu. Táž jména, která parser čte jako tvrzení zdroje o EF Core entitě, jsou tedy pro builder bezvýznamná — rozdíl je v tom, kdo je vyslovil.

U EF Core to není hypotetická obava. Podle konvence se vlastnost pojmenovaná `Id` nebo `<název typu>Id` stane primárním klíčem, aniž by to kdokoli tvrdil. Chybějící klíč se tedy sám neohlásí: buď entita takovou vlastnost nemá a stavba modelu selže, nebo ji má a klíč tiše vznikne. Generovaný bezklíčový typ (`[Keyless]`) je proto nutný ne jako náhrada za chybějící klíč, ale jako **jediný způsob, jak tuto konvenci potlačit** a udržet v cílovém kódu totéž, co tvrdí mezireprezentace.

Bezklíčový typ ale nese omezení: nesmí být cílem vztahu, žádná jiná entita na něj nesmí mít navigační vlastnost a sám smí nést jen referenční navigace ven, nikoli kolekce. Tato omezení kopírují to, co v relační databázi platí o tabulce bez primárního klíče, na kterou nemůže ukazovat cizí klíč. Zdrojový model je ovšem porušit může, protože například Dapper nic z toho nevynucuje. Builder proto ověří, že entita omezením vyhovuje, a **teprve pak** bezklíčový typ vygeneruje; jinak překlad odmítne se strukturovanou diagnostikou. Cíl, který bezklíčový tvar nemá vůbec, odmítne vždy (NHibernate). Dapper klíč nevyjadřuje, takže se u něj nic neodmítá; nese varování podle rozhodnutí [004](004-unexpressible-facts-as-warnings.md).

**Variantu F nezavrhujeme, jen ji neimplementujeme teď.** Poptávka je datová struktura, takže rozšířit její sestavování z „jen cílový framework" na „cílový framework plus konkrétní úloha" je přírůstek, ne přestavba. Dokud jsou převáděné projekty v řádu jednotek entit, je úspora menší než složitost suchého průchodu; jakmile se nástroj otevře dávkovým vstupům (F14) a bude platit časový limit na projekt o sto entitách (S3), je F první kandidát na zpřesnění.

## Důsledky

**Vznikají dvě věci, ne jedna.** Komponenta, která čte katalog — frameworkově nezávislá, volaná orchestrací — a deklarace požadavků na straně cíle, ze které se sestavuje poptávka. Druhou z nich mezitím dodalo rozhodnutí [009](009-target-framework-descriptor.md) jako deskriptor cílového frameworku. Wrappery zůstávají bez znalosti databáze, což je podmínka toho, aby přidání frameworku dál znamenalo jen parser a builder (S1).

**Definice poptávky v rozhodnutí 009 se opravuje.** 009 ji převzalo z 008 včetně odečtení toho, co v mezireprezentaci už je. Odečítání odpadá: poptávka je sjednocení kategorií ve stavu *vyžaduji* a *umím vyjádřit*, a rozhoduje o zápisu, ne o dotazu.

**Každý parser musí umět konvence svého frameworku.** Pro `EFCoreEntityParser` to znamená odvození klíče z vlastnosti `Id` nebo `<název entity>Id` tam, kde entita nemá `[Key]` ani třídní `[PrimaryKey]`. To už podle 008 vzniklo (`FindConventionKey`) a teprve tím se stal bezpečným bezklíčový typ: ten je na místě jen tehdy, když se nepřítomnosti klíče v mezireprezentaci dá věřit. U javových parserů podle F7–F10 se táž otázka vrátí; u MyBatisu je konvenční mapování jediné, které existuje.

**Implicitní se převodem stává explicitním.** Entita EF Core s klíčem odvozeným z konvence vyjde z převodu EF Core → EF Core s atributem `[Key]`, který na vstupu nebyl. Není to vada: článek uvádí mezi návrhovými cíli (§3.3), že přístup má implicitní strukturu zviditelnit tam, kde to jde, a pravidlo E3 s výchozími konvencemi při chybějícím explicitním mapování počítá. S2 to neporušuje — determinismus se týká opakovaných běhů nad stejným vstupem.

**Fáze čtení katalogu musí být měřitelná odděleně** (S3). To je vlastnost návrhu, ne implementační detail: ohraničený krok s vlastním časem, ne dotazy prolnuté s generováním.

**Připojení k databázi se stává nepovinným vstupem překladu.** Dnes existuje jen pro Advisor (`ConnectionStrings:AdvisorDatabase`). Překlad bez něj nesmí selhat — proběhne, doplní se konvencemi a výsledek nese diagnostiku, že katalog nebyl k dispozici. Podle S4 se přihlašovací údaje nesmí objevit v generovaných souborech ani v záznamu běhu.

**Dvě existující místa se s tímto rozhodnutím sjednotí.** Prázdný `catch` v `QualifyEntityTableNames`, který polyká selhání spojení, je proti požadavku na diagnostiku. A dvojí řešení kvalifikace názvu tabulky — heuristika v `EFCoreLinqQueryParser` proti dotazu v `HarnessGenerationUtilities` — se nahradí jedním mechanismem s prioritou zdrojů podle tohoto rozhodnutí.

**Odblokuje to čtyři věci naráz:** Dapper jako plnohodnotný zdroj překladu, doplnění délky a přesnosti klíčových sloupců, naplnění `ColumnPairs` u vícesloupcových cizích klíčů tam, kde cílová entita není součástí převodu, a detekci N:M v parserech.

**Bezklíčový typ potřebuje registraci v modelu.** Na rozdíl od běžné entity, kterou EF Core může objevit přes navigaci z jiné entity, se keyless typ do modelu dostane jen přes `DbSet` nebo explicitní konfiguraci. Dnešní překladový builder `DbContext` negeneruje, takže se rozdíl neprojeví; při doplnění generování kontextu je to podmínka, na kterou je třeba pamatovat.

**Širší čtení konvencí ztratilo své kritérium.** 008 nechávalo otevřené, jestli se má z konvencí zdroje materializovat víc než klíč, a odkládalo to na okamžik, kdy mezireprezentace bude evidovat původ faktu. Ten okamžik nenastane. Otázka tím nemizí — jen potřebuje jiné kritérium než „až půjde poznat, odkud hodnota je". Je to samostatná otevřená položka.

**Co toto rozhodnutí neurčuje:** jak se katalog čte, které pohledy se dotazují, jak se řeší dialekt jiné databáze než SQL Server a zda se odpovědi cachují. To je práce podle F4 a rozhodne se při implementaci.
