# 045 — Převod, ze kterého nic nevyšlo, to musí říct

Datum: 2026-08-23
Stav: platí
Požadavky: F11, F14, S6, S7
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [004](004-unexpressible-facts-as-warnings.md) a [025](025-query-language-as-content-type.md); nález testů podle rozhodnutí [043](043-rest-contract-guarded-over-http.md)

## Kontext

`/convert` odpovídá **200 s prázdným `sources` i prázdným `records`**, když ze vstupu nešlo nic přečíst. Našly to testy přes HTTP (rozhodnutí [043](043-rest-contract-guarded-over-http.md)) ve dvou podobách:

- jednotka, jejíž obsah parser přečíst neumí — `"this is not C#"` Roslyn rozparsuje, žádnou třídu v ní nenajde, takže nevznikne jediná `EntityMap` a builder nemá co vydat;
- prázdný seznam `sources`, kde se nestane nic už z principu.

V obou případech dostane volající **artefakt žádný a důvod žádný**. To je přesně to ticho, kterému mají rozhodnutí [010](010-diagnostics-as-returned-data.md) a [004](004-unexpressible-facts-as-warnings.md) předcházet: diagnostika je vrácená data a náhražka se negeneruje mlčky. Prázdná odpověď bez záznamu není ani jedno — je to tvrzení „hotovo" o něčem, co se nestalo.

**Tuhle třídu vady projekt jednou už chytil, o patro výš.** `ParserFactory` nese komentář, který to říká doslova: prázdný seznam parserů „produced an empty result and no error at all, so a bad source framework looked like a source with no entities". Tehdejší náprava byla výjimka u neznámého zdrojového frameworku. Zbylé ticho je táž rodina o úroveň níž — vstup je čitelný jako požadavek, jen z něj nic nevyplynulo.

**Dotazová větev přitom pravidlo má a entitní ne.** Rozhodnutí [025](025-query-language-as-content-type.md) v orchestraci vyslovilo, že nevyplněná jednotka není tvrzení, takže prázdný dotazový vstup se přeskočí bez záznamu, kdežto neprázdný, který nikdo neumí přečíst, je `Failure`. Entitní strana žádnou takovou větu nemá — a rozdíl mezi oběma větvemi je historický, ne zvolený.

## Zvažované varianty

1. **Odpovědět 400.** Láká to, protože „nic nevyšlo" vypadá jako vada požadavku. Jenže to jde proti rozhodnutí [010](010-diagnostics-as-returned-data.md): převod, který přečetl tři jednotky ze čtyř, musí vydat, co vyrobil, a stavový kód neunese důvod u jednotlivé jednotky. Zároveň by se částečný úspěch stal nerozeznatelným od úplného selhání, což je u dávkového vstupu podle F14 to horší z obojího. Zamítáme.

2. **Vyhodit výjimku z orchestrace.** Táž námitka a navíc přímý rozpor s rozhodnutím 010, které se pro vrácená data proti výjimkám rozhodlo vědomě. Zamítáme.

3. **Nechat a popsat jako známou mezeru v `open-items.md`.** F11 žádá validaci a strukturovanou diagnostiku právě tohohle druhu a případ je levný na ohlášení; odložit ho by znamenalo psát položku tam, kde stačí záznam. Zamítáme.

4. **Záznam typu `Failure`, stav 200 zůstává.**

## Rozhodnutí

**Volíme variantu 4 a zapisujeme pro entitní větev totéž pravidlo, které dotazová větev má od rozhodnutí 025.** Jsou z toho dvě věty:

- **Neprázdná jednotka v jazyce, který zdrojový framework nečte, je `Failure`** s uvedením toho jazyka. Dosud propadla bez povšimnutí, protože smyčka parserů si prostě vybírá, co `CanParse` přijme, a na zbytek se nikdo neptá.
- **Převod, ze kterého nevyšel jediný artefakt, vydá `Failure` o sobě jako celku.** Důvod rozlišuje dva případy, protože pro volajícího to jsou dvě různé zprávy: buď nepřišla žádná jednotka, nebo přišly a žádná nic nevydala.

**Prázdná jednotka se dál přeskakuje bez záznamu** — nevyplněné vstupní pole není tvrzení (rozhodnutí 025) a hlásit ho by znamenalo zaplevelit záznamy tím, co uživatel právě nevyplnil.

**Domovem obou pravidel je orchestrace, ne wrapper.** Je to tvrzení o běhu jako celku a o tom, co s jednotkou udělal výběr parserů; wrappery se nemění a S1 zůstává nedotčené — táž úvaha, jakou pro dotazovou stranu udělalo rozhodnutí 025.

**Co tím pokryté není a proč:** jednotka, která nic nevydá, zatímco jiná jednotka téhož běhu vydá artefakt. Rozeznat ji počítáním `EntityMaps` před a po každém parseru nejde — u NHibernate parser XML mapování obohacuje entitu, kterou už vytvořil parser třídy, takže žádnou novou mapu nepřidá a poctivá jednotka by dostala nepoctivý záznam. Poctivá atribuce po jednotkách vyžaduje, aby parser hlásil, co z které jednotky přečetl, což je zásah do rozhraní parserů; nese ho vlastní položka v [`open-items.md`](../open-items.md).

## Důsledky

**Stavový kód se nemění a tvar odpovědi taky ne**, takže tohle rozhodnutí samo o sobě MAJOR nezakládá. Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR — přibývá diagnostický záznam tam, kde dřív žádný nebyl, a dosavadní artefakty zůstávají beze změny. Vydání, ve kterém to vyjde, bude MAJOR z jiného důvodu (rozhodnutí [044](044-error-response-as-problem-details.md)).

**Vstup, který dřív vypadal jako úspěch, teď vypadá jako selhání — a je to tak správně.** Kdo posílal nečitelnou jednotku a dostával prázdno, dostane napříště důvod; kdo se na prázdnou odpověď spoléhal jako na „nic k překladu", má nově záznam, který to říká.

**Frontend nepotřebuje zásah.** Záznamy zobrazuje tak, jak přijdou (rozhodnutí [033](033-shape-of-the-static-frontend-screens.md)), takže nový `Failure` se objeví v témž seznamu jako ostatní.
