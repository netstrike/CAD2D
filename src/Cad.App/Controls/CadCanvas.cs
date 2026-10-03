using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.App.Controls;

/// <summary>
/// Area di disegno: pan e zoom, inoltro di mouse e clic all'<see cref="Editing.Editor"/> e disegno con
/// <see cref="SceneDrawOperation"/> (SkiaSharp). Disegno e sovrapposizione sono istantanee immutabili,
/// così il thread di rendering non legge mai oggetti che l'interfaccia sta cambiando.
/// </summary>
public sealed class CadCanvas : Control
{
    private const double WheelZoomFactor = 1.2;

    /// <summary>Raggio di cattura di snap e selezione, in pixel.</summary>
    private const double AperturePixels = 8;

    /// <summary>Distanza in pixel entro cui un clic prende un grip.</summary>
    private const double GripPixels = 6;

    /// <summary>Oltre questo numero di entità selezionate i grip non si mostrano (come negli altri CAD).</summary>
    private const int MaxGripEntities = 100;

    /// <summary>Oltre questo tempo il tasto destro tenuto premuto apre il menu invece di dare Invio.</summary>
    private const int RightHoldMilliseconds = 300;

    private Point? _rightPress;
    private Avalonia.Threading.DispatcherTimer? _rightHold;

    /// <summary>Tasto destro tenuto premuto: la finestra apre il menu contestuale in quel punto.</summary>
    public event EventHandler<Point>? ContextMenuRequested;

    private static readonly CadColor HighlightColor = new(0x4F, 0xA3, 0xFF);

    private Editor? _editor;
    private SceneGeometry _geometry = SceneGeometry.Empty;
    private Overlay _overlay = Overlay.Empty;
    private Point? _panOrigin;
    private Vector2? _pointerWorld;

    public CadCanvas()
    {
        ClipToBounds = true;
        // Il mirino sostituisce il puntatore del sistema.
        Cursor = new Cursor(StandardCursorType.None);
    }

    public ViewTransform View { get; } = new();

    public Scene Scene => _geometry.Scene;

