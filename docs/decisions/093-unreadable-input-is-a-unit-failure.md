# 093 — Neparsovatelný vstup je selhání jednotky, ne výjimka převodu

Datum: 2026-09-21
Stav: platí
Požadavky: F11, F14, S7
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [044](044-error-response-as-problem-details.md), [045](045-a-conversion-that-produced-nothing-says-so.md), [066](066-records-attributed-to-the-input-unit.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md) a [092](092-input-nesting-depth-capped-before-the-descent.md)

## Kontext

Nástroj čte pět jazyků a na jednotku, jejíž text se přečíst nedá, odpovídá třemi různými způsoby:

| Jednotka, kterou čtečka nepřečte | Co se stane dnes |
|---|---|
| T-SQL — `SqlQueryReader` | `Failure` s řádkem a sloupcem, jednotka nevydá nic, ostatní jednotky převodu se přeloží |
| javová třída — `JavaClassReader` | totéž; pozici nese `JavaSyntaxError` |
| HQL — `NHibernateHqlQueryParser` | totéž |
| JPQL — `JpqlQueryParser` | totéž |
| C# — `CSharpSyntaxTree.ParseText` | Roslyn o chybě ví, my se ho na ni neptáme: ve stromě se nenajde žádná třída, jednotka nevydá nic a mluví za ni až obecný `Failure` orchestrace — bez pozice a bez důvodu (rozhodnutí 045) |
| XML — `XDocument.Parse` | `XmlException` projde orchestrací ven, obsluha `/convert` z ní udělá `ProblemDetails` se stavem 400 (rozhodnutí 044) a **celý převod nevydá nic** |

