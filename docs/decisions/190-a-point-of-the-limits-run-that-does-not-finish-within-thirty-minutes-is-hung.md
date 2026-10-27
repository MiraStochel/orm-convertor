# 190 — Bod běhu mezí je `hung` po třiceti minutách a překlad, který nevrátí do meze S3, se ukončí; za bodem, kde se zastavila cesta, a za stropem řady `entities` jde dál překlad sám, a řetězy bez závorek jsou řadami se stropem vypnutým

Datum: 2026-10-27
Stav: platí; nahrazuje mez dvou hodin na bod (`hung`) a konec procházky prvním bodem, který neprošel, v oddílu *Co se počítá za mez*, tabulku os v oddílu *Osy a řady* a sloupce oddílu *Limits* v oddílu *Druh `series` a experimentální běh* rozh. 182
Požadavky: S3, T3
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md); nezaznamenaný pokus o běh mezí z 2026-10-25/26 nad `f43d9df`; procházka řady `entities` v kontejneru `tests` na MIS3 z 2026-10-27 nad `689e58f` (níž, *Co měření ukázalo*); měření překladače Roslyn 5.6.0 na hostiteli MIS3 z téhož dne

## Kontext

Rozhodnutí 182 počítá za mez i bod, který „nedokončil do dvou hodin" (`hung`). Mez má jediný účel: procházka nesmí na jednom bodu stát donekonečna, a co za ní je, už záznam nepotřebuje vědět — bod neprošel, řada v tom směru skončila. Číslo dvě hodiny ale nevzešlo z měření, a první pokus o běh ukázal, co stojí: z osmnácti hodin .NET půlky nad fixturou šlo šestnáct na osm bodů, které rodič ukončil právě na té mezi — sedm bodů řady `expression` ze zdrojů HQL a JPQL v překladu (vada, kterou `ce94d6e` opravil) a bod řady `subqueries` se šestnácti úrovněmi z Hibernatu do EF Core při kompilaci. Záznam z toho pokusu nevznikl; běh se musí opakovat celý.

Kód navíc mez měřil jinak, než ji rozhodnutí vyslovilo: rodič čekal dvě hodiny na **proces potomka**, který prochází všechny cíle jednoho zdroje, takže řada s mnoha pomalými body, které všechny projdou, by skončila na bodu, na kterém ji mez zastihla. Mez na bod je oprava k rozhodnutí 182, ne volba; volbou je, kolik má měřit.

Druhá otázka vyšla z téhož měření. Procházka podle rozhodnutí 182 končí směr **prvním bodem, který neprošel na kterémkoli stupni**, a stupně cesty — kompilace, přijetí frameworkem — padají dřív než překlad: v řadě `entities` stojí překlad 800 entit nejvýš 8 s a kompilace sedm minut, do NHibernatu přijetí vyčerpá paměť; vnořené poddotazy do EF Core se přeloží za 20 ms a nezkompilují za dvě hodiny. Kde se zastaví cesta, tam záznam o mezi **překladače** nic neřekne, a strop řady `entities` je 800 kvůli ceně cesty, ne kvůli řádu, který by kdo napsal. Text práce přitom potřebuje obojí: kam dojde nástroj, aby se mu dalo věřit, a kam dojde ověření jeho výstupu.

Třetí otázka přišla s rozhodnutím 191: strop hloubky platí od něj i pro řetězy, které text píše bez závorky — součet, CASE v ELSE, `NOT NOT`, množinová operace za množinovou operací. Rozhodnutí 182 pouští se stropem vypnutým jen řady `nesting` a `subqueries`, protože strop je mez vyslovená, ne nalezená; řady `expression` a `unions` by se stropem zapnutým skončily na něm. Řady pro CASE a NOT nejsou vůbec.

### Co měření ukázalo

Mez na bod musí ležet nad nejdelším bodem, který projde, jinak udělá z bodu, který by prošel, `hung`, a posune mez řady. Nejdražší řadou je podle rozhodnutí 182 `entities`, protože druhý stupeň projektu kompiluje každý dotaz zvlášť s každou entitou; žádný běh ji dosud neprošel. Procházka jen této řady v kontejneru `tests` (procesory 2–3, 6 GiB) dala dobu bodu (překlad, čtení, kompilace, přijetí):

