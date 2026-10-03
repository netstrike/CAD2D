using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.App;

/// <summary>
/// Aiuti all'inserimento: interruttori della barra di stato, inserimento rapido vicino al cursore, completamento e
/// storico della riga di comando, opzioni cliccabili.
/// </summary>
public partial class MainWindow
{
    private const int MaxCommandHistory = 50;
    private readonly List<string> _commandHistory = [];
    private int _historyIndex = -1;
    private Point? _pointerOnCanvas;

    // ---------- Interruttori della barra di stato ----------

    /// <summary>Ogni interruttore con il tasto funzione, la proprietà dell'editor e il nome mostrato nello storico.</summary>
    private (ToggleButton Toggle, Key Key, Action<Editor, bool> Apply, string Name)[] DraftingToggles =>
    [
        (SnapToggle, Key.F3, (ed, on) => ed.SnapEnabled = on, "Snap"),
        (GridToggle, Key.F7, (ed, on) => ed.GridVisible = on, "Griglia"),
        (OrthoToggle, Key.F8, (ed, on) => ed.OrthoEnabled = on, "Ortho"),
        (GridSnapToggle, Key.F9, (ed, on) => ed.GridSnapEnabled = on, "Aggancio alla griglia"),
        (PolarToggle, Key.F10, (ed, on) => ed.PolarEnabled = on, "Tracciamento polare"),
        (TrackToggle, Key.F11, (ed, on) => ed.TrackingEnabled = on, "ETrack"),
        (LineweightToggle, Key.None, (_, on) => { Canvas.ShowLineweights = on; Canvas.RefreshScene(); }, "Spessori di linea"),
        (QuickInputToggle, Key.F12, (_, _) => { }, "Inserimento rapido"),
    ];

    private void InitializeDraftingToggles()
    {
        foreach (var (toggle, _, apply, _) in DraftingToggles)
        {
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (_editor is not null)
                {
                    apply(_editor, toggle.IsChecked == true);
                    Canvas.InvalidateOverlay();
                }

                UpdateQuickInput();
            };
        }

        // Tasto destro su POLARE: incrementi angolari tipici.
        var angles = new[] { 90.0, 45, 30, 22.5, 15, 10, 5 };
        var items = angles.Select(a => new MenuItem { Header = $"{a.ToString(CultureInfo.CurrentCulture)}°", ToggleType = MenuItemToggleType.Radio, Tag = a }).ToList();
        foreach (var item in items)
        {
            item.Click += (_, _) =>
            {
                if (_editor is not null)
                {
                    _editor.PolarIncrementDegrees = (double)item.Tag!;
                    _polarIncrement = _editor.PolarIncrementDegrees;
                    PolarToggle.IsChecked = true;
                    AppendHistory($"<Tracciamento polare ogni {item.Header}>");
                }

                CommandBox.Focus();
            };
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Opening += (_, _) =>
        {
            foreach (var item in items)
            {
                item.IsChecked = _editor is not null && Math.Abs((double)item.Tag! - _editor.PolarIncrementDegrees) < 1e-9;
            }
        };
        PolarToggle.ContextMenu = menu;
    }

    private double _polarIncrement = 45;

    /// <summary>Riporta sull'editor nuovo (nuovo disegno o apertura) lo stato degli interruttori.</summary>
    private void ApplyDraftingToggles(Editor editor)
    {
        foreach (var (toggle, _, apply, _) in DraftingToggles)
        {
            apply(editor, toggle.IsChecked == true);
        }

        editor.PolarIncrementDegrees = _polarIncrement;
    }

    /// <summary>F3, F7-F12: inverte l'interruttore e lo scrive nello storico.</summary>
    private bool HandleFunctionKey(Key key)
    {
        foreach (var (toggle, k, _, name) in DraftingToggles)
        {
            if (k == key && k != Key.None)
            {
                toggle.IsChecked = toggle.IsChecked != true;
                AppendHistory($"<{name} {(toggle.IsChecked == true ? "attivo" : "disattivato")}>");
                return true;
            }
        }

        return false;
    }

    // ---------- Inserimento rapido ----------

    private void InitializeQuickInput()
    {
        Canvas.PointerMoved += (_, e) =>
        {
            _pointerOnCanvas = e.GetPosition(Canvas);
            UpdateQuickInput();
        };
        Canvas.PointerExited += (_, _) =>
        {
            _pointerOnCanvas = null;
            UpdateQuickInput();
        };
        CommandBox.TextChanged += (_, _) =>
        {
            UpdateQuickInput();
            UpdateCompletion();
        };
    }

