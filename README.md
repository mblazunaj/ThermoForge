# ThermoForge

Windows alat za CPU/GPU temperature i zasebne stress testove. Sučelje je na hrvatskom.

## Korištenje

Pokreni instalacijski EXE. Uključuje aplikaciju, .NET runtime i službeni potpisani PawnIO driver. Zatim pokreni ThermoForge. Ako CPU temperature nisu dostupne, pritisni **Aktiviraj CPU senzore** i potvrdi UAC.

- Odvojeno pokretanje i zaustavljanje CPU-a i odabranog GPU-a; oba mogu raditi istovremeno.
- Zajednički timer: 1, 5, 10, 15, 30 minuta ili **Ništa** za rad do ručnog prekida.
- Odbrojavanje počinje prvim testom. Dodavanje drugog testa ne resetira vrijeme.
- **Zaustavi sve**, **Esc** ili zatvaranje aplikacije prekida oba testa.
- Temperaturni pragovi početno su CPU 95 °C / GPU 85 °C i mijenjaju se dok testovi ne rade.
- Zaštita provjerava najvišu temperaturu svih dostupnih CPU/GPU senzora, uključujući GPU hotspot i memoriju. Prelazak praga prekida oba testa.
- Nedostajući ili zastarjeli senzori, neodazivanje sučelja i prekid veze s radnim procesom zaustavljaju testove. Bez temperature nema početka testa za taj uređaj.
- CPU radi vektorske izračune na svim logičkim procesorima, uključujući sustave s više procesorskih grupa. GPU koristi Direct3D 11 compute shader, bez ograničavanja na brzinu osvježavanja ekrana.

Desni klik u praznom dijelu prozora: automatska provjera nadogradnji, mapa postavki/dnevnika i informacije o programu.

## Podrška i ograničenja

Windows 10 2004+ ili Windows 11 x64, Intel/AMD CPU, NVIDIA/AMD/Intel GPU s Direct3D 11. Podrška određenog senzora ovisi o driveru i LibreHardwareMonitoru; ne postoji jamstvo očitanja na svakom modelu. Neki CPU-i ne izlažu temperature svake jezgre. Neke integrirane grafike nemaju zasebnu temperaturu. Nedostajući podaci prikazuju se kao crtica; ne procjenjuju se iz drugih uređaja. Jednako nazvani višestruki GPU-i blokiraju odabir testa kad se senzor ne može jednoznačno povezati.

Za CPU senzore uglavnom trebaju administratorska prava. Aplikacija se zadano pokreće bez njih i nudi ponovno pokretanje s pravima. Ovo je alat za opterećenje, a ne dokaz ispravnosti svih računskih jedinica ili VRAM-a. Postotak opterećenja ovisi o hardveru, toplinskim ograničenjima, napajanju i GPU engineu koji se mjeri. Temperaturna zaštita uzorkuje podatke svake sekunde i ne zamjenjuje zaštite hardvera.

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

`--self-test` izvršava CPU posao s najviše dvije dretve kroz 300 ms i nekoliko kratkih GPU dispatch naredbi, zatim čita stvarne senzore. Ne radi dugotrajni test punog opterećenja. `--policy-test` provjerava samo pravila bez pristupa hardveru i koristi se u CI-u.

## Licence

Izvorni kod aplikacije pripada vlasniku projekta; bez zasebne dozvole sva prava ostaju zadržana. Biblioteke se distribuiraju neizmijenjene pod njihovim licencijama. Izrada generira `THIRD-PARTY-NOTICES.txt` i mapu `licenses` s tekstovima i poveznicama na izvorni kod. LibreHardwareMonitor je MPL-2.0; PawnIO je GPL-2.0-or-later s iznimkom za module, a njegovi moduli LGPL-2.1-or-later. Vortice i .NET runtime imaju MIT licence i vlastite obavijesti.
