# 009 — Deskriptor cílového frameworku místo vlastností rozptýlených v builderech

Datum: 2026-08-11
Stav: platí
Požadavky: F2, F4, F7–F10, F11, S1
Podklad: audit 2026-08-02, kap. 4.4

## Kontext

Každý cílový framework klade na generovaný artefakt podmínky, které nevyplývají z překládané domény, ale z něj samotného. U NHibernate jsou čtyři a každá je dnes splněna jinak:

| Podmínka | Jak je dnes splněna |
|---|---|
| `virtual` na mapovaných členech | zadrátováno v `BuildPropertySignature` |
| `Equals`, `GetHashCode` a `[Serializable]` u kompozitního klíče | privátní krok `BuildIdentityMembers` mimo šestici abstraktních metod |
| bezparametrický konstruktor | náhodou — builder žádný konstruktor negeneruje, takže C# doplní implicitní |
| nefinální třída | náhodou — builder negeneruje `sealed` |

Dvě ze čtyř tedy platí jen proto, že jsme nic neudělali. Žádná není nikde vyslovena. U EF Core přibývá pátá položka téže povahy: bez atributu pro bezklíčový typ si EF Core primární klíč odvodí konvencí z vlastnosti pojmenované `Id` nebo `<název typu>Id`, takže entita, kterou mezireprezentace vede bez klíče, ho v generovaném kódu tiše získá.

Rozhodnutí [006](006-flat-composite-key-rendering.md) tuto kategorii už jednou pojmenovalo — pro kompozitní klíč — a přiřadilo ji builderu. Bezprostředním podnětem byl kritický nález auditu, kdy chybějící identitní členy vyrobily syntakticky správný a přitom nespustitelný výstup. Zobecnění tehdy zůstalo otevřené.

Rozhodnutí [008](008-database-as-metadata-source.md) k témuž konceptu přidalo obrácenou stranu. Rozsah čtení databázového katalogu určuje cílová strana převodu: cíl deklaruje, které mapovací fakty vyžaduje a které umí vyjádřit, a z deklarace vzniká jediná dávková poptávka. Kde tato deklarace žije, 008 nechalo otevřené.

Obě strany dnes existují jen jako podmínky rozeseté v tělech builderů, nebo vůbec ne. Seznam faktů, které Dapper nevyjádří a o kterých má podle rozhodnutí [004](004-unexpressible-facts-as-warnings.md) nést varování, je zapsán pouze jako text ve srovnávací analýze v `docs/analysis/`. Při rozšíření na Hibernate, MyBatis a EclipseLink si tyto vlastnosti každý nový builder objeví znovu a na některou zapomene — u NHibernate se to už jednou stalo.

## Zvažované varianty

### A — Ponechat v builderech, doplnit společný test

Vlastnosti frameworku zůstanou zapsané v kódu builderů; přibude jen test napříč frameworky, který ověří, že generovaný artefakt obsahuje, co má.

Je to nejlevnější varianta a odchytila by chybu typu chybějících identitních členů. Neřeší ale vstupní stranu: poptávku do katalogu nelze sestavit z podmínek rozptýlených v generujícím kódu, protože ta musí vzniknout dřív, než generování začne. A test, který kontroluje výstup, nedokáže rozlišit vlastnost frameworku od náhody — bezparametrický konstruktor by prošel dál z téhož důvodu jako dnes.

### B — Abstraktní vlastnosti na `AbstractEntityBuilder`

Deklarace by byla součástí kontraktu builderu; každý konkrétní builder by ji přepsal.

Drží deklaraci na jednom místě a je to nejmenší zásah do dnešní struktury. Poptávku by ale šlo sestavit jen z instance builderu. Advisor posuzuje sadu kandidátních frameworků a podle 008 se jejich poptávky sjednocují do jedné dávky — muselo by se tedy vyrobit tolik builderů, kolik je kandidátů, jen aby se zjistilo, na co se zeptat. Deklarace by navíc byla svázaná s objektem, který drží stav rozpracovaného výstupu, a nešla by testovat ani číst samostatně.

### C — Samostatný deskriptor cílového frameworku

Vlastnosti frameworku popisuje vlastní typ, nezávislý na instanci builderu. Builder ho konzumuje při generování, orchestrace z něj sestavuje poptávku.

## Rozhodnutí

**Varianta C: každý framework má deskriptor, který na jednom místě říká, co framework vyžaduje, co umí vyjádřit a co ke generovanému artefaktu přidává.**

Rozhodující je, že tutéž deklaraci potřebují tři různí odběratelé v různých okamžicích. Builder ji potřebuje při generování, aby přidal vynucené členy. Orchestrace ji potřebuje **před** generováním, aby sestavila poptávku do katalogu podle 008. Diagnostika ji potřebuje, aby podle 004 ohlásila fakty, které se do cíle nepropsaly. Deskriptor svázaný s instancí builderu by druhého ani třetího odběratele neobsloužil, aniž by se builder vyráběl jen kvůli dotazu na jeho vlastnosti.

Druhý důvod je, že otevřená otázka deklarace cílové verze frameworku hledá totéž místo. Volba mezi dvěma způsoby zápisu klíče podle verze EF Core nebo dostupnost typu podle verze NHibernate jsou vlastnosti cíle ve stejném smyslu jako ty ostatní. Bez společného místa vzniknou dvě deklarace vedle sebe a bude se rozhodovat, co patří do které.

