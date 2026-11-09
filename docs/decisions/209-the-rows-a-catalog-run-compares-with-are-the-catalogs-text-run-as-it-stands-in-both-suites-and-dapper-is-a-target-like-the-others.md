# 209 — Řádky, se kterými běh katalogu srovnává, dává v obou sadách text katalogu spuštěný tak, jak stojí, a Dapper je v .NET sadě cílem jako ostatní; soudce vzniká z řádků textu

Datum: 2026-11-09
Stav: platí; nahrazuje odstavec *Zdrojem je Dapper* rozh. 149 a větu o zdroji v odstavci *Schéma je SEDE* rozh. 208
Požadavky: F13, T1, T2, T3
Podklad: rozhodnutí [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [157](157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md), [170](170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md), [208](208-sede-as-the-fourth-reference-domain-over-the-dba-stack-exchange-dump-judged-by-a-judge-of-our-own.md); záznamy běhů [FIBEN 2026-11-02](../runs/2026-11-02-fiben.md) a [SEDE 2026-11-08](../runs/2026-11-08-sede-2.md)

## Kontext

Běh katalogu (FIBEN rozh. 149, SEDE rozh. 208) překládá každý dotaz z Dapperu do cílů a výsledek každého cíle srovnává s *řádky zdroje* (089) a řádky zdroje se soudcem. Obě rozhodnutí říkají „zdrojem je Dapper" a obě sady to dnes čtou každá jinak:

| Sada | Co dává řádky zdroje | Co se z nich stane |
|---|---|---|
| .NET | artefakt, který nástroj vyrobil překladem textu z Dapperu **do Dapperu** — parser T-SQL text přečte, builder Dapperu ho znovu napíše, artefakt se zkompiluje a spustí | Dapper je zároveň zdroj i jeden ze tří cílů; `judge-candidate.tsv` nese řádky tohoto artefaktu |
| Java | text katalogu přes JDBC, parametry deklarované před ním (`DECLARE @jméno typ = literál;`), bez překladu | tři cíle se srovnávají s textem; `java-judge-candidate.tsv` nese řádky textu |

U FIBEN se rozdíl neukázal: parser přečte všech 237 dotazů a Dapper je napíše tak, že vrátí tytéž řádky jako text — běh [2026-11-02](../runs/2026-11-02-fiben.md) má v obou kandidátech soudce týchž 237 digestů. Katalog SEDE je ale psaný lidmi, ne námi, a [běh z 2026-11-08](../runs/2026-11-08-sede-2.md) ukázal, co čtení .NET sady stojí:

- **Zdroj .NET sady neproběhl u 279 z 1 201 dotazů, které katalog nárokuje** — 205 odmítl parser, 73 artefaktů Dapperu selhalo nad daty, jeden se nezkompiloval. Text přes JDBC tytéž dotazy spustil (1 200 z 1 201). Každý z nich pak v .NET sadě selhal třikrát, v Dapperu, EF Core i NHibernatu, jako „zdroj neproběhl", ačkoli selhal překlad, ne zdroj.
- **Kandidát soudce .NET sady pokrývá jen to, co nástroj umí**: 922 nárokovaných dotazů proti 1 200 v javové sadě. Soudce, který by z něj vznikl, by měřil budoucí verze jen nad dotazy, které přeložila ta první — přesně opačně, než má regresní soudce fungovat.
- **Dapper se srovnával sám se sebou.** Test čtvrtého stupně běžel pro všechny tři frameworky a buňka Dapperu byla i řádky zdroje; od chvíle, kdy buňka drží verdikt místo řádků, vyšlo 922 neshod s prázdným rozdílem. Dapper jako cíl přitom ve srovnání chybí: překlad Dapper → Dapper je skutečný překlad (parser i builder), a kde ztratí projekci, nikdo to nevidí, protože jeho řádky jsou měřítkem.

Rozhodnout je třeba, co je *zdroj* ve smyslu 089 u běhu katalogu.

## Zvažované varianty

1. **Ponechat zdrojem artefakt Dapperu a jen opravit srovnání se sebou** (buňka zdroje se shoduje sama se sebou, jak tomu bylo před změnou). Zamítáme: odstraní 922 falešných selhání, ale ne příčinu ostatních tří nálezů. Zdroj dál závisí na tom, co nástroj přečte, kandidát soudce dál pokrývá jen přeložené dotazy a selhání překladu do Dapperu se dál hlásí jako selhání zdroje ve všech cílech.
2. **Soudce brát z javové sady a .NET sadu nechat, jak je.** Zamítáme: dvě sady by srovnávaly s různými zdroji, takže by stejný překlad mohl být v jedné shodný a v druhé ne, a soudce by vznikal jinde, než se proti němu v .NET sadě srovnává.
3. **Zdrojem v obou sadách je text katalogu, spuštěný tak, jak stojí; Dapper je v .NET sadě cílem jako EF Core a NHibernate.** Tohle volíme.

## Rozhodnutí

> **Řádky, se kterými běh katalogu srovnává cíle a ze kterých vzniká soudce, dává text katalogu spuštěný nad databází katalogu bez překladu, s hodnotami parametrů katalogu deklarovanými před ním — v .NET sadě přes ADO.NET, v javové přes JDBC, v obou týmž textem dávky. Zdrojovým frameworkem překladu zůstává Dapper; jeho artefakt je v .NET sadě jedním ze tří cílů a srovnává se s textem jako ostatní.**

**Proč text.** F13 žádá „spustit zdrojovou i přeloženou variantu dotazu a porovnat normalizované výsledky". Zdrojová varianta je to, co do nástroje vstupuje — text katalogu s entitami Dapperu —, a Dapper text nemění: spouští ho, jak je napsaný. Artefakt, který nástroj z textu vyrobí, je už přeložená varianta, i když je jejím cílem znovu Dapper. Brát za měřítko výstup nástroje znamená měřit nástroj jím samým: kde parser text nepřečte, zmizí měřítko i pro cíle, které by dotaz možná nesly nativním SQL, a kde builder Dapperu ztratí sloupec, ztratí ho i měřítko. Text na nástroji nezávisí, a proto může soudit každou jeho verzi.

**Proč stejně v obou sadách.** Javová sada to tak dělá už dnes, protože Dapper nemá. Když obě sady spouštějí týž text týmž způsobem (deklarace parametrů a text v jedné dávce), liší se jejich zdroje jen ovladačem, a dva kandidáti soudce se dají položit vedle sebe digest po digestu: rozdíl mezi nimi pak znamená, že text své řádky neurčuje (výřez bez určujícího řazení, rozh. 170), ne že se liší dva zdroje.

**Co se spouští.** Text se spouští jen u dotazu, který katalog nárokuje jako přeložený: nenárokovaný dotaz neplatí nad schématem, není čtením, nebo selže nad daty, a měřítkem být nemůže. Jeho cíle se dál překládají, kompilují a spouštějí a záznam říká, co s ním udělaly, ale se zdrojem se nesrovnávají; buňka bez zdroje nemá verdikt shody a do podílu shody se nepočítá. Sloupce výsledku textu musí být ty, které katalog uvádí, v jeho pořadí, jinak zdroj selže s důvodem. Text se měří stejnou politikou jako cíle (rozh. 145, 165), takže záznam běhu má čas textu vedle času každého artefaktu.

**Soudce.** Kandidát soudce každé sady nese řádky textu. Soudce se dál dělá z .NET sady (rozh. 149, 208) a doložený běh, ze kterého vzniká, položí oba kandidáty vedle sebe: dotaz, jehož digest se mezi sadami liší, záznam jmenuje. Soudce FIBEN se tímto rozhodnutím nemění, protože řádky textu a artefaktu Dapperu jsou u FIBEN totožné (2026-11-02).

**Co se netvrdí.** Text katalogu není pravda: je to text autora, se vším, co do něj autor napsal (rozh. 208, *Co se netvrdí*). Shoda cíle s textem říká, že překlad zachoval řádky, ne že text odpovídá titulku nebo otázce.

## Důsledky

- **Kód běhu.** .NET `CatalogRun` spouští text nárokovaného dotazu přes ADO.NET v téže dávce jako javový běh, vede zdroj odděleně od buněk (`CatalogSource`), srovnává s ním všechny tři cíle včetně Dapperu, měří ho a do `cells.tsv` píše jeho řádek s frameworkem `Text`; `judge-candidate.tsv` nese řádky textu. Buňka, jejíž zdroj neproběhl, nemá verdikt shody v žádné sadě (sloupec `agreesWithSource` prázdný) a souhrn počítá shodu jen nad buňkami se zdrojem. Testy `FibenRunTest` a `SedeRunTest` berou zdroj z textu.
- **FIBEN:** soudce beze změny; Dapper se v příštím běhu poprvé srovnává jako cíl.
- **SEDE:** soudce vzniká až z běhu podle tohoto rozhodnutí, ne z běhu 2026-11-08; záznam toho běhu zůstává, jak je (rozh. 167).
- [`architecture.md`](../architecture.md) §6.2 (běh FIBEN a SEDE) popisuje zdroj jako text; položka *SEDE jako čtvrtá referenční doména* v [`open-items.md`](../open-items.md) přestává čekat na volbu zdroje soudce.
