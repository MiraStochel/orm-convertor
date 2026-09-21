# 086 — Cílový databázový dialekt deklaruje deskriptor

Datum: 2026-09-21
Stav: platí
Požadavky: F2, F5, F7–F10, F11, S1, S2, S6
Podklad: rozhodnutí [009](009-target-framework-descriptor.md), [010](010-diagnostics-as-returned-data.md), [013](013-target-framework-versions.md), [014](014-language-type-model.md), [019](019-neutral-database-type-vocabulary.md), [050](050-one-home-for-the-singular-plural-heuristic.md), [052](052-literal-sql-type-reaches-the-ef-core-annotation.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md) a [082](082-t-sql-read-and-written-by-a-shared-project.md); [`architecture.md`](../architecture.md) §9, vyňatá oblast 5; otevřená položka „Cílový databázový dialekt v deskriptoru"; NHibernate 5.7.0 — schéma `nhibernate-mapping` a `TypeFactory`

## Kontext

Rozhodnutí 019 zneutralizovalo databázovou stranu typového modelu na rodiny s facetami a jednu věc vědomě odložilo: **dialekt**. Jeho vlastní slova zní, že emitovat `sql-type` odvozený z typové rodiny nebo vybrat typ podle systému vyžaduje vědět, na jaký databázový systém se míří, že se to nikde nedeklaruje, a že je to fakt o cíli převodu téhož tvaru jako cílová verze frameworku z rozhodnutí 013 — patří tedy k ní, ne do typového modelu. Tohle rozhodnutí tu větu dokončuje.

**Dnes je ten předpoklad všude a vyslovený nikde.** Názvy typů T-SQL hláskují čtyři místa: `DatabaseTypeConvertor.ToEFCore` a `FromEfCore` v `EFCoreWrappers`, `JpaSqlTypeWriting` a `JpaSqlTypeReading` v `JakartaPersistence` a `SqlServerCatalogReader.MapType` v `DatabaseCatalog`. Dva z těch souborů nesou v hlavičce poznámku, že jsou **kopií** té druhé a že je čeká společné bydliště. Duplicita je ale jen to viditelné; podstatnější je, že žádné z těch míst nemá jak říct, **čí** ten pravopis je, a proto ho nemůže nikdo ani zvolit, ani zkontrolovat.

**Cena toho mlčení je dnes vidět na výstupu, ne jen v kódu.** Převodní tabulka `ToNHibernate` má tři tvrzení, pro která NHibernate 5.7.0 neregistruje žádný typ: znaková data pevné délky jiné než jednoznakové, v obou unicode variantách, a neunicode velký text. `TypeFactory` jména `StringFixedLength`, `AnsiStringFixedLength` ani `AnsiStringClob` nezná — mapování s nimi by továrnu relací neshodilo až za běhu, nýbrž hned při vázání —, takže builder zapíše nejbližší registrované jméno a rozdíl vydá záznamem `Loss`. Mapování pak tvrdí *proměnnou* délku tam, kde zdroj řekl *pevnou*, a o neunicode velkém textu netvrdí neunicode vůbec.

Přitom NHibernate má kanál, který by to tvrzení unesl přesně: atribut `sql-type` na vnořeném `<column>`. Nástroj ho zapisuje, ale jen když ho nese zdroj (`SourceSqlType`, rozhodnutí 019 a 052); odvozený nezapisuje, protože zapsat ho znamená pojmenovat typ konkrétního databázového systému — a nástroj nedeklaruje, na který míří. Přesně tak to hranice záruk dnes zapisuje: **vyňatá oblast 5** říká, že jediným dialektem je SQL Server a že emise `sql-type` odvozeného z typové rodiny čeká na deklaraci dialektu v deskriptoru. Chybějící deklarace tedy není kosmetika; stojí věrnost u cíle, který věrný být umí.

**Dvě fakta téže vlastnosti, dva kanály.** Za vyslovení stojí, proč je `sql-type` vedle `type` správná odpověď a ne zdvojení: `type` je tvrzení o **mapování** — kterým `IType` se hodnota čte a zapisuje na straně .NET —, kdežto `sql-type` je tvrzení o **sloupci**, které NHibernate použije při exportu a validaci schématu. Mezireprezentace nese obojí odděleně už od 019 (rodina s facetami na jedné straně, jazykový typ vlastnosti na druhé), takže dvojice atributů není opis jedné věci dvakrát, nýbrž dvě tvrzení na svých místech.

