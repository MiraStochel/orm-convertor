# 016 — Stupně ověření generovaných artefaktů a zdroj testovací databáze

Datum: 2026-08-17
Stav: platí
Požadavky: F2, F3, F4, F6, F11, S2, S4, S5
Podklad: kritérium ověření u F6; rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md) a [006](006-flat-composite-key-rendering.md)

## Kontext

Nejbližším cílem je překlad z Dapperu do EF Core a NHibernate s mapovacími fakty doplněnými z databázového katalogu. Je to doslovné znění F6 — z frameworku s neúplnou či implicitní mapovací informací vytvořit úplné mapování cílového frameworku doplněním z databáze — a kritérium ověření u něj zní: *z entity obsahující jen názvy a jazykové typy vlastností vznikne kompilovatelná cílová entita se správnými názvy tabulek a sloupců, klíči a omezeními*. Z toho jediného souvětí plynou dvě věci a obě určují, co je třeba postavit dřív.

**Ze zdroje nepřijde nic.** `DapperEntityParser` čte hlavičku třídy a vlastnosti a `AddPrimaryKey` nezavolá nikdy. `DapperDescriptor` má `TableName`, `SchemaName`, `ColumnName`, `DatabaseType`, `Length`, `PrecisionAndScale`, `Nullability`, `PrimaryKey`, `PrimaryKeyStrategy` i `ForeignKeyColumns` označené jako `NotExpressible`. Rozhodnutí 015 z toho vyvodilo, že Dapper je jako zdroj nejsilnějším případem pro čtení katalogu; čteno obráceně to znamená, že tenhle převod nemá bez připojené databáze odkud vzít ani jednu z položek, které kritérium F6 vyjmenovává. **Databáze proto není doplněk na konec, ale předpoklad.**

**O výsledku nerozhodne tvarová aserce.** Kritérium mluví o *kompilovatelné* cílové entitě, ne o očekávaném řetězci. Mapování doplněné z katalogu je navíc ten případ, kde je rozdíl mezi tvarem a přijatelností největší: fakty v něm nepocházejí ze zdroje, nýbrž ze schématu, takže nikdo kromě cílového frameworku nepotvrdí, že ke generované třídě sedí.

**Co dnes ověřujeme.** Dvojí, a je třeba je rozlišit. Testy nad mezireprezentací tvrdí něco o modelu: `PrimaryKeyTest` a `CompositeKeyTest` kontrolují pořadí částí klíče, názvy sloupců, databázové i jazykové typy a strategii generování. Tím je F1 pokryté — jednoduchý klíč i složené klíče o dvou, třech a čtyřech částech. Testy nad výstupem naproti tomu tvrdí něco o tvaru textu: `NHibernateCompositeKeyRoundTrip` čeká výskyt řetězce `<key-property name="OrderID" column="OrderId" type="Int32" />` a pořadí tří takových elementů. Druhý druh dokazuje, že builder napsal, co jsme čekali. Nedokazuje, že to NHibernate přijme.

**Je to přesně ta třída chyby, kterou pojmenovalo rozhodnutí 006:** výstup je syntakticky správný a přitom nespustitelný. Mapování odkazující na vlastnost, jakou třída nemá, `<composite-id>` s klíčovou třídou bez `Equals` a `GetHashCode`, klíčová část bez typu — nic z toho žádná řetězcová aserce nezachytí, protože všechno to jsou tvrzení o vztahu mapování ke třídě, ne o podobě jednoho elementu. Rozhodnutí 006 tuhle chybu popsalo a rozhodnutí 009 na ni odpovědělo vynucenými členy v deskriptoru; ověřeno ale dodnes není, že vynucené členy skutečně stačí.

