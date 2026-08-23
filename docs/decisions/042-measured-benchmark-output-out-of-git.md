# 042 — Naměřený výstup benchmarků mimo git

Datum: 2026-08-23
Stav: platí
Požadavky: T1, T7, S5
Podklad: audit [2026-08-23](../audits/2026-08-23-post-release-1-1-0-audit.md), nález 7.1; rozhodnutí [013](013-target-framework-versions.md) a [034](034-central-version-management.md)

## Kontext

Adresář `benchmarks/results/` drží 235 souborů — 209 obrázků PNG, devět souborů CSV s měřeními, osm stránek HTML, osm souhrnů v markdownu a jeden skript v R. Je to **34,13 MB z 37,16 MB všech verzovaných bajtů repozitáře**, tedy dvaadevadesát procent. Poslední změna obsahu je z **6. března 2025**, tedy více než rok před převzetím projektu; ve forku se nezměnil jediný z těch souborů. Jediný commit forku, který se `benchmarks/` dotkl, přidal do jeho `README.md` šestadvacet řádků o tom, že jde o historický běh.

Nic v `ORMConvertor/` na ten adresář neodkazuje — ani kód, ani testy, ani konfigurace. Odkazuje na něj jen `benchmarks/README.md`, a to jako na výstup jednoho konkrétního běhu.

**Proč to řešíme teď a proč to není úklid.** Dosud šlo o zděděný obsah, na kterém nic nestálo a který nikomu nepřekážel. Vydáním 1.1.0 se ale změnilo, kdo repozitář čte: značka je adresou, kterou dostane čtenář práce a čtenář `CITATION.cff`. Ten si repozitář naklonuje celý, a devět desetin toho, co si stáhne, je vyrenderovaná grafika jednoho běhu z března 2025 — ne nástroj, o kterém si přišel číst.

**Podstatné je, co z toho je zdroj a co odvozenina.** Měření samotná (`.csv`), jejich souhrny (`.md`) a skript v R, který z měření vykresluje obrázky, jsou zdroj: nedají se zrekonstruovat a jsou dokladem, že experiment proběhl. Dvě stě devět souborů PNG a osm stránek HTML jsou **odvozeniny toho skriptu** — jsou obnovitelné a repozitář je nese jen proto, že je někdo tehdy commitnul spolu se vším ostatním. Uvnitř nich jsou navíc dvě dvojice bajtově shodných obrázků, což je vlastní podpis toho, že se ukládalo bez rozmyslu, co.

**A je tu mez, kterou tohle rozhodnutí nepřekročí.** Odstranění z `HEAD` **velikost klonu nezmenší** — bloby zůstávají v historii a `git clone` je stáhne dál. Zmenšit ji umí jedině přepis historie, a ten je vyloučený: značky `1.0` i `1.1.0` jsou vydané na `origin` a rozhodnutí [041](041-versioning-and-release.md) zakazuje posouvat vydanou značku právě proto, aby jedno číslo neoznačovalo dva různé stromy. Přepsat historii by všechny značky přepsalo najednou. Co tedy rozhodnutí umí, je zastavit další růst a narovnat to, co vidí čtenář dnešního stromu — ne uklidit minulost.

## Zvažované varianty

1. **Nechat, jak je.** Nic nestojí a nikoho to nepálí. Jenže „nikoho to nepálí" přestalo platit ve chvíli, kdy repozitář dostal značku a citační metadata; a devadesát dva procent objemu na odvozeninu jednoho ročního běhu je tvrzení o tom, co je v projektu podstatné. Zamítáme.

2. **Smazat `benchmarks/` celé.** Nejčistší na pohled a špatné věcně: benchmarky jsou zděděná experimentální část, na kterou se odvolává `open-items.md` (sjednocení ADO.NET provideru) a která je podkladem pro T1 a T7. Smazat srovnání sedmi ORM znamená smazat doklad, že vzniklo. Zamítáme.

