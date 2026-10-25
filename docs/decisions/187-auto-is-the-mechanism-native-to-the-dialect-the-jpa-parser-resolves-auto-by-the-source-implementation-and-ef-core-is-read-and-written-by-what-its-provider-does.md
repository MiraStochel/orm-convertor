# 187 — `Auto` je mechanismus nativní dialektu: `AUTO` JPA rozkládá parser profilem zdrojové implementace, builder JPA píše `Auto` jako `IDENTITY` a strategie klíče EF Core se čte i píše podle toho, co z modelu udělá provider

Datum: 2026-10-25
Stav: platí; nahrazuje řádek `Auto` rozh. 011 (jeho význam a člen JPA `AUTO`), čtení a zápis `AUTO` a výchozí jméno sekvence v oddílu *Strategie generování* rozh. 077 a oddíl *`AUTO` jako tabulka, vypsaná celá* rozh. 080
Požadavky: F1, F2, F7–F10, F11, S1, S2
Podklad: rozhodnutí [011](011-key-generation-strategy-vocabulary.md), [020](020-canonical-generator-parameter-vocabulary.md), [064](064-absence-of-generation-as-a-catalog-fact.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md), [129](129-sql-server-2022-stays-the-only-database-dialect.md), [132](132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); měření nad připnutými verzemi z 2026-10-25 (tabulka v *Kontextu*); [`use-cases.md`](../use-cases.md), *Co nástroj nedělá* a UC6

## Kontext

Rozhodnutí 011 dalo slovníku strategií hodnotu `Auto` s významem „mechanismus vybere framework podle dialektu" a přiřadilo jí tři zápisy: `native` NHibernatu, `AUTO` JPA a `[DatabaseGenerated(Identity)]` EF Core. Rozhodnutí 077 a 080 pak `AUTO` při čtení nechala jako `Auto` (co z něj implementace udělá, je podle nich absenční výchozí hodnota, kterou rozhodnutí 067 nematerializuje) a při zápisu do JPA ho rozložila profilem **cílové** implementace: v Hibernatu na sekvenci `<Entita>_SEQ`, v EclipseLinku na tabulku čítače. Měření nad připnutými verzemi ukázalo, že pod jedním slovem stojí dvě rodiny zápisů, které na SQL Serveru 2022 — jediném dialektu nástroje (rozh. 129) — dávají opačnou odpověď:

| Zápis zdroje | Co z něj vznikne na SQL Serveru 2022 | Měří |
|---|---|---|
| NHibernate 5.7.0 `native` nad `MsSql2012Dialect` | `IdentityGenerator`, týž generátor jako `identity`, ačkoli dialekt sekvence umí | `NHibernate/NHibernateGeneratorProviderTest` |
| EF Core 10.0.10, jednodílný klíč celočíselného typu kromě `byte`, bez anotace i s `[DatabaseGenerated(Identity)]` | sloupec IDENTITY; anotace na takovém klíči nemění nic | `EFCore/EFCoreValueGenerationProviderTest` |
| EF Core `[DatabaseGenerated(Identity)]` pod `UseHiLo()`, `UseKeySequences()` modelu | sekvence hi/lo `EntityFrameworkHiLoSequence` po 10, výchozí hodnota `NEXT VALUE FOR [<Entita>Sequence]` — žádný IDENTITY | tamtéž |
| Hibernate 7.4.5 `AUTO` (i holé `@GeneratedValue`) | sekvence **`<tabulka>_SEQ`** po 50 | `hibernate/KeyGenerationClaimsTest` |
| EclipseLink 5.0.0 `AUTO` | tabulka čítače `SEQUENCE` (`SEQ_NAME`, `SEQ_COUNT`, řádek `SEQ_GEN`), i nad připojením | `EclipseLinkClaimsTest`, `eclipselink/KeyGenerationClaimsTest` |
| `IDENTITY` v obou implementacích JPA | sloupec IDENTITY | oba `KeyGenerationClaimsTest` |

