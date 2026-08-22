Apap# Odkazy
GitHub: https://github.com/CollaboratingPlatypus/PetaPoco
NuGet: https://www.nuget.org/packages/PetaPoco.Compiled/
Dokumentace: https://github.com/CollaboratingPlatypus/PetaPoco/wiki ([OLD](https://github.com/CollaboratingPlatypus/PetaPoco/wiki/Old-Documentation))

# Úvod
Hodně malé ORM, rychlé a jednoduché.
Jednoduché na použití.
Dříve bývalo v jednom souboru.
# Distribuce a vydání
## Licence
Open-source

[Apache 2.0](http://www.apache.org/licenses/LICENSE-2.0)
> A permissive license whose main conditions require preservation of copyright and license notices. Contributors provide an express grant of patent rights. Licensed works, modifications, and larger works may be distributed under different terms and without source code.
## Cena
zdarma
## Dostupnost
Source code - GitHub
Balíček - NuGet
## Počet stáhnutí
Total - 1 375 389
Poslední rok = 1 375 389 - 848 876 = 526 513
## Release cyklus
Nepravidelný podle chyb a PR.
Na NuGetu, častěji.
Semantic Versioning + beta
Aktuální verze (7.10.): [6.0.683](https://www.nuget.org/packages/PetaPoco.Compiled/6.0.683) 20.9.2024
# Komunita a podpora
## Udržovanost
101 issues, 374 closed
## Dokumentace
Dokumentace je integrovaná v repu jako GitHub wiki. Několik stránek.
Quickstart, popis všech funkcionalit, případně vytháhnutá stará dokumentace, která se nestihla ještě zmigrovat.
Quickstart ukazuje pochopitelně jak nainstalovat, nastavit a zvolit DB.

Není popsáno veškeré API, ale prý je samodokumentující.
## Podpora komunity
nějaké otázky na SO, github issues
ne moc
## Podpora verzí .NET
.NET (compatible 8)
.NET Core (compatible)
.NET Standard(included 2.1)
.NET Framework (compatible 4.6, included 4.5)
## Enterprise verze
NE
## Rozšíření
oficiální NE
komunitní:
https://github.com/asherber/PetaPoco.SqlKata - Lepší SQL builder - staví sql query string

https://github.com/asherber/StaTypPocoQueries.PetaPoco - rozšířenější linq podpora

https://github.com/hippasus/PetaPoco.DBEntityGenerator - podle databáze vygeneruje objekty do kódu

https://github.com/schotime/NPoco - fork který je docela populární - další features jako LINQ a tak
## Vývojové nástroje
NE
# Databázová podpora
## Podpora systémů a databází
SQL Server, SQL Server CE, MS Access, SQLite, MySQL, MariaDB, PostgreSQL, Firebird DB, and Oracle
## Vlastnosti specifické pro DB
generuje specifické SQL přes DB providery, ale specifikcé featury asi ne
# Konfigurace a schéma
## Konfigurace
přes connection string

má taky fluent configuration - několik konstruktorů
přes fluent kód lze nastavit 
## Schéma DB
Schéma není v kódu, ale v databázi. Lze využít externí DB projekt.
Při změně DB potřeba přepsat SQL dotazy případně namapované objetky.
PřeNs rozšíření si lze nechat vygenerovat objekty do kódu reflektující databázi.
## Migrace a verzování
NE
# Mapování
## Mapování entit a vztahů
### Entity
POCOs 
Automatické mapování podle názvu properties.
- automatické mapování nenamapuje ID a další důležité věci, nedoporučuje se
Lze specifikovat konvence (přidání podtržítek, změna velikosti písma atd).
Případně lze přes atributy sepcifikovat název tabulky, sloupců, primární klíč atd.

### Vztahy
Nelze explicitně definovat. Je potřeba napsat SQL dotaz s JOINem. Pak lze ale výsledek rozdělit do více tříd. A lze specifikovat, jak je spojit. Jako i Dapperu..
```cs
var sql = PetaPoco.Sql.Builder
	.Append("SELECT articles.*, users.*")
	.Append("FROM articles")
	.Append("LEFT JOIN users ON articles.user_id = users.user_id");

var result = db.Query<article, user, article>((a,u) => { a.user=u; return a }, sql);
```
## Podpora datových formátů a typů
Stejně very jako u Dapperu - nepodporuje, ale jelikož se píšou dotazy v čistém SQL, tak pokud to umí DB lze se na ně teoreticky dotázat.
## Kolekce
NE
# Dotazování a databázové operace
## Podpora a pokrytí dotazování
Pokud generujeme dotazy z objektů, tak se generuje SQL.
Pro složitější dotazy je potřeba SQL psát ručně.

Velice slabá podpora pro LINQ - Exists, Single(OrDefault), First(OrDefault)
Rozšíření přidává pár dalších- `Query()`, `Fetch()`, `Page()`, `SkipTake()`, `Single()`, `SingleOrDefault()`, `First()`, `FirstOrDefault()`, and `Delete()` - které v nich umožnuje typované "lambda" funkce
## DDL a DML
DDL - Nelze, jen pokud napíšeme čisté SQL
DML - ANO. lze vytvářet objekty v aplikaci a pak je předat metodám jako Insert, Save, Update, Delete. Těm dáme POCO objekt nebo přes parametry specifikujeme id, tabulku atd
## Batch operace
Batch transkace ano, data přenášena po jednom.
Bulk nelze pokud nenapíšeme INSERT do SQL.
## Change tracking
NE
## Seed data
NE
## Podpora transakcí
Zase přes ADO.NET + vlastní podpora zanořených transakcí
## Fetch strategie
NE
## Caching
NE
# Ostatní
## Logování 
Přes `LastSQL`, `LastArgs`, `LastCommand` lze získat poslední vykonané sql

## Asynchronní programování
Async varianty metod
# Závěr
## Shrnutí
Oproti Dapperu má trošku lepší dotazy, které můžou mít typované. Jednoduché CRUD není potřeba psát v parametrizovaném SQL, jsou typované. Ty lze doplnit do dapperu přes extension.

Dapper je ale populárnější. Má aktualizovanou dokumentaci a větší podporu.

