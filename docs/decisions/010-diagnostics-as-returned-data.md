# 010 — Diagnostika jako vrácená data, ne výjimka

Datum: 2026-08-12
Stav: revidováno
Požadavky: F5, F11, F14, T3, S6
Podklad: JSS článek, §3.3 a pravidlo E8

## Kontext

Nástroj dnes mlčí. Převod do Dapperu zahodí klíče, vztahy i mapování sloupců a vrátí úspěch; entita bez identifikátoru projde do mapování NHibernate, které se nedá spustit; neznámý generátor NHibernate se přeloží stejně jako `assigned`, takže vlastní implementaci zapsanou názvem typu nelze od legitimní hodnoty odlišit. Vlastnost, kterou zná jen XML mapování a ne entitní třída, shodí generování výjimkou `NotSupportedException` s textem `None` — z něj se nedozvíš ani o kterou entitu šlo.

Rozhodnutí [004](004-unexpressible-facts-as-warnings.md) přitom už v červenci stanovilo, že nevyjádřitelný fakt má nést varování. Mechanismus pro ně nevznikl a seznam faktů, kterých se to u Dapperu týká, žije dodnes jen jako text ve srovnávací analýze.

Mezitím přibyly dvě věci, které na tento chybějící mechanismus čekají. Rozhodnutí [009](009-target-framework-descriptor.md) zavedlo deskriptor cílového frameworku se třemi stavy — *vyžaduji*, *umím vyjádřit*, *neumím vyjádřit* — takže je poprvé strojově zapsáno, co se kde ztratí; konzumenta v produkčním kódu ale deskriptor nemá. Rozhodnutí [008](008-database-as-metadata-source.md) uložilo evidovat původ každého faktu a hlásit rozpor mezi zdrojem a databázovým katalogem; ani jedno nemá kam ústit.

**Kanál chybí i technicky.** `ConversionHandler.Convert` vrací `List<ConversionSource>`, tedy pouze artefakty. Neexistuje místo, kudy by se cokoli dalšího dostalo ven, takže tohle je první rozhodnutí v řadě, které se nedá provést uvnitř wrapperů — sahá do orchestrace a do REST API.

**Situace se liší povahou, ne mírou.** Když si projdeme, co by se hlásilo, vyjdou pěti různé případy:

1. fakt nemá zdroj ani katalog,
2. cílový framework fakt neumí vyjádřit,
3. zdroj a katalog si odporují,
4. převod fakt zúží — `smalldatetime` projde přes NHibernate jako `datetime2`, jiná přesnost i rozsah,
5. cíl fakt vyžaduje a nemá ho nikdo.

Případ 1 z velké části řeší přístup k databázi podle rozhodnutí 008. Případy 2 až 4 ale nejsou o chybějící informaci: informace je k dispozici a **vědomě se zahazuje nebo přepisuje**, protože se frameworky liší v tom, co unesou. To žádný přístup k datům neodstraní; je to vlastnost překladu mezi nestejně expresivními cíli. Případ 5 je jediné skutečné selhání.

**Co k tomu říká článek a co ne.** Článek pro tuhle oblast používá pojem *validace* a dává jí značnou váhu: v §3.1 je validace přeložených artefaktů proti požadavkům cílového frameworku jednou ze tří schopností, jejichž absenci článek označuje za jádro problému; §5 ji uvádí jako jednu ze tří věcí, které mezireprezentace umožňuje; §9 jako jeden ze tří cílů prototypu; Tabulka 5 v §11 řadí práci do řádku s rule-based translation a validačními kontrakty. Formálně ji zakotvuje pravidlo **E8**, které definuje úplnost mapování jako konjunkci tabulky, primárního klíče a platnosti všech vlastností. §3.3 přitom výslovně žádá ověřit přítomnost povinných metadat **před generováním**, zatímco §4.3 a §7.2 nechávají frameworková omezení až na okamžik emise (pravidla E10 a Q14).

