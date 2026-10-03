using Cad.IO;

namespace Cad.IO.Tests;

public class TextCodesTests
{
    [Theory]
    [InlineData(@"Riga 1\PRiga 2", "Riga 1\nRiga 2")]
    [InlineData(@"{\C1;rosso} e {\fArial|b1;grassetto}", "rosso e grassetto")]
    [InlineData(@"\pxqc;centrato", "centrato")]
    [InlineData(@"\pi1,qj;giustificato", "giustificato")]
    [InlineData(@"\H2.5x;alto", "alto")]
    [InlineData(@"\Lsottolineato\l", "sottolineato")]
    [InlineData(@"\S1/2;", "1/2")]
    [InlineData(@"\S1^2;", "1/2")]
    [InlineData(@"a\\b \{c\}", @"a\b {c}")]
    [InlineData(@"\U+00D8 20", "Ø 20")]
    [InlineData(@"incompleto \H2", "incompleto ")]
    public void MText_codes_become_plain_text(string input, string expected) =>
        Assert.Equal(expected, TextCodes.MTextToPlain(input));

    [Theory]
    [InlineData("%%c20", "Ø20")]
    [InlineData("45%%d", "45°")]
    [InlineData("%%p0.1", "±0.1")]
    [InlineData("%%usottolineato%%u", "sottolineato")]
    [InlineData("100%%%", "100%")]
    [InlineData("%%176", "°")]
    [InlineData("senza codici", "senza codici")]
    public void Text_special_characters_are_decoded(string input, string expected) =>
        Assert.Equal(expected, TextCodes.DecodeSpecialCharacters(input));

    [Fact]
    public void Long_paragraph_formatting_terminates()
    {
        var input = string.Concat(Enumerable.Repeat(@"\P\pi1,qj;Lorem {\C1ipsum} dolor", 200));
        var plain = TextCodes.MTextToPlain(input);
        Assert.Equal(200, plain.Split('\n').Length - 1);
    }
}
