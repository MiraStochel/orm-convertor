# 007 — Dokumentace organizovaná podle rozhodnutí, ne podle času

Datum: 2026-08-03
Stav: platí
Požadavky: žádné
Podklad: audit 2026-08-02, nález 3.5

## Kontext

Dosavadní systém dokumentace byl organizovaný podle času: zmražený snímek stavu, časová osa změn v changelogu a datované audity. Aktuální stav se z něj skládal replayem — konvence zněla „stav = `current-state.md` + changelog".

Tenhle tvar neobsluhoval to, co od dokumentace ve skutečnosti potřebujeme, totiž **stopu rozhodnutí**: kdy padlo, proč, a jestli se v čase měnilo.

- Rozhodnutí neměla datum ani stav. Že rozhodnutí o formě kompozitního klíče vzniklo o dva týdny později než zbytek design docu 001, šlo zjistit jen z changelogu nebo z git historie.
- Neexistoval pojem „nahrazeno". Revize rozhodnutí by znamenala přepsat původní text, a s ním i úvahu, proč tehdy dával smysl.
- Skládání stavu replayem selhalo v praxi: audit 2026-08-02 zjistil (nález 3.5), že z dvojice `current-state.md` + changelog nešlo poznat, že design doc 002 vůbec existuje.
- Design doc navíc mísil tři žánry — motivaci, rozhodnutí a plán kroků. Rozhodnutí jsou trvalá, plán zastarává s každým dokončeným krokem, takže dokument jako celek stárnul rychleji než jeho nejcennější část.

## Zvažované varianty

1. **Systém zachovat a doplnit pravidlo**, že každá změna včetně vzniku nového design docu musí mít záznam v changelogu. Tuto cestu otevírala oprava O5 z auditu.
2. **Přeorganizovat dokumentaci podle žánru** a časovou osu zrušit.

## Rozhodnutí

**Varianta 2.** Dokumentace se člení podle toho, co který dokument je, ne kdy vznikl:

| Dokument | Role | Životní cyklus |
|---|---|---|
| `requirements.md` | zadání vedoucího | zmražené |
| `baseline.md` | stav při převzetí projektu | zmražené |
| `architecture.md` | jak nástroj funguje dnes | živé, aktualizuje se se změnou chování |
| `open-items.md` | co zbývá — rozhodnutí i práce | živé, položka mizí, jakmile je hotová |
| `decisions/NNN-*.md` | jedno rozhodnutí = jeden soubor | neměnné, mění se jen pole `Stav` |
| `audits/RRRR-MM-DD-*.md` | datovaná revize stavu | neměnné |
| `analysis/` | tutoriály a srovnání frameworků | přibývá podle potřeby |

Rozhodnutí se **nepřepisují**. Změna názoru znamená nové rozhodnutí a stav `nahrazeno NNN` u původního; obojí zůstává čitelné vedle sebe. Každé rozhodnutí nese datum, stav a vazbu na požadavky F/S/T.

Varianta 1 řešila jen bezprostřední příznak. Kdyby platila, changelog by dál nesl tři různé role najednou — čím se změnilo, proč, a co teď platí — a každou hůř než dokument určený právě jí.

Zaniká changelog a kategorie `design/`:

- **Changelog** dělal tři práce a všechny mají lepší domov. „Co se změnilo kdy" umí git historie mechanicky a zadarmo. „Proč" patří do rozhodnutí. „Co teď platí" patří do `architecture.md`. Jako čtvrtý opis si navíc vynucoval replay, aby z něj šel stav složit.
- **Design docy** se rozebraly: rozhodnutí do `decisions/`, popis výsledného modelu do `architecture.md`, plán kroků a neimplementované části do `open-items.md`. Analýza variant zůstává uvnitř příslušného rozhodnutí.

## Důsledky

Stav se přestává skládat replayem a čte se ze dvou živých dokumentů: `architecture.md` odpovídá na „jak to funguje", `open-items.md` na „co zbývá". Tím padá i šestikrokový postup, kterým se stav dosud rekonstruoval.

Disciplína se přesouvá, nezmenšuje: místo připsání řádku do changelogu se po dokončení úkolu aktualizuje `architecture.md` a škrtne položka v `open-items.md`. Rozdíl je v tom, že když to někdo neudělá, je dokument rovnou nesprávný, ne jen neúplný. Je to vědomá výměna — nesprávnost je vidět, neúplnost se pozná až při rekonstrukci.

Pole `Požadavky` u rozhodnutí dává zpětnou dohledatelnost F/S/T → rozhodnutí, kterou text práce potřebuje tak jako tak.

Cena, kterou tím platíme: design doc 001 měl souvislý výklad „dotazový model a kompozitní klíče" a rozebráním se rozpadl na šest samostatných rozhodnutí. Souvislý příběh se skládá až při psaní práce z `architecture.md` (jaký model vznikl) a z rozhodnutí (proč zrovna takový). Přijali jsme to proto, že ten výklad už tak jako tak mísil hotové s plánovaným a s každým dalším krokem by zastarával dál.

Audity zůstávají jako vlastní kategorie, ne jako součást `analysis/`. Jsou to datované revize s vlastním životním cyklem — nález vede k opravě, k rozhodnutí, nebo k položce v `open-items.md` — kdežto `analysis/` je referenční materiál pro text práce. Duplicita mezi seznamem `O`/`R`/`Ú` v auditu a `open-items.md` je záměrná a neškodná: audit říká, co bylo otevřené k danému datu, `open-items.md` co je otevřené teď.
