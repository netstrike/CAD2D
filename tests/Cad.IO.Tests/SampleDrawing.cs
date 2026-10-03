using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.IO.Tests;

/// <summary>
/// La tavola di prova della fase 3: una flangia in A4 con viste, sezione tratteggiata, assi, quote, blocchi e cartiglio,
/// costruita solo con i comandi di CAD2D, come li scriverebbe un utente sulla riga di comando.
/// </summary>
public static class SampleDrawing
{
    public static (CadDocument Document, List<string> Messages) Build()
    {
        var document = new CadDocument();
        var ed = new Editor(document) { SnapEnabled = false };
        var messages = new List<string>();
        ed.Message += messages.Add;

        void T(params string[] inputs)
        {
            foreach (var input in inputs)
            {
                ed.SubmitText(input);
            }
        }

        void Pick(double x, double y) => ed.Click(new Vector2(x, y), 0.5);

        // Layer.
        T("LA",
            "Nuovo", "CONTORNO, ASSI, NASCOSTE, QUOTE, TRATTEGGI, TESTI, CARTIGLIO",
            "Colore", "ciano", "CONTORNO",
            "Colore", "rosso", "ASSI", "Tipolinea", "CENTER", "ASSI",
            "Colore", "giallo", "NASCOSTE", "Tipolinea", "HIDDEN", "NASCOSTE",
            "Colore", "verde", "QUOTE",
            "Colore", "8", "TRATTEGGI",
            "Colore", "bianco", "TESTI, CARTIGLIO",
            "");
        T("LTS", "0.5");

        // Squadratura A4 orizzontale e cartiglio.
        T("LA", "Corrente", "CARTIGLIO", "");
        T("REC", "0,0", "297,210");
        T("REC", "10,10", "287,200");
        T("REC", "187,10", "287,45");
        T("L", "187,25", "287,25", "");
        T("L", "237,10", "237,25", "");

        // Vista frontale: piastra con spigoli raccordati, foro centrale.
        T("LA", "Corrente", "CONTORNO", "");
        T("REC", "30,60", "150,140");
        T("RACCORDA", "Raggio", "8", "Polilinea");
        Pick(90, 60);
        T("C", "90,100", "20");

        // Foro come blocco, ripetuto con una serie 2x2.
        T("C", "45,75", "4");
        T("B", "FORO", "45,75");
        Pick(49, 75);
        T("", "");
        T("AR");
        Pick(49, 75);
        T("", "Rettangolare", "2", "2", "50", "90");

        // Vista dall'alto con le linee nascoste dei fori.
        T("REC", "30,158", "150,173");
        T("LA", "Corrente", "NASCOSTE", "");
        foreach (var x in new[] { 41, 49, 70, 110, 131, 139 })
        {
            T("L", $"{x},158", $"{x},173", "");
        }

        // Sezione A-A: due rettangoli tratteggiati ai lati del foro centrale.
        T("LA", "Corrente", "CONTORNO", "");
        T("REC", "200,60", "215,80");
        T("REC", "200,120", "215,140");
        T("LA", "Corrente", "TRATTEGGI", "");
        T("H", "Motivo", "ANSI31", "Scala", "0.5", "207,70", "207,130", "");

        // Assi.
        T("LA", "Corrente", "ASSI", "");
        T("L", "22,100", "158,100", "");
        T("L", "90,52", "90,180", "");
        T("L", "194,100", "221,100", "");
        foreach (var (x, y) in new[] { (45, 75), (135, 75), (45, 125), (135, 125) })
        {
            T("L", $"{x - 7},{y}", $"{x + 7},{y}", "");
            T("L", $"{x},{y - 7}", $"{x},{y + 7}", "");
        }

        // Quote.
        T("LA", "Corrente", "QUOTE", "");
        T("STILEQUOTA", "Testo", "3", "Frecce", "3", "Decimali", "1", "");
        T("DLI", "30,60", "150,60", "90,48");
        T("DLI", "150,60", "150,140", "165,100");
        T("DLI", "45,75", "135,75", "90,68");
        T("DLI", "45,75", "45,125", "22,100");
        T("DLI", "200,140", "215,140", "207.5,150");
        T("DLI", "200,80", "200,120", "T", "Ø<>", "190,100");
        T("DDI");
        Pick(90 + 20 * Math.Sqrt(0.5), 100 + 20 * Math.Sqrt(0.5));
        T("70,82");
        T("DRA");
        Pick(30 + 8 - 8 * Math.Sqrt(0.5), 60 + 8 - 8 * Math.Sqrt(0.5));
        T("20,50");

        // Quote in serie sulla vista dall'alto.
        T("DLI", "30,173", "49,173", "40,185");
        T("QCONTINUA", "70,173", "110,173", "131,173", "150,173", "");

        // Annotazioni: direttrice sui fori, tolleranza di perpendicolarità, distinta, nuvola di revisione.
        T("DIRETTRICE", $"{45 - 4 * Math.Sqrt(0.5)},{125 + 4 * Math.Sqrt(0.5)}", "24,146", "", "4 fori Ø8", "passanti", "");
        T("TOLLERANZA", "Perpendicolarità", "0,05", "A", "165,190");
        T("TABELLA", "Colonne", "2", "Righe", "3", "Larghezza", "40", "Altezza", "7", "100,44",
            "Pos.", "Descrizione", "1", "Flangia S235", "2", "Vite M8x20");
        T("NUVOLA", "Arco", "5", "161,184", "224,196");

        // Testi.
        T("LA", "Corrente", "TESTI", "");
        T("DT", "Centro", "207.5,46", "3.5", "0", "SEZIONE A-A", "");
        T("DT", "30,30", "3.5", "0", "4 fori Ø8 passanti", "Tolleranze generali ISO 2768-m", "");
        T("LA", "Corrente", "CARTIGLIO", "");
        T("STILETESTO", "Nuovo", "Cartiglio", "Carattere", "Times New Roman", "");
        T("DT", "192,32", "6", "0", "FLANGIA 120x80", "");
        T("DT", "192,15", "3", "0", "Scala 1:1", "");
        T("DT", "242,15", "3", "0", "Materiale S235", "");

        return (document, messages);
    }
}
