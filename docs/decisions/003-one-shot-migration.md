# 003 — Jednorázový přepis místo přechodného období

Datum: 2026-07-16
Stav: platí

## Kontext

Přestavba mezireprezentace (podmínkový strom, kompozitní klíče, vztahy na úrovni entity) mění API, které používají všechny tři wrappery. Je třeba zvolit, jak migraci provést.

## Zvažované varianty

1. **Postupně** — staré API dočasně `[Obsolete]`, běží vedle nového.
2. **Jednorázovým přepisem** všech wrapperů najednou, v rámci každého implementačního kroku.

## Rozhodnutí

**Jednorázový přepis, žádné `[Obsolete]`.**

Na projektu pracuje jeden člověk, IR API nemá žádné konzumenty mimo tento repozitář a aplikace smí být během přestavby dočasně nefunkční. Souběh starého a nového modelu by za těchto podmínek nepřinášel hodnotu, jen náklady: adaptéry mezi oběma reprezentacemi, dvojí testovací matici (každý scénář proti starému i novému API) a mrtvý kód s `[Obsolete]` atributy, který by se po pár týdnech stejně mazal.

## Důsledky

Roli záchranné sítě přebírá git a testy. Každý implementační krok se dokončí včetně úprav testů a commitne jako funkční milník; CI na `main` dá zpětnou vazbu po každém pushi. Případný červený stav mezi milníky znamená „rozpracováno", ne problém.

Implementační kroky je proto nutné řezat tak, aby byl každý dokončitelný jako jeden ohraničený přepis.
