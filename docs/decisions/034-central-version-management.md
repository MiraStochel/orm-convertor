# 034 — Centrální správa verzí

Datum: 2026-08-20
Stav: platí
Požadavky: S2, S6
Podklad: audit 2026-08-02, kap. 3.4.1; rozhodnutí [003](003-one-shot-migration.md), [013](013-target-framework-versions.md), [016](016-generated-artifact-verification-levels.md) a [030](030-scope-of-version-1-0.md); `.csproj` všech patnácti projektů `ORMConvertor.sln`; `.github/workflows/ormconvertor-tests.yml`; `ORMConvertorAPI/Dockerfile`

## Kontext

Rozhodnutí [013](013-target-framework-versions.md) zafixovalo množinu verzí a dalo jí kanonické místo: tabulku v `architecture.md` §1. Ta tabulka není evidence, je to tvrzení. Všechno, co v dokumentaci i v `docs/analysis/` píšeme o chování frameworků, platí proti ní, a odvolává se na ni i požadavek S2, který žádá shodný výstup „při stejném vstupu, konfiguraci, schématu a **verzi** nástroje". Tvrzení se ale nedrží samo: verze, proti kterým se doopravdy obnovuje, staví a testuje, jsou napsané jinde — v patnácti souborech `.csproj`, každá ručně, a nic je s tabulkou nespojuje. `architecture.md` §6.2 to o jednom z těch míst říká rovnou: shoda `Tests.csproj` s tabulkou „se hlídá ručně".

**Co je dnes kde.** V `ORMConvertor.sln` je patnáct projektů. Všech patnáct opakuje týž třířádkový blok `TargetFramework`/`ImplicitUsings`/`Nullable`, tedy i řádek „.NET 10" z tabulky, patnáctkrát. Balíčkových referencí je dvacet pět a jmen balíčků šestnáct, takže devět zápisů verze je duplicitních:

| Balíček | Verze | Kolik projektů |
|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` | 5.6.0 | 6 (`Common`, `CSharpEntityParsing`, `LinqParsing`, `DapperWrappers`, `EFCoreWrappers`, `NHibernateWrappers`) |
| `Microsoft.Data.SqlClient` | 7.0.2 | 3 (`DatabaseCatalog`, `AdvisorBenchmarking`, `Tests`) |
| `Dapper` | 2.1.79 | 2 (`AdvisorBenchmarking`, `Tests`) |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.10 | 2 (`AdvisorBenchmarking`, `Tests`) |

K tomu číslo `10.0.10` samo: stojí u sedmi různých balíčků řady .NET 10 (EF Core, EF Core SqlServer, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.AspNetCore.OpenApi` a tři balíčky `Microsoft.Extensions.Configuration`) v osmi zápisech napříč třemi projekty. Zvednout řadu znamená dnes najít a přepsat osm míst a doufat, že se žádné nepřehlédlo — přičemž rozejít se mohou i mezi sebou, nejen s tabulkou.

**Co už vázané je, a jak úzce.** Test `DeclaredVersionsMatchTheVerificationPackages` (rozhodnutí [016](016-generated-artifact-verification-levels.md)) porovnává verzi v deskriptoru s informační verzí sestavení, které ověřovací úroveň skutečně načte. To je silná vazba, ale vede jinam, než by tahle položka potřebovala: váže **deskriptor na `Tests.csproj`**, nikoli `Tests.csproj` na tabulku, a týká se tří jmen z šestnácti. Pokrývá právě ta tři, na kterých stojí tvrzení vydávaná za běhu — záznam běhu podle S6 bere verze z deskriptorů (§5.1) —, takže o Dapperu, NHibernate a EF Core nástroj lhát neumí. O zbylých třinácti mlčí, a mlčí i o tom, jestli číslo v deskriptoru odpovídá tomu, co tabulka slibuje čtenáři dokumentace.

