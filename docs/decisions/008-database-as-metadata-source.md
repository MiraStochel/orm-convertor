# 008 — Databáze jako autoritativní doplněk chybějících mapovacích faktů

Datum: 2026-08-10
Stav: platí
Požadavky: F4, F5, F6, F11, S1, S3
Podklad: JSS článek, §7.1 a pravidlo E9

## Kontext

Mezireprezentace rozlišuje „fakt chybí" od „fakt je" — `ColumnName`, `Type`, `Length`, `IsNullable` i `PrimaryKey` jsou nepovinné a prázdná hodnota znamená, že ji zdrojový framework nevyjádřil. Co se s takovou mezerou stane dál, ale dnes nikdo neřídí. Každý builder si ji řeší po svém a vždy potichu.

Bezprostředním podnětem je entita bez primárního klíče. Dapper parser klíč nevytvoří nikdy, protože Dapper nemá čím ho zapsat. Oba buildery na `PrimaryKey == null` reagují prostým `return`: EF Core vygeneruje třídu bez `[Key]`, NHibernate `<class>` bez `<id>`. Ani jeden framework takový výstup nepřijme — EF Core vyžaduje klíč nebo explicitní bezklíčový typ, NHibernate mapování bez identifikátoru. Je to přesně ta třída chyby, kterou popsalo rozhodnutí [006](006-flat-composite-key-rendering.md): výstup je syntakticky správný a přitom nespustitelný.

Klíč je jen nejviditelnější případ. Stejný vzorec je všude jinde: `NHibernateEntityBuilder.ResolveNhType` hádá databázový typ z typu CLR, `ColumnName ?? Property.Name` dosazuje název vlastnosti místo názvu sloupce, a u klíčového sloupce se délka, přesnost a měřítko negenerují vůbec. Ve všech případech vzniká hodnota, kterou nikdo netvrdil, a od hodnoty přenesené ze zdroje ji ve výstupu nelze odlišit.

**Kde se dnes o cíli ví.** `ConversionHandler.Convert` dostává zdrojový i cílový framework spolu se všemi vstupy, tedy dřív, než se cokoli naparsuje. Instanci cílového builderu vytváří jako úplně první krok a parsery plní mezireprezentaci přímo do ní — `EntityMaps` je vlastnost cílového builderu a žádný neutrální objekt mezi parserem a builderem neexistuje. Znalost cíle je tedy k dispozici od začátku převodu a nemusíme si ji nijak obstarávat.

**Precedens v kódu.** Čtení databázového katalogu v repozitáři už je: `AdvisorBenchmarking.HarnessGenerationUtilities.QualifyEntityTableNames` se ptá `INFORMATION_SCHEMA` na schéma, kterým se kvalifikuje název tabulky. Volají ho `DapperBenchmarkHarnessBuilder.Build` a `EfCoreBenchmarkHarnessBuilder.Build`, tedy buildovací strana, jednou na začátku stavby harnessu, a ptá se právě na ten jediný fakt, který potřebuje, aby vygenerované SQL fungovalo. Ve třech ohledech je to blízko tomu, co chceme. Ve třech dalších ne: výsledek nikam do mezireprezentace neuloží, otevře sice jedno spojení, ale pošle samostatný dotaz na každou entitu a u jmen končících na „s" ještě druhý na tvar bez ní, a selhání spojení odchytí prázdným `catch`.

`baseline.md` uvádí, že v kódu žádná logika pro čtení schématu není. Mluví o doplňování **modelu** a v tom smyslu to platí — katalog se čte, ale plní jím vygenerovaný harness, ne mezireprezentaci.

**Tentýž problém se přitom v repozitáři řeší dvakrát a jinak.** Chybějící schéma u názvu tabulky doplňuje `EFCoreLinqQueryParser.ResolveQualifiedTableName` heuristikou nad `EntityMaps` včetně dopočtu pluralizací, zatímco `HarnessGenerationUtilities.ResolveQualifiedTableName` téhož jména dotazem do katalogu. Dvě odpovědi na tutéž otázku ve dvou vrstvách, které o sobě nevědí.