První rodina nechává mechanismus dialektu a dialekt odpoví sloupcem IDENTITY; druhá má přenositelnou výchozí hodnotu implementace a IDENTITY nedá nikdy. **Dnešní překlad přes hranici rodin mechanismus tiše mění.** Entita EF Core nad sloupcem IDENTITY — nejběžnější případ, i v případových studiích — vyjde do Hibernatu jako `SEQUENCE` se `@SequenceGenerator`, do EclipseLinku jako `TABLE` nad tabulkou čítače: oba artefakty sahají na objekt, který databáze nemá, a vložený řádek by navíc nesl hodnotu do sloupce IDENTITY, kterou SQL Server odmítne. Čtení přitom projde, takže čtyři stupně ověření, které běží jen `SELECT`, nic nevidí; záznam `Convention` tu mluví o „volbě frameworku", ale jmenuje mechanismus cíle, ne ten, nad kterým databáze stojí. Totéž platí obráceně: `AUTO` Hibernatu přečtené jako `Auto` vyjde do NHibernatu jako `native`, tedy IDENTITY nad sloupcem, který plní sekvence, do EclipseLinku jako tabulka čítače a do EF Core konvencí jako IDENTITY.

**Měření opravilo i dvě tvrzení 077.** Hibernate pojmenuje sekvenci podle **tabulky**, ne podle entity (`AutoKeyeds_SEQ` pro entitu `AutoKeyed` nad tabulkou `AutoKeyeds`): `<Entita>_SEQ` platí, jen když se tabulka jmenuje jako entita. A totéž platí o `SEQUENCE` bez generátoru; generátor bez `sequenceName` pojmenuje sekvenci sám sebou, v obou implementacích, a krok je 50. EclipseLink dává `SEQUENCE` bez generátoru sekvenci `SEQ_GEN_SEQUENCE` — ale jen nad připojením: bez něj nezná verzi serveru a napíše sloupec IDENTITY, takže skript bez připojení, kterým 080 změřilo `AUTO`, tuhle otázku zodpovědět neumí. Builder dnes pro sekvenci bez jména píše `<Entita>_SEQ` se záznamem „výchozí jméno implementace vypsané" a parser jméno, které implementace zdroje opravdu použije, nečte.

**Na straně EF Core je slovo 011 správné, jeho čtení ne všude.** `[DatabaseGenerated(Identity)]` skutečně netvrdí sloupec IDENTITY — pod `UseHiLo()` z něj vznikne hi/lo — a 011 ho právem nedalo do `Identity`. Parser ale tvrdí `Auto` i tam, kde provider IDENTITY neudělá: u klíče typu `byte` bez anotace (konvence ho negeneruje), u anotace na klíči `Guid` nebo `string` (hodnotu generuje klient jako GUID), u `Computed` na klíči (model EF Core odmítne), u klíče pod `UseHiLo()` či `UseKeySequences()` modelu a u klíče s voláním mechanismu ve fluent konfiguraci, které podmnožina 132 nečte. Builder EF Core počítá `byte` mezi klíče, které konvence generuje; `Identity` na klíči, který konvence negeneruje — část složeného klíče, klíč doplněný z katalogu —, vypíše bez anotace, a EF Core pak takový klíč přiděluje aplikací nad sloupcem IDENTITY; a jeho záznam `Loss` říká „strategie se zahazuje" i tam, kde ji konvence nese (`Identity` na celočíselném klíči, `Uuid` na klíči `Guid`), a mlčí o tom, že u `Sequence` a `HiLo` artefakt nese IDENTITY.

**Je třeba rozhodnout**, kde se význam „mechanismus nechávám frameworku" rozkládá na mechanismus, a co tedy hodnota `Auto` tvrdí.

## Zvažované varianty

**1 — Ponechat dnešní stav: `Auto` znamená volbu frameworku a rozkládá ji cíl.** Zamítáme. Rozklad cílem odpovídá na otázku, kterou nikdo neklade: databáze existuje a stojí nad mechanismem *zdroje* — nástroj schéma nepřevádí, katalog jen čte ([`use-cases.md`](../use-cases.md), *Co nástroj nedělá*) —, takže co by s `AUTO` udělal cíl, kdyby schéma tvořil sám, je pro artefakt nad cizím schématem bez významu. Je to také jediné místo, kde builder rozhoduje podle konvence, kterou zdroj ani nezná.

**2 — Každý parser rozloží zápis až na konkrétní mechanismus a `Auto` zmizí.** Konvence EF Core i `native` NHibernatu by se četly jako `Identity`, `AUTO` JPA jako sekvence či tabulka. Model by byl nejostřejší, ale zamítáme ho ze dvou důvodů. Ztratí se slovo zdroje: `native` → `native` by se stalo `native` → `identity`, tedy tvrzení silnější, než zdroj učinil — totéž, kvůli čemu 011 zavedlo `Auto` pro EF Core. A měření ukazuje, že u EF Core silnější opravdu je: anotace nedrží mechanismus proti `UseHiLo()`, které konzument smí přidat, kdežto `UseIdentityColumn()` ano. Kdyby parser z holého `int Id` tvrdil `Identity`, každý okružní překlad EF Core → EF Core by hlásil ztrátu připnutí, které zdroj nikdy nenapsal — šum v nejčastějším případě.