Hlášení toho, co se při překladu ztratilo, ale článek nežádá nikde. Q14 dovoluje nepodporovanou konstrukci přepsat nebo dekomponovat a o hlášení mlčí, §3.2 vyjímá sémantickou ekvivalenci ze záběru. Požadavek na hlášení ztrátovosti pochází výhradně ze zadání vedoucího, a to ze čtyř nezávislých míst: **F11** („nepodporované konstrukce nesmí být potichu vynechány"), **F5** (hlášení konfliktů, ověřované nejméně čtyřmi scénáři), **T3** (metrika úplnosti přenesených mapovacích vlastností) a **S6**, který v záznamu běhu jmenuje varování i zdroje metadat.

## Zvažované varianty

### A — Nechat stav a spolehnout se na úplnost vstupů

Kdyby byl vždy k dispozici zdrojový kód i databáze, většina mezer by nevznikla a hlásit by nebylo co.

Předpoklad ale neplatí, a to z vlastního rozhodnutí: podle 008 je připojení k databázi nepovinné a překlad bez něj nesmí selhat. Katalog navíc uzavírá jen databázovou polovinu mezireprezentace, ne jazykovou, a u schématu spravovaného migracemi neřekne nic. Hlavně ale varianta neřeší případy 2 až 4, které o úplnosti vstupu nejsou vůbec.

### B — Výjimka

Nejlevnější mechanismus a na případ 5 sedí.

Selhává na třech místech. Případy 2 až 4 jsou **úspěšné** převody — není na čem výjimku vyhodit, protože nic neselhalo a výstup je platný; kdyby se házela, nástroj by odmítal generovat Dapper vůbec. F11 v ověření žádá, aby neúspěšný překlad strukturovanou diagnostiku **vrátil**, a výjimka není vrácená hodnota. A při dávkovém vstupu podle F14 zabije jedna vadná entita celý projekt o deseti entitách.

### C — Zápis do logu

Diagnostika by se psala do aplikačního logu a výstup převodu by zůstal beze změny.

Nevyžaduje zásah do API. Uživatel webového rozhraní se ale k logu serveru nedostane, takže hlášení nedorazí tomu, komu je určeno, a T3 by muselo metriku úplnosti dolovat z textového logu místo ze strukturovaných dat.

### D — Vrácená data, jeden druh záznamu

Návratový typ převodu nese artefakty i záznamy; selhání zůstávají výjimkou, záznamy pokrývají jen ztrátovost.

Splňuje F5, T3 i S6 a je to nejmenší možná změna. Naráží ale na doslovné znění F11 — neúspěšný překlad má diagnostiku vrátit — a na F14, protože výjimka dál ukončí dávku.

### E — Vrácená data, dva druhy záznamu ve dvou okamžicích

Jako D, ale i selhání se vrací jako záznam, a záznamy vznikají ve dvou fázích: kontrola úplnosti proti deskriptoru **před** generováním, záznamy o ztrátě **při** emisi.

## Rozhodnutí

**Varianta E: diagnostika je vrácená data. Úplnost se ověřuje před generováním, ztrátovost se zaznamenává při emisi, a oba druhy záznamu plynou z deskriptoru cílového frameworku.**

**Vrací se, nehází se.** F11 to v ověření říká doslova: neúspěšný překlad *vrátí* strukturovanou diagnostiku s frameworkem, artefaktem, chybějící vlastností a důvodem selhání. Výjimka to nesplňuje ani formálně, ani prakticky — u dávkového vstupu podle F14 by jedna vadná entita ukončila zpracování ostatních, ačkoli s nimi není nic v nepořádku. Vrácený záznam dovolí přeložit, co jde, a o zbytku podat zprávu.

**Dva druhy, ne stupnice závažnosti.** Rozlišení nepotřebuje nový výčet, protože už existuje v deskriptoru z rozhodnutí 009. Kategorie ve stavu *vyžaduji*, kterou nikdo nedodal, je **selhání**: artefakt nevznikne. Kategorie ve stavu *neumím vyjádřit*, kterou zdroj nesl, je **ztráta**: artefakt vznikne a je platný, jen chudší než vstup. Škála typu „info – warning – error" by k tomu nic nepřidala a zavedla by hodnoty, pro které nemáme kritérium.

**Dva okamžiky, protože je tak má článek.** §3.3 žádá ověřit přítomnost povinných metadat před generováním; §4.3 a §7.2 nechávají frameworková omezení na okamžik emise. Kontrola úplnosti je tedy **brána**: projde se deskriptor, vyberou se kategorie ve stavu *vyžaduji*, a chybí-li po doplnění z katalogu některá z nich, artefakt se negeneruje a vrátí se záznam o selhání. Formální oporou je E8, které úplnost definuje jako konjunkci tabulky, klíče a platnosti vlastností; deskriptor tuhle definici zpřesňuje pro konkrétní cíl. Záznamy o ztrátě naopak vznikají až při emisi, kdy je vidět, co se do výstupu opravdu nedostalo.

Rozdělení má i praktický důvod: odmítnout mapování NHibernate bez identifikátoru má smysl dřív, než se napíše půlka artefaktu.

**Většina záznamů vzniká mechanicky.** Builder je nemusí vypisovat ručně: průnik faktů, které mezireprezentace nese, s kategoriemi ve stavu *neumím vyjádřit* je seznam ztrát, a rozdíl mezi kategoriemi ve stavu *vyžaduji* a tím, co je v mezireprezentaci, je seznam selhání. Deskriptor tím dostává prvního konzumenta v produkčním kódu; dosud ho četl jen test.

**Původ faktu je událost, ne stav v mezireprezentaci.** Rozhodnutí 008 ve svých důsledcích uvádí, že mezireprezentace musí evidovat původ každého faktu, a zdůvodňuje to tím, že jinak nemá F5 ani F11 co hlásit. Toto rozhodnutí ten důsledek **zužuje**: původ se vydává jako záznam v okamžiku, kdy fakt dodá katalog, a v modelu se neukládá. Důvodů je několik. S6 chce zdroje metadat ve strojově čitelném **záznamu běhu**, což je log, ne pole v modelu. Konzumenta, který by se modelu ptal „odkud je tahle hodnota", se nepodařilo pojmenovat: idempotentní doplňování z 008 si vystačí s testem na prázdnou hodnotu, rozpor se vyhodnocuje v okamžiku dodání a konvence cílového frameworku se do modelu nikdy neukládá, protože ji builder aplikuje až při generování. A alternativa by znamenala obalit každou z šesti hodnot `PropertyMap` typem s původem, tedy podstatný zásah do modelu bez odběratele.

Je to změna volby, ne její doplnění, takže se 008 nereviduje, ale nahrazuje: platnou verzi nese rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), které z 008 přebírá všechno ostatní — prioritu zdrojů, vítězství zdroje nad katalogem i požadavek, aby se rozpor ohlásil.

