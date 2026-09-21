# 091 — Katalog doplní i deklarovaně cizí zdroj a řekne to

Datum: 2026-09-21
Stav: platí
Požadavky: F4, F5, F6, F11, S6
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [015](015-mapping-fact-completion-from-the-catalog.md), [028](028-assembly-name-is-not-ours-to-invent.md), [029](029-database-connection-is-the-consumer-projects-fact.md), [030](030-scope-of-version-1-0.md), [031](031-key-class-as-declaration-of-key-parts.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [086](086-target-database-dialect-declared-by-the-descriptor.md) a [088](088-a-declared-foreign-source-dialect-is-not-read.md); [`architecture.md`](../architecture.md) §5.2 a §9, vyňatá oblast 5; otevřená položka „Deklarovaně cizí zdroj a připojený katalog"

## Kontext

Rozhodnutí 088 zavedlo deklaraci zdrojového dialektu a v seznamu toho, na co se při implementaci narazí, vyslovilo čtvrtou věc, kterou vědomě nezodpovědělo: **katalog zábrana neřeší a nesmí to předstírat.** Otevřená položka z toho udělala samostatnou otázku. Tohle rozhodnutí je její odpovědí a nic dalšího neotevírá.

### Co se v tom případu skutečně sejde

Tři fakta, každé z jiného rozhodnutí, a teprve dohromady tvoří problém:

- **Katalog je vždycky SQL Server.** `SqlServerCatalogReader` je jediné místo, které nástroj má, a jiného čtenáře metadat nemá (rozhodnutí 015, požadavek F4).
- **Připojení je fakt konfigurace serveru, ne požadavku** (rozhodnutí 029 a 030). V požadavku převodu proto nestojí nic, z čeho by se dalo usoudit, kam připojení míří, a nástroj se na to nemá koho zeptat.
- **Zábrana rozhodnutí 088 se doplňování netýká.** Její tabulka to říká výslovně: doplnění z katalogu „čte připojenou databázi, ne zdrojový artefakt", takže deklarace cizího dialektu čtení katalogu nezastaví.

Výsledek: zdroj, který deklaroval `AnotherSystem`, dostane mapovací fakta ze SQL Serveru — délku sloupce, jeho typ, nullabilitu, primární klíč — do projektu psaného pro něco jiného.

### Dvě čtení, která vstup nerozliší

**Legitimní.** Databáze se migrovala na SQL Server a projekt se převádí **právě proto**. Pak je katalog autoritou na nové schéma a doplnění je nejen v pořádku, ale je to přesně ta operace, kvůli které se připojení konfiguruje (F6).

**Omyl.** Připojení míří na SQL Server, který s projektem nemá nic společného — testovací kopie jiné aplikace, databáze druhého projektu na témž serveru. Pak se do mapování dostanou správně přečtená fakta z nesprávné databáze.

Nástroj ty dva případy nerozliší a rozlišit je nemůže: rozdíl mezi nimi není v žádném vstupu, který dostává. Ví, že katalog je SQL Server, a ví, co o sobě vyslovil zdroj; neví, jestli databáze za připojením je ta, se kterou zdrojový projekt mluví.

### Proč je to jiná otázka než u rozhodnutí 088

Stojí za to říct to nahlas, protože z toho plyne celá odpověď.

088 odmítlo číst cizí doslovné SQL proto, že by ho muselo **uhodnout**: `timestamp` je v T-SQL osm bajtů binárky a jinde okamžik v čase, oba významy jsou syntakticky v pořádku a z textu se vybrat nedá. Tady se nehádá nic. Katalog vrací `nvarchar(50)` a je to `nvarchar(50)` — fakt přečtený z živého schématu, jednoznačně a bez tabulky jmen, která by mohla znamenat něco jiného jinde.

Pochybnost je tedy jiného druhu: ne „je ten fakt přečtený správně", nýbrž „je ta databáze ta správná". A to není otázka o dialektu vůbec. Zábrana 088 na ni proto nemá co odpovědět a rozšířit ji sem by bylo přesně to „pokušení rozšířit hranici", před kterým se 088 samo varovalo.

### Sázka je u takového zdroje vyšší, ne nižší

Ještě jedna věc, bez které by se varianty vážily špatně. Zdroj, který deklaroval cizí dialekt, přichází do fáze doplnění **chudší o doslovné typy sloupců**: 088 je nepřečetlo a vydalo o každém `Loss` kategorie `DatabaseType`. Mezera, kterou katalog umí zaplnit, je u něj tedy větší než u obyčejného vstupu. Kdo by doplňování v tomhle případě odmítl, odmítá ho tomu vstupu, který ho potřebuje nejvíc.

### A ticho je totéž ticho, které 088 odstraňovalo

Dnes se nestane nic. Doplněný fakt dojde do artefaktu, vedle něj stojí záznam `Supplied` o jeho původu — a ten původ nese jméno katalogu, ne pochybnost o něm. Konzument čte, že délka sloupce přišla z databáze, a nemá z čeho poznat, že jde o databázi systému, o kterém zdroj řekl, že pro něj psaný není. Je to tiché tvrzení téhož druhu jako to, které 088 odstraňovalo; liší se jedině tím, že tvrdí původ místo významu.

## Zvažované varianty

### 1 — Mlčet jako dnes

Zamítáme, třemi důvody.

**Je to ticho, které nástroj jinde odstraňuje.** Záznam o původu faktu je celý smysl kategorie `Supplied` (rozhodnutí 010 a 015: původ je událost, ne stav v modelu). Vydat původ a zamlčet, že je sporný, je horší než nevydat nic, protože k výsledku zve důvěru.

**Proti F11 to stojí tak, jak to popsalo 088**: ne mlčením o vynechání, nýbrž tichým tvrzením. Nic se tu nevynechává — do mapování se něco **přidá** a o okolnosti toho přidání se mlčí.

**Protiargument, který je nejsilnější, a přesto neobstojí.** Dá se namítnout, že konzument si to odvodí sám: `ConversionResult` nese od 088 pole `DeclaredSourceDialect` a od 015 pole `CatalogState`, takže „zdroj je cizí" a „katalog byl přečtený" jsou obě v odpovědi. Neobstojí to ze dvou důvodů. `Reached` říká, že se katalog **přečetl**, ne že z něj do mapování něco došlo — u cíle s prázdnou poptávkou nebo u entity bez nalezené tabulky nedošlo nic, takže z dvojice polí se odvodí i případy, ve kterých není o čem mluvit. A hlavně: diagnostika je vrácená data, která konzument čte jako **seznam nálezů** (rozhodnutí 010). Nález, který v tom seznamu není a musí se poskládat ze dvou hlaviček odpovědi, je nález, který nikdo nenajde.

### 2 — Záznam u každé doplněné vlastnosti

Zamítáme. Věta je u všech doplněných vlastností táž — je to jedno tvrzení o běhu, ne N tvrzení o vlastnostech — takže se N-krát zopakuje. Je to tvar, který zamítá úvaha rozhodnutí 028: záznam popisující uspořádání místo nálezu o vstupu, tady ještě znásobený počtem vlastností. A neřekne nic navíc: **které** fakty se to týká, je „všech, o kterých fáze vydala záznam", a ty konzument v odpovědi už má.

### 3 — Doplňování při takovém rozporu vůbec nespustit

Zamítáme, čtyřmi důvody.

**Bere převodu právě to, kvůli čemu se katalog připojuje** (F6) — a bere to, jak ukazuje kontext, tomu vstupu, který je na tom nejhůř.

**Rozhoduje spor, který nástroj rozhodnout neumí, a rozhoduje ho proti pravděpodobnějšímu čtení.** Migrovaná databáze je běžný důvod, proč se projekt převádí; připojení na cizí server je omyl. Odmítnout doplnění znamená předpokládat omyl pokaždé.

**Prodražuje deklaraci o cenu, kterou 088 nevyslovilo.** Cena zábrany byla pojmenovaná a ohraničená: dotazová půlka u Dapperu a MyBatisu, jedno tvrzení na vlastnost u ostatních. Rozšířit ji o celé doplňování znamená, že deklarace `AnotherSystem` stojí víc, než za co se vyslovovala — a kdo ji pak nevysloví, aby o doplňování nepřišel, ztratí i zábranu, kterou vyslovit chtěl.

**Je to odmítnutí na podezření, ne na faktu.** Nástroj neví, že je připojení špatné; ví jen, že to nemá jak ověřit. Mezi „nevím" a „ne" je rozdíl a rozhodnutí 088 ho drželo na druhé straně právě proto, že tam **věděl**: text, který by se musel uhodnout, uhodnout odmítl.

### 4 — Nechat deklarovat i dialekt připojení

Zdroj deklaruje svůj dialekt, konzument navíc deklaruje, jakého systému je připojená databáze, a nástroj oboje porovná.

Zamítáme dvěma důvody, z nichž druhý je vážnější. Slovník by měl **jedinou hodnotu**: čtečka je jedna a je to SQL Server, takže pole by nevybíralo nic — týž důvod, kterým 086 zamítlo prázdnou deklaraci a 088 seznam jmen systémů. A především: odpovídá to na otázku, kterou nikdo nemá. Pochybnost nezní „je katalog SQL Server" — je, jistě a bez deklarace —, nýbrž „je tahle databáze ta, se kterou projekt mluví". Na to nemá tvar odpovědi žádný slovník, protože odpověď není o systému, ale o konkrétní instanci a konkrétním projektu.

### 5 — Doplnit a rozpor vyslovit jedním záznamem o běhu

Doplňování běží beze změny a běh, ve kterém se deklarovaně cizí zdroj potkal s faktem z katalogu, nese jeden záznam o tom, že se ty dvě věci rozcházejí.

## Rozhodnutí

**Volíme variantu 5. Katalog doplňuje i deklarovaně cizímu zdroji a fáze o tom vydá jediný záznam za běh: fakta přišla z katalogu SQL Serveru, ačkoli zdroj vyslovil, že jeho SQL to SQL není. Fakta se použijí, pochybnost se předá tomu jedinému, kdo ji umí rozhodnout — tomu, kdo připojení nakonfiguroval.**

Je to táž úvaha, kterou vedlo 088, dovedená k opačnému výsledku, protože vstupní fakt je jiný: **co by se muselo uhodnout, se nepřekládá; co se ví přesně a jen není jisté, odkud to platí, se použije a řekne se to.**

### Proč je to rozpor, a ne původ

Záznam má druh `Conflict`.

Rozhodnutí 015 zavedlo `Conflict` jako „zdroj a katalog se rozcházejí, nástroj to nemlčky neřeší ve prospěch jednoho z nich, nýbrž ohlásí". Rozhodnutí 031 ten druh už jednou rozšířilo za rozpor **hodnot** — na dvě prvostupňová tvrzení vstupu, když třída jmenovaná jako klíčová nese vlastní mapování. Tady se rozcházejí zase dvě tvrzení, která nástroj drží: co zdroj vyslovil o svém systému a pro jaký systém mluví katalog, ze kterého se četlo. Nástroj rozpor neřeší, ohlásí ho.

Ostatní druhy sedí hůř. `Supplied` jmenuje původ, o kterém se nepochybuje, a je vždycky o jednom faktu; tenhle záznam je o běhu a o pochybnosti. `Incompleteness` tvrdí, že něco chybí — nechybí nic, naopak přibylo. `Loss` ani `Failure` to nejsou, protože se nic nezahodilo ani neodmítlo.

**Jedna věc se u něj ale liší od všech ostatních rozporů a je potřeba ji vyslovit.** Jinde vítězí zdroj a hodnota katalogu se zahodí (pravidlo E9, rozhodnutí 015). Tady zdroj hodnotu **nemá** — právě proto fáze běžela — takže se použije ta katalogová. Společné jádro druhu se tím nemění: nástroj rozpor nerozhodl potichu. Mění se jen to, že „zdroj vítězí" je pravidlo o rozporu hodnot, ne definice druhu, a dokumentace druhu to musí říct, aby slovník zůstal poctivý.

### Jeden záznam na běh, bez kategorie a bez entity

Jedno tvrzení o běhu se vyslovuje jednou. Kterých faktů se týká, je odvoditelné — všech, o kterých fáze vydala `Supplied` nebo `Conflict` —, a co je odvoditelné, se neopakuje.

**Bez kategorie** (`Category`), protože pochybnost není vlastností jedné kategorie faktů; týká se všech stejně. Je to týž tvar, jakým 088 vydává odmítnutí dotazu: co není vlastností toho, o čem se mluví, kategorii nedostane (rozhodnutí 048).

**Bez entity a bez vlastnosti**, protože rozpor není nikde lokalizovaný. Záznam patří k běhu, stejně jako záznam o nenakonfigurovaném nebo nedostupném katalogu, který fáze vydává dnes.

### Kdy přesně vznikne

Musí platit všechno trojí:

1. zdroj deklaroval `AnotherSystem`,
2. katalog se přečetl,
3. fáze z něj vydala aspoň jeden fakt — záznam `Supplied` nebo `Conflict`.

**Třetí podmínka je ta, která dělá ze záznamu nález.** Bez ní by se záznam vydal i tam, kde ze SQL Serveru do mapování nedošlo nic: cíl s prázdnou poptávkou (Dapper) se katalogu neptá vůbec, a katalog, ve kterém se tabulka entity nenajde, nedodá nic. V obou případech není o čem mluvit a záznam by popisoval uspořádání, ne tenhle běh (rozhodnutí 028).

**`Conflict` se do třetí podmínky počítá stejně jako `Supplied`.** Fakt, který se s mapováním jen porovnal a prohrál se zdrojem, se s ním potkal taky — a u takového běhu je varování ještě užitečnější než jinde, protože vysvětluje, proč se katalog rozchází se zdrojem soustavně.

**Deklarace `SqlServer2022` záznam nevydá**: zdroj a katalog se shodují. **Nevyslovený dialekt (`null`) taky ne**: nevyslovené není tvrzení a nástroj čte přesně jako dosud (rozhodnutí 067 a 088). Zábrana i tenhle záznam tedy chrání jen toho, kdo promluví, a je to táž vědomá cena.

### Kde to v kódu sedí

`CatalogCompletion.Complete` dostává deklaraci jako nepovinný parametr — touž cestou, jakou podle 088 teče k parserům, tedy od orchestrace ke komponentě, která ji spotřebuje. **Na entitní builder se nevěší** ze stejného důvodu jako tam: builder patří cílovému frameworku, a fakt o zdroji putující přes cílový objekt by tu hranici obrátil.

**Co deklarace znamená, zůstává na jednom místě.** `Common.Sql.ForeignDialect` je od 088 jediné místo, které vyslovuje pravidlo o cizí deklaraci; fáze se ho zeptá a vlastní je jí jenom záznam — táž dělba, jakou 088 zavedlo pro wrappery. Predikát je ovšem **jiná otázka** než `StopsReading`: fáze se neptá, jestli se má přestat číst, nýbrž jestli se deklarace rozchází se systémem, za který mluví katalog. Že obě odpovědi dnes vyjdou na tutéž hodnotu výčtu, je shoda dneška, ne totožnost otázek — až se `AnotherSystem` rozpadne na pojmenované systémy (třetí otázka otevřené položky „Druhý databázový dialekt"), rozejdou se: čitelný cizí dialekt čtení nezastaví, ale s katalogem SQL Serveru se pořád rozcházet bude.

**Rozhoduje počet záznamů samotné fáze**, sejmutý těsně před čtením katalogu. Dřívější dva kroky fáze — rozpuštění klíčových tříd a materializace konvenčních navigací — umějí vydat `Conflict` vlastní (rozhodnutí 031) a běží dřív, takže základna se bere až za nimi.

### Co se nemění

Priorita zdrojů, poptávka, čtečka, dotazy do katalogu, všechny záznamy, které fáze vydává dnes, pole `CatalogState` i `CatalogReadTime`, a rozhraní parserů a builderů. Zdroj dál vítězí nad katalogem všude, kde nějakou hodnotu má.

## Důsledky

**REST kontrakt se nemění.** Nepřibývá pole; přibývá záznam v seznamu, který kontrakt má. Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) to tedy není ani MINOR — data v existujícím tvaru nejsou změna tvaru. Frontend nepotřebuje nic: záznamy se vypisují obecně a ovládací prvek deklarace vznikl s 088.

**S2 se nemění.** Záznam závisí jen na deklaraci v požadavku a na tom, co fáze z katalogu vydala; týž vstup nad týmž schématem dá týž výsledek.

**S6 dostává nález, ne pole.** Záznam běhu nesl `DeclaredSourceDialect` a `CatalogState` už od 088 a 015 a nese je dál; co přibývá, je nález v seznamu nálezů, protože z dvojice hlaviček se nález neodvozuje (viz zamítnutí varianty 1).

**Hranice záruk se nezmenší a znovu v ní ubude ticho.** Vyňatá oblast 5 říká dál, že dialekt je jeden. Nově ale platí, že se o cizí deklaraci mlčí jen tam, kde se nic nestalo: kde zdroj promluvil a katalog do mapování dosáhl, běh to vydá.

**Co zodpovězené není a ani být nemůže.** Jestli je připojená databáze ta, se kterou zdrojový projekt mluví. Nástroj řekne, co ví, a otázku předá; komu nestačí odpověď „zkontrolujte to", má k dispozici jedinou skutečnou jistotu, a tou je nepřipojovat ke zdrojovému projektu katalog, který k němu nepatří. Otevřená položka „Druhý databázový dialekt" si tím **nebere nic** — obě její zbývající otázky (čím se volí cílový dialekt, jestli padá zamítnutí multidialektové knihovny) platí beze změny.

**Na co se při implementaci narazí.**

1. **Základna počtu záznamů.** Bere se těsně před čtením katalogu, ne na začátku fáze — jinak by `Conflict` z rozpuštění klíčové třídy (rozhodnutí 031) vydal záznam o běhu, ve kterém se katalog ke slovu vůbec nedostal.
2. **Odvození jazykového typu leží za čtením a patří jinam.** `InferLanguageTypes` běží po doplnění, vydává `Convention` a běží i bez připojení; do podmínky se nepočítá. Fakt, ze kterého odvozuje, je ale často ten katalogový — a ten už svůj `Supplied` vydal, takže se případ chytí bez zvláštního opatření.
3. **Nevyslovený dialekt nesmí změnit ani bajt.** Žádný dnešní vstup dialekt nedeklaruje, takže celá matice musí zůstat táž a přibýt nesmí jediný záznam — je to táž kontrola, jakou si vyžádalo 088, a je to definice toho, že je i tohle přírůstek.
4. **Benchmarkingová kvalifikace názvů tabulek se toho netýká.** `HarnessGenerationUtilities` používá touž čtečku, ale mimo fázi doplnění a bez deklarace; Advisor jako celek stojí mimo záruky (§9, oblast 1) a nic se tam neprotahuje.

**Testy.** Že deklarovaně cizí zdroj s připojeným katalogem doplněná fakta **dostane** — mapování je bohatší, ne chudší. Že o takovém běhu vznikne právě jeden záznam `Conflict` bez kategorie a bez entity a že jmenuje obojí: co vyslovil zdroj i z čeho se četlo. Že týž vstup bez deklarace a s deklarací `SqlServer2022` dá bajtově týž výstup a žádný takový záznam. Že se záznam nevydá tam, kde z katalogu nic nedošlo — cíl s prázdnou poptávkou a entita, jejíž tabulka se nenašla. A že se vydá i u běhu, ve kterém katalog jen prohrál se zdrojem, tedy kde jsou samé `Conflict`y a ani jeden `Supplied`.
