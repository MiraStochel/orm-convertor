# 141 — Katalog LDBC běží nad jedním data setem, oficiálním scale factorem 1, a hloubka dotazu na cestu je změřená vlastnost těchto dat zapsaná u dotazu; upravená data ani tabulka vah nevznikají

Datum: 2026-10-15
Stav: platí
Požadavky: T1, T2, T3, F13
Podklad: rozhodnutí [140](140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) (co nechalo otevřené), [110](110-ldbc-snb-as-a-second-reference-domain.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [121](121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md), [122](122-the-text-of-a-catalog-query-takes-the-shape-a-targets-implementation-needs-under-the-same-criterion.md); [`analysis/recursion-and-its-limit.md`](../analysis/recursion-and-its-limit.md) (oddíly 6 a 7); měření všech dotazů katalogu a vlastností grafů nad `LdbcSnb` SF 1 na MIS3 z 2026-10-15 (záznam v [`ORMConvertor/README.md`](../../ORMConvertor/README.md), *Catalog query times*)

## Kontext

Rozhodnutí 140 zvolilo pro pět dotazů na cesty (IC 13, IC 14, BI 15, BI 19, BI 20) prohledání do šířky jako řetěz nerekurzivních definic s hloubkou danou daty a nechalo tři věci otevřené: který data set pro kterou databázi (oficiální scale factor, nebo upravená data pro databázi jen s dotazy BI), zda BI 19 a BI 20 dostanou tabulku vah hran naplněnou při načtení, a kolik hloubka nad daty je. Obojí mělo rozhodnout až měření.

Změřeno bylo 2026-10-15 nad `LdbcSnb` SF 1 na MIS3 (kontejner `mssql_db`, text katalogu s příkladovými parametry, čtyři běhy, z nich tři s teplou cache; uvádí se nejmenší):

| Třída | Dotazy | Čas nad SF 1 |
|---|---|---|
| vyhledání přes index (pod 0,2 s) | IS 1–7 (2–3 ms), IC 8 (8 ms), IC 11 (19 ms), IC 1, IC 4 (60 ms), IC 7 (68 ms), IC 2 (85 ms), BI 7, BI 20 (130 ms), IC 10 (170 ms) | žádný scale factor je nad 0,2 s nezvedne: desetinásobek dat přidá vyhledání přes index jednotky procent |
| průchody v okně | IC 3 (1,4 s), IC 5, IC 6, IC 9 (0,2–0,3 s), IC 12 (1,0 s), BI 1–5, BI 8–13, BI 16–18 (0,2–12 s), BI 6 (27 s), BI 14 (38 s) | všechny pod 300 s |
| dotazy na cesty, dnešní texty s mezí | IC 13 0,12 s, IC 14 2,0 s, BI 15 46 s, BI 19 přes 500 s, BI 20 0,17 s | BI 19 mimo okno |
| dotazy na cesty, řetěz úrovní podle 140 | IC 13 0,17 s (5 úrovní), IC 14 2,2 s (5), BI 15 5,3 s (11), BI 19 6,6 s (5), BI 20 0,13 s (7) | všechny v okně; BI 19 ze 70násobku dolů, protože definici vah čte každá úroveň jednou, ne pro každý dosažený klíč |

Vlastnosti grafů, z každé osoby (ne ze vzorku; skript je `LdbcJudge/LdbcPathQueryTest`, totéž změřil pomocný program nad týmiž hranami):

| Hrany | Osob s hranou | Průměrný stupeň | Komponent | Hloubka | Dvojic na hloubce |
|---|---|---|---|---|---|
| `knows` (IC 13, IC 14) | 9 163 | 39,4 | 1 | **5** (průměr) | 2 172 z 83,95 milionu |
| `knows` s odpověďmi (BI 19) | 8 674 | 32,4 | 1 | **5** (nejvíc hran nejlevnější cesty) | 21 056 z 75,2 milionu |
| `knows` se společnou univerzitou (BI 20) | 3 038 | 4,9 | 443 | **7** | 12 ze 70 146 |
| `knows` s cenou podle fór příkladového období (BI 15) | 9 163 | 39,4 | 1 | **11** | 4 z 83,95 milionu |

Hloubka vážených dotazů je nejvíc hran, které má nejlevnější cesta (mezi stejně levnými ta s nejméně hranami): tolik kol Bellmana–Forda stačí, aby každá dvojice měla svou cenu. U BI 15 závisí cena hrany na parametrech období, takže změřená je pro příkladové období; jiné období může potřebovat jinou hloubku.

## Zvažované varianty

### 1 — Jeden data set, oficiální SF 1, pro celý katalog

### 2 — Oficiální scale factor podle databáze: SF 1 pro Interactive, větší pro BI

Větší data (SF 3, SF 10) by zvedla průchody BI blíž k horní mezi a nechala Interactive na SF 1 se soudcem. Zamítáme: nic to nekoupí. Horní mez okna drží SF 1 u všech dotazů i bez toho; spodní mez porušují jen vyhledání přes index, a ta žádný scale factor nad 0,2 s nezvedne (SF 10 dá desetinásobek řádků a vyhledání roste s logaritmem). Druhý data set by přitom zdvojil měření hloubek, zavedl druhý loader a druhou hodnotu vlastnosti `ldbc.dataset` a rozdělil katalog na dvě poloviny s různými nároky — druhý přístup k témuž problému bez výsledku.

### 3 — Upravená data pro databázi jen s dotazy BI

Prořídlé `knows` nebo vlastní malý graf, nad kterým doběhne i rekurzivní text. Zamítáme: potřeba zmizela (BI 19 běží 6,6 s nad SF 1 tvarem podle 140), a upravená data by vzala soudce všem dotazům Interactive, protože data sdílejí (6.4 analýzy), a hloubku by udělala vlastností vymyšlených dat místo publikovaného data setu.

### 4 — Odvozená tabulka vah hran pro BI 19 a BI 20, naplněná loaderem

Po vzoru `RootPostId`. Zamítáme: rozhodovala by o ní jen doba běhu, a ta je v okně (BI 19 čte definici vah pětkrát, 6,6 s); tabulka by byla druhý mechanismus vedle textu — údržba loaderem, verze vlastnosti, výjimka pro BI 15, jehož váhy závisejí na parametrech a v tabulce být nemohou. Text, který váhy definuje, je totéž, co píše referenční implementace.

### 5 — Hloubka pevná, jedna pro všech pět dotazů

Jedna konstanta (největší z naměřených, 11) pro všechny texty. Zamítáme: u IC 13 a IC 14 by šest úrovní navíc stálo šestinásobek čtení soudce nad celou sadou (6 818 čtení na dotaz a framework) za nic, a konstanta bez vazby na data je mez, ne změřená vlastnost — přesně to, co 140 odmítlo.

## Rozhodnutí

**Volíme variantu 1 a hloubku jako vlastnost dat. Katalog LDBC běží nad jedním data setem, oficiálním LDBC SNB Interactive v1 scale factor 1, pojmenovaným v `LdbcSnbSample.DataSet` týmž jménem archivu, které loader zapisuje do rozšířené vlastnosti `ldbc.dataset` databáze `LdbcSnb`. Hloubka každého dotazu na cestu je změřená vlastnost tohoto data setu nad hranami, které dotaz sám definuje, z každé osoby: průměr grafu u hledání podle počtu kroků, nejvíc hran nejlevnější cesty u hledání podle váhy; u dotazu, jehož hrany závisejí na parametru, nad příkladovými parametry, a poznámka to říká. Hloubka se zapisuje u dotazu (`LdbcSearchDepth`: počet úrovní a co měří), text má přesně tolik úrovní, a dotaz s hloubkou je `AsSpecified`. Upravená data nevznikají, tabulka vah nevzniká, druhý data set nevzniká. Okno 0,2–300 s drží nad SF 1 horní mez u všech 41 dotazů; spodní mez u vyhledání přes index drží počet opakování v měření, jak říká 140.**

Pro problém zjednodušení tak existuje v celém katalogu právě jeden přístup: jeden tvar textu (řetěz úrovní podle 140, týž pro vážené i nevážené hledání), jeden parametr (hloubka), jeden způsob, jak ho získat (měření z každé osoby nad hranami dotazu), a jeden data set, nad kterým platí. IC 12 do toho nepatří: hierarchie tříd tagů je strom, rekurze tam nic nezjednodušuje.

### Proč jeden data set

Měření ukázalo, že data set nemá na okno vliv tam, kde by ho měl mít: dotazy pod spodní mezí jsou vyhledání přes index a rostou s logaritmem dat, dotazy nad horní mezí byly jen BI 15 a BI 19 a ty srazil tvar textu, ne data. Druhý data set by tedy neřešil žádný změřený problém a přidal by druhou hodnotu každé vlastnosti, která teď je jedna: hloubka, soudce, loader, poznámka dotazu. Pro měření T2 a T3 je jeden data set navíc přímo podmínkou srovnatelnosti: matice srovnává frameworky nad týmž dotazem a týmiž daty.

### Proč hloubka u dotazu, a ne u data setu

Hloubka není jedna na data set: `knows` má průměr 5, hrany BI 20 potřebují 7 a hrany BI 15 pro příkladové období 11. Je to vlastnost dvojice (data set, definice hran), a definici hran dává dotaz. Zapsaná u dotazu je i tím, co test drží: `Combined/LdbcCatalogTest` hlídá, že text má přesně tolik úrovní, kolik hloubka říká, a `LdbcJudge/LdbcPathQueryTest` nad `LdbcSnb` jednak porovná výsledek textu s algoritmem (Bellman–Ford na hloubku, nad hranami čtenými vlastním SQL testu, z příkladových parametrů i z osob, měst a firem vybraných z dat), jednak změří hloubku data setu z každé osoby a tvrdí, že není větší než hloubka textu — jen nad data setem, který katalog jmenuje; nad jiným to neplatí a test to řekne.

### Co se stane, když data set hloubku přeroste

Data set s větší změřenou hloubkou, než má text, by z textu udělal znovu zjednodušení. Pravidlo je: text roste o úrovně, data se nezmenšují. Cena je lineární (BI 15 s jedenácti úrovněmi 5,3 s), takže růst je vždy dostupný; a růst hloubky je změna textu katalogu podle existujícího rozhodnutí, ne nové rozhodnutí.

## Důsledky

- **Katalog:** pět textů podle 140 v tvaru s nulovou smyčkou (revize 140), stav `AsSpecified`, `SearchDepth` 5 / 5 / 11 / 5 / 7, poznámka s hloubkou a změřeným časem; příkladová osoba `person2Id` IC 14 a BI 15 je 96 místo 65, která nemá přátele a vracela prázdný výsledek. Konstanta `LdbcSnbSample.DataSet`.
- **Nástroj:** beze změny tvaru; vada *konstanta bez aliasu v dalším operandu množinové operace* opravena před přepisem (čtečka T-SQL ji nese, builder jí dá jméno podle pozice i nahoře v dotazu a v poddotazu).
- **Nativní SQL:** řetěz je mezivýsledek, ne rekurze, takže EF Core pět dotazů píše LINQ; NHibernate a EclipseLink nativním SQL kvůli mezivýsledkům (113). Počty v `architecture.md` §6.2.
- **Testy:** `LdbcCatalogTest.EveryPathQueryStatesTheDepthItsTextRunsWith`, `LdbcJudge/LdbcPathQueryTest`; soudce měří IC 13 a IC 14 dál, nyní jako `AsSpecified` — verdikt nad celou sadou SF 1 je samostatný běh obou sad (položka v `open-items.md`).
- **Dokumentace:** `architecture.md` §6.2, `subset.md` 1.3 a 2.10, `traceability.md` T2, kořenový `README.md` *Guarantees*, `ORMConvertor/README.md` (tabulka časů jako datovaný záznam podle 095).
