# 068 — Dokumentovaná precedence zdrojového frameworku řadí čtení jeho artefaktů

Datum: 2026-08-26
Stav: platí
Požadavky: F2, F5, F7–F10, F11, S1, S2
Podklad: rozhodnutí [017](017-source-precedence-for-mapping-facts.md), [049](049-language-facts-under-source-precedence.md), [063](063-stated-keylessness-as-a-carried-fact.md) a [067](067-a-derived-convention-is-a-statement-a-default-is-not.md)

## Kontext

Rozhodnutí [017](017-source-precedence-for-mapping-facts.md) uspořádalo zdroje faktů do stupňů zdroj → katalog → konvence cíle a uvnitř prvního stupně rozlišilo dvě úrovně: vstupní text frameworku (1a) před pomocnými mapovacími artefakty (1b). Mechanismus je jediný a platí všude: dřív přečtený fakt platí, pozdější smí jen doplnit prázdné místo a rozdíl je záznam `Conflict`. Rozhodnutí [049](049-language-facts-under-source-precedence.md) totéž pravidlo rozšířilo na jazyková fakta vlastnosti, takže druhá deklarace téže vlastnosti nevyrobí duplikát. Jeden případ ale 017 vědomě nechalo otevřený a pojmenovalo ho ve svých důsledcích: framework, který **precedenci mezi svými artefakty sám dokumentuje, a to opačnou**.

Takové frameworky jsou v plánu dva. EF Core dokumentuje pořadí fluent API nad anotacemi nad konvencemi: co konfigurace v `DbContext`u řekne jinak než anotace, platí podle konfigurace. MyBatis (F8) souběh anotací a XML mapperu řeší vlastními pravidly. Číst takový projekt naším výchozím pořadím — třída s anotacemi před konfigurací — znamená u každého překryvu vybrat hodnotu, se kterou zdrojový projekt nikdy neběžel: anotace `[Column("A")]` vedle `HasColumnName("B")` znamená v EF Core sloupec `B`, kdežto naše pořadí by do artefaktu propsalo `A` a záznam `Conflict` by tu volbu ještě obhajoval. Přeložil by se program, který neexistuje. Ověřovací scénáře F5 — nullabilita, název sloupce, datový typ, primární klíč — jsou přitom přesně fakty, které umí tvrdit obě strany takového překryvu.

Dokud se čte jediný mapovací artefakt na framework, případ nenastává; nastane s prvním parserem fluent konfigurace a s MyBatisem. Rozhodnutí 049 zavřelo mechanickou půlku — duplikát nevznikne a rozdíl se nezamlčí —, otázka pořadí zůstala. A s ní druhá otázka, kterou otevřená položka kladla výslovně: **kde má být precedence zdrojového frameworku zapsaná.** Deskriptor to bez rozmyslu být nemůže — popisuje cíl převodu, ne zdroj, a kategorii pro tohle nemá.

## Zvažované varianty

1. **Držet výchozí pořadí všude a rozdíl jen hlásit.** Jednotné a deterministické — jenže artefakt pak nese hodnotu, kterou zdrojový projekt za běhu nemá, a překlad má reprodukovat význam zdroje, ne naši preferenci; tu větu říká samo rozhodnutí 017, když případ otevírá. Záznam `Conflict` na tom nic nespraví: dokumentoval by, že jsme viděli obě hodnoty a vybrali tu, kterou zdrojový framework zahazuje. Zamítáme.

2. **Nechat pořadí čtení výchozí a dovolit parseru s vyšší precedencí přepisovat obsazené fakty.** Věcně by vyšlo totéž, ale rozbila by se rovnost, o kterou se 017 opírá místo evidence původu: „už tvrzeno výš" by přestalo znamenat „fakt je při příchodu neprázdný", takže by správné hlášení konfliktu potřebovalo vědět, *kdo* obsazenou hodnotu zapsal — a původ faktu by se musel vrátit do modelu, čemuž se rozhodnutí [010](010-diagnostics-as-returned-data.md), [015](015-mapping-fact-completion-from-the-catalog.md) i 017 záměrně vyhýbají. Zapisovací cesty builderu by navíc dostaly dva režimy — doplňovací a přepisovací — a každá by musela vědět, ve kterém právě je. Zamítáme.

3. **Zapsat precedenci zdroje do deskriptoru.** Deskriptor říká, co cílový framework žádá a co neumí vyjádřit (rozhodnutí [009](009-target-framework-descriptor.md)); pořadí čtení je fakt o čtení, tedy o zdrojové straně, a vyslovený domov už má — uspořádaný seznam parserů, který 017 prohlásilo za vyslovený fakt frameworku, ne za náhodu seznamu. Nová kategorie deskriptoru by týž fakt zdvojila do místa, které ho nikdy nečte, a rozmyla by pojem, který se dosud drží čistě. Zamítáme.

4. **Pořadí čtení uvnitř prvního stupně určuje precedence, kterou si zdrojový framework sám dokumentuje; kde žádnou nedokumentuje, platí výchozí pořadí vstupní text před pomocnými artefakty. Mechanismus se nemění.**

## Rozhodnutí

Volíme variantu 4. **Uvnitř prvního stupně se artefakty zdrojového frameworku čtou v pořadí jeho vlastní dokumentované precedence, od nejsilnějšího k nejslabšímu; výchozí pořadí — vstupní text před pomocnými mapovacími artefakty — platí tam, kde framework žádnou precedenci nedokumentuje. Mechanismus rozhodnutí 017 zůstává jediný: dřív přečtené platí, pozdější doplňuje prázdná místa a rozdíl je záznam `Conflict`.**

