# CAD2D

CAD 2D personale per Windows, ispirato a DraftSight: riga di comando, snap, layer, quote e file DXF/DWG.

Stack: C# su .NET 8, Avalonia per l'interfaccia, SkiaSharp per il disegno, ACadSharp per DXF/DWG (dalla fase 1).

## Struttura

| Progetto | Contenuto |
| --- | --- |
| `src/Cad.Geometry` | Vettori, matrici, bounding box, segmenti, cerchi, intersezioni. Nessuna dipendenza dalla UI. |
| `src/Cad.Document` | Modello del disegno: layer, entità, blocchi, indice spaziale (R-tree). |
| `src/Cad.IO` | Lettura DXF (ASCII e binario) con ACadSharp, conversione dei codici di testo. |
| `src/Cad.Rendering` | Vista (mondo ↔ schermo) e scena pronta da disegnare: blocchi esplosi, colori risolti, curve approssimate. |
| `src/Cad.App` | Applicazione Avalonia: area di disegno SkiaSharp, pannello layer, apertura file. |
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

Fase 1 (visualizzatore DXF): apre file DXF da File > Apri (Ctrl+O), trascinandoli sulla finestra o dalla riga di comando.
Disegna linee, cerchi, archi, ellissi, polilinee con archi, spline, punti, testi, testi multiriga, blocchi (anche annidati)
e quote, con i colori dei layer. Il pannello a sinistra accende e spegne i layer.
Le entità non ancora gestite (per esempio tratteggi e solidi) sono contate nella barra di stato.

Comandi della vista: rotella per lo zoom attorno al cursore, tasto centrale trascinato per il pan,
doppio clic centrale o F per lo zoom estensioni.