    public Editor? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null)
            {
                _editor.Document.Changed -= OnDocumentChanged;
                _editor.StateChanged -= OnEditorStateChanged;
                _editor.Selection.Changed -= OnEditorStateChanged;
            }

            _editor = value;
            if (_editor is not null)
            {
                _editor.Document.Changed += OnDocumentChanged;
                _editor.StateChanged += OnEditorStateChanged;
                _editor.Selection.Changed += OnEditorStateChanged;
            }

            RefreshScene();
            ZoomExtents();
        }
    }

    /// <summary>Raggio di cattura in unità di disegno alla scala attuale.</summary>
    public double Aperture => AperturePixels / View.Scale;

    public event EventHandler<Vector2>? CursorWorldPositionChanged;
    public event EventHandler? ViewChanged;

    /// <summary>Ricostruisce la scena dopo una modifica al documento o alla visibilità dei layer, senza cambiare la vista.</summary>
    public void RefreshScene()
    {
        // La geometria precedente non si libera esplicitamente: il thread di rendering potrebbe ancora disegnarla.
        _geometry = _editor is null ? SceneGeometry.Empty : SceneGeometry.Build(SceneBuilder.Build(_editor.Document), LightBackground);
        RefreshOverlay();
        OnViewChanged();
    }

    public void ZoomExtents()
    {
        View.ZoomExtents(_geometry.Scene.Bounds);
        OnViewChanged();
    }

    /// <summary>Torna a una vista salvata.</summary>
    public void RestoreView(Vector2 center, double scale)
    {
        View.SetView(center, scale);
        OnViewChanged();
    }

    public void ZoomWindow(Vector2 a, Vector2 b)
    {
        View.ZoomExtents(BoundingBox.FromPoints(a, b));
        OnViewChanged();
    }

    public override void Render(DrawingContext context)
    {
        // Render non deve modificare altri controlli: la vista si aggiorna solo in OnSizeChanged e negli handler di input.
        context.Custom(new SceneDrawOperation(new Rect(Bounds.Size), _geometry, _overlay, View.WorldToScreenMatrix, View.VisibleWorldBounds));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var firstLayout = e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0;
        View.SetViewport(e.NewSize.Width, e.NewSize.Height);
        if (firstLayout)
        {
            View.ZoomExtents(_geometry.Scene.Bounds);
        }

        OnViewChanged();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var world = ToWorld(point.Position);
        var properties = point.Properties;

        if (properties.IsMiddleButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                ZoomExtents();
            }
            else
            {
                _panOrigin = point.Position;
                e.Pointer.Capture(this);
            }
        }
        else if (_editor is null)
        {
            return;
        }
        else if (properties.IsRightButtonPressed)
        {
            // Tasto destro breve = Invio (conferma o ripete l'ultimo comando); tenuto premuto = menu contestuale.
            _rightPress = point.Position;
            _rightHold?.Stop();
            _rightHold = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(RightHoldMilliseconds), Avalonia.Threading.DispatcherPriority.Input, (_, _) =>
            {
                _rightHold?.Stop();
                _rightHold = null;
                if (_rightPress is { } at)
                {
                    _rightPress = null;
                    ContextMenuRequested?.Invoke(this, at);
                }
            });
            _rightHold.Start();
        }
        else if (properties.IsLeftButtonPressed)
        {
            if (!TryStartGripEdit(world))
            {
                _editor.Click(world, Aperture, removeFromSelection: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_panOrigin is { } origin)
        {
            View.PanByScreen(new Vector2(position.X - origin.X, position.Y - origin.Y));
            _panOrigin = position;
            OnViewChanged();
        }

        var world = ToWorld(position);
        _pointerWorld = world;
        if (_editor is not null)
        {
            _editor.Hover(world, Aperture);
        }
        else
        {
            RefreshOverlay();
        }

        CursorWorldPositionChanged?.Invoke(this, _editor?.Cursor ?? world);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerWorld = null;
        RefreshOverlay();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panOrigin is not null && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _panOrigin = null;
            e.Pointer.Capture(null);
        }
        else if (e.InitialPressMouseButton == MouseButton.Right && _rightPress is not null)
        {
            // Rilasciato prima del menu: è un Invio.
            _rightHold?.Stop();
            _rightHold = null;
            _rightPress = null;
            _editor?.SubmitText("");
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        View.ZoomAt(new Vector2(position.X, position.Y), Math.Pow(WheelZoomFactor, e.Delta.Y));
        OnViewChanged();
        e.Handled = true;
    }

    /// <summary>Senza comandi in corso, un clic su un grip di un'entità selezionata avvia lo stiramento.</summary>
    private bool TryStartGripEdit(Vector2 world)
    {
        if (_editor is null || _editor.IsCommandActive || _editor.WindowStart is not null || _editor.Selection.Count is 0 or > MaxGripEntities)
        {
            return false;
        }

        var tolerance = GripPixels / View.Scale;
        foreach (var entity in _editor.Selection.Items)
        {
            var grips = entity.Grips;
            for (var i = 0; i < grips.Count; i++)
            {
                if (Vector2.Distance(grips[i], world) <= tolerance)
                {
                    _editor.StartGripEdit(entity, i);
                    return true;
                }
            }
        }

        return false;
    }

    private Vector2 ToWorld(Point p) => View.ScreenToWorld(new Vector2(p.X, p.Y));

    private void OnDocumentChanged(object? sender, EventArgs e) => RefreshScene();

    private void OnEditorStateChanged(object? sender, EventArgs e) => RefreshOverlay();

    /// <summary>Sfondo bianco (tema chiaro): il colore 7 si disegna nero.</summary>
    public bool LightBackground { get; set; }

    /// <summary>Spessori di linea visibili (LWT).</summary>
    public bool ShowLineweights { get; set; }

    /// <summary>Ridisegna la sovrapposizione dopo un cambio di impostazioni (griglia, tracciamento).</summary>
    public void InvalidateOverlay() => RefreshOverlay();

    private void RefreshOverlay()
    {
        if (_editor is null)
        {
            _overlay = Overlay.Empty with { Crosshair = _pointerWorld };
            InvalidateVisual();
            return;
        }

        var selected = _editor.Selection.Items;
        var highlight = selected.Count == 0
            ? []
            : SceneBuilder.BuildEntities(selected, HighlightColor).Batches.SelectMany(b => b.Polylines).ToList();
        var grips = selected.Count is 0 or > MaxGripEntities || _editor.IsCommandActive
            ? []
            : selected.SelectMany(s => s.Grips).ToList();

        var preview = new List<(CadColor, Vector2[])>();
        var previewEntities = _editor.GetPreview();
        if (previewEntities.Count > 0)
        {
            foreach (var batch in SceneBuilder.BuildEntities(previewEntities).Batches)
            {
                preview.AddRange(batch.Polylines.Select(p => (batch.Color, p)));
            }
        }

        var cursor = _pointerWorld is null ? (Vector2?)null : _editor.Cursor;
        var rubberFrom = !_editor.IsSelecting && _editor.BasePoint is { } basePoint && cursor is not null ? basePoint : (Vector2?)null;
        var window = _editor.WindowStart is { } start && _pointerWorld is { } raw ? (start, raw) : ((Vector2, Vector2)?)null;

        // L'oggetto sotto il cursore si illumina prima del clic (non se è già selezionato).
        var hover = _pointerWorld is not null && _editor.HoverEntity is { } hovered && !_editor.Selection.Contains(hovered)
            ? SceneBuilder.BuildEntities([hovered], HighlightColor).Batches.SelectMany(b => b.Polylines).ToList()
            : [];

        var tracking = _pointerWorld is null ? null : _editor.CurrentTracking;
        _overlay = new Overlay(highlight, grips, preview, rubberFrom, cursor, window, _pointerWorld is null ? null : _editor.CurrentSnap, cursor, hover)
        {
            TrackingLines = tracking?.Lines ?? [],
            TrackingLabel = tracking?.Label,
            TrackingPoint = tracking?.Point,
            Acquired = [.. _editor.AcquiredPoints],
            GridSpacing = _editor.GridVisible ? _editor.GridSpacing : 0,
            ShowLineweights = ShowLineweights,
        };
        InvalidateVisual();
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