**Stav infrastruktury.** Testovací projekt nereferencuje žádného databázového klienta ani žádný balík cílového frameworku. Není to opomenutí: wrappery samy NHibernate ani EF Core nereferencují, protože generují text a k tomu jim stačí Roslyn na čtení vstupu. Workflow v `.github` spouští `dotnet test` bez service containeru. Jediné čtení katalogu v repozitáři je v `AdvisorBenchmarking.HarnessGenerationUtilities` a selhání spojení v něm polyká prázdný `catch`. `docker-compose.yml` staví SQL Server s WideWorldImporters přes `database.Dockerfile`, ale jako prostředí pro aplikaci; `ConnectionStrings__AdvisorDatabase` je v něm commitnutá deklarace, kterou zatím nikdo nespustil.

Je proto třeba rozhodnout, které stupně ověření uznáváme a co každý z nich vyžaduje, kam v nich patří Dapper jako zdroj, odkud se bere testovací databáze a co se stane, když k dispozici není.

## Zvažované varianty

Otázka se rozpadá na dvě, které spolu souvisejí jen jedním koncem: stupně ověření určují, jaká infrastruktura je vůbec potřeba, a teprve pak má smysl se ptát, odkud ji vzít.

### Stupně ověření

#### A — Zůstat u tvarových asercí

Dnešní stav. Testy porovnávají vygenerovaný text s očekávaným. Nevyžadují nic než samotné řešení, běží v milisekundách a jsou naprosto deterministické. Platí se za to tím, že nelze splnit ani doslovné znění F6 — „kompilovatelná cílová entita" je tvrzení o překladači, ne o řetězci —, ani totéž u F2, a F11 nemá čím doložit syntaktickou správnost generovaných souborů. Především ale zůstává neošetřená celá třída chyb z rozhodnutí 006, a to zrovna u mapování, které si nástroj poskládal sám z katalogu.

#### B — Rovnou spuštění proti databázi

Každý test by generovaný kód přeložil, spustil proti databázi a ověřil, že se entita uloží a načte. Je to nejsilnější důkaz a F3 splňuje přímo. Cena je trojí. Na infrastruktuře by záviselo úplně všechno, i to, co ji nepotřebuje — celý převod EF Core ↔ NHibernate. Testy zpomalí z milisekund na sekundy a přestanou být použitelné jako průběžná zpětná vazba. A když takový test selže, neřekne, jestli je vada v builderu, ve čtečce katalogu, v mapování, nebo ve schématu; u doplňovaného mapování jsou to čtyři možnosti se stejným projevem.

#### C — Stupňovité ověření

Uznáme několik stupňů, kde každý odpovídá na jinou otázku a stojí jinou infrastrukturu. Test si vybere nejnižší stupeň, který jeho tvrzení unese. Cenou je, že stupňů je víc než jeden a je třeba udržet zřejmé, co který dokazuje; ziskem, že selhání ukazuje na místo a že převody nezávislé na databázi zůstanou rychlé.

### Zdroj testovací databáze

#### D — Docker Compose s WideWorldImporters

Dnešní deklarace v `docker-compose.yml`. Reprodukovatelné a v souladu s S5. Na Windows ale vyžaduje běžící Docker Desktop, start kontejneru s obnovenou zálohou trvá desítky sekund a databáze je cizí: schéma, proti kterému bychom tvrdili, jsme nenavrhli my.

#### E — Lokální SQL Server, schéma vlastněné testy

Testy se připojí k instanci, kterou má vývojář k dispozici, a schéma si vytvoří samy — vlastní tabulky ve vlastním schématu, po doběhnutí zase zahozené. Nejrychlejší cesta a plná kontrola nad tím, co se testuje. Samo o sobě to nesplňuje S5 a v CI to nefunguje.

#### F — Testcontainers

Kontejner vyzvedne a spustí sám testovací kód. Spojuje reprodukovatelnost s tím, že prostředí není třeba připravovat ručně, a v CI funguje. Docker ale vyžaduje pořád, přidává balíkovou závislost a první běh platí stažením image.

#### G — SQLite nebo in-memory poskytovatel

