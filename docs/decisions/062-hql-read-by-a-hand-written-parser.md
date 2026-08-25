# 062 — HQL se čte vlastním sestupným parserem v NHibernate wrapperu

Datum: 2026-08-25
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [022](022-native-query-syntax-in-builders.md), [025](025-query-language-as-content-type.md), [026](026-home-of-shared-query-reading.md) a [047](047-content-type-reaches-the-query-parser.md); [`architecture.md`](../architecture.md) §5 a §9 (vyňatá oblast 4); JSS §3.2

## Kontext

Rozhodnutí 022 zvolilo HQL jako cílový tvar NHibernate a zároveň přiznalo nesymetrii: jako zdroj se NHibernate čte z LINQ, protože HQL parser by znamenal buď vlastní gramatiku, nebo referenci na NHibernate uvnitř wrapperu. Převod NHibernate → NHibernate tedy proběhne a vrátí platný dotaz, ale ne týž text — §9 architektury to nese jako vyňatou oblast 4, požadavek T3 nemá u tohoto směru co porovnávat a matice T2 v něm měří překlad z jiného jazyka, než v jakém je zdroj napsaný.

Infrastruktura přitom na parser čeká hotová a čekala na něj vědomě: typ obsahu `HqlQuery` existuje od rozhodnutí 025, holý HQL artefakt builder vydává právě proto, aby spotřebitel nemusel dotaz dolovat z generovaného kódu, typ obsahu dojde až do dotazového parseru (rozhodnutí 047) a orchestrace vybírá parser podle jazyka jednotky — jednotka `HqlQuery` dnes končí poctivým `Failure` „zdroj nemá parser“. Domov parseru vyslovila otevřená položka podle pravidla rozhodnutí 026: parser jazyka bydlí ve wrapperu frameworku, který tím jazykem píše, tedy v `NHibernateWrappers`, v téže kategorii jako `DapperSqlQueryParser` s gramatikou T-SQL.

Nerozhodnuté zůstalo, **čím** se HQL přečte — a odpověď se nesmí vyhnout konfrontaci s variantou 5 rozhodnutí 026, která ručně psaný parser pro T-SQL zamítla: tam byl vlastní parser místem, „kde ručně psané parsery hnijí“, protože chyba se neprojeví výjimkou, ale tiše jiným významem dotazu.

## Zvažované varianty

### 1 — Reference na NHibernate uvnitř wrapperu

Referenční parser HQL existuje jediný a je uvnitř NHibernate (`NHibernate.Hql.Ast.ANTLR`). Zamítáme, a je to zamítnutí doslovné, ne analogické: S1 zakazuje wrapperu závislost na frameworku, pro který generuje, a rozhodnutí 022 tuhle cestu výslovně jmenovalo jako to, co wrappery dělat nemají. Nad rámec S1 je to i prakticky špatná závislost — ten parser je vnitřnost NHibernate, ne veřejné API pro čtení jazyka, a jeho výstupem je ANTLR strom stavěný pro překlad do SQL uvnitř session factory, ne pro čtení do cizí mezireprezentace.

### 2 — Hotová gramatika třetí strany

ANTLR gramatiky v `grammars-v4` popisují HQL javovského Hibernate, ne NHibernate 5.7.0 — oba jazyky se od rozdělení projektů rozešly (Hibernate 6 nadto HQL přepsal na novou gramatiku) a žádná autorita gramatiku pro NHibernate neudržuje. Argument, kterým rozhodnutí 026 zvolilo `ScriptDom` — referenční parser od výrobce jazyka, doložitelný jednou větou —, tady nemá předmět: výrobcem jazyka je NHibernate sám a jeho parser je varianta 1. Zbyla by komunitní gramatika cizího dialektu plus ANTLR runtime ve wrapperu, tedy slabší záruky než vlastní gramatika podmnožiny, kterou kontrolujeme celou. Zamítáme.

### 3 — Vlastní sestupný parser podmnožiny, kterou nese dotazová mezireprezentace

## Rozhodnutí

**Volíme variantu 3. `NHibernateWrappers` dostává `NHibernateHqlQueryParser` — vlastní tokenizer a rekurzivní sestup nad podmnožinou HQL, kterou dotazová mezireprezentace unese. Parser si nárokuje jedině typ obsahu `HqlQuery`; LINQ zůstává parseru sdílenému (rozhodnutí 026) a na jeden dotazový jazyk připadá dál právě jeden parser (rozhodnutí 025).**

**Proč tu neplatí zamítnutí varianty 5 z rozhodnutí 026.** Rozdíl je trojí a každý sám o sobě mění závěr:

