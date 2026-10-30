# 195 — Javová půlka běhu mezí pokračuje, kde skončila, bod, který .NET půlka do cíle nepřeložila, instanci neposílá, a překládá instancí s mezí paměti a restartem; překlad má 35 s, bod 30 minut a server javové půlky 2048 MB

Datum: 2026-10-30
Stav: platí; nahrazuje soud javové půlky nad každým vypsaným bodem v oddílu *Druh `series` a experimentální běh* rozh. 182
Požadavky: S3, S5, T3
Podklad: rozhodnutí [078](078-java-suite-as-a-client-of-a-running-instance.md), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [164](164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [190](190-a-point-of-the-limits-run-that-does-not-finish-within-thirty-minutes-is-hung.md), [193](193-the-docker-vm-has-twelve-gigabytes-so-that-the-limits-inside-it-add-up-to-less-than-it.md); [záznam běhu mezí 2026-10-29](../runs/2026-10-29-limits.md) s javovou půlkou přerušenou; [záznam experimentálního běhu 2026-10-24](../runs/2026-10-24-experiment.md) se servery na 2048 MB; měření špičky paměti překladu stropových bodů řad do javových cílů na MIS2 z 2026-10-30 (níž, *Co měření ukázalo*); `experiment/ExperimentRunPlanTest`

## Kontext

Běh mezí (rozh. 182, 190) má dvě půlky: .NET půlka překládá a soudí v procesu a u javového cíle zná jen překlad, javová půlka překládá tytéž vypsané body nasazenou instancí přes HTTP (rozh. 078) a prochází jimi stupně cesty. Dne 2026-10-29 obě .NET půlky doběhly a javová půlka s fixturou jako katalogem posoudila sedm os z patnácti (1077 řádků z asi 2400 na procházku); od 22:02 UTC instance `test_app` hlásila hladovění poolu vláken a v 02:17 UTC ji jádro virtuálního stroje ukončilo pro paměť, s ní skončila databáze i sada, a záznam javové půlky nevznikl. Jaký mechanismus body v instanci hromadil, doložené není — log instance říká jen hladovění a konec —; co kód javové půlky dělal, doložené je:

- **Nepokračovala.** Soubor `java-tool-rows.tsv` psala celý znovu a z dřívějšího běhu nechávala jen řádky procházky s druhým katalogem; půlka přerušená v desáté hodině začínala od nuly. .NET půlka přitom pokračuje, kde skončila (bod s řádkem `pass` se znovu neprochází), a README to tvrdilo o obou.
- **Překlad nad mezí klienta byl výjimka, ne řádek.** Klient čekal dvě minuty; po nich `HttpTimeoutException` prošla jako `IllegalStateException`, případ testu padl a bod řádek neměl. Instance přitom překládala dál, protože obsluha `/convert` je synchronní a do pipeline nejde žádný token zrušení.
- **Stupně cesty mez neměly.** Mez třiceti minut na bod (rozh. 190) drží rodič .NET půlky nad potomkem; kompilace javacem, přijetí Hibernatem či EclipseLinkem a dotaz čtvrtého stupně v javové sadě mez neměly, takže bod, který se nevrátí, zastaví celou půlku.
- **Instance neměla mez paměti.** Kontejnery sad mají 6 GB (`mem_limit`), server 4096 MB v měřicím režimu (rozh. 164), instance `test_app*` nic. Pravidlo rozh. 193 — součet mezí všeho, co běh pouští současně, se musí vejít do 12 GB stroje — pro instanci nenechává místo: server 4096 MB a asi půl GB mimo buffer pool, sada 6 GB, jádro a Docker kolem 1 GB dávají 11,5 GB.

**Je třeba rozhodnout**, jak javová půlka nese body, které instance nepřeloží nebo cesta nedosoudí, a jak se do stroje vejde s mezí instance.

### Co měření ukázalo

Kolik paměti překlad bodu v instanci potřebuje, nikdo neměřil: .NET půlka zapisuje paměť *alokovanou* za překlad (až 9 GB u 4096 vlastností do MyBatisu), což je součet přes úklidy, ne špička. Měřeno proto 2026-10-30 na MIS2 (Windows, sestavení Release, .NET 10): překlad stropového bodu každé řady z každého zdroje, který ji vyslovuje, do tří javových cílů, v procesu s fixturou jako katalogem a s omezeními procházky (strop zanoření vypnutý u řad, které ho vypínají), se serverovým GC jako v obrazu instance (`DOTNET_gcServer=1`), špička pracovní sady a spravované haldy vzorkovaná každé 2 ms. Překlad běžel na vlákně se zásobníkem 512 MB: s 1 MB i 1,5 MB zásobníku na Windows přetekl zásobník už u 4096 úrovní řady `nesting`, kde ho v kontejneru podle záznamu 2026-10-29 žádný zdroj nepřetekl — rámce na Windows x64 jsou větší a pád je vlastnost hostitele měření, ne kontejneru; o paměti nic neříká.

| Řada (strop) | Nejtěžší směr do javového cíle | Špička pracovní sady | Pracovní sada procesu před bodem | Špička haldy | Čas |
|---|---|---|---|---|---|
| `unit-size` (64 MB) | Dapper → MyBatis | 1 640 MB | 1 605 MB | 912 MB | 2,9 s |
| `subqueries` (2048) | Hibernate → Hibernate | 691 MB | 251 MB | 420 MB | 2,0 s |
| `entities` (800) | EF Core → Hibernate | 571 MB | 571 MB | 243 MB | 3,3 s |
| `properties` (4096) | Hibernate → MyBatis | 401 MB | 302 MB | 137 MB | 6,0 s |
| `case` (4096) | MyBatis → MyBatis | 388 MB | 285 MB | 124 MB | 1,8 s |
| `relations` (256) | EF Core → EclipseLink | 323 MB | 283 MB | 121 MB | 0,6 s |
| `expression` (4096) | NHibernate → EclipseLink | 305 MB | 285 MB | 45 MB | 0,1 s |
| `not` (4096) | Dapper → EclipseLink | 296 MB | 284 MB | 71 MB | 0,4 s |
| `exists` (512) | Dapper → Hibernate | 290 MB | 290 MB | 50 MB | 0,04 s |
| `conditions` (4096) | MyBatis → MyBatis | 267 MB | 242 MB | 84 MB | 0,4 s |
| `key-parts` (64) | EF Core → Hibernate | 264 MB | 264 MB | 27 MB | 0,3 s |
| `ctes` (256) | EF Core → MyBatis | 258 MB | 253 MB | 63 MB | 0,3 s |
| `unions` (256) | Dapper → Hibernate | 249 MB | 249 MB | 42 MB | 0,03 s |
| `nesting` (4096) | Dapper → Hibernate | 246 MB | 240 MB | 18 MB | 0,4 s |
| `joins` (512) | EF Core → MyBatis | 229 MB | 194 MB | 103 MB | 4,0 s |

Pracovní sada před bodem je sada procesu měření, který serverový GC mezi body nezmenšuje; co bod přidal, je rozdíl. Žádný stropový bod do javového cíle nepřidal víc než 440 MB (`subqueries` 2048) a žádný neměl haldu nad gigabajt; největší — 912 MB u 64 MB komentáře řady `unit-size` — patří bodu, který instance odmítne mezí velikosti těla Kestrelu dřív, než ho překládá (rozh. 182). Časy překladu do javových cílů na stropech jsou vteřiny, nejvíc 9,3 s u 4096 vlastností z NHibernatu do MyBatisu — kde .NET půlka přeložila do 30 s, instance se do 35 s vejde.

## Zvažované varianty

### Těžké body

**1 — Jen mez paměti instance.** Nutná — bez ní bod, který chce víc, ukončí stroj, ne instanci —, ale sama nestačí: s mezemi rozh. 193 pro ni není místo (půl GB), a sada by na bodu, který instanci ukončil, dál padala výjimkou bez řádku a další body by šly na mrtvou instanci. Část volby, ne volba.

**2 — Javová půlka soudí jen body, které .NET půlka do téhož cíle přeložila v mezi S3.** Volíme v užším tvaru: instanci se neposílá bod, jehož překlad do cíle .NET půlka **nedokončila** — `too slow`, `crashed`, `hung` —, protože instance je tentýž překladač a překlad, který se v procesu nevrátil do 30 s, se nevrátí ani v ní; řádek bodu nese výsledek .NET půlky, aby ho souhrn spároval. Bod `refused` a `threw` se posílá: odpověď přijde hned a je to nález o nasazení, ne o překladači — mez velikosti těla Kestrelu u řad `unit-size` a `entities` (rozh. 182) by jinak záznam neměl. Ani tahle varianta sama nestačí: bod, který .NET půlka přeložila za 25 s s pěti gigabajty alokací, by instanci bez meze pořád mohl ukončit.

**3 — Instance ukončí překlad po odpojení klienta.** Zamítáme. Obsluha `/convert` je synchronní a zrušení by znamenalo token zrušení v každém parseru a builderu, tedy změnu `AbstractWrappers` (S1); a rozh. 190 říká to podstatné samo: vlákno v .NET zastavit nejde, potomek proto ukončuje sám sebe — instance se za jeden požadavek ukončit nemůže.

**4 — Snížit mez sady místo serveru.** Zamítáme z důvodu, kterým rozh. 193 zamítlo totéž pro .NET sadu: co javac a start Hibernatu nad 800 entitami potřebují, změřené není, a menší mez by dělala pády tam, kde je dělá konfigurace.

**5 — Server javové půlky na 2048 MB.** Volíme. Javová půlka nic neměří — čas a paměť překladu zapisuje .NET půlka —, server pro ni čte katalog fixtury a pouští jeden dotaz na bod nad fixturou; experimentální běh pouští na stejném základě dva servery s 2048 MB ([záznam 2026-10-24](../runs/2026-10-24-experiment.md)) a rozh. 193 to výslovně dovoluje („běh, kterému to s výchozími mezemi nevyjde, jednu z nich sníží a záznam to řekne"). Součet pak vychází: server asi 2,5 GB, sada 6 GB, jedna instance 2 GB, jádro a Docker kolem 1 GB — 11,5 GB.

### Bod, který se nevrátí

**6 — Klient čeká dvě minuty a končí výjimkou** (dosud). Zamítáme: bod bez řádku, případ testu padlý, záznam o bodu mlčí a sada stojí na dalším bodu nad instancí, která možná nežije.

**7 — Meze rozh. 190 i v javové půlce, každý konec řádkem.** Volíme. Klient čeká na překlad 35 s — mez S3 a rezerva, jak čeká potomek .NET půlky — a bod je pak `too slow`; instance, která neodpoví vůbec (spojení odmítnuté nebo přerušené, tedy instance ukončená mezí paměti), je `crashed` a sada na ni před dalším bodem počká, jako čeká na začátku; bod v běhu déle než třicet minut je `hung` na dosaženém stupni a vlákno, které ho soudí, je opuštěno — v JVM vlákno zastavit nejde, stejně jako v .NET; je démonické, aby sadu nedrželo naživu —; cesta, která vyhodí výjimku, zapíše `threw` na dosaženém stupni a výjimku pustí dál, aby případ testu padl viditelně. Proces na bod podle vzoru `foreign-run.sh` zamítáme: byl by to druhý mechanismus téže smyčky s druhým formátem řádku, a potomkem by tu byl celý JVM, který by někdo musel zvenčí pouštět znovu.

### Pokračování

**8 — Soubor řádků se píše celý znovu** (dosud). Zamítáme: půlka přerušená po deseti hodinách začíná od nuly, a to ji 2026-10-29 stálo celý výsledek.

**9 — Bod, který má řádek, se znovu nesoudí; řádek `in progress` je `crashed`.** Volíme, slovy .NET půlky: bod s řádkem se znovu neprochází a bod, na kterém dřívější běh zemřel — jeho řádek zůstal `in progress` —, je `crashed` na stupni, kterého dosáhl, jako když rodič .NET půlky najde mrtvého potomka. Platí pro druh `series`: experimentální běh nad maticí je krátký a nad týmž adresářem se pouští znovu právě proto, aby řádky napsal znovu.

## Rozhodnutí

**Volíme varianty 1, 2 v užším tvaru, 5, 7 a 9.**

- **Instance `test_app`, `test_app_ldbc`, `test_app_cases` a `test_app_dry` mají v compose `mem_limit` 2 GB a `restart: unless-stopped`.** Bod, který chce víc, ukončí instanci v jejím kontejneru, ne stroj; restart ji vrátí pro další bod. Dva gigabajty jsou dvojnásobek největší změřené haldy stropového bodu do javového cíle a jediné číslo, se kterým součet mezí vyjde.
- **Javová půlka běhu mezí pouští `test_db` s `DATABASE_MAX_SERVER_MEMORY_MB=2048`**, jedinou instanci, kterou překládá, a sadu s `--no-deps`; záznam to říká, jako to říká záznam experimentálního běhu.
- **Bod, jehož překlad do cíle .NET půlka nedokončila** (`too slow`, `crashed` nebo `hung` v `series-points.tsv` pod týmž identifikátorem), **se instanci neposílá**; jeho řádek je stupeň 0 s výsledkem .NET půlky a větou, že nebyl poslán.
- **Překlad má 35 s, bod 30 minut.** `too slow` bez odpovědi do 35 s; `crashed` bez odpovědi vůbec, pak čekání na instanci; `hung` po třiceti minutách na dosaženém stupni s opuštěným vláknem; `threw` na dosaženém stupni, když cesta vyhodí.
- **Půlka pokračuje, kde skončila.** Bod druhu `series`, který má v `java-tool-rows.tsv` řádek, se znovu nesoudí; řádek `in progress` z dřívějšího běhu je `crashed` na dosaženém stupni; řádky procházky s druhým katalogem zůstávají jako dosud.

Slova výsledků jsou slova .NET půlky (`SeriesPoint`), aby souhrn četl obě půlky jedním slovníkem; bod, který javová půlka neposlala, souhrn z místa, kde se zastavila cesta, vyjímá, protože ho vyjímá už jako bod, kde se zastavil překlad.

## Důsledky

- **Kód:** `experiment/ExperimentRunTest` — plán běhu (`plan`: co se soudí, co zůstává), `judgeBounded` a opuštění bodu, `threw`, zpracování `ToolApi.NoAnswer`; `foreign/ForeignVerdict` — `stopped`, `stoppedAs`, `parse`, slova `TOO_SLOW`, `HUNG`, `CRASHED`, `THREW`; `tool/ToolApi` — `convert` s dobou čekání a `NoAnswer`; `docker-compose.yml` — `mem_limit` a `restart` čtyř instancí; `experiment/ExperimentRunPlanTest` drží plán bez instance a databáze.
- **Dokumentace:** [`architecture.md`](../architecture.md) §6.2, řádek *běh mezí*; `ORMConvertor/README.md`, *The limits run*; [`runs/README.md`](../runs/README.md), *Druhy běhů* a *Metodika*; položka javové půlky je od teď práce, ne rozhodnutí.
- **Co záznam uvidí jinak:** body, které .NET půlka do javového cíle nepřeložila, mají řádky javové půlky na stupni 0 s jejím výsledkem; instance ukončená mezí paměti je řádek `crashed` a stroj žije; bod nad třicet minut je `hung` a půlka jde dál; půlka přerušená zvenčí se pouští znovu nad týmž svazkem a dokončí zbytek.
- **Co rozhodnutí neurčuje:** proč se body v instanci 2026-10-29 hromadily — s těmito mezemi je další pád instance řádkem a její log důkazem; co javac a Hibernate potřebují nad 800 entitami (mez sady zůstává 6 GB, neměřená); a nad kterým stromem javová půlka záznam 2026-10-29 doplní — to vede otevřená položka.
