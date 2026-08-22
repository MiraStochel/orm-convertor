# 039 — Kontejnerová konfigurace prostředí

Datum: 2026-08-22
Stav: platí
Požadavky: F4, F6, S2, S4, S5, S6
Podklad: rozhodnutí [016](016-generated-artifact-verification-levels.md), [029](029-database-connection-is-the-consumer-projects-fact.md) a [030](030-scope-of-version-1-0.md); `docker-compose.yml`, `ORMConvertorAPI/Dockerfile` a workflow v `.github` ke dni rozhodnutí

## Kontext

Požadavek S5 zní: *celý systém, databáze, .NET a Java testovací projekty a experimentální pipeline spustitelné dokumentovanou kontejnerovou konfigurací; čisté prostředí reprodukuje testy jedním hlavním příkazem.* Jsou v té větě dvě samostatná tvrzení a je snadné splnit první a druhé přehlédnout. *Popsat prostředí kontejnerem* je jedna věc; *reprodukovat testy v čistém prostředí jedním příkazem* druhá. Čisté prostředí je takové, kde není předinstalované nic — ani SQL Server, ani .NET SDK.

Rozhodnutí 016 zvolilo pro testovací databázi lokální instanci, jejíž schéma si testy vytvářejí samy, a S5 tím vědomě odložilo. Odklad ale dostal podmínku: *„Právě proto je podstatné, že hostitele určuje konfigurace: podmínka S5 se pak dohání přidáním služby a proměnné prostředí, ne návratem k tomuto rozhodnutí."* Tohle rozhodnutí tu podmínku proplácí a na 016 nic nemění — připojovací řetězec se dál čte z konfigurace a testy dál nevědí, kdo databázi spustil.

Stav, ze kterého se vychází, má tři místa a každé z nich něco netvrdí:

1. **`docker-compose.yml` popisuje prostředí pro aplikaci, ne pro testy.** Staví aplikaci a SQL Server s WideWorldImporters, ale `ConnectionStrings__AdvisorDatabase` je v něm commitnutá deklarace, kterou dosud nikdo nespustil. `ConnectionStrings__CatalogDatabase` v něm chybí úplně, takže v kontejneru se fáze doplnění z katalogu nikdy nespustí a kritéria F4 a F6 přes rozhraní neplatí ani tam, kde je databáze na dosah ruky ve vedlejší službě.
2. **Workflow v `.github` spouští `dotnet test` bez databáze.** Databázově závislé testy se v něm přeskočí; volání `SkipIfUnavailable()` je dnes šestadvacet, napříč čtečkou katalogu, kontrolou fixture samotné a ověřením se zdrojem v Dapperu.
3. **Testovací projekt databázi nezakládá.** `TestSchemaFixture` vytváří *schéma* v databázi, kterou mu určí připojovací řetězec; samotná databáze je jeho předpoklad, ne výsledek.

A je tu past, kterou pojmenovalo už rozhodnutí 016 a která je v CI ostřejší než lokálně: **přeskočení je samo tvrzením o pokrytí.** Test, který se tiše nespustil, vypadá v souhrnu skoro jako test, který prošel. Dokud databáze v CI nebyla, byla to přiznaná daň — rozhodnutí 030 ji vyslovilo podmíněností kritérií F4 a F6. Jakmile ale konfigurace databázi slibuje, mění se povaha té daně: zelený běh, ve kterém se všechno databázové přeskočilo, netvrdí „databázi nemáme", nýbrž „máme ji a je to v pořádku" — a to je nepravda, kterou nikdo neuvidí. Postavit databázi do CI a nechat přitom přeskakování beze změny by tedy podmíněnost F4 a F6 neodstranilo, jen schovalo.

Rozhodnout je proto třeba čtvero: čím se prostředí popíše, kde probíhá testovací běh, odkud v něm testy vezmou databázi a co se stane, když databáze chybí tam, kde ji konfigurace slíbila.

## Zvažované varianty

### Čím se prostředí popíše

