# 198 — Položka archivu je relativní cesta bez `..` a bez znaků, které jméno souboru nesmí nést; jiné jméno odmítne celý archiv s 400, které položku jmenuje

Datum: 2026-10-30
Stav: platí
Požadavky: S7, F14, S4
Podklad: rozhodnutí [033](033-shape-of-the-static-frontend-screens.md) (nahrazeno 099, část o stažení platí), [044](044-error-response-as-problem-details.md), [136](136-a-project-archive-is-unpacked-by-the-browser-with-own-code.md), [196](196-a-defect-of-the-tool-refuses-the-unit-entity-or-query-with-a-record-and-a-log-line-and-400-stays-for-the-request-itself.md); [`threat-model.md`](../threat-model.md), hrozba 3; revize odolnosti [2026-10-30](../audits/2026-10-30-robustness-audit.md), nález 3.2

## Kontext

`POST /archive` balí dvojice *jméno + obsah* do ZIP pro stažení celého výstupu (rozh. 033) a nic nepřekládá. Jména dává klient a server je bral beze změny; model hrozeb to vedl jako hrozbu 3 — jediné místo, kde nástroj vydá soubor se jménem, které neurčil — s ochranou *nic* a s poznámkou, že co se stane, rozhoduje rozbalovač uživatele. Revize z 2026-10-30 to změřila: `../../evil.txt` i `/etc/passwd` prošly do archivu doslova, prázdné jméno a `C:\abs\p.txt` shodily celý archiv s 400 a textem výjimky `ZipArchive`, který o položce nic neříká.

Jediný klient, kterého nástroj zná, je vlastní frontend: jména položek bere ze jmen artefaktů, která skládá server, a z cest jednotek, které přišly z archivu projektu rozbaleného v prohlížeči (rozh. 136) — relativní cesty s adresáři, `src/Sales/Customer.cs`. Jméno, které vede ven z adresáře nebo je absolutní, od něj nikdy nepřijde; přijde-li, je to chyba jiného klienta nebo záměr.

## Zvažované varianty

### 1 — Nechat jména beze změny (dnešní stav)

Předpoklad důvěryhodné sítě (model hrozeb) a rozbalovače, které `..` samy odmítají. Zamítáme: předpoklad o rozbalovači není náš a neověřujeme ho; ochrana stojí jeden průchod seznamem jmen a nic legitimního neodmítne.

### 2 — Jména opravit (odstranit `..`, úvodní oddělovač, písmeno jednotky)

Archiv by vznikl vždy. Zamítáme: tiše by změnil, oč klient žádal — soubor by přistál jinde, než klient řekl, a chyba klienta by zůstala skrytá. Nástroj nikde jinde vstup neopravuje mlčky: co nepřijme, odmítne záznamem nebo odpovědí, která to řekne (rozh. 045, 196).

### 3 — Odmítnout celý archiv s 400, které položku jmenuje

Jméno je přijatelné, je-li neprázdné, nezačíná-li oddělovačem, nemá-li segment `..` a nenese-li znak, který jméno souboru nesmí (`< > : " | ? *` a řídicí znaky — množina Windows, přísnější než Linuxu, protože archiv se rozbaluje na obou). Jinak 400 `ProblemDetails` s větou, která položku jmenuje a říká, co jí chybí. Celý archiv, ne položka: archiv bez jedné položky by byl neúplný výstup, který se tváří jako úplný, a klient, který poslal špatné jméno, má opravit požadavek, ne dostat zkrácenou odpověď. Volíme ji.

## Rozhodnutí

**Položka archivu je relativní cesta pod adresářem, do kterého se archiv rozbalí; jiné jméno odmítne celý archiv s 400, které položku jmenuje.** Konkrétně `Endpoints.EntryNameRefusal`: prázdné nebo bílé jméno, jméno začínající `/` či `\`, segment `..` (pod oběma oddělovači), znak z `< > : " | ? *` nebo řídicí znak — každé s vlastní větou (rozh. 044), logované jako odmítnutí požadavku (rozh. 196). `C:\…` padá na dvojtečku, takže absolutní cesta Windows nepotřebuje vlastní pravidlo. Adresáře v cestě (`src/Sales/Customer.cs`) procházejí, protože tak frontend jmenuje jednotky z archivu projektu.

**Co rozhodnutí netvrdí.** Že by archiv byl bezpečný proti rozbalovači, který relativní cestu s adresáři vyloží jinak než jako podadresář; tvrdí jen, že nástroj nevydá jméno, které by z adresáře vedlo ven nebo by rozbalovač nemohl zapsat. Jména zůstávají klientova (rozh. 033): nástroj je nemění, jen některá nepřijme.

## Důsledky

- **Kód:** `Endpoints.EntryNameRefusal`, `CharactersNoFileNameMay`; kontrola `files` a položek `null` před balením (196).
- **Testy:** `Api/ArchiveEndpointTest` — osm jmen, která archiv odmítne (každé s větou, která jméno nese), relativní cesta s adresáři, která projde, a chybějící seznam.
- **Dokumenty:** `threat-model.md`, vstupní bod `/archive` a hrozba 3 (ochrana místo *nic*); `architecture.md` §6.5; `traceability.md` S7.