**Čtyři věci dělají z otázky rozhodnutí, ne psaní kódu.**

**Čím dialekt je.** Deskriptor se podle rozhodnutí 009 váže na **uzavřené** množiny — jen nad nimi umí říct, co cíl vyjádřit umí, a přesně tenhle argument použilo 019, aby slovník `DatabaseType` uzavřený zůstal. Volný řetězec by tu vazbu zrušil dřív, než by kdokoli napsal první tabulku.

**Kde se deklarace vysloví.** Jednou pro nástroj, nebo jednou za framework? Vypadá to jako otázka o duplicitě — šest deskriptorů by dnes neslo tutéž hodnotu —, ale není: **cílový framework nepodporuje každý databázový systém.** NHibernate má konečný seznam dialektů, EclipseLink konečný seznam platforem, MyBatis nemá ani jedno, protože SQL píše uživatel. „Na jaký systém je tenhle artefakt napsaný" je tedy tvrzení o dvojici (framework, systém), ne o nástroji.

**Co deklarace smí změnit na výstupu.** Nic, jen to místo, kde vlastní slovník cíle tvrzení ztrácí, nebo všude, kde cíl doslovný typ napsat umí? Tahle otázka rozhoduje o povaze celého nástroje víc než obě předchozí: doslovný typ dělá artefakt **věrným** a zároveň **vázaným na jeden databázový systém**, a která z těch dvou vlastností má přednost, je volba, ne technikálie.

**Kde bydlí tabulka.** Poznámky v `JpaSqlTypeReading` i v `DatabaseTypeConvertor` slibují „sdílený projekt čtení T-SQL", a ten od rozhodnutí 082 existuje: `TransactSql`. Jenže slib byl psaný dřív, než ten projekt dostal podobu, a podoba tu otázku otevírá znovu.

**Co tohle rozhodnutí naopak neřeší, je zdrojová strana**, a otevřená položka to říkala předem. Doslovný typ ve **zdrojovém** artefaktu — `[Column(TypeName="money")]`, `columnDefinition`, `sql-type` v `hbm.xml` — je napsaný v dialektu zdrojového projektu, o kterém nástroj nemá žádnou deklaraci. Odpověď na to dalo už rozhodnutí 082 pro texty dotazů: v tomhle projektu je veškeré SQL T-SQL, jiný dialekt zůstává vyňatý a multidialektová knihovna je zamítnutá, protože by jiný dialekt přeložila tiše. Táž věta platí pro názvy typů.

## Zvažované varianty

### 1 — Deklarovat a nic tím nezměnit

Přidat do deskriptoru pole, vydat ho v záznamu běhu a nechat všechny emise, jak jsou. Nejlevnější a v jednom smyslu i nejpoctivější: nástroj by začal říkat, co dosud jen mlčky předpokládal.

Zamítáme. Pole, které nic nevybírá, netvrdí nic — je to dokumentace zapsaná v jazyce C#, a dokumentaci máme jinde. Hranice záruk by si první větu oblasti 5 nechala beze změny, tři záznamy o zúžení by zůstaly a otevřená položka by po odbavení vypadala stejně jako před ním. Rozhodnutí 019 sem položku poslalo právě proto, aby se emise odblokovala; deklarace bez ní je odklad pod jiným jménem.

### 2 — Dialekt jako volný řetězec

`Dialect = "SQL Server 2022"` vedle `Version = "5.7.0"`, tedy doslovná symetrie s verzí.

Zamítáme. Verze je řetězec proto, že ji **nikdo neinterpretuje**: nese se do záznamu běhu a do testu, který ji váže na načtený balík, a žádná větev kódu se na ni neptá. Dialekt je přesně naopak — je to vstup do výběru tabulky. Řetězec by tedy musel každý builder rozebrat, tedy vyrobit si vlastní neúplný výčet dialektů v místě použití, a mechanická kontrola, na které deskriptor stojí (009), by se neměla čeho chytit. Uzavřenou množinu si tu žádá totéž pravidlo, kterým 019 obhájilo uzavřenost `DatabaseType`.

### 3 — Jeden dialekt pro celý nástroj, mimo deskriptor

Konstanta v `Model` nebo `Common`, kterou si přečte, kdo potřebuje. Šest deskriptorů by neopakovalo tutéž hodnotu.