Je proto třeba rozhodnout čtyři věci: kdo se databáze ptá, kdy, s jakou prioritou vůči ostatním zdrojům a co se stane, když odpověď nepřijde.

## Zvažované varianty

### A — Konvence v builderu

Dnešní stav. Chybějící fakt si každý builder nahradí odhadem ve chvíli generování.

Je to nejlevnější varianta a nevyžaduje připojení k databázi. Platí za to tím, že odhad se nikde nezaznamená: dva cílové frameworky dostanou z téže mezireprezentace různá data, protože každý hádá jinak, a F11 nemá co ohlásit, protože v okamžiku generování už není poznat, co bylo tvrzení a co domněnka.

### B — Dotaz z parseru

Parser by při čtení zdrojového artefaktu rovnou doplňoval, co v něm není.

Mezireprezentace by tím byla úplná hned po parsování. Logika doplňování by ale žila v každém parseru zvlášť a s každým dalším frameworkem (Hibernate, MyBatis, EclipseLink) by se opakovala — proti smyslu S1, podle kterého se framework přidává jen parserem a builderem, ne rozšiřováním společné funkcionality. Navíc parser podle článku (§4.3) analyzuje artefakty konkrétního frameworku; databázový katalog žádný takový artefakt není.

### C — Úplné doplnění mezireprezentace před generováním

Samostatný krok nad hotovou mezireprezentací doplní z katalogu vše, co v ní chybí, bez ohledu na to, kdo si o to řekl.

Drží čtení na jednom místě a mezireprezentace je po něm úplná pro libovolný cíl. Cenou je, že rozsah dotazů neurčuje nikdo: doplní se i fakta, která cílový framework nevyjádří. Při převodu do Dapperu, který nemá čím zapsat ani klíč, ani typ sloupce, je celý krok zbytečný. Navíc předpokládá mezireprezentaci odpojenou od builderu, kterou dnešní orchestrace nemá.

### D — Dotaz z builderu v místě spotřeby

Builder se ptá katalogu ve chvíli, kdy při generování narazí na chybějící fakt.

Rozsah dotazů je tím minimální ze všech variant: ptáme se právě na to, co se skutečně vypisuje, a Dapper builder se jako cíl nezeptá nikdy. Cena je ale vysoká ve čtyřech ohledech. Dotazy vznikají po jednom během generování, takže z jedné dávky je N drobných dotazů — přesně vzorec, jehož výskyt v aplikacích postavených na ORM měříme jako problém. Logika čtení se opakuje v každém builderu, což je proti S1. Protože se mezera odhalí až uprostřed psaní výstupu, nemá F11 co validovat předem: validace úplnosti před generováním je pak buď zbytečná, nebo lživá. A protože se dotazy prolínají s generováním, nelze splnit S3, který žádá měřit a reportovat načtení metadat odděleně od času překladu.

### E — Poptávku formuluje cíl, čtení obstará jedna komponenta

Cílová strana převodu deklaruje, které kategorie mapovacích faktů vyžaduje a které umí vyjádřit. Z deklarace vznikne jediná dávková poptávka, ta se položí katalogu před generováním a jejím výsledkem se doplní mezireprezentace.

Odděluje se tím mechanismus od řízení: čte jedno místo, ale samo neurčuje, co číst.

### F — Poptávku formuluje celé zadání převodu

Rozsah neurčuje jen cílový framework, ale konkrétní úloha: které entity a dotazy se skutečně převádějí, jakým směrem, a zda jde o jeden cíl, nebo o sadu kandidátů posuzovaných Advisorem.

Poptávka je nejužší možná — entitní větev potřebuje z katalogu jiná fakta než dotazová a u běhu Advisoru nad třemi kandidáty stačí jedna dávka místo tří. Zadání ale plně neurčuje potřebu předem: co se bude vypisovat, se ví až během generování, takže varianta vyžaduje buď suchý průchod, který požadavky posbírá, nebo konzervativní nadhad, který ji zpět přibližuje variantě E.

