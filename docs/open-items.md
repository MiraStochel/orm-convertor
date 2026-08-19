# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Kde se pokračuje, říká značka na řádku vazeb položky** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě. Samostatný seznam pořadí tenhle soubor nemá — rozcházel se s položkami pod ním. Co značku nenese, je neuspořádané a bere se podle priorit plynoucích z požadavků F/S/T.

---

## Otevřená rozhodnutí

### Sloupec verze jako mapovací fakt
*Vytčeno z rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md), které `RowVersion` odstranilo ze slovníku typů. Požadavky F2, F7–F10, F11.*

`RowVersion` se dosud nesl jako databázový typ, ale typ to není — je to token pro optimistickou souběžnost, který každý framework vyjadřuje vlastním mechanismem: JPA anotací `@Version`, EF Core voláním `IsRowVersion()`, NHibernate elementem `<version>`. Rozhodnutí 019 ho ze slovníku odstranilo jako typ jediného systému a fakt tím zůstal bez domova: sloupec `rowversion` z katalogu vyjde jako `VarBinary` s doslovným názvem na únikové cestě a význam „tenhle sloupec nese verzi řádku" se ztratí. Rozhodnout je třeba, jestli mezireprezentace dostane vlastní mapovací fakt pro sloupec verze — a s ním kategorii v deskriptoru, aby šlo říct, který cíl ho vyjádřit umí — nebo jestli zůstane mimo rozsah a bude se hlásit jako nevyjádřitelný. Do té doby se ztrácí bez záznamu, což je přesně to, čemu má bránit rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md).

### Kritérium pro širší čtení konvencí zdroje
*Navazuje na rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), které v tomto bodě nahradilo [008](./decisions/008-database-as-metadata-source.md). Souvisí s [010](./decisions/010-diagnostics-as-returned-data.md). Požadavky F2, F5, F6.*

Parsery dnes čtou konvenci zdrojového frameworku jen tam, kde by její neznalost změnila význam, což je v praxi jediné místo: primární klíč v `EFCoreEntityParser.FindConventionKey`. Implicitní je ale i název sloupce, název tabulky a nullabilita odvozená z jazykového typu, a u MyBatisu je konvenční mapování jediné, které vůbec existuje, takže F6 na širším čtení stojí. Rozhodnutí 008 otázku odložilo s podmínkou „až mezireprezentace bude evidovat původ faktu"; rozhodnutí 010 ale původ z modelu vyňalo a 015 tuhle volbu převzalo, takže odkládací podmínka nikdy nenastane a otázka zůstala bez kritéria. Je třeba rozhodnout, podle čeho se pozná, kterou konvenci materializovat. Nabízí se „materializuj tam, kde by cíl doplnil něco jiného, než tvrdí zdroj", to ale znamená znát konvence obou stran a mít je kde zapsat — pravděpodobně v deskriptoru, který dnes kategorii pro konvence nemá. Dokud kritérium chybí, přibývají konvence jednotlivě a bez pravidla, jak se to stalo u klíče v EF Core.

### Framework s vlastní precedencí mezi svými artefakty
*Navazuje na rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), které tenhle případ vědomě nechalo otevřený. Předpoklad čtení fluent konfigurace EF Core a podpory MyBatisu (F8). Požadavky F5, F11.*

Rozhodnutí 017 uspořádalo zdroje faktů na vstupní text frameworku, pomocné mapovací artefakty, katalog a konvenci cíle. Některý framework ale mezi svými vlastními artefakty precedenci sám dokumentuje, a to opačnou: EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML mapperu vlastními pravidly. Přeložit takový projekt naším pořadím znamená přeložit něco jiného, než co zdroj znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Rozhodnout je třeba, jestli precedence zdrojového frameworku přebíjí naše pořadí uvnitř prvního stupně, a pokud ano, kde je ta precedence zapsaná: deskriptor popisuje cíl, ne zdroj, a kategorii pro tohle nemá. Dokud se čte jediný artefakt na framework, případ nenastane; nastane s prvním parserem fluent konfigurace.

### Klíčová třída u kompozitního klíče na straně entity
*Navazuje na rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md) a [014](./decisions/014-language-type-model.md). Podklad: audit 2026-08-02, kap. 3.2.*