**3 — Rozdělit `Auto` na dvě hodnoty: „nativní dialektu" a „výchozí implementace".** Druhá by nesla význam, který se liší implementace od implementace, tedy API konkrétního frameworku v mezireprezentaci — sjednocení, které 011 zamítlo jako variantu 2 —, a builder by k ní stejně potřeboval vědět, kterou implementaci zdroj mínil. Ta znalost je jen v parseru zdroje.

**4 — `Auto` znamená mechanismus nativní dialektu; přenositelnou výchozí hodnotu implementace JPA rozloží její parser.** Volíme, níž.

## Rozhodnutí

**Volíme variantu 4. Hodnota `Auto` tvrdí, že úložiště vytvoří hodnotu při vložení mechanismem nativním pro dialekt — na SQL Serveru 2022 sloupcem IDENTITY —, a zdroj ho nepřipnul. `AUTO` JPA do ní nepatří: parser Hibernatu i EclipseLinku ho rozloží profilem zdrojové implementace na mechanismus i s parametry, které implementace na SQL Serveru odvodí, a builder JPA píše `Auto` jako `IDENTITY`. Parser EF Core tvrdí `Auto` jen tam, kde z klíče provider udělá IDENTITY, a builder EF Core volí `[DatabaseGenerated]` podle toho, co EF Core s artefaktem udělá.**

Je to rozhodnutí 067 přiložené na místo, kde ho 077 a 080 přiložila špatně. Test odvození 067 jmenuje „z typu klíče jeho generování" výslovně jako odvození: implementace dokumentuje, že `AUTO` nad číselným klíčem dá sekvenci (Hibernate) či tabulku čítače (EclipseLink), a měření to potvrdilo. A test mezery je splněn beze zbytku: každý další čtenář prázdného faktu dojde k jiné hodnotě — NHibernate a EF Core k IDENTITY, druhá implementace JPA ke svému mechanismu. Absenční výchozí hodnotou je jen to, co ze zápisu nic neodvozuje, a to zde nic není: jméno sekvence i krok jsou odvozené z tabulky a ze specifikace a databáze zdroje je obsahuje. Jméno, které by cíl vymyslel, by bylo jiné.

### Slovník: `Auto` a `Identity`

| Hodnota | Význam | Co se na ni mapuje |
|---|---|---|
| `Auto` | úložiště vytvoří hodnotu při vložení mechanismem nativním pro dialekt; na SQL Serveru 2022 sloupcem IDENTITY. Zdroj mechanismus nepřipnul. | NHibernate `native`; EF Core `[DatabaseGenerated(Identity)]` a konvence nad klíčem, z něhož provider udělá IDENTITY |
| `Identity` | sloupec IDENTITY a zdroj ho připnul | NHibernate `identity`, JPA `IDENTITY`, EF Core `UseIdentityColumn()`, katalog (`is_identity`, rozh. 064) |

Ostatní řádky 011 platí beze změny; JPA `AUTO` se z řádku `Auto` přesouvá do rozkladu níž. Rozdíl mezi oběma hodnotami na jediném dialektu zůstává ve dvou věcech: ve věrnosti slova zdroje (`native` se vrací jako `native`) a v připnutí, které EF Core umí jen fluent API — hodnota `Auto` se do anotací EF Core zapíše beze ztráty, `Identity` ztratí připnutí.

### Parser JPA rozkládá `AUTO` profilem zdrojové implementace

| Zápis zdroje | Hibernate 7.4.5 | EclipseLink 5.0.0 |
|---|---|---|
| `AUTO` nebo holé `@GeneratedValue` nad klíčem typu `short`, `int`, `long` či jejich obálky, bez generátoru | `Sequence`, `SequenceName` = `<tabulka>_SEQ`, `BlockSize` = 50 | `HiLo` nad tabulkou čítače: `CounterTable` = `SEQUENCE`, `CounterKeyColumn` = `SEQ_NAME`, `CounterValueColumn` = `SEQ_COUNT`, `CounterKeyValue` = `SEQ_GEN`, `BlockSize` = 50 |
| `AUTO` se jménem generátoru | mechanismus generátoru: `@SequenceGenerator` jako `SEQUENCE`, `@TableGenerator` jako `TABLE` | týž |
| `SEQUENCE` bez generátoru | `SequenceName` = `<tabulka>_SEQ` | `SequenceName` = `SEQ_GEN_SEQUENCE` |
| `@SequenceGenerator` bez `sequenceName`; bez `allocationSize` | `SequenceName` = jméno generátoru; `BlockSize` = 50 | týž |
| `AUTO` nad klíčem jiného typu (`UUID`, `String`, …) | `Unspecified` se jménem `AUTO` v `SourceStrategyName` — úniková cesta 011; rozklad nikdo neměřil | týž |

