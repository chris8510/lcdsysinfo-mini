# LCDSysInfo 3.5 (USB 20A0:41ED): rekonstruiertes Protokoll

*[English version](PROTOCOL.md)*

Quelle: statische Analyse von `GOverlay.exe` 1.4.3
(SHA-256 `14df5442…bd4e767`, MVID `71548391-6036-4f9d-80ee-efb6c3aac153`)
mit REA `inspect_managed_members`, Klasse
`GOverlay.LCDsysinfo2`. Die Methodennamen sind nicht verschleiert.

Alles hier ist aus dem IL-Code abgeleitet. „Beobachtet“ heißt: steht so im
Code. „Vermutet“ heißt: gedeutet.

**Am Gerät bestätigt (Firmware 251):**
- Lesebefehle: Seriennummer, Firmware, Sperrstatus, Dateitabelle mit 134
  Einträgen, Pixel lesen (136).
- Zeichenbefehle: Füllen (26), Text mit eingebauter Schrift (25), Rechtecke
  (191), Icons mit und ohne Transparenz (29/193), Text mit Flash-Schrift (190).

Noch nicht am Gerät getestet: Helligkeit (13), Hintergrundbild (30).

## Transport

- Bibliothek: LibUsbDotNet (`UsbDevice.ControlTransfer`, `UsbSetupPacket`),
  Treiber libusb-win32 (`libusb0.sys`).
- **Alle Befehle sind Control-Transfers am Endpoint 0.** Es gibt keine Bulk-
  oder Interrupt-Endpoints für Zeichenbefehle.
  - Schreiben: `bmRequestType = 0x40` (Vendor, Host→Gerät)
  - Lesen: `bmRequestType = 0xC0` (Vendor, Gerät→Host)
- Mehrbyte-Werte im Datenteil sind **Big-Endian** (`hi = v / 256`, `lo = v % 256`).
- Farben sind **RGB565** (16 Bit), z. B. `65535` = weiß.
- Display: **480 × 320** Pixel (Grenzprüfungen in `Draw_Text_Font`).
- Für `wLength` übergibt der Code im Setup-Paket 0, die tatsächliche Länge
  kommt aus dem Puffer, den `ControlTransfer` mitbekommt. Vermutet: Die
  Bibliothek setzt `wLength` aus der Pufferlänge.

## Befehle (bRequest)

| bRequest | Richtung | wValue | wIndex | Daten | Bedeutung (Methode) |
|---|---|---|---|---|---|
| 12 (0x0C) | IN | 0 | 5 | 8 Byte lesen | Seriennummer (`init`) |
| 12 | IN | 0 | 1 | 8 Byte lesen, Byte 1 | Firmware-Version (`init`) |
| 12 | IN | 0 | 2 | 11 Byte lesen, Byte 7 | Sperrstatus, 137 = „blacklisted“ (`GetLockedStatus`) |
| 12 | IN | Zähler | 4 | 128 Byte je Aufruf | Dateitabelle aus dem Flash (`readTable`) |
| 12 | IN | … | 3 | … | Screenshot vom Gerät lesen (`readScreenFromDevice`) |
| 13 (0x0D) | OUT | Helligkeit | Helligkeit | – | Helligkeit 0…255 (`brightness`) |
| 14 (0x0E) | OUT | Bild-Nr. | 4 | 5 Byte | Flash-Bild anzeigen (`Flash_Screen`) |
| 15 (0x0F) | OUT | arg1 | arg2 | – | Flash-Befehl (`Send_Command_to_flash`) |
| 16 (0x10) | OUT | 0 | arg | … | in den Flash kopieren (`CopyToFlash1`) |
| 25 (0x19) | OUT | Textlänge (+512 = kleine Schrift) | Farbe | Kopf + ASCII | Text mit eingebauter Schrift (`Draw_Text_Font`) |
| 26 (0x1A) | OUT | Zeilenmaske | Farbe | – | Bereiche mit Farbe füllen (`ClearLines`, `ClearScreen`) |
| 28 (0x1C) | OUT | 514 | Helligkeit | – | Helligkeit im Leerlauf (`brightnessidle`) |
| 28 | OUT | … | … | … | EEPROM schreiben / Firmware-Modus (`WriteEprom`, `putInFirmwareMode`): **nicht verwenden** |
| 29 (0x1D) | OUT | Sektor | Sektor | 4 Byte Position | Icon aus dem Flash zeichnen (`Draw_Icon`) |
| 30 (0x1E) | OUT | Bild-Nr. | 0 | – | Hintergrundbild setzen (`SetBackground`) |
| 33 (0x21) | OUT | … | … | ≥13 Byte | Fortschrittsbalken (`Draw_progress_bar`) |
| 136 (0x88) | IN | x | y | – | Pixel lesen (`getpixel`) |
| 189 (0xBD) | OUT | Abstand | 1 | – | Zeichenabstand der Schrift (`SetCharacterSpaceFont`) |
| 190 (0xBE) | OUT | Textlänge | Schrift-Sektor | Kopf + ASCII | Text mit Flash-Schrift (`Draw_Text_Font`) |
| 191 (0xBF) | OUT | Datenlänge | Formtyp | 10–11 Byte | Rechteck, Kreis, Dreieck, Linien, Pixel (`Draw_*`) |
| 192 (0xC0) | OUT | Anzahl | 0 | Liste | mehrere Icons auf einmal (`Draw_Icons`) |
| 193 (0xC1) | OUT | Sektor | 0xC217 | 4 Byte Position | Icon mit Transparenz, Schlüsselfarbe 0xC217 (`Draw_Icon`) |

