# 013 — Zafixované verze cílových frameworků

Datum: 2026-08-14
Stav: platí
Požadavky: S2, S6, F7–F10
Podklad: `.csproj` všech projektů repozitáře; `docs/analysis/tutorials/hibernate-getting-started.md`; projects.eclipse.org (EclipseLink 5.0.0), github.com/mybatis/mybatis-3 (3.5.19), github.com/jakartaee/persistence

## Kontext

Wrappery nereferencují žádný ORM balíček, jen Roslyn — generují text. Cílová verze tedy nikde není uvedená a existuje jen implicitní předpoklad o tom, jakou syntaxi cíl přijme. Jenže ten předpoklad rozhoduje: mezi `[PrimaryKey]` a `HasKey` volí verze EF Core, dostupnost `DateOnly` verze NHibernate, a u javové strany dokonce to, jestli je `hbm.xml` vůbec podporovaný formát.

Bez deklarované verze nelze splnit ani S2, který žádá shodný výstup při stejném vstupu, konfiguraci a **verzi**, ani S6, který žádá strojově čitelný záznam běhu včetně verzí frameworků.

Pro .NET stranu je množina verzí známá a ověřená proti `.csproj`. Pro javovou stranu chybí a článek ji nedodá: prototyp i experiment v něm běží na .NET 8 a javové frameworky cituje jen odkazem na dokumentaci — Hibernate bez verze, MyBatis 3 a EclipseLink 2.5.x. Poslední jmenovaná je z doby `javax.persistence`, takže by se anotace psaly proti jinému jmennému prostoru než u Hibernate; pro F9 a F10, kde jde právě o srovnatelnost dvou implementací téhož standardu, je to nepoužitelné.

Repozitář je v tom dál než článek: tutoriál `hibernate-getting-started.md` už fixuje Hibernate ORM 7.4.5.Final, Jakarta Persistence 3.2, JDK 25 a `mssql-jdbc` 13.4.0.jre11.

## Zvažované varianty

**Kde se verze deklaruje:**

1. **V mezireprezentaci.** Verze by se nesla na entitě nebo mapování. Zamítáme: verze je vlastnost cíle, ne zdrojového modelu. Při převodu jedné IR do dvou různých cílů by neplatila ani jedna hodnota a mezireprezentace by nesla fakt, který s jejím obsahem nesouvisí (JSS §5.4).

2. **V konfiguraci převodu jako volný řetězec.** Jednoduché, ale nikdo by neověřil, že hodnota dává smysl, a builder by ji stejně musel interpretovat proti nějaké tabulce toho, co která verze umí — tedy proti deskriptoru.

3. **V deskriptoru cílového frameworku** (rozhodnutí [009](009-target-framework-descriptor.md)). Deskriptor už nese, co cíl umí vyjádřit a co vyžaduje; verze je totéž tvrzení v jemnějším rozlišení a patří do téhož místa.

**Které verze:** buď ty, které cituje článek, nebo aktuální stabilní vydání s ohledem na dlouhodobou podporu. První možnost zamítáme kvůli EclipseLink 2.5.x, viz kontext.

## Rozhodnutí

**Verze se deklaruje v deskriptoru cílového frameworku (varianta 3).** Převod, který verzi nezvolí explicitně, použije zafixovanou.

**Zafixovaná množina** je tato a její kanonické místo je `architecture.md`, ne audit:

| Komponenta | Verze | Proč |
|---|---|---|
| .NET | 10 (`net10.0`) | LTS, listopad 2025 |
| NHibernate | 5.7.0 | aktuální stabilní |
| EF Core (+ SqlServer) | 10.0.10 | řada odpovídající .NET 10 |
| Dapper | 2.1.79 | aktuální stabilní |
| JDK | 25 | LTS, září 2025 — protějšek .NET 10 |
| Jakarta Persistence | 3.2 | aktuální vydání standardu |
| Hibernate ORM | 7.4.5.Final | aktuální stabilní, implementace 3.2 |
| EclipseLink | 5.0.0 | první vydání implementující 3.2 (23. 3. 2026) |
| MyBatis | 3.5.19 | poslední vydání řady 3.5 |
| `mssql-jdbc` | 13.4.0.jre11 | ovladač k SQL Serveru 2022 |
| SQL Server | 2022 | tatáž instance pro oba ekosystémy |

**Kritérium pro budoucí posun:** aktuální stabilní vydání téže řady, u platforem verze s dlouhodobou podporou, u javové strany implementace téhož vydání Jakarta Persistence. Dokud kritérium platí, posun verze je úprava `architecture.md` a nové rozhodnutí nepotřebuje. Změna kritéria — například přechod na Jakarta Persistence 4.0 — nové rozhodnutí potřebuje.

**Mimo rozsah** zůstává Jakarta Persistence 4.0, jejíž vydání je plánované na konec roku 2026, a `hbm.xml` jako *cílový* formát Hibernate: v řadě 7 je zavržený a v 8.0 zmizí, takže Hibernate cílíme anotacemi. Čtení `hbm.xml` se to netýká, to je věc zdrojové strany.

## Důsledky

Deskriptor dostane údaj o cílové verzi a buildery podle něj budou volit syntaxi tam, kde se verze liší; do té doby platí zafixovaná hodnota jako implicitní. Záznam běhu podle S6 bude verze uvádět z téhož místa, takže nemůže tvrdit něco jiného než generátor.

Dvojice NHibernate ↔ Hibernate není paritní a rozhodnutí to nezakrývá: NHibernate 5.7 vychází z návrhu Hibernate 3.x, kdežto Hibernate 7 má XML mapování zavržené. Překlad `hbm.xml` → anotace tedy není volba stylu, ale jediná cesta, a promítne se do překladových pravidel javových builderů.

Dvě zafixované verze nesou riziko, které je lepší mít napsané: EclipseLink 5.0.0 je čerstvý major s rozbitým API, takže se u něj dá čekat víc opravných vydání než u ostatních; a MyBatis 3.5.19 uzavírá řadu postavenou na Javě 8 — na JDK 25 poběží, ale další řada zvedne minimum, takže posun nebude jen změna čísla.

Javová část je zatím **deklarace, ne závislost**: v repozitáři není žádný javový projekt. Verze platí pro tutoriály, analýzy a pro to, co budou javové wrappery cílit; skutečnou závislostí se stanou až s prvním javovým projektem.