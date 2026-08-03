# 004 — Nevyjádřitelné fakty hlásit varováním, negenerovat náhražky

Datum: 2026-07-16
Stav: platí
Požadavky: F11
Podklad: JSS článek, tab. 1

## Kontext

Dapper nemá mechanismus explicitního mapování klíčů a vztahů — ve srovnání z článku (tab. 1) je specifikace primárního klíče „implicit / manual" a cizí klíče se řeší ručními joiny. Dnešní builder klíče a vztahy zahazuje potichu: kroky `BuildPrimaryKey` a `BuildForeignKey` vůbec nevolá. Je třeba ujasnit, má-li je začít nějak reflektovat.

## Zvažované varianty

1. **Generovat náhražku** — pseudo-atributy nebo metadata v komentářích.
2. **Tiše zahodit** — zachovat dnešní stav.
3. **Negenerovat, ale nahlásit** strukturovaným varováním.

## Rozhodnutí

**Negenerovat, ale nahlásit strukturovaným varováním.**

Do čeho generovat, není: pseudo-atributy nebo metadata v komentářích by vytvářely kód, který žádné Dapper API nečte, a budily by falešný dojem, že se informace přenesla. Tiché zahození je ale přesně chování, které F11 zakazuje — „nepodporované konstrukce nesmí být potichu vynechány".

Výsledek konverze proto ponese strukturované varování za každý nevyjádřený fakt, například „primární klíč (CustomerID) je zachován jen v IR; Dapper ho nevyjadřuje", aby uživatel v UI i volající API viděli, co se do cílového kódu nepropsalo.

## Důsledky

Varování se napojí na diagnostickou infrastrukturu z F11, jakmile vznikne; do té doby stačí jednoduchý seznam varování ve výsledku konverze.

Podstatné je, že IR zůstává úplná — při překladu Dapper → EF Core / NHibernate se metadata neztrácejí, protože zdrojem pravdy je pivot přes IR, ne vygenerovaný Dapper kód.

Otevřená návaznost: audit 2026-08-02 (položka R3) navrhuje přeformulovat tento mechanismus jako obecnou kategorii „fakt nevyjádřitelný v cílovém frameworku" parametrizovanou frameworkem, ne jako dapperovskou větev, a zahrnout do ní i zúžení uvnitř typového modelu. Do rozhodnutí o R3 platí toto rozhodnutí v původním rozsahu.