Žádný server. Dialekt je ale jiný a rozdíly leží přesně tam, kam míříme: `IDENTITY`, sémantika vícesloupcových cizích klíčů, chování `DatabaseType`, který je dnes fakticky seznamem typů T-SQL. U čtení katalogu je to diskvalifikující samo o sobě — čtečka podle F4 se ptá na tabulky, sloupce, klíče a omezení konkrétního katalogu, takže testovat ji nad jiným dialektem znamená testovat něco jiného. In-memory poskytovatel EF Core navíc relační omezení nevynucuje vůbec.

## Rozhodnutí

**Varianta C se čtyřmi stupni, a jako zdroj databáze varianta E.**

| Stupeň | Co dokazuje | Co vyžaduje |
|---|---|---|
| 1. **Tvar** | builder napsal to, co jsme čekali, a v očekávaném pořadí | nic |
| 2. **Překlad** | vygenerovaný artefakt je syntakticky správný | Roslyn pro C#, XSD pro mapování NHibernate |
| 3. **Přijetí frameworkem** | cílový framework mapování přijme a sestaví z něj model | balíky cílových frameworků |
| 4. **Běh proti databázi** | entita se uloží a načte se stejnou identitou | připojená databáze |

**Nosný je třetí stupeň.** Oba cílové .NET frameworky ověřují mapování proti třídě už ve chvíli konfigurace, tedy dřív, než se pokusí o spojení: NHibernate při `BuildSessionFactory` nad `Configuration` s pouhým dialektem, EF Core při sestavení modelu z `DbContext`, kterému stačí připojovací řetězec jako text. Právě tady se pozná mapování odkazující na neexistující vlastnost, klíčová třída bez `Equals` a `GetHashCode` i klíčová část bez typu — celá třída chyb z rozhodnutí 006 a zároveň jediné skutečné ověření vynucených členů z rozhodnutí 009. U mapování doplněného z katalogu je to navíc jediný verdikt, který něco znamená: fakty přišly zvenčí zdroje a nic než cílový framework nepotvrdí, že ke generované třídě sedí.

**Stupeň říká, co je potřeba, vzhledem k cíli — ne vzhledem ke zdroji.** To je rozdíl, který je snadné přehlédnout, a rozhoduje o pořadí prací. U převodu EF Core → NHibernate zdroj klíč vyjadřuje, artefakt tedy vznikne bez jakéhokoli spojení a druhý i třetí stupeň nad ním proběhnou nasucho. U Dapperu jako zdroje artefakt bez katalogu v podobě, kterou by mělo smysl soudit, vůbec nevznikne — chybí mu tabulka, sloupce, typy i klíč, takže cílový framework by odmítl vstup, ne generátor. Táž úroveň ověření je proto jednou bez databáze a podruhé s ní, a rozhoduje o tom zdroj převodu. **Prostředí s testovací databází je tím prvním krokem, ne posledním.**

**Scénář Dapper → cíl vedeme pod F6, ne pod F2.** F2 žádá překlad složených klíčů „mechanismem daného frameworku" a mechanismy vyjmenovává: více klíčových sloupců, `@EmbeddedId`, `@IdClass`, XML mapování. Dapper žádný z nich nemá a jeho deskriptor uvádí kategorii `PrimaryKey` jako nevyjádřitelnou; je to konstatování o frameworku, ne mezera v nástroji. Obousměrný scénář, který kritérium F2 žádá pro každý framework, tedy u Dapperu dává smysl jen ve směru cíl → Dapper, kde se ztráta klíče nese jako varování podle rozhodnutí [004](004-unexpressible-facts-as-warnings.md). Směr Dapper → cíl s úplným klíčem je naopak doslovné kritérium F6. Zapsat to je nutné: jinak by „pro každý framework" v kritériu F2 žádalo po Dapperu něco, co Dapper vyjádřit neumí, a scénář, který ve skutečnosti patří pod F6, by držel F2 trvale otevřené.