Zamítáme dvěma důvody. Za prvé to říká rozhodnutí 019 jinak — dialekt je fakt o cíli převodu a patří k verzi cílového frameworku. Za druhé, a podstatněji: **konstanta tvrdí, že podporovaný dialekt je vlastnost nástroje, a to není pravda.** Dialekt umí vyjádřit jedině cíl, a co který cíl umí, je právě obsahem deskriptoru. Až bude dialektů víc, bude NHibernate deskriptor jmenovat ty, pro které má registrovaný dialekt, a MyBatis deskriptor jiné; konstanta by se v ten den musela rozpadnout na šest polí, tedy udělat to, co se tímhle rozhodnutím udělat může hned a zadarmo. Že dnes všech šest hodnot souhlasí, je fakt o dnešku, ne tvar řešení — a hlídá ho test, ne typ.

### 4 — Deklarace a doslovný typ všude, kde ho cíl unese

Deskriptor deklaruje dialekt a každý cíl, který má kam napsat doslovný typ sloupce, ho napíše vždy: NHibernate `sql-type` u každé vlastnosti s rodinou, EF Core `[Column(TypeName=…)]` odvozené z dialektu, JPA `columnDefinition` u každého sloupce. Výstup by byl maximálně určitý a nikdy by nezáležel na tom, co si cílový framework domyslí.

Zamítáme třemi důvody.

**Tvrdilo by se víc, než řekl zdroj.** Vlastnost, o jejímž sloupci zdroj řekl jen „celé číslo", by v artefaktu dostala `int` — tedy volbu nástroje vydávanou za fakt. Rozhodnutí 067 rozlišuje odvozenou konvenci, která je tvrzením a hlásí se, od výchozí hodnoty, která tvrzením není; tahle varianta z každého sloupce dělá to první a zaplavila by diagnostiku záznamy, které nikomu nic neřeknou.

**Vázalo by to každý artefakt na jeden systém.** Generovaná JPA entita s `columnDefinition` u každého sloupce se stává nepřenositelnou mezi databázemi, ačkoli JPA samo přenositelné je a ačkoli o tu přenositelnost nikdo nežádal. Cíl, který si typ sloupce odvodí z jazykového typu sám a správně, nemá důvod ho slyšet od nás.

**U EF Core by to zrušilo rovnováhu, kterou nastavilo 052.** To rozhodnutí dalo přednost doslovnému zápisu **zdroje** před odvozeným jménem právě s odůvodněním, že odvozený tvrdí typ, který jsme vybrali my. Rozšířit odvozený na všechna místa by tu větu obrátilo.

### 5 — Deklarace a doslovný typ jako náhradní kanál

Deskriptor deklaruje dialekt z uzavřené množiny a doslovný typ se zapisuje **právě tam, kde by vlastní typový slovník cíle tvrzení změnil**.

## Rozhodnutí

**Volíme variantu 5. Cílový databázový dialekt deklaruje deskriptor cílového frameworku jako hodnotu uzavřeného slovníku, a doslovný typ toho dialektu se do artefaktu zapisuje právě tam, kde vlastní typový slovník cíle tvrzení modelu neunese.**

### Co se deklaruje

Do `Model` přibývá výčet `DatabaseDialect` s jedinou hodnotou, `SqlServer2022`. Stojí vedle `ORMEnum`, tedy **ne** v `Model.AbstractRepresentation`: obojí je slovník, kterým deskriptor mluví o cíli převodu, a ani jedno není faktem mezireprezentace — přesně jak to 019 o dialektu napsalo. Číslování po desítkách je totéž jako u `ORMEnum`, aby se druhý dialekt dal zařadit mezi, ne jen za.

`TargetFrameworkDescriptor` dostává `required DatabaseDialect Dialect` vedle `Version` a všech šest deskriptorů ho vysloví. Povinnost je záměrná a je to týž mechanismus jako u úplnosti `Support`: přidání druhé hodnoty dialektu nesmí nikde propadnout do tiché výchozí volby.

**Deklaruje i Dapper, který žádný typ sloupce nepíše.** Není to formalita: deskriptor popisuje cíl, ne jen to, co cíl zapisuje, a `FactSupport.NotExpressible` u kategorie `DatabaseType` už samo říká, že Dapper ten kanál nemá. Dvě různá tvrzení na dvou místech jsou přesnější než jedno chybějící.

