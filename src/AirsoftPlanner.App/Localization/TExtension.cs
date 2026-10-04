using System;
using AirsoftPlanner.Core.Localization;
using Avalonia.Markup.Xaml;

namespace AirsoftPlanner.App.Localization;

/// <summary>Texte traduit dans les écrans : <c>Text="{l:T cle}"</c> (langue choisie au démarrage).</summary>
public class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