## Rozhodnutí

**Varianta E: katalog čte jediná komponenta, ale rozsah čtení určuje cílová strana převodu — jednou dávkou před generováním.**

Klíčové je, že „jedno místo pro čtení" a „ptát se jen na to, co má odběratele" nejsou protiklady. Jsou to odpovědi na dvě různé otázky a rozhodnutí je odděluje:

- **Mechanismus** — připojení, dialekt, konkrétní dotazy do katalogu, dávkování a případné cachování — žije na jednom místě, v komponentě nezávislé na frameworku. Nový parser ani nový builder ho neimplementuje znovu (S1) a při rozšíření na jinou databázi se mění jedno místo. Dvojí řešení kvalifikace názvu tabulky popsané v kontextu je doklad, že bez tohoto pravidla vzniká druhá odpověď sama od sebe.
- **Řízení** — co se má dohledat — formuluje strana, která fakta spotřebuje. Poptávka je datová struktura, ne volání: cíl řekne, co potřebuje, komponenta to obslouží jednou dávkou.

**Iniciativa na straně cíle je čtením článku, ne odklonem od něj.** Popis parsování entit v §7.1 říká výslovně, že když zdroj mapovací informaci nemá, parser zaznamená jen to, co má, a doplnění z konvencí i z metadat — s odkazem na pravidlo E9 — *delegates convention- and metadata-based completion to the builder*. Odpovědnost za doplnění tedy článek přiřazuje builderu; varianty B a C jdou proti němu. Pravidlo E9 samo je permisivní a jeho jedinou tvrdou podmínkou je nepřepsat explicitní mapování; o okamžiku ani o rozsahu neříká nic, a právě ten prostor toto rozhodnutí zaplňuje.

**Proti doptávání během generování mluví S3.** Požadavek žádá, aby se načtení metadat databáze měřilo a reportovalo odděleně od času překladu. Odděleně měřit lze jen ohraničenou fázi; dotazy rozeseté mezi vypisováním vlastností by šlo nanejvýš sečíst a od času generování by je nešlo odlišit vůbec. Táž logika plyne z §9.2 článku: prototyp slučuje informace z více parserů do jedné instance mezireprezentace *před generováním*. Je to sice o parserech, ne o databázi, ale je to vzorec, který článek pro doplňování mapovacího modelu používá — sloučit napřed, generovat potom.

**Napětí, které rozhodnutí nese vědomě.** Obrázek 3 vede šipku z databázových metadat do frameworkově agnostické reprezentace, tedy metadata plní mezireprezentaci, ne builder, a §4.3 uvádí, že se frameworkově závislá omezení vynucují výhradně při generování. Čteno doslovně by z toho nejlépe vyšla varianta D. Ve skutečnosti si tato místa neodporují: §7.1 říká, **kdo doplnění iniciuje**, obrázek 3, **kam výsledek patří**. Varianta E drží obojí — iniciuje cíl, výsledek končí v mezireprezentaci.

**Rozsah řízený cílem není v rozporu s F4.** Požadavek žádá umět načíst „alespoň" tabulky, sloupce, datové typy, nullabilitu, primární a cizí klíče, unikátní omezení, délku, přesnost a měřítko, a ověřuje se podílem správně získaných metadat proti testovací databázi. To je test schopnosti čtečky, ne požadavek, aby se při každém převodu načetlo všechno. Komponenta musí umět celý výčet; kdy se který kus použije, řídí poptávka.

**Rozsah řízený cílem nedělá mezireprezentaci frameworkově závislou.** Cíl neurčuje, co mezireprezentace umí nést, ani jaký tvar doplněný fakt má — určuje jen, co se vyplatí dohledávat. Název sloupce dohledaný kvůli NHibernate je týž název sloupce, jaký by si vyžádal EF Core. Mezireprezentace tedy zůstává neutrální v obsahu a řízená v úplnosti. Že výsledek patří do ní, a ne jen do generovaného výstupu, žádá F5 přímo: slučovat se má do jediné mezireprezentace.

