using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.App.Controls;

/// <summary>
/// Area di disegno: gestisce pan e zoom e delega il disegno a <see cref="SceneDrawOperation"/> (SkiaSharp).
/// </summary>
public sealed class CadCanvas : Control
{
    private const double WheelZoomFactor = 1.2;

    private IReadOnlyList<Segment2D> _scene = [];
    private SceneGeometry _geometry = SceneGeometry.Empty;
    private Point? _panOrigin;
    private bool _initialFitDone;

    public CadCanvas()
    {
        ClipToBounds = true;
    }

    public ViewTransform View { get; } = new();

    public IReadOnlyList<Segment2D> Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            _geometry.Dispose();
            _geometry = SceneGeometry.Build(value);
            _initialFitDone = Bounds.Width > 0;
            if (_initialFitDone)
            {
                ZoomExtents();
            }
        }
    }

    public event EventHandler<Vector2>? CursorWorldPositionChanged;
    public event EventHandler? ViewChanged;

    public void ZoomExtents()
    {
        View.ZoomExtents(_geometry.Bounds);
        OnViewChanged();
    }

    public override void Render(DrawingContext context)
    {
        // Render non deve modificare altri controlli: la vista si aggiorna solo in OnSizeChanged e negli handler di input.
        context.Custom(new SceneDrawOperation(new Rect(Bounds.Size), _geometry, View.WorldToScreenMatrix));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        View.SetViewport(e.NewSize.Width, e.NewSize.Height);
        if (!_initialFitDone)
        {
            _initialFitDone = true;
            View.ZoomExtents(_geometry.Bounds);
        }

        OnViewChanged();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsMiddleButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ZoomExtents();
        }
        else
        {
            _panOrigin = point.Position;
            e.Pointer.Capture(this);
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

        CursorWorldPositionChanged?.Invoke(this, View.ScreenToWorld(new Vector2(position.X, position.Y)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panOrigin is not null && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _panOrigin = null;
            e.Pointer.Capture(null);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F)
        {
            ZoomExtents();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _geometry.Dispose();
        _geometry = SceneGeometry.Empty;
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