Úrovně 1a a 1b z rozhodnutí 017 zůstávají jako **rozlišení druhů artefaktů** — vstupní text je ten, který je přítomný vždy a jediný nese jazykovou stranu modelu —, přestávají ale samy o sobě být pořadím. Pořadím je precedence frameworku a výchozí pořadí je její záloha.

Námitka, kvůli které 017 zamítlo obecnou přednost mapovacího artefaktu — nejsilnějším zdrojem by se stal artefakt, který nemusí být přítomný, takže by výsledek závisel na tom, co uživatel přiložil —, se tady obrací v argument: u frameworku s dokumentovanou precedencí závisí na přítomných artefaktech i význam zdrojového projektu samého. Přidat fluent řádek znamená změnit, jak projekt běží; překlad, který to nesleduje, není stabilnější, jen nesprávný. A protože pozdější úroveň dál jen doplňuje prázdná místa, fakty, které silnější artefakt neřeší, se přidáním souboru nemění — druhá půlka námitky na doplňovací mechanismus nedopadá.

**Zapsaná je precedence tam, kde 017 pořadí čtení už zapsalo: v uspořádaném seznamu parserů.** Seznam, který `ParserFactory` pro framework vrací, je jeho vyslovený fakt; u frameworku s dokumentovanou precedencí ponese komentář odkaz na její dokumentaci. Deskriptor se nemění — dál popisuje jen cíl.

Konkrétně:

- **EF Core** bude číst fluent konfiguraci před entitní třídou s anotacemi, protože jeho dokumentace staví fluent API nad anotace. Záznam `Conflict` pak říká, že anotace tvrdila něco, co konfigurace přebíjí — tedy tutéž událost, kterou EF Core sám řeší týmž směrem.
- **Odvozené konvence zdroje** (rozhodnutí [067](067-a-derived-convention-is-a-statement-a-default-is-not.md)) stojí na konci čtení frameworku, pod oběma úrovněmi artefaktů — tam je řadí každý framework, který precedenci dokumentuje, a materializace jen do prázdného místa je přesně to, co zapisovací cesty vynucují už dnes. Dokumentovaná trojice EF Core fluent → anotace → konvence se tak zobrazí celá: parser konfigurace, parser třídy, materializace konvencí.
- **Framework, který souběh dokumentuje jako chybu, ne jako pořadí** — MyBatis tentýž příkaz definovaný anotací i XML mapperem odmítá —, se soudí sémantikou zdroje, stejně jako rozhodnutí [063](063-stated-keylessness-as-a-carried-fact.md) soudilo rozpor `[Keyless]` s klíčem: kolize dopadne tak, jak dopadá ve frameworku samém. Přesné znění pravidla a jeho dopad — `Conflict`, nebo `Failure` — si proti zafixované verzi ověří rozhodnutí k F8.
- **NHibernate a Dapper se nemění.** Dapper čte jediný artefakt; u NHibernate v rozsahu, který čteme, nese mapovací fakty jen `hbm.xml`, takže se úrovně věcně nepřekrývají, framework mezi nimi žádnou precedenci nedokumentuje a platí výchozí pořadí.

Stupně se nemění: zdroj před katalogem před konvencí cíle platí dál a nic, co si framework dokumentuje, nesahá přes hranici prvního stupně ven.

## Důsledky

**Dnešní chování se nemění.** Žádný dnešní framework nečte dva artefakty s dokumentovanou precedencí mezi nimi, takže pořadí každého seznamu v `ParserFactory` zůstává, jak je. Pravidlo se zapisuje dřív, než na něj kód narazí, z téhož důvodu jako u 017 a 049: překryvy jsou vzácné a vyzkoušením se na ně nepřijde. Komentář `ParserFactory` a `architecture.md` §5 nově vyslovují pravidlo místo výchozího pořadí jako pravidla.

**První parser fluent konfigurace vstoupí do seznamu EF Core první.** Artefakt čtený před třídou může entitu založit — týž najdi-nebo-založ podle jména, který dělá `NHibernateXMLMappingParser`, a na straně vlastností ho drží rozhodnutí 049 (`AddProperty`) a `GetOrCreatePropertyMap`. Stráž konvenčního klíče EF Core se přitom rozšíří z „třída nemá `[Key]` ani `[PrimaryKey]`" na „klíč dosud nikdo netvrdil" — s konfigurací čtenou dřív je to dotaz na stav builderu v témže místě.

**Priorita zdrojů zůstává dokumentovaná, jak žádá F5** — jen je uvnitř prvního stupně per framework a citovatelná z jeho vlastní dokumentace, což je silnější tvrzení než jednotné pořadí: překlad vybírá touž hodnotu, kterou vybírá zdrojový framework.

**Rozhodnutí 017 platí dál beze změny stavu.** Tohle rozhodnutí vyplňuje případ, který 017 ve svých důsledcích výslovně označilo za samostatnou otevřenou položku; jeho výchozí pořadí zůstává pravidlem všude, kde framework mlčí. Verze nástroje se sama o sobě neposouvá: nemění se artefakt ani rozhraní (rozhodnutí [041](041-versioning-and-release.md)).
