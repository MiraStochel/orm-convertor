# 210 — Vlastnost typu GUID nese doménová třída MyBatisu jako `String`, protože MyBatis pro `java.util.UUID` nemá type handler

Datum: 2026-11-09
Stav: platí
Požadavky: F8, F10, F11, T3
Podklad: rozhodnutí [040](040-boundary-of-the-handed-over-artifact.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md), [104](104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md), [208](208-sede-as-the-fourth-reference-domain-over-the-dba-stack-exchange-dump-judged-by-a-judge-of-our-own.md) a [212](212-a-property-whose-getter-would-be-getclass-is-a-java-member-with-an-underscore-and-a-mybatis-query-takes-the-result-map-of-its-entity.md); [záznam běhu SEDE 2026-11-08](../runs/2026-11-08-sede-2.md)

## Kontext

Sloupec `uniqueidentifier` nese mezireprezentace jako skalár `Guid` a javové buildery ho píšou jako `java.util.UUID`. Hibernate 7.4.5 i EclipseLink 5.0.0 typ mapují samy. [Běh SEDE z 2026-11-08](../runs/2026-11-08-sede-2.md) ukázal, že MyBatis ne: každý mapper nad tabulkami `PostHistory` a `SuggestedEdits`, které mají sloupec `RevisionGUID`, odmítl MyBatis už při čtení XML („No typehandler found for property RevisionGUID") a 107 buněk skončilo jako *nezkompilováno*.

Fakta jsme změřili nad připnutou verzí MyBatis 3.5.19 a ovladačem mssql-jdbc 13.4.0 (javová sada, `mybatis/ResultMappingClaimsTest`, nad literálem `CONVERT(uniqueidentifier, …)`):

| Tvar | Výsledek |
|---|---|
| vlastnost `UUID` v `<result>` uzavřeného `<resultMap>` | mapper **odmítnut** při stavbě továrny: „No typehandler found for property Ref" |
| vlastnost `UUID` pod `resultType` (automatické mapování) | dotaz proběhne, vlastnost zůstane **`null` bez jediného slova**; nemapuje-li v řádku nic jiného, není ani instance — řádek je `null` |
| vlastnost `String` v `<resultMap>` i pod `resultType` | text hodnoty, `6F9619FF-8B86-D011-B42D-00C04FC964FF` |
| `resultType="map"` | hodnota přijde jako `String`, týž text |
| parametr `UUID` v `#{x}` | naváže se a porovnání s `uniqueidentifier` platí |

Registr type handlerů MyBatisu 3.5.19 `UUID` nezná. Ovladač hodnotu `uniqueidentifier` předává jako text (`getString`, `getObject` → `String`), a to je jediná podoba, ve které ji MyBatis bez cizí pomoci přečte. Parametr handler nepotřebuje: neznámý typ MyBatis váže jako objekt a ovladač `UUID` přijme.

## Zvažované varianty

1. **Vygenerovat type handler** — třídu `BaseTypeHandler<UUID>` (třeba vnořenou v entitě) a v `<result>` ji jmenovat atributem `typeHandler`. Mapování entity by tím prošlo, ale jen tam, kde handler stojí v atributu: dotaz, který entitu materializuje automatickým mapováním, by vlastnost dál nechal `null`, protože automatické mapování hledá handler v registru konfigurace — a konfiguraci (`mybatis-config.xml`, `<typeHandlers>`) nevydáváme, je to fakt projektu konzumenta (rozh. 040). Doménová třída by navíc nesla import frameworku, který MyBatis jinak nežádá (rozh. 084: POJO bez importu z frameworku). Zamítáme: vzniká nový druh artefaktu, který opraví jen polovinu cest.
2. **Vlastnost vynechat z mapování se záznamem** jako kolekci bez vztahu (rozh. 178). Zamítáme: hodnota, kterou MyBatis přečíst umí, by se ztratila celá.
3. **Nést hodnotu v doménové třídě jako `String`** — text, který ovladač předává.

## Rozhodnutí

**Volíme variantu 3.** `MyBatisEntityBuilder` píše vlastnost skaláru `Guid` jako `String` — pole, getter i setter, klíčovou část stejně — a hlásí to záznamem `Loss` na artefaktu `JavaEntity` bez kategorie: typ `UUID` se nepřenesl, hodnota ano, jako text. `jdbcType` se pro rodinu `Uuid` nepíše jako dosud (rozh. 084, `MyBatisJdbcType`) a jeho ztrátu hlásí dosavadní záznam s kategorií `DatabaseType`. Text čte MyBatis pod uzavřeným `<resultMap>` entity, pod `resultType` i do mapy (`resultType="map"`, rozh. 104) stejně, takže jedno pravidlo pokrývá všechny cesty, kterými MyBatis řádek materializuje.

Parametry dotazu se nemění: parametr typu `Guid` zůstává v deklaraci metody mapperu `UUID`, protože se změřeně naváže, a vazba na `String` by vyžadovala, aby SQL Server převedl text na `uniqueidentifier` sám.

Fakta drží `mybatis/ResultMappingClaimsTest` javové sady (odmítnutí `<result>`, `null` pod `resultType`, text v `String`, vazba parametru), takže verze MyBatisu, která handler pro `UUID` přidá, selže tam; zápis drží `Combined/CatalogRunDefectTest`.

## Důsledky

- **Mapper nad tabulkou se sloupcem `uniqueidentifier` MyBatis přijme** a 107 buněk běhu SEDE, které se nezkompilovaly, se přeloží; hodnota přijde ve tvaru, v jakém ji vrací text přes JDBC, takže se s ním i srovná.
- **Překlad do MyBatisu a zpět typ `Guid` ztrácí**: parser MyBatisu čte `String` a nástroj typ neodhaduje. Je to vyslovená mez cíle, hlášená záznamem; [`subset.md`](../subset.md) ji vede v záporné polovině.
- **Hodnota je text velkými písmeny** (`6F9619FF-…`), jak ho vrací ovladač; konzument, který potřebuje `UUID`, ho převede `UUID.fromString`.
- **Dotčená místa:** `MyBatisEntityBuilder.AppendField`; `architecture.md` (zápis MyBatisu), `subset.md` (záporná polovina), `traceability.md` (F8).
