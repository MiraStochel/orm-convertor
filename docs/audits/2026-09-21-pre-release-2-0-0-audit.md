# Revize před vydáním 2.0.0, 2026-09-21

Revize, která stojí mezi uzavřením druhého cíle a vydáním, jež ho zavírá. Ptá se úžeji než revize z [2026-08-23](2026-08-23-post-release-1-1-0-audit.md) a v jiném pořadí než ona: poznámky k vydání bydlí v anotaci značky a značka se nikdy neposouvá (rozhodnutí [069](../decisions/069-major-marks-a-milestone-not-a-break.md)), takže každé tvrzení, které do anotace půjde, musí být ověřené **dřív**, než se značka vyrazí. Předmětem je tedy nárok verze a to, co ho nese: otevřené položky, [`architecture.md`](../architecture.md) proti kódu v rozsahu toho, co se pohnulo od `1.2.0`, hranice záruk vyslovená na třech místech, rozhodnutí 069–094 a jejich rejstřík, mrtvý kód a velikost sdílených bází, a nakonec srovnání s vnější praxí pro výzkumný software.

Nálezy jsou číslované po kapitolách a označené závažností. Co z nich plyne, je v kapitole 9.

**Tahle revize na rozdíl od všech předchozích ověřovala spuštěním.** Běžela na MIS3, tedy na stroji s Dockerem, takže tvrzení o obou testovacích sadách, o stavbě obrazu, o běhu systému i o pokrytí testy jsou v ní **měření, ne citace**. Co přesně se spustilo a s jakým výsledkem, shrnuje kapitola 7.

## Zafixované verze a stav, ke kterému audit platí

Pracovní kopie ke dni 2026-09-21, commit **`36d688d` „The Java half of the inheritance boundary reaches the guarantees section"**. Strom je čistý; `origin/main` je o tři commity pozadu, protože poslední tři ještě nebyly odeslané, a audit měří pracovní kopii, tedy strom, ze kterého vydání vznikne.

Poslední značka je `1.2.0`. `<Version>` v `ORMConvertor/Directory.Build.props` i pole `version` v `CITATION.cff` nesou dál `1.2.0`, jak postup vydání podle rozhodnutí [069](../decisions/069-major-marks-a-milestone-not-a-break.md) žádá: číslo se posouvá vlastním commitem až po poslední práci, která do vydání patří — tedy po opravách z téhle revize.

Od značky `1.2.0` je na `main` **45 commitů**. Verze frameworků jsou centrálně v `ORMConvertor/Directory.Packages.props`, `ORMConvertor/JavaTests/pom.xml`, `Directory.Build.props` a `global.json` (rozhodnutí [034](../decisions/034-central-version-management.md)); audit je nesnímkuje a při kontrole proti tabulce „Zafixované verze" v [`architecture.md`](../architecture.md) nenašel odchylku v žádném řádku.

---

## 1. Co `2.0.0` nárokuje, proti otevřeným položkám

Podmínka vydání je splněná: kategorie *Příští položky* v [`open-items.md`](../open-items.md) je nad revizí prázdná, obě sady v zafixovaném prostředí jsou zelené (kapitola 7) a všech šest požadavků druhého cíle — F7–F10, F12 a F13 — je nárokovaných. Vydání samo je samostatný krok a zatím neproběhlo. Vady jsou v tom, **čím je nárok doložený směrem nahoru**, tedy k zadání.

### 1.1 Šest požadavků, které vydání zavírá, nemá v `use-cases.md` jediný scénář — kritické

[`use-cases.md`](../use-cases.md) o sobě v druhé větě říká: „Požadavky F1–F15 jsou odvozené: každý z nich vznikl z některého scénáře níž, a **když se scénář nenajde, je to samo o sobě zjištění**." Scénářů je pět a odkazují se dohromady na F1–F6, F11, F14 a F15.

Chybí tedy **F7, F8, F9, F10, F12 a F13** — a to jsou přesně ty požadavky, kvůli kterým se `2.0.0` vydává. Dokument nemá scénář pro migraci přes hranici ekosystémů, ani pro javový cíl, ani pro spustitelnou javovou sadu, ani pro diferenční ověření; všech pět scénářů popisuje svět tří .NET frameworků, jaký byl před rozhodnutím [076](../decisions/076-java-wrappers-in-csharp-jvm-in-containers.md).

Nález není o tom, že by ty požadavky nebyly splněné — kapitola 7 ukazuje, že splněné jsou. Je o tom, že **vrstva, která má říkat, proč je nástroj má umět a pro koho, o nich mlčí**, a že dokument sám tenhle stav prohlašuje za zjištění. Vydání, které cíl zavírá, by tedy zavíralo šest požadavků bez jediné věty o tom, koho se týkají. Je to táž třída vady, jakou revize z 2026-08-23 našla u nároku na S5 (nález 1.1): doklad, o který se hlavní tvrzení vydání opírá, popisuje jiný stav než ten vydávaný.

