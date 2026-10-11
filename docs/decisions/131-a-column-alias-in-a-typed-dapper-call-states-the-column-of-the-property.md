# 131 — Alias sloupce ve volání Dapperu, které mapuje řádky na entitu, tvrdí sloupec její vlastnosti

Datum: 2026-10-11
Stav: platí
Požadavky: F5, F6, F11, S1, S2
Podklad: rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), [017](017-source-precedence-for-mapping-facts.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [050](050-one-home-for-the-singular-plural-heuristic.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [108](108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md) a [109](109-a-code-unit-carries-every-query-it-hands-over.md); JSS článek, §7.1 a pravidlo E9; Dapper 2.1.79, `DefaultTypeMap.GetMember` a README (*Multi Mapping*, *Multiple Results*, *Type Handlers*); [srovnání frameworků](../analysis/orm-frameworks-comparison.md), §4; [tutoriál k Dapperu](../analysis/tutorials/dapper-getting-started.md), krok 4; položka *Alias v SQL jako zdroj mapování Dapperu* v `open-items.md`

## Kontext

Dapper mapovací metadata nemá. Řádek výsledku převádí na objekt jediným pravidlem: **sloupec výsledku jde do členu typu stejného jména**. `DefaultTypeMap.GetMember` hledá vlastnost nejdřív se shodou velikosti písmen, pak bez ní, potom pole; přepínač `MatchNamesWithUnderscores` je globální nastavení procesu, ne vlastnost dotazu. Kde se jméno sloupce tabulky od jména vlastnosti liší, má projekt dvě cesty a obě popisuje [tutoriál](../analysis/tutorials/dapper-getting-started.md) v kroku 4: **alias `AS` v textu dotazu**, která je idiomatická, a globální mapu typu (`SqlMapper.SetTypeMap` s `CustomPropertyTypeMap`), registrovanou kódem mimo dotazy, kterých se týká. [Srovnání frameworků](../analysis/orm-frameworks-comparison.md) (§4) to shrnuje jednou větou: název sloupce má Dapper „jen v aliasech `AS`, rozptýleně".

**Nástroj alias jako mapování nečte.** Čte ho jen dotazová větev, jako alias projekce (§4.4 architektury). Entitní mapa Dapperu dostává sloupce až z katalogu (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) a ten páruje vlastnost se sloupcem podle jména (`TableImage.FindColumn(ColumnName ?? Property.Name)`). Doména tutoriálu — vlastnost `Id` nad sloupcem `AuthorId` — tak dopadne celá špatně: vlastnost `Id` sloupec nenajde a dostane `Incompleteness`, sloupec primárního klíče `AuthorId` vlastnost nemá, takže se klíč nedoplní, a entita, která v Dapperu klíč i mapování má, vyjde do NHibernatu odmítnutá a do EF Core jako `[Keyless]`. Je to přesně případ, kvůli kterému F6 existuje — úplné mapování cíle z frameworku s implicitním mapováním —, a selhává v idiomatickém zápisu.

Rozhodnutí [067](067-a-derived-convention-is-a-statement-a-default-is-not.md) tomu nijak neodporuje: databázové fakty Dapperu vyloučilo z *třídy*, protože „mapovacím artefaktem je SQL dotazu", a [017](017-source-precedence-for-mapping-facts.md) v tabulce úrovní napsalo pro Dapper u pomocného mapování pomlčku. Alias je tedy mapovacím artefaktem Dapperu, který nástroj zatím nečte. Rozhodnout je přesto třeba, ne jen napsat, ze tří důvodů:

- **Alias je výrok o jednom výsledku, ne o tabulce.** Mezireprezentace je tabulková — vlastnost má jeden sloupec. Dapper nic takového netvrdí: každý dotaz mapuje sám za sebe a dva dotazy smějí do téže vlastnosti dát různé sloupce. Je třeba říct, kdy se z výroku o výsledku stává výrok o mapování entity, a co je konflikt.
- **Ne každý alias je mapování.** `SELECT COUNT(*) AS Total` nebo `c.Name AS CustomerName` do DTO žádnou entitu nemapuje. Na jaký typ se řádky mapují, říká jen volání, ne SQL.
- **Čas.** Orchestrace čte dotazy až po fázi doplnění z katalogu a po vydání entit (`ConversionHandler.Convert`). Alias přečtený dotazovou větví by přišel pozdě: katalog by ho nepároval a entitní artefakt by ho nenesl.

## Zvažované varianty

1. **Nečíst a vyslovit mez.** Nic nestojí; `subset.md` už dnes vede, že se alias nepáruje. Zamítáme: F6 by se u Dapperu splnilo jen tam, kde se jméno vlastnosti a sloupce shodují — tedy tam, kde není co mapovat. Nárok by platil v okrajových případech a selhal v tom, který je pro framework typický.

2. **Číst alias dotazovou větví a do entitních map ho propsat tam.** Využije čtení, které už existuje. Zamítáme: přijde po fázi katalogu i po stavbě entit, takže by orchestrace musela fáze přeskládat nebo doplňovat podruhé — a to kvůli faktu jednoho frameworku, což S1 vylučuje. Dotazový parser by navíc zapisoval do entitních map, ačkoli rozhodnutí [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md) drží dvě role jednotky ve dvou průchodech právě proto, aby každý zapisoval jen do svého.

3. **Brát alias z každého SQL převodu, holé jednotky SQL nevyjímaje, a entitu určit podle tabulky konvencí rozhodnutí [050](050-one-home-for-the-singular-plural-heuristic.md).** Nejširší sběr. Zamítáme: holý text neříká, na jaký typ se jeho řádky mapují. Přiřadit alias entitě jen proto, že tabulka v `FROM` jí jménem odpovídá, by z projekce sestavy (`c.Name AS CustomerName` do DTO) udělalo tvrzení o entitě `Customer`, které nikdo nevyslovil — a podle 067 se domněnka za výrok zdroje nevydává nikdy, protože výrok zdroje katalog nepřepíše.

4. **Alias v textu volání, které mapuje řádky na jednu entitu převodu, nad sloupcem tabulky té entity, jako tvrzení zdroje** — přečtené v entitním průchodu Dapper wrapperu, jako pomocný mapovací artefakt (úroveň 1b rozhodnutí 017).

## Rozhodnutí

**Volíme variantu 4.** Alias sloupce v textu volání Dapperu, které mapuje řádky výsledku na entitu převodu, je tvrzením zdroje o sloupci její vlastnosti. Zapíše se do mezireprezentace jako fakt `ColumnName` prvního stupně priority, úrovně 1b, dřív než běží katalog.

**Kdy je alias tvrzením.** Musí platit všechno najednou:

| Podmínka | Proč |
|---|---|
| **Volání mapuje řádky na jeden typ**: `Query`, `QueryAsync`, `QueryFirst…`, `QuerySingle…`, `QueryUnbufferedAsync` s jediným typovým argumentem `T`, nebo s `typeof(T)` na místě typu | jen volání říká, kam řádek jde; holá jednotka SQL, `Execute…`, `ExecuteScalar<T>` (skalár), `QueryMultiple` (typ až na `Read<T>()`) a multi-mapping (řádek rozdělený `splitOn` mezi víc typů) to neříkají |
| text je řetězcový literál, jeden příkaz a čte se jako dotaz (`commandType` ho nedělá procedurou) | táž pravidla, podle nichž ho čte dotazová větev (rozhodnutí [108](108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md), [109](109-a-code-unit-carries-every-query-it-hands-over.md)) |
| **`T` je entita převodu**, jednoznačně podle jména a vysloveného jmenného prostoru | jinak není komu fakt zapsat; `Query<int>` či `Query<dynamic>` nic netvrdí |
| prvek projekce vnějšího `SELECT` (ne množinové operace) je **holý sloupec pod aliasem** | výraz pod aliasem (`UPPER(Name) AS Name`) žádný sloupec nemapuje; řádek množinové operace pochází z víc tabulek |
| **tabulka sloupce je tabulkou entity**: pojmenovaná tabulka `FROM` (ne mezivýsledek, ne odvozená tabulka), určená kvalifikátorem, u nekvalifikovaného sloupce jedinou tabulkou `FROM`; a je to tabulka, kterou entita vyslovila, jinak kandidát rozhodnutí 050 | sloupec jiné tabulky (join) by vlastnost entity tvrdil o cizí tabulce; tabulku entity hledá týmž pravidlem katalog i dotazový builder, takže všechna tři čtení se shodnou |
| **alias jmenuje skalární vlastnost `T`** pravidlem Dapperu — se shodou velikosti písmen, pak bez ní — a v projekci se neopakuje | jen tam Dapper hodnotu uloží; na navigaci skalár nedá |
| alias se od jména sloupce liší i bez ohledu na velikost písmen | shoda je výchozí identita všech čtenářů, tedy absenční výchozí, které 067 nematerializuje |

**Je to tvrzení zdroje, ne doplnění.** Alias je nejexplicitnější zápis mapování, jaký Dapper má — přesně „explicitně definované mapování", které podle pravidla E9 doplnění z metadat nesmí přepsat. Katalog ho proto nepřepíše, jen porovná: sloupec, který tabulka nemá, je dál záznamem o vlastnosti bez sloupce, a typ, délku či klíč doplní katalog k vlastnosti už se správným sloupcem (F5). Tvrzení zdroje záznam nevydává (rozhodnutí 067, oddíl o materializaci); původ faktu nese kniha tvrzení, která ho připíše jednotce s voláním.

**Úroveň 1b a entitní průchod.** Třída je u Dapperu úrovní 1a a sloupce nenese; text dotazu je pomocný mapovací artefakt, jako `hbm.xml` u NHibernatu nebo XML mapper u MyBatisu. Čte ho třetí parser Dapper wrapperu, který stojí v seznamu `ParserFactory` za entitním parserem a před dotazovým — v entitním průchodu, takže všechny třídy převodu jsou už přečtené, a před katalogem, takže fáze doplnění páruje podle tvrzeného sloupce. Jednotku s voláním tak čtou oba průchody, každý pro svou roli (rozhodnutí 081). Orchestrace se nemění a nový framework nic nového neimplementuje (S1). Znalost Dapperu — které volání mapuje na který typ — zůstává ve wrapperu; čtení SQL — který prvek projekce je sloupec které tabulky pod aliasem — ve sdíleném projektu `TransactSql`, kde jsou typy gramatiky.

**Co je konflikt.** Nad tabulkovou mezireprezentací se dva aliasy rozcházejí jen jedním způsobem:

- **Dva různé sloupce pro tutéž vlastnost** (dvě volání, `AuthorId AS Id` a `LegacyId AS Id`) je `Conflict` kategorie `ColumnName` a platí první v pořadí čtení — jednotky v pořadí požadavku, volání v pořadí textu. Je to pravidlo rozhodnutí 017 pro dva artefakty téže úrovně a stávající zápisová cesta `SetPropertyDatabaseMapping` ho už implementuje. Uspokojivé to není, stejně jako tam: Dapper takový projekt provozuje bez potíží, protože mapuje dotaz po dotazu, a model, který vlastnosti dává jeden sloupec, ho vyjádřit nemůže. Záznam to říká a výsledek je deterministický (S2).
- **Týž sloupec pod dvěma jmény do dvou vlastností** (`AuthorId AS Id` v jednom volání, `AuthorId AS AuthorKey` v druhém) konfliktem **není**: jsou to dva výroky o dvou vlastnostech a žádný nepopírá druhý. Zapíšou se oba. Mezireprezentace nevaliduje; že některý cíl dvě vlastnosti nad jedním sloupcem nepřijme, je věcí jeho builderu, a tentýž stav umí vyslovit i jiné zdroje (dvakrát `[Column("X")]` v EF Core).
- Opakovaný stejný alias je opakované tvrzení a nedělá nic.

**Co se nečte, a zůstává tedy vyslovenou mezí:**

- **tabulka entity z dotazu.** Čte-li `Query<Author>` z `tblAuthor`, který není kandidátem jména `Author`, alias nic netvrdí. Tabulku entity určuje jediné místo, rozhodnutí 050 a vyslovené mapování; že dotaz čte řádky entity z tabulky jiného jména, by byl výrok o tabulce, ne o sloupci — samostatná volba, kterou toto rozhodnutí nedělá;
- **globální mapa typu** (`SqlMapper.SetTypeMap` s `CustomPropertyTypeMap`, přepínač `DefaultTypeMap.MatchNamesWithUnderscores`): mapa je funkce, která běží jinde než dotaz, přepínač nastavení všech typů naráz — ani jedno není zápis mapování, který by se dal přečíst. Podle rozhodnutí [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) ale mlčet nesmí: každý výskyt v jednotce je `Loss` kategorie `ColumnName`, u mapy se jménem typu z `typeof`;
- alias v holé jednotce SQL, v multi-mappingu a v `QueryMultiple` (výš).

## Důsledky

**Vůči rozhodnutím 015, 017 a 067 je to doplnění, ne změna volby** — týž vztah, jaký má 067 k 015. Řádek Dapperu v tabulce úrovní 017 dostává pro úroveň 1b obsah („alias v textu volání `Query<T>`"); třída Dapperu podle 067 dál netvrdí nic a priorita zdrojů se nemění. Žádné z těch rozhodnutí se nepřepisuje.

**F6 platí i pro idiomatický zápis Dapperu.** Doména tutoriálu s katalogem vyjde do NHibernatu s `<id name="Id" column="AuthorId">` a generátorem z katalogu, do EF Core s `[Column("AuthorId")]` a klíčem; dotaz převedený do LINQ, HQL či JPQL jmenuje vlastnost `Id`, protože buildery mapují sloupec zpět přes entitní mapy (§5 architektury). Bez katalogu nese cíl aspoň sloupec.

**Dapper jako cíl se nemění.** `ColumnName` je u něj `NotExpressible`; převod, jehož entita sloupec nese — teď i Dapper → Dapper s aliasem —, má na třídě mechanický `Loss` podle rozhodnutí [004](004-unexpressible-facts-as-warnings.md), jako každý zdroj se sloupcem. Dotazový artefakt alias nese, protože ho nese projekce.

**Dotčená místa:** nový parser Dapper wrapperu, jeho místo v `ParserFactory`, čtení přejmenovaných sloupců v `TransactSql` a sdílení výběru volání s dotazovým parserem Dapperu. Model, deskriptory, orchestrace a kontrakt REST se nemění. Artefakty se mění jen pro vstupy s aliasem v typovaném volání; číslo vydání určí rozhodnutí [098](098-the-number-is-decided-once-per-release.md).

**Dokumentace:** `architecture.md` (§5, čtení entit a mapování), `subset.md` (řádek o aliasu mezi nerozhodnutými se mění na vyslovené meze výš), `traceability.md` (F6) a věta tutoriálu, podle níž nástroj aliasy nečte.
