# 157 — Buňka cíle, která se od zdroje liší vyslovenou mezí, vznik soudce FIBEN neblokuje; záznam běhu ji jmenuje

Datum: 2026-10-19
Stav: platí; nahrazuje první podmínku odstavce *Soudce* rozh. 149; pro q99, q124–126, q200–202 a q237 ji nahrazuje 163
Požadavky: T2, T3, F13
Podklad: rozhodnutí [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (odstavec *Soudce*), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [040](040-boundary-of-the-handed-over-artifact.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md); [`subset.md`](../subset.md) 2.2 (ořez `CHAR` v EclipseLinku); záznam *The FIBEN run* v `ORMConvertor/README.md` (běh z 2026-10-18, q63)

## Kontext

Rozhodnutí 149 dává soudci FIBEN vzniknout z prvního běhu, který splní tři podmínky; první z nich je, že *zdroj i všech pět cílů se nad daty shodnou*. Shoda šesti frameworků je důkaz, že řádky zdroje, které soudce zmrazí, jsou řádky dotazu, a ne chyba jednoho čtení.

Běh z 2026-10-18 ukázal buňku, která se shodnout nemůže: q63 v EclipseLinku vrací `"YES"`, kde zdroj a ostatní cíle vracejí `"YES  "`. EclipseLink 5.0.0 při čtení ořeže koncové mezery hodnoty sloupce pevné šířky (`CHAR(n)`); řídí to nastavení platformy `shouldTrimStrings`, které patří relaci konzumentského projektu (040), a [`subset.md`](../subset.md) 2.2 to vede jako vyslovenou mez: artefakt nastavení nevyslovuje a kanonický tvar čtvrtého stupně (089) mezery neořezává, takže rozdíl zůstává neshodou. Podmínka 149 s takovou buňkou nepočítala a doslova vzato ji nesplní žádný běh — soudce by nevznikl nikdy.

## Zvažované varianty

1. **Ořezat hodnoty `CHAR` v kanonickém tvaru.** Neshoda by zmizela, ale kanonický tvar by přestal porovnávat hodnoty, které konzument dostane: `"YES"` a `"YES  "` jsou pro aplikaci různé řetězce. Zamítáme.

2. **Vynechat q63 ze soudce.** Řádky zdroje q63 jsou správné — shodují se s ostatními čtyřmi cíli — a soudce drží řádky zdroje, ne cíle. Vynechání by ubralo dotaz, který nic neohrožuje. Zamítáme.

3. **Neshoda, kterou plně vysvětluje vyslovená mez, podmínku neruší.** Buňka, jejíž rozdíl je přesně případ řádku `subset.md` (konstrukce, důvod, co nástroj udělá), není neshoda překladu, nýbrž zapsaná vlastnost cíle; zbylé čtyři cíle a zdroj shodu dokládají dál. Tuhle volíme.

## Rozhodnutí

**Volíme variantu 3. První podmínka vzniku soudce zní: zdroj a všech pět cílů se nad daty shodnou, kromě buněk, jejichž rozdíl je případem vyslovené meze v [`subset.md`](../subset.md); takové buňky jmenuje záznam běhu i s řádkem meze.** Neshoda, kterou žádná mez nevysvětluje, podmínku dál ruší, a vada (kterou mez není) také. Druhá a třetí podmínka 149 i věta, že nový soudce vzniká jen novým rozhodnutím, platí beze změny.

## Důsledky

- **Soudce FIBEN** vznikne z běhu obou sad po opravách rozhodnutí 155 a 156 ([`open-items.md`](../open-items.md)); q63 v EclipseLinku záznam jmenuje s mezí `subset.md` 2.2.
- **Kód** se nemění: běh porovnává jako dosud a neshodu vykazuje; posouzení, zda ji mez vysvětluje, patří záznamu.
- **Dokumentace.** Stav rozhodnutí 149 a jeho řádek v rejstříku (150); `architecture.md` §6.2.