### 1.2 `CITATION.cff` popisuje nástroj, který překládá jen mezi .NET frameworky — závažné

Pole `abstract` v [`CITATION.cff`](../../CITATION.cff) zní: „A tool for translating entities, mappings, and queries between **.NET ORM frameworks (Dapper, NHibernate, Entity Framework Core)**…". Seznam `keywords` obsahuje `.NET`, `Dapper`, `Entity Framework Core` a `NHibernate` a neobsahuje Javu, Hibernate, EclipseLink, MyBatis ani Jakarta Persistence.

Soubor je **strojově čitelný popis vydání** a je jediným nositelem citace vedle značky. Při vydání se v něm podle postupu 069 posouvají pole `version` a `date-released`; pokud se u toho neopraví i `abstract` a `keywords`, vydá `2.0.0` — vydání za javový ekosystém — metadata, která javový ekosystém nezmiňují. Kořenový [`README.md`](../../README.md) přitom obě ekosystémy jmenuje hned v první větě, takže dvě místa téhož tvrzení se rozcházejí.

### 1.3 Číslo velikosti sdílené báze v zadání téhle revize je zastaralé — drobné

Položka *Revize před vydáním 2.0.0* v [`open-items.md`](../open-items.md) zadává pátou kapitolu slovy „velikost sdílených bází, které dědí všech šest wrapperů (`AbstractEntityBuilder` má 2362 řádků, `AbstractQueryBuilder` 1475)". Ke dni revize má `AbstractEntityBuilder` **2728 řádků**, tedy o 366 víc; číslo u `AbstractQueryBuilder` sedí. Otázka tím nemizí — odpovídá na ni nález 5.4 —, jen její zadání už neplatí doslova.

---

## 2. `architecture.md` proti kódu

Kontrola proběhla v rozsahu, který položka revize vymezuje: šest wrapperů, `TransactSql`, `JakartaPersistence`, diferenční ověření, deklarace dialektu a strop zanoření. Co sedělo, shrnuje nález 2.4; tady jsou odchylky.

### 2.1 §5 a §9 tvrdí „tři .NET ORM" a „devět směrů", ačkoli frameworků je šest a směrů šestatřicet — závažné

Tři místa nesou větu z doby před javovým ekosystémem:

- [`architecture.md`](../architecture.md) §5 otevírá odrážku o dotazové cestě nadpisem „**Dotazová matice je úplná ve všech devíti směrech.**" — a hned za ním vyjmenovává parsery a buildery všech šesti frameworků.
- [`architecture.md`](../architecture.md) §9, *Rozsah implementace ve zkratce*, začíná „Podporované jsou tři .NET ORM a **překlad dotazů je úplný ve všech devíti směrech**…" — a týž odstavec o několik vět dál říká „je v šestici i MyBatis… matice směrů má šestatřicet polí včetně osmnácti napříč ekosystémy". Odstavec si tedy odporuje sám.
- [`traceability.md`](../traceability.md), řádek **S4**, dokládá druhou větu požadavku testem `Combined/ArtifactCarriesNoCredentialsTest` „ve všech devíti směrech". Ten test bere směry z `CrossFrameworkInputs.Directions`, tedy ze součinu výčtu `ORMEnum` se sebou samým — **šestatřiceti**. Doklad je tu tedy podhodnocený čtyřnásobně.

Že matice opravdu pokrývá šestatřicet směrů, drží `Combined/QueryMatrixTest`: obě jeho teorie běží nad `Directions` a tvrdí, že každý směr vydá neprázdný dotazový artefakt a že v žádném se dotaz neztratí mlčky.

**Jedna část té věty ale zastaralá není a oprava ji musí zachovat.** Jednotlivé *kategorie* dotazu se v šestatřiceti směrech neměří všechny: `Combined/SubQueryConditionTest`, `SetOperationQueryTest`, `PaginationQueryTest`, `DistinctQueryTest`, `InValueListTest` a `QueryTargetShapeTest` stojí dál nad třemi .NET buildery, kdežto `GroupedQueryTest` běží přes všechny směry a `QueryParameterTest` s `PaginationParameterTest` sahají i na javové cíle. Rozlišení „matice směrů je šestatřicetipolová, ale kategorie se měří na devíti .NET směrech plus tam, kde je to výslovně jinak" dnes nevysloví ani jedno z těch tří míst — a `traceability.md` u T2 ho vysloví („devět .NET směrů… Chybí javová strana"). Autoritativní §9 je tedy proti řádku T2 zároveň nadsazené i podhodnocené: nadsazuje počet frameworků a podhodnocuje šíři matice.