**Testy vlastní své schéma.** Čtvrtý stupeň si tabulky vytvoří sám ve vlastním schématu a po doběhnutí je zahodí; nic v testech nejmenuje WideWorldImporters. Důvod není pohodlí, ale povaha toho, co dokazujeme. U scénáře s Dapperem je fixture zároveň **zdrojem pravdy**: ze vstupu přijdou jen názvy vlastností a jazykové typy, takže názvy tabulky a sloupců, typy, klíč i cizí klíče musí vyjít ze schématu, a schéma je tím současně očekávanou odpovědí. F4 měří podíl správně získaných metadat proti *testovací* databázi — takový podíl nelze spočítat proti schématu, jehož správnou odpověď neznáme. Případy, o které jde u F1–F3 — složené klíče o dvou až čtyřech částech, vícesloupcové cizí klíče, spojovací tabulka —, je navíc třeba zkonstruovat, ne hledat v cizí ukázkové databázi a doufat, že tam v potřebné podobě jsou. WideWorldImporters tím neztrácí smysl, jen si podrží ten dosavadní: je to databáze pro benchmarking Advisoru, kde je realistický objem dat celý účel.

**Připojení je konfigurace, ne kód.** Připojovací řetězec se čte z proměnné prostředí nebo z user secrets a v repozitáři není. S4 zakazuje přihlašovací údaje v generovaných souborech a v lozích a commitnuté heslo `sa` je proti témuž duchu; proti lokální instanci navíc `Trusted_Connection=True` žádné údaje nenese. Protože o hostiteli rozhoduje konfigurace, je pozdější přechod na variantu D nebo F změnou konfigurace, ne přepisem testů.

**Chybějící databáze test přeskočí, ne shodí.** Když připojovací řetězec není nastavený, testy závislé na databázi se přeskočí s uvedeným důvodem a ostatní běží dál. Nově se to netýká jen čtvrtého stupně: přeskočí se i ty scénáře druhého a třetího stupně, které mají zdrojem Dapper, protože jejich vstup vzniká až čtením katalogu. Důvody jsou tři. Rozhodnutí 015 už stanovilo, že překlad bez připojené databáze selhat nesmí — testovací sada nemá tvrdit opak toho, co tvrdí nástroj. CI dnes databázi nemá a trvale červená hlavní větev je horší než chybějící signál, protože znehodnotí i ten zbývající. A převody, které databázi nepotřebují, si tím udrží rychlou zpětnou vazbu.

**Přeskočení je ale samo tvrzením o pokrytí.** Test, který se tiše nespustil, vypadá v souhrnu skoro jako test, který prošel. Proto platí, že kritéria F4 a F6 jsou splněná jen tam, kde běh s databází skutečně proběhl, totéž u „spuštěného" testu v F3, a že přeskočení musí nést svůj důvod. Evidovat to je věcí záznamu běhu podle S6, ne testovacího běhu; xUnit v3 dynamické přeskakování nabízí a konkrétní podoba volání patří do implementace.

**S5 se tím odkládá, ne opouští.** Požadavek žádá celý systém včetně databáze spustitelný dokumentovanou kontejnerovou konfigurací, kde čisté prostředí reprodukuje testy jedním hlavním příkazem. Varianta E to nesplňuje a splnit ani nemá; splní to varianta D nebo F, doplněná ve chvíli, kdy databázově závislé testy budou existovat. Právě proto je podstatné, že hostitele určuje konfigurace: podmínka S5 se pak dohání přidáním služby a proměnné prostředí, ne návratem k tomuto rozhodnutí.

## Důsledky

**Pořadí prací se obrací.** Dosavadní plán stavěl na první místo klíče ve všech třech frameworcích a prostředí s databází až za ně. Nejbližším cílem je nyní cesta z Dapperu přes katalog, a ta má opačnou závislost: prostředí je předpokladem čtečky katalogu, čtečka předpokladem toho, aby vůbec vznikl artefakt, a druhý a třetí stupeň tím, co o něm rozhodne. Konkrétní pořadí kroků je v `open-items.md`.

