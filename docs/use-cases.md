# Případy užití

**Účel:** vrstva *nad* požadavky — kdo nástroj používá, v jaké situaci ho otevře a co tím řeší. `requirements.md` odpovídá na otázku „co musí systém umět", tenhle dokument na otázku „proč to má umět a pro koho". Požadavky F1–F15 jsou odvozené: každý z nich vznikl z některého scénáře níž, a když se scénář nenajde, je to samo o sobě zjištění.

Dokument je živý: scénář přibude, když se objeví, a upraví se, když se ukáže, že jsme si ho představovali jinak. Popis současného chování je v [`architecture.md`](./architecture.md), plnění požadavků v [`traceability.md`](./traceability.md), hranice záruk v `architecture.md` §9.

---

## Aktéři

| Aktér | Co s nástrojem řeší | Co potřebuje především |
|---|---|---|
| **Migrující vývojář** | Má běžící projekt nad jedním frameworkem a potřebuje ho převést na jiný — kvůli výkonu, kvůli konci podpory, kvůli sjednocení s jiným týmem. | Věrnost překladu a **jmenovitý seznam toho, co překlad neunesl**; potichu vynechaný fakt je horší než neúspěšný převod. |
| **Architekt před volbou** | Ještě nemá napsáno a rozhoduje se, který framework unese jeho dotazovou zátěž. | Srovnání postavené na měření, ne na dojmu; a možnost říct omezení (kolik frameworků, kolik paměti, jak často se který dotaz volá). |
| **Autor experimentu** | Diplomová práce a články: matice překladů, metriky korektnosti, srovnání s LLM. | Dávkové zpracování přes API, opakovatelnost do posledního bajtu a strojově čitelný záznam každého běhu. |
| **Konzumentský projekt** | Není uživatel, je příjemce: projekt, do kterého vygenerovaný artefakt vstoupí a který ho přeloží a spustí. | Aby artefakt nic netvrdil o něm samotném — název sestavení, připojení, závislosti si dodá sám (rozhodnutí [040](./decisions/040-boundary-of-the-handed-over-artifact.md)). |

## Co je ve skutečnosti vstupem — a kde nástroj začíná a končí

Ve skutečnosti má uživatel v ruce **projekt**: repozitář s entitami, mapovacími soubory, konfigurací a dotazy rozesetými po service třídách. Nástroj tuhle hierarchii **nevidí a nezkoumá**. Jednotkou převodu je *artefakt* — obsah jednoho souboru s entitou, jedno mapování, jeden dotaz —, převodem je **množina jednotek poslaná najednou**, a jméno souboru je popisek na klientovi: jednotky odeslané na `/convert` žádné jméno nenesou a server o souborech neví (rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md), `architecture.md` §9).

Z toho plyne dělba práce, kterou je lepší říct nahlas, než ji nechat každého objevit:

- **Vyhledání artefaktů v projektu je práce uživatele.** Nástroj neprochází repozitář, nepozná dotaz uvnitř service třídy a nesestaví seznam entit sám.
- **Překládá se mapovací a dotazový obsah, ne aplikační kód okolo.** Volající metoda, transakce, DI registrace a `DbContext` s připojením zůstávají uživateli; připojení navíc nevstupuje ani do mezireprezentace (rozhodnutí [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md)).
- **Doplnit chybějící fakty umí databáze, ne odhad.** Když zdroj mapovací informaci nenese — typicky u Dapperu — doplní ji katalog připojené databáze (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)). Bez připojení převod proběhne na konvencích a řekne to záznamem.
- **Sestavení výsledného projektu je práce konzumenta.** Co k artefaktu chybí, vyjmenovává každý scénář níž zvlášť.

---

## UC1 — Migrace projektu z Dapperu na EF Core

*Aktér: migrující vývojář. Požadavky: F4, F5, F6, F11, F14, S7. Nejvíc namáhaný scénář celého nástroje.*

**Výchozí stav.** Projekt používá Dapper: entity jsou prosté C# třídy bez atributů, mapování neexistuje (jméno sloupce = jméno vlastnosti, klíč nikde), dotazy jsou řetězce SQL. Databáze běží a je dostupná.

**Tok.** Uživatel vybere zdrojový framework Dapper a cílový EF Core, vloží entitní třídy a SQL dotazy jako jednotky jednoho převodu a spustí překlad. Server jednotky rozparsuje do mezireprezentace, **doplní z katalogu**, co zdroj neřekl (primární a cizí klíče, názvy tabulek a sloupců, typy, nullabilitu, unikátní omezení), a vygeneruje entity EF Core s anotacemi a dotazy jako LINQ.

**Výsledek.** Artefakty po jednotkách, ke každé záznamy o převodu: co katalog doplnil, kterou konvenci cíl použil, co zdroj nesl a cíl to vyjádřit neumí. Odpověď navíc nese identifikátor běhu, verzi nástroje, verze obou frameworků a stav katalogu (S6).

**Co doplní konzument.** Soubor projektu se závislostmi, `DbContext` a jeho registraci, připojovací řetězec.

**Kde je dnes hranice.** Bez připojeného katalogu tenhle scénář nedoběhne do konce: entita bez klíče u cíle, který klíč vyžaduje, se odmítne u kontroly úplnosti a řekne to záznamem (rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md)) — a to je správně, protože klíč není co uhodnout. Diagnostika se váže k entitě a vlastnosti, ne ke zdrojovému souboru.

## UC2 — Sjednocení dvou frameworků v jednom řešení

*Aktér: migrující vývojář. Požadavky: F1, F2, F3, F5, F11, F14, S2.*

