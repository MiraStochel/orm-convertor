# Analýzy

Podkladové materiály o frameworcích samotných: čím se liší, co který umí vyjádřit a jak se v něm napíše minimální funkční aplikace. Slouží jako vstup pro rozhodnutí o návrhu mezireprezentace a jako materiál pro analytickou část práce.

**Nejsou to dokumenty o našem nástroji.** Jak nástroj funguje dnes, říká [`architecture.md`](../architecture.md); proč je takový, [`decisions/`](../decisions/README.md). Rozhodnutí sem odkazují jako na podklad, ne naopak.

**Tvrzení platí proti zafixovaným verzím**, a jen proti nim. Kanonická tabulka verzí je v [`architecture.md`](../architecture.md), části „Zafixované verze" (rozhodnutí [013](../decisions/013-target-framework-versions.md)); chování frameworku se ověřuje proti dokumentaci té verze, ne proti nejnovější. Tutoriály navíc uvádějí ověřené verze i ve vlastní hlavičce.

| Dokument | Co obsahuje |
|---|---|
| [`orm-frameworks-comparison.md`](orm-frameworks-comparison.md) | Srovnání tří .NET ORM, které převodník zpracovává — NHibernate, EF Core, Dapper. Členěné tematicky, ne po frameworcích, protože tak je členěná i analytická kapitola; u každého tématu sleduje expresivitu, syntaxi a implicitní defaulty, které musí parser materializovat. |
| [`tutorials/nhibernate-getting-started.md`](tutorials/nhibernate-getting-started.md) | NHibernate od nuly: doména `Author` 1:N `Book`, mapování v `hbm.xml`, konfigurace v kódu, schéma přes `SchemaExport`. |
| [`tutorials/efcore-getting-started.md`](tutorials/efcore-getting-started.md) | EF Core od nuly, táž doména a totéž číslování kroků. |
| [`tutorials/dapper-getting-started.md`](tutorials/dapper-getting-started.md) | Dapper od nuly, táž doména a totéž číslování kroků. |
| [`tutorials/hibernate-getting-started.md`](tutorials/hibernate-getting-started.md) | Hibernate od nuly — první javový díl. Odděluje, co je jinak kvůli Javě a co kvůli Hibernate; to rozlišení je pro F10 podstatnější než kterýkoli jednotlivý rozdíl. |

Čtyři tutoriály mají **záměrně shodnou doménu i číslování kroků**, aby se daly položit vedle sebe a číst po řádcích. Hibernate díl popisuje framework, který nástroj zatím nepřekládá (F7–F10); je to příprava javové větve, ne popis implementovaného stavu.

Starší poznámky z rešerše před převzetím projektu leží v `notes/` v kořeni repozitáře. Jsou zmražené ve stavu k převzetí a proti zafixovaným verzím ověřené nejsou — na rozdíl od dokumentů zde.
