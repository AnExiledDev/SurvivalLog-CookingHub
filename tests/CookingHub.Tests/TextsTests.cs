using System.Reflection;
using System.Text.RegularExpressions;
using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>Every word on screen exists in English and Chinese, and every word page.js asks for is one the plugin sends.</summary>
public class TextsTests
{
    private static IEnumerable<Phrase> AllPhrases() =>
        typeof(Texts).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.GetValue(null)).OfType<Phrase>()
            .Concat(Texts.Page.Select(word => word.Phrase));

    private static string PageScriptText()
    {
        using var stream = typeof(TextsTests).Assembly.GetManifestResourceStream("CookingHub.page.js") ?? throw new InvalidOperationException("page.js is not embedded");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    [Fact]
    public void Every_text_has_plain_english_and_a_chinese_translation()
    {
        foreach (var phrase in AllPhrases())
        {
            var hasWords = Regex.Replace(phrase.En, @"\{\d\}", "").Any(char.IsLetter);

            Assert.False(string.IsNullOrWhiteSpace(phrase.En));
            Assert.True(phrase.En.All(ch => ch is >= ' ' and <= '~'), phrase.En);
            Assert.True(!hasWords || phrase.Zh.Any(ch => ch >= (char)0x4e00 && ch <= (char)0x9fff), phrase.Zh);
        }
    }

    [Fact]
    public void Both_languages_fill_in_the_same_blanks()
    {
        foreach (var phrase in AllPhrases())
        {
            Assert.True(phrase.En.Contains("{0}") == phrase.Zh.Contains("{0}"), phrase.En);
            Assert.True(phrase.En.Contains("{1}") == phrase.Zh.Contains("{1}"), phrase.En);
            Assert.True(phrase.En.Contains("{2}") == phrase.Zh.Contains("{2}"), phrase.En);
        }
    }

    [Fact]
    public void Page_keys_are_not_listed_twice()
    {
        Assert.Equal(Texts.Page.Count, Texts.Page.Select(w => w.Key).Distinct().Count());
    }

    /// <summary>page.js has no English of its own, so a key it asks for that the plugin never sends shows as the bare key.</summary>
    [Fact]
    public void Every_word_page_js_looks_up_exists()
    {
        var script = PageScriptText();
        var keys = Texts.Page.Select(w => w.Key).ToHashSet();
        var looked = Regex.Matches(script, @"say\('([A-Za-z0-9]+)'").Select(m => m.Groups[1].Value);
        var tabled = Regex.Matches(script, @"'((?:tab|f|t|part|b|p|s|n)[A-Z][A-Za-z0-9]*)'").Select(m => m.Groups[1].Value);
        var used = looked.Concat(tabled).ToList();

        Assert.Contains("tPerfect", used);
        Assert.Contains("fLocked", used);

        foreach (var key in used) Assert.True(keys.Contains(key), key);
    }
}