**Druhý a třetí stupeň mají vzniknout dřív než čtečka katalogu.** Nezávisejí na sobě — na převodu EF Core ↔ NHibernate běží bez databáze —, takže o pořadí rozhoduje něco jiného: čtečka doplňuje fakty, jejichž správnost se pozná až přijetím u cíle. Kdyby vznikla dřív, soudily by ji zatím tvarové aserce nad mapováním, které jsme si sami poskládali, a ty by se pak stejně přepisovaly. S hotovým třetím stupněm dostane každá iterace čtečky rovnou odpověď od NHibernate a EF Core.

**Testovací projekt získá balíky cílových frameworků** — EF Core, NHibernate, Dapper a `Microsoft.Data.SqlClient`. **S1 to neporušuje.** Požadavek chrání rozšiřitelnost nástroje: wrappery zůstávají bez závislosti na frameworku, pro který generují, a přidání dalšího frameworku dál znamená jen parser a builder. Nástroj na NHibernate nezávisí; závisí na něm ověření, a to je něco jiného. NHibernate v tomto řešení dosud není vůbec (je jen v odděleném `benchmarks/`), takže tam přibývá jako nový balík.

**Překlad má v repozitáři už jednoho hotového spotřebitele a druhého mít nemá.** `RoslynBenchmarkCompiler` a `BenchmarkReferenceProvider` v `AdvisorBenchmarking` jsou `internal`, kompilátor při neúspěchu vyhazuje výjimku a sestavení rovnou zavádí do `AssemblyLoadContext`. Ověření druhého stupně potřebuje pravý opak: vrácené diagnostiky a žádné zavádění. Druhá cesta přes Roslyn vedle první je přesně ta dvojí odpověď na tutéž otázku, kterou vytklo rozhodnutí 015 kvalifikaci názvu tabulky. Krok překladu proto dostane jedno místo použitelné z obou stran.

**Třetí stupeň sváže testy s konkrétní verzí frameworku.** Tvrzení „NHibernate to přijme" je tvrzením o verzi, proti které test běží. Váže se tím na rozhodnutí [013](013-target-framework-versions.md): pokud se verze balíku v testech a verze v deskriptoru cílového frameworku rozejdou, test dokládá něco jiného, než co generátor tvrdí. Dokud deskriptor verzi nenese, platí verze zafixovaná v `architecture.md` a shoda se hlídá ručně.

**Tvarové aserce nezanikají.** Jsou jediným stupněm, který umí tvrdit *jak* je něco zapsáno — pořadí částí klíče, přítomnost atributu, podoba elementu —, a přesně to potřebuje S2, který žádá byte-wise shodné nebo po normalizaci ekvivalentní artefakty. Vyšší stupeň by prošel i výstupu, který je správný a pokaždé jiný.

**CI se zatím nemění.** Běží stupně 1 a 2 a ty scénáře třetího stupně, jejichž zdroj klíč vyjadřuje; všechno, co stojí na katalogu nebo na běhu, se přeskočí. Doplnění service containeru je krátký zásah do workflow a proměnná prostředí; patří k dohnání S5, ne sem.

**`ConnectionStrings__AdvisorDatabase` v `docker-compose.yml` zůstává neověřená deklarace.** Dosavadní plán počítal s tím, že se ověří právě při stavbě testovacího prostředí. Tím, že testovací databáze vzniká jinudy, se to nestane — položka nemizí, jen se přesouvá k odložené kontejnerové konfiguraci.

**Co toto rozhodnutí neurčuje:** jak se popíše schéma testovací fixture (SQL skript, nebo kód), jestli se čtvrtý stupeň spouští po testu, nebo po kolekci, jak se uklízí a zda se pracuje v transakci, a jestli se mapování NHibernate ověřuje proti XSD dodanému s balíkem, nebo proti kopii v repozitáři. To je práce a rozhodne se při implementaci.
