# 064 — Nepřítomnost generování je fakt katalogu

Datum: 2026-08-25
Stav: platí
Požadavky: F4, F6, F11
Podklad: rozhodnutí [011](011-key-generation-strategy-vocabulary.md), [015](015-mapping-fact-completion-from-the-catalog.md) a [063](063-stated-keylessness-as-a-carried-fact.md); nález ze scénářů 4. stupně (2026-08-25, [`architecture.md`](../architecture.md) §6.2); `sys.columns` (`is_identity`, `default_object_id`)

## Kontext

Fáze doplnění (rozhodnutí 015) zná o strategii klíče jediný kladný fakt: IDENTITY sloupec dostane `Identity`. Sloupec bez IDENTITY nechává `Unspecified` s odůvodněním, že „databáze hodnotu negeneruje" neříká, kdo ji přiřazuje. Oba buildery pak nechají mluvit konvenci cíle, jenže ty dvě konvence tvrdí opak: NHibernate za `Unspecified` vypíše `assigned` a záznamem `Convention` řekne, že je to jeho domněnka, kdežto EF Core u jednodílného celočíselného nebo Guid klíče mlčky nevypíše nic — a právě tím nechá svou konvenci odvodit generování hodnoty úložištěm.

Artefakt nad tabulkou `Products` — klíč přiděluje aplikace, sloupec IDENTITY nemá — tak tvrdí generování, které schéma nemá, a katalog to celou dobu věděl: `is_identity` četl. Vidět to bylo až na 4. stupni ověření a jen napůl: `DapperManyToManyPersistenceTest` prochází, protože výslovně nastavenou hodnotu klíče EF Core do INSERT vypíše i u vlastnosti považované za generovanou; konzument, který by hodnotu nechal na frameworku, dostane místo přiděleného klíče chybu databáze.

Vada má dvě vrstvy. První je chybějící tvrzení: nikdo nevysloví fakt, který katalog zná. Druhá je asymetrie hlášení: NHibernate svou konvenci `assigned` ohlásí, EF Core své konvenční generování neohlásí vůbec — do artefaktu nic nepíše, takže není token, ke kterému by záznam patřil, ale behaviorální tvrzení vzniká stejně a F11 o něm mlčí.

A jedna mez čtečky: `ColumnImage` nese jen `IsIdentity`. Sloupec přitom může plnit i default constraint — `NEXT VALUE FOR` sekvence, `NEWID()` —, takže „není IDENTITY" ještě neznamená „hodnotu musí dodat aplikace".

## Zvažované varianty

1. **Vypsat `[DatabaseGenerated(None)]` v EF Core builderu za `Unspecified`.** Nejmenší zásah a špatný: bez katalogu `Unspecified` znamená „nikdo neřekl", ne „přiděluje aplikace", takže by builder tvrdil přidělování, které nikdo nevyslovil. Je to táž struktura jako `[Keyless]` v rozhodnutí 015: potlačit konvenci cíle je bezpečné jen nad nepřítomností, které se dá věřit. Tam důvěru dodal parser čtením konvenčního klíče zdroje; tady ji může dodat jedině katalog, protože původce hodnoty je fakt schématu, ne artefaktu žádného frameworku.

2. **Doplnit `Assigned` z holého „není IDENTITY".** Tvrdí, co katalog nepotvrdil: sloupec s default constraintem plní úložiště, přestože IDENTITY není. Nad sekvencí by EF Core s vypsaným `None` vkládal výchozí hodnotu CLR — jedno nepravdivé tvrzení vyměněné za jiné.

3. **Jen hlásit, nedoplňovat.** Záznam vedle vadného artefaktu říká uživateli, ať ručně spraví, co nástroj uměl vyslovit sám — rozhodnutí 015 vzniklo přesně proto, aby fakta, která katalog zná, dodávala fáze. A artefakt, jehož INSERT se chová jinak, než schéma žádá, není chudší program, je to jiný program — táž poctivost, kterou na dotazové straně drží rozhodnutí 053, 060 a 061 o množině řádků.

4. **Čtečka ponese fakt defaultu a fáze doplní `Assigned` symetricky k `Identity`.**

## Rozhodnutí

**Volíme variantu 4: nepřítomnost generování vyslovuje fáze doplnění, a smí ji vyslovit, protože ji katalog opravdu tvrdí.**

**„Není IDENTITY a nemá default" je kladné tvrzení schématu, ne mezera.** Databáze hodnotu nevyrobí, takže ji INSERT musí přinést — a přesně to jmenuje `Assigned` ve slovníku mechanismů (rozhodnutí 011: „aplikace dodá hodnotu před vložením"). Jemnější mechanismy dodací strany — `Uuid`, `HiLo`, `Increment` — jsou z pohledu schématu nerozlišitelné podoby téhož: hodnota vzniká mimo databázi. Vyslovit je umí jen zdroj a priorita zdrojů (pravidlo E9, rozhodnutí 015) je chrání: doplňuje se pouze `Unspecified`.