Mapovací stranu už parser čte: `<composite-id class=>` i `<composite-id name= class=>` skončí jako `SourceKeyClass`. U formy `Embedded` ale části klíče nejsou vlastnostmi entity, nýbrž klíčové třídy, a entita nese jedinou vlastnost jejího typu. Zbývá tedy trojí: odkud vzít jazykové typy částí, jak zabránit tomu, aby se držící vlastnost dostala do mezireprezentace jako běžná vlastnost (ploché vykreslení klíčovou třídu ruší), a co udělat s C# zdrojem klíčové třídy, pokud do převodu vstoupí — entitní parser by z něj dnes udělal další entitu.

Vstupní překážku odstranil jazykový typový model: vlastnost typu `OrderLineId` projde parsováním jako `Unknown` a entita té formy se dostane do mezireprezentace. Přijetí frameworkem ji dnes odhalí — u téhle formy odkáže mapování na vlastnost, kterou třída nemá, a 3. stupeň takový pár odmítne (tvarová aserce to odhalit nemohla). Trojí otázka výše tím ale zodpovězená není. Třetí z nich navíc sahá mimo entitní větev: „další soubor téhož převodu" je vícesouborový vstup podle F14, pro který rozhodnutí neexistuje, takže rozhodovat se dá jen s vědomím, že se toho tématu dotýká.

### Centrální správa verzí
*Podklad: audit 2026-08-02, kap. 3.4.1. Souvisí s S2, jehož determinismus se opírá o dané verze.*

Zavést `Directory.Packages.props`, případně `global.json`, aby se sjednocení verzí udržovalo mechanicky a ne ručně. Dnes se může nepozorovaně rozejít — tabulka zafixovaných verzí v `architecture.md` pak tvrdí něco, co v `.csproj` souborech nemusí platit.

### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate a EF6 na `System.Data.SqlClient`. Pro srovnání výkonu je to metodologický confound. Buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, ověřit provider u PetaPoco a EF6 a přeměřit, nebo confound explicitně popsat v textu práce.

### Osud `wwwroot`
*Souvisí s S5.*

Automatizovat build Angularu do `wwwroot`, nebo `wwwroot` z gitu odstranit. Dnes je to commitnutý build, takže po každé změně frontendu hrozí, že nasazený bundle neodpovídá zdrojákům.

---

## Otevřená práce

### NHibernate builder si vymýšlí název sestavení
*Na řadě. Odporuje rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md), které zakazuje generovat náhražky za fakta, která nemáme. Požadavky F2, F11, S2.*