    /// <summary>
    /// Riquadro vicino al cursore: la richiesta in corso e, per i punti con punto base, distanza e angolo che seguono
    /// il mouse. Il testo scritto finisce nel campo attivo; Tab lo blocca e passa al campo dopo.
    /// </summary>
    private void UpdateQuickInput()
    {
        var editor = _editor;
        var show = editor is not null && QuickInputToggle.IsChecked == true && _pointerOnCanvas is not null
                   && (editor.IsCommandActive || !string.IsNullOrEmpty(CommandBox.Text));
        QuickInputBox.IsVisible = show;
        if (!show || editor is null || _pointerOnCanvas is not { } at)
        {
            return;
        }

        var typed = CommandBox.Text ?? string.Empty;
        QuickPrompt.Text = StripKeywords(editor.Prompt);
        if (editor.WantsPointFromBase && editor.BasePoint is { } origin)
        {
            var delta = editor.Cursor - origin;
            var degrees = ((InputParser.RadiansToDegrees(delta.Angle) % 360) + 360) % 360;
            var distanceText = editor.LockedDistance is { } d ? Format(d) : Format(delta.Length);
            var angleText = editor.LockedAngle is { } a ? Format(InputParser.RadiansToDegrees(a)) : Format(degrees);

            // Il campo attivo è il primo non bloccato: lì compare ciò che si scrive.
            var editingDistance = editor.LockedDistance is null;
            SetField(QuickFirstBox, QuickFirst, editingDistance && typed.Length > 0 ? typed : distanceText, editingDistance, editor.LockedDistance is not null);
            SetField(QuickSecondBox, QuickSecond, (!editingDistance && typed.Length > 0 ? typed : angleText) + "°", !editingDistance, editor.LockedAngle is not null);
            QuickSecondBox.IsVisible = true;
        }
        else if (editor.WantsPoint && !editor.WantsPointFromBase)
        {
            var c = editor.Cursor;
            SetField(QuickFirstBox, QuickFirst, typed.Length > 0 ? typed : $"{Format(c.X)}, {Format(c.Y)}", true, false);
            QuickSecondBox.IsVisible = false;
        }
        else
        {
            SetField(QuickFirstBox, QuickFirst, typed, true, false);
            QuickSecondBox.IsVisible = false;
            QuickFirstBox.IsVisible = typed.Length > 0 || editor.IsCommandActive;
        }

        // Vicino ai bordi il riquadro passa dall'altra parte del cursore, per restare visibile.
        var size = QuickInputBox.Bounds.Size;
        var left = at.X + 22 + size.Width > Canvas.Bounds.Width ? at.X - 22 - size.Width : at.X + 22;
        var top = at.Y + 22 + size.Height > Canvas.Bounds.Height ? at.Y - 22 - size.Height : at.Y + 22;
        Avalonia.Controls.Canvas.SetLeft(QuickInputBox, Math.Max(0, left));
        Avalonia.Controls.Canvas.SetTop(QuickInputBox, Math.Max(0, top));
    }