**Chybějící `global.json` je totéž o stupeň výš.** Verze SDK není napsaná nikde v repozitáři: CI si instaluje `10.0.x`, Dockerfile staví z obrazu `mcr.microsoft.com/dotnet/sdk:10.0` a na vývojovém stroji rozhoduje to, co tam kdy nainstaloval Visual Studio. Tři místa, tři formulace, žádná vazba. Přímo na generovaný text SDK nesahá — artefakty skládá náš vlastní kód a kompiluje je Roslyn z balíčku, ne z SDK —, ale netýká se nás jen okrajově: ověřovací harness podle `architecture.md` §6.2 zrcadlí konzumentský projekt tím, že do kompilace dodává „implicitní usings SDK". Ta věta je tvrzení o SDK, a dokud žádné SDK deklarované není, není proti čemu ho číst.

**Proč to není kosmetika.** Rozhodnutí [030](030-scope-of-version-1-0.md) uzavřelo, že verze 1.0 nesmí být slepencem jednotlivě omluvených polotovarů a že nároky se vyslovují nahlas. Verze 1.0 nárokuje S2 i S6. Determinismus je přitom jediný nárok v seznamu, který se nedá ukázat na jednom běhu — pozná se až tím, že se dva běhy nebo dva stroje rozejdou, a nejlevnější způsob, jak se rozejdou, je ruční úprava jednoho z patnácti souborů. Rozhodnutí 013 ostatně vzniklo mimo jiné proto, že článek cituje javové frameworky jen odkazem na dokumentaci a Hibernate dokonce bez čísla; repozitář, který čísla má, ale nechává je rozeseté a nesvázané, je proti tomu jen o krok dál.

## Zvažované varianty

1. **Nechat stav a hlídat kázní.** Tohle je varianta, kterou máme, a audit 2026-08-02 v kapitole 3.4.1 ji označil za nález. Selhává tiše a selhává právě ve chvíli, kdy na tabulce nejvíc záleží — při povýšení verze, tedy v jediném okamžiku, kdy se ta čísla vůbec přepisují. Navíc dělá z S2 nárok, který nemá kdo ověřit: „stejná verze nástroje" je předpoklad, o kterém repozitář nedokáže říct, jestli platil. Zamítáme.

2. **Test, který přečte `.csproj` soubory a porovná je s tabulkou v `architecture.md`.** Je to nejlevnější věc, která umí selhat nahlas, a proto ji nezamítáme jako nesmysl. Zamítáme ji jako řešení: duplicitu neodstraňuje, nýbrž ji zavádí do inventáře — verze zůstane napsaná na n místech a přibude n+1. místo, které o nich mluví. Test parsující build soubory je navíc druhý, horší build systém: musel by rozumět tomu, co MSBuild dědí, co je podmíněné a co je tranzitivní, a jeho selhání by se hlásilo v jazyce, který k opravě nevede. A na nový projekt bez verze nedosáhne vůbec — ten mu prostě přibude.

3. **Vlastní `Directory.Build.props` s verzemi jako MSBuild vlastnostmi** (`$(RoslynVersion)` a podobně, dosazované do atributů `Version`). Sjednocení by fungovalo, ale je to ruční nápodoba toho, co NuGet umí sám, a s horším okolím: číslo schované za vlastností je neviditelné pro `dotnet list package --outdated`, pro automatické hlídače závislostí i pro správce balíčků v IDE. Vyměnili bychom ruční rozsev za ruční mechanismus, který navíc oslepí nástroje, jež by na rozejití uměly upozornit dřív než my. Zamítáme.

4. **Centrální správa balíčků NuGetu (`Directory.Packages.props`), `global.json` a `Directory.Build.props` pro to, co balíček není.**

## Rozhodnutí

**Volíme variantu 4.** V adresáři řešení `ORMConvertor/` — ne v kořeni repozitáře — vzniknou tři soubory a patnáct `.csproj` se o jejich obsah zkrátí. Provede se to jedním přepisem, ne postupně (rozhodnutí [003](003-one-shot-migration.md)).

**`Directory.Packages.props`** zapne centrální správu (`ManagePackageVersionsCentrally`) a ponese `PackageVersion` pro všech šestnáct jmen balíčků. Z projektů zmizí atribut `Version`; ostatní metadata reference zůstávají tam, kde jsou — `PrivateAssets` a `IncludeAssets` u `xunit.runner.visualstudio` popisují, jak projekt balíček používá, což je fakt projektu, ne verze. Zapneme přitom i **zákaz lokálního přebití** (`CentralPackageVersionOverrideEnabled` na `false`): bez něj by projekt směl napsat `VersionOverride` a rozejít se stejně tiše jako dnes, jen v novém zápisu. Teprve tenhle přepínač dělá ze sjednocení mechanismus místo nabídky.