**Čtečka získává jeden fakt: `HasDefault`.** `sys.columns.default_object_id` je nenulové právě tam, kde sloupec nese default constraint; je to jeden sloupec navíc v dotazu, který už běží, žádný nový dotaz (S3). F4 vyjmenovává metadata se slovem „alespoň" a tenhle příznak je v jeho duchu: je to přesně to metadatum, které rozhoduje, zda hodnotu vyrábí úložiště.

**Fáze netvrdí nic, co dosud nesoudí.** Kontrola konfliktů bere `Assigned` nad IDENTITY sloupcem za rozpor od rozhodnutí 015; doplnění komplementu jen zavírá jednostrannost, ve které se kladný fakt dodával a jeho protějšek ne. Kontrola sama se nemění: `Identity` i `Auto` nad sloupcem bez IDENTITY zůstávají rozporem i tam, kde sloupec default má — `native` i `identity` čtou hodnotu mechanismem, který default neposkytuje.

**Klíčový sloupec s defaultem zůstává `Unspecified` a stav se hlásí.** Úložiště ho umí naplnit, ale booleovský příznak mechanismus nepojmenuje; záznam `Incompleteness` (kategorie `PrimaryKeyStrategy`) řekne, že default existuje a fáze nemá jak ho přeložit. Rozpoznat `NEXT VALUE FOR` z textu definice a doplnit `Sequence` se jménem sekvence je vědomě odložený přírůstek: vyžaduje čtení definic constraintů, ne katalogový příznak, a tahle vada na něm nestojí.

**EF Core builder svou konvenci nově hlásí.** Zůstane-li strategie `Unspecified` a konvence cíle klíč generuje — jednodílný celočíselný nebo Guid klíč —, vydá se záznam `Convention`: výstup se bude chovat jako generovaný, a je to konvence cíle, ne fakt zdroje. Je to zrcadlo záznamu, který NHibernate vydává k vypsanému `assigned`, a přesně ten záznam, který by tuhle vadu ukázal dřív než 4. stupeň. Hlásí se jen generující případ: kde konvence cíle nic kladného netvrdí — řetězcový klíč, složený klíč —, výstup se chová jako přidělovaný, nic se nepíše a není co hlásit.

**Buildery se jinak nemění.** `Assigned` s konvenčně generujícím typem vypisuje EF Core builder jako `[DatabaseGenerated(DatabaseGeneratedOption.None)]` už dnes — je to jediný způsob, jak konvenci potlačit, a nově je krytý tvrzením katalogu. NHibernate za `Assigned` píše `assigned` jako dosud; jeho záznam `Convention` sám zmlkne, protože platí jen pro `Unspecified`, a původ nese záznam `Supplied` fáze — původ je událost, ne stav modelu (rozhodnutí 010 a 015).

**Vztah k položce o kritériu širšího čtení konvencí zdroje.** Tahle volba se jí nedotýká: materializuje fakt katalogu (druhý stupeň priority), ne konvenci zdrojového frameworku (první stupeň). Otevřená otázka, podle čeho materializovat konvence zdroje, zůstává otevřená beze změny.

## Důsledky

**Dotčená místa:** `ColumnImage` (nový člen `HasDefault`), `SqlServerCatalogReader` (jeden sloupec navíc v dotazu na sloupce), `CatalogCompletion` (`StrategyFor` a `CompleteKeyStrategies` doplňují `Assigned`, defaultem krytý klíčový sloupec se hlásí), `EFCoreEntityBuilder` (záznam `Convention` za konvenční generování). NHibernate wrapper a Dapper se nemění.

**Nález se uzavírá celý.** S připojeným katalogem vyjde `Products` do EF Core s `[DatabaseGenerated(DatabaseGeneratedOption.None)]` a záznamem `Supplied`; konzument, který nechá hodnotu klíče na frameworku, dostane přidělený klíč, ne chybu databáze. `assigned` NHibernate nad touž tabulkou přestává být domněnkou a stává se tvrzením. Kompozitní klíče artefakt nemění — EF Core je konvencí negeneruje a `<composite-id>` generátor nemá — a doplněné `Assigned` jejich částí je konzistentní s tím, co oba cíle dělají.

**Bez katalogu se výstup nemění.** `Unspecified` zůstane, konvence cílů mluví dál a dál si protiřečí — bez katalogu nikdo lepší tvrzení nemá. Nový je jen záznam `Convention` na straně EF Core, takže rozpor obou konvencí je poprvé vidět v diagnostice, ne až za běhu.

**Verzování (rozhodnutí 041): navenek nic.** REST kontrakt se nemění a nepřibývá žádný nový druh záznamu ani kategorie — frontendová tabulka popisků zůstává.

**Testovací schéma dostává default.** Aby byl příznak měřitelný proti skutečnému katalogu (F4), nese fixture sloupec s default constraintem; scénáře 4. stupně nad `Products` tím nově vykonávají i přidělovaný klíč vyslovený katalogem.

**Javová strana čte týž fakt.** JPA strategii vyslovuje výslovně (`GenerationType`) a MyBatis nevyjadřuje nic — `Assigned` z katalogu bude pro F8 tímtéž, čím je dnes pro Dapper jako zdroj: jediným místem, odkud fakt může přijít.