Poslední řádek je jediný, ve kterém jedna vadná jednotka bere výsledek i všem ostatním. Převod deseti entitních tříd a jednoho rozbitého `hbm.xml` dnes neskončí devíti hotovými artefakty a jedním záznamem, nýbrž čtyřistovkou s jedinou větou, ze které se navíc nedá poznat, **která** z jedenácti jednotek ji způsobila: `ProblemDetails` nese text výjimky a pole `unit` z rozhodnutí [066](066-records-attributed-to-the-input-unit.md) v něm není, protože to není diagnostický záznam. Přesně tomuhle mají F14 („diagnostika po jednotlivých souborech"), S7 („zvýraznění chyb na úrovni souboru a řádku") a rozhodnutí [045](045-a-conversion-that-produced-nothing-says-so.md) („částečný převod musí vydat, co vyrobil") předcházet.

**Není to jedno místo, ale pět.** XML čte `NHibernateXMLMappingParser` na entitním průchodu a `NHibernateXmlQueryParser` na dotazovém — týž `hbm.xml`, dvakrát, protože jednotka smí být mapování a dotaz zároveň (rozhodnutí [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md)) —, `JpaOrmXmlParser` za Hibernate i EclipseLink a `MyBatisXmlMappingParser` s `MyBatisXmlQueryParser` přes společné `MyBatisMapperDocument`. Tři z nich volají `XDocument.Parse` přímo, zbylé dva čtou přes společné `MyBatisMapperDocument`, které místo toho staví `XmlReader` s vypnutým resolverem, protože každý mapper MyBatisu nese DOCTYPE své DTD. Jakákoli odpověď tedy musí být vyslovená jednou a platit pro všech pět, jinak se rozejdou.

**Věta, o kterou se dnešní stav opírá, pochází z rozhodnutí [010](010-diagnostics-as-returned-data.md):** „Výjimky zůstávají pro chyby programu — nepodporovaný cílový framework, poškozený vstup, který nelze naparsovat." Byla napsaná v době, kdy nástroj četl C# a XML a víc nic, a od té doby ji **třikrát přebila praxe, aniž by se na ni kdokoli podíval**: rozhodnutí 045 udělalo záznam z jednotky, kterou nikdo neumí přečíst, rozhodnutí [062](062-hql-read-by-a-hand-written-parser.md) a [076](076-java-wrappers-in-csharp-jvm-in-containers.md) daly HQL, JPQL i javové třídě `Failure` s pozicí a rozhodnutí [092](092-input-nesting-depth-capped-before-the-descent.md) totéž pro vstup zanořený nad strop. Čtyři z pěti jazyků tedy „poškozený vstup" jako výjimku nehlásí. Nezbylo po té větě pravidlo, nýbrž ustrnutí: XML je jazyk, na který nikdo nesáhl.

**Námitka o pozici, kterou nese otevřená položka, neplatí.** Položka předpokládá, že `XDocument` hlásí pozici jinak než `TSql160Parser` nebo naše čtečky, takže by záznam neměl co nést. `XmlException` ale nese `LineNumber` a `LinePosition` jako vlastnosti, tedy strojově a bez parsování textu, a nese je i u dokumentu odmítnutého z jiného důvodu než nesprávný tvar — třeba u zakázaného DOCTYPE. Je to přesně ten údaj, jaký S7 žádá a jaký dnes z celé té výjimky vybírá jedině člověk, který si přečte `detail` ve čtyřistovce.

**Námitka o klientovi platí, ale ke kontraktu nedosahuje.** Frontend opravdu validuje XML `DOMParser`em ještě před odesláním (rozhodnutí [033](033-shape-of-the-static-frontend-screens.md)), takže z prohlížeče se dnešní chování vyvolává těžko. Jenže ta validace je **pomocník, ne brána** — je to formulace, kterou o sobě má `wwwroot/js/translation.js` i `architecture.md` §9 —, server zůstává autoritativní a `/convert` má klienty i mimo prohlížeč: javovou testovací sadu, která si artefakty vyzvedává přes HTTP (rozhodnutí [078](078-java-suite-as-a-client-of-a-running-instance.md)), kontraktní testy (rozhodnutí [043](043-rest-contract-guarded-over-http.md)) a kohokoli, kdo si klienta vygeneruje z dokumentu OpenAPI. Že je vada málo vidět, je fakt o naší jediné obrazovce, ne o kontraktu, který vydáváme.

## Zvažované varianty

### 1 — Nechat, jak to je, a popsat asymetrii v dokumentaci

Nejlevnější a v jednom ohledu obhajitelné: rozbité XML je vstup, který nikdo neposílá schválně, a z prohlížeče ho `DOMParser` zachytí dřív.

Zamítáme. Nejde o schopnost, kterou by nástroj neuměl a kterou by šlo poctivě vyjmout ze záruk — ta schopnost je hotová a čtyřikrát použitá, jen se na pátý jazyk nevztahuje. Vyjmutí by navíc muselo znít „diagnostika po souborech platí pro čtyři z pěti jazyků a u pátého vám selže celý převod", což není hranice, nýbrž popis vady. A F14 mluví o dávkovém vstupu: cena se neplatí u vadné jednotky, nýbrž u těch zdravých vedle ní.

### 2 — Srovnat to opačným směrem: neparsovatelný vstup je výjimka u všech pěti jazyků

Symetrie by vznikla taky, a hlásit by se dalo jednou větou z orchestrace. Ve prospěch mluví, že „vstup je nečitelný" opravdu vypadá jako vada požadavku, a stav 400 je pro vadný požadavek standardní odpověď.

Zamítáme, a je to totéž zamítnutí, které už jednou padlo v rozhodnutí 045 nad variantou „odpovědět 400": u dávkového vstupu se tím částečný úspěch stane nerozeznatelným od úplného selhání a stavový kód neunese důvod u jednotlivé jednotky. F11 navíc žádá diagnostiku **vrácenou**, ne vyhozenou. Varianta by ke všemu zrušila pozici u čtyř jazyků, kde dnes funguje, a vrátila zpátky vadu, kterou rozhodnutí 092 zavřelo teprve před pár dny — a to ve prospěch stavu, který nikdo nezvolil, jen zůstal.

### 3 — Chytat výjimku v orchestraci, jedním `try` kolem každého volání parseru

Lákavé, protože je to jedno místo a pokrylo by i parser, který teprve přibude. Orchestrace jediná ví, o kterou jednotku jde, takže by záznam uměla připsat správně.

Zamítáme ze tří důvodů. `catch (Exception)` kolem parseru by spolkl i chyby programu, které rozhodnutí 010 schválně nechalo padat — `NullReferenceException` z vlastní chyby ve wrapperu by se změnila v mírný záznam „jednotka se nepřeložila" a zmizela by z dohledu. Zúžit ho na `XmlException` by zase znamenalo, že orchestrace zná jméno z `System.Xml`, tedy fakt o jednom vstupním jazyce na místě, které má být nad jazyky i frameworky (S1). A pozice by se z výjimky musela dolovat tam, kde o ní není co vědět: čtečka jazyka je jediné místo, kde je pozice fakt, a ne jen pole nějaké výjimky.

### 4 — Hlásit tam, kde se čte: `Failure` s pozicí, týmž kanálem jako SQL a Java

## Rozhodnutí

**Volíme variantu 4 a vyslovujeme jedno pravidlo pro všech pět jazyků: jednotka, jejíž text čtečka nepřečte, je `Failure` připsaný té jednotce a nesoucí pozici, na které čtení skončilo. Jednotka nevydá nic, ostatní jednotky téhož převodu se přeloží a odpověď zůstává 200. Výjimkou neodpovídá na neparsovatelný vstup žádný jazyk — tedy ani XML.**

**Kde přesně vede hranice mezi záznamem a výjimkou.** Rozhodnutí 010 ji položilo správně a jen ji pojmenovalo slovy, která zestárla: výjimka patří **chybě programu**, záznam **stavu, se kterým návrh počítá**. Text, který nám poslal klient, je vždycky druhý případ, ať je jakkoli rozbitý — počítáme s ním, umíme o něm mluvit a víme, kde v něm čtení skončilo. Chybou programu zůstává nepodporovaný zdrojový nebo cílový framework — `ParserFactory` i orchestrace na něj dál vyhazují `InvalidOperationException`, a je to správně, protože to není vlastnost vstupní jednotky, nýbrž požadavku jako celku — a jakákoli vada našeho kódu. Rozhodnutí 010 se kvůli tomu **nepřepisuje a nemění stav**: hotová rozhodnutí se nepřepisují (rozhodnutí [007](007-documentation-structure.md)) a na rozpor platí pozdější. Tohle je jen první rozhodnutí, které tu hranici vysloví celou, místo aby ji jako 045, 062, 076 a 092 posunulo mlčky o jeden jazyk.

**Věta je jedna a bydlí na jednom místě.** Pět čtecích míst dostane společnou čtečku v `AbstractWrappers` — tam, kde od rozhodnutí 092 bydlí `NestingDepthGuard`, a ze stejného důvodu: `AbstractWrappers` referencuje každý projekt, který něco čte, a jediná věta vyslovená na jednom místě se nemůže rozejít. Čtečka z textu vyrobí dokument, nebo důvod ve tvaru „The XML could not be read at line *L*, column *C*: …", tedy v témž tvaru, jaký mají všechny čtyři existující věty. Nastavení, kterým `MyBatisMapperDocument` ignoruje DOCTYPE a vypíná resolver, jí cestuje parametrem: je to vlastnost dokumentu, ne selhání, a hrozba „dokument si řekne o stažení něčeho zvenku" zůstává zavřená tam, kde je zavřená dnes.

**Pozice musí ukazovat do textu, který poslal klient.** Všech pět míst dnes čte `source.Trim()`, protože bílé znaky před deklarací XML jsou samy o sobě chyba. Ořezání ale posune číslování řádků, takže si čtečka počet zahozených konců řádků pamatuje a přičítá ho zpátky. Bez toho by záznam jmenoval řádek v textu, který klient nikdy neviděl, a S7 by byl splněný jen naoko.

**Když jednotku čtou dva průchody, mluví ten první.** `hbm.xml` a mapper MyBatisu čte entitní i dotazový parser (rozhodnutí 081) a entitní průchod běží celý dřív než dotazový. Rozbitost dokumentu je jeden fakt o jednotce, ne dva, takže ji vysloví **mapovací parser** a dotazový parser nad toutéž jednotkou mlčí — vrátí prázdno a nehlásí nic. Aby ta tichost nemohla někdy znamenat, že nemluví nikdo, patří k rozhodnutí test přes **všechny** frameworky, které enum nese: jednotka s rozbitým XML vydá u každého, kdo XML čte, právě jeden záznam o nečitelném dokumentu — ani žádný, ani dva —, a u toho, kdo XML nečte, místo něj dosavadní větu o jednotce, kterou nikdo nevzal. Vyslovený nad chováním, a ne nad složením `ParserFactory`: takhle drží i pro framework, který teprve přibude, a nečte se k tomu vnitřek orchestrace. Dnes oba průchody čtou XML u NHibernate a MyBatisu, kdežto u Hibernate a EclipseLinku je otázka bezpředmětná — `orm.xml` čte jediný parser.

**Obecný záznam o neplodné jednotce zůstává vedle toho konkrétního.** Jednotka, která nic nevydala, dostane od orchestrace `Failure` podle rozhodnutí 045 („jednotka byla přečtena jako XML a nevzešlo z ní ani mapování, ani dotaz"), a tenhle záznam se nepotlačuje. Je to přesně to, co se dnes děje u rozbité javové třídy i u rozbitého SQL: jeden záznam říká proč, druhý říká, že z jednotky nic nevzešlo. Dvě věty o téže jednotce jsou levnější než výjimka z pravidla, kterou by čtenář musel znát.

**Co tohle rozhodnutí nepokrývá.** Nechává beze změny C#, tedy pátý řádek tabulky výš: Roslyn syntaktickou chybu zná, ale my se ho na ni neptáme, takže jednotka s rozbitou třídou skončí jen obecným záznamem bez pozice. Nespadá to sem, protože to není volba mezi výjimkou a záznamem — C# je na straně záznamu už dnes — nýbrž otázka, jak přesně čtečka mluví, a ta se dá zodpovědět kdykoli potom; zůstává v [`open-items.md`](../open-items.md). Stejně tak se nemění dokument, který se přečíst **dá** a jen nenese očekávaný kořen (`hibernate-mapping`, `entity-mappings`, `mapper`). Takový vstup dál vrací prázdno bez vlastního záznamu, protože o něm nemá kdo říct víc, než co řekne obecná věta o neplodné jednotce.

## Důsledky

**Vstup, který dnes končí čtyřistovkou, napříště skončí dvoustovkou** — s artefakty ostatních jednotek a s jedním `Failure` navíc. Kdo se na stav 400 spoléhal jako na „něco bylo rozbité", musí se napříště dívat do záznamů; u čtyř z pěti jazyků to ovšem musí už dnes.

**Pro číslo verze je to MINOR a nese se to ve vydání `2.0.0`.** Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je MINOR nová schopnost uvnitř cíle, a schopnost tu přibývá: převod s jednou rozbitou jednotkou XML vydá, co vyrobil ze zbytku. To, že z REST kontraktu mizí jedna třída čtyřistovek, je samo o sobě PATCH a ve vydání se veze — vydání nese nejvyšší pozici, kterou pohnula kterákoli jeho změna. Číslo v `Directory.Build.props` se tímhle rozhodnutím nehýbe.

**Hrozba 4 se o kousek zmenší.** `threat-model.md` počítá s tím, že se v textu výjimky poslaném do pole `detail` může objevit jméno serveru nebo instance (rozhodnutí 044). Jedna rodina výjimek tím kanálem napříště neprojde vůbec a věta v záznamu je naše, ne cizí. Hrozbu to nezavírá — jiné výjimky tou cestou chodí dál — a tohle rozhodnutí se nesmí číst jako její náprava.

**Testy.** Zkouší se to přes všechny frameworky, které enum nese: jednotka s rozbitým XML vedle zdravých jednotek téhož běhu dá u každého, kdo XML čte, jeden `Failure` s řádkem a sloupcem připsaný té rozbité — a artefakty těch zdravých, což je celá věc tohohle rozhodnutí. K tomu tři testy na to, co je na něm choulostivé: že `hbm.xml` ani mapper MyBatisu nevydá o téže rozbitosti dva stejné záznamy, že pozice odpovídá řádku v odeslaném textu i u dokumentu s prázdným řádkem na začátku, a že dokument, který se přečíst dá a jen nenese očekávaný kořen, žádný takový záznam nedostane. Na úrovni HTTP k tomu patří test podle rozhodnutí [043](043-rest-contract-guarded-over-http.md), protože stav odpovědi je částí kontraktu: týž vstup, který dosud končil čtyřistovkou, odchází jako 200 s artefakty zdravé jednotky a se záznamem o té rozbité.

**Dokumentace po implementaci.** `architecture.md` §5.1 dostane do výčtu důvodů `Failure` nečitelný text jednotky jako pravidlo platné pro všech pět jazyků a věta „Výjimky zůstávají vyhrazené chybám programu (nepodporovaný framework, neparsovatelný vstup)" se opraví — neparsovatelný vstup mezi chyby programu nepatří. Zúžení S7 v §9 („C# a SQL neparsuje — jejich syntaktickou chybu hlásí server") se doplní o XML a zpřesní o C#, kde server pozici nehlásí. `traceability.md` se mění u S7 a F14.

**Frontend nepotřebuje zásah.** Záznamy zobrazuje tak, jak přijdou (rozhodnutí 033), takže nový `Failure` se objeví v témž pásu jako ostatní, se svou jednotkou i svou pozicí. Kontrola `DOMParser`em zůstává a zůstává pomocníkem — napříště pomocníkem, který uživateli ušetří jedno kolo, a ne jediným, co ho dělí od ztráty celého výsledku.
