# 101 — Join po asociační cestě se odvozuje ze vztahu mezireprezentace; bez sloupců se odmítá jmenovitě

Datum: 2026-09-29
Stav: platí
Požadavky: F7, F8, F9, F11, T1, T2
Podklad: rozhodnutí [001](001-entity-reference-by-name.md), [005](005-many-to-many-as-explicit-junction-entity.md), [015](015-mapping-fact-completion-from-the-catalog.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md) a [096](096-a-rule-of-the-paper-is-cited-where-it-argues.md); revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nález 6.2; otevřená položka „Join po asociační cestě jde odvodit ze vztahu, a neodvozuje se"; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), řádek *Explicitní LEFT a RIGHT JOIN*

## Kontext

Pravidlo **Q7** článku odvozuje podmínku implicitního joinu z metadat vztahu — `JoinCondition = FK(levá) = PK(pravá)` — a je jediné z pětadvaceti pravidel, které nástroj nesplňuje. Rozhodnutí 096 to přiznalo jako vyslovenou mezeru: `from Customer c join c.orders o` v HQL i v JPQL končí od rozhodnutí 070 záznamem `Failure`, protože čtečka cestu nepřečte. Odmítnutí je správné potud, že dotaz bez joinu vrací jiné řádky — ale přečíst se ta cesta **dá**. Mezireprezentace nese všechno, co je k tomu třeba: `Relation` na `EntityMap` s `SourceNavigationProperty`, tedy se jménem vlastnosti, kterou cesta jmenuje; `ColumnPairs` s uspořádanými dvojicemi sloupců i pro kompozitní klíč; a `Role`, která říká, která strana fyzický cizí klíč drží — u `Owning` zdrojová entita, u `Inverse` cílová (`architecture.md`, §4.3).

Není to okrajový tvar. Join po asociační cestě je tvar, kterým se join v JPQL **píše**: specifikace zná jen `join alias.asociace`, a join na entitu s vypsanou podmínkou (`join Customer c on …`) je rozšíření obou implementací nad ní. Řádek *join* v matici kategorií má proto u javových zdrojů dnes tu vzácnější podobu, a případová studie T1 nad kódem Hibernate by narazila na odmítnutí u většiny joinů. Přitom mezi rozhodnutími o slovníku stojí tohle první proto, že mění jen čtení: cíle, které cestu neumějí — Dapper, MyBatis, LINQ —, dostanou z odvozené podmínky běžný join, a JPA cíle už dnes vypisují join jako entitu s podmínkou.

Dvě otázky zůstávaly otevřené. **Odkud se vztah vezme, když ho zdroj nevyslovil:** `c.orders` je jméno vlastnosti, ne tabulky, takže se musí spárovat se vztahem entity za aliasem, a ta v převodu být nemusí — převod jediné dotazové jednotky bez entit ji nemá vůbec. A **co se stane, když se vztah najde, ale `ColumnPairs` jsou prázdné**, protože je nikdo nedoplnil ani ze zdroje, ani z katalogu, ani z druhé entity převodu. Obě odpovědi musí platit pro HQL i JPQL zároveň, protože cesta je v obou týmž tvarem a oba parsery ji dnes odmítají touž větou.

K tomu, co parser dotazu v okamžiku čtení v ruce má: orchestrace mu předává mapy převodu **po** fázi rozresolvování rozhodnutí 001 a **po** doplnění z katalogu (rozhodnutí 015) — entitní builder se staví dřív, než se čte první dotaz —, takže `ColumnPairs` jsou vyplněné všude, kde je zdroj vyslovil (`@JoinColumn`, `column` u `<many-to-one>` nebo `<key>`) nebo kde je znal katalog. Prázdné zůstávají tam, kde zdroj sloupec nevyslovil a katalog nebyl po ruce, a u vztahu N:M vždycky, protože ten podle rozhodnutí 005 stojí na junction entitě a sloupce nese ona.

## Zvažované varianty

### 1 — Nechat odmítnutí

Dnešní stav a nejmenší zásah; rozhodnutí 070 ho zvolilo pro každou konstrukci, kterou čtečka nepřečte. Zamítáme: 070 odmítá to, co model *nenese*, a cesta do té množiny nepatří — model ji nese celou, jen ji nikdo nečte. Přiznaná mezera proti článku (096) je lepší než tichá, ale je to pořád mezera v pravidle, které je splnitelné, a v tvaru, kterým se join ve dvou ze šesti jazyků píše.

### 2 — Odvodit podmínku ze vztahu, a kde sloupce chybí, dosadit konvenci frameworku