`<tabulka>` je jméno z `@Table`, bez něj jméno entity — výchozí jméno tabulky JPA, pod nímž implementace sekvenci pojmenuje. `orm.xml` javový typ nevyslovuje, takže jeho `AUTO` bez generátoru rozloží až čtení třídy, které typ klíče zná; do té doby nic netvrdí a pořadí `orm.xml` před anotacemi (rozh. 068) tím zůstává. Ze jmenování se strategie `TABLE` nemění: co Hibernate vytvoří pro `TABLE` bez generátoru, nikdo nezměřil, a 080 to proto nechalo profilu nevyplněné. Materializace záznam nevydává, jako všude v 067; profil, který ji nese (`AutoStrategy`, nově i výchozí jméno sekvence), patří k implementaci zdroje a parser ho dostává přes kontext čtení, který oba čtenáře zdroje JPA sdílejí.

### Builder JPA píše `Auto` jako `IDENTITY`

Hodnota `Auto` se do obou implementací JPA vypíše jako `@GeneratedValue(strategy = GenerationType.IDENTITY)` se záznamem `Convention`: zdroj nechal mechanismus dialektu, na SQL Serveru je to sloupec IDENTITY a JPA ho jinak než `IDENTITY` nevysloví, protože `AUTO` by v cíli znamenalo jeho přenositelnou výchozí hodnotu. Zásada 076 „`AUTO` se nevypisuje nikdy" platí dál a teprve teď beze zbytku: builder nerozkládá nic, co by měla rozložit implementace zdroje. `Sequence` bez jména dostane výchozí jméno **cílové** implementace — Hibernate `<tabulka>_SEQ`, EclipseLink `SEQ_GEN_SEQUENCE` —, takže věta záznamu „výchozí jméno implementace vypsané" je pravdivá; jména ze zdroje JPA tam už doplnil parser.

### Parser EF Core tvrdí `Auto` jen tam, kde provider udělá IDENTITY

| Zápis klíče v EF Core | Strategie |
|---|---|
| bez anotace, jednodílný klíč celočíselného typu kromě `byte` | `Auto` |
| bez anotace, jednodílný klíč typu `byte` | `Assigned` — konvence ho negeneruje |
| bez anotace, `Guid` | `Uuid` (beze změny) |
| `[DatabaseGenerated(Identity)]` nad celočíselným nebo desetinným typem | `Auto` |
| `[DatabaseGenerated(Identity)]` nad `Guid` nebo `string` | `Uuid` — klient vygeneruje GUID, databáze nemá nic |
| `[DatabaseGenerated(Identity)]` nad jiným typem | `Unspecified` se jménem `Identity` v `SourceStrategyName` |
| `[DatabaseGenerated(Computed)]` na klíči | `Unspecified` se jménem `Computed` a záznam `Loss`: EF Core takový model odmítne |
| klíč, nad nímž `OnModelCreating` volá `UseHiLo()` nebo `UseKeySequences()` modelu, a strategie by jinak byla `Auto` | `Unspecified` se jménem volání v `SourceStrategyName` |
| klíč s voláním mechanismu na vlastnosti ve fluent konfiguraci — `UseIdentityColumn`, `UseHiLo`, `UseSequence`, `HasDefaultValue`, `HasDefaultValueSql`, `HasComputedColumnSql`, `HasValueGenerator`, `ValueGeneratedOnAddOrUpdate` | `Unspecified` se jménem volání v `SourceStrategyName` |

Volání v posledních dvou řádcích zůstávají mimo podmnožinu 132 se svým záznamem `Loss`; mění se jen to, že klíč pod nimi přestane tvrdit mechanismus, který mu volání vzalo. `Unspecified` je pro takový klíč poctivá hodnota: tvrzení zdroje existuje, ale slovník ho nepřečetl, a 011 má pro to únikovou cestu se jménem. Fáze doplnění z katalogu ho pak doplní tím, co schéma skutečně má (rozh. 064), a bez katalogu mluví konvence cíle se svým záznamem. Jednu mez parser nepřekoná: klíč, který je zároveň cizím klíčem vztahu 1:1, konvence EF Core také negeneruje (změřeno), ale parser se to dozví až z odložené konvenční navigace po přečtení všech jednotek, kdy je strategie klíče už zapsaná; tvrdí tedy dál `Auto` a mez vede [`subset.md`](../subset.md) i položka v [`open-items.md`](../open-items.md).

