# 105 — Dotaz formuluje vlastní poptávku do katalogu: vazbu tabulky, ze které se typuje parametr

Datum: 2026-09-30
Stav: platí
Požadavky: F5, F6, F7–F10, F11, F13, T2, S1, S2, S3
Podklad: rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), [050](050-one-home-for-the-singular-plural-heuristic.md), [066](066-records-attributed-to-the-input-unit.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [083](083-parameter-as-the-fifth-operand-shape.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md) a [089](089-differential-verification-as-the-fourth-level-over-a-query.md); JSS článek, §7.1 a pravidlo E9; první běh diferenční matice přes kategorie T2 (2026-09-30), sekce `ScalarParameter`, `CollectionParameter` a `InListWithABoundValue`

## Kontext

**Skalár parametru se bere ze sloupce na druhé straně porovnání** (rozhodnutí 083). Odvozuje ho sdílená brána šablony dotazového builderu přes mapovací mezireprezentaci: alias sloupce vede k tabulce, tabulka k entitě převodu, entita k vlastnosti a vlastnost ke skaláru. Tabulku k entitě brána váže jen přes **uvedené** mapování — jméno tabulky, které mezireprezentace u entity nese, nebo entitu, která se jmenuje stejně jako tabulka. Konvenci rozhodnutí 050, podle níž se ke `ShopOrderLines` hledá třída `ShopOrderLine`, vykreslování sloupců a typování okamžiku používají, brána ne: skalár v signatuře generované metody je tvrzení o volajícím, a to smí plynout jen z faktu, ne z odhadu.

U pěti zdrojů ze šesti je vazba v dotazu samém nebo vedle něj. LINQ, HQL i JPQL jmenují entity, ne tabulky, takže brána entitu najde podle jména. MyBatis typ parametru uvádí v signatuře rozhraní mapperu (rozhodnutí 084). **Jediný Dapper jmenuje tabulku a vazbu k třídě neuvádí nikde.** POCO `ShopOrderLine { int Quantity; … }` neříká, že bydlí v `ShopOrderLines`, a dotaz

```sql
SELECT * FROM ShopOrderLines AS ol WHERE ol.Quantity >= @minQuantity
```

neříká, že `ol` je `ShopOrderLine`.

**Vazbu dodává katalog, ale jen když si o ni řekne cíl.** Fáze doplnění z rozhodnutí 015 hledá tabulku entity `ShopOrderLine` mezi kandidáty rozhodnutí 050 a najde `ShopOrderLines`. Zapíše ji ale jedině tehdy, když kategorii `TableName` poptává deskriptor cíle. EF Core, NHibernate, Hibernate a EclipseLink ji poptávají, protože tabulku vyjádří. Dapper jako cíl nepoptává nic a fáze se k databázi ani nepřipojí. MyBatis poptává sloupce, ne tabulku, takže tabulku přečte a vazbu nezapíše. Týž dotaz nad týmž katalogem tedy dopadne takto:

| Směr | Parametr `@minQuantity` |
|---|---|
| Dapper → EF Core, NHibernate, Hibernate, EclipseLink | `int minQuantity` |
| Dapper → Dapper | `Failure` kategorie `QueryParameter`, artefakt nevzniká |
| Dapper → MyBatis | totéž |

**Typ parametru přitom není fakt o cíli.** Je to hodnota, kterou volající do dotazu předává, tedy fakt o zdrojovém dotazu. Rozhodnutí 083 to vyslovilo i o odmítnutí: brána odpovídá za všechny cíle stejně, „a platí to i pro Dapper cíl, který by text opsat mohl". Dnes se odpověď podle cíle liší, a ne proto, že by se cíle lišily v tom, co vyjádří — `QueryParameter` uvádí jako vyjádřitelný deskriptor každého z nich. Liší se proto, že o dostupnosti faktu rozhoduje fáze postavená pro jiného odběratele: pro entitní artefakt.

**Uživatel to vidí zřídka, matice pokaždé.** Uživateli se mez ukáže jen ve dvou směrech. Diferenční matice na ni narazí vždy, protože zdrojová varianta kategorie je identitní směr (rozhodnutí 089). Tři parametrické kategorie proto od 2026-09-30 nemají Dapper za zdroj, a to z důvodu, který nespočívá v jazyce, ale v nástroji. §9 přitom tvrdí, že se parametr zdroje Dapper „bez katalogu netypuje", a mlčky tím říká, že s katalogem se typuje vždy.

**Katalog dotaz nepotřebuje pokaždé, a kdy ho potřebuje, se dá určit přesně.** Brána bez něj neobstojí jen tehdy, když platí čtyři věci naráz:

- parametr bere skalár ze sloupce, tedy nejde o počet stránkování, vzorek `LIKE`, porovnání s konstantou ani s `COUNT`;
- sloupec je kvalifikovaný aliasem nebo tabulkou — nekvalifikovaný sloupec najde brána i bez katalogu, protože ho hledá napříč všemi entitami převodu;
- jméno tabulky se liší od jména třídy;
- zdroj tabulku neuvedl.

V reálném kódu nad Dapperem je to běžné. Parametr je norma, jak zapsalo už 083, joiny se bez aliasů nepíšou a tabulky v množném čísle jsou obvyklé — `Sales.Customers` ve WideWorldImporters. Neplatí to ale o každém dotazu a převod bez dotazů to nepotřebuje nikdy. Všechno z toho je po přečtení dotazu známé; odhadovat to není třeba.

Otázka tedy zní, jak vazbu z katalogu dostat k bráně, aniž by se porušilo, co rozhodnutí 015 zavedlo o poptávce a o zápisu do mezireprezentace.

## Zvažované varianty

### 1 — Poptávku nechat cílovou a nárok zúžit

§9 by vyslovilo, že parametr zdroje Dapper se typuje jen do cílů, které vyjádří tabulku, tedy do Dapperu a MyBatisu ani s katalogem ne. Nestojí to žádný kód. Zamítáme, protože by tak jako vlastnost cílů zapsalo něco, co je vlastnost fáze, a potvrdilo rozpor s větou 083, že brána odpovídá za všechny cíle stejně. Matice T2 a F13 by nesly díru natrvalo: tři kategorie bez zdroje, kterého se F6 týká jmenovitě a který rozhodnutí 015 nazývá „nejsilnějším případem pro čtení katalogu". A věta o hotovém 4. stupni by nesla výjimku, kterou nezpůsobuje jazyk, nýbrž nástroj.

### 2 — Rozšířit cílovou poptávku o tabulku, kdykoli zdroj nese dotaz

Podmínka se dá vyhodnotit před fází, protože orchestrace ví, jestli jednotku přijme dotazový parser. Zamítáme ze dvou důvodů a každý stačí sám.

**Zapsala by fakt, který cíl nevyjádří, před stavbou artefaktu, který ho nepotřebuje.** Fáze běží před stavbou entit. `TableName` v mezireprezentaci by entitní builder Dapperu i MyBatisu podle mechanického pravidla ztrát (rozhodnutí 004, 009 a 010) ohlásil u každé entity záznamem `Loss` „The source states TableName and the target has no way to record it". Ten záznam by byl dvakrát nepravdivý: zdroj tabulku neuvedl a nic se neztrácí. Rozhodnutí 015 přitom říká výslovně, že se do modelu „zapíše jen to, co cíl umí vyjádřit", a tahle varianta by to porušila kvůli faktu, který entitní artefakt vůbec nespotřebuje. Obejít to jde jedině tak, že se vazba zapíše až po vydání entit — a tím se varianta mění na variantu 4 s hrubším spouštěčem.

**Je hrubá.** Přečetla by tabulky všech entit i v převodu, jehož dotaz žádný parametr nemá, porovnává ho s konstantou nebo s nekvalifikovaným sloupcem. Stav připojení a čas čtení podle S3 by hlásily čtení tam, kde ho nic nepotřebovalo.

### 3 — Brána se zeptá katalogu sama, když vazbu nenajde

Orchestrace by builderu předala delegáta a brána by ho zavolala ve chvíli, kdy uvedené mapování tabulku nezná. Tahle varianta je nejpřesnější v okamžiku a zamítáme ji, protože je to varianta D rozhodnutí 015 a důvody, které ji tam vyřadily, platí i tady. Zaprvé 015 říká, že „poptávka je datová struktura, ne volání". Právě to dělá z čtení jeden ohraničený krok s vlastním časem (S3) a s jedním místem, kde se řeší nedostupné spojení; volání uprostřed `Build()` by z každé cesty brány udělalo místo, které musí počítat s infrastrukturou. Zadruhé by generování přestalo být funkcí mezireprezentace a začalo by záviset na vstupu a výstupu uprostřed vypisování. A poptávku jako data jde otestovat bez databáze, volání ne.

### 4 — Dotaz formuluje poptávku vlastní a táž komponenta ji obslouží mezi čtením dotazu a jeho stavbou

## Rozhodnutí

**Volíme variantu 4. Vedle poptávky cíle, kterou fáze doplnění obsluhuje před stavbou entit, formuluje každý dotaz poptávku vlastní: vazbu na tabulku u těch tabulek, ze kterých brána rozhodnutí 083 potřebuje typovat parametr a ke kterým mapovací mezireprezentace neuvádí žádnou entitu. Obslouží ji táž komponenta, `DatabaseCatalog`, týmiž pravidly, v ohraničeném kroku mezi čtením dotazu a jeho stavbou. Výsledek se zapíše do mezireprezentace přírůstkově, jak to rozhodnutí 015 předvídá pro druhou poptávku.**

Konkrétně:

- **Poptávku formuluje šablona.** Který parametr se porovnává s kterým sloupcem a v kterém rozsahu platí alias toho sloupce, ví jen průchod brány (rozhodnutí 083). Šablona `AbstractQueryBuilder` proto poptávku vydá jako data: množinu tabulek tak, jak je dotaz jmenuje, i se schématem, pokud ho dotaz píše, které parametr potřebuje a mapování je neváže. Každý cíl ji zdědí a žádný ji neimplementuje (S1). Orchestrace se nedozví nic víc, než že poptávka existuje.
- **Poptávka se obslouží před `Build()`, jednou dávkou.** Orchestrace ji po přečtení dotazu předá komponentě katalogu a teprve potom zavolá `Build()`. Krok je ohraničený a má vlastní čas, který se připočte k času čtení katalogu v převodu (S3). **Když dotaz nepoptává nic, nic se nečte.** Převod bez dotazů a převod, jehož dotazy vazbu nepotřebují, se katalogu nedotknou, stejně jako se ho nedotkne prázdná poptávka cíle.
- **Vazbu hledá katalog týmž pravidlem jako fáze entit.** Kandidátem pro tabulku, kterou dotaz jmenuje, je entita, mezi jejímiž kandidátními jmény tabulky podle rozhodnutí 050 ta tabulka je. Katalog potvrdí, že taková tabulka existuje — ve schématu, které dotaz jmenuje, pokud nějaké jmenuje. Nic nového se nezavádí: je to přesně párování, které fáze entit dělá pro zdroj Dapper už dnes, kdykoli si o tabulku řekne cíl. Dapper → Dapper tak dostane tutéž vazbu, jakou dnes dostává Dapper → EF Core. Když katalog tabulku nenajde, nebo najde víc shod (případ `OrderLines` ve dvou schématech z historie rozhodnutí 089), vazba nevznikne a brána odmítne jako dosud. Nově záznamem, který řekne, že katalog tabulku nenašel, a který se tak liší od záznamu o nepřipojeném katalogu.
- **Vazba z katalogu je pro bránu uvedené mapování; konvence dál ne.** Pravidlo brány — skalár v signatuře je tvrzení o volajícím a plyne jen z faktu — zůstává. Fakt z katalogu je podle priority zdrojů rozhodnutí 015 fakt druhého stupně, fakt schématu, ne odhad. Třetí stupeň, konvence, bránu dál neotypuje. Brána tak bere vazbu ze stejných dvou stupňů, ze kterých se rozhoduje každý jiný mapovací fakt: od zdroje, a kde zdroj mlčí, od katalogu.
- **Zapisuje se do mezireprezentace, přírůstkově a až po vydání entit.** Zapíše se to, z čeho se vazba skládá, a nic víc: jméno tabulky a schéma entity (`TableName`, `SchemaName`). Sloupce, typy ani klíče se nezapisují, protože dotaz poptává jen vazbu. Zápis se řídí pravidlem 015: nic nepřepisuje, jen doplňuje. **Pořadí je součástí rozhodnutí.** Poptávka dotazu se obsluhuje až po vydání entitních artefaktů. Ty proto zůstanou přesně takové, jaké je určila poptávka cíle, a mechanické pravidlo ztrát nemá co hlásit — a to právem, protože se neztrácí nic, co zdroj řekl. Jednou zapsaná vazba platí pro každý další dotaz převodu, který ji najde a nic nepoptává. Který dotaz vazbu poptal, určuje pořadí jednotek na vstupu, takže tentýž vstup dá týž výsledek i tytéž záznamy (S2).
- **Původ nese záznam `Supplied` u dotazu, který vazbu poptal.** Původ je podle rozhodnutí 015 událost, ne stav. Záznam jmenuje entitu, kategorii a katalog jako zdroj a podle rozhodnutí 066 a 081 patří jednotce a dotazu, jejichž poptávka ho vyvolala.
- **Poptávka cíle se nemění a rozhodnutí 015 platí dál.** Cíl dál určuje, co se zapíše kvůli entitnímu artefaktu. Tohle rozhodnutí přidává druhého odběratele, dotaz, a s ním nejužší podobu varianty F rozhodnutí 015, tedy poptávky podle skutečné úlohy. Nepotřebuje suchý průchod, který F žádala, protože v okamžiku poptávky dotazu je dotaz přečtený a potřeba známá přesně. 015 variantu F „nezavrhuje, jen ji neimplementuje teď"; tohle je její první a nejmenší krok, ne náhrada varianty E.

## Důsledky

**Typ parametru přestane záviset na cíli.** Dapper → Dapper i Dapper → MyBatis s katalogem otypují `@minQuantity` jako `int`, stejně jako zbylé čtyři cíle; bez katalogu odmítnou všech šest stejně. Věta §9 o parametru zdroje Dapper tím platí v obou polovinách: bez katalogu se netypuje, s katalogem, který tabulku má, se typuje do každého cíle.

**Diferenční matice dostane Dapper zpět za zdroj tří parametrických kategorií.** Zdrojová varianta, tedy identitní směr, se spustí, a podle rozhodnutí 089 je tím Dapper zdrojem. `ScalarParameter`, `CollectionParameter` a `InListWithABoundValue` budou mít šest zdrojů místo pěti a věta o hotovém 4. stupni v kategorii Dotazy ztratí výjimku u Dapperu. Pravidlo z historie 089 „zdroj, jehož vlastní běh je odmítnut, není zdrojem" platí dál, jen přijde o svůj jediný případ.

**Odmítnutí `refusedFrom = Dapper:QueryParameter` se stane tvrzením o běhu bez katalogu.** Manifest `Tests/Database/QueryShapes/categories.txt` ho vyslovuje bez podmínky, a to přestane stačit. .NET matice kategorií převádí bez katalogu, ale javová sada převádí přes `test_app`, který katalog má, a bude-li v něm tabulka domény, odmítnutí nepřijde. Manifest i obě sady musí odmítnutí vázat na to, že běh katalog nemá. Jinak se změní v tvrzení, které platí podle toho, v jakém pořadí se naplnilo schéma.

**Záznam `Convention` o typu odvozeném z tabulky ustoupí u dotazu do SQL cíle záznamu `Supplied`.** Kde poptávka dotazu vazbu zapíše, najdou dotazové buildery Dapperu a MyBatisu entitu přes uvedené mapování místo přes konvenci 050. Entita je tatáž, protože katalog potvrdil právě kandidáta konvence, a text artefaktu se nemění. Mění se jen to, co záznam tvrdí: párování je fakt schématu, ne odhad.

**Stav připojení a čas čtení.** Převod, jehož dotaz vazbu poptal, hlásí stav katalogu `Reached` tam, kde dosud hlásil `Unused`, a čas čtení katalogu zahrnuje oba kroky. Tvar odpovědi `/convert` se nemění.

**Pro sedmý framework se nemění nic** (S1). `Model` se nemění. `AbstractWrappers` dostává poptávku v šabloně, jednou. Orchestrace dostává krok mezi čtením dotazu a jeho stavbou a `DatabaseCatalog` obsluhu poptávky nad týmž čtečem. Wrappery ani deskriptory se nemění.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR**: vstupy, které dosud skončily `Failure`, nově vydají artefakt, a veřejné rozhraní ani tvar odpovědi se nemění.

**Co tohle rozhodnutí neurčuje.** Zda se poptávky několika dotazů jednoho převodu sloučí do jednoho čtení (nejdřív přečíst všechny jednotky, pak stavět), nebo se obslouží po jedné: je to otázka počtu dotazů do databáze, ne toho, co se zapíše, a přírůstkový zápis zaručuje, že se tatáž vazba nečte dvakrát. Dále zda cíl MyBatis použije obraz tabulky, který fáze entit už přečetla, nebo ho přečte znovu. Obojí se rozhodne při implementaci.

**Testy.** S katalogem Dapper → Dapper a Dapper → MyBatis nad `ol.Quantity >= @minQuantity` vydají `int minQuantity` a záznam `Supplied` u dotazu, a entitní artefakty nenesou `Loss` kategorie `TableName`. Bez katalogu dávají `Failure` jako dosud, se záznamem, který chybějící katalog jmenuje. Převod bez dotazů, dotaz bez parametru, parametr proti nekvalifikovanému sloupci a parametr proti konstantě nechají katalog ve stavu `Unused`. Tabulka, kterou katalog nemá, dá `Failure` s důvodem, že ji katalog nenašel. Dvě tabulky téhož jména v různých schématech bez schématu v dotazu dají `Failure`, se schématem v dotazu vazbu. Dva dotazy téhož převodu nad toutéž tabulkou vazbu přečtou jednou a `Supplied` nese ten první. Doložení, kvůli kterému rozhodnutí vzniklo, nese diferenční matice: Dapper jako šestý zdroj kategorií `ScalarParameter`, `CollectionParameter` a `InListWithABoundValue`.