**Záznam běhu dialekt vydává** (S6). `ConversionResult` dostává `TargetDatabaseDialect` vedle `TargetFrameworkVersion` a bere ho z téhož deskriptoru, takže záznam nemůže tvrdit nic jiného než generátor — táž věta, jakou 013 napsalo o verzi. Konzument tím dostane odpověď na otázku, kterou dosud musel uhodnout: pro jakou databázi je artefakt, který si odnáší.

### Kde bydlí pravopis dialektu

Tabulka „rodina a facety → název typu" a její čtecí protějšek dostávají jediné bydliště: **`Common`, jmenný prostor `Common.Sql`.** Vydává tři věci:

- `SqlTypeReading` — jedno čtení doslovného názvu do neutrálního slovníku (rodina, facety, které název sám tvrdí, a příznak, že spelling patří na únikovou cestu). Typ se přesouvá z `EFCoreWrappers.Convertors` beze změny tvaru a `JpaSqlTypeReading` v něm zaniká.
- `SqlTypeSpelling.Read(string)` — čtecí směr.
- `SqlTypeSpelling.Name(dialekt, rodina, unicode)` a `SqlTypeSpelling.Literal(dialekt, rodina, unicode, délka, přesnost, měřítko)` — zapisovací směr ve dvou tvarech. `Name` vrací **holé jméno** bez argumentů, protože EF Core nese délku a přesnost vlastními anotacemi; `Literal` vrací **úplný doslovný typ** včetně argumentů, protože `sql-type` i `columnDefinition` jsou definice sloupce a jméno bez argumentu je jiný sloupec. `Literal` vrací `null`, když model nenese facetu, kterou by název potřeboval — `nchar` bez délky je v T-SQL `nchar(1)`, tedy tiché zúžení na jeden znak, a vymyslet si délku je horší než nenapsat nic.

**Proč `Common`, a ne `TransactSql`.** Poznámky v obou dnešních kopiích slibují „sdílený projekt čtení T-SQL" a ten projekt existuje; slib byl ale psaný, než dostal podobu. `TransactSql` je projekt postavený kolem **gramatiky** — nese `Microsoft.SqlServer.TransactSql.ScriptDom`, čte `SELECT` do builderu a zapisuje instrukce do textu. Pravopis názvu typu s gramatikou nesouvisí: je to tabulka dvaceti rodin. Dát ji tam by znamenalo dvě věci, obě špatné. Jednak by `EFCoreWrappers` a `JakartaPersistence` — a s ním oba javové JPA wrappery — musely referencovat parser T-SQL kvůli tabulce jmen, tedy táhnout gramatiku do projektů, které SQL nikdy nečtou. Jednak, a hlavně, **druhý dialekt by pak bydlel uvnitř projektu pojmenovaného po gramatice toho prvního**: tabulka jmen PostgreSQL v `TransactSql` je nesmysl, který by se musel hned napravovat. `Common` je naopak popsané jako frameworkově nezávislé konvertory sdílené všemi buildery, `AbstractWrappers` ho už referencuje, takže **každý wrapper ho má tranzitivně** a nikam neputuje žádný nový balíček.

**Rozhodnutí 014 to neporušuje**, ačkoli jeho věta zní podobně. Tam se do `Common` nepustila tabulka **jazykový typ → databázový typ**, a to s odůvodněním, že je to výchozí předpoklad konkrétního frameworku — `GuessFromScalarType` NHibernate a protějšek EF Core se liší a liší se právem. Tady jde o tabulku **rodina → název typu databázového systému**, na frameworku nezávislou z definice. Je to tentýž tvar, jaký rozhodnutí 050 zvolilo pro heuristiku jednotného a množného čísla: jediné místo pro wrappery i pro `DatabaseCatalog`.

### Čtení dialekt nevybírá

`SqlTypeSpelling.Read` **žádný dialekt nebere** a je to vědomá nesouměrnost. Zapisovací směr vybírá deklarovaný cílový dialekt; čtecí směr čte jediné SQL, které tenhle nástroj zná. Tu větu vyslovilo rozhodnutí 082 o textech dotazů a tady platí doslova o názvech typů: `money` v `[Column(TypeName=…)]`, `datetime2(3)` v `columnDefinition` i `char(2)` v `sql-type` se čtou jako T-SQL, protože jiný dialekt nástroj nečte. Předstírat, že zdrojový artefakt čteme v dialektu **cíle**, by byla nepravda: zdroj s cílem nemá nic společného.