**Tranzitivní připínání (`CentralPackageTransitivePinning`) zapínat nebudeme.** Vypadá jako zesílení téhož, ale je to obrácené tvrzení: soubor by začal určovat verze balíčků, které jsme nikdy nezvolili a o kterých nic netvrdíme, a chybné číslo by v něm byla přesně ta lež, které se tímhle rozhodnutím bráníme. Tabulka §1 jmenuje jen to, co referencujeme přímo, a centrální soubor zůstane v témž rozsahu.

**`global.json`** zafixuje pásmo SDK na `10.0.100` s `rollForward: latestFeature`. Znamená to „některé SDK řady .NET 10", tedy přesně to, co dnes nezávisle na sobě říkají CI (`10.0.x`) i Dockerfile (`sdk:10.0`), nově ale napsané v repozitáři a platné i pro lokální překlad. Přesnou záplatu nepřipínáme záměrně: byl by to slib reprodukovatelnosti, který neudržíme — první stroj bez té konkrétní záplaty by překlad odmítl —, a na emitovaný text SDK stejně nesahá. Připínáme tu úroveň, o které tabulka §1 mluví, a nic nad ni.

**`Directory.Build.props`** převezme trojici, kterou dnes opisuje všech patnáct projektů: `TargetFramework`, `ImplicitUsings` a `Nullable`. `TargetFramework` je řádek „.NET 10" z tabulky a patří sem ze stejného důvodu jako verze balíčků; zbylé dva do „verzí" nespadají, ale stojí v témž bloku a rozdělit hoisting na dvě etapy by znamenalo sáhnout do patnácti souborů dvakrát. Vlastnosti, které popisují jednotlivý projekt — `OutputType`, `RootNamespace`, `UserSecretsId`, `AllowUnsafeBlocks` —, zůstávají u něj. Tenhle soubor je zároveň místem, kam později přijde číslo verze nástroje, jak s tím počítá položka o značce vydání.

**Kanonické místo se nemění a nezdvojuje.** Tabulka v `architecture.md` §1 zůstává tím, čím ji určilo rozhodnutí 013: vysloveným tvrzením, proti čemu platí všechno, co o frameworcích píšeme. Má sedmnáct řádků a devět z nich centrální správa balíčků strukturálně nedosáhne — JDK, Jakarta Persistence, Hibernate, EclipseLink, MyBatis, `mssql-jdbc`, SQL Server a dvě vendorované knihovny ve `wwwroot/vendor/`. Řádek „.NET 10" přebírá `Directory.Build.props` a `global.json`; zbylých sedm řádků jmenuje osm balíčků, kdežto centrální soubor jich ponese šestnáct. `Directory.Packages.props` je proto strojově čitelná polovina téhož tvrzení, ne jeho konkurent: **.NET řádky tabulky se od zavedení čtou jako záznam toho, co je v centrálním souboru**. Asymetrie osmi a šestnácti je záměrná — tabulka mluví o balíčcích, o jejichž chování něco tvrdíme, centrální soubor o všem, co se obnovuje.

**Vazbu tabulky na soubor nedoplňujeme dalším testem.** Tvrzení vydávaná za běhu jsou svázaná už dnes: deskriptor nese verzi, záznam běhu ji z něj bere (S6) a `DeclaredVersionsMatchTheVerificationPackages` ho drží u toho, co ověřovací úroveň načte. Zbylé řádky — Roslyn, `Microsoft.Data.SqlClient`, ScriptDom, xUnit — jsou infrastruktura, o jejímž chování nástroj uživateli nic netvrdí; u ScriptDomu je nosné tvrzení stejně jinde, totiž explicitní volba gramatiky `TSql160Parser` v kódu. Přidat na ně test by znamenalo vrátit se k variantě 2, jen s jedním souborem místo patnácti. Cena rozejití po zavedení je jedna přehlédnutá řádka ve dvou souborech vedle sebe, ne v patnácti.