**Dapper vystupuje v obou rolích a v každé jinak.** Jako **cíl** nevyvolá žádný dotaz — nemá čím vyjádřit ani klíč, ani mapování sloupce, takže jeho poptávka je prázdná. Jako **zdroj** je naopak nejsilnějším případem pro čtení katalogu: F6 žádá právě z frameworku s implicitním mapováním (Dapper, MyBatis) vyrobit úplné mapování cíle doplněním z databáze. Rozsah tedy neurčuje zdroj, ale cíl, a nulová poptávka u Dapperu platí jen v jednom směru.

Cenou za řízený rozsah je, že mezireprezentace doplněná pro jeden cíl nemusí stačit pro jiný. Doplňování je proto **přírůstkové a idempotentní**: druhá poptávka se ptá jen na to, co dosud chybí, a fakt už jednou získaný se znovu nedohledává ani nepřepisuje. Pro běh Advisoru nad sadou kandidátů se poptávky kandidátů sjednotí do jedné dávky.

**Priorita zdrojů**, sestupně:

1. explicitní fakt ze zdrojových artefaktů — kód, anotace, mapovací soubory,
2. databázový katalog,
3. konvence cílového frameworku.

Databáze tedy nikdy nepřepíše to, co zdroj tvrdí. Odpovídá to pravidlu E9, které doplnění z metadat připouští výslovně s podmínkou nepřepsat explicitně definovaná mapování: zdrojový projekt může mít důvod mapovat jinak, než jak vypadá dnešní schéma, a překladač není od toho, aby ho opravoval. Rozpor mezi zdrojem a katalogem se proto nemlčky neřeší ve prospěch jednoho z nich — **ohlásí se** a překlad pokračuje se zdrojovou hodnotou. Hlášení konfliktů vyžaduje F5 přímo.

Konvence z varianty A nezaniká, jen klesá na třetí místo — bez připojené databáze je pořád lepší než nic. Mění se ale její postavení: **každý doplněný fakt nese svůj původ** a konvenční odhad je ve výsledku odlišitelný od hodnoty přenesené ze zdroje i od hodnoty přečtené z katalogu. Bez toho by rozhodnutí nebylo ověřitelné a F11 by neměla co hlásit.

**Primární klíč**, který je podnětem tohoto rozhodnutí, se řídí týmž řetězem, s jedním
upřesněním na jeho konci. Pokud klíč nedá zdroj ani katalog, není to mezera, ale fakt:
tabulka klíč nemá, nebo ji nemáme s čím ověřit. Konvenční dopočet klíče nepřipadá
v úvahu — vymyslet klíč z vlastnosti jménem `Id` je horší než klíč nemít, protože chybu
přesune z okamžiku překladu do okamžiku běhu.

U EF Core to není hypotetická obava. Podle konvence se vlastnost pojmenovaná `Id` nebo
`<název typu>Id` stane primárním klíčem, aniž by to kdokoli tvrdil. Chybějící klíč se
tedy sám neohlásí: buď entita takovou vlastnost nemá a stavba modelu selže, nebo ji má
a klíč tiše vznikne. Generovaný bezklíčový typ (`[Keyless]`) je proto nutný ne jako
náhrada za chybějící klíč, ale jako **jediný způsob, jak tuto konvenci potlačit**
a udržet v cílovém kódu totéž, co tvrdí mezireprezentace.

Bezklíčový typ ale nese omezení: nesmí být cílem vztahu, žádná jiná entita na něj nesmí
mít navigační vlastnost a sám smí nést jen referenční navigace ven, nikoli kolekce. Tato
omezení nejsou svévolná — kopírují to, co v relační databázi platí o tabulce bez
primárního klíče, na kterou nemůže ukazovat cizí klíč. Zdrojový model je ovšem porušit
může, protože například Dapper nic z toho nevynucuje. Builder proto ověří, že entita
omezením vyhovuje, a **teprve pak** bezklíčový typ vygeneruje; jinak překlad odmítne se
strukturovanou diagnostikou. Cíl, který bezklíčový tvar nemá vůbec, odmítne vždy
(NHibernate). Dapper klíč nevyjadřuje, takže se u něj nic neodmítá; nese varování podle
rozhodnutí [004](004-unexpressible-facts-as-warnings.md).

