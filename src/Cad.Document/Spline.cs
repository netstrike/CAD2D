using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Spline (NURBS) come nei file DXF/DWG: grado, punti di controllo, nodi ed eventuali pesi. Quelle disegnate con
/// SPLINE tengono anche i punti di passaggio, che diventano i grip: spostarne uno ricalcola la curva.
/// </summary>
public sealed class SplineEntity : Entity
{
    private const int MinStepsPerSpan = 4;
    private const int MaxStepsPerSpan = 64;

    /// <summary>Come per archi ed ellissi: un punto ogni 2° circa di cambio di direzione.</summary>
    private const double MaxTurnPerStep = Math.PI / 90;
    private Vector2[]? _points;

    public SplineEntity(Layer layer, int degree, IEnumerable<Vector2> controlPoints, IEnumerable<double> knots, IEnumerable<double>? weights = null, bool isClosed = false)
        : base(layer)
    {
        Degree = degree;
        ControlPoints = [.. controlPoints];
        Knots = [.. knots];
        Weights = weights is null ? [] : [.. weights];
        IsClosed = isClosed;
        if (Degree < 1 || ControlPoints.Count <= Degree || Knots.Count != ControlPoints.Count + Degree + 1)
        {
            throw new ArgumentException("Spline non valida: servono almeno grado + 1 punti di controllo e punti + grado + 1 nodi.");
        }
    }

    public int Degree { get; }
    public IReadOnlyList<Vector2> ControlPoints { get; }
    public IReadOnlyList<double> Knots { get; }

    /// <summary>Pesi dei punti di controllo; vuota se valgono tutti 1 (spline non razionale).</summary>
    public IReadOnlyList<double> Weights { get; }

    /// <summary>Punti di passaggio da cui la spline è stata calcolata; vuota per le spline lette solo come punti di controllo.</summary>
    public IReadOnlyList<Vector2> FitPoints { get; private init; } = [];

    public bool IsClosed { get; }

    /// <summary>Nodi non bloccati agli estremi: i primi punti di controllo si ripetono in fondo e la curva si richiude liscia.</summary>
    public bool IsPeriodic => Knots[0] < Knots[Degree];

    public double StartParameter => Knots[Degree];
    public double EndParameter => Knots[^(Degree + 1)];

    public override BoundingBox Bounds => BoundingBox.FromPoints(Flatten());

    public override IReadOnlyList<Vector2> Grips => FitPoints.Count > 0 ? FitPoints : ControlPoints;

    public Vector2 StartPoint => PointAt(StartParameter);
    public Vector2 EndPoint => PointAt(EndParameter);

    /// <summary>
    /// Spline cubica che passa per i punti dati (con 2 o 3 punti il grado scende a 1 o 2). Aperta: interpolazione
    /// globale con parametri a lunghezza di corda. Chiusa: spline periodica uniforme, liscia anche nel punto di chiusura.
    /// </summary>
    public static SplineEntity Through(Layer layer, IReadOnlyList<Vector2> points, bool closed)
    {
        var fit = RemoveDuplicates(points, closed);
        if (fit.Count < 2)
        {
            throw new ArgumentException("Servono almeno due punti distinti.");
        }

        return closed && fit.Count >= 3 ? Periodic(layer, fit) : Open(layer, fit);
    }

    /// <summary>Punto della curva al parametro <paramref name="u"/> (algoritmo di de Boor, con i pesi se ci sono).</summary>
    public Vector2 PointAt(double u)
    {
        var p = Degree;
        u = Math.Clamp(u, StartParameter, EndParameter);
        var span = FindSpan(u);
        var rational = Weights.Count == ControlPoints.Count;
        var x = new double[p + 1];
        var y = new double[p + 1];
        var w = new double[p + 1];
        for (var j = 0; j <= p; j++)
        {
            var index = span - p + j;
            var weight = rational ? Weights[index] : 1;
            x[j] = ControlPoints[index].X * weight;
            y[j] = ControlPoints[index].Y * weight;
            w[j] = weight;
        }

        for (var r = 1; r <= p; r++)
        {
            for (var j = p; j >= r; j--)
            {
                var i = span - p + j;
                var denominator = Knots[i + p - r + 1] - Knots[i];
                var alpha = denominator == 0 ? 0 : (u - Knots[i]) / denominator;
                x[j] = (1 - alpha) * x[j - 1] + alpha * x[j];
                y[j] = (1 - alpha) * y[j - 1] + alpha * y[j];
                w[j] = (1 - alpha) * w[j - 1] + alpha * w[j];
            }
        }

        return new Vector2(x[p] / w[p], y[p] / w[p]);
    }

