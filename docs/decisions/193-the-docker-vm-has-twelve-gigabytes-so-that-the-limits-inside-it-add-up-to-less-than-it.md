# 193 — Virtuální stroj Dockeru má 12 GB, aby se do něj vešel součet mezí všeho, co v něm běží současně

Datum: 2026-10-29
Stav: platí; nahrazuje 8 GB paměti virtuálního stroje v odstavci *Prostředí* a v důsledku *Hostitel MIS3* rozh. 145
Požadavky: S5, S6, T3
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [164](164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [190](190-a-point-of-the-limits-run-that-does-not-finish-within-thirty-minutes-is-hung.md); nezaznamenaný pokus o běh mezí z 2026-10-27/28 nad `fd27497` (níž, *Co se stalo*); `docker info` a `free -m` ve virtuálním stroji po změně z 2026-10-29

## Kontext

Rozhodnutí 145 zafixovalo virtuální stroj Dockeru na MIS3 na čtyři procesory a 8 GB bez swapu a nechalo uvnitř dvě meze, které jsou pro záznamy podstatné: SQL Server s `max server memory` 4096 MB (měřicí režim, rozh. 164) a kontejnery sad s `mem_limit` 6 GB. Každá mez sama sedí, jejich součet ne: server 4 GB a kus paměti mimo buffer pool, sada 6 GB, k tomu jádro a cache — přes 11 GB v stroji, který jich uvnitř hlásí 7,75. Dokud jedna ze stran zůstala daleko pod svou mezí, nebylo to vidět; experimentální běh z 2026-10-24, který potřeboval dva servery najednou, to obešel tím, že oba omezil na 2048 MB.

### Co se stalo

První pokus o záznam běhu mezí (rozh. 182 a 190) běžel nad `fd27497` hladce tři a čtvrt hodiny. Pak se sešly dvě těžké věci: SQL Server se opakovaně snažil sestavit plán dotazu s 256 a 512 joiny a vzdával to (chyba 8623, „query processor ran out of internal resources", v logu od 18:29 UTC) a sada současně kompilovala artefakt EF Core s 512 joiny. V 19:55:46 UTC server zapsal `Long Sync IO` — čtení z disku delší než sekunda —, ve stejné minutě přestal Docker odpovídat skriptu běhu, v 20:05 UTC SQL Server spadl a do rána psal výpis paměti. Procházka běžela dál bez databáze: 37 ze 77 procházek .NET půlky nad fixturou proběhlo nad mrtvým serverem a záznam z běhu nevznikl. Vyčerpání paměti ve stroji bez swapu je z toho úvaha, ne měření — jádro stroje svůj log nevydá —, ale jiný výklad toho, že se dvě strany pod svými mezemi společně dostaly přes paměť stroje, nemáme.

MIS3 má 16 GB. Windows s Defenderem a Docker Desktopem si bez dalších aplikací vezmou tři až čtyři.

**Je třeba rozhodnout**, jak meze uvnitř stroje a stroj samotný srovnat.

## Zvažované varianty

### 1 — Nechat 8 GB a snížit paměť serveru

`DATABASE_MAX_SERVER_MEMORY_MB` 1536 by součet srazil pod paměť stroje. Zamítáme: 4096 MB je měřicí režim serveru (rozh. 164), ve kterém stojí záznamy časů katalogů a běhů FIBEN; snížit ho pro jeden druh běhu by znamenalo dva servery v záznamech, a pro běh FIBEN nad 720 MB dat by menší buffer pool měřil disk místo dotazu.

### 2 — Nechat 8 GB a snížit mez kontejnerů sad

Zamítáme: sada je jedna a s 6 GB prochází i body, které 6 GB potřebují — kompilace projektu 800 entit, kompilace artefaktu s 512 joiny. Menší mez by z nich udělala pády a posunula meze řad tam, kde je dělá konfigurace, ne nástroj.

### 3 — Stroji 12 GB

Uvnitř 11 963 MB (`free -m`), součet mezí server 4096 MB a zhruba 0,5 GB mimo buffer pool, sada 6 GB, jádro, Docker a cache kolem 1 GB — asi 11,5 GB — se vejde. Bod, který chce víc, ukončí jádro v jeho vlastním kontejneru a je to řádek `crashed`; databáze běží dál. Windows zůstanou čtyři GB. Volíme.

### 4 — Stroji 13 či 14 GB

Zamítáme: Windows by zůstaly dva až tři GB, a hostitel, který sám stránkuje, zpomalí i stroj, který měří.

### 5 — Swap ve stroji

Zamítáme z důvodu rozhodnutí 145: běh, který tiše stránkuje, mění změřené časy, a vyčerpání paměti má skončit pádem bodu, ne pomalým během.

## Rozhodnutí

**Volíme variantu 3. Virtuální stroj Dockeru na MIS3 má čtyři procesory, 12 GB paměti a žádný swap (`%USERPROFILE%\.wslconfig`); všechno ostatní z rozhodnutí 145 a 164 platí beze změny — server v měřicím režimu s 4096 MB, kontejnery sad s 6 GB, rozdělení procesorů.**

Pravidlo, které za číslem stojí: **součet mezí všeho, co běh pouští ve stroji současně, nesmí přesáhnout paměť stroje.** Běh, kterému to s výchozími mezemi nevyjde, jednu z nich sníží a záznam to řekne — experimentální běh se dvěma servery tak dál pouští oba s 2048 MB, protože dva servery po 4096 MB a sada se nevejdou ani do 12 GB.

## Důsledky

- **Záznamy** od tohoto data uvádějí 12 GB; dřívější uvádějí 8 GB. Změřené časy se tím posunout nemají — procesory a jejich rozdělení se nemění a žádný dřívější běh nebyl paměti blízko —, ale ověřené to není; kdyby se to v souhrnu ukázalo, je to věc *Nesrovnalostí* přehledu záznamů.
- **Dokumentace:** [`runs/README.md`](../runs/README.md), *Metodika*; [`architecture.md`](../architecture.md) §6, *Měřicí prostředí*; `ORMConvertor/README.md`, *Docker*.
- **Běh mezí** se opakuje celý, v novém prostředí; z pokusu z 2026-10-27/28 se nic nepřebírá.