**Variantu F nezavrhujeme, jen ji neimplementujeme teď.** Poptávka je datová struktura, takže rozšířit její sestavování z „jen cílový framework" na „cílový framework plus konkrétní úloha" je přírůstek, ne přestavba. Dokud jsou převáděné projekty v řádu jednotek entit, je úspora dotazů menší než složitost suchého průchodu; jakmile se nástroj otevře dávkovým vstupům (F14) a bude platit časový limit na projekt o sto entitách (S3), je F první kandidát na zpřesnění.

## Důsledky

**Vznikají dvě věci, ne jedna.** Komponenta, která čte katalog — frameworkově nezávislá, volaná orchestrací — a deklarace požadavků na straně cíle, ze které se sestavuje poptávka. Wrappery zůstávají bez znalosti databáze, což je podmínka toho, aby přidání frameworku dál znamenalo jen parser a builder (S1). Pojem „co cílový framework vyžaduje" navíc nevzniká kvůli tomuto rozhodnutí sám o sobě — potřebujeme ho stejně pro členy, které si framework vynucuje na generované třídě, a tato deklarace je jeho druhá polovina, obrácená od výstupu ke vstupu.

**Mezireprezentace musí evidovat původ faktu.** Dnes nese jen hodnotu. Bez rozlišení „ze zdroje / z katalogu / konvencí" nelze splnit ani hlášení konfliktů z F5, ani diagnostiku z F11. Konkrétní podobu tohoto údaje řeší otevřené rozhodnutí o diagnostice jako kategorii, kterému se tím zpřesňuje zadání.

**Fáze čtení katalogu musí být měřitelná odděleně** (S3). To je vlastnost návrhu, ne implementační detail: ohraničený krok s vlastním časem, ne dotazy prolnuté s generováním.

**Připojení k databázi se stává nepovinným vstupem překladu.** Dnes existuje jen pro Advisor (`ConnectionStrings:AdvisorDatabase`). Překlad bez něj nesmí selhat — proběhne, doplní se konvencemi a výsledek nese diagnostiku, že katalog nebyl k dispozici. Podle S4 se přihlašovací údaje nesmí objevit v generovaných souborech ani v záznamu běhu.

**Dvě existující místa se s tímto rozhodnutím sjednotí.** Prázdný `catch` v `QualifyEntityTableNames`, který polyká selhání spojení, je proti požadavku na diagnostiku. A dvojí řešení kvalifikace názvu tabulky — heuristika v `EFCoreLinqQueryParser` proti dotazu v `HarnessGenerationUtilities` — se nahradí jedním mechanismem s prioritou zdrojů podle tohoto rozhodnutí.

**Odblokuje to čtyři věci naráz:** Dapper jako plnohodnotný zdroj překladu, doplnění délky a přesnosti klíčových sloupců, naplnění `ColumnPairs` u vícesloupcových cizích klíčů a detekci N:M v parserech. Všechny čtyři dnes stojí na tomtéž — že cílové sloupce a jejich vlastnosti nejdou určit z jedné překladové jednotky.

**Bezklíčový typ potřebuje registraci v modelu.** Na rozdíl od běžné entity, kterou EF
Core může objevit přes navigaci z jiné entity, se keyless typ do modelu dostane jen přes
`DbSet` nebo explicitní konfiguraci. Dnešní překladový builder `DbContext` negeneruje,
takže bez něj nefunguje ani běžná entita a rozdíl se neprojeví; při doplnění generování
kontextu je to podmínka, na kterou je třeba pamatovat.

**Co toto rozhodnutí neurčuje:** jak se katalog čte, které pohledy se dotazují, jak se řeší dialekt jiné databáze než SQL Server a zda se odpovědi cachují. To je práce podle F4 a rozhodne se při implementaci.