    /// <summary>
    /// La curva come spezzata. Ogni tratto tra due nodi ha tanti passi quanto gira il poligono di controllo che lo
    /// governa (che racchiude la curva), così le parti quasi dritte costano pochi punti e le curve strette restano lisce.
    /// </summary>
    public IReadOnlyList<Vector2> Flatten()
    {
        if (_points is not null)
        {
            return _points;
        }

        var result = new List<Vector2>();
        for (var k = Degree; k < Knots.Count - Degree - 1; k++)
        {
            var from = Knots[k];
            var to = Knots[k + 1];
            if (to - from <= 0)
            {
                continue;
            }

            var steps = StepsFor(k);
            for (var s = result.Count == 0 ? 0 : 1; s <= steps; s++)
            {
                result.Add(PointAt(from + (to - from) * s / steps));
            }
        }

        if (result.Count == 0)
        {
            result.Add(PointAt(StartParameter));
        }

        return _points = [.. result];
    }

    /// <summary>Lunghezza approssimata lungo la spezzata.</summary>
    public double Length
    {
        get
        {
            var points = Flatten();
            var length = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                length += Vector2.Distance(points[i - 1], points[i]);
            }

            return length;
        }
    }

    // Le trasformazioni affini si applicano ai punti di controllo: la curva trasformata è esatta.
    protected override Entity TransformCore(Matrix2D m) =>
        new SplineEntity(Layer, Degree, ControlPoints.Select(m.Transform), Knots, Weights.Count > 0 ? Weights : null, IsClosed)
        {
            FitPoints = [.. FitPoints.Select(m.Transform)],
        };

    protected override Entity MoveGripCore(int index, Vector2 position)
    {
        if (FitPoints.Count > 0)
        {
            var fit = FitPoints.ToList();
            fit[index] = position;
            return Through(Layer, fit, IsClosed);
        }

        var control = ControlPoints.ToList();
        control[index] = position;
        return new SplineEntity(Layer, Degree, control, Knots, Weights.Count > 0 ? Weights : null, IsClosed);
    }

    private int StepsFor(int span)
    {
        var turn = 0.0;
        for (var i = span - Degree + 1; i < span; i++)
        {
            var a = ControlPoints[i] - ControlPoints[i - 1];
            var b = ControlPoints[i + 1] - ControlPoints[i];
            if (a.Length > 0 && b.Length > 0)
            {
                turn += Math.Abs(Math.Atan2(Vector2.Cross(a, b), Vector2.Dot(a, b)));
            }
        }

        return Math.Clamp((int)Math.Ceiling(turn / MaxTurnPerStep), MinStepsPerSpan, MaxStepsPerSpan);
    }

    private int FindSpan(double u)
    {
        var last = ControlPoints.Count - 1;
        if (u >= Knots[last + 1])
        {
            // A fine dominio vale l'ultimo tratto non vuoto.
            var span = last;
            while (span > Degree && Knots[span] >= Knots[span + 1])
            {
                span--;
            }

            return span;
        }

        var low = Degree;
        var high = last + 1;
        while (high - low > 1)
        {
            var mid = (low + high) / 2;
            if (u < Knots[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    private static List<Vector2> RemoveDuplicates(IReadOnlyList<Vector2> points, bool closed)
    {
        var result = new List<Vector2>();
        foreach (var point in points)
        {
            if (result.Count == 0 || !result[^1].IsAlmostEqual(point))
            {
                result.Add(point);
            }
        }

        if (closed && result.Count > 2 && result[0].IsAlmostEqual(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static SplineEntity Open(Layer layer, List<Vector2> fit)
    {
        var n = fit.Count - 1;
        var p = Math.Min(3, n);

        // Parametri proporzionali alla lunghezza delle corde, nodi per media (Piegl e Tiller, A9.1).
        var total = 0.0;
        var t = new double[n + 1];
        for (var k = 1; k <= n; k++)
        {
            total += Vector2.Distance(fit[k - 1], fit[k]);
            t[k] = total;
        }

        for (var k = 1; k <= n; k++)
        {
            t[k] /= total;
        }

        var knots = new double[n + p + 2];
        for (var j = 0; j <= p; j++)
        {
            knots[j] = 0;
            knots[n + 1 + j] = 1;
        }

        for (var j = 1; j <= n - p; j++)
        {
            var sum = 0.0;
            for (var i = j; i < j + p; i++)
            {
                sum += t[i];
            }

            knots[j + p] = sum / p;
        }

        // Sistema N·P = Q: riga k = funzioni di base valutate in t[k].
        var matrix = new double[n + 1, n + 1];
        for (var k = 0; k <= n; k++)
        {
            for (var i = 0; i <= n; i++)
            {
                matrix[k, i] = Basis(knots, p, i, t[k], n);
            }
        }

        var control = Solve(matrix, fit);
        return new SplineEntity(layer, p, control, knots) { FitPoints = fit };
    }

    /// <summary>
    /// Spline cubica periodica uniforme per n punti: i punti di controllo risolvono (P[i-1] + 4P[i] + P[i+1]) / 6 = Q[i]
    /// in modo ciclico; i primi tre si ripetono in fondo e i nodi sono uniformi, come le spline periodiche dei DXF.
    /// </summary>
    private static SplineEntity Periodic(Layer layer, List<Vector2> fit)
    {
        var n = fit.Count;
        var matrix = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            matrix[i, (i + n - 1) % n] += 1.0 / 6;
            matrix[i, i] += 4.0 / 6;
            matrix[i, (i + 1) % n] += 1.0 / 6;
        }

        var solved = Solve(matrix, fit);

        // Con P[i] in posizione i+1 la curva al nodo i+3 passa per Q[i]: si comincia da P[n-1].
        var control = new List<Vector2> { solved[n - 1] };
        control.AddRange(solved);
        control.Add(solved[0]);
        control.Add(solved[1]);
        var knots = Enumerable.Range(0, control.Count + 4).Select(i => (double)i).ToList();
        return new SplineEntity(layer, 3, control, knots, isClosed: true) { FitPoints = fit };
    }

    /// <summary>Funzione di base N[i,p](u) di Cox-de Boor; all'ultimo nodo vale 1 solo l'ultima.</summary>
    private static double Basis(IReadOnlyList<double> knots, int p, int i, double u, int last)
    {
        if (u >= knots[^1])
        {
            return i == last ? 1 : 0;
        }

        var n = new double[p + 1];
        for (var j = 0; j <= p; j++)
        {
            n[j] = u >= knots[i + j] && u < knots[i + j + 1] ? 1 : 0;
        }

        for (var k = 1; k <= p; k++)
        {
            for (var j = 0; j <= p - k; j++)
            {
                var left = knots[i + j + k] - knots[i + j];
                var right = knots[i + j + k + 1] - knots[i + j + 1];
                n[j] = (left == 0 ? 0 : (u - knots[i + j]) / left * n[j]) + (right == 0 ? 0 : (knots[i + j + k + 1] - u) / right * n[j + 1]);
            }
        }

        return n[0];
    }

    /// <summary>Eliminazione di Gauss con pivot parziale per le due coordinate insieme.</summary>
    private static List<Vector2> Solve(double[,] a, IReadOnlyList<Vector2> b)
    {
        var size = b.Count;
        var x = b.Select(v => v.X).ToArray();
        var y = b.Select(v => v.Y).ToArray();
        for (var col = 0; col < size; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < size; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (pivot != col)
            {
                for (var k = 0; k < size; k++)
                {
                    (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                }

                (x[col], x[pivot]) = (x[pivot], x[col]);
                (y[col], y[pivot]) = (y[pivot], y[col]);
            }

            for (var row = col + 1; row < size; row++)
            {
                var factor = a[row, col] / a[col, col];
                if (factor == 0)
                {
                    continue;
                }

                for (var k = col; k < size; k++)
                {
                    a[row, k] -= factor * a[col, k];
                }

                x[row] -= factor * x[col];
                y[row] -= factor * y[col];
            }
        }

        var result = new Vector2[size];
        for (var row = size - 1; row >= 0; row--)
        {
            double sx = x[row], sy = y[row];
            for (var k = row + 1; k < size; k++)
            {
                sx -= a[row, k] * result[k].X;
                sy -= a[row, k] * result[k].Y;
            }

            result[row] = new Vector2(sx / a[row, row], sy / a[row, row]);
        }

        return [.. result];
    }
}