    private static readonly IBrush ActiveFieldBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));
    private void SetField(Border box, TextBlock text, string value, bool active, bool locked)
    {
        box.IsVisible = true;
        text.Text = value;
        box.BorderBrush = active ? ActiveFieldBrush : Brush("Cad.FieldBorder");
        box.Background = Brush(locked ? "Cad.FieldLocked" : "Cad.Field");
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>La richiesta senza l'elenco delle opzioni tra parentesi quadre, che diventano pulsanti.</summary>
    private static string StripKeywords(string prompt)
    {
        var open = prompt.IndexOf(" [", StringComparison.Ordinal);
        var close = prompt.IndexOf(']', Math.Max(open, 0));
        return open >= 0 && close > open ? prompt.Remove(open, close - open + 1) : prompt;
    }

    // ---------- Opzioni cliccabili ----------

    private IReadOnlyList<string> _shownKeywords = [];

    private void UpdateKeywordButtons()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        PromptText.Text = editor.Keywords.Count > 0 ? StripKeywords(editor.Prompt) : editor.Prompt;
        if (editor.Keywords.SequenceEqual(_shownKeywords))
        {
            return;
        }

        _shownKeywords = editor.Keywords;
        KeywordPanel.Children.Clear();
        foreach (var keyword in editor.Keywords)
        {
            var button = new Button
            {
                Content = keyword,
                FontSize = 11.5,
                Padding = new Thickness(6, 0),
                MinHeight = 20,
                Focusable = false,
                Foreground = ActiveFieldBrush,
            }.Themed(Button.BackgroundProperty, "Cad.Button");
            ToolTip.SetTip(button, $"Opzione {keyword} (oppure scrivi {keyword[..1]})");
            button.Click += (_, _) =>
            {
                CommandBox.Text = "";
                editor.SubmitText(keyword);
                CommandBox.Focus();
            };
            KeywordPanel.Children.Add(button);
        }
    }

    // ---------- Completamento e storico ----------

    /// <summary>A riga vuota di comando, mentre si scrive: i comandi che iniziano con il testo.</summary>
    private void UpdateCompletion()
    {
        var text = (CommandBox.Text ?? string.Empty).Trim();
        if (_editor is not { } editor || editor.IsCommandActive || text.Length == 0 || text.Contains(' '))
        {
            CompletionPopup.IsOpen = false;
            return;
        }

        var matches = editor.CommandNames
            .Concat(UiOnlyCommands)
            .Where(n => n.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n.Length)
            .ThenBy(n => n, StringComparer.Ordinal)
            .Take(12)
            .ToList();
        if (matches.Count == 0 || (matches.Count == 1 && string.Equals(matches[0], text, StringComparison.OrdinalIgnoreCase)))
        {
            CompletionPopup.IsOpen = false;
            return;
        }

        CompletionList.ItemsSource = matches;
        CompletionList.SelectedIndex = 0;
        CompletionPopup.IsOpen = true;
    }

    private static readonly string[] UiOnlyCommands = ["ZOOMESTENSIONI", "PROPRIETA", "PALETTELAYER", "GESTORELAYER", "OPZIONI", "SELEZIONATUTTO", "TEMA", "DLGTRATTEGGIO", "DLGSTILEQUOTA"];

    private void InitializeCompletion()
    {
        CompletionList.Focusable = false;
        CompletionList.SelectionChanged += (_, _) => { };
        CompletionList.Tapped += (_, _) =>
        {
            if (CompletionList.SelectedItem is string command)
            {
                CompletionPopup.IsOpen = false;
                CommandBox.Text = "";
                RunUiCommand(command);
            }
        };
    }

    /// <summary>Tasti della riga di comando oltre Invio: Tab, frecce su e giù.</summary>
    private bool HandleCommandBoxNavigation(KeyEventArgs e)
    {
        if (_editor is not { } editor)
        {
            return false;
        }

        var text = CommandBox.Text ?? string.Empty;
        switch (e.Key)
        {
            case Key.Tab when CompletionPopup.IsOpen && CompletionList.SelectedItem is string completion:
                CommandBox.Text = completion;
                CommandBox.CaretIndex = completion.Length;
                CompletionPopup.IsOpen = false;
                return true;

            case Key.Tab when editor.LockInput(text):
                CommandBox.Text = "";
                UpdateQuickInput();
                return true;

            case Key.Tab:
                return true;

            case Key.Up or Key.Down when CompletionPopup.IsOpen:
            {
                var count = CompletionList.ItemCount;
                var index = CompletionList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                CompletionList.SelectedIndex = (index + count) % count;
                CompletionList.ScrollIntoView(CompletionList.SelectedIndex);
                return true;
            }

            case Key.Up or Key.Down when _commandHistory.Count > 0:
            {
                // Storico dei valori e dei comandi scritti, dal più recente.
                if (_historyIndex < 0)
                {
                    _historyIndex = _commandHistory.Count;
                }

                _historyIndex = Math.Clamp(_historyIndex + (e.Key == Key.Up ? -1 : 1), 0, _commandHistory.Count);
                var value = _historyIndex < _commandHistory.Count ? _commandHistory[_historyIndex] : string.Empty;
                CommandBox.Text = value;
                CommandBox.CaretIndex = value.Length;
                CompletionPopup.IsOpen = false;
                return true;
            }

            case Key.Escape when CompletionPopup.IsOpen:
                CompletionPopup.IsOpen = false;
                return true;
        }

        return false;
    }

    /// <summary>Invio con il completamento aperto esegue il comando evidenziato.</summary>
    private string AcceptCompletion(string text)
    {
        if (CompletionPopup.IsOpen && CompletionList.SelectedItem is string completion)
        {
            CompletionPopup.IsOpen = false;
            return completion;
        }

        return text;
    }

    private void RememberInput(string text)
    {
        _historyIndex = -1;
        text = text.Trim();
        if (text.Length == 0 || (_commandHistory.Count > 0 && _commandHistory[^1] == text))
        {
            return;
        }

        _commandHistory.Add(text);
        if (_commandHistory.Count > MaxCommandHistory)
        {
            _commandHistory.RemoveAt(0);
        }
    }

    /// <summary>Copia, taglia, incolla e seleziona tutto: al disegno, salvo quando si sta scrivendo in una casella.</summary>
    private string? ClipboardShortcut(KeyEventArgs e, bool ctrl, bool shift)
    {
        if (!ctrl)
        {
            return null;
        }

        var typing = e.Source is TextBox box && (!ReferenceEquals(box, CommandBox) || !string.IsNullOrEmpty(box.Text));
        if (typing)
        {
            return null;
        }

        return (e.Key, shift) switch
        {
            (Key.C, false) => "COPIACLIP",
            (Key.C, true) => "COPIABASE",
            (Key.X, false) => "TAGLIACLIP",
            (Key.V, false) => "INCOLLACLIP",
            (Key.A, false) => "SELEZIONATUTTO",
            _ => null,
        };
    }
}