### 2.2 Velikost sady je vyslovená na pěti místech, ačkoli pravidlo zní „jen na jednom" — závažné

[`ORMConvertor/README.md`](../../ORMConvertor/README.md), část *How large the suite is, and what it covers*, o sobě říká: „**This is the only place that says how large the suite is.** Everything else refers here and does not repeat the number — two figures in two places drifted apart once already, and the only defence against that is not to write them twice."

Číslo 1442 .NET testů a 146 javových, z toho 97 integračních, přesto stojí ještě na čtyřech dalších místech: v [`architecture.md`](../architecture.md) §6.2 a §9, v [`traceability.md`](../traceability.md) u F13 a v sekci *Guarantees* kořenového [`README.md`](../../README.md). Ke dni revize **neplatí ani jedno z nich**: .NET sada má 1551 testů (kapitola 7). Jedna práce tak zneplatnila pět míst naráz, a pravidlo, které přesně tomu mělo předejít, je vyslovené v šestém.

Obhajoba, že jde o *datované záznamy o běhu*, a ne o tvrzení o dnešní velikosti, drží u §6.2 a padá u zbylých tří: sekce *Guarantees* i §9 uvádějí ta čísla jako doklad nároku, tedy jako stav, a řádek F13 v traceability také.

### 2.3 Datovaný záznam o ověření nerozliší dva běhy téhož dne — drobné

§6.2 začíná záznam slovy „**Obě sady proběhly 2026-09-21 v zafixovaném prostředí a jsou zelené**… .NET sada **1442** testů". Tahle revize provedla týž běh **téhož dne** nad jiným stromem a dostala 1551. Dva pravdivé záznamy o téže sadě, z téhož dne, s různými čísly se od sebe datem odlišit nedají.