Až dialekty přibudou, bude zdrojová strana potřebovat deklaraci vlastní — to je otázka, kterou otevřená položka avizovala a kterou tohle rozhodnutí vrací do [`open-items.md`](../open-items.md) jako novou položku, ne jako mlčení.

### Kdy se doslovný typ zapíše

Pravidlo má jednu větu a tři upřesnění.

**Doslovný typ deklarovaného dialektu se zapíše právě tehdy, když by vlastní typový slovník cíle tvrzení modelu změnil.** Je to náhradní kanál, ne hlavní: mluví se jím teprve tam, kde první kanál mlčí nebo lže.

**Doslovný typ zdroje má přednost před odvozeným.** Nese-li model `SourceSqlType`, zapíše se on a nic se neodvozuje — pravidlo rozhodnutí 052 platí beze změny a nově z něj plyne i to, že **se v takovém případě nevydá záznam o zúžení**: tvrzení je unesené, jen jiným kanálem. Dnes ten záznam vzniká i tam, kde doslovný typ vedle sebe stojí, a je to tichá nepřesnost, kterou tohle rozhodnutí odstraňuje.

**Odvozený doslovný typ je tvrzení cíle a hlásí se jím** (rozhodnutí 067 a 010). Kategorie je `DatabaseType`, druh `Convention` — ne `Loss`, protože se nic neztratilo, a ne mlčení, protože artefakt nově jmenuje typ konkrétního databázového systému, který zdroj nenapsal. Záznam jmenuje dialekt i napsaný typ.

**Nenese-li model facetu, kterou by název potřeboval, neodvozuje se nic** a zůstává dnešní stav: nejbližší jméno slovníku cíle a záznam `Loss` o změněném tvrzení. Znaková data pevné délky bez délky jsou přesně ten případ.

**Náhradní kanál nesmí vyrobit mapování, které se rozejde samo se sebou.** Tohle je mez, bez které by pravidlo bylo nebezpečné, a stojí za vysvětlení, protože se nedá odvodit z tvaru záznamu. Zúžení mají dvě povahy. U znakových dat mění náhradní jméno **facetu** — pevnou délku na proměnnou, unicode na nic —, ale zapsaný `IType` pořád váže znakovou hodnotu proti znakovému sloupci, takže doslovný sloupec vedle něj tvrzení doplní a nic nerozbije. U temporálního ústupu z rozhodnutí 071 mění náhradní jméno **rodinu**: když 5.7.0 nemá typ, který by četl `Duration` ze sloupce rodiny `BigInt`, zapíše se `DateTime` — a `sql-type="bigint"` vedle něj by dalo mapování, které NHibernate při stavbě továrny přijme a při první hydrataci shodí. Vydat místo chudšího artefaktu artefakt rozbitý je horší výsledek než dnešek.

**Odpovědět na to umí jedině převodní tabulka, protože jedině ona ví, co nahradila.** `NHibernateTypeNaming` proto vedle důvodu zúžení nese i příznak, jestli doslovný sloupec tvrzení obnoví; tři znakové řádky ho mají, temporální ústup ne. Builder se neptá, jak velké zúžení je — to by bylo hádání —, nýbrž čte odpověď od toho, kdo ji zná.

### Kde se to v šesti cílech projeví

| Cíl | Kanál doslovného typu | Co se mění |
|---|---|---|
| NHibernate | `sql-type` na vnořeném `<column>` | tři zúžení `ToNHibernate` se stávají věrnými: `char(n)`, `nchar(n)` a `text` |
| EF Core | `[Column(TypeName=…)]` | nic; jméno rodiny se nově bere z tabulky dialektu místo z kopie ve wrapperu |
| Hibernate, EclipseLink | `columnDefinition` na `@Column` | nic; nationalizační háček EclipseLinku je jediná instance pravidla a nově čte tabulku dialektu |
| MyBatis | žádný | nic; `jdbcType` jmenuje rodinu JDBC, ne typ jednoho systému |
| Dapper | žádný | nic; POCO typ sloupce nevyslovuje vůbec |

**NHibernate je jediný cíl, kde se výstup mění**, a mění se právě tam, kde dosud vznikal záznam o zúžení:

