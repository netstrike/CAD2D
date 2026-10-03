# CAD2D

CAD 2D personale per Windows, ispirato a DraftSight: riga di comando, snap, layer, quote e file DXF/DWG.

Stack: C# su .NET 8, Avalonia per l'interfaccia, SkiaSharp per il disegno, ACadSharp per DXF/DWG (dalla fase 1).

## Struttura

| Progetto | Contenuto |
| --- | --- |
| `src/Cad.Geometry` | Vettori, matrici, bounding box, segmenti, cerchi, intersezioni. Nessuna dipendenza dalla UI. |
| `src/Cad.Document` | Modello del disegno: layer, entità, blocchi, indice spaziale (R-tree). |
| `src/Cad.IO` | Lettura e scrittura DXF con ACadSharp, conversione dei codici di testo. Il salvataggio conserva tutto ciò che non è stato modificato. |
| `src/Cad.Rendering` | Vista (mondo ↔ schermo) e scena pronta da disegnare: blocchi esplosi, colori risolti, curve approssimate. |
| `src/Cad.Editing` | Editor senza interfaccia (testabile): comandi, riga di comando, snap, ortho, selezione, grip. |
| `src/Cad.App` | Applicazione Avalonia: area di disegno SkiaSharp, riga di comando, pannello layer, apertura e salvataggio. |
| `tests/*` | Test xUnit per ogni libreria. |
| `tools/Cad.DxfCheck` | Apre tutti i DXF di una cartella e riassume l'esito: utile per provare file reali. |
| `tools/genera_campione.py` | Rigenera `samples/demo.dxf` (richiede `pip install ezdxf`). |

## Comandi

```
dotnet build
dotnet test
dotnet run --project src/Cad.App                      # apre samples/demo.dxf
dotnet run --project src/Cad.App -- C:\disegni\tavola.dxf
dotnet run --project tools/Cad.DxfCheck -- C:\disegni   # prova tutti i DXF di una cartella
```

## Stato

Fase 3.5 (interfaccia): barra multifunzione, palette Proprietà e Layer, inserimento rapido vicino al cursore,
tracciamento polare ed ETrack, gestore layer con spessori di linea, finestre per tratteggio, stile di quota e opzioni,
tema chiaro. Ogni comando si raggiunge da barra o menu, senza scriverne il nome.

Fase 3 (disegno tecnico): si fa una tavola completa, con quote, tratteggi, blocchi e tipi di linea.
`samples/tavola.dxf` è stata disegnata solo con i comandi di CAD2D (vedi `tests/Cad.IO.Tests/SampleDrawing.cs`).

### Disegno

