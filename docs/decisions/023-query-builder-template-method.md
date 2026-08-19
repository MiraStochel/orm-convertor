# 023 — Šablonová metoda dotazového builderu podle relačního pořadí

Datum: 2026-08-19
Stav: platí
Požadavky: F7–F10, F11, T2, S1, S2
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md), které tuhle otázku otevřelo; [009](009-target-framework-descriptor.md) a [010](010-diagnostics-as-returned-data.md); JSS §6.2, pravidla Q1, Q2, Q4, Q8

## Kontext

`AbstractQueryBuilder` je dnes záznamník: nasbírá instrukce do plochého seznamu a předá je jediné abstraktní metodě `Build()`. Všechno ostatní si každý builder dělá sám. Protože builder existuje jediný, není na tom nic vidět — až rozhodnutí [022](022-native-query-syntax-in-builders.md) uložilo napsat další dva a otázka, co z toho je společné, se stala nevyhnutelnou.

Co `DapperSqlQueryBuilder` uvnitř skutečně dělá, jsou dvě různé věci slepené dohromady:

1. **Roztřídí ploché instrukce do klauzulí.** Sedmkrát `OfType<T>()`, k tomu spojení víc `Where` a víc `Having` konjunkcí podle pravidla Q4 — napsané dvakrát, jednou pro každé z nich.
2. **Složí z klauzulí text** v pořadí `SELECT · FROM · JOIN · WHERE · GROUP BY · HAVING · ORDER BY`.

První část je vlastnost mezireprezentace, ne frameworku: pravidlo Q4 („víc filtrů se spojuje konjunkcí") a pravidlo Q3 („chybí-li projekce, materializuje se celá entita") platí bez ohledu na to, do čeho se generuje. Se třemi buildery by konjunkce podle Q4 existovala v šesti kopiích a každá kopie je příležitost, aby se rozešly — přesně to, čemu brání S2.

Zároveň se dnes v téže metodě řeší dvě pravidla způsobem, který rozhodnutí [010](010-diagnostics-as-returned-data.md) zakazuje: **Q2** („každý dotaz má právě jeden zdroj") končí výjimkou `QueryBuilderException("None or too many query sources.")` místo záznamu a **Q8** („nejvýš jedno seskupení; agregace vyžaduje seskupení") se nekontroluje vůbec.

Otázka tedy zní, jestli má `AbstractQueryBuilder` dostat šablonovou metodu jako entitní builder, a pokud ano, **v jakém pořadí kroky stojí** — protože povrchová syntaxe LINQ se od SQL a HQL liší a naivní odpověď zní, že jedna šablona tři cíle neunese.

## Zvažované varianty

### 1 — Nechat `Build()` abstraktní a nesdílet nic

Dnešní stav. Nejmenší zásah do `AbstractWrappers` a každý builder si dělá, co chce. Zamítáme: šestkrát opsané pravidlo Q4, třikrát nezávisle odvozené chování prázdné projekce a tři místa, kde Q2 může skončit výjimkou místo záznamu. Rozhodnutí [009](009-target-framework-descriptor.md) vedlo tentýž spor o mapovací fakty a dopadl stejně — co je mechanické, patří do základu, protože jinak se na to zapomene právě v tom builderu, který nikdo nečte.

### 2 — Sdílet jen roztřídění, emisi nechat celou na builderu

`AbstractQueryBuilder` by nabídl pomocnou metodu vracející klauzule a `Build()` by zůstala abstraktní. Odstraní to duplicitu pravidel a nevnucuje žádné pořadí. Je to poctivá varianta a dlouho vypadala jako správná odpověď, protože se zdá, že LINQ má jiné pořadí než SQL.

Zamítáme ji, jakmile se to pořadí napíše vedle sebe — viz níže. Bez šablony navíc zůstává nevyřešené, kdo spouští kontrolu vyjádřitelnosti podle rozhodnutí 022: buď ji volá každý builder ručně (a zapomene), nebo ji volá metoda, která builderem řídí — a tou je právě šablona.

### 3 — Šablonová metoda s pevným pořadím kroků a artefaktem po klauzulích

## Rozhodnutí

**Volíme variantu 3. `Build()` se stává konkrétní šablonovou metodou; pořadí kroků je pořadí relačního vyhodnocení, ne pořadí klauzulí SQL; a textové pořadí si každý cíl skládá sám v závěrečném kroku.**

**Klíčové pozorování: LINQ nemá jiné pořadí, má stejné.** Napsáno vedle sebe:

| | pořadí |
|---|---|
| SQL | SELECT · FROM · JOIN · WHERE · GROUP BY · HAVING · ORDER BY |
| HQL | *(select)* · from · join · where · group by · having · order by |
| LINQ | `Set<T>()` · `.Join` · `.Where` · `.GroupBy` · `.Where`(having) · `.OrderBy` · `.Select` |