JPA sloupec cizího klíče bez `@JoinColumn` odvozuje jako `<vlastnost>_<odkazovaný sloupec>`, NHibernate jako jméno vlastnosti. Parser dotazu by tedy mohl podmínku dopsat i nad prázdnými páry. Zamítáme ze dvou důvodů. Za prvé je to tvrzení o *mapování*, vyslovené na špatném místě: která konvence platí, ví jedině parser zdrojového frameworku, který mapovací artefakt četl (rozhodnutí 015 a 067 — konvenci materializuje parser, protože jen on ví, odkud vstup pochází), a dotazový parser by ji musel znát podruhé, pro každý framework zvlášť. Za druhé by sloupec dosazený v dotazu neodpovídal sloupci, se kterým pracuje mapovací větev téhož převodu: entitní builder páry nevyplnil, takže cílové mapování cizí klíč nevypíše, a dotaz by joinoval přes sloupec, který v cílovém artefaktu nikdo nedeklaruje. Kde se konvence má materializovat, je otázka kritéria z rozhodnutí 067 nad entitním parserem, ne nad čtením dotazu.

### 3 — Nést cestu v modelu a nechat odvození builderům

`JoinInstruction` by dostal druhý tvar — navigaci místo podmínky — a každý ze šesti builderů by si podmínku odvodil sám, nebo by ji JPA cíle vypsaly zpět jako cestu. Zamítáme: mezireprezentace dotazu je relační (pravidlo Q6, join jako čtveřice s podmínkovým stromem) a Q7 říká právě to, že se cesta *rozresolvuje* metadaty na podmínku, ne že se nese dál. Druhý tvar joinu by odvození rozkopíroval do šesti builderů, z nichž tři — Dapper, MyBatis, LINQ — cestu vyjádřit neumějí a potřebovaly by ji stejně, a JPA cíle by získaly jen jinou podobu téhož joinu: entity join s podmínkou, který dnes vypisují, čtou obě implementace a vrací tytéž řádky (rozhodnutí 065).

### 4 — Odvodit podmínku ze vztahu při čtení; co odvodit nejde, odmítnout záznamem, který jmenuje proč

## Rozhodnutí

**Volíme variantu 4. Parser HQL i JPQL čte `join alias.vlastnost [as] alias2 [with|on podmínka]` jako join na cílovou entitu vztahu s podmínkou odvozenou z jeho `ColumnPairs`, přesně podle pravidla Q7. Kde vztah, jeho cílová entita nebo jeho sloupce nejsou v mapách převodu, artefakt se odmítá záznamem `Failure`, který jmenuje cestu a to, co chybí — nikdy se nehádá.** Mění se jen čtení; model, `AbstractWrappers`, orchestrace ani žádný builder se nemění (S1).

Konkrétně to znamená sedm věcí:

**Vztah se bere z map převodu, přes alias a jméno vlastnosti.** Levá část cesty je alias deklarovaný ve `from` nebo v dřívějším joinu; entita za ním je `EntityMap` z map, které orchestrace parseru předala. Pravá část je jméno vlastnosti a páruje se s `Relation.SourceNavigationProperty` té entity, bez ohledu na velikost písmen, jak parser páruje každé jméno. Cílová entita joinu je `Relation.TargetEntity`, dohledaná jménem mezi mapami převodu (rozhodnutí 001). Alias joinu je ten, který zdroj napsal; nenapsal-li žádný — což specifikace JPQL u `join` nepřipouští a HQL připouští jen u `fetch` —, je aliasem jméno vlastnosti, stejně jako entity join bez aliasu nese jméno entity.

**Podmínka je konjunkce rovností přes `ColumnPairs`, s cizím klíčem vlevo a odkazovaným klíčem vpravo.** Kterou stranu drží cizí klíč, říká `Role`: u `Owning` je to entita za aliasem cesty (`join o.customer c` dává `o.CustomerID = c.CustomerID`), u `Inverse` cílová entita joinu (`join c.orders o` dává `o.CustomerID = c.CustomerID` také — vlevo je vždy strana, která klíč fyzicky nese, doslova podle vzorce `FK(levá) = PK(pravá)`). Jeden pár je holé porovnání, více párů `And` v pořadí párů, které je autoritativní (§4.3); operandy jsou sloupcové operandy s aliasem a jménem sloupce, tedy týž tvar, který parser vyrábí z vypsané podmínky, takže LINQ builder z nich čte klíčové selektory a SQL builder `ON` klauzuli beze změny.

**Podmínka, kterou zdroj k cestě připsal, se ke konjunkci přidává.** HQL `with` i JPQL `on` na asociačním joinu zužují join nad rámec vztahu; přečtou se jako dosud a připojí se za odvozené rovnosti jako další člen `And`. Cíl, který takový join neumí — LINQ join bere jen rovnosti sloupců —, ho odmítne v místě emise touž větou jako entity join s toutéž podmínkou; parser tu nic nového nezavádí. `fetch` zůstává ztrátou se záznamem, jak byl.

**Cesta o víc než dvou částech se odmítá.** `join o.customer.address a` je v JPQL průchod vnořeným objektem a v HQL implicitní join přes mezičlánek; komponenty model nenese (vyňatá oblast 2, §9) a mezičlánek by potřeboval alias, který zdroj nenapsal. Záznam cestu jmenuje a říká, že se čte právě jedna asociace od aliasu.