| Comando | Alias | |
| --- | --- | --- |
| LINEA | L, LINE | linee consecutive, opzioni Chiudi e Annulla |
| POLILINEA | PL, PLINE | |
| CERCHIO | C, CIRCLE | centro e raggio, opzione Diametro |
| ARCO | A, ARC | tre punti |
| RETTANGOLO | REC, RECT | due angoli |
| TRATTEGGIO | H, BH, HATCH | clic dentro un contorno chiuso (le isole si riconoscono da sole); opzioni Motivo, Scala, Angolo, Seleziona |
| BLOCCO | B, BLOCK | nome, punto base, oggetti; gli oggetti diventano un'istanza del blocco |
| INSERISCI | I, INSERT | nome (`?` per l'elenco), punto; opzioni Scala e Rotazione |

### Modifica

| Comando | Alias | |
| --- | --- | --- |
| SPOSTA, COPIA, RUOTA | M, CO, RO | su selezione esistente o da selezionare |
| SPECCHIA, SCALA | MI, SC | |
| OFFSET | O | distanza o punto di passaggio |
| TAGLIA, ESTENDI | TR, EX | Invio = tutti gli oggetti fanno da limite |
| RACCORDA, CIMA | F, CHA | due linee, opzione Polilinea per tutti gli spigoli |
| SERIE | AR | rettangolare o polare |
| ESPLODI | X | blocchi, polilinee, quote |
| CANCELLA | E, CANC, tasto Canc | |
| ANNULLA, RIPETI | U, Ctrl+Z, Ctrl+Y | |

### Testi e quote

| Comando | Alias | |
| --- | --- | --- |
| TESTO | DT, TEXT | una riga per volta; opzioni Centro, Destra, Mezzo |
| TESTOM | T, MT | testo su più righe |
| MODIFICATESTO | ED | propone il testo attuale sulla riga di comando (anche per le quote; `<>` è la misura) |
| QLINEARE | QL, DLI | orizzontale o verticale secondo dove si porta la quota; opzione Testo |
| QALLINEATA | QA, DAL | |
| QRAGGIO, QDIAMETRO | QR/DRA, QD/DDI | cerchi, archi e raccordi di polilinea |
| QANGOLARE | QAN, DAN | due linee (anche lati di polilinea) o un arco |
| STILEQUOTA | DST | altezza testo, frecce, decimali, scala globale (stile ISO-25, virgola decimale) |

### Proprietà e layer

| Comando | Alias | |
| --- | --- | --- |
| LAYER | LA | Nuovo, Corrente, Colore, Tipolinea, Accendi/Spegni, Blocca/Sblocca, Congela/Scongela, Rinomina, Elimina, Elenco |
| COLORE | COL | per la selezione o per i nuovi oggetti |
| TIPOLINEA | LT | CONTINUOUS, DASHED, HIDDEN, CENTER, DASHDOT, PHANTOM, DOT |
| SCALATL | LTS | scala globale dei tipi di linea |

Le caselle in Home > Layer e proprietà cambiano layer, colore e tipo di linea degli oggetti selezionati, oppure quelli
dei nuovi oggetti se non c'è selezione. La palette Proprietà (a destra) mostra e modifica tutti i dati degli oggetti
selezionati: geometria, testo, quota, spessore di linea; i valori diversi tra più oggetti appaiono come *Vari*.
Il gestore layer (Home > Layer) è una tabella con stato, colore, tipo e spessore di linea di ogni layer; la palette
Layer ne è la versione compatta. LWT nella barra di stato mostra gli spessori.

### Misure e appunti

| Comando | Alias | |
| --- | --- | --- |
| DISTANZA | DI | distanza, angolo, delta X e Y tra due punti |
| AREA | AA | area e perimetro per punti, oppure di un oggetto chiuso (opzione Oggetto) |
| ID | | coordinate di un punto |
| COPIACLIP, TAGLIACLIP, INCOLLACLIP | Ctrl+C, Ctrl+X, Ctrl+V | anche da un disegno all'altro (layer e blocchi mancanti vengono creati) |
| COPIABASE | Ctrl+Maiusc+C | copia con punto base |
| GRIGLIA, POLARE | | passo della griglia, incremento del tracciamento polare |

### File e vista

| Comando | Alias | |
| --- | --- | --- |
| SALVA, SALVACOME, APRI, NUOVO | Ctrl+S, Ctrl+Maiusc+S, Ctrl+O, Ctrl+N | chiede conferma se ci sono modifiche |
| ZOOM | Z | finestra o estensioni (Invio) |

Punti: clic, oppure `x,y`, `@dx,dy` (relativo), `d<angolo` e `@d<angolo` (polare, gradi), oppure solo un numero per
una distanza nella direzione del cursore. Invio, Spazio o tasto destro confermano; Invio a vuoto ripete l'ultimo comando;
Esc annulla. Tasto destro breve = Invio, tenuto premuto = menu contestuale.

Aiuti al disegno, nella barra di stato: F3 snap agli oggetti (tasto destro: tipi), F7 griglia, F8 ortho, F9 aggancio
alla griglia, F10 tracciamento polare (tasto destro: incremento), F11 ETrack (fermati un attimo su uno snap per
acquisirne il punto, poi segui le guide orizzontali e verticali), F12 inserimento rapido. Con l'inserimento rapido
si scrive la distanza, Tab la blocca, poi si scrive l'angolo. Sulla riga di comando: completamento dei nomi mentre si
scrive, frecce su e giù per lo storico, opzioni del comando cliccabili.

Selezione: clic su un'entità, oppure finestra con due clic (da sinistra a destra solo le entità interne,
da destra a sinistra anche quelle intersecate); Maiusc+clic toglie; un secondo clic nello stesso punto passa
all'oggetto sovrapposto successivo; Ctrl+A seleziona tutto. Sulle entità selezionate compaiono i grip:
un clic su un grip lo sposta (anche i punti delle quote).

Vista: rotella per lo zoom attorno al cursore, tasto centrale trascinato per il pan, doppio clic centrale per lo zoom estensioni.

Il DXF salvato conserva intatte le entità non toccate e quelle non ancora gestite, che restano visibili solo nel
conteggio della barra di stato. Le quote lette dal file mantengono la loro grafica finché non vengono modificate.
