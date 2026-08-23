# 040 — Hranice předávaného artefaktu vůči konzumentskému projektu

Datum: 2026-08-22
Stav: platí
Požadavky: F2, F5, F11, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [010](010-diagnostics-as-returned-data.md), [020](020-canonical-generator-parameter-vocabulary.md), [028](028-assembly-name-is-not-ours-to-invent.md), [029](029-database-connection-is-the-consumer-projects-fact.md) a [037](037-enforced-member-binding-held-by-the-test.md); ISO/IEC 25010:2023, charakteristika *Compatibility*, podcharakteristika *Interoperability* (viz [`quality-model.md`](../quality-model.md))

## Kontext

Vygenerovaný artefakt nikdy nežije sám. Vždycky se má vložit do projektu, který nástroj nevidí: má vlastní název sestavení, vlastní připojení do databáze, vlastní soubor projektu se závislostmi, vlastní registraci v kontejneru a vlastní jazykovou verzi. **Schopnost do takového projektu zapadnout je celý smysl existence nástroje** — v pojmech ISO/IEC 25010:2023 je to *Interoperability* pod charakteristikou *Compatibility* —, a přitom je to jediná oblast modelu kvality, ke které seznam S1–S7 nemá požadavek (viz [`quality-model.md`](../quality-model.md)). Zadání ji nevynechalo omylem; prostě ji nepojmenovalo, protože ji pokládá za samozřejmou.

Důsledek je vidět v rozhodovací stopě. Tutéž otázku jsme už dvakrát řešili od nuly a pokaždé u jednoho konkrétního údaje:

- **[028](028-assembly-name-is-not-ours-to-invent.md)** — název sestavení v NHibernate mapování se odvozoval ze jmenného prostoru. Mapování tak tvrdilo o cílovém projektu něco, co obvykle neplatí, a NHibernate ho odmítl.
- **[029](029-database-connection-is-the-consumer-projects-fact.md)** — připojení do databáze do mezireprezentace nevstupuje a do výstupu se nevypisuje; text 029 přitom název sestavení jmenuje jako „týž druh faktu" a poznámku z 028 povyšuje na pravidlo, ale jen pro připojení.

Obě rozhodnutí došla ke stejné odpovědi po dvou různých cestách: 028 přes zákaz náhražek ([004](004-unexpressible-facts-as-warnings.md)), 029 přes druhou větu S4. To je zdravé znamení — pravidlo za nimi existuje —, jenže zapsané není, takže se u třetí otázky bude argumentovat potřetí a může vyjít jinak.

**A ty otázky stojí ve frontě.** Jmenný prostor, když ho zdroj nenese. Soubor projektu a závislosti v něm. Konfigurační soubor cílového ekosystému (`hibernate.cfg.xml`, `persistence.xml`). Registrace v DI kontejneru, kterou EF Core v konzumentském projektu potřebuje. Cílová verze jazyka. Styl formátování výstupu. Názvy souborů, pod kterými artefakty odcházejí. Javová větev (F7–F10) počet těch otázek zdvojnásobí, protože `persistence.xml` i `pom.xml` vypadají jako přirozený výstup javového builderu — a nejsou.

Dnes je z celého pravidla ověřená právě jedna věta na jednom místě (`InventedFactsTest` hlídá, že mapování nenese `assembly=`) a jedna z druhé strany (`ArtifactCarriesNoCredentialsTest` hlídá, že artefakt nenese připojení). Samotné pravidlo nehlídá nic, takže třetí výmysl by se do výstupu dostal stejně tiše jako ten první.

## Zvažované varianty

1. **Nechat to na jednotlivých rozhodnutích.** Dnešní stav: každý takový údaj dostane vlastní soubor v `decisions/`, až na něj dojde. Má to jednu skutečnou přednost — rozhoduje se s konkrétním případem v ruce a bez abstrakce dopředu. Jenže případy jsou už teď vyjmenované a jsou to varianty jedné otázky; psát k nim šest rozhodnutí znamená šestkrát opakovat tutéž úvahu a riskovat, že sedmé vyjde jinak, protože ho bude psát jiný den jiná potřeba. Hlavně ale žádné z těch rozhodnutí nezavádí pravidlo, které by šlo hlídat testem: hlídat jde jen jeho jednotlivé instance, a to je přesně stav, kvůli kterému se výmysl s názvem sestavení nezjistil dřív. Zamítáme.