| Tvrzení modelu | Dnes | Nově |
|---|---|---|
| `Char`, unicode, délka *n* ≠ 1 | `type="String" length="n"` + `Loss` | `type="String"` + `<column length="n" sql-type="nchar(n)"/>` + `Convention` |
| `Char`, neunicode, délka *n* ≠ 1 | `type="AnsiString" length="n"` + `Loss` | `type="AnsiString"` + `<column length="n" sql-type="char(n)"/>` + `Convention` |
| `Text`, neunicode | `type="StringClob"` + `Loss` | `type="StringClob"` + `<column sql-type="text"/>` + `Convention` |
| `Char` bez délky | `type="String"` + `Loss` | beze změny |
| temporální ústup rozhodnutí 071 | nejbližší jméno + `Loss` | beze změny |

`type` přitom zůstává, jaké bylo. Není to opomenutí: jméno registrované v `TypeFactory` říká, kterým `IType` se hodnota na straně .NET čte a zapisuje, a to se doslovným typem sloupce nemění. Dvojice `type="AnsiString" sql-type="char(2)"` je tedy přesně ta dvě tvrzení, která model nese — neunicode znaková data na straně databáze, pevná délka dvou znaků na straně sloupce.

**EclipseLink se tím dostává pod totéž pravidlo, pod kterým už fakticky byl.** Rozhodnutí 080 dalo nationalizaci doslovný typ proto, že EclipseLink pro unicode nemá anotaci na žádné úrovni — tedy přesně proto, že vlastní slovník cíle tvrzení neunese. Je to jediná instance obecného pravidla, které tímhle rozhodnutím dostává jméno, a `JpaSqlTypeWriting` si po přesunu tabulky ponechává jen to, co je opravdu JPA: odvození znakové rodiny z jazykového typu a výchozí délku 255, kterou dává `@Column#length` a kterou wrapper hlásí `Convention`.

### Čtečka katalogu zůstává, kde je

`SqlServerCatalogReader.MapType` se do `Common.Sql` **nestěhuje**, ačkoli hláskuje týž jazyk. Dva důvody, oba věcné.

**Nečte název, čte řádek.** Katalog uvádí každou facetu ve vlastním sloupci `sys.columns`, takže čtečka bere z názvu typu jedině rodinu a unicode a délku, přesnost i měřítko dopočítává z čísel vedle — včetně dělení dvěma u unicode znakových sloupců a přesunu zlomkové vteřiny ze `scale` do `Precision`. Sdílený čtenář názvu by jí z toho neuměl pomoci s ničím a společné rozhraní by muselo nést oba tvary vstupu.

**Čte jinou databázi než tu, o které mluví deskriptor.** Katalog je zdroj faktů, na který se nástroj připojil; deklarovaný dialekt je vlastnost cíle. Že jsou dnes oba SQL Server, je shoda okolností, ne tvrzení, a spojit je do jednoho místa by tu shodu proměnilo v předpoklad.

Zůstávají tím v řešení **dvě** hláskování T-SQL místo dnešních čtyř, a to druhé je vysvětlené. Že se tabulky v jedné drobnosti dnes liší — `image` je v čtečce katalogu rodina bez úniku, v čtenáři doslovných názvů rodina s úniknutým spellingem — je zaznamenané a při implementaci se **nesjednocuje**: čtečka katalogu doslovný název nepotřebuje, protože ho dopočítá z řádku, a měnit její chování při přesunu cizí tabulky by byla změna schovaná v refaktoru.

### Co se nemění

`MappingFactCategory` nedostává hodnotu: dialekt není mapovací fakt, o kterém by se dalo říct „cíl ho neumí vyjádřit", nýbrž volba cíle. `DatabaseType` zůstává uzavřený slovník dvaceti rodin a facety zůstávají facetami; tohle rozhodnutí do typového modelu nesahá vůbec, což je přesně ta hranice, kterou 019 nakreslilo. Nemění se ani `IQueryVisitor`, orchestrace ani rozhraní parserů a builderů. Čtení `sql-type` v `NHibernateXMLMappingParser` zůstává, jak je — doslovný název jde na únikovou cestu a rodina se z něj nevyčítá —, protože „v jakém dialektu je tenhle název" je otázka zdrojové strany.

## Důsledky