### Obsah deskriptoru

**První část jsou vynucené členy** — co builder musí ke generovanému artefaktu přidat, ačkoli to není fakt o překládané doméně. Položka nese podmínku, za které platí: `virtual` na mapovaných členech u NHibernate platí vždy, identitní členy jen u kompozitního klíče, bezklíčový atribut u EF Core jen tehdy, když entita klíč nemá. Podmíněnost je podstatná — bez ní by se z deklarace stal seznam, který nelze ověřit, protože by nešlo říct, kdy má člen chybět.

**Druhá část je vztah ke kategoriím mapovacích faktů** — název tabulky a schématu, název sloupce, databázový typ, délka, přesnost a měřítko, nullabilita, primární klíč, párování sloupců cizího klíče. Ke každé kategorii deskriptor uvádí jeden ze tří stavů:

- **vyžaduji** — bez faktu se negeneruje. Chybí-li i po doplnění z katalogu, překlad se odmítne se strukturovanou diagnostikou. Sem patří identifikátor u NHibernate.
- **umím vyjádřit** — doplň, pokud to jde. Když katalog není k dispozici, nastoupí konvence a fakt nese záznam o svém původu podle 008.
- **neumím vyjádřit** — nedoplňuj a ohlas. Sem patří u Dapperu klíče, vztahy i typ sloupce.

Tři stavy jsou jádro tohoto rozhodnutí, protože z nich plyne obojí, co deskriptor obsluhuje. **Poptávka do katalogu** je sjednocení kategorií ve stavu *vyžaduji* a *umím vyjádřit*, zmenšené o to, co už v mezireprezentaci je. **Varování podle rozhodnutí 004** je průnik faktů, které mezireprezentace nese, s kategoriemi ve stavu *neumím vyjádřit*. Jedna struktura tedy popisuje vstupní i výstupní stranu, což je nejsilnější doklad toho, že jde o jeden koncept, a ne o dva náhodou sousedící.

### Umístění a konzumace

Typ deskriptoru patří do `AbstractWrappers` vedle ostatních frameworkově nezávislých kontraktů; deskriptor konkrétního frameworku do jeho wrapperu, vedle parseru a builderu. Přidání frameworku tak dál znamená sáhnout jen do jeho wrapperu (S1).

Vynucené členy dostanou v builderu pojmenovaný krok. Dnešní `Build()` není šablonová metoda: každý builder si ji píše sám a šest `protected abstract` metod, které měly generování řídit, zůstalo jako prázdné stuby s poznámkou o nepoužití. Krok pro vynucené členy do této struktury nelze zavěsit, aniž by se rozhodlo, jestli struktura vůbec platí. Rozhodujeme proto zároveň, že `Build()` se stane šablonovou metodou nad kroky s parametry a mrtvé stuby zmizí — jednorázovým přepisem podle rozhodnutí [003](003-one-shot-migration.md), ne souběžnou existencí obou cest. Dělat to ve dvou krocích by znamenalo tutéž část pipeline přepsat dvakrát.

### Co deskriptor nepopisuje

Nepopisuje syntaxi ani způsob, jakým se co vypisuje — to zůstává odpovědností builderu. Deskriptor říká, že NHibernate vyžaduje identitní členy u kompozitního klíče; jak vypadá jejich tělo, ví builder. Nepopisuje ani typový model: převodní tabulky zůstávají tam, kde jsou, a neutralizace typového modelu je na tomto rozhodnutí nezávislá.

## Důsledky

**Přidání frameworku znamená parser, builder a deskriptor.** Je to rozšíření kontraktu z S1, ne jeho porušení: dnes tyto informace v kódu také jsou, jen nepojmenované a objevované znovu. Cena je jeden typ navíc, výnos je to, že se na položku nedá zapomenout tiše.

**Dvě náhodou splněné podmínky se stanou deklarovanými.** Bezparametrický konstruktor a nefinální třída u NHibernate se dnes generují správně shodou okolností. Chování se nezmění, přestane ale být náhodné — a jakmile některý budoucí builder začne generovat konstruktor nebo zapečetěnou třídu, test to zachytí.

**Vzniká společný test napříč frameworky**, který proti deskriptoru ověří, že generovaný artefakt obsahuje všechny vynucené členy za podmínek, za kterých platí. Přesně takový test u identitních členů chyběl a jeho absence pustila do repozitáře nespustitelný výstup.

**Seznam nevyjádřitelných faktů se přesouvá z analytického textu do kódu.** Dnes žije v `docs/analysis/` jako součást srovnání frameworků. Tam zůstane jako podklad a zdůvodnění, ale závazná podoba bude v deskriptoru, protože z ní vzniká diagnostika.

**Rozhodnutí 008 dostává, co mu chybělo.** Poptávka do katalogu má odkud vzniknout, a to bez instanciace builderu, takže sjednocení poptávek kandidátů u Advisoru je prostá operace nad deskriptory.

**Deskriptor je připravené místo pro cílovou verzi frameworku**, až se o ní rozhodne. Toto rozhodnutí ji nezavádí a nepředjímá její podobu.

**Pro javové frameworky (F7–F10) je ID třída vynuceným členem** ve stejném smyslu jako identitní členy u NHibernate — rozhodnutí 006 to konstatuje. Její vygenerování ale vyžaduje otypovaná pole, tedy neutralizovaný typový model. Deskriptor takovou položku unese; její naplnění u javového builderu čeká na jinou práci.