# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Od 2026-09-21 jsou položky roztříděné do pěti kategorií, ne do cílů zadání.** Cíle 1 a 2 jsou hotové a třetí by zbyl jediný, takže dělení podle nich přestalo třídit. Kategorie říká, na čem se pracuje teď — a u zbytku, proč se na něm nepracuje:

- **[Příští položky](#příští-položky)** — na co se sahá teď, a za nimi revize a vydání `2.0.0`, které řadu uzavírají.
- **[Advisor](#advisor)** — Advisor, benchmarking a experimentální požadavky T1–T7, tedy bývalý cíl 3.
- **[Rozhraní](#rozhraní)** — zásahy do frontendu, odložené stranou od všeho ostatního.
- **[Užitečné, ne nutné](#užitečné-ne-nutné)** — co by nástroji nebo repozitáři prospělo, ale nic to nenárokuje a nic tím není blokované.
- **[Zbytky](#zbytky)** — zdokumentované mezery a nezodpovězené otázky, na které se nesahá.

**Odkazy na cíle zůstávají čitelné a čísla se nepřidělují znovu.** Cíl 1 je vydání `1.2.0`, cíl 2 je to, co uzavře `2.0.0`, a cíl 3 je dnešní kategorie [Advisor](#advisor), takže zmínky o „cíli 1" a „cíli 2" v rozhodnutích [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [069](./decisions/069-major-marks-a-milestone-not-a-break.md) dál platí.

**Kategorie neříká pořadí — to dál nese značka** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě, obě stojí na začátku kurzívového řádku vazeb a dnes leží všechny v první kategorii. Samostatný seznam pořadí tenhle soubor nemá a kategorie jím nejsou: položka je v souboru právě jednou a mezi kategoriemi se stěhuje jedinou úpravou.

**Cíl 1 je od 2026-08-26 hotový a vydaný jako `1.2.0`** — překlad entit, mapování a dotazů mezi třemi .NET frameworky. Co to vydání změnilo, nese anotace jeho značky (`git tag -n99 1.2.0`); co nástroj nárokuje dnes, [`architecture.md`](./architecture.md), §9.

**Cíl 2 je od 2026-09-21 uzavřený.** Poslední krok, který mu zbýval, byl běh obou sad v zafixovaném prostředí — profil `test` v compose, obraz Mavenu s Temurinem 25, SQL Server 2022 —, a proběhl: 1442 .NET testů a 146 javových, z toho 97 integračních, bez selhání a bez přeskočení. Tím vstoupily do nároku **F10, F12 i F13** a všech šest požadavků cíle (F7–F10, F12–F13) je nárokovaných ([`architecture.md`](./architecture.md), §9, [`traceability.md`](./traceability.md), sekce *Guarantees* kořenového [`README.md`](../README.md)). Že ten běh není formalita, ukázal i tentokrát: našel jednu vadu, a byla v obrazu — stupeň `java-tests` kopíroval z `Tests/Database` jen jmenovaný skript schématu, kdežto pom odtud od rozhodnutí [089](./decisions/089-differential-verification-as-the-fourth-level-over-a-query.md) čte i celé `Differential/`, takže sada, zelená na checkoutu, v kontejneru spadla na chybějících testovacích zdrojích; stupeň teď kopíruje celý adresář (§6.2). **Podmínka vydání `2.0.0` je tím splněná** (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md)); samo vydání je samostatný krok a zatím neproběhlo. Jediná položka, která v něm zbývala — druhý databázový dialekt —, leží mezi [užitečnými, ne nutnými](#užitečné-ne-nutné) a žádnou jeho větu neblokuje.

---

## Příští položky

Na těchhle položkách se pracuje teď a do téhle kategorie je dostalo jedno společné: každá buď dělá nepravdivou větu, kterou [`architecture.md`](./architecture.md) dnes vyslovuje, nebo vydá tiše špatný výstup. Přesně to by jinak našla revize, která stojí za nimi — a opravovalo by se pod hotovou revizí. Přišly z porovnání [`analysis/`](./analysis/README.md) s kódem — poslední z revize celého repozitáře z 2026-09-21 odbavilo rozhodnutí [094](./decisions/094-entity-identity-inside-a-conversion.md) —; co z obou nálezů zůstalo otázkou, leží ve [Zbytcích](#zbytky). U atributů NHibernate mapování jde navíc přímo o větu [`architecture.md`](./architecture.md), §5, která hranici plochého čtení vyslovuje šířeji, než dnes platí; opraví se s tou položkou.

Za položkami stojí **revize a vydání `2.0.0`**, v tomhle pořadí. Kód se kvůli nim nezmrazuje: položky se odbavují dál a revize měří strom, až bude řada nad ní prázdná.

### Práce

#### Atributy NHibernate mapování, které parser přeskakuje bez záznamu
*Na řadě. Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md) a [004](./decisions/004-unexpressible-facts-as-warnings.md); vyňatá oblast 2 hranice záruk ([`architecture.md`](./architecture.md), §9) se týká prvků, ne atributů. Táž [`architecture.md`](./architecture.md), §5, tvrdí, že hranici plochého čtení vyslovuje záznam — u těchhle atributů zatím ne. Požadavek F11.*

XML parser čte z `<property>` název, sloupec, typ, délku, přesnost, nullabilitu a unikátnost a z `<class>` název, tabulku a schéma; `index`, `check` a `default` hlásí záznamem. Zbytek mizí beze slova: na `<property>` `formula`, `access`, `insert`, `update`, `lazy`, `generated` a `optimistic-lock`, na `<class>` `discriminator-value`, `where`, `mutable`, `optimistic-lock`, `dynamic-insert`, `dynamic-update`, `batch-size` a `lazy`. Přinejmenším `formula` a `where` mění význam — vlastnost s `formula` nemá sloupec a výstup jí ho vymyslí — a srovnání frameworků obojí jmenuje jako výrazovou schopnost NHibernate. Práce je vydat u každého z nich záznam `Loss` týmž tvarem, jakým se hlásí `check` a `default`.

#### Anotace EF Core, pro které model místo má, ale čtou se jako ztráta
*Potom. Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md); souvisí s [049](./decisions/049-language-facts-under-source-precedence.md). Požadavky F1, F5.*

Větev pro neznámou anotaci hlásí záznamem `Loss` i `[StringLength]`, což je délka, `[Unicode]`, což je faceta `IsUnicode`, a `[InverseProperty]`, pro které model nese dosud nevyužité pole `InverseRelationName`. `InventedFactsTest` přitom `[StringLength]` jako nepřečtenou anotaci tvrdí, takže se s ním pohne zároveň. Práce je číst tři anotace do faktů, které pro ně model má, a záznam nechat jen anotacím bez místa.

#### Bázová třída entity mizí bez záznamu
*Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md); dědičnost sama je vyňatá oblast 2 hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavek F11.*

Sdílený C# parser čte z hlavičky třídy jen přístupový modifikátor a seznam bázových typů nečte. Hierarchie v EF Core zdroji — třída odvozená od jiné entity převodu, kterou EF Core mapuje konvencí jako TPH — tak nezanechá žádnou stopu, kdežto týž fakt v NHibernate mapování (`<subclass>`) záznam dostane. Práce je vydat záznam `Loss` u bázového typu, který jmenuje entitu převodu; co dědičnost znamená pro mezireprezentaci, zůstává vyňatou oblastí.

#### Dvojice kolekčních navigací EF Core je N:M, ne dvě 1:N
*Práce podle rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [005](./decisions/005-many-to-many-as-explicit-junction-entity.md); souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §8. Požadavky F1, F3.*

EF Core čte dvě kolekční navigace mezi touž dvojicí entit bez vlastnosti cizího klíče jako N:M s implicitní spojovací tabulkou (od verze 5). Parser EF Core registruje každou kolekci hned jako inverzní 1:N, takže z dvojice vzniknou dva vztahy, které tvrdí cizí klíč na obou stranách; a fáze doplnění nabídne spojovací tabulku z katalogu jen kolekci, která ještě žádný vztah nenese, takže tentýž katalog, který Dapper zdroji spojovací entitu syntetizuje, EF Core zdroj nespraví. Je to dokumentované odvození z toho, co artefakt tvrdí, a mezera putuje k jinému vztahu, takže podle kritéria 067 se materializuje. Práce je nechat kolekci čekat jako konvenční navigaci a po doparsování všech entit spárovat dvojici na N:M týmž mechanismem, jakým se dnes materializují konvenční navigace N:1.

#### Konstruktor `DateTime` v LINQ predikátu se nečte jako konstanta
*Práce podle rozhodnutí [024](./decisions/024-typed-query-operand.md) — slovník `ScalarType` hodnotu `DateTime` má a všechny tři visitory pro ni větev vypisují, jen bez výrobce —; rozhodnutí [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) konstrukci nechává odmítat a kvůli ní přišel vzorový dotaz EF Core o svůj filtr podle data. Požadavky F11, T2, T3.*

Sdílený LINQ parser čte v pozici operandu literál, sloupec, poddotaz a hodnotu ze scope; `new DateTime(2025, 1, 1)` je konstrukce objektu a odmítá artefakt jako nepřečtený filtr. Práce je číst `new DateTime(r, m, d)` — i s kvalifikací `System.` a s časovými složkami — nad celočíselnými literály jako `QueryConstant` typu `DateTime` v ISO zápisu bez zdobení, jak žádá 024; visitory ho pak vypíší jako `'2025-01-01'` v SQL a HQL a `DateTime.Parse("2025-01-01")` v LINQ, protože ty větve už mají. Opačný směr — řetězcový literál T-SQL porovnaný s datovým sloupcem, který se čte jako `String` a do LINQ vyjde jako nepřeložitelné porovnání data s řetězcem — je jiná mezera a tahle položka ji neřeší.

#### Revize před vydáním 2.0.0
*Práce, která uzavírá řadu nad sebou a předchází vydání pod ní. Žánr popisuje [`audits/README.md`](./audits/README.md) a rozhodnutí [007](./decisions/007-documentation-structure.md); předchůdcem je [revize připravenosti verze 1.0](./audits/2026-08-21-version-1-0-readiness-audit.md). Značku pořadí dostane, až bude řada nad ní prázdná.*

Poznámky k vydání bydlí v anotaci značky a značka se nikdy neposouvá (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md)), takže každé tvrzení, které do anotace půjde, musí být ověřené dřív, než se značka vyrazí. Proto revize stojí **před** vydáním: po něm by se opravovalo pod hotovou značkou.

Čím má projít: (1) co `2.0.0` nárokuje, proti otevřeným položkám; (2) [`architecture.md`](./architecture.md) proti kódu, v rozsahu toho, co se pohnulo od `1.2.0` — šest wrapperů, `TransactSql`, `JakartaPersistence`, diferenční ověření, deklarace dialektu, strop zanoření; (3) hranice záruk vyslovená na třech místech (sekce *Guarantees* kořenového [`README.md`](../README.md), §9 a [`traceability.md`](./traceability.md)), tedy kapitola, která v obou předchozích revizích nesla kritické nálezy; (4) rozhodnutí 069–092 a jejich rejstřík — stavy, odkazy na nahrazení, zápisy `revidováno`; (5) mrtvý kód a deklarace bez čtenáře, a k tomu otázka, jestli velikost sdílených bází, které dědí všech šest wrapperů (`AbstractEntityBuilder` má 2362 řádků, `AbstractQueryBuilder` 1475), neohrožuje invariant „nový framework je nový wrapper" (S1); (6) srovnání s vnější praxí.

**Šestá kapitola má tentokrát doslovné měřítko.** Kritéria hodnocení výzkumných artefaktů — dokumentovanost, konzistence s článkem, úplnost a **spustitelnost** — se na tenhle repozitář vztahují přímo, protože artefaktem za článkem je on sám; spustitelnost se navíc dá vyzkoušet cizíma rukama: Docker, kořenový [`README.md`](../README.md) a nic dalšího. K téže kapitole patří číslo pokrytí testy, které CI sbírá (`XPlat Code Coverage`) a které nikde neuvádíme — ne jako hranici, ale aby se pozdější tvrzení nemělo od čeho odchýlit.

**Tahle revize běží na MIS3, takže smí ověřovat spuštěním** — obě sady v profilu `test`, stavbu obrazu i běh systému. Žádná z předchozích revizí build nespustila a ta z 2026-08-23 to o sobě v kapitole 9 výslovně říká.

**Jednu věc si revize vyřeší sama na sobě.** Kategorie [Zbytky](#zbytky) se odvolává na „revizi celého repozitáře z 2026-09-21", a ta nemá v [`audits/`](./audits/README.md) žádný soubor — její nálezy jsou rozepsané rovnou do položek. Položky stojí samy o sobě, jak pravidlo žádá, takže k práci nechybí nic; chybí snímek, na který se ten název odvolává. Buď ho tahle revize nahradí a název se přesměruje na ni, nebo ten název zmizí.

#### Vydání 2.0.0
*Práce podle rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md), které nese kritérium MAJOR i čtyřkrokový postup vydání (ten přenáší z [041](./decisions/041-versioning-and-release.md)). Podmínka je od 2026-09-21 splněná; předchází revize nad touhle položkou. Požadavky S2, S6.*

Cíl 2 je uzavřený, takže vydání, které ho zavírá, je podle rozhodnutí 069 první **MAJOR**. Samo vydání je ale samostatný krok a zatím neproběhlo: `<Version>` v `ORMConvertor/Directory.Build.props` i `CITATION.cff` pořád nesou `1.2.0`.

**Postup je čtyřkrokový a pořadí je jeho podstatou:** (1) práce hotová, pracovní kopie čistá, CI zelené; (2) `<Version>` se posune vlastním commitem spolu s poli `version` a `date-released` v `CITATION.cff` — jiného strojově čitelného nositele čísla nemáme (rozhodnutí [034](./decisions/034-central-version-management.md)); (3) anotovaná značka `2.0.0` vzniká na tom commitu; (4) anotace nese poznámky k vydání ve třech odstavcích: co se změnilo na tvaru výstupu, co přibylo, co se pohnulo na hranici záruk. Látku pro ně nesou rozhodnutí 076–092, [`architecture.md`](./architecture.md) §6.2 a §9 a `git log` od značky `1.2.0`.

**Doklady se pořizují až po posledním commitu, který do vydání patří** — tedy po opravách z revize, ne před nimi. Znamená to zopakovat běh obou sad v zafixovaném prostředí na MIS3 a u obou profilů je krok `build` povinný: `run` sám o sobě použije existující obraz, takže měří strom, ze kterého byl obraz postavený, a ne checkout. Zastaralý strom takhle prošel jako zelený už dvakrát ([`README`](../ORMConvertor/README.md#tests)).

**Značka se nikdy neposouvá a číslo se nikdy nepoužije podruhé.**

## Advisor

Lepší Advisor a benchmarking a na nich stojící experimentální požadavky T1–T7; T7 navazuje na existující ILP Advisor. Advisor s benchmarkingem jsou ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) a jsou zároveň **příštím milníkem zadání**, tedy látkou na `3.0.0` (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md)).

Celá oblast byla vědomě odložená, dokud běžely cíle 1 a 2, a ta podmínka 2026-09-21 uzavřením cíle 2 vypršela. Otázku, čím se pokračuje, zodpovídá pořadí zapsané výš: nejdřív [příští položky](#příští-položky), pak revize, pak vydání `2.0.0` — a teprve potom se sahá sem. Značky pořadí položky do té doby nedostávají.

### Rozhodnutí

#### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1, a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), které hranici pro javovou větev už vyslovilo. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Pro javovou větev je hranice daná: rozhodnutí 076 drží JVM v kontejneru a javová větev Advisoru — implementace `IBenchmarkExecutor` pro javové frameworky, které F15 žádá měřit — běží mimo proces aplikace, buď jako `java` spuštěná v kontejneru aplikace, nebo jako samostatná služba runneru. Tím se otázka nezmenšuje, ale zostřuje: .NET měřený in-process a Java mimo proces se musí dát srovnat, takže volba hranice pro .NET větev a metodologie měření se rozhodují spolu. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

#### Parametrizovaný dotaz Advisor nezměří
*Vyplynulo z rozhodnutí [083](./decisions/083-parameter-as-the-fifth-operand-shape.md), které dalo generované metodě parametry za dosavadní první argument. Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F15, T4, T7.*

Harness benchmarkingu hledá v přeloženém sestavení veřejnou metodu s **právě jedním** parametrem typu `DbContext` (`EFCoreBenchmarkHarnessBuilder`, viz [`architecture.md`](./architecture.md) §8). Metoda parametrizovaného dotazu má za kontextem ještě parametry dotazu, takže ji harness přeskočí a celý běh skončí hláškou „EF Core query method not found", ačkoli překlad proběhl a artefakt je v odpovědi. Rozšířit hledání je práce na pět minut; otázka, která z toho dělá rozhodnutí, je **čím se parametr při měření naplní**. Hodnota určuje selektivitu dotazu, a tedy i naměřený čas i alokaci, takže volba hodnoty je volba o tom, co se vlastně měří: výchozí hodnota typu měří prázdný výsledek, hodnota od volajícího vyžaduje nové pole v požadavku běhu, a hodnota odvozená z katalogu (třeba medián sloupce) váže měření na data, která v cílové databázi být nemusejí. Rozhodnutí musí vyslovit, která z cest platí, a co se stane s dotazem, jehož parametr se naplnit nedá — jestli z běhu vypadne se záznamem, nebo běh odmítne. Souvisí s položkou o nedoplněném překladu níž, protože obě jsou o tom, co přesně Advisor měří.

#### Advisor měří nedoplněný překlad
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Sem odkázalo rozhodnutí [059](./decisions/059-advisor-response-carries-the-measured-translations.md), které svou variantu 3 zamítlo jen pro teď. Souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F15, T7.*

Překladová fáze `/advisor/run` volá `ConversionHandler.Convert` bez připojovacího řetězce, takže benchmark kompiluje a měří překlad bez katalogového doplnění — kdežto `/convert` tentýž vstup doplní a uživatel by nasadil doplněnou verzi. Čísla Advisoru tedy platí o jiném kódu, než jaký si uživatel z nástroje odnese. Od rozhodnutí 059 je to aspoň vidět: odpověď nese měřené artefakty a jejich stav říká, že katalog nebyl použit. Rozhodnout je třeba, jestli má překladová fáze dostat tutéž cachovanou čtečku jako fáze benchmarková — technicky je to po zavedení `CachingCatalogReader` levné, jedna dávka na framework — a co to udělá s naměřenými čísly: doplněné entity nesou jiné atributy a vztahy, takže se mění kompilovaný harness, a změna metodologie měření se musí přeměřit, ne jen zapnout. K témuž rozhodnutí patří i agregace `CatalogReadTime` přes převody běhu, má-li se o katalogové ceně běhu Advisoru něco tvrdit: každý převod svou fázi měří (`architecture.md`, §5.2), ale dokud překladová fáze čtečku nedostane, je ten čas u všech převodů běhu null — součet by tvrdil nulu, která není měřením.

#### Iterační politika benchmarku je konstanta v kódu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavek T7.*

`BenchmarkExecutor` měří každý pár (dotaz × framework) pevným postupem: dvě zahřívací iterace, pilotní běh a z něj odvozených 3–20 měřených iterací s cílem ~500 ms celkem. Konstanty jsou zapsané v kódu bez odůvodnění a bez možnosti je ovlivnit z rozhraní, přitom právě ony určují rozptyl a délku běhu, o které v T7 jde; nadbytečná náhledová invokace — celé jedno provedení dotazu jen kvůli ladicímu výpisu — už je zrušená. Rozhodnout je třeba, jestli jsou tyhle hodnoty součástí metodologie, kterou text práce vysloví a odůvodní, nebo parametrem požadavku, a čím se volba podloží; měnit je bez rozhodnutí znamená měnit význam všech dosavadních čísel.

#### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient`, který k nim teče přes `benchmarks/Common`. Pro srovnání výkonu je to metodologický confound. **Rozsah je nově dohledaný celý** — u PetaPoco vyloučením, protože `Microsoft.Data.SqlClient` v grafu balíků obou jeho projektů není, u EF6 z `WWIDbConfiguration`; podrobnosti nese [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Zbývá tedy volba, ne zjišťování: buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` (samostatný balík, dnes nereferencovaný) a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

### Práce

#### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

#### Hláška o neřešitelném ILP modelu dorazí do logu až s dalším voláním
*Nalezeno při ověření (2026-08-24); popis v [`architecture.md`](./architecture.md), §8, je podle toho opravený. Vyňatá oblast 1 hranice záruk (§9). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavky T7, S6.*

`solve_problem()` v `Advisor/ilp.c` vypisuje `No feasible solution found.` obyčejným `printf`. Standardní výstup je v kontejneru přesměrovaný na rouru, tedy plně bufferovaný, a nikdo ten buffer nevyprazdňuje. Hláška se do logu **dostane**, ale teprve až ji protlačí výstup dalšího volání: tři neřešitelné úlohy za sebou vydaly dvě hlášky, každou o jeden běh opožděnou. Vlastní výpis GLPK dorazí včas, protože nejde přes `stdio`, takže v logu stojí `PROBLEM HAS NO PRIMAL FEASIBLE SOLUTION` bez naší věty vedle sebe.

Oprava je jednořádková — `fflush(stdout)` za tím výpisem, případně řádkové bufferování při inicializaci knihovny —, zadarmo ale není: `libadvisor.so` se překládá jedině v Docker buildu, takže změnu je nutné přeložit a ověřit v kontejneru, a sahá se přitom do oblasti bez jediného testu, kterou vyjímáme ze záruk vcelku. Dokud se to nestane, drží ten stav §8 svým popisem, aby nikdo nehledal hlášku, která po jeho volání v logu ještě není. Návratový kód ani tělo odpovědi to nijak nemění — neřešitelnou úlohu pozná volající z **400**, respektive ze `status: -1`, přesně jak §8 popisuje a jak jsme ověřili.

#### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Souvisí s rozhodnutím [069](./decisions/069-major-marks-a-milestone-not-a-break.md) — nový koncový bod je nová schopnost, tedy vydání MINOR — a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), podle kterého je Advisor kontejnerový rozhodnutím a build nativní knihovny pro Windows nevzniká. Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so` a `AdvisorRunHandler` výjimku z P/Invoke zachytí a vrátí její text, takže uživatel se o nedostupnosti dozví jako o `DllNotFoundException` — po vyplnění celého formuláře a po odeslání běhu. Úvodní odstavec obrazovky přitom říká dopředu, že Advisor potřebuje kontejner; nedostupnost se tedy sděluje dvakrát, jednou naší větou předem a jednou hláškou zavaděče potom.

Aby to obrazovka mohla říct **místo** běhu a vlastními slovy, potřebuje se serveru zeptat, jestli je Advisor na tomhle hostiteli k dispozici — dnes na to není koncový bod a klient si to odvodit nemůže. Je to tedy nový koncový bod, tedy nová schopnost podle rozhodnutí 069; sám o sobě je malý, ale předchází mu volba, jestli do vyňaté oblasti sahat dřív, než se dodělá rozdělané.

## Rozhraní

Zásahy do rozhraní jsme odložili stranou všeho ostatního. Z F14 je hotový vícesouborový vstup a výstup po souborech; zbytek bloku F14–F15 — dávkové vstupy a zobrazení mezireprezentace — je tady a optimalizační půlka F15 patří [Advisoru](#advisor).

### Rozhodnutí

#### Směr překladu jako jedna věc a vstup vedle výstupu
*Navazuje na rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md), které pětikrokový tvar obrazovky zvolilo a je podle něj napsaný kód; případnou změnu je proto třeba nahradit, ne revidovat. Souvisí s [032](./decisions/032-frontend-as-static-pages-without-a-build.md) a s rozhodnutím [066](./decisions/066-records-attributed-to-the-input-unit.md). Požadavky F14, S7.*

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných sekcí viditelných najednou a první dvě z nich jsou volba zdroje a volba cíle. Důsledek je, že směr překladu na obrazovce nikde nestojí jako jedna věc: „EF Core → NHibernate" se poprvé objeví až v hlavičce výsledku a prohodit obě volby jde jen ručně, dvěma zásahy do dvou rozbalovacích seznamů. Jedna řádka směru s tlačítkem pro prohození by z pěti sekcí udělala čtyři, což S7 nebrání — „nejvýš pět kroků" je strop, ne kvóta —, ale je to změna volby, kterou 033 vyslovilo výslovně, takže patří do nového rozhodnutí.

Do téhož rozhodnutí patří druhá otázka, protože obě mění tvar téže obrazovky a navrhovat je zvlášť by znamenalo navrhnout ji dvakrát: **jestli má vstup stát vedle výstupu.** Dnes jsou vstupní jednotky nahoře jako textová pole a artefakty dole jako panely, takže se zdroj a výsledek nedají číst současně — na výkladové stránce vedle sebe stojí, na nástrojové ne. Podstatná je tu poctivost, ne rozvržení: server neříká, který artefakt vznikl ze které jednotky (§9, zúžení F14), takže sloupce vedle sebe se buď musí spárovat toutéž jmennou heuristikou, jakou se artefakty pojmenovávají, a jako heuristika se i označit, nebo nesmí tvrdit párování vůbec a nesou pak nadpisy typu „co jste poslali" a „co přišlo zpět". Druhá cesta nic nevymýšlí. Půlka předpokladů skutečného párování už stojí: jednotky od rozhodnutí [066](./decisions/066-records-attributed-to-the-input-unit.md) nesou jméno a záznamy na ně ukazují; co dál chybí, je druhá půlka — aby artefakt výstupu nesl, ze které jednotky (přesněji: z které entity a jejích jednotek) vznikl.

#### Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje
*Zúžení, které dnes vyslovuje [`architecture.md`](./architecture.md), §9 („zobrazení IR verze nenárokuje vůbec"). Souvisí s rozhodnutími [010](./decisions/010-diagnostics-as-returned-data.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md) a [069](./decisions/069-major-marks-a-milestone-not-a-break.md). Požadavky F11, F14.*

Požadavek F14 žádá zobrazení čtyř věcí — vstupu, mezireprezentace, výstupu a diagnostiky — a nárokujeme tři: `/convert` mezireprezentaci nevrací a rozhraní ji nemá odkud vzít. Je to jediné místo, kde se dnes nárok na F14 zužuje z důvodu, který leží na serveru, ne na obrazovce, a zároveň to nejlépe placené místo pro text práce: pipeline parse → doplnění → build se čtenáři, který nástroj nikdy nespustí, ukazuje právě prostředním článkem.

Rozhodnout je třeba dřív, než se cokoli začne psát, protože cena není ve vykreslení: serializovaný tvar `EntityMap`, klíče, vztahů a dotazových instrukcí by se stal součástí REST kontraktu se vším, co to znamená pro verzování (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md), změna MINOR při přidání pole a PATCH při jeho odebrání). Otázka tedy zní, jestli se mezireprezentace vydává jako plnohodnotná část odpovědi, nebo jako výslovně nestabilní náhled, u kterého se dopředu řekne, že se může měnit mezi vydáními — a druhá odpověď je levnější jen zdánlivě, protože nestabilní část kontraktu je pořád část kontraktu.

### Práce

#### Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška
*Souvisí s rozhodnutím [033](./decisions/033-shape-of-the-static-frontend-screens.md), které validaci XML s číslem řádku zavedlo, a s [032](./decisions/032-frontend-as-static-pages-without-a-build.md), bod f (žádná další vendorovaná knihovna bez rozhodnutí). Požadavek S7.*

Validace před odesláním hlásí u nesprávně utvořeného XML číslo řádku a serverová hláška u SQL nese řádek a sloupec z `TSql160Parser`. Editor jednotky je ale holý `<textarea>` bez číslování, takže „řádek 7" se v něm hledá počítáním. Doslovné znění S7 mluví o zvýraznění chyb na úrovni souboru a řádku a tohle je jeho druhá půlka, která chybí — první, tedy chyba přiřazená ke konkrétní jednotce, hotová je.

Práce je to hotová v zadání, ne v rozvaze: postranní sloupec s čísly řádků, který se posouvá spolu s textovým polem, je několik desítek řádků vlastního kódu a chová se spolehlivě. Co je potřeba nedělat, je sáhnout po hotovém editoru — CodeMirror nebo cokoli podobného by byla třetí vendorovaná knihovna, a to je podle bodu 032f samostatné rozhodnutí, ne detail implementace.

## Užitečné, ne nutné

Co by nástroji nebo repozitáři prospělo, ale nic to nenárokuje: žádná věta záruk na tom nestojí a žádná jiná položka tím není blokovaná. Odbavit se to dá kdykoli — nebo nikdy. Značky pořadí tyhle položky nedostávají.

### Rozhodnutí

#### Druhý databázový dialekt
*Vyplynulo z rozhodnutí [086](./decisions/086-target-database-dialect-declared-by-the-descriptor.md), které deklaraci zavedlo a slovník otevřelo s jedinou hodnotou. Jednu ze tří otázek odbavilo rozhodnutí [088](./decisions/088-a-declared-foreign-source-dialect-is-not-read.md). Souvisí s rozhodnutím [082](./decisions/082-t-sql-read-and-written-by-a-shared-project.md), které multidialektovou knihovnu zamítlo. Vyňatá oblast 5 hranice záruk ([`architecture.md`](./architecture.md), §9). Značku pořadí nemá a nedostane: žádná záruka na ní nestojí a žádnou jinou položku neblokuje. Požadavky F5, F7–F10, S2.*

Od 2026-09-21 nástroj říká, pro jaký databázový systém artefakty píše, a je to SQL Server 2022. Druhý systém do slovníku přidat lze, ale zapsat hodnotu nestačí a odpovědět bylo potřeba trojí. **Druhá z těch tří otázek je od 2026-09-21 zodpovězená** rozhodnutím 088 a zbývají dvě.

**Čím se dialekt volí.** Deskriptor dnes nese jedinou hodnotu a volba z rozhraní neexistuje — je to táž otevřená věta, jakou rozhodnutí [013](./decisions/013-target-framework-versions.md) nechalo u verze frameworku, a obě se pravděpodobně zodpoví spolu, protože obojí je fakt o cíli převodu a obojí by se muselo dostat do požadavku, do deskriptoru a do záznamu běhu.

**Jak se deklaruje dialekt zdrojového artefaktu — zodpovězeno 2026-09-21** rozhodnutím [088](./decisions/088-a-declared-foreign-source-dialect-is-not-read.md): zdroj smí dialekt svého doslovného SQL deklarovat v požadavku převodu a deklarace jiného systému, než který tahle verze čte, čtení **zastaví** — dotaz se nevydá (`Failure`), doslovný typ sloupce se nepřečte (`Loss`). Neplyne z toho, že by se cizí dialekt překládal; plyne z toho, že se neuhodne. Rozhodnutí u toho opravilo předpoklad, se kterým tahle položka vznikla: věta, že SQL psané pro jiný systém gramatikou neprojde, platí pro cizí **syntaxi** (`LIMIT 10`, `||`) a pro **neznámá jména** (`VARCHAR2(50)`), kdežto jméno legální v obou dialektech s jiným významem projde tiše a špatně — `timestamp` je v T-SQL osm bajtů binárních dat, jinde okamžik v čase, a `SUBSTR` je pro `TSql160Parser` obyčejné volání funkce. Práce z toho je od 2026-09-21 hotová — zábrana sedí v `SqlQueryReader` a v `SqlTypeSpelling.Read`, deklaraci nese požadavek převodu i překladová obrazovka a záznam běhu ji vydává ([`architecture.md`](./architecture.md), §5 a §5.1).

**Jestli se tím mění zamítnutí multidialektové knihovny.** Rozhodnutí 082 ji zamítlo s odůvodněním, že jiný dialekt by se přeložil **tiše**. Deklarace je právě ta věc, která tichost odstraňuje, takže argument sám o sobě už neplatí a otázka se otevírá znovu — nikoli ale automaticky ve prospěch knihovny: cena je pořád celá gramatika navíc a druhá tabulka jmen, a užitek je nulový, dokud v repozitáři není druhý databázový systém, proti kterému by to šlo ověřit. To byl i druhý důvod rozhodnutí 019 dialekt tehdy nezavádět. Rozhodnutí 088 přitom tuhle otázku nebere: zábrana cizí dialekt odmítá, nečte ho, takže knihovnu nepotřebuje ani nevylučuje.

**Co se změní v den, kdy budou čitelné dialekty dva.** Deklarace zdroje, kterou zavedlo rozhodnutí 088, se ze **zábrany** stane **přepínač** — hodnota `AnotherSystem` se rozpadne na pojmenované systémy a totéž pole začne vybírat gramatiku a tabulku jmen. Tvar se nemění, roste jen slovník. Teprve tehdy vznikne otázka, která se dnes položit nedá, protože čitelný i zapisovatelný dialekt je jeden: **co nástroj dělá, když se dialekt zdroje s cílovým rozejde.** Překlad dialektu je vlastní volba a patří sem, ne do 088.

#### Celý vložený zdroj si nástroj nerozdělí na mapovací a dotazovou část
*Souvisí s rozhodnutími [025](./decisions/025-query-language-as-content-type.md) a [047](./decisions/047-content-type-reaches-the-query-parser.md), která jazyk jednotky svěřila deklaraci klienta, a hlavně s [081](./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md), které roli jednotky přesunulo z deklarace na nárok parserů zdrojového frameworku. Míří proti vyslovenému necíli v [`use-cases.md`](./use-cases.md) („nástroj nepozná dotaz uvnitř service třídy"). Požadavky F11, F14, S1, S2, S7.*

Uživatel má v ruce soubor, ne jednotku. Typicky je v něm entitní třída a hned pod ní repozitář s `connection.Query<Customer>("select …")` nebo s LINQ řetězem, v Javě entita a vedle ní metoda s `createQuery`. Dnes ten text musí rozřezat sám: jednotka deklaruje typ obsahu a u C# i u Javy ta hodnota nese vedle jazyka i roli (`CSharpEntity` proti `CSharpQuery`, `JavaEntity` proti `JavaQuery`), takže „vlož, co máš" znamená „vlož zvlášť entitu a zvlášť dotaz". Otázka zní, jestli rozřezání má umět nástroj.

Celý vložený soubor dnes nekončí chybou, nýbrž nesmyslem, a je to tiché. Sdílený C# parser — a stejně tak javový — bere **každou deklaraci třídy v jednotce** jako entitu převodu; je to vědomé pravidlo, na kterém stojí vícetřídní vstup F14 ([`architecture.md`](./architecture.md), §5), takže z repozitáře vznikne mapa entity `CustomerRepository` a z ní artefakt, prázdná třída pojmenovaná po něm. Dotaz v jeho metodě nepřečte nikdo: orchestrace od rozhodnutí 081 nabízí jednotku oběma průchodům, jenže `LinqQueryParser` i `DapperSqlQueryParser` si nárokují `CSharpQuery`, ne `CSharpEntity`, takže dotazový průchod jednotku minul. Záznam o tom nevznikne — jednotka byla nárokovaná a něco vydala, takže není ani nenárokovaná, ani jalová (rozhodnutí [066](./decisions/066-records-attributed-to-the-input-unit.md)).

Rozhodnout je třeba trojí. **Kde rozdělení bydlí.** V klientovi ho rozhodnutí 081 už jednou zamítlo: rozřezávací kód v JavaScriptu leží mimo testovací sadu a záznamy by ukazovaly na fragmenty vyrobené klientem, ne na soubor, který má uživatel v ruce. Na serveru zbývá říct, kdo dělí — rozpoznat entitu od repozitáře je tvrzení o zdrojovém frameworku, takže podle S1 patří do wrapperu, ne do orchestrace, a je to táž věta, jakou vyslovilo 081 („hodnota jmenuje jazyk, framework si jmenuje roli"). Mechanika k tomu existuje celá; chybí jen to, aby si tutéž hodnotu nárokovaly obě strany i u C# a Javy, jak to dnes dělá jedině `JavaQuery` u MyBatisu (rozhodnutí [084](./decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md)).

**Podle čeho se role pozná.** Tady je cena celé položky. Věta „třída, která nese volání dotazu, není entita" je tvrzení o tvaru textu, a tvar textu jsme k rozhodování dvakrát odmítli pustit — v 025 u volby jazyka a v 047 u výběru dotazového parseru —, pokaždé proto, že hádání je nedeterminismus zakázaný S2. Rozdíl je v tom, že jazyk se tu nehádá, deklaruje ho klient dál, a hádá se role uvnitř souboru; tu vyslovit pravidlem lze, ale pravidlo musí být zapsané a musí se hlásit. Co se přečetlo jako entita, co jako dotaz a co se přeskočilo, patří do záznamů — jinak je špatné rozdělení přesně ten tiše špatný výstup, jaký nástroj jinde odmítá (rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md) a [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md)).

**Kam až ambice sahá.** [`use-cases.md`](./use-cases.md) dnes říká, že vyhledání artefaktů v projektu je práce uživatele a že nástroj „nepozná dotaz uvnitř service třídy". Rozhodnutí tu větu buď posune — jeden vložený soubor ano, procházení repozitáře ne —, nebo ji potvrdí; posunout ji lze, zmrazený ten dokument není, ale musí se to udělat vědomě a v tomtéž rozhodnutí. S dávkovým vstupem, který F14 jmenuje („archiv projektu"), to nesplývá: archiv je víc souborů, z nichž každý má dál jednu roli, kdežto tady jde o jeden soubor, jehož role jedna není.

Proč je položka tady, a ne mezi příštími: nenárokujeme ji nikde — ani §9, ani `use-cases.md` neslibují, že nástroj vstup rozřeže — a obejít se dá tím, co obrazovka umí dnes, tedy jednou jednotkou na roli. Užitek je v S7 a ve větě F14 o vkládání celých tříd: uživatel má v ruce nejčastěji právě ten celý soubor.

#### Vynucení stylu a reprodukovatelnost sestavení
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.4. Souvisí s rozhodnutími [034](./decisions/034-central-version-management.md) a [039](./decisions/039-container-configuration-of-the-environment.md). Požadavky S2, S5.*

„Reprodukovatelné prostředí" dnes znamená „jedním příkazem", ne „bajtově stejně": soubor zámku závislostí neexistuje, základní obrazy kontejnerů jsou připnuté na pohyblivé značky a pravidla stylu, která v repozitáři jsou, build nevynucuje. Rozhodnout je třeba, jestli se nárok S2 rozšiřuje z výstupu překladu i na sestavení samo — zámek závislostí, obrazy podle digestu, styl vynucený v CI — a jestli je to tvrzení, které text práce potřebuje, nebo údržba, která počká; dokud volba nepadne, platí dnešní užší čtení a nic víc se netvrdí. Táž otázka se týká i akcí v CI: `actions/checkout@v5` a spol. visí na pohyblivé značce, ne na digestu, takže co workflow spustí, se může změnit bez zásahu do repozitáře.

#### Trvalý identifikátor vydání
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.1, a [pět doporučení fair-software.eu](https://fair-software.eu/). Souvisí s rozhodnutím [069](./decisions/069-major-marks-a-milestone-not-a-break.md), jehož vysloveným předpokladem je, že nástroj není publikovaný. Sahá se na to až úplně na konci vývoje; značku pořadí do té doby nedostává.*

Kořenový [`README.md`](../README.md) žádá citovat verzi a `CITATION.cff` k tomu nese metadata, jenže vydání nemá trvalý identifikátor: jediným nositelem je značka v gitu, která existuje, dokud existuje repozitář. Vnější praxe pro výzkumný software — pět doporučení fair-software.eu, tedy veřejný repozitář, licence, záznam v registru, citovatelnost a kontrolní seznam kvality — má tady čtyři body z pěti a chybí právě ten registr.

Rozhodnout je třeba dvojí. **Jestli se fork cizího prototypu archivuje pod vlastním identifikátorem**, a pokud ano, kde a s jakým autorstvím; `LICENSE` nese dva držitele autorských práv právě proto, že repozitář je napůl zděděný. A **jestli záznam v registru znamená, že nástroj je publikovaný** ve smyslu předpokladu rozhodnutí 069: to rozhodnutí se má podle vlastní věty nahradit, ne dovysvětlit, jakmile předpoklad přestane platit, a archiv s identifikátorem je té hranici blízko, byť konzumenta nevyrábí.

Do té doby stojí citace na značce a na `CITATION.cff`, a je to vědomé: identifikátor se razí z vydání, takže se přidá až docela nakonec, ne uprostřed vývoje.

## Zbytky

Zdokumentované mezery a nezodpovězené otázky, na které se nesahá. Jsou tu **zapsané, ne zařazené**: značky pořadí nedostávají a dojít na ně může kdykoli — nebo vůbec. Zapisujeme je proto, aby nález nezůstal jen v konverzaci a aby text práce věděl, co nástroj o svých frameworcích netvrdí.

Zdroje jsou tři. **Revize celého repozitáře z 2026-09-21**, z níž to, co byla oprava, leží v kódu a v [`architecture.md`](./architecture.md) — sdílená čtečka T-SQL odmítá příkaz stojící vedle překládaného `SELECT`u, klauzuli `WITH`, `INTO`, `FOR XML`/`FOR JSON` a `TABLESAMPLE` a hlásí nápovědy ztrátou; sdílený LINQ parser jmenuje kroky, které mění množinu řádků, a čte zpět `g.Key`; T-SQL visitor vypisuje `COUNT(*)` bez aliasu. Ze čtyř otázek, které po opravách zbyly, je jedna od 2026-09-21 zodpovězená rozhodnutím [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md) a týž den naimplementovaná — strop hloubky zanoření, u kterého měření ukázalo, že vada je širší, než jak ji revize našla: padá i `TSql160Parser`, na prosté závorce ze všech nejdřív, takže strop dostalo všech pět rekurzivně čtených jazyků, ne tři. Druhá je od téhož dne zodpovězená rozhodnutím [093](./decisions/093-unreadable-input-is-a-unit-failure.md) a týž den naimplementovaná — neparsovatelné XML shazovalo celý převod, dnes je to `Failure` jedné jednotky jako u zbylých čtyř jazyků —, třetí je mezi [příštími položkami](#příští-položky) a tady zůstává jedna.

Druhým zdrojem je **porovnání [`analysis/`](./analysis/README.md) s kódem z 2026-09-14** a k němu čtyři levné konstrukce, které rozhodnutí [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) vědomě nechalo odmítat, ač by je model unesl a všechny cíle vyjádří. Pět položek odsud jsme 2026-09-15 přeřadili do tehdejšího cíle 2 — skaláry mimo uzavřený seznam, modifikátor `virtual`, nepersistovanou vlastnost, `DISTINCT` a výčet v `IN` —, protože každá rozšiřuje model nebo slovník, který javové buildery zdědí, a rozšíření je levnější před šesti buildery než po nich; pět dalších je dnes mezi příštími položkami.

**Třetím zdrojem je rozhodnutí [093](./decisions/093-unreadable-input-is-a-unit-failure.md) a má tu jedinou položku.** Při srovnání toho, jak všech pět jazyků hlásí nečitelný vstup, vyšlo najevo, že u C# nehlásí pozici nikdo; rozhodnutí to vědomě nechalo stranou, protože samo volilo mezi výjimkou a záznamem a C# je na straně záznamu už dnes.

**Dvě ze zbylých otázek jsou tatáž věc v jiném hávu** — `ESCAPE` u `LIKE` a agregační `DISTINCT`: obojí je konstrukce, kterou mezireprezentace nenese, kterou všechny cíle vyjádřit umějí a kterou parser proto odmítá nebo zahazuje. Až na ně dojde, je to jedno rozhodnutí, ne dvě; sdružit dvě otázky téže látky do jednoho souboru je tvar, který v tomhle souboru drží i položka o směru překladu („obě mění tvar téže obrazovky").

### Rozhodnutí

#### Syntaktickou chybu v C# nehlásí nikdo s pozicí
*Vyšlo najevo při psaní rozhodnutí [093](./decisions/093-unreadable-input-is-a-unit-failure.md), které tenhle případ vědomě nechalo stranou. Souvisí s [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md). Požadavky F11, S7.*

`CSharpSyntaxTree.ParseText` syntaktickou chybu zná a my se jí neptáme: parser vezme ze stromu deklarace tříd a diagnostiky Roslynu nečte vůbec. Jednotka `CSharpEntity` s rozbitou třídou tak nevydá nic a mluví za ni až obecný `Failure` orchestrace — „jednotka byla přečtena a nevzešlo z ní ani mapování, ani dotaz" —, tedy bez pozice a bez důvodu; u jednotky LINQ je to věta „No LINQ query chain was found in the source". Zbylé čtyři jazyky přitom řádek a sloupec hlásí a [`architecture.md`](./architecture.md) §9 o klientské validaci tvrdí, že syntaktickou chybu C# hlásí server — hlásí ji ovšem jen v té míře, že z jednotky nic nevzešlo.

Rozhodnout je třeba, co s tím, že Roslyn na rozdíl od našich čteček **zotavuje**: ze zpřeházeného textu vytáhne třídu, kterou dnes přeložíme. Odmítnout celou jednotku, jakmile strom nese syntaktickou chybu, je odpověď shodná se zbylými čtyřmi jazyky, ale vzala by překlad vstupům, které dnes projdou; hlásit chybu a přeložit, co se zotavilo, zase odporuje tomu, že `Failure` podle §5.1 znamená artefakt nevydaný.

#### Jednotka SQL nese právě jeden příkaz, a víc jich odmítá
*Vyplynulo z opravy z 2026-09-21 (viz [`architecture.md`](./architecture.md), §5). Souvisí s rozhodnutími [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) a [081](./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md). Požadavky F8, F11, F14.*

Sdílená čtečka T-SQL brala z textu první `SELECT` a zbytek ignorovala, takže `DELETE FROM T; SELECT …` odcházelo jako artefakt pro čtení a o smazání neřeklo nic. Od 2026-09-21 je každý příkaz vedle překládaného `SELECT`u `Failure`, který ho jmenuje — to je odpověď rozhodnutí 070 a táž, jakou dává MyBatis wrapper zapisujícímu `<insert>`.

Odmítnutí je ale jen bezpečná polovina odpovědi. Rozhodnutí 081 dalo jednotce právo nést **víc dotazů** a `IQueryParser.Parse` vrací builder na každý z nich; hbm.xml i mapper MyBatisu toho využívají a jednotka `SqlQuery` s dvěma `SELECT`y by mohla také. Rozhodnout je třeba, jestli se dva `SELECT`y jedné jednotky mají číst jako dva dotazy — a pokud ano, čím se pojmenují, když `SELECT` na rozdíl od `<query name>` a `<select id>` jméno nenese, takže by ho musel vymyslet nástroj (proti čemuž stojí rozhodnutí [028](./decisions/028-assembly-name-is-not-ours-to-invent.md)). Zapisující příkaz zůstane odmítnutý tak jako tak.

#### Agregační `DISTINCT` nemá v projekci místo
*Souvisí s rozhodnutím [073](./decisions/073-distinct-as-a-flag-of-the-query-scope.md), které nese `DISTINCT` jako příznak (pod)dotazu a modifikátor agregátu vědomě nechalo stranou. Požadavky F8, F11, T2.*

`COUNT(DISTINCT c.CustomerId)` je modifikátor funkce, ne dotazu: `ProjectInstruction` nese funkci jako holý název a operand sloupce s agregační funkcí totéž, takže zhroucení uvnitř agregátu nemá kam. T-SQL parser ho do rozhodnutí 073 tiše přepisoval na `COUNT(c.CustomerId)`, tedy jinou hodnotu; dnes projekci s ním vypustí se záznamem `Loss` a v podmínce filtru nebo filtru po agregaci odmítne artefakt záznamem `Failure`, HQL parser ho ve své podmnožině gramatiky hlásí jako syntaktickou chybu a LINQ tvar `g.Select(e => e.Sloupec).Distinct().Count()` je nečitelná projekce se záznamem `Loss`. Všechny tři cíle modifikátor vyjádří — T-SQL i HQL `count(distinct …)`, LINQ `.Select(…).Distinct().Count()` nad skupinou — a v ručně psaném SQL, které žádá F8, je běžný. Rozhodnout je třeba, jestli funkce v projekci a v operandu dostane příznak zhroucení, a jak ho vypíše LINQ cíl bez seskupení, kde agregát v projekci dnes ani nenese.

#### Alias v SQL jako zdroj mapování Dapperu
*Souvisí s rozhodnutími [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), [017](./decisions/017-source-precedence-for-mapping-facts.md) a [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md). Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4, a [tutoriál k Dapperu](./analysis/tutorials/dapper-getting-started.md), krok 4. Požadavek F6.*

Jedinou formou mapování, kterou Dapper má, je alias `AS` v dotazu, a nástroj ji nečte: SQL parser aliasy nese jen jako alias projekce a entitní mapa Dapper zdroje dostává sloupce až z katalogu, který páruje sloupec s vlastností podle jména. Doménu z tutoriálu — vlastnost `Id` nad sloupcem `AuthorId` — tak katalog nespáruje a klíč nedodá. Rozhodnout je třeba, jestli má alias z dotazové jednotky téhož převodu propsat název sloupce do entitní mapy jako tvrzení prvního stupně; je to fakt jednoho dotazu, ne třídy, a dva dotazy mohou týž sloupec aliasovat různě, takže rozhodnutí musí říct, co je konflikt a co ne.

#### Fluent konfigurace EF Core jako vstupní jednotka
*Rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [068](./decisions/068-source-framework-precedence-orders-the-reading.md) s parserem fluent konfigurace počítají, ale položku k němu nikdo nezapsal. Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4. Požadavky F1, F5.*

Srovnání frameworků označuje fluent API v `OnModelCreating` za primární formu mapování EF Core a nástroj čte jen anotace a konvence. Třída kontextu navíc není ze čtení vyloučená: každá deklarace třídy v jednotce je entita, takže vložený `DbContext` vyjde jako entita s kolekčními vztahy na své `DbSet` vlastnosti. Rozhodnout je třeba, jestli fluent konfigurace vstupuje jako další artefakt EF Core, čtený v pořadí, které 068 stanovilo, a co se do té doby dělá s třídou kontextu ve vstupu — vyloučení se záznamem je levnější než dnešní tichý omyl.

#### Dapper.Contrib v rozsahu, nebo mimo něj
*Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §2. Souvisí s rozhodnutím [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md).*

Dapper.Contrib přidává k holému Dapperu atributy `[Table]`, `[Key]` a `[ExplicitKey]`, tedy tři mapovací fakty, které holý Dapper nemá kde vyslovit. Nástroj čte jen holý Dapper. Rozhodnout je třeba, jestli je Contrib výslovně mimo rozsah, nebo jestli ho Dapper parser čte jako anotace — po vzoru EF Core parseru a s týmž kritériem rozhodnutí 067.

#### Klauzule `ESCAPE` u `LIKE` nemá v podmínce místo
*Souvisí s rozhodnutím [051](./decisions/051-like-pattern-translated-not-carried-over.md), které vzorek `LIKE` do LINQ překládá, a s [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md), které `ESCAPE` nechává odmítat. Požadavky F11, T2, T3.*

`LIKE 'A!_%' ESCAPE '!'` odmítá artefakt záznamem `Failure`, protože `ComparisonCondition` s operátorem `Like` nese jen vzorek a vzorek čtený bez únikového znaku vybere jiné řádky — podtržítko by bylo zástupným znakem. Všechny tři cíle únikový znak nesou: T-SQL i HQL klauzulí `escape`, EF Core přetížením `EF.Functions.Like(x, vzorek, únik)`. Rozhodnout je třeba, jestli únikový znak dostane místo na porovnání s operátorem `Like`, a jak se s ním vypořádá překlad vzorku z rozhodnutí 051: kotvený vzorek s únikem před zástupným znakem má stále přesný protějšek (`'A!_%'` je `StartsWith("A_")`), jen ho rozpoznání musí číst po únikovém znaku.
