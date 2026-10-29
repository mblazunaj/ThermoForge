# ThermoForge

Windows alat za CPU/GPU temperature i zasebne stress testove. Sučelje je na hrvatskom.

## Korištenje

Pokreni instalacijski EXE. Uključuje aplikaciju, .NET runtime i službeni potpisani PawnIO driver. ThermoForge pri pokretanju odmah traži administratorska prava kroz Windows UAC i automatski učitava sve dostupne temperature. U aplikaciji više nema dodatnog gumba za aktiviranje CPU senzora.

- Odvojeno pokretanje i zaustavljanje CPU-a i odabranog GPU-a; oba mogu raditi istovremeno.
- Zajednički timer: 1, 5, 10, 15, 30 minuta ili **Ništa** za rad do ručnog prekida.
- Odbrojavanje počinje prvim testom. Dodavanje drugog testa ne resetira vrijeme.
- **Zaustavi sve**, **Esc** ili zatvaranje aplikacije prekida oba testa.
- Temperaturni pragovi početno su CPU 95 °C / GPU 85 °C i mijenjaju se dok testovi ne rade.
- Zaštita provjerava najvišu temperaturu svih dostupnih CPU/GPU senzora, uključujući GPU hotspot i memoriju. Prelazak praga prekida oba testa.
- Nedostajući ili zastarjeli nadzirani senzori, neodazivanje sučelja i prekid veze s radnim procesom zaustavljaju testove.
- Integrirani Intel GPU bez zasebne temperature može raditi uz **nadzor CPU paketa**. Tipka i poruka jasno navode taj način; GPU temperatura ostaje označena crticom. GPU temperaturni prag u tom načinu primjenjuje se na CPU paket. To nije izravna temperaturna zaštita GPU-a. Ako CPU paket nestane ili prijeđe prag, oba testa staju. Zasebne grafičke kartice ne koriste ovu zamjenu.
- CPU radi vektorske izračune na svim logičkim procesorima, uključujući sustave s više procesorskih grupa. GPU koristi Direct3D 11 compute shader, bez ograničavanja na brzinu osvježavanja ekrana.

Desni klik u praznom dijelu prozora: automatska provjera nadogradnji, mapa postavki/dnevnika i informacije o programu.

## Podrška i ograničenja

Windows 10 2004+ ili Windows 11 x64, Intel/AMD CPU, NVIDIA/AMD/Intel GPU s Direct3D 11. Podrška određenog senzora ovisi o driveru i LibreHardwareMonitoru; ne postoji jamstvo očitanja na svakom modelu. Neki CPU-i ne izlažu temperature svake jezgre. Neke integrirane grafike nemaju zasebnu temperaturu. Nedostajući podaci prikazuju se kao crtica; ne procjenjuju se iz drugih uređaja. Jednako nazvani višestruki GPU-i blokiraju odabir testa kad se senzor ne može jednoznačno povezati.

Za CPU senzore uglavnom trebaju administratorska prava. Aplikacija ih zato traži odmah pri pokretanju; Windows UAC potvrdu i dalje potvrđuje korisnik. Za integrirani Intel GPU dopušten je jasno označen nadzor CPU paketa samo ako postoji točno jedan CPU s valjanim očitanjem paketa. Ostali GPU-i bez vlastitih senzora ostaju blokirani. Ovo je alat za opterećenje, a ne dokaz ispravnosti svih računskih jedinica ili VRAM-a. Postotak opterećenja ovisi o hardveru, toplinskim ograničenjima, napajanju i GPU engineu koji se mjeri. Temperaturna zaštita uzorkuje podatke svake sekunde i ne zamjenjuje zaštite hardvera.

Postavke i dnevnik: `%LOCALAPPDATA%\ThermoForge`. Nadogradnje ih čuvaju. Deinstalacija čuva te postavke i zajednički PawnIO driver; driver se može zasebno ukloniti iz Windows aplikacija.

## Nadogradnje i objava

Aplikacija provjerava najnoviji stabilni GitHub Release iz `mblazunaj/ThermoForge`. Preuzimanje i pokretanje instalacije zahtijeva potvrdu u aplikaciji. Očekuju se datoteke `ThermoForge-Setup-X.Y.Z.exe` i `ThermoForge-Setup-X.Y.Z.exe.sha256` u istoj objavi, označenoj `vX.Y.Z`. SHA-256 otkriva oštećeno preuzimanje. Instalacija nije potpisana certifikatom izdavača dok vlasnik ne doda potpisivanje.

Za novu verziju promijeni `<Version>` u `ThermoForge/ThermoForge.csproj` i pošalji izmjenu u `main`. GitHub Actions sastavlja instalaciju, pokreće testove pravila zaštite te novu verziju objavljuje s tagom `vX.Y.Z` i kontrolnim zbrojem. Već objavljena verzija ne prepisuje se. Službeni PawnIO preuzima se s unaprijed zadanom verzijom, provjerom SHA-256 i Authenticode potpisa.

## Izrada

Potrebni su Windows, .NET SDK 10 i Inno Setup 6.7+ ili 7. Iz korijena izvora pokreni:

```powershell
.\build.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

Instalacija i SHA-256 izlaze u susjednu mapu `outputs`. Ne trebaju privatni tokeni za izradu. Za objavu treba GitHub autentikacija vlasnika.

Provjera pravila i kratkih radnih zadataka na lokalnom računalu:

```powershell
Start-Process .\publish\ThermoForge.exe -ArgumentList '--self-test', 'self-test.txt' -Wait
```

`--self-test` izvršava CPU posao s najviše dvije dretve kroz 300 ms i nekoliko kratkih GPU dispatch naredbi, zatim čita stvarne senzore. Ne radi dugotrajni test punog opterećenja. Uz rezultat stvara snimku sučelja s jasno označenim testnim senzorima za provjeru izgleda. `--policy-test` provjerava pravila i vezu GPU tipke s testnim senzorima, bez pristupa hardveru. CI ga pokreće preko izgrađenog DLL-a kako provjere ne bi ovisile o UAC prozoru. Ikona je višerezolucijski ICO (16–256 px), s izvornim SVG-om u `ThermoForge/Assets`; `Generate-Icon.ps1` ponovno generira ICO i PNG.

## Licence

Izvorni kod aplikacije pripada vlasniku projekta; bez zasebne dozvole sva prava ostaju zadržana. Biblioteke se distribuiraju neizmijenjene pod njihovim licencijama. Izrada generira `THIRD-PARTY-NOTICES.txt` i mapu `licenses` s tekstovima i poveznicama na izvorni kod. LibreHardwareMonitor je MPL-2.0; PawnIO je GPL-2.0-or-later s iznimkom za module, a njegovi moduli LGPL-2.1-or-later. Vortice i .NET runtime imaju MIT licence i vlastite obavijesti.
