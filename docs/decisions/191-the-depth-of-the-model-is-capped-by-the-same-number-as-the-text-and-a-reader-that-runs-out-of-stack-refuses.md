# 191 — Hloubku modelu omezuje totéž číslo jako text: řetězy bez závorek měří model po čtení, CASE až po END a řadu prefixových operátorů počítá text před gramatikou, a čtečka, které dochází zásobník, jednotku odmítne

Datum: 2026-10-27
Stav: platí; nahrazuje výčet toho, co strop počítá (oddíl *Rozhodnutí*, „Počítají se kulaté a složené závorky"), a důsledek *Ohraničí se i emisní strana, aniž bychom na ni sáhli* rozh. 092
Požadavky: F11, S4, S7, S3
Podklad: rozhodnutí [092](092-input-nesting-depth-capped-before-the-descent.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [190](190-a-point-of-the-limits-run-that-does-not-finish-within-thirty-minutes-is-hung.md); [`threat-model.md`](../threat-model.md), hrozba 2; sondy na MIS2 z 2026-10-26; procházka řad `expression`, `unions`, `case` a `not` se stropem vypnutým na hostiteli MIS3 (Windows, sestavení Debug) a v kontejneru `tests` (Linux) z 2026-10-27 nad `689e58f` s úpravami procházky podle rozh. 190 (níž, *Co měření ukázalo*)

## Kontext

Rozhodnutí 092 dalo nástroji strop hloubky 128 a vynucuje ho nad tokeny dřív, než parser sestoupí: počítá kulaté a složené závorky. Opíralo se o to, že hloubku dělají jedině vypsané závorky a poddotazy — řetěz `a or b or c` parser čte smyčkou se zploštěním — a že strom, který prošel zastropovaným parserem, je hluboký nejvýš tolik, kolik strop dovolil, takže se tím ohraničí i emisní strana.

Pro AND a OR to platí, pro jiné řetězy ne. Součet `a + b + c` je v modelu binární strom hluboký jako řetěz (`QueryExpression.Binary`, rozhodnutí 107), CASE v ELSE předchozího CASE je CASE uvnitř CASE, `NOT NOT …` je `NotCondition` uvnitř `NotCondition`, `UNION` po `UNION` je množinová operace uvnitř množinové operace — a text žádný z nich nepíše se závorkou. Strop je nevidí, model je drží hluboké, jak dlouhé jsou, a každý rekurzivní průchod po čtení jde do té hloubky. Sondy na MIS2 to ukázaly v ručně psaných vstupech: součet 1024 členů shodí proces ve visitoru cíle Dapper, CASE vnořený 1024krát ze zdrojů HQL, JPQL i T-SQL, holé `NOT` 2048krát ze zdroje T-SQL — přetečení zásobníku, které .NET nezachytí, uprostřed požadavku a bez záznamu.

### Co měření ukázalo

Rozhodnutí potřebuje vědět, **kde** zásobník přeteče, protože na tom závisí, co ho může zastavit. Procházka řad se stropem vypnutým (rozh. 190) zapisuje do řádku pádu metodu, kterou runtime vypsal:

| Kde zásobník přetekl | Tvary (hostitel, Windows) | Hloubka pádu, hostitel | Hloubka pádu, kontejner |
|---|---|---|---|
| gramatika ScriptDom (`antlr.Parser.match`) | CASE ze zdrojů Dapper a MyBatis | 1024, z MyBatisu do EF Core 512 | 4096 |
| naše čtečky: LINQ `ReadBinary`/`ReadOperand`, T-SQL `ReadCondition`, HQL a JPQL `ParsePrimaryOperand` | součet z EF Core, NOT z T-SQL, CASE z HQL a JPQL | 1024–2048 | nad 4096 |
| průchody po čtení: `QueryExpression.Rebuilt`, `ConditionIsWellFormed`, visitory | součet ze zdrojů T-SQL, HQL, JPQL; NOT z LINQ, HQL, JPQL | 1024–2048 | nad 4096 |

Vedle toho ukázala, co se zastaví samo: LINQ čte řetěz `?:` jako jeden CASE s tolika větvemi, takže je v modelu plochý, a Roslyn odmítne výraz od 2048 větví sám (`CS8078`); řetěz `UNION` do 256 výběrů prošel všude. Mez **cílových** frameworků je jiná otázka a patří do třídění běhu mezí; jeden nález z ní je ale pro tohle rozhodnutí podstatný: vlastní parser HQL NHibernatu přeteče na hostiteli už při 128 článcích součtu, CASE i NOT (64 projde), v kontejneru při 1024.

Kontejner má zásobník několikanásobně hlubší než hostitel — Windows dávají vláknu 1,5 MB, Linux z `ulimit` víc —, takže táž mez leží na obou platformách jinde a mez vyslovená jedním číslem musí stát pod tou nižší.

**Je třeba rozhodnout**, čím mez vyjádřit — hloubkou modelu, klíčovými slovy ve stropu 092, nebo délkou řetězu —, zda k ní přidat druhou hráz převedenou na `Failure`, a kde mez stojí vůči oběma platformám.

## Zvažované varianty

### 1 — Hloubka modelu po čtení

Jedna míra pro všech pět jazyků: po čtení, než začne kterýkoli další průchod, se změří hloubka modelu dotazu a proti `ParseLimits.MaxNestingDepth` se odmítne. Jazykově nezávislá, na jednom místě a s týmž číslem. Sama nestačí: přichází **po** čtení, a tři ze sedmi míst pádu z tabulky jsou čtení samo — gramatika ScriptDom a naše čtečky sestupují do hloubky řetězu dřív, než model existuje. Bereme ji jako jednu ze tří částí.

### 2 — Klíčová slova ve stropu 092

Rozšířit sčítání nad tokeny o konstrukce, které se v textu vnořují bez závorky. Pro CASE je to přirozené: `CASE` otevírá a `END` zavírá stejně jednoznačně jako závorka — v SQL, HQL a JPQL; v C# a Javě je `case` návěští větve přepínače bez `END`, a tam se počítat nesmí. Pro řadu prefixových operátorů (`NOT NOT`, `!!`, `- -`) taky: každý další je o úroveň hlouběji, dokud řada trvá, a řadu dvou set takových tokenů nikdo nenapíše. Pro binární řetěz (`a + b + c`) ale ne: operátor je stejný v řetězu i mimo něj a jeho hloubku určuje gramatika, ne tokeny. Je to **jediné** místo, které předchází gramatice ScriptDom, takže CASE z T-SQL jinak zastavit nejde. Bereme ji jako druhou část.

### 3 — Délka řetězu v textu

Počítat operátory na jedné úrovni závorek jako délku řetězu. Zamítáme: v každém jazyce jiný výčet operátorů, s rozdílem mezi binárním a unárním mínusem, s řetězením `+` v T-SQL a `||` v HQL — heuristika, která se splete tam, kde ji nikdo neotestoval, a to, co měří, měří varianta 1 přesně.

### 4 — Druhá hráz: čtečka, které dochází zásobník, odmítne

`RuntimeHelpers.EnsureSufficientExecutionStack` na vstupu do rekurzivních metod našich čteček vyhodí zachytitelnou výjimku dřív, než zásobník přeteče, a orchestrace z ní udělá `Failure` jednotky. Mez není vyslovená číslem — leží tam, kde dojde zásobník, tedy na každé platformě jinde —, ale nikdy nekončí pádem procesu, a pokrývá i tvar sestupu, na který při psaní stropu nikdo nepomyslel. Do gramatiky ScriptDom ani Roslynu ji vložit nejde; Roslyn má vlastní (`InsufficientExecutionStackException` jsme v sondě viděli) a ta se zachytí stejně. Bereme ji jako třetí část.

### 5 — Větší zásobník

Překládat na vlákně s velkým zásobníkem. Zamítáme z důvodu rozhodnutí 092: mez posune, nenajde; a vlákno, kterému dojde i velký zásobník, ukončí proces stejně.

## Rozhodnutí

**Volíme varianty 1, 2 a 4 jako tři části jednoho stropu.** Číslo zůstává jedno — `ParseLimits.MaxNestingDepth`, výchozí 128, provozovatelem nastavitelné i vypnutelné jako dosud (rozhodnutí 092) —, a měří se jím tři věci, každá tam, kde ji vidět jde:

1. **Text, před gramatikou** (`NestingDepthGuard`, rozšíření rozhodnutí 092): kromě kulatých a složených závorek ve všech pěti jazycích se počítá **CASE až po svůj END** v SQL, HQL a JPQL a **řada prefixových operátorů** (`NOT`, `!`, `-`, `+`, `~`) ve všech pěti, každý operátor řady o úroveň hlouběji, dokud řada trvá. Překročení je `Failure` s řádkem a sloupcem tokenu jako dosud.
2. **Model, po čtení** (`ModelDepth`, `ConversionHandler`): hloubka modelu dotazu je počet výpočtů — operace, volání, CASE, okno, agregace do seznamu —, negací a množinových operací vnořených do sebe na nejhlubší cestě; porovnání, AND a OR nepřidávají nic (porovnání řetěz nemá, AND a OR jsou plochý seznam), poddotaz také ne (jeho závorky počítá text), ale cesta jím pokračuje. Měří se vlastním zásobníkem na haldě, ne rekurzí, hned po čtení a dřív než poptávka katalogu, brána i kroky, a dotaz hlubší než strop je `Failure`, který jmenuje hloubku: model polohu nemá. Odmítnutý dotaz zahodí, co parser přečetl, aby do něj nesestoupil žádný průchod.
3. **Čtečka, které dochází zásobník** (`ReaderStack`): rekurzivní metody čtečky T-SQL, LINQ, HQL a JPQL se na vstupu ptají, zda zásobník unese další úroveň, a orchestrace zachycenou `InsufficientExecutionStackException` — vlastní i Roslynovu — mění v `Failure` jednotky. To není mez, ale hráz: vstup, který prošel prvními dvěma částmi, na ni nenarazí; narazí na ni jen vstup na instanci se stropem vypnutým, nebo tvar, na který strop nepomyslel.

**Kde mez stojí.** Nejnižší změřený pád je 512 (CASE z MyBatisu do EF Core v gramatice ScriptDom, hostitel); 128 leží čtyřikrát pod ním na Windows a třicetkrát pod nejnižším pádem v kontejneru. Rezerva je menší než osminásobek, který rozhodnutí 092 změřilo pro závorky, a je to nejtěsnější místo; dolní mez se nehýbe — ručně psaný dotaz se v řetězu bez závorek vnořuje v jednotkách, strojově složený v desítkách.

**Proč tři části, a ne jedna.** Jediné pravidlo, které by stačilo samo, by muselo předcházet všem sestupům, a takové místo je jedině text — ten ale binární řetěz nepozná. Model pozná všechno, ale až po čtení. Proto počítá text, co text vidí a co by jinak zastavilo jedině gramatiku, kterou nemáme v moci; model, co text nevidí; a hráz drží, co obě míry minou. Číslo je pro všechny tři jedno, takže provozovatel nastavuje jednu věc a záznam běhu (`maxNestingDepth`, rozhodnutí 092) vypovídá o všech třech.

## Důsledky

- **Kód:** `NestingDepthGuard.FirstBeyond` s parametrem `caseNests` (zapnutým u `SqlText`, parseru HQL a JPQL) a řadou prefixových operátorů; `ModelDepth` a `AbstractQueryBuilder.ModelDepth`/`Discard`; měření v `ConversionHandler` po čtení; `ReaderStack.Ensure` v `SqlQueryReader.ReadCondition`/`ReadOperand`, `LinqQueryParser.ParseCondition`/`ReadOperand`, `ParseNot`/`ParsePrimaryOperand` obou parserů HQL a JPQL; záchyt v obou průchodech `ConversionHandler` s jedním záznamem na jednotku.
- **Testy:** `Combined/ModelDepthCapTest` — součet a řetěz množinových operací hluboký 128 projde a o článek hlubší je `Failure` s hloubkou; CASE hluboký 128 v SQL, HQL a JPQL projde a o úroveň hlubší je `Failure` s pozicí; řetěz o 1024 článcích (množinové operace 256), na kterém proces se stropem vypnutým končil, je `Failure`, a to, že test doběhne, je důkaz; CASE se počítá jen v jazyce dotazů a řada prefixových operátorů jen, dokud trvá; vypnutý strop vypíná i hloubku modelu. Výš sada nesahá, ze stejného důvodu jako u rozhodnutí 092.
- **Řady běhu mezí:** řetězy, které strop od teď počítá, jsou řadami se stropem vypnutým a přibývají osy `case` a `not` (rozh. 190).
- **Hrozba 2** v [`threat-model.md`](../threat-model.md): řetěz bez závorek už proces neshodí; zbývá limit počtu a velikosti jednotek a artefakt exponenciální v hloubce vstupu.
- **Co rozhodnutí neřeší:** cílový framework, který nepřijme artefakt hluboký pod stropem. Parser HQL NHibernatu přeteče na Windows při 128 článcích řetězu, tedy na mezi, kterou nástroj pouští; to je otevřená položka, ne část tohoto stropu, protože jde o mez jednoho cíle, ne o vstup.
