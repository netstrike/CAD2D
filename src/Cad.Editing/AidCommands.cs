namespace Cad.Editing;

/// <summary>Aiuti al disegno: GRIGLIA (passo e accensione) e POLARE (incremento del tracciamento polare).</summary>
internal static class AidCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("GRIGLIA", Grid, "GRID");
        editor.RegisterCommand("POLARE", Polar, "POLAR");
    }

    private static async Task Grid(Editor ed)
    {
        var answer = await ed.GetNumberAsync($"Passo della griglia <{Editor.Format(ed.GridSpacing)}>:", "Attiva", "Disattiva", "Aggancio");
        switch (answer.Status)
        {
            case PromptStatus.Ok when answer.Value > 0:
                ed.GridSpacing = answer.Value;
                ed.GridVisible = true;
                ed.Write($"Griglia con passo {Editor.Format(answer.Value)}.");
                break;
            case PromptStatus.Ok:
                ed.Write("Il passo deve essere maggiore di zero.");
                break;
            case PromptStatus.Keyword when answer.Keyword == "Attiva":
                ed.GridVisible = true;
                break;
            case PromptStatus.Keyword when answer.Keyword == "Disattiva":
                ed.GridVisible = false;
                break;
            case PromptStatus.Keyword:
                ed.GridSnapEnabled = !ed.GridSnapEnabled;
                ed.Write(ed.GridSnapEnabled ? "<Aggancio alla griglia attivo>" : "<Aggancio alla griglia disattivato>");
                break;
        }
    }

    private static async Task Polar(Editor ed)
    {
        var answer = await ed.GetNumberAsync($"Incremento angolare del tracciamento polare <{Editor.Format(ed.PolarIncrementDegrees)}>:", "Attiva", "Disattiva");
        switch (answer.Status)
        {
            case PromptStatus.Ok when answer.Value is > 0 and <= 180:
                ed.PolarIncrementDegrees = answer.Value;
                ed.PolarEnabled = true;
                ed.Write($"Tracciamento polare ogni {Editor.Format(answer.Value)}°.");
                break;
            case PromptStatus.Ok:
                ed.Write("L'incremento deve essere tra 0 e 180 gradi.");
                break;
            case PromptStatus.Keyword:
                ed.PolarEnabled = answer.Keyword == "Attiva";
                break;
        }
    }
}