| Směr | 100 | 200 | 400 | 800 |
|---|---|---|---|---|
| EF Core → Dapper | 0,3 min | 0,4 min | 1,6 min | 4,7 min |
| EF Core → EF Core | 0,7 min | 0,8 min | 2,6 min | 6,8 min |
| EF Core → NHibernate | 0,3 min | 0,9 min | 3,5 min | paměť |
| Hibernate → Dapper | 0,3 min | 0,3 min | 1,1 min | 4,5 min |
| Hibernate → EF Core | 0,5 min | 0,6 min | 2,1 min | 7,8 min |
| Hibernate → NHibernate | 0,3 min | 0,8 min | 3,4 min | paměť |

Překlad sám trval nejvýš 8 s; zbytek je kompilace (mezi 400 a 800 entitami zhruba trojnásobná) a u NHibernatu stavba session factory. Bod 800 do NHibernatu z obou zdrojů po kompilaci (264 s a 296 s) vyčerpal při přijetí 6 GiB kontejneru, desítky minut se točil v úklidu paměti a jádro ho ukončilo (kód 137); je to mez cesty, ne překladače. Procesor běžel s turbem (stav 100 %, ne 99 % rozhodnutí 145), takže v zafixovaném prostředí budou doby delší; o kolik, neměřeno.

Druhým známým bodem nad mezí je kompilace vnořených poddotazů do EF Core: ze zdroje Hibernate v pokusu z 2026-10-25 trvala při 2, 4 a 8 úrovních 0,6 s, 2,8 s a 34,4 s a při 16 úrovních nedoběhla do dvou hodin, překlad stál kolem 20 ms. Překladač Roslyn 5.6.0 na hostiteli kompiluje týž dotaz z vnořených lambd při 8 úrovních 17,9 s s růstem dvakrát až dvaapůlkrát na úroveň, kdežto s každou úrovní v lokální proměnné `IQueryable` 0,02 s nezávisle na hloubce. Bod 16 úrovní do EF Core tedy neprojde při žádné mezi, o které má smysl mluvit — extrapolace dává hodinu a půl až dvě a půl — a čeká ho až šest zdrojů ve dvou procházkách katalogu.

**Je třeba rozhodnout**, kolik mez na bod měří, zda a jak procházka hledá mez překladače za mezí cesty, a jak řady pokryjí řetězy, které od rozhodnutí 191 strop počítá.

## Zvažované varianty

### Mez na bod

**1 — Ponechat dvě hodiny.** Mez je patnáctinásobek nejdelšího bodu, který prošel, a žádný bod, který projde, nezastihne. Zamítáme pro cenu: dvanáct bodů vnořených poddotazů do EF Core a čtyři body `entities` 800 do NHibernatu by běh stály až dvaatřicet hodin čekání na body, o nichž je napřed známo, že neprojdou, a o nichž záznam z té doby nevyčte nic, co by kratší mez neřekla.

**2 — Jedna hodina.** Osminásobek nejdelšího bodu, zhruba šestnáct hodin čekání. Bezpečnost proti bodu, který projde, se oproti třiceti minutám nezvyšuje o nic, co by měření podpíralo: nejpomalejší směr roste mezi 400 a 800 entitami trojnásobně a 800 je strop cesty, takže řada žádný delší bod, který by cestou prošel, nemá. Zamítáme; platí za polovinu běhu jistotou, kterou nepotřebuje.

**3 — Patnáct minut.** Dvojnásobek nejdelšího bodu změřeného s turbem; v zafixovaném prostředí bez turba může odstup klesnout k jedné, a mez by pak zastihla bod `entities` 800 do EF Core, který prošel. Zamítáme: mez, která může posunout mez řady, by záznam zkreslila tam, kde ho text práce cituje.