LINQ je **relační pořadí vyhodnocení napsané doslova**. SQL a HQL jsou totéž pořadí, jen s projekcí přesunutou dopředu povrchovou syntaxí. Šablona proto běží v pořadí `Zdroj → Joiny → Filtr → Seskupení → Filtr po agregaci → Řazení → Projekce` a všechny tři cíle na něm sedí.

**Není to volba estetická, ale datová závislost.** V LINQ se projekční lambda váže na jinou proměnnou před seskupením a po něm — po `GroupBy` dostane `IGrouping`, ne prvek —, takže projekci nelze složit dřív, než je známo, jestli se seskupuje. Totéž platí pro řazení, které navíc musí střídat `OrderBy` a `ThenBy` podle pořadí. Šablona tedy nefixuje, kde text skončí, nýbrž **co už musí být rozhodnuté, když se krok spustí**. Je to týž argument, jakým `AbstractEntityBuilder` staví `BuildPrimaryKey` před `BuildProperties`: části klíče se vypisují v pořadí klíče, ne v pořadí deklarace vlastností.

**Text skládá až závěrečný krok.** Každý krok píše do vlastní přihrádky artefaktu — dotazová obdoba dvojice `Code`/`Mapping`, kterou u entit drží `EntityArtifact`. Závěrečný krok přihrádky pospojuje v pořadí, které je vlastností cíle: Dapper a NHibernate `Projekce · Zdroj · Joiny · Filtr · Seskupení · FiltrPoAgregaci · Řazení`, EF Core v pořadí běhu. Oddělení běhu od textu je to, co dovoluje jedné šabloně obsloužit obojí.

**Roztřídění a mechanická pravidla dělá šablona před prvním krokem.** Sem patří rozbalení vrcholového `SubQueryInstruction`, roztřídění podle typu instrukce, spojení víc `Where` a víc `Having` konjunkcí (Q4) a kontroly Q2 a Q8. Kontroly vydávají záznam podle rozhodnutí 022, ne výjimku — výjimky zůstávají vyhrazené chybám programu, jak stanovilo rozhodnutí 010. Sem patří i mechanické hlášení nevyjádřitelných dotazových vlastností z deskriptoru, tedy dotazová obdoba `ReportLosses`.

**Krok, ve kterém cíl nic negeneruje, má prázdné tělo.** Platí beze změny, co k tomu říká entitní větev: prázdné tělo je tvrzení o frameworku, ne mrtvý kód.

## Důsledky

**`DapperSqlQueryBuilder` se na šablonu přepíše, ne obalí** (rozhodnutí [003](003-one-shot-migration.md)). Jeho dosavadní privátní metody `BuildProjectionPart` až `BuildOrderByPart` se stanou implementacemi kroků; `BuildSelectQuery` zaniká. Tvarové aserce v existujících testech zůstávají v platnosti — pořadí textu se nemění.

**Odblokovává se jedno místo, kde dnes vzniká neplatný artefakt.** Šablona dostává i závěrečný krok, a to je místo, kde `DapperSqlQueryBuilder` skládá C# metodu s přebytečnou čárkou v seznamu argumentů `connection.Query<T>`. Přepis to opravuje mimochodem; dosud to obcházel `AdvisorBenchmarking` regulárním výrazem.

**Stránkování později nerozbije šablonu.** Přijde-li na řadu (dnes je mimo rozsah), nebude u všech cílů částí dotazu: v NHibernate je to `IQuery.SetMaxResults`, v JPA `setMaxResults`, tedy volání API v okolním kódu, kdežto v SQL a LINQ je to součást dotazu. Kdyby šablona fixovala pořadí *textu*, byl by to rozpor; protože fixuje pořadí *běhu* a text skládá závěrečný krok, je to jen další přihrádka, kterou závěrečný krok umístí jinam. Tuhle vlastnost považujeme za hlavní obhajobu varianty 3 proti variantě 2.

**Pro čtvrtý framework se nemění nic.** JPQL má pořadí klauzulí shodné se SQL, takže se do šablony vejde beze změny, a to i s tím, že stránkování vypisuje mimo dotaz. Přidání javového builderu tedy neznamená zásah do `AbstractWrappers` — což je přesně to, co žádá S1.

**Determinismus podle S2 sílí.** Roztřídění, konjunkce podle Q4 i výchozí projekce jsou nově jedno místo, takže tentýž model dá tentýž tvar výstupu ve všech cílech; dosud to bylo tvrzení o třech nezávislých implementacích, z nichž existovala jedna.

**Testy.** Test, že dva `Where` skončí jednou konjunkcí u každého cíle; test, že dotaz bez projekce vede na materializaci celé entity; a testy obou mechanických kontrol — dotaz bez zdroje i se dvěma zdroji vydá záznam a ne výjimku, a agregace bez seskupení rovněž.