### Builder EF Core volí `[DatabaseGenerated]` podle toho, co EF Core s artefaktem udělá

- **`Auto` i `Identity`** nad celočíselným či desetinným klíčem dostanou anotaci tam, kde ji konvence nedá — u části složeného klíče, u klíče typu `byte` či desetinného —, a nic tam, kde ji konvence dá; nad klíčem jiného typu anotace IDENTITY nedá, takže artefakt mechanismus nevysloví a builder vydá `Loss`. U `Identity` vydá builder `Loss` vždy: připnutí — a počáteční hodnotu a krok, nese-li je model — vyjádří EF Core jen fluent `UseIdentityColumn`, kterého artefakt nepoužívá; mechanismus pod výchozí strategií modelu stojí. Druh záznamu je `Loss`, ne `Convention`: přesně jeho definice — fakt, který cíl vyjádří jen v podobě, již artefakt nepoužívá.
- **`Uuid`** nad jednodílným klíčem `Guid` je konvence EF Core sama a builder nevydá nic ani záznam; nad klíčem `Guid` či `string`, který konvence negeneruje, dostane anotaci, pod níž GUID vygeneruje klient; nad klíčem jiného typu zůstává `Loss`.
- **`Sequence`, `HiLo`, `Increment`** zůstávají `Loss`, ale jejich věta říká, co artefakt udělá: klíč, který konvence generuje, poběží jako IDENTITY, jiný přidělí aplikace.
- **`byte`** mezi klíče, které konvence generuje, nepatří.

### Co se nemění

Builder NHibernatu píše `Auto` jako `native` a `Identity` jako `identity`; obojí dá na `MsSql2012Dialect` týž generátor, takže dosavadní výstup zůstává pravdivý. Fáze doplnění bere `Auto` nad sloupcem IDENTITY za soulad a nad sloupcem bez IDENTITY za rozpor jako dosud (064). Úplnost mapování experimentálního běhu (rozh. 173) dál počítá `Identity`, které se z artefaktu EF Core přečte zpět jako `Auto`, za nepřečtený fakt — ztráta připnutí je skutečná.

## Důsledky

- **Výstup se mění** ve směrech přes hranici rodin. EF Core a NHibernate → Hibernate a EclipseLink: klíč `Auto` vyjde jako `IDENTITY` místo sekvence či tabulky čítače. Hibernate a EclipseLink → kterýkoli cíl: klíč s `AUTO` nese mechanismus své implementace — do NHibernatu `sequence` se jménem `<tabulka>_SEQ` či `hilo` nad tabulkou čítače místo `native`, do EF Core `Loss` místo tiché konvence, do druhé implementace JPA mechanismus zdroje s vypsanými jmény. Hibernate jako cíl píše výchozí sekvenci `<tabulka>_SEQ` místo `<Entita>_SEQ`, EclipseLink `SEQ_GEN_SEQUENCE`. EF Core jako zdroj a cíl podle tabulek výš.
- **Fakta stojí jako testy vedle háčků, které na nich stojí**: `EFCore/EFCoreValueGenerationProviderTest` vedle parseru a builderu EF Core, `NHibernate/NHibernateGeneratorProviderTest` vedle `PrimaryKeyStrategyConvertor`, `hibernate/KeyGenerationClaimsTest` a `eclipselink/KeyGenerationClaimsTest` vedle profilů implementací. Povýšení frameworku, které fakt změní, selže tam.
- **Dokumentace:** [`architecture.md`](../architecture.md) §5 (čtení a zápis strategie po frameworcích), [`subset.md`](../subset.md) (strategie klíče, mez sdíleného klíče EF Core), [`use-cases.md`](../use-cases.md) UC6 (profil zdroje i cíle), položka práce v [`open-items.md`](../open-items.md) pro sdílený klíč EF Core.
- **Co toto rozhodnutí neurčuje:** čtení volání mechanismu ve fluent konfiguraci EF Core (`UseIdentityColumn` jako `Identity`, `UseHiLo` a `UseSequence` s parametry) — rozšíření podmnožiny 132, které se rozhodne samo, až ho vstup bude potřebovat; rozklad `AUTO` nad klíčem jiného než celočíselného typu a výchozí hodnoty `TABLE` v Hibernatu, dokud je nikdo nezměří.