3. **Vyjmout `results/` z gitu a nechat ho na disku.** Adresář zůstává tam, kde byl, přestane být verzovaný a `.gitignore` ho drží mimo. Historie se nepřepisuje.

4. **Přesunout `results/` do přílohy vydání nebo do samostatného úložiště.** Správné tam, kde data mají vlastní životní cyklus a vlastní citaci. Tady je nemají: je to jeden běh, který se už neopakuje, a vytvořit pro něj druhé úložiště znamená druhé místo, které se udržuje. Zamítáme jako předčasné — varianta 3 ho nevylučuje.

## Rozhodnutí

**Volíme variantu 3. `benchmarks/results/` přestává být verzovaný, zůstává na disku a `.gitignore` ho drží mimo git.** Historie se nepřepisuje a značky se nedotýkáme.

Vede nás k tomu rozdíl mezi zdrojem a odvozeninou, ne velikost sama. Kdyby v adresáři byla jen měření a souhrny — necelý megabajt —, nechali bychom je tam, protože jsou dokladem experimentu. Co ho nafukuje na čtyřiatřicet megabajtů, je dvě stě devět obrázků, které z těch měření vyrábí verzovaný skript v R; verzovat vstup i výstup téhož skriptu je táž chyba jako verzovat `bin/` vedle `.cs`. Je to ostatně pravidlo, které v tomhle repozitáři už jednou padlo z opačné strany: rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) zrušilo commitovaný build frontendu právě proto, že v gitu má být to, co se píše, ne to, co z toho vypadne.

**Adresář se přitom nemaže.** Kdo měření potřebuje — a text práce je potřebovat bude, protože T1 i T7 se o ně opírají —, má je dál na disku a `benchmarks/README.md` popisuje běh, ze kterého vzešla. Odstranění z gitu není odstranění dat; je to tvrzení, že tenhle repozitář je nástrojem, a ne archivem jednoho měření.

**Vyjmutí je celé, ne po souborech.** Nechat v gitu `.csv` a vyjmout `.png` by znamenalo hranici, kterou musí někdo hlídat při každém dalším běhu. Adresář je výstup, a výstup je vyjmutý vcelku — táž konstrukce, jakou pro hranici záruk zvolilo rozhodnutí [030](030-scope-of-version-1-0.md).

## Důsledky

**Repozitář ztrácí 235 souborů z indexu a klesá ze 726 na 491 verzovaných souborů.** Objem klonu se tím nezmenší, dokud se nepřepíše historie — což nechceme (viz kontext). Zmenší se ale to, co vidí `git status`, `git ls-files` a čtenář, který si strom prohlédne.

**Kdo si naklonuje repozitář, měření nedostane.** To je cena a je vědomá. Doklad, že experiment proběhl, nese `benchmarks/README.md` a historie gitu, ze které se dá kterýkoli soubor kdykoli vytáhnout (`git show <commit>:benchmarks/results/…`); měření tedy nezmizela, jen se přestala roznášet s každým klonem. Až bude text práce potřebovat konkrétní číslo, bere ho z disku nebo z historie.

**Vzniká pravidlo pro příští běh.** Výstup měření se do gitu nevrací — ani jeho obrázky, ani jeho HTML. Kdyby někdy vznikl důvod měření publikovat, je to varianta 4 a bude to nové rozhodnutí, ne návrat k tomuhle stavu.

**`.gitignore` se u té příležitosti zkrátil.** Stock šablona o 484 řádcích, ze které fungovala čtyři pravidla, ustoupila desetiřádkové verzi; součástí byla i past, kterou šablona nesla — zakomentovaný řádek `#wwwroot/` s pozvánkou „uncomment if you have tasks that create the project's static files in wwwroot". Od rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) jsou ve `wwwroot` **zdrojové soubory frontendu**, takže kdo by tu pozvánku přijal, odverzoval by celý frontend.