**Výchozí stav.** Historicky vzniklé řešení má část perzistence v NHibernate (entity + `.hbm.xml`) a část v EF Core. Cílem je jeden framework, ne dva.

**Tok.** Uživatel pošle entity i mapování NHibernate v jednom převodu a zvolí EF Core. Mapovací fakta se slučují podle vysloveného pořadí zdrojů (rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md)): co říká vstupní text frameworku, pak pomocné mapovací artefakty, pak katalog, pak konvence cíle — konflikty se hlásí, nezametají.

**Výsledek.** Entity cílového frameworku včetně složených klíčů a vícesloupcových cizích klíčů; vztahy N:M vyjde jako explicitní spojovací entita (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md)). Opakované spuštění nad týmž vstupem dá bajtově shodný výsledek (S2).

**Co doplní konzument.** U opačného směru (do NHibernate) název sestavení v mapování a `hibernate.cfg.xml`; obojí je fakt jeho projektu, ne převodu (rozhodnutí [028](./decisions/028-assembly-name-is-not-ours-to-invent.md) a [040](./decisions/040-boundary-of-the-handed-over-artifact.md)).

**Kde je dnes hranice.** Dědičnost, komponenty a `<join>` v NHibernate mapování se nečtou a hlásí se záznamem o ztrátě; převod NHibernate → NHibernate proběhne, ale dotaz se vrátí jako HQL, ne jako původní LINQ (`architecture.md` §9).

## UC3 — Výběr cílového frameworku podle naměřeného výkonu

*Aktér: architekt před volbou. Požadavky: F15, T7. **Celá tahle cesta je vyňatá ze záruk** (`architecture.md` §9, oblast 1).*

**Výchozí stav.** Zátěž je známá — sada dotazů a přibližná četnost každého z nich —, framework zvolený není. K dispozici je databáze s reálnými daty.

**Tok.** Uživatel vloží entity a dotazy, vybere kandidátské frameworky a zadá omezení: kolik frameworků smí výsledek použít, paměťový strop, váhy dotazů. Nástroj dotazy přeloží do každého kandidáta, vygenerovaný kód **zkompiluje a spustí** proti databázi, změří čas a paměť a nad naměřenou maticí vyřeší ILP model.

**Výsledek.** Doporučené přiřazení dotazů frameworkům a hodnota účelové funkce.

**Kde je dnes hranice.** Advisor pracuje jen s Dapperem a EF Core, nemá jediný test, potřebuje `libadvisor.so` (staví se jen v Dockeru) a **cizí kód spouští bez izolace a bez limitů** — první věta S4 se nenárokuje, viz [`threat-model.md`](./threat-model.md).

## UC4 — Překlad jediného dotazu jako sonda

*Aktér: kdokoli z prvních tří. Požadavky: F11, S7, S3.*

**Výchozí stav.** Nikdo nic nemigruje. Otázka zní: jak by tenhle dotaz vypadal v jiném frameworku — před rozhodnutím, při učení, při psaní textu.

**Tok.** Uživatel otevře stránku, vybere dvojici frameworků, vloží (nebo si nechá předvyplnit ukázkou) jednu entitu a jeden dotaz a spustí překlad. Bez databáze, bez projektu, bez konfigurace.

**Výsledek.** Přeložený dotaz v nativní syntaxi cíle — LINQ pro EF Core, HQL pro NHibernate, SQL pro Dapper (rozhodnutí [022](./decisions/022-native-query-syntax-in-builders.md)) — a záznamy o tom, co se cestou ztratilo.

**Proč je tenhle scénář v seznamu.** Je to referenční měřítko pro S7: „nahrát vstup → zvolit cíl → přeložit → zobrazit chyby" musí jít na nejvýš pět kroků, a tenhle scénář je ta pětikroková cesta. Zároveň je to jediný scénář, který nepotřebuje nic než prohlížeč.

## UC5 — Dávka pro experiment

*Aktér: autor experimentu. Požadavky: F14, S2, S6, T1, T2, T3.*

**Výchozí stav.** Případová studie nad reálnou open-source aplikací (T1) nebo matice překladů podle kategorií dotazů (T2).

**Tok.** Skript volá `/convert` v cyklu přes všechny dvojice frameworků a všechny kategorie dotazů, sbírá artefakty i záznamy a počítá podíly parsovatelných, kompilovatelných a spustitelných výstupů (T3).

**Výsledek.** Tabulka měření, ke každému běhu jeho identifikátor a verze nástroje i frameworků — bez toho by se výsledek nedal zopakovat ani citovat.

**Kde je dnes hranice.** Rozhraní je na dávku připravené (vícesouborový vstup, výstup po souborech, `/archive`), ale experimenty samotné běží mimo repozitář a T1–T7 verze nenárokuje.

---

## Co nástroj nedělá

Vymezení je součástí zadání scénářů — bez něj se první tři body čtou jako sliby:

- **Nepřevádí schéma databáze.** Ani ho nemění, ani negeneruje migrace; katalog jen čte (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)).
- **Nevydává spustitelný projekt.** Soubor projektu, konfiguraci ani registraci v kontejneru negeneruje, a je to volba, ne mezera (rozhodnutí [040](./decisions/040-boundary-of-the-handed-over-artifact.md)).
- **Nepíše dotazy.** Překládá ty, které dostane; co mezireprezentace neunese — dnes stránkování a poddotazy — hlásí záznamem, ne náhradou.
- **Nenahrazuje běhovou vrstvu.** Nic za běhu neproxuje ani nepřekládá; překlad je jednorázový úkon nad zdrojovým kódem.
- **Nezkoumá repozitář.** Vstup vybírá uživatel; vyhledávání entit a dotazů v projektu není součástí rozsahu.