`BuildTableSchema` vypisuje u entity se jmenným prostorem `<class name="Shop.Customer, Shop">` — druhou částí je název sestavení a builder ho odvozuje z jmenného prostoru. To jsou ale nezávislé věci: sestavení je fakt konzumentského projektu, který převod nezná a znát nemůže. Vygenerované mapování tak tvrdí o cílovém projektu něco, co skoro jistě neplatí, a NHibernate ho odmítne hláškou `persistent class ... not found`. Že to dosud nikdo neodhalil, je vlastnost testů, ne kódu: ověřovací testy kompilují entity pod názvem sestavení shodným se jmenným prostorem zdroje, takže si výmysl samy potvrzují — a `NHibernateAcceptance.QualifyAssembly` popisuje tenhle stav jako záměr („název, který builder už kvalifikoval, má přednost"), ačkoli je to obcházení vady. Správný tvar má název sestavení na kořenovém elementu `<hibernate-mapping assembly=...>`, kam ho doplní konzument, a `<class name=...>` nechá nekvalifikované; jmenný prostor kořen už dnes vypisuje. Rozhodnout je při tom třeba, jestli se název sestavení stane deklarovaným vstupem převodu, nebo zůstane vynechaný se záznamem o konvenci.

### EF Core parser nečte navigaci bez `[ForeignKey]`
*Potom. Souvisí s rozhodnutím [012](./decisions/012-foreign-key-rendering.md) a [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F3, F11.*

V `EFCoreEntityParser` skončí vlastnost, která není skalár a nemá `[ForeignKey]`, ve větvi `else` mezi kandidáty na konvenční klíč. Obyčejné `public Customer Customer { get; set; }` — v EF Core naprosto běžný zápis, protože vztah tam plyne z konvence — tedy nezaloží žádný vztah a do mezireprezentace vejde jako by to byl skalár. Cíl to pak vypíše jako obyčejnou vlastnost: NHibernate takové mapování odmítne, protože `<property>` míří na typ, který není namapovaný jako hodnota. Zachránit to umí jedině fáze doplnění z katalogu, tedy jen s připojenou databází, a bez ní o tom nevznikne ani záznam. Chybí tedy dvojí: uznat neskalární vlastnost za navigaci i bez anotace, a tam, kde se sloupce nedají odvodit, to ohlásit místo mlčení.

### EF Core parser zahazuje neznámé anotace beze stopy
*Souvisí s rozhodnutím [010](./decisions/010-diagnostics-as-returned-data.md). Požadavek F11.*

`switch` nad názvem atributu v `EFCoreEntityParser` nemá větev `default`, takže všechno mimo sedmičku rozpoznaných anotací se ztratí bez záznamu. U některých je to jen ochuzení (`[StringLength]`, `[Comment]`, `[Index]`), u dvou to ale mění význam artefaktu: `[NotMapped]` říká, že vlastnost se nemapuje vůbec, a `[Keyless]`, že typ nemá klíč. Obojí projde jako by tam nebylo. Nejmenší náprava je větev `default` se záznamem o ztrátě; k tomu patří rozhodnout, které z nich má mezireprezentace umět nést.

### Dva entitní parsery jsou totéž
*Vytčeno rozhodnutím [026](./decisions/026-home-of-shared-query-reading.md), které touž duplicitu odstranilo na dotazové straně. Požadavek S1.*

`DapperEntityParser` a `NHibernateEntityParser` se liší jedinou řádkou dokumentačního komentáře; oba čtou jmenný prostor, hlavičku třídy a vlastnosti a nic dalšího. Třetí kopii téhož drží `EFCoreEntityParser` uvnitř sebe. Každá oprava čtení vlastností se tak musí udělat dvakrát až třikrát. Na dotazové větvi tenhle problém vyřešila sdílená knihovna se zásuvnými body; entitní strana čeká na totéž a je to týž tvar řešení, ne nový.

### Priorita zdrojů uvnitř vstupu se nevynucuje
*Implementace rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md). Požadavky F5, F11, S2.*

Rozhodnutí 017 rozdělilo první stupeň priority na vstupní text frameworku a pomocné mapovací artefakty a uložilo, že vyšší úroveň se nepřepisuje nižší a rozdíl se hlásí. Kód to zatím nedělá. Pořadí čtení plyne jen z pořadí parserů v seznamu, který vrací `ParserFactory`, takže se přehozením dvou řádků tiše obrátí; `SetPropertyDatabaseMapping` přepisuje bezpodmínečně, takže mapovací artefakt přebije, co tvrdila entitní třída; a rozpor mezi dvěma vstupními artefakty nevydá žádný záznam, ačkoli týž rozpor proti katalogu skončí záznamem `Conflict`. Zbývá trojí: uspořádat seznam parserů jako vyslovený fakt frameworku, rozlišit na zápisové cestě druhé úrovně prázdný fakt od obsazeného, a neshodu vydat jako `Conflict`. Vzorem je fáze doplnění z katalogu, která přesně tohle už umí (viz `architecture.md`, §5.2).

### Kontejnerová konfigurace prostředí
*Dohnání S5 odložené rozhodnutím [016](./decisions/016-generated-artifact-verification-levels.md).*

S5 žádá celý systém včetně databáze spustitelný dokumentovanou kontejnerovou konfigurací, kde čisté prostředí reprodukuje testy jedním hlavním příkazem. Lokální instance zvolená rozhodnutím 016 to nesplňuje a splnit nemá. Protože ale o hostiteli rozhoduje konfigurace, a ne kód testů, jde o přidání služby a proměnné prostředí, ne o návrat k rozhodnutí.

Patří sem trojí: service container do workflow v `.github` spolu s proměnnou `ConnectionStrings__TestDatabase`, aby databázově závislé testy běžely i v CI (dnes se tam přeskakují, protože proměnná není nastavená), volba mezi Docker Compose a Testcontainers pro lokální reprodukci, a ověření `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml` — commitnutá deklarace, kterou dosud nikdo nespustil a jejíž ověření dřívější plán čekal od stavby testovacího prostředí.

### Cílová verze a databázový dialekt v deskriptoru
*Implementace rozhodnutí [013](./decisions/013-target-framework-versions.md) nad deskriptorem z rozhodnutí [009](./decisions/009-target-framework-descriptor.md); dialekt sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md). Požadavky S2, S6, F7–F10.*

Deskriptor cílového frameworku nese, co cíl umí vyjádřit, ale ne verzi, proti které to platí. Doplnit ji a nechat buildery volit syntaxi tam, kde se verze rozcházejí — u EF Core `[PrimaryKey]` proti `HasKey`, u NHibernate dostupnost `DateOnly`. Bez explicitní volby platí verze zafixovaná v `architecture.md`. Tímtéž údajem se pak plní záznam běhu podle S6, aby nemohl tvrdit něco jiného než generátor.

