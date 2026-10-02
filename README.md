# CAD2D

CAD 2D personale per Windows, ispirato a DraftSight: riga di comando, snap, layer, quote e file DXF/DWG.

Stack: C# su .NET 8, Avalonia per l'interfaccia, SkiaSharp per il disegno, ACadSharp per DXF/DWG (dalla fase 1).

## Struttura

| Progetto | Contenuto |
| --- | --- |
| `src/Cad.Geometry` | Vettori, matrici, bounding box, segmenti, cerchi, intersezioni. Nessuna dipendenza dalla UI. |
| `src/Cad.Rendering` | Trasformazione vista (mondo ↔ schermo), pan e zoom. |
| `src/Cad.App` | Applicazione Avalonia con l'area di disegno SkiaSharp. |
| `tests/*` | Test xUnit per ogni libreria. |

## Comandi

```
dotnet build
dotnet test
dotnet run --project src/Cad.App
```

## Stato

Fase 0 (fondamenta): finestra con 10.000 linee di prova, zoom con la rotella attorno al cursore,
pan trascinando con il tasto centrale, zoom estensioni con doppio clic centrale o tasto F.
