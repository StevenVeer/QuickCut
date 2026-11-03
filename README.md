# QuickCut

Klein Windows-programma (~60 KB) dat met één sneltoets een ring met knoppen opent rond je muis:
programma's starten, websites openen, media bedienen (Spotify, YouTube, ...) of toetscombinaties sturen.

- Openen: **2x rechtermuisklik** of de sneltoets **Ctrl+Alt+Spatie** (aan te passen in Instellingen).
  Niet in volledig scherm (games), tenzij je dat uitzet.
- **Ingedrukt houden**, naar een knop bewegen en loslaten kiest die knop (bij 2x rechtsklik: houd de 2e klik vast)
- Kort tikken laat de ring open: klik of 1-9 = knop, Spatie = midden, scrollen = volume, Esc = sluiten
- Midden = standaardknop (Play/Pauze), daaromheen max. 12 eigen knoppen
- Instellingen: dubbelklik op het icoon in het systeemvak, of start de exe nogmaals
- Instellingen worden opgeslagen in `%APPDATA%\QuickCut\config.ini`

## Bouwen

Geen extra software nodig: gebruikt de C#-compiler die in Windows zit (.NET Framework 4.x).

```
build.cmd
```

Dit maakt `QuickCut.exe` in de hoofdmap.