Revize z 2026-08-23 narazila na tutéž třídu vady z druhé strany (nálezy 1.1 a 1.2): tehdy číslo předcházelo testům, které vydání obsahovalo, a spor se dal rozplést jedině přes commity. Levná obrana je jmenovat u záznamu vedle data i **commit**, nad kterým běh proběhl; `ORMConvertor/README.md` to u svých záznamů dělá opisem („po rozhodnutí 085"), což je táž informace vyslovená tak, že se nedá ověřit strojově.

### 2.4 Co při kontrole sedělo

Zapisujeme i to, aby kapitola nevypadala jako seznam vad, a hlavně aby bylo příště vidět, co už jednou ověřené bylo. Tabulka „Zafixované verze" souhlasí se všemi čtyřmi centrálními soubory, řádek po řádku. Osm koncových bodů v §6.5 odpovídá osmi mapováním v `Endpoints.cs`. Šablonová metoda v §7 sedí do písmene — sedm kroků `protected abstract` v uvedeném pořadí a `ReportStatedBaseTypes` mezi druhou a třetí fází, jak §7 popisuje. Deklaraci dialektu nese všech šest deskriptorů a všechny hodnotou `SqlServer2022`. Strop zanoření je uplatněný ve všech pěti rekurzivně čtených jazycích. Seznam projektů v `ORMConvertor.sln` i seznam, který si stupeň `dotnet-build` v `ORMConvertorAPI/Dockerfile` vypisuje po jménech, je úplný a shodný s adresářem (21 projektů) — past, o které mluví nález 7.6 revize z 2026-08-23, tentokrát nesklapla. **Ve 120 dokumentech `docs/` a v obou `README.md` není jediný rozbitý relativní odkaz.** Ve výčtech modelu není hodnota bez čtenáře.

---

## 3. Hranice záruk je vyslovená na třech místech

Tahle kapitola nesla kritické nálezy v obou předchozích revizích a nese je i teď, byť měkčí. Pořadí autorit je dané: sekce *Guarantees* kořenového [`README.md`](../../README.md) je kanonická, [`architecture.md`](../architecture.md) §9 nese totéž česky s odůvodněním a [`traceability.md`](../traceability.md) váže požadavek na důkaz.

### 3.1 `README.md` nenárokuje S7, ačkoli ho §9 i traceability nárokují — a přitom ho zužuje — závažné

§9 vyjmenovává nárokované požadavky a **S7 je mezi nimi**; `traceability.md` mu dává stav „nárokované v užším rozsahu". Seznam *Covered* v `README.md` jmenuje překlad, javové frameworky, cross-ecosystem, javovou sadu, diferenční ověření, vícesouborový vstup a výstup, doplnění z katalogu, diagnostiku, determinismus, nepřítomnost přihlašovacích údajů v artefaktu, výkon překladu, modulární architekturu a kontejnerovou konfiguraci — tedy F1–F6, F7–F10, F11–F14, S1–S6 —, ale **uživatelské rozhraní ani jeho použitelnost v něm nestojí**. Slovo *frontend* padne v README dvakrát, v popisu adresářů a v návodu ke spuštění; v sekci nároku ne.

Zvlášť je to vidět o pár odstavců níž, kde čtvrtá odrážka „narrower sense" **zužuje klientskou validaci** — tedy S7. README tak zužuje nárok, který nikde nevyslovil.

Je to týž nález, jaký měla revize z 2026-08-23 pod číslem 3.2 („README nenárokuje S1 a S3, které §9 i traceability nárokují"). S1 a S3 byly doplněny; S7 zůstalo. Rozdíl proti tehdejšku je, že dnes už README nese zúžení téhož požadavku, takže nesoulad je vidět uvnitř jedné sekce.

### 3.2 README popisuje překlad dotazů jako „nine directions between the three frameworks" — závažné

Druhá odrážka *What the tool does* zní „**Query translation in all nine directions** between the three frameworks (… the generated queries are EF Core LINQ, NHibernate HQL, and Dapper SQL …)". JPQL ani mapper MyBatisu tam nejsou. O dva odstavce níž sekce *Guarantees* prohlašuje Hibernate, EclipseLink a MyBatis za pokryté a mluví o překladu přes hranici ekosystémů **v obou směrech**.

Jde o rub nálezu 2.1 v dokumentu, který je pro nárok kanonický, a jeho adresátem je konzument nástroje, nikoli my. Čtenář, který sekci *Guarantees* věří, se v odrážce nad ní dočte, že dotazy se překládají mezi třemi frameworky.

### 3.3 „1.2.0 closes the first milestone … which is what everything above describes" — drobné

Věta v části *Versioning and releases* platila v den, kdy vznikla. „Everything above" dnes popisuje šest frameworků a dva ekosystémy, takže odkaz na „to, co je popsané výš" míří na jiný text, než pro který byl napsaný. Souvisí s 3.2 a opraví se s ním zároveň; sama o sobě nic netvrdí nepravdivě, jen ukazuje jinam.

### 3.4 U F13 se nenárokované zúžení nejmenuje — drobné

Požadavek F13 žádá „**konfigurovatelná** práce s numerickou přesností a null hodnotami". `traceability.md` u F13 popisuje stav přesně: „měřítko desetinných čísel a počet platných číslic nese matice a **null je holé `NULL`**". Měřítko tedy konfigurovatelné je, zacházení s null hodnotami ne — je to jedno pevné pravidlo vykreslení.

Ověřovací kritérium F13 (třicet dvojic se shodným výsledkem a odhalený záměrně chybný překlad) splněné je a nárok na něm stojí, takže nález nezpochybňuje status `nárokované`. Nesedí však praxe téhle sady: §9 u sedmi požadavků zúžení **jmenuje**, a tohle je osmé, které se nejmenuje nikde. Buď patří mezi ně, nebo se musí říct, proč pevné pravidlo pro null hodnoty zúžením není.

---

## 4. Rozhodnutí a jejich rejstřík

Rejstřík je v pořádku a kontrola byla strojová: **93 souborů, 93 řádků rejstříku**, žádný soubor bez řádku a žádný řádek bez souboru. Datum, stav i pole *Požadavky* souhlasí v rejstříku a v souboru u všech 93 (u 041 a 057 se liší jen zápis stavu, protože rejstřík z čísla nahrazujícího rozhodnutí dělá odkaz). Mezera v číslování — vynechané 038 — je vyslovená v hlavičce rejstříku. Všech pět rozhodnutí se stavem `revidováno` (078, 080, 084, 089, 092) má sekci *Historie* s datem a s tím, co se doplnilo.

### 4.1 Rozhodnutí 070 cituje nahrazené rozhodnutí 041 jako živé pravidlo — drobné

Rozhodnutí [070](../decisions/070-a-parser-refuses-what-would-change-the-row-set.md) (2026-09-14) uzavírá důsledky větou „**Podle rozhodnutí [041] je to PATCH**". Rozhodnutí 041 ale nahradilo rozhodnutí [069](../decisions/069-major-marks-a-milestone-not-a-break.md) už **2026-08-26**, tedy devatenáct dní předtím. Ověřeno pořadím commitů: 069 vstoupilo commitem `9f2f90e` v 14:56 onoho dne, 070 commitem `7d6b9cf` o devatenáct dní později.

Rozhodnutí 060–068 citují 041 také, ale u nich je to v pořádku — všechna vznikla **před** 069 (066, 067 a 068 téhož dne, v 10:07, 11:02 a 11:35) a snímky se zpětně nepřepisují. Závěr rozhodnutí 070 platí beze změny, protože kritérium PATCH přenáší 069 z 041 nezměněné; vadná je jen adresa. Oprava zpětně by porušila pravidlo o nepřepisování rozhodnutí, takže patří sem a do rejstříku, ne do souboru 070 — touž cestou, jakou [`audits/README.md`](README.md) eviduje dva starší audity editované po datu.

---

## 5. Mrtvý kód, deklarace bez čtenáře a velikost sdílených bází

### 5.1 `QueryBuilderException` nemá výrobce ani čtenáře — drobné

`ORMConvertor/Model/Exceptions/QueryBuilderException.cs` deklaruje veřejnou výjimku, která se v celém řešení — v produkčním kódu i v testech — nevyskytuje na žádném dalším řádku. Vznikla commitem `704984e` („Dapper SQL query builder, instruction rework"), tedy ještě před převzetím tvaru diagnostiky.

Není to jen nepoužitá deklarace, nýbrž deklarace, která **navádí proti platnému rozhodnutí**: výjimka z builderu je přesně ten kanál, který rozhodnutí [010](../decisions/010-diagnostics-as-returned-data.md) nahradilo vrácenými záznamy. Adresář `Model/Exceptions/` neobsahuje nic jiného, takže odstraněním zmizí celý.

### 5.2 `Advisor/ilp.c` dál končí devětačtyřiceti řádky zakomentovaného `main()` — drobné

Nález 6.1 revize z [2026-08-23](2026-08-23-post-release-1-1-0-audit.md) neodbavený; stav beze změny. Soubor patří do vyňaté oblasti 1, takže priorita je nízká, ale trvá: zakomentovaný `main()` je jediný obsah konce souboru, který se nikdy nepřeloží.

### 5.3 `Tests/xunit.runner.json` dál nekonfiguruje nic — drobné

Nález 6.4 téže revize, také neodbavený. Soubor obsahuje jediný klíč `$schema` a žádné nastavení.

### 5.4 Sdílená entitní báze vyrostla o čtvrtinu a rozšiřovací plocha se nepohnula — závažné

Tohle je odpověď na otázku, kterou položka revize kladla: **neohrožuje velikost sdílených bází invariant „nový framework je nový wrapper" (S1)?**

Struktura ho neohrožuje, a měřili jsme to třemi způsoby:

1. **Jméno frameworku do sdílených vrstev neprosakuje.** V projektech `AbstractWrappers`, `Common` a `Model` se mimo komentáře nevyskytuje ani jedno ze šesti jmen; `ORMEnum` se tam objevuje na dvou deklarovaných místech, v deskriptoru a v záznamu. `ConversionHandler` — celá orchestrace překladu — nejmenuje framework **ani jednou**. Jediná místa, která je jmenují, jsou čtyři továrny v `OrmConvertor/Factories/` (229 řádků dohromady), tedy registr, který §1 architektury výslovně popisuje.
2. **Nový wrapper je opravdu levný.** `HibernateWrappers` má 185 řádků, `EclipseLinkWrappers` 226 — proti 5 251 řádkům sdílené vrstvy `JakartaPersistence`, kterou oba dědí. Šestý framework (`MyBatisWrappers`, 2 761 řádků) je dražší právě proto, že pod sebou žádnou vrstvu frameworku nemá, jak rozhodnutí [084](../decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md) říká dopředu.
3. **Růst sdílené vrstvy nepřinesla jména frameworků.** Od `1.2.0` sáhlo do `AbstractWrappers` 18 ze 45 commitů (+1 780 řádků), ale to, co přibylo, je schopnost sdílená všemi šesti — strop zanoření, `XmlSource`, `BoundParameters`, dvojrole jednotky, vyslovený bázový typ.

Nález je jinde a je to čísel dvojice. Od značky `1.2.0` do dneška:

| | `1.2.0` | dnes | rozdíl |
|---|---|---|---|
| `AbstractEntityBuilder` řádků | 2 137 | 2 728 | **+27,7 %** |
| `AbstractEntityBuilder` `virtual`/`abstract` členů | 9 | 9 | **beze změny** |
| `AbstractQueryBuilder` řádků | 651 | 1 475 | +126,6 % |
| `AbstractQueryBuilder` `virtual`/`abstract` členů | 12 | 16 | +4 |

Dotazová báze vyrostla víc, ale rozšiřovací plocha jí rostla s sebou. **Entitní báze vyrostla o čtvrtinu a nabídla za to nulu nových háčků.** Sedm z těch devíti členů jsou kroky šablonové metody, jejichž počet je pevný záměrně (§7), takže volných zásuvných bodů je fakticky dva. Framework, jehož potřeba se do nich netrefí, nemá kam jít jinam než do těla báze — a právě tak do ní za posledních pět dní přibylo přes pět set řádků.

Invariant tedy dnes **platí** a nález není o jeho porušení; je o tom, že poměr mezi tělem a plochou se posunul směrem, který ho porušit umožní, a že to nikdo neměří. Jde o dlouhý horizont, ne o překážku vydání.

### 5.5 Solution Items nese jeden ze tří Dockerfilů — drobné

`ORMConvertor.sln` má ve složce *Solution Items* `database.Dockerfile`, ale ne `ORMConvertorAPI/Dockerfile` — ten přitom nese pět stupňů včetně obou testovacích a seznamu projektů, který obraz už dvakrát rozbil. Je to táž past, na kterou upozorňuje `CLAUDE.md` u glob vzoru `Dockerfile*`, jen v jiném souboru. Tři centrální soubory, které revizi z 2026-08-23 ve složce chyběly (nález 7.6), doplněné jsou.

---

## 6. Co k tomu říká vnější praxe

Šestá kapitola má tentokrát doslovné měřítko, protože artefaktem za článkem je tenhle repozitář sám: kritéria hodnocení výzkumných artefaktů — **dokumentovanost, konzistence s článkem, úplnost a spustitelnost** — se na něj vztahují přímo. Spustitelnost jsme neodhadovali, nýbrž vyzkoušeli (kapitola 7).

### 6.1 Pokrytí testy: nikde neuvedené to není, jen zastaralé — oprava předpokladu

Položka revize v `open-items.md` žádá doplnit „číslo pokrytí testy, které CI sbírá (`XPlat Code Coverage`) a které nikde neuvádíme". **Předpoklad neplatí:** `ORMConvertor/README.md` ho uvádí — 77,5 % řádků a 66,2 % větví — jen to je měření z 2026-09-17 nad sadou o 881 testech, tedy nad 57 % dnešní sady. Vada tedy není mlčení, nýbrž stáří.

Revize pokrytí přeměřila (Release, LocalDB, `--collect:"XPlat Code Coverage"`, 1 551 testů):

| | řádky | větve |
|---|---|---|
| **celkem** | **80,0 %** (12 650 / 15 801) | **69,1 %** (8 047 / 11 642) |

Po projektech, od nejvyššího: `SampleData` a `EclipseLinkWrappers` 100 %, `CSharpEntityParsing` 99,1 %, `DapperWrappers` 97,6 %, `HibernateWrappers` 97,0 %, `OrmConvertor` 95,8 %, `Model` 94,2 %, `DatabaseCatalog` 90,2 %, `NHibernateWrappers` 89,3 %, `AbstractWrappers` 89,2 %, `EFCoreWrappers` 87,1 %, `Common` 83,2 %, `TransactSql` 81,3 %, `JakartaPersistence` 80,3 %, `MyBatisWrappers` 78,5 %, `JavaEntityParsing` 75,8 %, `LinqParsing` 74,6 %, `ORMConvertorAPI` 45,1 % — a **`Advisor` i `AdvisorBenchmarking` 0,0 %**.

Dvě čísla stojí za vyslovení, protože se o ně opírají věty jinde. **Nula u obou advisorových projektů** dělá z vyjmutí oblasti 1 měřený fakt, ne tvrzení ze čtení kódu — a je to nula i na řádcích, i na větvích, i po přidání 670 testů od posledního měření. A **`MyBatisWrappers` má 53,7 % větví**, nejméně ze všech překladových projektů; je to nejmladší wrapper a jediný, který stojí na dvou jazykových vrstvách místo na vrstvě frameworku.

### 6.2 Pět z dvaceti pěti pravidel článku nemá v repozitáři jedinou zmínku — drobné

Rozhodnutí i `architecture.md` argumentují překladovými pravidly článku (E1–E10, Q1–Q15) jménem. Napříč celým `docs/` se ale nikde nevyskytují **E6, E7, Q6, Q7 a Q9**: mapování vztahu, určení vlastnící strany, definice joinu, join odvozený ze vztahu a filtrace po agregaci.

Všech pět je přitom implementovaných — E7 je doslova invariant `Role` (`Owning`/`Inverse`), Q9 je `HavingInstruction`. Nález je o dohledatelnosti: čtenář, který drží článek vedle repozitáře, nemá jak ověřit, že pět z pětadvaceti pravidel nezůstalo opomenutých, protože o nich repozitář nemluví ani jako o splněných, ani jako o vyňatých. Konzistence s článkem je jedno ze čtyř kritérií téhle kapitoly a zrovna u ní se odpověď dohledává hůř než u zbylých tří.

### 6.3 Kořenový README radí `run` bez `build` — drobné

Část *Getting started* kořenového [`README.md`](../../README.md) zní: „`docker compose --profile test run --rm tests` runs the whole test suite without a .NET SDK or a database of your own."

Tenhle tvar je **bez kroku `build`**, tedy přesně ten, o kterém `ORMConvertor/README.md` na třech místech píše, že dvakrát tiše vydal výsledek staršího stromu. Na čerstvém klonu je věta pravdivá, protože `run` obraz poprvé postaví; při druhém čtení téhož návodu nad změněným stromem už pravdivá není. Kořenový README je přitom první a často jediný dokument, který cizí ruce otevřou — tedy ten, na kterém se kritérium spustitelnosti měří.

### 6.4 Trvalý identifikátor vydání dál chybí — evidováno, beze změny

Ze čtyř doporučení fair-software.eu má repozitář čtyři (veřejný repozitář, licence, citovatelnost přes `CITATION.cff`, kontrolní seznam kvality) a pátý — záznam v registru — nemá. Stav se od revize z 2026-08-23 (nález 8.1) nezměnil a nese ho vlastní položka v `open-items.md`, která výslovně říká, že se na to sahá až docela nakonec. Připomínáme to tu jen proto, že vydání `2.0.0` je nejbližší příležitost, kdy by identifikátor vznikal.

---

## 7. Co bylo ověřeno spuštěním

Tahle kapitola je v žánru nová: žádná předchozí revize build nespustila a ta z 2026-08-23 to o sobě v kapitole 9 výslovně říká. Všechno níž proběhlo 2026-09-21 na MIS3 nad commitem `36d688d`.

**Obě sady v zafixovaném prostředí, každá po dvojici `build` a `run`:**

| Sada | Prostředí | Výsledek |
|---|---|---|
| .NET (`tests`) | profil `test` v compose, SQL Server 2022 | **1 551 prošlých, 0 selhaných, 0 přeskočených** |
| .NET (`tests`) | hostitel, Release, LocalDB | **1 551 prošlých, 0 selhaných, 0 přeskočených** |
| Java (`java_tests`) | profil `test`, obraz `maven:3.9.11-eclipse-temurin-25-noble`, SQL Server 2022 (`Database version: 16.0`) | **146 prošlých, 0 selhaných, 0 přeskočených** |
| Java, jen integrační (`-Dgroups=integration`) | totéž | **97 prošlých, 0 selhaných, 0 přeskočených** |

Shoda kontejnerového a hostitelského čísla je ta část, kvůli které se obě uvádějí: obraz měří strom, ze kterého je postavený, a ne starší revizi.

**Běh systému** (`docker compose up --build`, aplikace plus SQL Server se vzorovou databází):

- statický frontend na `/orm/` odpovídá **200**;
- dokument OpenAPI na `/orm/openapi/v1.json` se vydává a nese `"version": "1.2.0"`, tedy číslo ze sestavení, jak §6.5 slibuje;
- `/convert` z EF Core do Hibernate vrátil **200**, tři artefakty (javová entita, javová metoda, JPQL) a deset záznamů; odpověď nese `targetDatabaseDialect`, `declaredSourceDialect` i `maxNestingDepth` (128), tedy všechna tři pole, která §6.5 u tohoto bodu popisuje;
- **doplnění z katalogu proběhlo proti vzorové databázi** — typy a unikátní omezení z `Sales.Customers` dorazily se záznamy a tři konflikty se zdrojem jsou vyřešené ve prospěch zdroje se záznamem, přesně jak popisuje §5.2; čtení katalogu si běh změřil sám (`catalogReadMilliseconds` 41,98);
- **`libadvisor.so` se v obrazu staví a P/Invoke funguje**: `/advisor-test` s malou instancí vrátil `{"status":0,"objective":2,"selected":[1,1],"assignment":[0,1]}`. Advisor zůstává vyňatou oblastí 1, ale jeho nativní polovina je tímhle během doložená;
- požadavek s vadným JSON tělem vrací prázdnou čtyřistovku z rámce, ne `ProblemDetails` — přesně jak §6.5 předem říká.

**Nový výstup, který běh potvrdil.** Vzorový dotaz EF Core nese od téhož dne zpátky filtr podle data a do JPQL vyšel jako `{ts '2025-01-01 00:00:00'}`, tedy v JDBC escape tvaru, pro který je definovaná jedině plná podoba s časem dne.

**Co se nespustilo:** `/advisor/run`, tedy překlad, kompilace a měření cizího kódu. Je to vyňatá oblast bez jediného testu a revize do ní nesahala dál než po ověření, že se nativní knihovna zavede.

---

## 8. Revize celého repozitáře z 2026-09-21, která nemá vlastní soubor

Kategorie *Zbytky* v [`open-items.md`](../open-items.md) se odvolává na „revizi celého repozitáře z 2026-09-21", a ta v tomhle adresáři soubor nemá — nálezy z ní byly rozepsané rovnou do položek. Zapisujeme ji sem, aby ten název měl kam mířit; **tahle revize ji nenahrazuje** a nemůže, protože měří pozdější strom a ptá se na něco jiného.

Co ta revize vynesla, je dnes všechno odbavené a poznat se to dá po stopách v kódu a v `architecture.md` §5: sdílená čtečka T-SQL odmítá příkaz stojící vedle překládaného `SELECT`u, klauzuli `WITH`, `INTO`, `FOR XML`/`FOR JSON` a `TABLESAMPLE` a hlásí nápovědy ztrátou; sdílený LINQ parser jmenuje kroky, které mění množinu řádků, a čte zpět `g.Key`; T-SQL visitor vypisuje `COUNT(*)` bez aliasu. Ze čtyř otázek, které po opravách zůstaly, odbavila tři rozhodnutí [092](../decisions/092-input-nesting-depth-capped-before-the-descent.md), [093](../decisions/093-unreadable-input-is-a-unit-failure.md) a [094](../decisions/094-entity-identity-inside-a-conversion.md), čtvrtá — syntaktická chyba v C# bez pozice — leží dál ve *Zbytcích*.

---

## 9. Co z auditu plyne

Rozdělení je podle rozhodnutí [007](../decisions/007-documentation-structure.md): oprava do kódu a do `architecture.md`, věc k rozmyšlení do `decisions/`, zbytek do `open-items.md`.

### Opravy

Všechny se vejdou před vydání a žádná z nich nevolí mezi možnostmi:

1. **Doplnit scénáře pro F7–F10, F12 a F13** do `use-cases.md` (nález 1.1). Dokument je živý, ne zmražený, a bez toho vydání zavírá šest požadavků, o kterých vrstva nad požadavky mlčí.
2. **Opravit `abstract` a `keywords` v `CITATION.cff`** tak, aby jmenovaly oba ekosystémy (1.2). Patří to do téhož commitu, který při vydání posune `version` a `date-released`.
3. **Přepsat „tři .NET ORM" a „devět směrů"** v `architecture.md` §5 a §9 a v řádku S4 `traceability.md` (2.1) — se zachovaným rozlišením, které kategorie se měří kde.
4. **Doplnit S7 do seznamu *Covered*** v kořenovém `README.md` a opravit tamtéž odrážku o překladu dotazů a větu o `1.2.0` (3.1, 3.2, 3.3).
5. **Jmenovat u F13 zúžení o null hodnotách**, nebo říct, proč zúžením není (3.4).
6. **Smazat `QueryBuilderException`** i s prázdným adresářem `Model/Exceptions/` (5.1).
7. **Doplnit `ORMConvertorAPI/Dockerfile` do Solution Items** (5.5) a **přidat `build` do věty v kořenovém README** (6.3).
8. **Přeměřit a přepsat pokrytí** v `ORMConvertor/README.md` na čísla z kapitoly 6.1, a s ním i velikost sady na 1 551 / 146 / 97 (6.1, 2.2).
9. **Zaznamenat u rozhodnutí 070 chybnou citaci** — v rejstříku rozhodnutí nebo v `audits/README.md`, ne v souboru 070 (4.1).

### Potřebná rozhodnutí

1. **Kde bydlí velikost sady, a co s tím, že ji pět míst opisuje** (2.2, 2.3). Volba je mezi „záznam o běhu smí číslo nést, ale musí u něj jmenovat commit" a „číslo je jen v jednom dokumentu a ostatní na něj odkazují". Obě jsou obhajitelné a obě mění víc než jeden soubor, takže jde o volbu, ne o opravu. Souvisí s tím i otázka, jestli poznámky k vydání smějí číslo sady nést vůbec — značka se neposouvá, takže co do anotace jednou půjde, zastará tam natrvalo.
2. **Jestli se pravidla článku mají vázat na kód jmenovitě** (6.2). Doplnit pět chybějících zmínek je oprava; zavést mapování pravidlo → místo v repozitáři je volba o tvaru dokumentace, která se dotkne `traceability.md` i `architecture.md`.

### Delší horizont

1. **Poměr mezi tělem a rozšiřovací plochou `AbstractEntityBuilder`** (5.4). Invariant S1 dnes platí, ale entitní báze roste bez nových háčků. Hlídat se to dá levně — dvě čísla u vydání —, opravovat draze.
2. **Neodbavené drobnosti z revize 2026-08-23**: zakomentovaný `main()` v `Advisor/ilp.c` (5.2) a prázdný `Tests/xunit.runner.json` (5.3).
3. **Trvalý identifikátor vydání** (6.4) — evidováno, beze změny, s vlastní položkou.