**Vyňatá oblast 5 se zmenšuje a zbytek se vysloví jinak.** Věta o `sql-type` odvozeném z typové rodiny z hranice záruk mizí, protože se to nově děje. Co v oblasti zůstává, jsou `CHECK` a výchozí hodnoty sloupce — doslovné SQL výrazy, které mezireprezentace nenese a které dostávají záznam o ztrátě — a **jediný dialekt**, nově jako **deklarovaná** mez místo mlčky předpokládané. Rozdíl není slovíčkaření: dosud byl SQL Server předpokladem, který si čtenář musel dohledat v kódu, nově je to hodnota v deskriptoru, kterou vydává záznam každého běhu.

**Tři záznamy o zúžení mizí z výstupu NHibernate a jeden záznam o konvenci přichází.** Pro T3 a pro matici T2 je to zlepšení téhož druhu, jaké přineslo 052 u EF Core: buňka, která dosud měřila překlad hlásící ztrátu, měří překlad, který ztrátu nemá.

**Round-trip NHibernate → NHibernate se u znakových dat pevné délky uzavírá, a to už na druhém průchodu.** První převod vydá `type="AnsiString" sql-type="char(2)"` se záznamem `Convention`; zpětné čtení dá rodinu `VarChar` a doslovný `char(2)` na únikové cestě; druhý převod zapíše týž doslovný typ, protože podle 052 má přednost, a **žádný záznam už nevydá** — první převod tvrzení vyslovil, druhý jen opakuje, co mu řekl zdroj. Výstup druhého průchodu je bajtově týž jako výstup prvního, což je vlastnost, kterou lze otestovat a na kterou S2 míří.

**Pro sedmý framework (S1) přibývá jedna deklarace a žádný háček.** Nový deskriptor vysloví dialekt stejně, jako dnes vyslovuje verzi a úplnou tabulku podpory; nová tabulka jmen nevzniká, dokud nepřibude dialekt, a ten je věcí položky níž, ne wrapperu. Abstraktní buildery se nemění.

**Přibývá pole v odpovědi `/convert`.** `TargetDatabaseDialect` je přírůstek, ne změna existujícího pole, takže REST kontrakt zůstává zpětně kompatibilní; podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to **MINOR** — přibývá schopnost a s ní pozorovatelný fakt —, a vyjde s vydáním, které podle [`open-items.md`](../open-items.md) cíl 2 vydá před `2.0.0`.

**Druhý dialekt se otevírá jako položka, ne jako slib.** Zapisujeme do `open-items.md` rozhodnutí, které bude muset vyslovit trojí: čím se dialekt volí (deskriptor dnes nese jeden a volba z rozhraní neexistuje), jak se deklaruje dialekt **zdrojového** artefaktu, a jestli se přitom mění zamítnutí multidialektové knihovny z rozhodnutí 082 — to zamítnutí stálo na tom, že jiný dialekt by se přeložil tiše, a deklarace je právě ta věc, která tichost odstraňuje. Dokud ta položka nepadne, je jediným dialektem SQL Server 2022 a nástroj nic víc netvrdí; nově to ale sám říká.

**Testy.** Že všech šest deskriptorů deklaruje dialekt a že dnes deklarují týž — jedním testem, po vzoru vazby verzí na balíčky, takže druhá hodnota si vynutí rozhodnutí a ne tichý rozchod. Nad `CharacterTypeVerificationTest`, který tři zúžení drží od rozhodnutí 019: tři vlastnosti nově vydají `sql-type="nchar(3)"`, `sql-type="char(2)"` a `sql-type="text"`, počet záznamů `Loss` kategorie `DatabaseType` klesne na nulu a nahradí je tři `Convention`; mapování dál projde schématem `nhibernate-mapping` a NHibernate 5.7.0 z něj dál postaví továrnu relací — což je jediný stupeň, který potvrdí, že `sql-type` na těch místech opravdu přijímá. Znaková data pevné délky **bez** délky dál vydají jen nejbližší jméno a `Loss`. Vlastnost, která nese doslovný typ zdroje i rodinu se zúžením, vydá doslovný typ zdroje a **žádný** záznam. Druhý průchod téhož mapování — přečtená entita i přečtené mapování — vydá bajtově týž dokument a nulu záznamů kategorie `DatabaseType`. U EF Core, Hibernate, EclipseLinku, MyBatisu a Dapperu projdou všechny dosavadní testy beze změny očekávaných výstupů — to je definice toho, že se tabulka přestěhovala a nic se nepřepsalo —, a nationalizace EclipseLinku dál vydává `ntext`, `nchar(n)` i výchozí `nvarchar(255)` se svým záznamem.