### Details zu den wichtigsten Befehlen

**Fläche füllen (26):** Die Methode bekommt sechs Flags, aus denen eine Maske
entsteht (`wValue`, Bit 0…5 = 1, 2, 4, 8, 16, 32). `wIndex` ist die
RGB565-Farbe. `ClearScreen(c)` setzt alle sechs Bits, also Maske `0x3F`.
Vermutet: sechs horizontale Streifen à 320/6 Pixel.

**Text, eingebaute Schrift (25):**
- Schriften: `regular` (`wValue = Länge`) oder `regular_small` (`wValue = Länge + 512`).
- `wIndex` = Textfarbe (RGB565).
- Daten: `x(2) y(2) x_ende(2) y+40(2)`, danach der ASCII-Text plus `"\r\n"`,
  dann ein Nullbyte.
- `x_ende = min(x + Breite, 480)`.
- Der Code baut einen 12-Byte-Kopf, kopiert den Text aber ab Byte 8. Die
  Farbfelder werden also überschrieben, wirksam sind nur 8 Kopfbytes.

**Text, Flash-Schrift (190):**
- `wValue` = Textlänge, `wIndex` = Flash-Sektor der Schrift (aus der Dateitabelle).
- Daten: `x(2) y(2) x_ende(2) 1(2) Farbe(2) Hintergrund(2)`, danach Text +
  `"\r\n"` + Nullbyte.
- Hintergrund `12345` heißt „transparent“. Das Gerät schneidet Text bei `x_ende` ab.
- Zeichenbreiten für die Ausrichtung stehen im `info`-Feld der Schrift in der
  Dateitabelle: Feld 2 ist `' '`, Feld *k* ist das Zeichen `0x1E + k` (ein
  vorangestelltes `#` wird ignoriert, leere Felder sind fehlende Zeichen).

**Icon (29 / 193):**
- Daten: `y(2) x(2)`, in dieser Reihenfolge.
- Den Sektor liefert die Dateitabelle über den Icon-Namen.
- 193 zeichnet mit der Transparenz-Schlüsselfarbe 0xC217. Die Icon-Bitmaps
  verwenden dafür 197,65,189 bzw. 206,65,198 (RGB).

