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

Fase 2 (editor): si disegna e si modifica dalla riga di comando, come in DraftSight.

| Comando | Alias | |
| --- | --- | --- |
| LINEA | L, LINE | linee consecutive, opzioni Chiudi e Annulla |
| POLILINEA | PL, PLINE | |
| CERCHIO | C, CIRCLE | centro e raggio, opzione Diametro |
| ARCO | A, ARC | tre punti |
| RETTANGOLO | REC, RECT | due angoli |
| SPOSTA, COPIA, RUOTA | M, CO, RO | su selezione esistente o da selezionare |
| CANCELLA | E, CANC, tasto Canc | |
| ANNULLA, RIPETI | U, Ctrl+Z, Ctrl+Y | |
| SALVA, SALVACOME, APRI, NUOVO | Ctrl+S, Ctrl+Maiusc+S, Ctrl+O, Ctrl+N | chiede conferma se ci sono modifiche |
| ZOOM | Z | finestra o estensioni (Invio) |

Punti: clic, oppure `x,y`, `@dx,dy` (relativo), `d<angolo` e `@d<angolo` (polare, gradi), oppure solo un numero per
una distanza nella direzione del cursore. Invio, Spazio o tasto destro confermano; Invio a vuoto ripete l'ultimo comando;
Esc annulla. F3 accende e spegne gli snap (estremo, medio, centro, intersezione, perpendicolare, nodo), F8 l'ortho.

Selezione: clic su un'entità, oppure finestra con due clic (da sinistra a destra solo le entità interne,
da destra a sinistra anche quelle intersecate); Maiusc+clic toglie. Sulle entità selezionate compaiono i grip:
un clic su un grip lo sposta. Clic su un layer nel pannello per renderlo corrente.

Vista: rotella per lo zoom attorno al cursore, tasto centrale trascinato per il pan, doppio clic centrale per lo zoom estensioni.

Il DXF salvato conserva intatte le entità non toccate e quelle non ancora gestite (tratteggi, solidi...),
che restano visibili solo nel conteggio della barra di stato.