2. **Vydávat rovnou spustitelný projekt.** Opačný extrém, a nikoli nesmyslný: uživatel by dostal složku, kterou otevře a spustí — entita, mapování, soubor projektu se závislostmi, konfigurace s připojením, registrace kontextu. Pro scénář „přeložím projekt a hned ho zkusím" je to lákavé. Znamená to ale vymyslet si všechno, co nikdo nezadal: název projektu, cílovou verzi frameworku a jazyka, verze balíčků, připojovací řetězec. Rozhodnutí [004](004-unexpressible-facts-as-warnings.md) přesně tohle zakazuje, a to z důvodu, který tady platí dvojnásob — náhražka vypadá jako hotový výstup a chyba se pozná až u konzumenta. Přibyl by druh artefaktu, který dnes nevydáváme, výstup by začal záviset na prostředí generátoru (S2 žádá pravý opak) a do zdrojového souboru by se vrátily přihlašovací údaje, které S4 zakazuje. A konzument, který má vlastní projekt — tedy ten, pro kterého je nástroj psaný —, by drtivou většinu toho balíku stejně zahodil. Zamítáme.

3. **Vyslovit pravidlo, dát mu rozlišovací kritérium a nechat ho držet testem.**

## Rozhodnutí

**Volíme variantu 3. Předáváme jen ta tvrzení a jen ty artefakty, které jsou faktem o entitě, mapování nebo dotazu. Fakt o projektu, který výsledek přeloží a spustí, nevydáváme — ani jako výchozí hodnotu, ani jako náhražku, ani jako odhad z jiného faktu.**

**Rozlišovací kritérium je jediná otázka: změní se ta hodnota, když tentýž zdrojový kód přeloží jiný projekt?** Když ano, je to fakt konzumentského projektu a nepatří nám. Když ne, je to fakt zdroje a neseme ho beze změny. Kritérium je záměrně mechanické, aby se u sedmé otázky nedalo argumentovat pocitem, a rovnou odpovídá na celou frontu:

| Údaj | Změní se s projektem? | Kdo ho dodá |
|---|---|---|
| název sestavení | ano | konzument (rozhodnutí 028) |
| připojovací řetězec | ano | konzument (rozhodnutí 029) |
| soubor projektu a závislosti (`.csproj`, `pom.xml`) | ano | konzument |
| konfigurace ekosystému (`hibernate.cfg.xml`, `persistence.xml`) | ano | konzument |
| registrace v DI kontejneru (`AddDbContext`) | ano | konzument |
| cílová verze jazyka (`LangVersion`) | ano | konzument; my jen říkáme, co výstup předpokládá |
| jmenný prostor | ne — je ve zdrojovém kódu | neseme ze zdroje |
| název entity, tabulky, sloupce, klíč, vztah | ne | neseme ze zdroje nebo z katalogu |
| styl formátování výstupu | není ani jedno — je to fakt generátoru | neseme, deterministicky (S2) |

Poslední řádek stojí za vysvětlení, protože kritériem projde nahoru i dolů: styl formátování se s konzumentským projektem nemění, ale ani ho nenese zdroj. Je to vlastnost našeho vydávání a S2 na ní stojí — proto držíme jeden tvar, ne tvar odhadnutý z konvencí cílového projektu, které stejně nevidíme. Konzument si výstup přeformátuje vlastním nástrojem, což je operace bez ztráty významu.

Z pravidla plynou tři pravidla emise:

**Fakt, který vstup nese, jde na výstup beze změny.** Jmenný prostor je učebnicový případ: `namespace Shop;` je vlastnost té třídy, ne projektu, který ji přeloží, takže překlad ho přenáší a nepřepisuje ho na nic „vhodnějšího pro cíl". Kdyby ho zdroj neměl, výstup ho nemá také — entita v globálním jmenném prostoru je věrné tvrzení o vstupu, kdežto vymyšlený `Generated` nebo jmenný prostor odvozený z názvu entity je tvrzení o projektu, který jsme neviděli.

