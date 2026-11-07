<h1 align="center">ColorLayers by Synrik 1.2</h1>

<p align="center">
<a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="Lizenz MIT"></a>
</p>

Es gibt Menschen, die im Meeting zum Whiteboard gehen und mit drei Strichen eine Katze zeichnen, die alle wiedererkennen. Und es gibt den Rest von uns. Wenn du zum Rest gehörst und trotzdem Freunde oder Kollegen beeindrucken willst, ohne vorher zehn Jahre Kunststudium nachzuholen, ist ColorLayers dein Werkzeug. Das Programm nimmt ein Bild, zerlegt es in Farbebenen und zeichnet es mit deiner Maus in ein Zeichenprogramm oder auf ein digitales Whiteboard. Das Talent kommt aus der Datei, den Applaus darfst du behalten.

Das Ergebnis hängt vom Zielprogramm, vom Pinsel und von deiner Geduld beim Kalibrieren ab. Paint ist ein guter Ort, um das vor dem großen Auftritt herauszufinden.

## Inhalt

- [Was ist ColorLayers?](#was-ist-colorlayers)
- [Herkunft und Lizenz](#herkunft-und-lizenz)
- [Voraussetzungen](#voraussetzungen)
- [Bauen und starten](#bauen-und-starten)
- [Schnellstart mit Paint](#schnellstart-mit-paint)
- [Die Bedienung im Detail](#die-bedienung-im-detail)
- [Tastenübersicht](#tastenübersicht)
- [Unterstützte Zielprogramme](#unterstützte-zielprogramme)
- [Fehlerbehebung](#fehlerbehebung)
- [Geschichte des Projekts](#geschichte-des-projekts)
- [Links zum Originalprojekt](#links-zum-originalprojekt)
- [Danksagung](#danksagung)

## Was ist ColorLayers?

ColorLayers ist ein Fork von AutoDraw 2.2. Das Original zeichnet Bilder in Schwarz-Weiß nach. ColorLayers ergänzt einen Farbmodus, der das Bild auf eine feste Anzahl von Farben reduziert und jede Farbe als eigene Ebene zeichnet. Zwischen den Ebenen wählst du die passende Farbe selbst im Zielprogramm aus, danach geht es mit der nächsten Ebene weiter.

Die wichtigsten Funktionen im Überblick

- Bild importieren und automatisch auf eine sinnvolle Größe einpassen
- Bis zu 16 freigegebene Zeichenfarben. Papierweiß bleibt transparent.
- Farbanzahl 0 schaltet in den klassischen Monochrom-Modus mit den bekannten AutoDraw-Filtern
- Palette mit Farbnamen und Hexwerten, damit du die richtige Farbe im Zielprogramm findest
- Overlay zum Positionieren direkt über dem Zielfenster, mit Größenänderung per Tastatur
- Prognose, die zeigt, wie das Bild mit breitem Pinsel ungefähr aussehen wird
- Kalibrierung der Pinselbreite und optional ein eigener, separater Konturpinsel
- Zusammenhängende Striche mit gedrückter Maustaste
- Not-Aus über Entf oder Ende

## Herkunft und Lizenz

ColorLayers basiert auf AutoDraw von AlexDalas und Siydge. Version 1.0 baute auf dem Upstream-Tag `v2.1.1` auf. Version 1.2 übernimmt ausgewählte Änderungen aus AutoDraws Entwicklungsbranch `main`, darunter den automatischen Zoom der Bildvorschau, den Import aus der Zwischenablage und die Unterstützung für .NET 8. Farbplanung, Overlay, Größenregler und Maussteuerung stammen weiterhin von ColorLayers.

Das Projekt steht wie das Original unter der MIT-Lizenz. Die Datei [LICENSE](LICENSE) ist unverändert übernommen, inklusive des ursprünglichen Copyright-Hinweises. Weitere Lizenzen eingebundener Komponenten findest du in [Assets/LICENSES.txt](Assets/LICENSES.txt).

Synrik pflegt ColorLayers als eigenständigen Fork. Die verlinkten Releases und Support-Kanäle gehören zum AutoDraw-Original. ColorLayers kannst du aus diesem Quellcode bauen.

## Voraussetzungen

- Windows 10 oder 11 in der 64-Bit-Version
- Das .NET SDK in Version 8 oder neuer zum Bauen
- Die .NET 8 Runtime für den unten beschriebenen Build
- Ein Zeichenprogramm, das normale Mauseingaben annimmt, zum Beispiel Paint

Dieser Fork zielt auf `net8.0-windows` und ist für Windows x64 eingerichtet.

## Bauen und starten

Öffne eine Konsole im Projektordner, also dort, wo `Autodraw.csproj` liegt, und führe diesen Befehl aus.

```powershell
dotnet publish Autodraw.csproj -c Release -r win-x64 --self-contained false -o publish
```

Danach liegt im Ordner `publish` die Datei `Autodraw.exe`. Starte sie per Doppelklick. Den ganzen Ordner kannst du an einen beliebigen Ort kopieren, die EXE braucht die Dateien daneben.

Falls Windows beim ersten Start nach der .NET-Laufzeit fragt, installiere die .NET 8 Runtime für Windows x64 und starte erneut.

## Schnellstart mit Paint

Bevor du dich vor Publikum am Whiteboard versuchst, probiere alles einmal in Paint aus. Strg+Z verzeiht den ersten Testlauf.

1. Öffne Paint und mach das Fenster ausreichend groß. Wähle einen Pinsel oder Stift mit kleiner Breite.
2. Starte ColorLayers und lade ein Bild. Logos, Comicfiguren und Bilder mit klaren Flächen funktionieren am besten.
3. Stelle die Farbanzahl ein und klicke **Bild verarbeiten** / **Process Image**. Die Vorschau zeigt die reduzierte Palette.
4. Klicke **Zeichnen vorbereiten** / **Begin Drawing**. Das Overlay folgt deinem Mauszeiger.
5. Schiebe das Overlay über die Zeichenfläche von Paint und drücke Enter, um die Position zu fixieren. Mit Bild ↑ und Bild ↓ änderst du vorher die Größe.
6. Das Overlay zeigt die erste Farbe mit Namen und Hexwert. Wähle diese Farbe in Paint aus.
7. Klicke einmal auf die Titelleiste von Paint, damit das Fenster im Vordergrund ist, ohne dass ein Punkt entsteht. Drücke dann die Starttaste. ColorLayers zeichnet jetzt diese Farbebene.
8. Nach jeder Ebene zeigt das Overlay die nächste Farbe. Farbe in Paint wählen, Starttaste drücken, wiederholen.
9. Wenn etwas schiefgeht, drückst du Entf oder Ende. Das stoppt sofort.

Sobald das in Paint klappt, weißt du, wie sich deine Einstellungen verhalten, und kannst andere Zielprogramme ausprobieren.

## Die Bedienung im Detail

### Bild importieren

Lade ein Bild in der Hauptansicht. ColorLayers passt große Bilder automatisch auf eine handhabbare Größe an. Die endgültige Größe auf dem Bildschirm legst du später im Overlay fest.

Je größer das Bild, desto länger dauert das Zeichnen. Ein paar hundert Pixel Breite sind für ein Whiteboard meist mehr als genug.

### Farbanzahl

Mit der Farbanzahl bestimmst du die Obergrenze für das Motiv. Unter **Palette…** wählst du aus 16 Standardfarben oder gibst bis zu 16 eigene Hexfarben ein. Wähle die Farben, die du im Zielfenster tatsächlich einstellen kannst. Ähnliche Bildfarben können auf demselben verfügbaren Farbton landen.

- Weiß und fast weiß gelten als transparenter Hintergrund. Ein breiter Pinsel kann an Kanten etwas überstehen.
- Wenige Farben bedeuten weniger Farbwechsel und ein plakativeres Ergebnis.
- Viele Farben sehen näher am Original aus, kosten aber Zeit und Nerven, weil du jede Farbe einzeln im Zielprogramm auswählst.

Die Palette listet jede Farbe mit einem lesbaren Namen und dem Hexwert. Viele Zeichenprogramme erlauben die Eingabe eines Hexwerts, damit triffst du den Ton genau.

### Monochrom-Modus mit Farbanzahl 0

Bei der Farbanzahl 0 wird das Bild mit den klassischen Schwarzweißfiltern verarbeitet. Helligkeits- und Alphaschwellen stehen dann zur Verfügung. Das alte Zeichenmuster ist für den klassischen Lauf mit einem 1-Pixel-Stift verfügbar. Die Farbzeichnung plant ihre Wege automatisch.

### Das Overlay

Nach dem Start erscheint das Bild als Overlay, das dem Mauszeiger folgt. Darunter steht eine Statuszeile mit der aktuellen Farbe, ihrer Nummer, der Pinselbreite und den verfügbaren Tasten.

- Enter fixiert die Position. Ein zweites Enter löst sie wieder.
- Bild ↑ und Bild ↓ oder Num + und Num − ändern die Größe des Bildes.
- F7 schaltet zwischen Farbvorlage und Prognose um, wenn die Pinseleinstellungen oder die Konturvereinfachung das Motiv verändern.

Position und Größe kannst du nur vor der ersten Farbe ändern. Sobald die erste Ebene gezeichnet ist, bleibt das Bild an seinem Platz, damit die Farben sauber übereinanderliegen.

### Die Prognose

Ein breiter Pinsel deckt mehr ab als ein einzelnes Pixel. Kleine Details verschwinden dann oder laufen ineinander. Die Prognose rechnet aus, wie das Bild mit deinen Einstellungen ungefähr aussehen wird. Sie ist eine Annäherung und kein exaktes Abbild. Wie ein Zielprogramm Striche glättet, weichzeichnet oder an den Enden abrundet, kann ColorLayers nicht vorhersehen.

### Die Starttaste

Die Starttaste löst das Zeichnen der aktuellen Farbebene aus. Du kannst sie in den Einstellungen frei festlegen. Die Voreinstellung des Originalprojekts ist die linke Umschalttaste. Wenn du AutoDraw schon früher benutzt hast, steht dort eventuell noch eine andere Taste, zum Beispiel Einfg. Das Overlay zeigt immer an, welche Taste gerade gilt.

Beim ersten Druck auf die Starttaste merkt sich ColorLayers das Fenster, das gerade im Vordergrund ist, als Ziel. Klicke also vorher einmal in dein Zeichenprogramm. Wechselt das Vorderfenster während des Zeichnens oder verlässt der Mauszeiger das Zielfenster, hält ColorLayers an. Du kehrst dann zum Zielfenster zurück, wählst dieselbe Farbe und drückst erneut die Starttaste.

### Not-Aus

Entf oder Ende beenden das Zeichnen sofort und lassen die Maustaste los. Das funktioniert jederzeit, auch mitten in einem Strich. Halte im Zweifel einen Finger über einer der beiden Tasten.

### Pinselbreite kalibrieren

Damit ColorLayers weiß, wie breit dein Pinsel im Zielprogramm wirklich ist, gibt es eine Kalibrierung.

1. Stelle im Zielprogramm den Pinsel ein, mit dem du zeichnen willst.
2. Setze mit diesem Pinsel einen einzelnen Testpunkt auf die Zeichenfläche.
3. Starte in ColorLayers die Kalibrierung. Ein Kreis folgt deinem Mauszeiger.
4. Lege den Kreis über deinen Testpunkt und passe seine Größe mit Bild ↑ und Bild ↓ an, bis er den Punkt genau umschließt.
5. Enter speichert die Breite. Esc bricht ab, ohne etwas zu ändern.

Ist die Breite größer als ein Pixel, plant ColorLayers die Striche passend zur Pinselbreite und die Prognose wird verfügbar.

### Separater Konturpinsel

Aktiviere **Extra Kontur Stift** / **Separate outline brush**, wenn du Schwarz mit einer eigenen Pinselbreite zeichnen willst. Ohne Häkchen gilt die normale Breite. Die separat gespeicherte Konturbreite kannst du später wieder einschalten.

Seine Breite kalibrierst du genauso wie die normale Pinselbreite. Solange der Konturpinsel aktiv ist, erinnert dich eine rote Warnung daran, die Breite im Zielprogramm umzustellen, bevor die Kontur gezeichnet wird. Diese Warnung ist ernst gemeint. Wer mit dem breiten Flächenpinsel die feinen Umrisse zeichnet, bekommt einen sehr expressionistischen Hund.

### Reihenfolge der Farben

Unter **Konturen** kannst du dunkle Ebenen zuerst oder zuletzt zeichnen lassen. **Schmale Konturen weglassen** füllt dünne dunkle Ränder nach Möglichkeit mit angrenzenden Farben. Breite dunkle Flächen bleiben erhalten. Damit breite Pinsel schmalere Farbflächen noch erreichen, darf der Abdruck in diesem Modus etwas über den Rand hinausgehen. **Automatisch** nutzt einen separaten dünneren Konturstift zuletzt, zieht dunkle Ebenen bei mittleren Breiten vor und vereinfacht bei sehr breiten Pinseln die Konturen.

### Farbe und Pinsel im Zielprogramm wählen

ColorLayers bedient nur die Maus auf der Zeichenfläche. Farbe und Pinsel wählst du selbst im Zielprogramm aus, bevor du für die jeweilige Ebene die Starttaste drückst. Das Overlay zeigt dir dafür Namen, Hexwert und Breite der nächsten Farbe an. Ist eine Ebene so schmal, dass sie mit dem eingestellten Pinsel nichts zeichnen würde, überspringt ColorLayers sie und zeigt das an.

### Zusammenhängende Striche

ColorLayers folgt den Farbflächen waagerecht, senkrecht und diagonal. Zu einem offenen Ast darf der Pinsel ein kurzes Stück auf derselben Farbe zurückfahren. Die Route begrenzt diese Rückwege auf 24 Schritte und höchstens einen erneuten Besuch pro Pinselmittelpunkt. Für weitere Sprünge hebt ColorLayers die Maustaste. So spart die App Klickpausen, während sie die geplanten Farbflächen beibehält.

### Modi Detailliert und Schnell

**Detailliert** / **Detailed** ist die Standardeinstellung. **Schnell** / **Fast** nutzt dieselben Flächen mit kürzerem Abstand zwischen Mausbewegungen. Dunkle Ebenen behalten das detaillierte Tempo. Die Vorschau und der Zeichenlauf teilen sich den vorbereiteten Plan.

### Interval und Click Delay

Diese beiden Werte steuern das Tempo.

- **Interval** ist die Wartezeit zwischen zwei Mausbewegungen. Die Voreinstellung ist 1.
- **Click Delay** ist die Pause nach dem Loslassen der Maustaste, bevor der nächste Strich beginnt. Die Voreinstellung ist 100 ms.

Wenn das Zielprogramm Striche verschluckt oder Lücken entstehen, erhöhe zuerst Click Delay und dann Interval. Wenn alles sauber aussieht und dir langweilig wird, kannst du vorsichtig heruntergehen.

### Sprache

Die Oberfläche startet auf Englisch. Über **Language** im Reiter **General** kannst du zu Deutsch wechseln. Die zentralen Bedienelemente, das Overlay und die Kalibrierung wechseln mit. Einige übernommene AutoDraw-Ansichten bleiben auf Englisch.

## Tastenübersicht

| Taste | Wirkung |
|---|---|
| Starttaste, voreingestellt linke Umschalttaste | Aktuelle Farbebene zeichnen |
| Enter | Position im Overlay fixieren oder lösen, in der Kalibrierung speichern |
| Bild ↑ / Bild ↓ | Bild im Overlay vergrößern oder verkleinern, in der Kalibrierung Kreis anpassen |
| Num + / Num − | Wie Bild ↑ und Bild ↓ |
| F7 | Zwischen Vorlage und Prognose umschalten |
| Esc | Kalibrierung abbrechen |
| Entf / Ende | Not-Aus, sofort stoppen |

## Unterstützte Zielprogramme

ColorLayers erzeugt ganz normale Mausbewegungen und Klicks. Es funktioniert deshalb in Programmen, die solche Eingaben direkt auf ihrer Zeichenfläche verarbeiten.

- **Paint** ist die empfohlene erste Station und der Maßstab für alle Tests.
- Andere Zeichenprogramme im Fenstermodus funktionieren in der Regel, solange ein Mausstrich mit gedrückter Taste dort eine Linie zieht.
- Digitale Whiteboards im Browser funktionieren oft, sind aber empfindlicher. Manche glätten Striche, andere verwerfen schnelle Bewegungen. Plane hier höhere Werte für Interval und Click Delay ein.

Für Spiele oder Programme im exklusiven Vollbildmodus gibt es keine Zusage. Das Overlay muss über dem Zielfenster sichtbar sein, und das ist dort nicht garantiert. Programme, die mit erhöhten Rechten laufen, nehmen unter Umständen keine Eingaben von einem normal gestarteten ColorLayers an.

Wenn du mehrere Bildschirme mit unterschiedlicher Windows-Skalierung nutzt, kalibriere die Pinselbreite auf dem Bildschirm, auf dem du auch zeichnest.

## Fehlerbehebung

**Beim Druck auf die Starttaste passiert nichts.**
Prüfe im Overlay, welche Taste als Starttaste eingetragen ist. Klicke vorher auf die Titelleiste des Zielfensters, denn ColorLayers zeichnet nicht in sein eigenes Fenster. Falls noch die Prognose berechnet wird, warte einen Moment.

**Das Zeichnen hört mittendrin auf.**
Wahrscheinlich hat ein anderes Fenster den Fokus bekommen oder der Mauszeiger lag außerhalb des Zielfensters. Mach das Zielfenster groß genug für das ganze Bild, kehre zurück, wähle dieselbe Farbe und drücke die Starttaste.

**Es fehlen Teile von Strichen.**
Erhöhe Click Delay und Interval. Manche Programme brauchen nach dem Aufsetzen des Stifts einen Moment, bevor sie Bewegungen annehmen.

**Die Farben überdecken sich unschön.**
Kalibriere die Pinselbreite neu und schau dir die Prognose mit F7 an. Ein kleinerer Pinsel oder weniger Farben helfen oft mehr als jede andere Einstellung.

**Die Kontur ist viel zu dick.**
Wenn die rote Meldung erscheint, stell im Zielprogramm auf die kalibrierte Konturbreite um. Falls dir nur ein breiter Pinsel zur Verfügung steht, probier **Schmale Konturen weglassen** und prüfe die Flächen mit **F7**.

**Ich will einfach nur, dass es aufhört.**
Entf oder Ende.

## Geschichte des Projekts

AutoDraw begann als Herzensprojekt von Siydge. Zuerst war es ein Fork eines AutoIt-Skripts, das für mehr Roblox-Spiele angepasst wurde. Danach folgte eine überarbeitete Version einer .NET-WinForms-Anwendung von [rainzhao2000 (ursprünglicher autodrawer auf GitHub)](https://github.com/rainzhao2000/autodrawer). Später stieß AlexDalas, auch bekannt als gz9, zur Entwicklung dazu. Gemeinsam schrieben die beiden den Code komplett neu und wechselten auf Avalonia, um langfristig mehrere Desktop-Systeme zu unterstützen.

Die erste Generation, AutoDrawer 1.x, war in C# mit WPF geschrieben und lief nur unter Windows. Sie ist weiterhin im [Repository von AutoDrawer Version 1 bei AlexDalas](https://github.com/AlexDalas/autodrawer/) zu finden. Version 2 ist die Avalonia-Neufassung, auf der ColorLayers aufbaut.

## Links zum Originalprojekt

Alle folgenden Links führen zum ursprünglichen AutoDraw-Projekt und zu dessen Support.

- [Original-Repository von AutoDraw auf GitHub](https://github.com/auto-draw/autodraw)
- [AutoDraw-Entwicklungsstand für ColorLayers 1.2](https://github.com/auto-draw/autodraw/tree/782500235e9816eba197075c50b88e2b32d218ef)
- [AutoDraw-Tag v2.1.1, Basis von ColorLayers 1.0](https://github.com/auto-draw/autodraw/tree/v2.1.1)
- [Original-Releases von AutoDraw, ohne ColorLayers](https://github.com/auto-draw/autodraw/releases)
- [Originale Dokumentation von AutoDraw](https://github.com/auto-draw/autodraw/wiki)
- [Originaler Discord-Server von AutoDraw](https://discord.gg/rwvUFraDnb)

## Danksagung

Ein großer Dank geht an AlexDalas und Siydge für AutoDraw und an rainzhao2000 für die ursprüngliche Grundlage. Die Icons von AutoDraw stammen von [Icons8](https://icons8.com).