**Rechteck (191):** geklärt über die 9 Aufrufstellen in `LCDSysInfo2_Do.DoLCDSys`.
- Signatur: `Draw_Rectangle(x1, y1, x2, y2, Farbe, Umriss, Radius)`.
- x2/y2 sind **Endkoordinaten**. Beispiel aus dem Code:
  `(x, y, x+Breite, y+Höhe, color_bg, 0, 0)`.
- Daten: `y1(2) x1(2) y2(2) x2(2) Farbe(2) [Radius(1)]`. Das sind 10 Byte,
  mit Radius > 0 11 Byte.
- `wValue` = Datenlänge (10 bzw. 11). `wIndex` = Formtyp:

  | | ohne Radius | mit Radius |
  |---|---|---|
  | gefüllt (`Umriss = 0`, `color_bg`) | 3 | 10 |
  | nur Umriss (`Umriss = 1`, `color_border`) | 2 | 9 |

- **Am Gerät bestätigt:** gefüllt, Umriss und abgerundet, geprüft durch
  Zurücklesen von Pixeln mit Befehl 136.

**Dateitabelle (12, wIndex 4):** geklärt über `LCDSys2_StoreFileTableOnDevice`
und `LCDSys2_getFilesTable`.
- Flash: 2048 Sektoren à 4096 Byte (8 MiB). Die ersten 16 Sektoren
  (`sectors_filetable`) halten die Tabelle.
- Lesen: `wValue` = Blocknummer 0, 1, 2 …, je 128 Byte. Maximal 479 Blöcke,
  bis zur Zeile `-------------------<EOT>-----------------`.
- Danach `\r\n` und Nullbytes entfernen und am EOT-Marker abschneiden.
- Inhalt ist ASCII. Einträge sind durch `;` getrennt, Felder durch `|`. Jeder
  Eintrag beginnt mit `|`:
  - belegt: `|<sektor>|<datei>|<anzahl_sektoren>|<typ>|<info>|<md5>|<bytes>;`
  - Verweis (gleiche Datei wie ein anderer Sektor): `|<sektor>|<ref_sektor>;`
  - am Ende: `|UPDATES|<liste>;`
- Icons und Schriften werden über das Feld `<datei>` gefunden (z. B.
  `icon_hdd_04_on.bmp`, `Arial_14px.bin`). Für Befehl 29/193 bzw. 190 wird
  dessen `<sektor>` verwendet. `<typ>` ist eine Zahl (1 = Bild, 3 = Schrift).
- `trans` = 1, wenn `<info>` den Text `(T)` enthält. Dann zeichnet man mit 193
  statt 29.
- Das Lesen ist harmlos. Schreiben (`StoreFileTableOnDevice`) nutzt die
  Flash-Befehle 15/16 und ist für eine Anzeige-App unnötig.

## Was das für eine schlanke Ersatz-App bedeutet

Das Gerät rendert selbst. Der Host schickt nur winzige Befehle:

- Text: ca. 20–40 Byte
- Icon: 4 Byte
- Fläche: 0 Byte Daten

Icons, Hintergründe und Schriften liegen bereits im Flash. Ein Update pro
Sekunde mit 10–20 Werten sind also wenige hundert Byte. Das schafft ein
winziges Programm bei praktisch 0 % CPU.

Minimaler Ablauf:

1. Gerät öffnen (VID 0x20A0, PID 0x41ED).
2. Dateitabelle einmal lesen (Name → Sektor) und cachen.
3. `SetBackground` oder `ClearScreen`.
4. Danach pro Takt nur geänderte Werte: alten Bereich mit 191/26 übermalen,
   dann Text 25/190 bzw. Icon 29/193.

## Offene Punkte / nicht verifiziert

- Noch offen: Befehl 33 (Fortschrittsbalken, ersetzbar durch zwei Rechtecke),
  die übrigen Formtypen von 191 (Kreis, Dreieck, Linien), Screenshot-Lesen
  (wIndex 3). LCDSysInfo Mini braucht nichts davon.
- Befehle 15, 16 und 28 (Flash, EEPROM, Firmware-Modus) können das Gerät
  verändern. Für eine Anzeige-App werden sie nicht gebraucht und sollten
  gemieden werden.
