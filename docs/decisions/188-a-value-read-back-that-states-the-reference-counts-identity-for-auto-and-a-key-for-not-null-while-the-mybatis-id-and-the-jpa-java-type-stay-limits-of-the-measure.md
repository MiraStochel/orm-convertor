# 188 — Zpětné čtení uzná hodnotu, která fakt reference vyslovuje: `Identity` za `Auto` a klíč za NOT NULL svých sloupců; `<id>` MyBatisu a databázový typ, který JPA nechává javovému typu, zůstávají vyslovenou mezí míry

Datum: 2026-10-25
Stav: platí; nahrazuje srovnání hodnot a osud odchylek zpětného čtení v oddílu *Úplnost přenesených mapovacích vlastností* rozh. 173 a výčet odchylek v zúžení T3 rozh. 183
Požadavky: T3, T4
Podklad: rozhodnutí [054](054-nullable-key-part-is-a-reported-loss.md), [063](063-stated-keylessness-as-a-carried-fact.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md), [129](129-sql-server-2022-stays-the-only-database-dialect.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [183](183-t2-and-t3-leave-the-exempt-area-in-a-narrower-sense-the-build-holds-the-category-matrix-and-the-run-measures-the-metrics-without-promising-numbers.md) a [187](187-auto-is-the-mechanism-native-to-the-dialect-the-jpa-parser-resolves-auto-by-the-source-implementation-and-ef-core-is-read-and-written-by-what-its-provider-does.md); dokumentace připnutých frameworků a SQL Serveru (*Co říká dokumentace*); měření nad připnutými verzemi z 2026-10-25 (*Co jsme změřili*); kód `Tests/Experiment/MappingCompleteness.cs`, `MappingCompletenessTest.cs`; otevřená položka *Odchylky zpětného čtení v úplnosti mapování*

## Kontext

Úplnost přenesených mapovacích vlastností, poslední metriku T3, měří od rozhodnutí 173 zpětné čtení: artefakt entit a mapování přečte parser cílového frameworku zpět do mezireprezentace a fakta se porovnají s knihou tvrzení převodu. Fakt je přenesený, když zpětné čtení drží týž předmět s touž hodnotou; hodnoty se srovnávají, jak je kniha píše, s úlevou pro rodinu databázového typu a se sloupci cizího klíče a unikátního omezení jako množinou. Kde kniha vede fakt jako nesený a parser ho nepřečte, stojí odchylka jménem v `Experiment/MappingCompletenessTest`; rozhodnutí 173 o nich řeklo, že jsou předmětem míry a kandidátem na práci, a rozhodnutí 183 je vyjmenovalo v zúžení T3. Nad scénářem `CrossFrameworkInputs` jsou po rozhodnutí 187 čtyři druhy:

| Směry | Fakt reference | Co artefakt nástroje napíše | Co z něj parser cíle přečte |
|---|---|---|---|
| EF Core, NHibernate, Hibernate, EclipseLink → MyBatis | primární klíč | `<id>` výsledkové mapy | nic: `<id>` je identita výsledku, ne klíč (rozh. 084) |
| EF Core → NHibernate, Hibernate, EclipseLink | NOT NULL klíčového sloupce | `<id>`, `@Id` bez `not-null` či `nullable` — oba buildery tvrzení na klíč záměrně nepíšou | klíč, nullabilitu žádnou |
| EF Core → Hibernate, EclipseLink | strategie `Auto` | `GenerationType.IDENTITY` (rozh. 187) | `Identity` |
| NHibernate → Hibernate, EclipseLink | databázový typ `Integer`, `Decimal`, u Hibernatu i `VarChar` | javový typ `Integer`, `BigDecimal`, `String`; `columnDefinition` jen tam, kde ho slovník potřebuje | žádný typ, u `String` jen facetu unicode |

Úplnost mapování javových cílů a MyBatisu vychází o tato fakta nižší, než co artefakt nese — u obou implementací JPA podle kontroly rozhodnutí 173 skoro o celý rozdíl proti cílům .NET. Srovnání s jazykovým modelem podle T4 to nekřiví, protože tutéž mez dostane artefakt modelu; křiví to tabulku T3, která cíle staví vedle sebe. **Je třeba rozhodnout po odchylce**, zda ji má číst parser cíle, zda ji má měřidlo uznat za přečtenou, nebo zda zůstane vyslovenou mezí míry.

### Co říká dokumentace

| Zdroj | Co říká |
|---|---|
| SQL Server, *CREATE TABLE*, PRIMARY KEY constraints | „All columns defined within a PRIMARY KEY constraint must be defined as NOT NULL. If nullability isn't specified, all columns participating in a PRIMARY KEY constraint have their nullability set to NOT NULL." |
| Hibernate ORM 7.4 User Guide, *Identifiers* | o sloupcích identifikátoru předpokládají Hibernate i JPA „NOT NULL — The values cannot be null. For composite ids, no part can be null." |
| Jakarta Persistence 3.2, §2.4 a `@Column` | „Every entity must have a primary key"; o nullabilitě klíčového sloupce nic, a `nullable` má výchozí hodnotu `true` |
| NHibernate reference, `<id>` a `<composite-id>` | „Mapped classes must declare the primary key column of the database table"; o nullabilitě nic |
| EF Core, *Keys* a *Entity Properties* | klíč odpovídá primárnímu klíči relační databáze; o nullabilitě klíčové vlastnosti nic, nullabilita jinak plyne z typu a z anotací nullability |
| MyBatis 3, *Mapper XML Files*, `<id>` | „id will flag the result as an identifier property to be used when comparing object instances"; a primárním klíčem jen proto, že je „an obvious choice (even if composite)" |
| Hibernate ORM 7.4 User Guide, *Basic values* | „By default, Hibernate maps `String` to the `VARCHAR` JDBC type", `Integer` na `INTEGER`, `BigDecimal` na `NUMERIC`; `@Nationalized` přepne na národní variantu |

### Co jsme změřili

Na otázku, zda je klíčový sloupec NOT NULL, odpovídá jednoznačně jen dokumentace SQL Serveru a příručka Hibernatu; reference NHibernatu ani dokumentace EF Core ji nekladou a specifikace JPA ji svou výchozí hodnotou `nullable = true` spíš zamlžuje. Fakt jsme proto změřili nad připnutými verzemi a nad tvary, které buildery píšou — jednoduchý klíč přidělovaný aplikací, klíč nad textem, dvoudílný klíč —, každý nad typem, který je sám o sobě nullable, a s kontrolním sloupcem téhož typu mimo klíč, aby bylo vidět, že sloupec NOT NULL dělá klíč, ne jazykový typ:

| Framework | Kde | Klíčový sloupec bez tvrzení o nullabilitě | Týž typ mimo klíč |
|---|---|---|---|
| EF Core 10.0.10, SQL Server provider | `Experiment/KeyColumnNullabilityProviderTest`: model bez připojení a `GenerateCreateScript` | `IsNullable` je false a skript píše `NOT NULL` — i nad `int?` a nad `string` bez anotací nullability, jednoduchý klíč i obě části `[PrimaryKey]` | nullable, `NULL` |
| NHibernate 5.7.0, `MsSql2012Dialect` | tamtéž: konfigurace po stavbě továrny a `GenerateSchemaCreationScript` | `Column.IsNullable` je false a skript píše `not null` — `<id>` nad `Int32` i `String` a obě části `<composite-id>` | nullable |
| Hibernate 7.4.5 | `hibernate/KeyColumnNullabilityClaimsTest` javové sady: skript generování schématu | `not null` — `@Id` nad `Integer` i `String` a obě části `@IdClass` | bez `not null` |
| EclipseLink 5.0.0 | `eclipselink/KeyColumnNullabilityClaimsTest`: skript generování schématu nad připojením | `NOT NULL` — tytéž tři tvary | bez `NOT NULL` |

Klíč tedy vyslovuje NOT NULL svých sloupců ve všech čtyřech cílech, které vyjádří klíč i nullabilitu; MyBatis nullabilitu nevyjádří a Dapper ani klíč. Je to tentýž fakt, ze kterého vycházejí oba buildery, když tvrzení na klíč nepíšou („a column that carries the identifier is not nullable" builderu NHibernatu, „a key column is never nullable" builderu JPA), a databázová půlka faktu, jehož jazykovou půlku drží rozhodnutí 054: žádný cíl nepřipouští nullable identifikátor.

Co z `Auto` a `Identity` na jediném dialektu vznikne, změřilo rozhodnutí 187 (`NHibernateGeneratorProviderTest`, `EFCoreValueGenerationProviderTest`, `KeyGenerationClaimsTest` obou implementací JPA): `IDENTITY` JPA je v obou implementacích sloupec IDENTITY a `Auto` je podle 187 právě sloupec IDENTITY, který zdroj nepřipnul.

## Zvažované varianty

**1 — Ať každou odchylku čte parser cíle.** Zamítáme ji po odchylce, protože u každé už platí rozhodnutí, které parseru takové čtení zakazuje, a z důvodu, který měřidlo nemění:

- *`<id>` jako klíč* — rozhodnutí 084: `<id>` je příznak identity výsledku, ne deklarace klíče, a dokumentace MyBatisu to potvrzuje, když klíč jmenuje jen jako „obvious choice". Materializovaný klíč by navíc ve fázi doplnění proměnil tiché `Supplied` ve falešný `Conflict` proti skutečnému klíči schématu (rozh. 063, 067).
- *NOT NULL z klíče* — neprojde testem mezery rozhodnutí 067. Prázdnou nullabilitu klíče doplní katalog touž hodnotou, protože sloupec primárního klíče je na SQL Serveru NOT NULL, a každý cíl, který klíč vyjádří, udělá sloupec NOT NULL sám (změřeno). Mezera tedy nikam jinam neputuje a materializace by smazala jen signál „nikdo neřekl", na kterém stojí přírůstkový zápis i priorita zdrojů.
- *`Identity` jako `Auto`* — parser JPA čte `IDENTITY` správně: je to připnutý sloupec IDENTITY, tedy `Identity` (rozh. 187). Číst ho jako `Auto` by zdroji JPA vzalo připnutí, které vyslovil.
- *databázový typ z javového typu* — rozhodnutí 067 jmenuje právě toto odvození, typ, který provider odvodí z jazykového typu, mezi těmi, která se nematerializují; a jako tvrzení zdroje by `String` nad sloupcem `nvarchar` vyrobil `Conflict` proti katalogu tam, kde dnes stojí `Supplied`.

**2 — Nechat všechny čtyři vyslovenou mezí míry**, jak je nechalo rozhodnutí 173. Zamítáme. Dvě z odchylek nejsou místem, kde by artefakt fakt nevyslovil, ale místem, kde je srovnání přísnější než slovník, ve kterém srovnává. Zpětné čtení tam drží hodnotu, která fakt reference ve slovníku mezireprezentace vyslovuje, a to ne pravidlem jednoho frameworku, ale pravidlem, které platí v každém cíli: klíč bez tvrzení o nullabilitě je ve všech čtyřech cílech, které ho vyjádří, sloupcem NOT NULL, a `Identity` je na jediném dialektu týž sloupec jako `Auto`, jen připnutý. Počítat je za nepřenesené znamená hlásit ztrátu, kterou žádný čtenář artefaktu nevidí — framework ne (změřeno) a databáze ne —, a tabulka T3 by pak cíle řadila podle toho, zda jejich zápis klíče nese slovo navíc. U strategie je to ještě ostřejší: `IDENTITY` je jediný zápis, kterým jazyk JPA `Auto` vysloví (rozh. 187), takže by žádný artefakt JPA — nástroje ani modelu — `Auto` přenést nemohl, ať by ho napsal jakkoli.

**3 — Ať měřidlo čte každý fakt tak, jak ho čte framework cíle, i tam, kde parser nemá kam ho zapsat.** Měřidlo by samo četlo `<id>` výsledkové mapy MyBatisu jako klíč a javový typ JPA jako rodinu databázového typu podle tabulky implementace. Zamítáme. Měřidlo by se stalo druhým čtenářem frameworku vedle jeho parseru, tedy přesně tím, čemu rozhodnutí 173 uhnulo, když míru definovalo jako to, co z artefaktu přečte parser téhož frameworku. Znalost, kterou by k tomu potřebovalo, by buď převzalo od builderu — a nástroj by pak soudil sám sebe, protože builder JPA nechává typ javovému typu právě proto, že ho za jeho vyslovení považuje —, nebo by ji drželo samo jako druhou tabulku faktů frameworku, kterou by bylo třeba měřit a udržovat vedle parseru, který tytéž zápisy čte jinak. A u `<id>` by měřidlo četlo do artefaktu tvrzení, které framework sám nečiní.

**4 — Uznat hodnotu, která fakt reference vyslovuje pravidlem slovníku mezireprezentace platným v každém cíli, a ostatní odchylky vyslovit jako mez míry.** Volíme, níž.

## Rozhodnutí

**Volíme variantu 4. Fakt je přenesený, když zpětné čtení drží týž předmět s hodnotou, kterou srovnání uzná, nebo s hodnotou, která fakt reference vyslovuje: strategie klíče `Identity` vyslovuje `Auto` a část klíče, o jejíž nullabilitě zpětné čtení nic neříká, vyslovuje NOT NULL svého sloupce. `<id>` MyBatisu jako klíč a databázový typ, který JPA nechává javovému typu, zůstávají vyslovenou mezí míry — nečte je parser ani měřidlo — a patří mezi práce, které dělat nebudeme.**

Kritérium, které obě úlevy odlišuje od obou mezí, je jedno: úleva se smí opřít jen o pravidlo, které platí ve slovníku mezireprezentace samém — nad jediným dialektem a v každém cíli, který danou kategorii vyjádří —, a ne o znalost jednoho frameworku, kterou by měřidlo muselo držet vedle jeho parseru. Obě úlevy kritériem projdou, obě meze ne.

### `Identity` vyslovuje `Auto`

Hodnota `Auto` podle rozhodnutí 187 tvrdí, že úložiště vytvoří hodnotu mechanismem nativním pro dialekt, na SQL Serveru 2022 sloupcem IDENTITY, a že ho zdroj nepřipnul; `Identity` tvrdí týž sloupec připnutý. Na jediném dialektu nástroje (rozh. 129) tedy artefakt, ze kterého se zpět přečte `Identity`, vyslovuje všechno, co reference `Auto` říká, a navíc připnutí. Obráceně úleva neplatí: `Auto` přečtené tam, kde reference drží `Identity`, připnutí ztrácí a zůstává nepřeneseným faktem, jak řeklo rozhodnutí 187. A úleva nepřijme nic jiného: artefakt, který klíč EF Core napíše jako `AUTO`, se zpět přečte profilem implementace jako sekvence či tabulka čítače a fakt nepřenese — právě tu tichou změnu mechanismu, kterou rozhodnutí 187 našlo, a kterou tak měřidlo u artefaktu modelu uvidí.

### Klíč vyslovuje NOT NULL svých sloupců

Část klíče, o jejíž nullabilitě zpětné čtení nic neříká, přenáší NOT NULL, které reference u jejího sloupce drží. Pravidlo stojí na měření výš, ne na builderu: že oba buildery tvrzení na klíč nepíšou, je důsledek téhož faktu, ne jeho důkaz. Uplatní se jen tam, kde zpětné čtení nullabilitu nevyslovilo — co parser přečetl, zůstává jeho čtením i na klíči, takže artefakt, který klíčový sloupec výslovně prohlásí za nullable, fakt nepřenese. A klíč nevyslovuje nic jiného než NOT NULL: reference, která by u klíčového sloupce držela `nullable`, se jím nepřenese.

Pravidlo patří do měřidla, a ne do parseru, z téhož důvodu, z jakého ho parser číst nemá. V řetězci převodu mezera nikam neputuje, takže materializace nic nezíská; v měřidle je naopak klíč jediným místem, kde artefakt NOT NULL svého klíčového sloupce vysloví, a srovnání, které ho tam nevidí, měří slovo, ne fakt.

### Co zůstává vyslovenou mezí míry

| Odchylka | Proč ne parser | Proč ne měřidlo | Co z toho plyne |
|---|---|---|---|
| `<id>` MyBatisu jako klíč | rozh. 084 a 063: `<id>` je identita výsledku a klíč z něj by blokoval doplnění z katalogu | měřidlo by četlo tvrzení, které MyBatis nečiní, druhým čtením jeho mapperu | každý artefakt MyBatisu s klíčem je o části klíče kratší, u nástroje i u modelu stejně, takže srovnání podle T4 to nemění |
| databázový typ, který JPA nechává javovému typu | rozh. 067: typ odvozený providerem z jazykového typu se nematerializuje; u `String` nad `nvarchar` by vznikl falešný `Conflict` | měřidlo by drželo tabulku typů implementace — převzatou od builderu, tedy nástroj soudící sám sebe, nebo vlastní, druhou vedle parseru | podíl obou implementací JPA je nižší o databázové typy, které artefakt vyslovil jen javovým typem; rozdělení po kategoriích faktů je ukáže zvlášť |

Obě meze jsou vlastností míry, ne artefaktu: artefakt fakt nese, ale tvarem, který mezireprezentace nemá kam zapsat. Stojí jménem v `MappingCompletenessTest` — nová odchylka test shodí a odchylka, která zmizí, také —, a záznam běhu je u podílů MyBatisu a JPA jmenuje.

### Co úlevy nejsou

Nejsou obecným odvozováním. Jsou dvě, každá jmenovaná a změřená a s testem, který selže, změní-li povýšení frameworku fakt, na kterém stojí; další úleva je nové rozhodnutí. Jmenovatel (fakta, která deskriptor cíle vyjádří), sloupec `carried` (tvrzení knihy) ani sloupec `invented` se nemění, a stejné úlevy platí pro artefakt nástroje i pro cizí, protože měřidlo je jedno (rozh. 172, 173).

## Důsledky

- **Kód:** `Experiment/MappingCompleteness` — `Transferred` (část klíče a NOT NULL) a `SameValue` (`Identity` za `Auto`); `MappingCompletenessTest` drží jménem jen obě meze a tvrdí obě úlevy i jejich hranice; měření stojí jako `Experiment/KeyColumnNullabilityProviderTest` (EF Core, NHibernate) a v javové sadě jako `hibernate/KeyColumnNullabilityClaimsTest` a `eclipselink/KeyColumnNullabilityClaimsTest`.
- **Podíly:** ve směrech EF Core → NHibernate, Hibernate a EclipseLink a všude, kde klíč se strategií `Auto` jde do JPA, vychází úplnost mapování vyšší. První záznam experimentálního běhu ([2026-10-24](../runs/2026-10-24-experiment.md)) měřil předchozím srovnáním, takže podíl mapování těchto cílů v příštím záznamu s ním srovnatelný není; záznam se nemění a příští záznam to uvede.
- **Dokumentace:** [`architecture.md`](../architecture.md) §6.2 (úplnost mapování) a §9 (T3), [`traceability.md`](../traceability.md) řádek T3, kořenové `README.md` (T3 v *Guarantees* a *Beyond this version*); v [`open-items.md`](../open-items.md) položka *Odchylky zpětného čtení v úplnosti mapování* mizí a obě meze přibývají mezi práce, které dělat nebudeme.
- **Co toto rozhodnutí neurčuje:** rozdělení úplnosti mapování po kategoriích faktů (otevřená položka *Úplnost mapování po kategoriích faktů*), které obě meze v tabulce T3 oddělí od ostatních faktů.