**A — Testcontainers.** Knihovna, kterou si testovací kód sám vyzvedne image, spustí kontejner a připojovací řetězec z něj přečte. Láká tím, že jediným příkazem zůstane `dotnet test` a že se workflow nemusí měnit vůbec. Mluví proti ní trojí. Vrací volbu hostitele do testovacího kódu, odkud ji rozhodnutí 016 záměrně vyňalo a na čem postavilo odklad S5; přechod na Testcontainers je přesně ten „přepis testů", kterému se ta věta chtěla vyhnout, protože z fixture udělá vedle čtečky konfigurace ještě správce životního cyklu kontejneru. Dál splňuje jen půl S5: databázi kontejnerizuje, testovací běh ne — `dotnet test` pořád potřebuje .NET SDK na hostiteli, takže na „čisté prostředí" nedosáhne a „celý systém" se stejně musí popsat druhou konfigurací pro aplikaci; jeden požadavek by pak nesly dva nezávislé mechanismy. A konečně sílu, kterou Testcontainers mají — kontejner na míru každé testovací třídě a paralelní běhy nad izolovanými instancemi —, tenhle projekt nepotřebuje: rozhodnutí 016 zvolilo jediné schéma pro celou kolekci právě proto, že metadata se zapisují jednou a čtou mnohokrát. Nezamítáme to jako špatnou technologii, nýbrž jako odpověď na tuhle otázku.

**B — Docker Compose.** Konfigurace je soubor, ne kód; testy dostanou řetězec proměnnou prostředí a nevědí, kdo službu spustil. Týž soubor už popisuje aplikaci i databázi, takže obě poloviny S5 nese jeden mechanismus a je jedno místo, kam se čtenář dívá. Cena je, že Compose nic nespustí zevnitř `dotnet test`, takže „jedním hlavním příkazem" bude příkaz Compose, ne příkaz .NET.

### Kde probíhá testovací běh

**C — Na hostiteli, v kontejneru jen databáze.** Nejlevnější a pro každodenní vývoj přirozené. Věta o čistém prostředí by ale platila jen pro databázi: .NET SDK by na hostiteli být muselo.

**D — V kontejneru i testovací běh.** Obraz nese SDK i zdroje, takže na hostiteli stačí Docker a S5 platí doslova. Cena je velký obraz a to, že uvnitř není IDE ani debugger — je to reprodukce, ne vývojová smyčka.

### Odkud vezmou testy databázi

**E — Táž služba, na které stojí WideWorldImporters.** Ušetří jednu instanci SQL Serveru. Znamenalo by to ale vázat testy na databázi, kterou nikdy nečtou: rozhodnutí 016 říká výslovně, že nic v testech nejmenuje WideWorldImporters, a říká i proč — schéma fixture je zároveň očekávanou odpovědí, proti které se podle F4 měří podíl správně získaných metadat, kdežto WideWorldImporters je cizí schéma, jehož smyslem je realistický objem dat pro benchmarking Advisoru. Navíc by každý testovací běh platil stažením a obnovením zálohy, ke které nesáhne.

**F — Vlastní služba SQL Serveru, zapnutá profilem.** Čistý obraz bez jakéhokoli obsahu; všechno, co v něm je, vytvoří fixture. Profil znamená, že běžné `docker compose up` o ni nezakopne. Cena je druhá instance SQL Serveru, běží-li oba profily současně.

### Co se stane, když databáze chybí

**G — Přeskočit vždy.** Dnešek. Nemění nic a podmíněnost F4 a F6 zůstane navždy, protože ji nikdo nemá jak vyvrátit.

**H — Selhat vždy.** Obrátí past naruby: vývojář bez databáze dostane červenou sadu. To rozhodnutí 016 zakázalo ze tří důvodů, které platí dál — překlad bez databáze selhat nesmí a sada nemá tvrdit opak toho, co tvrdí nástroj; trvale červená hlavní větev znehodnotí i zbývající signál; a převody, které databázi nepotřebují, si mají udržet rychlou zpětnou vazbu.