**Vztah N:M se odmítá.** Podle rozhodnutí 005 stojí N:M na junction entitě se dvěma vztahy N:1 a sloupce nese ona, takže cesta přes něj je dva joiny a alias mezičlánku, který by nástroj musel vymyslet. Záznam vztah jmenuje a říká, že cesta přes N:M by potřebovala dva joiny přes junction entitu; je to vyslovená mez tohoto rozhodnutí, ne mezera — zdroj, který ji potřebuje, ji přinese jako vlastní rozhodnutí.

**Co chybí, se jmenuje, a nic se nehádá.** Čtyři situace, čtyři věty, každá `Failure` kategorie `Join`, protože dotaz bez joinu by vrátil jiné řádky (rozhodnutí 070): entita za aliasem není v mapách převodu (převod jediné dotazové jednotky bez entit — záznam říká, že mapování entity musí být součástí převodu, což je přesně to, co `/required-content` u obou JPA frameworků vyjmenovává); entita vztah toho jména nedeklaruje; cílová entita vztahu není v převodu (katalog třídu nedodá, rozhodnutí 015); a vztah má prázdné `ColumnPairs` — záznam jmenuje vztah a říká, že sloupce cizího klíče nevyslovil zdroj ani nedodal katalog. Dosadit jméno sloupce by bylo přesně to hádání, které rozhodnutí 067 váže na vyslovené tvrzení zdroje, a bylo by v rozporu s mapovací větví téhož převodu (varianta 2).

**Buildery cestu nevypisují.** JPA cíle zapisují join dál jako `join Entita alias on …`, tedy entity join s podmínkou: mezireprezentace navigaci nenese a nést ji nemá (varianta 3), obě implementace ten tvar čtou — javová sada ho ověřuje na 2. a 3. stupni nad záměrně špatným dotazem — a vrací tytéž řádky (rozhodnutí 065). Textový round trip NHibernate → NHibernate nad cestou proto vrací jiný text než vstup: podmínku místo cesty. Je to změna zápisu, ne množiny řádků, a identita textu, kterou §9 nárokuje, se týká jazyka, který builder vydává — cestu nevydává nikdy.

## Důsledky

**Q7 je splněné a rozhodnutí 096 přestává u něj mluvit o mezeře.** 096 platí dál — pravidlo se cituje tam, kde odůvodňuje volbu — a věta v §5, která u Q7 přiznávala mezeru, se mění na popis toho, jak se odvozuje. Všech pětadvacet pravidel článku je tím buď implementováno, nebo vysloveně vyňato.

**JPQL a HQL zdroj vysloví join v tvaru, kterým se píše.** Řádek *join* matice kategorií může u javových zdrojů dostat cestu a diferenční matice s ním; obě jsou položky důkazu v `open-items.md` a čekaly na tomhle rozhodnutí. Sdílená doména `Tests/Database/QueryShapes` dnes žádný vztah nedeklaruje, takže cesta se do ní dostane s prací na kategoriích ve sdílených souborech, ne tady.

**Dotaz bez mapování entity se do žádného cíle nepřeloží — a je to vyslovené.** Jediná dotazová jednotka HQL nebo JPQL s cestou dnes končí záznamem, který říká, co dodat. To je viditelná cena varianty 4 a je poctivá: entity join s vypsanou podmínkou se přeloží dál i bez map, cesta bez map nemá z čeho vzniknout.

**Zdroj JPA bez `@JoinColumn` a bez katalogu cestu nepřeloží.** Entitní parser JPA sloupec cizího klíče bez anotace nematerializuje, takže `ColumnPairs` zůstanou prázdné a cesta se odmítne s větou o chybějících sloupcích; s katalogem po ruce je fáze doplnění vyplní (rozhodnutí 015). Zda konvence `<vlastnost>_<odkazovaný sloupec>` obstojí v obou testech rozhodnutí 067 a má se materializovat, je otázka entitního parseru a vlastní práce, kterou `open-items.md` zapisuje; tohle rozhodnutí na ní nestojí.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** přibývá schopnost čtení, žádný dosavadní výstup se nemění — co bylo odmítnuté, se buď přeloží, nebo odmítne s přesnější větou.

**Testy.** U obou parserů: vlastnící cesta N:1 dává join s podmínkou `FK = PK` a alias zdroje; inverzní cesta 1:N dává touž podmínku s cizím klíčem na straně joinované entity; kompozitní klíč dává konjunkci v pořadí párů; připsaná `with`/`on` podmínka se přidává jako další člen; odvozený join projde do SQL cíle jako `INNER JOIN … ON …`; a čtyři odmítnutí — bez map, neznámá vlastnost, prázdné páry, N:M — pokaždé bez artefaktu a se záznamem `Failure` kategorie `Join`, který cestu jmenuje. Cesta o třech částech se odmítá také.