## Důsledky

**Povýšení verze se stane jednořádkovou úpravou.** Řada .NET 10 se dnes zvedá na osmi místech, Roslyn na šesti; po zavedení na jednom. Právě proto je tahle položka v pořadí verze 1.0 na patnáctém místě, tedy před posledními čtyřmi: chrání tabulku, o kterou se opírá S2, a čím dřív platí, tím míň ručních přepisů ji ještě může porušit.

**Restore přestane fungovat bez těch souborů.** Projekt bez atributu `Version` a bez `TargetFramework` sám o sobě neobnoví ani nepřeloží. Pro `dotnet` spouštěné z `ORMConvertor/` — což je způsob, jaký popisuje `CLAUDE.md` i CI — se nic nemění, oba soubory leží o adresář výš než projekty a MSBuild je najde. Dvě místa to ale zasahuje:

- **Dockerfile.** Fáze `dotnet-build` kopíruje nejdřív jen `.sln` a `.csproj` soubory, aby se cache obnovy nerozbila každou úpravou zdrojáku, a teprve pak zbytek. Do téhle první kopie musí přibýt `Directory.Packages.props` a `Directory.Build.props`, jinak obnova v ní selže. Docker je ze záruk verze 1.0 vyňatý (rozhodnutí [030](030-scope-of-version-1-0.md)) a nasazení řešíme ruční instancí, ale nechat v repozitáři soubor, o kterém víme, že po tomhle zásahu selže, by bylo tiché zhoršení — ty dvě řádky patří k této práci.
- **Překlad z kořene repozitáře.** `global.json` v `ORMConvertor/` se uplatní podle pracovního adresáře, ne podle umístění řešení: kdo spustí `dotnet build ORMConvertor/ORMConvertor.sln` z kořene, dostane SDK podle stroje. Umístění do kořene by tenhle případ pokrylo, ale zasáhlo by i `benchmarks/`, což nechceme (níže). Rozdíl proto přiznáváme a příkazy v `CLAUDE.md` i v CI zůstávají spouštěné z `ORMConvertor/`.

**`benchmarks/ORMComparison.sln` zůstává nedotčené.** Je to samostatné řešení sourozenecké k `ORMConvertor/`, ze záruk verze 1.0 vyňaté vcelku, a jeho verze se od našich vědomě liší — NHibernate a EF6 v něm jedou na `System.Data.SqlClient` 4.9.1, což audit 2026-08-02 v kapitole 3.4.2 vede jako samostatnou otevřenou položku. Umístit centrální soubory do kořene repozitáře by je tiše natáhlo i na ně a buď by tam obnovu rozbilo, nebo, hůř, změnilo to, co benchmarky měří. Adresář řešení není v tomhle rozhodnutí administrativní detail, ale hranice.

**Tranzitivní rozejití zůstává mimo dosah.** Centrální správa řídí to, co referencujeme přímo. Že si `Microsoft.EntityFrameworkCore.SqlServer` přitáhne vlastní `Microsoft.Data.SqlClient` a NuGet pak sjednotí verzi podle svých pravidel, tímhle rozhodnutím ošetřené není a být nemá — vypsat tranzitivní uzávěr do centrálního souboru je právě to, co jsme výše zamítli. Kde na konkrétní tranzitivní verzi záleží, patří přímá reference, a ta pak spadá pod centrální správu jako každá jiná.

**Deskriptory a záznam běhu se nemění.** Verze cílových frameworků se za běhu dál vydávají z `TargetFrameworkDescriptor.Version` (rozhodnutí 013) a jejich vazba na skutečně načtené balíčky drží dosavadním testem. Tohle rozhodnutí sahá na to, odkud se verze berou při obnově, ne na to, co nástroj o verzích tvrdí uživateli; obě cesty se potkávají v `Tests.csproj`, který napříště žádné číslo nenese, takže se test ptá centrálního souboru přes sestavení, která se doopravdy načetla.

**`architecture.md` §1 dostane s implementací větu o tom, odkud se .NET řádky tabulky berou**, a §6.2 přestane hlídat shodu ručně, protože nebude co s čím srovnávat: `Tests.csproj` už verzi nebude psát. Do té doby platí dosavadní znění.
