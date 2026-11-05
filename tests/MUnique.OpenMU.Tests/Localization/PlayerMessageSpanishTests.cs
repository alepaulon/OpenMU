// <copyright file="PlayerMessageSpanishTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.Localization;

using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using MUnique.OpenMU.GameLogic.Properties;
using NUnit.Framework;

/// <summary>
/// Tests for the Spanish texts of <see cref="PlayerMessage"/>.
/// </summary>
public class PlayerMessageSpanishTests
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    /// <summary>
    /// Tests that every text has its own translation, so that a player never gets the fallback text.
    /// </summary>
    [Test]
    public void EveryKeyIsTranslated()
    {
        var spanishKeys = GetEntries(Spanish).Select(entry => entry.Key).ToHashSet();

        Assert.That(GetEntries(CultureInfo.InvariantCulture).Select(entry => entry.Key).Where(key => !spanishKeys.Contains(key)), Is.Empty);
    }

    /// <summary>
    /// Tests that a translation has the same format placeholders as the neutral text,
    /// otherwise <see cref="string.Format(string, object?[])"/> fails or loses a value when showing it.
    /// </summary>
    [Test]
    public void PlaceholdersAreTheSameAsInTheNeutralTexts()
    {
        var neutral = GetEntries(CultureInfo.InvariantCulture).ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (var (key, text) in GetEntries(Spanish))
        {
            Assert.That(GetPlaceholders(text), Is.EqualTo(GetPlaceholders(neutral[key])), key);
        }
    }

    /// <summary>
    /// Tests that a culture of a region falls back to the Spanish texts.
    /// </summary>
    [Test]
    public void RegionalCultureGetsTheSpanishTexts()
    {
        Assert.That(PlayerMessage.ResourceManager.GetString(nameof(PlayerMessage.InventoryFull), CultureInfo.GetCultureInfo("es-AR")), Is.EqualTo("El inventario está lleno"));
    }

    private static IEnumerable<(string Key, string Value)> GetEntries(CultureInfo culture)
    {
        // Loads the resource set of the culture, without taking the entries of its parent.
        _ = PlayerMessage.ResourceManager.GetString(nameof(PlayerMessage.InventoryFull), culture);
        var resourceSet = PlayerMessage.ResourceManager.GetResourceSet(culture, true, false)
                          ?? throw new MissingManifestResourceException(culture.Name);
        foreach (DictionaryEntry entry in resourceSet)
        {
            yield return ((string)entry.Key, (string)entry.Value!);
        }
    }

    private static List<string> GetPlaceholders(string text)
    {
        return Regex.Matches(text, @"\{\d+[^}]*\}").Select(match => match.Value).Distinct().Order(StringComparer.Ordinal).ToList();
    }
}
