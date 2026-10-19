# 152 — Cesta přes asociaci v HQL a JPQL, kterou nelze odvodit, odmítne dotaz v každé klauzuli, v projekci a řazení také

Datum: 2026-10-18
Stav: platí; nahrazuje větu „filtr, seskupení a řazení `Failure`, projekce `Loss`" odstavce *Co se odmítá, se odmítá jmenovitě* rozh. 144
Požadavky: F7, F9, F11, T1, T2
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [101](101-a-join-along-an-association-path-is-derived-from-the-relation.md), [144](144-a-path-through-an-association-outside-join-is-an-implicit-join-or-the-foreign-key-column.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); specifikace Jakarta Persistence 3.2, §4.4.4 *Path Expressions*; revize celého repozitáře z [2026-10-18](../audits/2026-10-18-whole-repository-audit.md), kapitola 4

## Kontext

Rozhodnutí 144 dalo parserům JPQL a HQL čtení cesty přes asociaci mimo `join` a o tom, co odvodit nejde (asociace, kterou mapování nedeklaruje, entita mimo převod, vztah bez párů, kolekce, join z podmínky joinu), řeklo: operand zůstane nepřečtený s důvodem „a klauzule rozhodne jako u každého operandu, který nepřečte (070): **filtr, seskupení a řazení `Failure`, projekce `Loss`**".

Ta věta se rozchází sama se sebou i s kódem. Pravidlo 070 nechává nepřečtený operand v projekci a v řazení ztrátou proto, že **výstup je chudší, ne jiný**: chybí sloupec či pořadí, řádky jsou tytéž. U cesty přes asociaci to neplatí. Specifikace JPA (§4.4.4) i HQL dávají cestě sémantiku vnitřního joinu: je-li asociace na cestě `NULL`, cesta nemá hodnotu a **řádek se výsledku neúčastní**. Zdroj `select o.id, o.customer.name from Order o` tedy vrací jen objednávky se zákazníkem; dotaz bez cesty vrací všechny. A kód navíc přidá implicitní joiny za každou asociaci, kterou cesta stihla projít, ještě než narazila na tu, kterou odvodit nejde: `select o.id, o.customer.region.name …` s regionem mimo převod vyšel jako `select o.id from Order o join Customer o_customer …` — region chybí, join zákazníka zůstal, a objednávky se zákazníkem bez regionu se vracejí, ač je zdroj vypustí. Pod výřezem (`setMaxResults`) bez vypuštěného klíče řazení navíc jde jiná stránka řádků.

Kód přitom 144 nedodržoval ani v tom, co říká o řazení: nepřečtený klíč řazení byl v obou parserech `Loss`, ne `Failure`.

## Zvažované varianty

1. **Ponechat větu 144 a opravit jen řazení na `Failure`.** Projekce by dál vracela jiné řádky než zdroj, se záznamem, který tvrdí opak („dropped" jako u chudšího výstupu). Zamítáme.

2. **V projekci a řazení cestu vypustit i s joiny, které přidala.** Odstraní zbloudilý join, ale ne rozdíl v řádcích: zdroj vypouští řádky s `NULL` asociací právě kvůli cestě, a dotaz bez ní je vrací. Zamítáme.

3. **Cesta, kterou odvodit nejde, odmítne dotaz v každé klauzuli.** Cesta je join; dotaz bez joinu vrací jiné řádky; takový dotaz se nevydá (053) a parser ho odmítne (070) — v projekci a řazení stejně jako ve filtru a seskupení.

## Rozhodnutí

**Volíme variantu 3. Nepřečtený operand, jehož důvodem je cesta přes asociaci, kterou mapy převodu neodvodí, je v HQL i JPQL `Failure` (`Join`) v každé klauzuli — ve filtru, seskupení, `having`, projekci i řazení —, se jménem cesty a větou, že cesta má sémantiku vnitřního joinu a dotaz bez ní by vrátil jiné řádky.** Ostatní nepřečtené operandy projekce a řazení zůstávají ztrátou podle 070; odstavec *Co se odmítá, se odmítá jmenovitě* rozhodnutí 144 platí ve všem ostatním, výčet důvodů včetně (150).

LINQ se to netýká: navigaci tam provider joinuje levým joinem, kde řádek reference smí chybět, a vnitřním jen nad povinnou referencí (148, 151), takže vypuštěná navigace v projekci řádky nemění a pravidlo 070 platí, jak stojí.

## Důsledky

- **Kód.** `JpqlQueryParser` a `NHibernateHqlQueryParser` rozliší v projekci a v řazení nepřečtený operand s kategorií `Join` (jediné, co ji nastavuje, je `ImplicitJoin` cesty) a odmítnou dotaz větou `PathNotDropped`.
- **Testy.** `Hibernate/HibernateJpqlQueryParserTest.APathTheMapsDoNotResolveRefusesTheProjectionAndTheOrderingToo`, nové řádky v `NHibernate/NHibernateHqlQueryParserTest.AnAssociationPathTheMapsDoNotResolveRefusesByName`.
- **Dokumentace.** `architecture.md` §5 (tabulka konstrukcí, *cesta přes asociaci mimo `join`*), `subset.md` 2.6.