**I — Rozhoduje konfigurace.** Proměnná prostředí, kterou nastavuje kontejnerová konfigurace a workflow. Kde je nastavená, tam je nepřítomnost databáze selhání; kde není, přeskočí se jako dosud. Je to totéž rozdělení rolí jako u připojovacího řetězce: o prostředí rozhoduje prostředí, ne kód.

## Rozhodnutí

**Volíme varianty B, D, F a I:**

> **Kontejnerovou konfigurací je Docker Compose a je jediná — týž soubor spouští systém i testy. Testovací běh probíhá uvnitř kontejneru proti vlastní službě SQL Serveru, zapnuté profilem `test`. Kde konfigurace databázi slibuje, tam je její nepřítomnost selhání, ne přeskočení.**

**Jeden soubor a dva profily.** Druhý compose soubor vedle prvního by byl druhou odpovědí na tutéž otázku a čtenář by musel vědět, kdy platí která; profil je táž informace zapsaná uvnitř. Výchozí `docker compose up --build` proto znamená pořád totéž co dosud — aplikace a databáze s WideWorldImporters —, kdežto `docker compose --profile test run --rm tests` je ten „jeden hlavní příkaz" z S5: vyzvedne obrazy, spustí databázi, počká, až začne odpovídat, založí ji, přeloží řešení a proběhne celou testovací sadou. Na hostiteli k tomu nemusí být nic než Docker.

**Testovací obraz je stupeň existujícího Dockerfile, ne čtvrtý Dockerfile.** Repozitář má tři a už dnes platí, že je glob `Dockerfile*` všechny nenajde; čtvrtý by tu past prohloubil. Testovací stupeň staví na stupni `dotnet-build`, takže tatáž kompilace, ze které vzniká aplikace, nese i testy — a stavba obrazu je tím sama kontrolou, že se řešení přeloží na Linuxu, ne jen na Windows.

**Balíčky se do testovacího obrazu pečou.** Ostatní stupně restorují přes cache mount, který se do obrazu nepropíše, protože jim stačí, že výsledek publikace nese všechno potřebné. U testů to nestačí: testovací běh se odehrává až při startu kontejneru — jinak by při stavbě obrazu neměl kam se připojit —, takže balíčky musí v obrazu zůstat. Testovací stupeň proto restoruje a builduje bez cache mountu a spouštěné `dotnet test` běží s `--no-build`, tedy bez sítě.

**Databázi zakládá konfigurace, schéma fixture.** Rozhodnutí 016 svěřilo fixture schéma a databáze není schéma; kdyby si ji fixture zakládala sama, nárokovala by si proti sdílené instanci právo zakládat databáze, které k vytvoření schématu nepotřebuje. Compose ji proto vytvoří krátkou inicializační službou nad týmž obrazem, seřazenou zdravotní kontrolou databázové služby. Tím je zároveň vyřešené čekání: testovací služba se rozeběhne teprve tehdy, když SQL Server odpovídá, takže se nestane, že by se sada přeskočila jen proto, že server ještě startoval.

**Vynucená databáze je proměnná prostředí, ne příznak v kódu.** `ORMCONVERTOR_REQUIRE_TEST_DATABASE` nastavují compose profil `test` a workflow, tedy právě ta dvě místa, kde databázi slibuje konfigurace. Kde je nastavená, ohlásí `SkipIfUnavailable()` selhání s týmž důvodem, se kterým by jinak přeskočil. Výchozí chování se nemění a rozhodnutí 016 zůstává v platnosti beze zbytku. **Tohle, a ne pouhá přítomnost databáze, je to, co odstraňuje podmíněnost kritérií F4 a F6:** nestačí, že databáze někde je — je potřeba, aby běh bez ní nemohl nepozorovaně projít.

**CI dostane tutéž databázi jinou cestou, a je to záměr.** Workflow nespouští Compose, nýbrž service container z téhož obrazu SQL Serveru 2022, do téže proměnné `ConnectionStrings__TestDatabase` a s toutéž vynucenou databází. Compose v CI by při každém běhu stavěl celý obraz znovu, protože běhové prostředí nemá vrstvovou cache — platilo by se minutami za cestu, kterou ověřuje ten, kdo ji spustí. Dělba je tedy tahle: **workflow tvrdí, že sada prochází proti SQL Serveru dané verze, kontejnerová konfigurace navíc tvrdí, že totéž jde zopakovat v prostředí, kde není nic.** Obojí je zdokumentované a obojí míří na tutéž verzi databáze, takže si nemohou protiřečit (S2).

