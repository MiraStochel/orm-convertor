# 065 — Hranicí pravidla o nevydaném dotazu je množina řádků

Datum: 2026-08-25
Stav: platí
Požadavky: F11, T2, T3, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md) a [061](061-subquery-as-a-condition-operand.md); otevřená položka „Meze pravidla ‚dotaz, který vrátí jiné řádky, se nevydá‘"

## Kontext

Rozhodnutí 053 zakázalo dosazovat náhražku za podmínku, kterou cíl vykreslit neumí: tautologie místo filtru vrací všechny řádky, které zdroj vyloučil, takže artefakt nevzniká a důvod je v záznamu. Své vlastní znění ale omezilo na podmínkový strom a operátor a samo přiznalo, že vedle nich stojí náhrady popsané jinde, na které jeho argument sedí doslova. Po rozhodnutích 060 (stránkování, které se nedá nést, je `Failure` bez artefaktu) a 061 (zahozený poddotaz přestal existovat) zbylo jediné takové místo: **plný vnější join se u EF Core i NHibernate vypisoval jako vnitřní** — zúžení uvnitř kategorie `JoinKind`, hlášené záznamem `Loss` v místě emise. Vnitřní join vrací *méně* řádků než plný vnější, takže výsledek je jiný, ne jen chudší; „dosazená náhražka není ztráta, je to jiný dotaz" platí i tady.

Do téže věty spadá i druhá náhrada v EF Core builderu, kterou položka nejmenovala: **join, jehož ON podmínka není konjunkcí rovností sloupců,** se zahazoval se záznamem `Loss` a artefakt vycházel bez něj. Dotaz bez svého joinu vrací jiné řádky dvojnásob — join filtruje i násobí.

Podstatný je přitom rozdíl mezi oběma cíli, které dnes zužují. **EF Core 10 plný vnější join vyjádřit umí**, jen ne jedním operátorem: má `LeftJoin`, `RightJoin` i množinové operace, a `FULL OUTER JOIN` je z učebnice právě jejich skladba — levý join sjednocený `UNION ALL` s řádky pravého joinu, které nenašly levý protějšek. **HQL v NHibernate 5.7.0 nemá ani plný vnější join, ani množinové operace** (deskriptor: `SetOperation` je `NotExpressible`), takže věrná skladba tam neexistuje.

## Zvažované varianty

1. **Ponechat hranici na podmínce (dnešní znění 053).** Uživatel by dál dostával artefakt se srozumitelným záznamem a matice T2 by v kategorii *druh joinu* dál měřila překlad. Jenže artefakt, který se přeloží, zkompiluje, spustí a odpoví jinou množinou řádků, je přesně ten tichý falešný pozitiv pro T3, kvůli kterému 053 vzniklo — a po rozhodnutích 060 a 061 by byl join jediným přeživším svého druhu, což se v textu práce nedá obhájit jinak než přiznáním nekonzistence. Zamítáme.

2. **Množina řádků jako hranice, s odmítnutím všude.** Plný vnější join by byl `Failure` bez artefaktu v obou cílech. Konzistentní a levné, ale odmítalo by to i dotaz, který cíl vyjádřit umí: u EF Core věrná skladba existuje a odmítnout vyjádřitelný dotaz znamená vyměnit chybný překlad za žádný tam, kde je správný k mání. Matice T2 by měřila odmítnutí i v buňce, kde překlad existuje. Zamítáme.

3. **Množina řádků jako hranice; kde cíl umí věrnou skladbu, vydá se skladba, kde ne, `Failure` bez artefaktu.**

## Rozhodnutí

**Volíme variantu 3 a vyslovujeme mez, kterou si rozhodnutí 053 samo otevřelo: hranicí pravidla je množina řádků, ne podmínka.** Náhrada, po které dotaz vrací jiné řádky, se nevydává — bez ohledu na to, jestli stojí ve filtru, v operátoru, nebo v joinu. Rozhodnutí 053 se tím nemění, dostává širší předmět; mechanismus už existuje: `Failure` kdekoli na dotazovém kanálu ruší artefakt.

Konkrétně to znamená čtyři věci:

**EF Core skládá plný vnější join věrně z operátorů, které má.** Builder vypíše `LeftJoin` a k němu `Concat` pravého joinu zúženého na řádky bez levého protějšku:

```
zdroj.LeftJoin(pravá, klíče…)
     .Concat(zdroj.RightJoin(pravá, klíče…)
                  .Where(x => x.levá == null))
```

`Concat` je `UNION ALL`: spárované dvojice se nezdvojí, protože pravá větev je filtrem vyloučí, a skutečné duplikáty se nezhroutí, jak by je zhroutilo `Union`. Filtr stojí na kořenovém členu levé strany — ten v levé větvi nikdy null není (je to zdroj FROM), takže null jednoznačně značí řádek bez protějšku. Prefix řetězu (zdroj a dosavadní joiny) se v pravé větvi opakuje; stejně ho opakuje SQL, které `FULL OUTER JOIN` emuluje bez něj. **Věrný překlad není ztráta ani konvence, takže žádný záznam nevzniká** — a že skladbu provider skutečně přeloží (`LEFT JOIN … UNION ALL … RIGHT JOIN`), drží 3. stupeň ověření.

**NHibernate plný vnější join odmítá.** HQL 5.7.0 nemá z čeho skládat, takže je to `Failure` v místě emise a artefakt nevzniká — místo dosavadního `Loss` a vnitřního joinu, který vracel jiné řádky.

**Join bez klíčových rovností je u EF Core `Failure`, ne zahození.** LINQ join bere dva klíčové selektory a podmínka, která není konjunkcí rovností sloupců, do nich tvar nemá; dosud se join vypustil se záznamem `Loss`. Věrný tvar (`SelectMany` s podmínkou) by byl nová práce — bude-li o ni stát, dostane vlastní položku; dnešní volba jen přestává vydávat jiný dotaz.

**Dapper se nemění.** T-SQL má `FULL JOIN` i libovolnou ON podmínku, takže se ho hranice nedotkne.

## Důsledky

**Čtení T3 se v obou cílech narovnává.** Dotaz s plným vnějším joinem do EF Core se nově počítá jako přeložený a ekvivalentní (dřív přeložený a neekvivalentní); do NHibernate jako nepřeložený se záznamem (dřív falešně přeložený). Kategorie *druh joinu* v matici T2 u NHibernate v této buňce měří odmítnutí — a to je poctivé čtení, protože cíl ten dotaz beze změny odpovědi vyjádřit neumí; táž věta platí u množinových operací od rozhodnutí 060.

**Kanonický nárok přestává mít výjimku.** Věta „a query that would return a different set of rows is not emitted at all" v README platila s tichou výhradou joinu; teď platí doslova.

**Round-trip NHibernate → NHibernate s `full join` nově odmítá artefakt.** Vlastní HQL parser (rozhodnutí 062) čte `full join` dál — je to legitimní vstup pro překlad do cíle, který ho má (SQL) —, ale zpáteční směr skončí záznamem. Vstup je hypotetický: NHibernate sám takový HQL nespustí.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** vstup, který dřív vydal artefakt, ho buď nevydá a přibude záznam (NHibernate), nebo vydá jiný, věrný (EF Core); veřejné rozhraní ani tvar odpovědi se nemění.
