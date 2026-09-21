# Analýzy

Podkladové materiály o frameworcích samotných: čím se liší, co který umí vyjádřit a jak se v něm napíše minimální funkční aplikace. Slouží jako vstup pro rozhodnutí o návrhu mezireprezentace a jako materiál pro analytickou část práce.

**Nejsou to dokumenty o našem nástroji.** Jak nástroj funguje dnes, říká [`architecture.md`](../architecture.md); proč je takový, [`decisions/`](../decisions/README.md). Rozhodnutí sem odkazují jako na podklad, ne naopak.

**Tvrzení platí proti zafixovaným verzím**, a jen proti nim. Kanonická tabulka verzí je v [`architecture.md`](../architecture.md), části „Zafixované verze" (rozhodnutí [013](../decisions/013-target-framework-versions.md)); chování frameworku se ověřuje proti dokumentaci té verze, ne proti nejnovější. Tutoriály navíc uvádějí ověřené verze i ve vlastní hlavičce.

| Dokument | Co obsahuje |
|---|---|
| [`orm-frameworks-comparison.md`](orm-frameworks-comparison.md) | Srovnání tří .NET ORM, které převodník zpracovává — NHibernate, EF Core, Dapper. Členěné tematicky, ne po frameworcích, protože tak je členěná i analytická kapitola; u každého tématu sleduje expresivitu, syntaxi a implicitní defaulty, které musí parser materializovat. |
| [`java-orm-frameworks-comparison.md`](java-orm-frameworks-comparison.md) | Javový protějšek s týmiž čísly kapitol a řádky tabulek — Hibernate (F7), EclipseLink (F9), MyBatis (F8). Navíc značí řádky, kde se Hibernate a EclipseLink shodují proto, že to určuje Jakarta Persistence 3.2: ta množina je kandidát na sdílenou vrstvu obou wrapperů. Od 2026-09-16 je celá trojice doložená během (viz tutoriály níž), ne jen dokumentací zafixovaných verzí. |
| [`tutorials/nhibernate-getting-started.md`](tutorials/nhibernate-getting-started.md) | NHibernate od nuly: doména `Author` 1:N `Book`, mapování v `hbm.xml`, konfigurace v kódu, schéma přes `SchemaExport`. |
| [`tutorials/efcore-getting-started.md`](tutorials/efcore-getting-started.md) | EF Core od nuly, táž doména a totéž číslování kroků. |
| [`tutorials/dapper-getting-started.md`](tutorials/dapper-getting-started.md) | Dapper od nuly, táž doména a totéž číslování kroků. |
| [`tutorials/hibernate-getting-started.md`](tutorials/hibernate-getting-started.md) | Hibernate od nuly — první javový díl. Odděluje, co je jinak kvůli Javě a co kvůli Hibernate; to rozlišení je pro F10 podstatnější než kterýkoli jednotlivý rozdíl. |
| [`tutorials/eclipselink-getting-started.md`](tutorials/eclipselink-getting-started.md) | EclipseLink od nuly, táž doména. Mění se jen framework, ne ekosystém: kroky 3 a 4 jsou s dílem k Hibernate znak po znaku shodné, takže všechno ostatní je rozdíl implementací JPA, ne jazyka. Obsahuje rozdíl DDL obou implementací nad týmiž anotacemi — nejsilnější doklad o jejich defaultech. |
| [`tutorials/mybatis-getting-started.md`](tutorials/mybatis-getting-started.md) | MyBatis od nuly, táž doména. Javový protějšek Dapperu, ale s `<resultMap>` jako skutečným mapováním; krok 9 ukazuje `getBoundSql`, tedy SQL konkrétního volání bez databáze, a s ním dynamický příkaz jako rodinu dotazů. |

Šest tutoriálů má **záměrně shodnou doménu i číslování kroků**, aby se daly položit vedle sebe a číst po řádcích. Tři javové díly vznikly jako příprava javové větve, před jejími wrappery; od 2026-09-20 nástroj všechny tři frameworky překládá (F7–F9, [`architecture.md`](../architecture.md) §9), takže se dnes čtou jako podklad k nim — popis implementovaného stavu je i tak `architecture.md`, ne tyhle dokumenty.

Prostředí se mezi díly liší a je to vědomé: tři .NET díly a díl k Hibernate předpokládají IDE na hostiteli, zatímco oba nové javové díly běží celé v kontejneru `maven:3.9.11-eclipse-temurin-25-noble` proti SQL Serveru v kontejneru, takže na hostiteli nepotřebují nic než Docker (rozhodnutí [039](../decisions/039-container-configuration-of-the-environment.md)). Dotčené jsou jen kroky 0, 1 a 8; kroky o frameworku zůstávají srovnatelné řádek po řádku.

Starší poznámky z rešerše před převzetím projektu leží v `notes/` v kořeni repozitáře. Jsou zmražené ve stavu k převzetí a proti zafixovaným verzím ověřené nejsou — na rozdíl od dokumentů zde.