**Heslo v konfiguraci není porušení S4.** Zákaz z S4 se podle rozhodnutí 029 vztahuje na artefakt, který nástroj předává, a na logy. Compose soubor a workflow jsou naproti tomu popis prostředí a instance, kterou popisují, vzniká toutéž konfigurací, je dosažitelná jen uvnitř sítě Compose nebo uvnitř běhu CI a s ním zaniká. Žádný údaj v repozitáři nemíří na skutečnou databázi; připojovací řetězec vývojáře zůstává v user secrets, jak stanovilo rozhodnutí 016. Vyslovujeme to proto, aby se commitnuté vývojové heslo nedalo číst jako opomenutí.

**Aplikace v kontejneru dostane i klíč ke katalogu.** `ConnectionStrings__CatalogDatabase` v compose dosud nebyl, takže kontejnerová instance běžela s vypnutým doplňováním z katalogu — tedy bez toho, co kritéria F4 a F6 nárokují —, ačkoli databáze stála ve vedlejší službě. Doplnění klíče je jednořádkové a viditelným důkazem je `CatalogState = Reached` v odpovědi `/convert`.

## Důsledky

**Vyňatá oblast „kontejnerová konfigurace a databáze v CI" ze záruk mizí.** `architecture.md` §9 ji vypouští ze seznamu vyňatých oblastí a s ní i podmíněnost kritérií F4 a F6; věta, která podle rozhodnutí 030 patřila i do textu práce, se tím ruší, ne přeformulovává.

**Rozhodnutí 030 se tím nenahrazuje.** Nemění se ani jeho kritérium, ani žádná jiná vyňatá oblast. Návrat vyňaté oblasti do hry je věc, se kterou 030 samo počítá — *„Vyňaté oblasti se vracejí do hry v pořadí, které si určí až ta verze"* —, a aktuální hranice záruk je podle rozhodnutí [007](007-documentation-structure.md) stav, takže bydlí v `architecture.md`, ne v rozhodnutí.

**S5 nárokujeme v rozsahu toho, co existuje.** Požadavek jmenuje vedle systému a databáze i javové testovací projekty a experimentální pipeline. Ty v repozitáři nejsou, takže není co kontejnerizovat; zůstávají uvnitř vyňaté oblasti „javový ekosystém a experimenty". Vyslovit to je nutné, jinak by S5 vypadal nárokovaný v rozsahu, který doložit neumíme.

**Odblokuje se 4. stupeň ověření.** Scénář, ve kterém se vygenerovaná entita a její mapování skutečně použijí k zápisu a čtení řádku, měl dosud tu vadu, že by existoval jen na stroji s nastaveným připojením. Tahle překážka padá; položka sama zůstává otevřená.

**`ConnectionStrings__AdvisorDatabase` přestává být neověřenou deklarací.** Rozhodnutí 016 ji nechalo viset právě na téhle práci. Ověřuje se spuštěním, ne čtením.

**CI se prodlouží o start SQL Serveru.** Řádově o desítky sekund na běh. Je to cena za to, že se šestadvacet míst přestane přeskakovat, a tedy za nepodmíněná kritéria F4 a F6.

**Při současném běhu obou profilů běží dvě instance SQL Serveru.** Je to důsledek toho, že testovací databáze a databáze pro Advisor mají různý účel a různý obsah. Kdo obojí nepotřebuje najednou, spustí jen jeden profil.

**Co toto rozhodnutí neurčuje:** jestli se výsledný obraz bude publikovat do registru, jestli má nasazená instance přejít z ruční správy na Compose, a build nativní knihovny Advisoru pro Windows. Všechno tři jsou samostatné otázky s vlastními položkami.