Sem patří i **cílový databázový dialekt**, který rozhodnutí 019 odmítlo řešit v typovém modelu: je to fakt o cíli převodu téhož tvaru jako verze frameworku. Bez něj nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — NHibernate builder dnes propisuje jen doslovný `SourceSqlType`, který nese zdroj. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server.

### Rozresolvování jmen entit — `property-ref` na inverzní straně
*Zbytek důsledku rozhodnutí [001](./decisions/001-entity-reference-by-name.md) a [012](./decisions/012-foreign-key-rendering.md). Požadavek F11.*

Fáze rozresolvování (`ResolveEntityNames` v `AbstractEntityBuilder`) běží před generováním, plní `ColumnPairs`, povyšuje `Unknown` typy na reference a nenalezené jméno cílové entity i nesouhlasící počet či pořadí sloupců hlásí záznamem podle rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md). Z důsledků rozhodnutí 001 tak zbývá jediné: `property-ref` na inverzní straně vztahu 1:1, který rozhodnutí 012 odkládá právě sem. Navigace protistrany je po rozresolvování dostupná, ale NHibernate builder ji zatím nehledá a atribut nevypisuje; zahozenou hodnotu ze vstupu aspoň hlásí parser záznamem o ztrátě.

### Poddotazy a množinové operace se nevykreslí
*Potom. Podklad: audit 2026-08-02, kap. 8. Požadavek T2, který dotazovou matici dělí i podle poddotazů a množinových operací.*

Dotazová mezireprezentace zanoření nese, vykreslovací strana ne. `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` vrací prázdný řetězec, takže poddotaz projde parsováním, ale jeho výsledek se nikam neskládá. **Tiché to už není** — `Normalize()` v šabloně dotazového builderu (rozhodnutí [023](./decisions/023-query-builder-template-method.md)) vnořený poddotaz ohlásí záznamem o ztrátě —, ale vykreslit ho to neumí. Vedle toho `AbstractQueryBuilder.Pop()` nesleduje úroveň zanoření pro množinové operace (TODO v kódu), takže složený dotaz se poskládá špatně; množinovou operaci navíc dnes vykresluje jedině Dapper builder, kdežto NHibernate ji podle deskriptoru vyjádřit neumí a EF Core builder pro ni nemá větev. Sem patří i stránkování, které mezireprezentace vůbec nenese — parsery ho hlásí jako ztrátu. Všechno tohle jsou kategorie dotazové matice podle T2, které tak nemají co měřit.

### EF Core — nullabilita se vyjadřuje jen jazykově
*Souvisí s rozhodnutím [009](./decisions/009-target-framework-descriptor.md): deskriptor kategorii uvádí jako vyjádřitelnou, protože popisuje framework, ne dnešní stav builderu. Požadavky F2, F11.*

Anotaci `[Required]` builder negeneruje. Databázová nullabilita z `PropertyMap.IsNullable` se propisuje jen do modifikátoru `required` a otazník za typem vychází z jazykové nullability vlastnosti. Parser přitom `[Required]` číst umí, takže vstup s ním se přeloží a zpět už tuto podobu nezíská.

### NHibernate builder — kolekce jen jako `<bag>`
*Navazuje na rozhodnutí [014](./decisions/014-language-type-model.md), které do modelu přineslo `CollectionKind`. Požadavky F3, F11.*

Kolekční vlastnost se generuje natvrdo jako `<bag inverse="true" cascade="all-delete-orphan">` a ostatní kolekční tvary (`set`, `list`, `map`) ani další kolekční vlastnosti builder neřeší (dva TODO v kódu). Atributy `inverse` a `cascade` jsou přitom fakty, které nikdo netvrdil — kaskádní mazání sirotků obzvlášť mění chování — a nevzniká o nich záznam. K témuž místu patří i to, že vztah N:M, který přežil fázi syntézy, vyjde jako `<many-to-many>` uvnitř `<bag>` bez atributu `table`: to není chudší mapování, nýbrž neplatné. Volba tvaru kolekce je v NHibernate sémantická — `set` vylučuje duplicity, `list` nese pořadí — takže dnešní stav mění chování, ne jen zápis. Model už druh kolekce nese (`CollectionKind` na `LangType`) a plní ho jazyková strana (`HashSet<T>` → `Set`); XML parser tvar elementu (`<set>` vs. `<bag>`) do modelu zatím nepropisuje a builder druh nečte — obojí patří k této položce.

### Advisor nemá build nativní knihovny pro Windows
*Mezera popsaná v [`architecture.md`](./architecture.md), §8. Souvisí s S5 a T7.*

`libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`) a název je v P/Invoke natvrdo linuxový, takže mimo Linux a Docker Advisor endpointy selhávají; překladová část na tom nezávisí. Soubor `ilp.c` má přitom exportní makra pro Windows připravená, jen build krok pro `advisor.dll` neexistuje. Buď ho doplnit, nebo vyslovit, že Advisor je vázaný na kontejner, a podle toho srovnat i název v `LibraryImport` — dnešní stav tvrdí to první a chová se podle druhého.

### Spuštění mimo Docker není ověřené ani popsané
*Mezera popsaná v [`architecture.md`](./architecture.md), §6, kde je na tuhle položku odkaz. Souvisí s S5 a S7.*

V repozitáři leží `ecosystem.config.js`, konfigurace pro proces manager PM2, tedy nasazení mimo Docker. Nikde není popsané, co ta cesta předpokládá — které proměnné prostředí, jaký build frontendu, co servíruje statické soubory — a nikdo ji nespustil. Z lokálního spuštění je podobně ověřený jen `http` launch profil; ostatní profily jsou commitnuté a nevyzkoušené. Obojí je stejný druh dluhu: deklarovaná cesta, o které se neví, jestli funguje.

### Frontend zaostal za API a nevaliduje vstup
*Souvisí s rozhodnutím [010](./decisions/010-diagnostics-as-returned-data.md), jehož záznamy frontend nezobrazuje. Požadavek S7. Odloženo do cílenější přestavby, současný stav je funkční.*

Uživatelské rozhraní zůstalo u tvaru API, který už neplatí, a chybové stavy nechává na serveru. `/convert` vrací od rozhodnutí 010 vedle artefaktů i pole `records` se strukturovanou diagnostikou a frontend je ignoruje, takže se uživatel o ztrátách, konvencích ani konfliktech nedozví — a nově je těch záznamů podstatně víc, protože je vydává i dotazová větev. Chyby ze serveru zobrazuje `main-page.component.ts` přes `err.message` místo `err.error`, takže místo hlášky ze serveru ukáže obecný text HTTP chyby; vzor správného čtení je v `advisor-page.component.ts`. A validace před odesláním podle S7, tedy chyby na úrovni souboru a řádku, neexistuje vůbec — u zdroje v Dapperu přitom server nově řádek i sloupec zná, protože je hlásí parser T-SQL. Prázdné jednotky odsud zmizely: server je od rozhodnutí [025](./decisions/025-query-language-as-content-type.md) přeskakuje sám, protože nevyplněné pole není tvrzení. Zbývá také přeložit frontend do `wwwroot` — výčet typů obsahu se změnil a commitnutý bundle ho zatím nezná.

---

## Vzdálenější horizont

Velké bloky ze zadání, každý si zaslouží vlastní rozhodnutí, než se do něj sáhne. F4–F6 mezi nimi už nejsou — jsou hotové a to, co z nich zbývá, je výše.

| Blok | Co odblokuje |
|---|---|
| **F11** validace a strukturovaná diagnostika | varování o nevyjádřitelných faktech a kontrola úplnosti IR jsou hotové (rozhodnutí 010), syntaktické ověření generovaných souborů také (rozhodnutí 016, `architecture.md` §6.2); zbývá záznam běhu podle S6, který stojí na cílové verzi v deskriptoru |
| **F7–F10** javový ekosystém a cross-ecosystem překlad | jádro rozšíření; typový model má zneutralizovaný na jazykové (rozhodnutí 014) i databázové straně (rozhodnutí 019) a parametry generátoru se nesou kanonicky s výběrem názvu ve výstupu (rozhodnutí 020 a 021, obojí implementované), takže slovníkové předpoklady jsou hotové |
| **F12–F13** testovací infrastruktura pro Javu, diferenční ověření | důkaz funkční ekvivalence |
| **F14–F15** dávkové vstupy a výběr cíle v UI | použitelnost nástroje mimo ruční zadávání; F14 je zároveň předpokladem třetí otázky u klíčové třídy |
| **T1–T7** experimenty | T7 navazuje na existující ILP Advisor |

Dotazová větev z původního zadání je hotová ve všech devíti směrech v rozsahu kategorií projekce, filtrace, join, agregace a řazení (rozhodnutí 022–027). Co z ní zbývá, jsou zbylé kategorie požadavku T2 — stránkování, poddotazy a množinové operace — a HQL parser, bez kterého NHibernate → NHibernate není textovým round-tripem.
