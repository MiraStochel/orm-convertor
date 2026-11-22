# 219 — Data LDBC se berou jen od LDBC: vydání `data_sets` nese jen archiv FIBEN, sestavení se na vlastní kopii dat LDBC neptá a profil `test` stojí na archivu LDBC ověřeném součtem v repozitáři

Datum: 2026-11-21
Stav: platí; nahrazuje kopii dat LDBC a pořadí adres pro ně z rozh. 215 a z rozh. 218 to, co projekt drží a na čem stojí profil `test`
Požadavky: S5, T1
Podklad: rozhodnutí [110](110-ldbc-snb-as-a-second-reference-domain.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [186](186-the-build-environment-is-pinned-on-content-lock-files-image-digests-and-action-commits-moved-by-dependabot.md), [215](215-the-database-image-takes-a-data-set-from-the-projects-own-copy-first-and-checks-it-against-a-committed-checksum.md) a [218](218-data-sets-the-project-does-not-hold-stay-foreign-sources-of-the-build-each-has-a-switch-and-the-test-profile-rests-on-none.md); `database.Dockerfile`, `database/datasets.sha256`

## Kontext

Rozhodnutí 215 připojilo k repozitáři vydání `data_sets` s kopiemi archivů, „jejichž licence šíření dovoluje", a rozhodnutí 218 na něm postavilo profil `test`: vydání neslo data LDBC SNB Interactive v1 scale factoru 0.1 s validační sadou a archiv FIBEN, a sestavení se na vlastní kopii ptalo dřív než na vydavatele.

Předpoklad o licenci je doložený jen u FIBEN: `data.zip` pochází z repozitáře pod Apache-2.0 a text licence je k vydání přiložen. U dat LDBC takový doklad nemáme — licenci, pod níž lze archivy z `datasets.ldbcouncil.org` šířit dál, se nám nepodařilo jednoznačně určit. Kopie dat, u nichž právo je šířit není jisté, do veřejného vydání nepatří, i kdyby sestavení dělala nezávislým na cizí adrese.

Je třeba rozhodnout, odkud se data LDBC berou, co z rozhodnutí 215 a 218 pro ně zůstává a co se tím mění na důkazu S5.

Změřeno 2026-11-21: archiv `social_network-sf0.1-CsvMergeForeign-StringDateFormatter.tar.zst`, jak ho podává `datasets.ldbcouncil.org`, má SHA-256 `c2b07d8c…a4f6`, tedy součet, který `database/datasets.sha256` nese od rozhodnutí 215. Kopie ve vydání byla soubor vydavatele beze změny.

## Zvažované varianty

1. **Kopii nechat.** Profil `test` by dál nestál na adrese vydavatele datové sady. Cenou je šíření dat bez doloženého práva; nejistota se nedá vyvážit tím, co kopie přináší. Zamítáme.
2. **Kopii z vydání odebrat a sestavení nechat, jak je.** Pořadí adres rozhodnutí 215 by platilo beze změny: sestavení by se u dat LDBC nejdřív zeptalo vydání, dostalo 404 a šlo k LDBC. Každé sestavení by se tak ptalo na místo, o němž jsme rozhodli, že na něm nic nebude, a soubor, který by se tam znovu objevil, by sestavení mlčky vzalo místo souboru vydavatele. Zamítáme.
3. **Odebrat i kopii FIBEN a vydání zrušit.** FIBEN vydavatele nemá — repozitář `IBM/fiben-benchmark` z GitHubu zmizel — a jeho licence je doložená; bez kopie by databáze `Fiben` v čistém prostředí nevznikla vůbec. Zamítáme.
4. **Data LDBC jen od LDBC, bez místa pro vlastní kopii; součet archivu scale factoru 0.1 zůstává v repozitáři.**

## Rozhodnutí

**Volíme variantu 4.**

- **Co projekt drží.** Vydání `data_sets` nese jen archiv FIBEN s textem jeho licence. Přílohy s daty LDBC a s validační sadou se z vydání odstraňují.
- **Data LDBC mají jedinou adresu.** `database.Dockerfile` bere data každého scale factoru i validační sadu z `datasets.ldbcouncil.org` a na `DATASETS_URL` se pro ně neptá. Místo pro vlastní kopii má sestavení jen u sad, u nichž kopie smí vzniknout; u ostatních sad (FIBEN, výpis SEDE, záloha WideWorldImporters) platí pořadí adres rozhodnutí 215 a 218 dál.
- **Součet zůstává.** `database/datasets.sha256` dál jmenuje archiv dat scale factoru 0.1, takže data profilu `test` jsou připnutá na obsah stejně jako před tímto rozhodnutím: jiný obsah pod stejným jménem je selhané sestavení. Seznam tím nese i soubor, který projekt nedrží — součet je údaj o souboru, ne jeho kopie. Řádek validační sady zaniká: jmenoval soubor, který jsme z archivu LDBC vybrali a zabalili sami, a ten už neexistuje.
- **Validační sada** se bere krokem, který byl před rozhodnutím 215 jediný a po něm náhradní: z archivu LDBC pro scale factory 0.1 až 10 si obraz nechá soubor svého scale factoru. Bere se, jak přišla.
- **Když sada není,** platí pravidlo rozhodnutí 218 beze změny: sestavení skončí chybou, která jmenuje sadu a přepínač `LDBC_SCALE_FACTOR=none`.

## Důsledky

- **Profil `test` stojí na adrese LDBC.** Vedle základních obrazů a balíčků, které rozhodnutí 186 připíná na obsah a nedrží, je to další cizí zdroj téhož druhu: data připnutá součtem, validační sada nepřipnutá. Zmizí-li adresa, profil `test` se v čistém prostředí nesestaví, protože soudce katalogu LDBC je jeho součástí (rozh. 117); systém se sestaví s `LDBC_SCALE_FACTOR=none`. Věta rozhodnutí 218, že profil `test` nestojí na žádné adrese vydavatele datové sady, neplatí; S5 zůstává nárokované v užším rozsahu.
- **`LDBC_SCALE_FACTOR=0.1` už není cesta k `LdbcSnb` z vlastní kopie.** Je to jen menší archiv téhož vydavatele.
- **Vydání `data_sets` se mění, ačkoli rozhodnutí 215 řeklo, že se přílohy nemažou.** Sestavení značky `2.4.1` to nerozbije: ptá se vydání, dostane 404 a pokračuje k LDBC — data ověří proti součtu, který nese, a validační sadu vezme náhradním krokem. V čistém prostředí to ověřené není.
- **Existující svazek se nemění.** Jména datových sad (`ldbc.dataset`, `ldbc.validation`) zůstávají, takže se nic znovu nenačítá.
- **Dotčená místa:** `database.Dockerfile`, `database/datasets.sha256`, `database/fetch-dataset.sh` (komentář), `docker-compose.yml` (komentář u `test_db`); `ORMConvertor/README.md` (*External dependencies of the build*, tabulka proměnných), kořenové `README.md`, `architecture.md` (§6.2 a tabulka databáze LDBC), `traceability.md` (S5).