1. **Jazyk je jiný a menší.** Varianta 5 stála před celým T-SQL — citované identifikátory, escapování, funkce, priority, všechno v jazyce, jehož vstup píše člověk a nástroj ho jen čte. Tady je jazykem podmnožina HQL vymezená tím, co mezireprezentace vůbec unese: klauzule select, from, join s `with`, where, group by, having a order by, podmínkový strom s porovnáními, `like`, `in (poddotaz)`, `is [not] null`, `exists`, `between` a poddotazy v operandu. Co leží mimo, by se nepřeneslo, ani kdyby to parser přečetl — gramatika je uzavřená stejně jako slovník `QueryFeature`.
2. **Gramatiku drží kotva, kterou T-SQL neměl.** Nástroj HQL sám vydává, takže výstup builderu je vstupem parseru a round-trip test builder → parser → builder přibíjí obojí k témuž jazyku: gramatika parseru se nemůže rozejít s jazykem builderu, aniž by spadl test. A třída selhání, kvůli které varianta 5 padla — tichý jiný význam —, je ošetřená disciplínou, kterou dotazová větev už má: neznámý znak nebo tvar je `Failure` s řádkem a sloupcem (táž věta, kterou S7 dostává od `TSql160Parser`), konstrukce mimo model je záznam podle rozhodnutí 010. Parser smí nerozumět; nesmí rozumět jinak.
3. **Alternativy jsou jiné.** U T-SQL stál proti vlastnímu parseru referenční parser od výrobce, zadarmo a s formální gramatikou — vlastní parser by byl luxus. U HQL jediný referenční parser stojí za zdí S1. Volba nezní „vlastní, nebo referenční“, ale „vlastní, nebo žádný“ — a „žádný“ znamená nechat vyňatou oblast 4 otevřenou navždy.

**Jména procházejí mapovací mezireprezentací, jako přesný invert builderu.** HQL jmenuje entity a vlastnosti, mezireprezentace tabulky a sloupce. Visitor builderu překládá na výstupu sloupec → vlastnost a tabulku → entitu; parser dělá totéž pozpátku — jméno entity rozřeší na kvalifikovanou tabulku a jméno vlastnosti na sloupec podle map převodu, a co mapy neznají, projde doslova, týmž záložním pravidlem, jaké má builder. Tím je round-trip uzavřený i nad mapováním, kde se vlastnost jmenuje jinak než sloupec.

**Hlášení drží dnešní precedens, ne nové pravidlo.** Syntaktická chyba je `Failure` s pozicí. Konstrukce, kterou model nenese, dostává týž záznam, jaký ve stejné situaci vydávají ostatní dva parsery: join po asociační cestě a join bez `with` padají se záznamem o ztrátě jako nečitelný join u Dapperu, `in` se seznamem hodnot padá jako u Dapperu na úrovni klauzule, parametr dotazu je nečitelný operand jako T-SQL proměnná, `distinct` je ztráta jako v LINQ. Otázka, kde přesně vede mez pravidla 053 u joinů, je otevřená položka a tenhle parser ji nepředjímá.

**Rozhraní se nemění.** Přibývá jedna třída ve wrapperu a jeden řádek v `ParserFactory`; `AbstractWrappers` ani orchestrace se nedotýkají, což je přesně cena, kterou S1 pro nový parser slibuje.

## Důsledky

**Vyňatá oblast 4 vystupuje z hranice záruk.** Převod NHibernate → NHibernate je textový round-trip nad holým HQL artefaktem, T3 má u tohoto směru co porovnávat a matice T2 v něm měří překlad z jazyka, ve kterém je zdroj napsaný. Do nároku to vstoupí příštím vydáním jako MINOR (rozhodnutí 041 — táž úvaha, kterou kontejnerizace vstoupila do 1.1.0); vydání 1.2.0 je beztak plánované uzavřením cíle 1.

**`CSharpQuery` zůstává parseru LINQ.** C# metoda obalující `session.CreateQuery("…")` se nečte — na jeden jazyk připadá jeden parser a `CSharpQuery` si drží ten sdílený. Kdo chce číst HQL, pošle ho holé, přesně v tom tvaru, v jakém ho nástroj sám vydává (rozhodnutí 025). Případné pozdější čtení HQL z obalu C# by byla obdoba dvou fází Dapper parseru a spadá do rámce tohoto rozhodnutí, ne do nového.

**Stránkování v holém HQL není a není to ztráta.** `SetFirstResult`/`SetMaxResults` žijí na `IQuery` mimo text dotazu (rozhodnutí 060), takže holý artefakt výřez nenese a round-trip ho nepotká; v textu nikdy nebylo, takže se čtením nic neztrácí (úvaha rozhodnutí 028). NHibernate-specifika mimo podmnožinu — klauzule `skip`/`take`, `fetch`, pojmenované parametry, asociační joiny — končí záznamem, ne odhadem.

**Obrazovka překladu nabízí u NHibernate čtvrtou jednotku.** `RequiredContent` dostává u NHibernate jednotku „Query (HQL)“ a `Samples` její vzorek — bez toho by klientská validace (typ mimo nabídku zdroje) nový jazyk odmítla dřív, než by se k parseru dostal. Je to datová změna čtecích koncových bodů, ne změna schématu kontraktu.

**Testy podle vzoru rozhodnutí 022:** tvarová aserce na každou kategorii rozsahu, negativní případy (syntaktická chyba s pozicí, asociační join, seznam hodnot v `in`, parametr) a k tomu round-trip identita: výstup builderu přečtený parserem a znovu vystavěný musí být týž text.
