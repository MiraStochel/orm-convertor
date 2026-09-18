# 080 — EclipseLink jako druhý profil nad JPA vrstvou

Datum: 2026-09-18
Stav: revidováno
Požadavky: F9, F10, F12, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [006](006-flat-composite-key-rendering.md), [009](009-target-framework-descriptor.md), [013](013-target-framework-versions.md), [016](016-generated-artifact-verification-levels.md), [019](019-neutral-database-type-vocabulary.md), [020](020-canonical-generator-parameter-vocabulary.md), [040](040-boundary-of-the-handed-over-artifact.md), [052](052-literal-sql-type-reaches-the-ef-core-annotation.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [068](068-source-framework-precedence-orders-the-reading.md), [069](069-major-marks-a-milestone-not-a-break.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [078](078-java-suite-as-a-client-of-a-running-instance.md) a [079](079-fractional-second-precision-as-second-precision.md); JSS §4.3, §5.2, §5.4 a §6.1; [tutoriál k EclipseLinku](../analysis/tutorials/eclipselink-getting-started.md), kroky 3, 4, 6 a 9, sekce *Rozdíl DDL* a *Co z toho plyne pro převodník*; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §4, §5, §6, §15, §16 a §20; Jakarta Persistence 3.2, kap. 2 a 11; EclipseLink 5.0.0; otevřená položka „EclipseLink wrapper nad JPA vrstvou"

## Kontext

Rozhodnutí 076 vyslovilo, že druhý javový wrapper je tenký — čtyři háčky nad `JakartaPersistence` — a rozhodnutí 077 tu vrstvu postavilo a vyzkoušelo na Hibernate. Tahle položka je proto jen o tom, co je EclipseLink samotné, a je to méně, než by se od druhého frameworku čekalo: **anotační podmnožina, tvar výstupu, čtení `orm.xml`, klíčová třída, jazyková osa i podmnožina JPQL se nemění ani o znak.** Kroky 3, 4 a 7 obou tutoriálů se nezměnily doslova; co se liší, je bootstrap, který nástroj negeneruje (rozhodnutí 040), a **defaulty za týmiž anotacemi**, které jsou neviditelné v textu a viditelné až v DDL.

Doložených rozdílů je pět a všechny pocházejí z běhu, ne z dokumentace — tutoriál je změřil nad druhou sadou entit, která schválně nemá jediný název ani strategii:

- **`@GeneratedValue` bez strategie je tabulka.** Hibernate 7.4.5 vyrobí sekvenci `<Entita>_SEQ` s krokem 50, EclipseLink 5.0.0 tabulku `SEQUENCE(SEQ_NAME, SEQ_COUNT)` a do ní řádek `SEQ_GEN`. Překlad, který by anotaci jen opsal, tiše vymění generátor za jiný.
- **Implicitní název se zapisuje velkými písmeny** (`NOTE`, `CREATEDON`, `TAG_ID`) — vlastnost `eclipselink.jpa.uppercase-column-names` je ve výchozím stavu zapnutá.
- **`String` je `VARCHAR` a nationalizace nemá anotaci.** Hibernate ji vyslovuje `@Nationalized` na atributu, na balíčku i globálně; EclipseLink zná jedinou cestu, doslovný `columnDefinition`.
- **`fetch = LAZY` na `@ManyToOne` je bez weavingu tiché.** Krok 9 tutoriálu to změřil sondou: bez javového agenta je atribut po `find` načtený, žádné varování, žádná výjimka. Je to jediný nález, kde se vygenerovaný kód chová jinak, než jeho anotace slibuje, a ani 2., ani 3. stupeň ověření to nenajde.
- **Vendor anotace jsou jiné** (`@AdditionalCriteria`, `@CacheIndex`, `@PrivateOwned`, `@BatchFetch`, `@Multitenant`) a ani jedna nemá v mezireprezentaci místo — na rozdíl od hibernátovské `@Nationalized`, která jedno má.

Vázané je tohle rozhodnutí trojím způsobem a stojí za to to říct, protože to zužuje prostor voleb skoro na nic. **Za prvé rozhodnutí 076: builder nespoléhá na default cíle.** Co parser nesmí materializovat z mlčení, to builder nesmí do mlčení schovat — a právě EclipseLink je důvod, proč to 076 vyslovilo. **Za druhé rozhodnutí 067 při čtení:** profil sice ví, že by se tabulka jmenovala `NOTE`, ale odvození z mlčení není tvrzení zdroje a mezeru doplní katalog (F6). **Za třetí rozhodnutí 019:** typ je rodina s facetami a unicode je faceta, ne typ; nationalizace je tedy jeden fakt ve dvou hláskováních, ne dva fakty.

## Zvažované varianty

Otázek je osm a u pěti z nich má rozhodnutí 076 nebo 077 odpověď, kterou stačí aplikovat. Skutečné varianty se nabízely u tří.

### Nationalizace bez anotace: mlčky, záznam, nebo doslovný typ

Faceta unicode dorazí do builderu a EclipseLink pro ni nemá anotaci. **Mlčky ji zahodit** je nejjednodušší a zamítáme to bez váhání: sloupec by v cíli vznikl jako `varchar`, tedy jiný sloupec, než jaký zdroj popsal, a rozhodnutí 004 přesně tohle zakazuje. **Ohlásit ji jako ztrátu a nic nevypsat** je poctivé, ale zbytečně slabé: cesta existuje, jen se jmenuje jinak. **Vypsat doslovný typ** je ta cesta — `columnDefinition = "nvarchar(200)"` je přesně to, co tutoriál dokládá jako jediný způsob, jak EclipseLinku nationalizaci říct. Volíme ji, se dvěma upřesněními, která z ní plynou: doslovný typ **přebíjí délku vedle sebe**, takže musí délku nést sám, a rodina bez národní varianty (`Decimal`, `Date`) žádný název nemá, takže tam ztráta se záznamem zůstává.

### Tabulka čítače: nechat ji na cíli, nebo vypsat z profilu

`@TableGenerator` bez parametrů je platná anotace a implementace si tabulku doplní sama. Nechat to tak je dnešní chování sdílené vrstvy a **je to přesně ta mezera, kterou 076 zakázalo**: pod EclipseLinkem by to byla tabulka `SEQUENCE`, pod Hibernate jeho vlastní, takže artefakt by nepojmenovával jeden databázový objekt, ale dva různé podle toho, kdo ho načte — a u generátoru klíče je to rozdíl mezi tím, odkud se čísla berou. Volíme **vypsat**: profil nese tabulku a její sloupce a builder je doplní tam, kde je zdroj neuvedl. Hranici té volby vyslovujeme hned: **vypisuje se jen to, co nějaký běh změřil.** Hodnoty EclipseLinku jsou z DDL v tutoriálu; co vytvoří Hibernate pro `TABLE`, žádný náš běh nezměřil, takže jeho profil tabulku čítače nenese a jeho `@TableGenerator` zůstává jako dosud. Nezměřený default není náš, abychom ho psali do artefaktu.

### Kde bydlí varování o weavingu

Rozhodnutí 076 slíbilo „podmíněný záznam u každé líné reference" a větu v hranici záruk. Kde ten záznam vzniká, je volba. **Při zápisu** by to znamenalo záznam u každé reference každého artefaktu pro EclipseLink — jenže mezireprezentace strategii načítání nenese (článek §5.4), takže builder žádnou línou referenci nevydává a záznam by opakoval touž větu u všeho. **Jen v deskriptoru a v hranici záruk** by zase nepotkal nikoho, kdo tenhle vstup skutečně překládá. Volíme **záznam při čtení**, podmíněný tím, že zdroj `fetch = LAZY` na jednoznačné referenci opravdu vyslovil. Je to jediné místo, kde se ta reference vyskytuje, a záznam říká obojí najednou: že strategie do mezireprezentace nevstupuje a že pod zdrojovou implementací nemusela platit ani tam. Je to vědomá odchylka od 077, kde se `fetch` nehlásí vůbec — pod Hibernate anotace dělá, co slibuje, a její zahození je běžná cena pivotu; tady je to navíc tvrzení o zdroji.

## Rozhodnutí

**EclipseLink vzniká jako druhý tenký profil nad `JakartaPersistence`: projekt `EclipseLinkWrappers` s deskriptorem, profilem a čtyřmi háčky, z nichž dva jsou prázdné a zůstávají prázdné proto, že tak zní odpověď na ně. `ORMEnum` dostává hodnotu `EclipseLink`. Sdílená vrstva se mění na čtyřech místech — tvar háčku nationalizace, tabulka čítače z profilu, věta o velkých písmenech v záznamu o názvu tabulky a háček líné reference — a všechna čtyři jsou parametrizace profilem, ne větvení podle frameworku.**

### Profil implementace

Deskriptor nese verzi `5.0.0` (rozhodnutí 013) a vedle něj stojí `JpaImplementationProfile` s hodnotami, které všechny pocházejí z běhu:

| Fakt profilu | Hibernate 7.4.5 | EclipseLink 5.0.0 |
|---|---|---|
| úroveň specifikace | 3.2 | 3.2 |
| rozklad `AUTO` | `Sequence`, krok 50 | `HiLo` nad tabulkou, krok 50 |
| tabulka čítače | neuvedena (nezměřeno) | `SEQUENCE(SEQ_NAME, SEQ_COUNT)`, řádek `SEQ_GEN` |
| implicitní názvy | beze změny | velkými písmeny |
| nationalizační režim | `@Nationalized` | jedině `columnDefinition` |
| balík vendor anotací | `org.hibernate.annotations` | `org.eclipse.persistence.annotations` |
| líná reference | proxy, funguje | jedině s weavingem |

Vynucené členy, tabulka podpory a podpora dotazů jsou **tytéž objekty** jako u Hibernate — obě implementace je berou z `JakartaPersistenceDescriptor`, protože je vyslovuje specifikace, ne ony. EclipseLink k nim nepřidává nic a jednu z nich by sám ani nepotřeboval: jeho indirekce přepisuje entitu místo dědění, takže by mu nevadila ani `final` třída; §2.1 ji ale žádá po obou, takže zůstává. Že se deskriptory liší jen verzí a profilem, drží test.

### Nationalizace doslovným typem

Háček 2 u EclipseLinku nevypisuje anotaci, ale doplňuje argument do `@Column`, který se právě píše. Pravidla jsou tři. **Doslovný typ ze zdroje vyhrává** a háček nedělá nic (rozhodnutí 052): `columnDefinition` už v argumentech je a je to to, co zdroj řekl. **Rodina s národní variantou se přeloží** na `nvarchar(délka)`, `nchar(délka)` nebo `ntext` — táž tabulka názvů T-SQL, kterou vrstva už má pro čtení, jen opačným směrem (zafixovaný cíl je SQL Server 2022, rozhodnutí 013; jiný dialekt je vyňatá oblast 5 jako všude jinde). Neuvedená délka se stává délkou 255, tedy výchozí hodnotou `@Column` podle specifikace, **se záznamem `Convention`**, protože doslovný typ délku vedle sebe přebíjí a artefakt tím tvrdí něco, co zdroj neřekl. **Chybějící rodinu doplní jazykový typ**: nejčastější zdroj té facety — `@Nationalized` nad `String` — žádný databázový typ netvrdí vůbec, takže rodinu čte builder z jazykového typu vlastnosti (`String` je `VarChar`, `char` je `Char`, nic jiného neimplikuje nic) **se záznamem `Convention`** kategorie `DatabaseType`: artefakt jmenuje typ, který zdroj nevyslovil. Je to odvození podle kritéria rozhodnutí 067 — vstup leží v přečteném artefaktu a mezera by putovala rovnou do varchar defaultu EclipseLinku, tedy do sloupce, který zdroj výslovně vyloučil. **Rodina bez národní varianty — a vlastnost, jejíž jazykový typ není znakový — je `Loss`** kategorie `DatabaseType`: EclipseLink pro fakt nemá anotaci a není co vypsat.

Kvůli tomuhle háčku se mění jeho podpis ve sdílené vrstvě: místo řádku za `@Column` dostává háček **argumenty rozepsaného `@Column` a seznam anotací pod něj**. Hibernate do druhého seznamu přidá `@Nationalized`, EclipseLink do prvního `columnDefinition`. Tvar výstupu Hibernate se tím nemění o jediný znak a volání na dvou místech v builderu se slučuje do jednoho, uvnitř `AppendColumn`.

### `AUTO` jako tabulka, vypsaná celá

Čtení se nemění: `@GeneratedValue` bez strategie i s `AUTO` je `Auto` (rozhodnutí 011) a **co to znamená v EclipseLinku, se nematerializuje** (067). Zápis rozkládá `Auto` podle profilu na `TABLE` a vypisuje `@TableGenerator` se všemi čtyřmi hodnotami tabulky čítače; kanonický parametr ze zdroje (rozhodnutí 020) každou z nich přebíjí jednotlivě, takže zdroj, který uvedl jen tabulku, dostane její název svůj a sloupce z profilu. Záznam `Convention` u rozkladu `AUTO` nově jmenuje mechanismus **hláskováním `GenerationType`** (`SEQUENCE`, `TABLE`), ne slovem modelu, aby čtenář našel v artefaktu to, o čem záznam mluví; u Hibernate se tím mění text jednoho záznamu a nic víc.

### Velká písmena

Na třech místech a všude vychází stejně. **Parser nematerializuje**: entita bez `@Table` nemá v modelu tabulku, ne tabulku `CUSTOMER` (067). **Builder vypisuje každý název vždycky**, takže se implicitní default cíle nikdy neuplatní — a právě to dělá artefakt přenositelným mezi oběma implementacemi, jak 076 chtělo. Tam, kde název přesto skládá z entity, záznam `Convention` u EclipseLinku dodává větu, že ponechaný mlčky by vznikl velkými písmeny; profil tu má svého jediného konzumenta a je to správný konzument, protože je to vysvětlení, ne chování. **Párování s katalogem** je v `DatabaseCatalog` už dnes necitlivé na velikost písmen (`TableImage.FindColumn`, `CatalogCompletion`), takže požadavek 4 z tutoriálu je splněný bez zásahu; výjimkou je jediné místo, které velikost písmen porovnává přísně, a je to místo, kde na ní záleží — rozhodnutí, jestli se název sloupce musí vypsat, protože se od názvu vlastnosti liší (`CREATEDON` proti `createdOn`).

### Vendor anotace a líná reference

Háček 3 zůstává **prázdný** a je to odpověď, ne opomenutí: žádná anotace `org.eclipse.persistence.annotations` nemá v mezireprezentaci místo — `@AdditionalCriteria` a `@Multitenant` jsou filtry nad modelem, `@CacheIndex` a `@BatchFetch` cache a načítání, `@PrivateOwned` kaskáda —, takže je hlásí sdílené čtení tak, jak hlásí každou nepřečtenou anotaci: `Loss` s jejím jménem. Tím je symetrie s Hibernate úplná, protože `@Formula` a spol. skončí v opačném směru stejně.

Líná reference dostává **podmíněný záznam při čtení**: `@ManyToOne(fetch = LAZY)` a `@OneToOne(fetch = LAZY)` ve zdroji pro EclipseLink jsou `Loss` s důvodem, který jmenuje weaving. Kolekce ne — indirekce kolekce weaving nepotřebuje a `@OneToMany` je líné ze specifikace. Sdílená vrstva k tomu dostává háček, jehož základ nedělá nic (Hibernate mlčí dál, 077), a fakta atributu druhý seznam pro důvody, které si implementace formuluje sama. Cestu `orm.xml` tohle nepokrývá: atribut `fetch` v XML nečte sdílený parser ani pro jednu implementaci a zůstává to tak — varování je pomůcka, ne fakt modelu, a kvůli němu nebudeme sdílený parser XML dělat implementačně závislým. **Hranice záruk dostává větu**, že líné načtení reference nástroj u EclipseLinku netvrdí.

### EQL nad JPQL

Háček 4 zůstává **prázdný** a i to je odpověď. EclipseLink Query Language je nadmnožina JPQL stejně jako HQL, ale to, co přidává, se dělí na dvě skupiny: buď to sdílený parser čte už dnes — spojení nesouvisejících entit s `on`, které builder vydává pro obě implementace (rozhodnutí 077) —, nebo to mezireprezentace nenese vůbec: `FUNC`, `OPERATOR`, `SQL` a `COLUMN` sahají pod mapování do databáze a poddotaz ve `FROM` je tvar, který model nemá. Klauzule `limit`, kterou čte háček Hibernate, v EQL neexistuje: stránkování patří na dotazový objekt a sdílený builder ho tam píše. Text s `limit` je proto v EclipseLink zdroji `Failure` s řádkem a sloupcem, ne dotaz tiše přečtený bez výřezu (rozhodnutí 070) — a to je přesně rozdíl mezi prázdným háčkem a parserem, který by bral všechno.

### Rozhraní a vzorky

`ORMEnum.EclipseLink = 50`, čtyři jednotky `/required-content` (entita v Javě, `orm.xml`, dotaz jako javová metoda, holé JPQL) s vlastními vzorky (`CustomerSampleEclipseLink`) a řádek v mirroru výčtu ve frontendu; typy obsahu, přípony ani zvýrazňovač se nemění, protože jazyky jsou tytéž. Vzorek je týž `Customer` jako u ostatních frameworků a liší se jediným řádkem — národní sloupec vysloveným doslovným typem —, takže se dá číst vedle hibernátovského jako doklad toho, že zdrojový text je jeden a profily dva. `ParserFactory` staví týž seznam v témž pořadí (`orm.xml` před třídou, rozhodnutí 068).

### Co tvrdí xUnit a co javová sada

Kritérium F9 — dvacet testů mapování, patnáct testů dotazů a nejméně pět překladů mezi Hibernate a EclipseLinkem — se dělí podle 076 stejně jako u F7. **xUnit tvrdí tvar a čtení**: profil (`AUTO` jako vypsaná tabulka čítače, doslovný typ místo `@Nationalized`, ztráta u rodiny bez národní varianty), nematerializované defaulty při čtení, záznam u líné reference, prázdné háčky (`limit` odmítnutý, vendor anotace hlášené) a sedm překladů mezi oběma implementacemi v obou směrech; k tomu EclipseLink vstupuje do matice `CrossFrameworkInputs`, která tím roste na dvacet pět směrů, a do matice vynucených členů. **Javová sada tvrdí přijetí a běh** (2.–4. stupeň): `javac`, stavba `EntityManagerFactory` nad generovanými třídami a uložení produktu proti SQL Serveru, ve třech zdrojových směrech, plus negativní polovina. Bootstrap sady je programový — `PersistenceUnitInfo` sestavený v kódu a předaný přímo provideru —, protože generované třídy žijí jen v dočasném adresáři scénáře pod vlastním zavaděčem a `persistence.xml` by je nenašel; weaving se nezapíná, což je přesně to nasazení, o kterém tohle rozhodnutí mlčet nechce.

Dvě tvrzení tohohle rozhodnutí xUnit ověřit neumí a javová sada je má potvrdit jako první, přesně tak, jak 077 nechalo potvrdit svá dvě: že `AUTO` v EclipseLinku 5.0.0 opravdu vytvoří tabulku `SEQUENCE` se sloupci `SEQ_NAME` a `SEQ_COUNT` a řádkem `SEQ_GEN`, a že `String` bez doslovného typu je `VARCHAR`, kdežto s ním `NVARCHAR`. Měří to `EclipseLinkClaimsTest` skriptem DDL, tedy toutéž metodou, jakou to změřil tutoriál: `generateSchema` provideru bez jakéhokoli připojení, s platformou vyslovenou vlastností `jakarta.persistence.database-product-name` (krok 6 tutoriálu změřil, že vendor vlastnost sama nestačí). **Obě tvrzení první běh potvrdil** (2026-09-18, viz Historie).

## Důsledky

**F9 zůstává vyňaté, dokud javová sada nad EclipseLinkem neprojde v CI.** Wrapper existuje, kritérium F9 má v xUnit všechny tři počty a javová polovina **poprvé proběhla 2026-09-18** — a jako u rozhodnutí 077 to nebyl formální krok: opravila tři předpoklady tohohle rozhodnutí (viz Historie). Proběhla ale mimo zafixované prostředí, proti SQL Serveru 2019 Express na vývojovém hostiteli; nárok proto vzniká až se zeleným během jobu `java-test` nad zafixovaným 2022, což je jediná zbývající věta a po ní se §9, [`traceability.md`](../traceability.md) i anglický nárok srovnají najednou. Do té chvíle je EclipseLink v repozitáři a v rozhraní, jen na něj verze neslibuje spoleh (§9, oblast 6). Je to táž opatrnost, s jakou do nároku vstupovalo F7.

**Sdílená vrstva se mění na čtyřech místech a všechna jsou parametrizací:** podpis háčku nationalizace, tabulka čítače z profilu, věta profilu v záznamu o odvozeném názvu tabulky a háček líné reference. Žádné větvení podle `ORMEnum`, žádný zásah do `AbstractWrappers`, `Common` ani orchestrace — invariant S1 platí dál a rozhodnutí 076 se potvrdilo tam, kde se potvrdit mělo: **druhý wrapper nad touž vrstvou stál pět souborů.**

**Tvar výstupu Hibernate se nemění**, mění se text dvou záznamů: rozklad `AUTO` jmenuje `SEQUENCE` místo `Sequence` a důvod vynuceného členu „nefinální třída" mluví o obou implementacích. Podle rozhodnutí 069 je to PATCH; schopnost, která přibyla, je MINOR a cíl 2 zavře `2.0.0`.

**Matice roste na dvacet pět směrů, z toho dvanáct napříč ekosystémy** (F10), a vzorky, `/required-content` i frontend nesou pátý framework. Co z F10 platí, se tím neposouvá: uzavření matice žádá i MyBatis.

**Co toto rozhodnutí neurčuje:** jednotku, která je mapování i dotaz zároveň, a operand parametru (F8); sdílené čtení T-SQL; javovou větev Advisoru (F15); a nic z toho, co by znamenalo číst `eclipselink-orm.xml` nebo nativní deskriptor — rozšířené schéma je mimo rozsah stejně jako hibernátovské `mapping.xml`, a `orm.xml` je společné oběma.

**Cena.** Pátá hodnota `ORMEnum` zdražuje každou matici (pětadvacet směrů místo šestnácti) a javová sada je o polovinu delší, protože EclipseLink má vlastní bootstrap a vlastní negativní polovinu. A doslovný typ v `columnDefinition` je cena za nationalizaci: artefakt pro EclipseLink je v tomhle jediném ohledu vázaný na dialekt, kdežto hibernátovský ne.

## Historie

**2026-09-18 — první běh obou sad opravil tři předpoklady; volba platí dál.** Rozhodnutí vzniklo a bylo naimplementováno téhož dne a ještě týž den ho první běžná CI a první běh javové sady opravily na třech místech. Žádné z nich nemění volbu — nationalizace doslovným typem, `AUTO` jako vypsaná tabulka čítače, dva prázdné háčky a záznam u líné reference platí beze změny —, všechna tři jsou případ, na který se při psaní nemyslelo, a proto se text opravil na místě:

- **Chybějící rodina u nationalizace.** Text říkal, že chybějící rodina je `Loss`. Jenže nejčastější zdroj facety — `@Nationalized` nad `String`, tedy přesně ten překlad, který rozhodnutí uvádí jako ukázkový — žádnou rodinu netvrdí, takže by se nationalizace ztrácela právě tam, kde má fungovat; test to chytil v CI. Rodinu proto doplňuje jazykový typ se záznamem `Convention` (viz *Nationalizace doslovným typem*).
- **Kořen jednotky nesmí být null.** `PersistenceUnitInfo` sestavený v kódu vypadá, že kořenovou URL nepotřebuje, když se nic neskenuje; EclipseLink z ní ale skládá název jednotky a na `null` spadne na `NullPointerException`. Bootstrap sady proto kořen vždy dodá — adresář scénáře, jinak místo, odkud byl nahrán sám.
- **Přihlašovací údaje si EclipseLink z URL nevezme.** Hibernate předá URL ovladači, jak stojí, a přihlásí se; EclipseLink si skládá vlastní vlastnosti spojení a pošle prázdného uživatele, což SQL Server odmítne chybou 18456. Sada proto čte `user` a `password` z URL a předává je jako `jakarta.persistence.jdbc.user` a `.password`. Je to táž třída nálezu jako v rozhodnutí 078: předpoklad o prostředí, který se dá ověřit jedině během.

Týž běh **potvrdil obě tvrzení profilu**, která xUnit ověřit neumí: `AUTO` vytvoří tabulku `SEQUENCE` se sloupci `SEQ_NAME` a `SEQ_COUNT` a řádkem `SEQ_GEN` a žádnou sekvenci, a `String` je `VARCHAR`, kdežto s doslovným typem `NVARCHAR`. Měří se to bez připojení, `generateSchema` s vyslovenou platformou, takže to platí i tam, kde žádná databáze není.