**4 — Třicet minut.** Čtyřnásobek nejdelšího bodu změřeného s turbem, dvojnásobek i při dvojnásobném zpomalení bez turba; čekání na známé body nad mezí zhruba osm hodin. Volíme.

### Mez překladače za mezí cesty

**5 — Druhý běh jen s překladem.** Samostatná procházka všech řad bez stupňů cesty, s vlastním souborem a vlastním souhrnem. Zamítáme: byl by to druhý mechanismus téže procházky s druhým formátem řádku a druhým souhrnem, a body, které už procházka s cestou přeložila, by se překládaly znovu.

**6 — Překlad sám dál, ve stejné procházce.** Kde cesta směr zastaví (bod nedosáhl třetího stupně, nebo proces zemřel až po překladu), procházka jde dál body téže řady jen s překladem, dokud neselže sám překlad — odmítnutí, výjimka, `too slow`, pád nebo `hung` před návratem překladu — nebo do stropu. Řádek takového bodu je `pass` s výsledkem `translated` a stupněm 0, jako dnes řádek javového cíle v .NET půlce. Souhrn pak sloupec *poslední přeložený* čte jako mez překladače a *poslední přijatý* jako mez cesty, a důvody dává dvakrát: kde se zastavil překlad, kde cesta. Volíme.

**7 — Za strop všech řad.** Překlad sám by pokračoval i za stropem každé řady, dokud neselže. Zamítáme: stropy řad kromě `entities` stojí na řádu, který nikdo nenapíše (512 joinů, 4096 podmínek), a mez překladače za nimi je akademická; cena by přitom rostla s počtem směrů — přes osm set ve dvou procházkách katalogu — a každý by skončil bodem, který stojí až mez S3. Za strop jde jen řada, jejíž strop určila cena cesty: `entities`, do 12 800 entit (šestnáctinásobek), kde se dá čekat mez S3 kolem dvou až šesti tisíc podle růstu mezi 400 a 800.

### Překlad, který nevrací

**8 — Čekat na překlad do meze na bod.** Bod, jehož překlad přesáhne 30 s, je podle rozhodnutí 182 `too slow`, ať doběhne kdykoli, ale procházka na něj čeká, a do třiceti minut meze na bod. Dokud bod, který v překladu stál, byl výjimkou (vada řady `expression`, opravená), stačilo to; od varianty 6 končí překladem nad mezí S3 skoro každý směr, který se zastaví, a od varianty 7 i čtyřiadvacet směrů řady `entities`. Zamítáme.

**9 — Ukončit překlad po mezi S3.** Potomek čeká na překlad nejvýš 30 s a pět vteřin rezervy; nevrátí-li se, řádek dostane `too slow` s dobou, po kterou se čekalo, a s pamětí alokovanou do té chvíle, a potomek ukončí sám sebe, protože vlákno v .NET zastavit nejde. Rodič ho spustí znovu a ten pokračuje dalším cílem. Ztrácí se jen to, o kolik byl překlad pomalejší, a to záznam nepotřebuje: mez S3 je mez požadavku a bod neprošel, ať by trval minutu nebo hodinu. Volíme. Překlad kvůli tomu běží na vlákně poolu, tedy tam, kde překládá požadavek nasazená instance.

### Řetězy bez závorek

**10 — Řady `expression` a `unions` se stropem zapnutým.** Skončily by na stropu rozhodnutí 191 a záznam by řekl jen to, co vyslovuje konfigurace. Zamítáme z důvodu, kterým rozhodnutí 182 pustilo `nesting` a `subqueries` bez stropu.

**11 — Všechny řetězy, které strop od rozhodnutí 191 počítá, jako řady se stropem vypnutým.** `expression` a `unions` běží se stropem vypnutým a přibývají dvě osy: `case` (CASE vnořený v ELSE předchozího, v projekci, 1 … 4096) a `not` (NOT řetězené před jediným predikátem filtru, sudý počet, aby bod vracel řádky, 2 … 4096), obě ve všech šesti zdrojích — HQL i JPQL čtou NOT i CASE, LINQ píše CASE jako `?:` a NOT jako `!`. Pád procesu je pak nález jako u `nesting`, a aby záznam řekl, **kde** zásobník přetekl — ve čtečce zdroje, v cizí gramatice, nebo v builderu cíle —, rodič z výstupu potomka přečte metodu, kterou runtime při přetečení vypíše, a dá ji do řádku. Volíme.