**Fakt, který vstup nenese a je konzumentův, se vynechá a nenahradí.** Vynechání je úplnější tvrzení než výmysl, jak argumentuje 028: mapování bez `assembly` říká přesně to, co víme, kdežto mapování s odvozeným `assembly` přidává čtvrtou informaci, kterou nikdo neposkytl.

**Záznam o tom vzniká jen tam, kde se něco stalo právě s tímhle vstupem.** Chybějící `assembly` je vlastnost formátu, kterou by nesl každý převod do NHibernate bez výjimky, a konstantní záznam nic netvrdí (028, [010](010-diagnostics-as-returned-data.md)) — patří do dokumentace, a je tam. Naopak fakt, který zdroj nesl a cíl ho neunese, je ztráta u tohoto převodu a záznam dostane, jak žádá [004](004-unexpressible-facts-as-warnings.md).

**Pravidlo drží test, ne dobrá vůle.** Vzniká `ConsumerProjectFactsTest` a hlídá ve všech devíti směrech tři tvrzení: jmenný prostor zdroje projde beze změny, jmenný prostor se nevymýšlí tam, kde ho zdroj nemá, a žádný artefakt nenese fakt konzumentského projektu ani jím není. Je to táž konstrukce jako u [037](037-enforced-member-binding-held-by-the-test.md) — vazbu, kterou nelze vyjádřit typem, drží tvrzení v testovací sadě. Bez něj by pravidlo bylo dohoda mezi autory, tedy přesně to, co selhalo u názvu sestavení.

**Dveře pro explicitní vstup zůstávají otevřené.** Kdyby se ukázalo, že konzumentům vadí doplňovat název sestavení ručně, není řešením ho odhadovat, ale přijmout ho jako nepovinný parametr v kanonickém slovníku ([020](020-canonical-generator-parameter-vocabulary.md)) — tedy jako údaj, který někdo vyslovil. Tuhle variantu 028 zamítlo jako předčasnou, ne jako špatnou, a tohle rozhodnutí na tom nic nemění: rozdíl mezi „uživatel to zadal" a „my jsme to uhodli" je celý předmět tohohle textu.

## Důsledky

**Mezireprezentace zůstává bez faktů konzumentského projektu, a to i do budoucna.** Slučování zdrojů podle F5 tak nikdy nemusí rozhodovat spor o připojení nebo o název sestavení — ty se do IR nedostanou. Priorita zdrojů ([017](017-source-precedence-for-mapping-facts.md)) tím zůstává otázkou o mapovacích faktech, ne o prostředí.

**Konzument má co doplnit, a je to popsané.** Název sestavení pro NHibernate, připojení pro kterýkoli framework, soubor projektu a registraci kontextu pro EF Core. Co přesně to u kterého scénáře je, nese [`use-cases.md`](../use-cases.md); tady stačí, že to není mezera výstupu, ale jeho hranice.

**Fronta otázek je zodpovězená dopředu, a rozhodnutí k nim už nevzniknou.** Soubor projektu, konfigurace ekosystému, DI registrace, jmenné prostory, verze jazyka a formátování mají odpověď v tabulce výše. Nové rozhodnutí si vyžádá až údaj, u kterého kritérium selže — tedy takový, který se s projektem mění, a přesto ho cílový framework vyžaduje uvnitř mapování. Zatím žádný takový neznáme.

**Pro javovou větev se tím zavírá past, kterou 029 otevřelo jen napůl.** JPA nese připojení v `persistence.xml`, Maven závislosti v `pom.xml`, a obojí vypadá jako přirozený výstup javového builderu. Ani jedno jím není, a nový wrapper se to dozví z pravidla, ne až z revize.

**Co výstup předpokládá o jazyku, je nově tvrzení, které dlužíme dokumentaci.** Dnešní generovaný C# používá file-scoped jmenné prostory, tedy C# 10 a novější; verzi cílového frameworku nese deskriptor ([013](013-target-framework-versions.md)). Zapisuje se to do [`architecture.md`](../architecture.md), ne do artefaktu — artefakt sám o jazykové verzi nic tvrdit nemá, protože `LangVersion` je vlastnost projektu, který ho přeloží.

**Cena je jedna testovací třída a 27 tvrzení navíc.** Běží nad orchestrací, takže se dotýkají všech tří wrapperů naráz; S1 to neporušuje, protože wrappery samotné o testu nevědí.