**Poptávka řídí, co se použije, ne co se načte.** Tohle je podmínka, bez které je hlášení konfliktů z F5 nedosažitelné. Rozhodnutí 008 staví poptávku do katalogu z toho, co v mezireprezentaci chybí — jenže fakt dodaný zdrojem v ní nechybí, takže by se na něj katalogu nikdo nezeptal a rozpor by nemohl vzniknout. Dotaz na katalog přitom vrací sloupce tabulky vcelku a omezovat ho na podmnožinu nic neušetří. Načítá se tedy celý sloupcový obraz dotčených tabulek, poptávka rozhoduje o tom, které fakty se z něj do mezireprezentace zapíšou, a hodnoty, které zdroj už nese, se porovnají: shodují-li se, neděje se nic, liší-li se, vydá se záznam o konfliktu a platí hodnota ze zdroje.

**Výjimky nemizí.** Zůstávají pro chyby programu — nepodporovaný cílový framework, poškozený vstup, který nelze naparsovat. Hranice je v tom, zda jde o stav, se kterým návrh počítá a o kterém má uživatel dostat zprávu, nebo o situaci, která nastat neměla. Diagnostika je pro první, výjimka pro druhé.

## Důsledky

**Návratový typ převodu se mění.** `ConversionHandler.Convert` musí vracet artefakty i záznamy. Změna se propíše do REST API. Frontend se v tomto kroku **nemění** — model a API jsou předpokladem čtení databázového katalogu, uživatelské rozhraní ne, a `wwwroot` je commitnutý build, jehož změna vyžaduje ruční rebuild Angularu. Zobrazení diagnostiky v UI zůstává jako otevřená položka spolu se zbytkem frontendových úprav.

**Rozhodnutí 004 dostává mechanismus.** Varování o nevyjádřitelných faktech přestává být příslibem a stává se chováním. Závazný seznam už podle 009 žije v deskriptoru; text ve `docs/analysis/` zůstává jako podklad a zdůvodnění.

**Pád na chybějícím jazykovém typu se stává záznamem.** `CLRTypeConvertor.ToString` dnes na `CLRType.None` vyhodí `NotSupportedException` uprostřed generování. Nově jde o kategorii, kterou cíl vyžaduje a nikdo nedodal, tedy o záznam o selhání s entitou i vlastností.

**Čtečka katalogu vydává záznamy dvou dalších druhů** — o původu doplněného faktu a o konfliktu se zdrojem. F5 žádá testy nejméně pro čtyři konfliktní scénáře: nullabilitu, název sloupce, datový typ a primární klíč.

**T3 dostane data, ze kterých se dá počítat.** Metrika úplnosti přenesených mapovacích vlastností potřebuje strojově čitelný seznam toho, co se nepřeneslo; z logu ani z výjimek by se nesestavila.

**Zúžení uvnitř typového modelu se stane hlásitelným, ale ne hned.** Slévání `DateTime`, `DateTime2` a `SmallDateTime` do jediného typu NHibernate je ztráta ve stejném smyslu jako nevyjádřitelný fakt, jen se odehrává uvnitř převodu typů, ne na hranici frameworku. Aby ji šlo ohlásit, musí být z převodu poznat, že zúžil — to je práce v typovém modelu, ne v diagnostice.

**Co toto rozhodnutí neurčuje:** jaký je úplný katalog kontrol strukturální úplnosti nad rámec toho, co deklaruje deskriptor; syntaktické ověření vygenerovaných souborů, které F11 žádá jako třetí položku; a záznam běhu podle S6 s identifikátorem, provedenými pravidly, verzemi frameworků a výsledky kompilace — ten je širší než diagnostika jednoho převodu a diagnostika do něj bude jen vstupovat.

## Historie

**2026-08-15 — revidováno.** Volba se nemění, doplněn je záznam o tom, kam se propsala. Toto rozhodnutí zúžilo dva důsledky rozhodnutí [008](008-database-as-metadata-source.md) — evidenci původu faktu v mezireprezentaci a odečítání už známých faktů z poptávky do katalogu — a obojí samo pojmenovalo jako změnu volby, aniž by ji kam zapsalo; 008 přitom zůstávalo ve stavu `revidováno` a jeho text dál tvrdil opak. Platnou verzi obou bodů proto nese rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), které v době vzniku tohoto rozhodnutí ještě neexistovalo, a text je o odkaz na ně doplněný. Podnětem byl audit 2026-08-15, nález 1.1.