## Rozhodnutí

**Volíme varianty 4, 6 a 7 v rozsahu řady `entities`, 9 a 11.**

- **Mez na bod je třicet minut.** Bod, který je v běhu déle, je `hung`: rodič potomka ukončí, z řádku `in progress` udělá `crash` s výsledkem `hung` a stupněm, na kterém bod stál, a další potomek pokračuje dalším cílem. Mez platí na bod — rodič čte soubor bodů procházky každých 30 s a hodiny pouští znovu, kdykoli se bod v běhu změní —, ne na proces potomka.
- **Překlad se ukončí po mezi S3.** Potomek čeká na překlad nejvýš 35 s; nevrátí-li se, je bod `too slow` a potomek skončí.
- **Za mezí cesty jde dál překlad sám.** Směr, který cesta zastavila, pokračuje body téže řady jen s překladem, dokud neselže překlad nebo nepřijde strop; řada `entities` pokračuje stejně i za strop, body 1600, 3200, 6400 a 12 800. Body za stropem se nevypisují do `inputs/`: javová půlka a navazující srovnání podle rozhodnutí 175 soudí jen body řady. Řádek ví, kde bod skončil: překlad, který se vrátil, zapíše do metadat řádku verze dřív, než začne cesta, takže řádek bez nich zemřel v překladu.
- **Řetězy bez závorek jsou řadami se stropem vypnutým:** `expression`, `unions`, nové `case` a `not`. Řádek bodu, při kterém proces zemřel na přetečení zásobníku, jmenuje metodu, kterou runtime vypsal.
- **Souhrn** dává po směrech poslední přeložený bod, bod, kde se zastavil překlad, poslední přijatý a poslední běžící bod a bod, kde se zastavila cesta.

Meze jsou vlastnost běhu, ne nástroje: záznam je jmenuje, jako jmenuje strop osy. Proč na bod čtyřnásobek a ne víc ani míň, říkají varianty 2 a 3: méně by mohlo posunout mez řady `entities` v zafixovaném prostředí, víc platí čekáním bez informace. Kdyby záznam ukázal bod, který prošel za víc než polovinu meze, je to důvod mez znovu otevřít, ne ji tiše posunout. Cena překladu samotného je podle varianty 7 omezená: za strop jde čtyřiadvacet směrů, každý nejvýš čtyřmi body, z nichž poslední stojí nejvýš 35 s.

## Důsledky

- **Kód:** `LimitSearch.PointBound` třicet minut a čtení souboru rodičem; `LimitSearch.Walk` s překladem samotným za mezí cesty a `FailedInTranslation`; `Translate` na vlákně poolu s ukončením po mezi S3; `OverflowFrame` v řádku pádu; `LimitAxis.Beyond` a osy `case` a `not` v `LimitSeries`; dva sloupce zastavení v oddílu *Limits* `ExperimentSummary`.
- **Dokumentace:** [`architecture.md`](../architecture.md) §6.2, řádek *běh mezí*; `ORMConvertor/README.md`, oddíl *The limits run*.
- **Co záznam uvidí jinak:** body `entities` 800 do NHibernatu budou nejspíš `hung` místo `crashed` — mez je zastihne v úklidu paměti dřív než jádro — a za nimi pokračuje překlad sám; vnořené poddotazy do EF Core jsou `hung` po třiceti minutách a za nimi pokračuje překlad sám do 2048 úrovní.
- **Co rozhodnutí neurčuje:** zda EF Core píše vnořené poddotazy jinak (lokální proměnnou na úroveň) — to je volba hláskování cíle s vlastním měřením, co z ní EF Core přeloží —, ani co s mezí paměti cesty u `entities` 800 do NHibernatu; obojí patří do třídění záznamu podle rozhodnutí 182.
