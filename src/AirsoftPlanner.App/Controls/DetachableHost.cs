using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace AirsoftPlanner.App.Controls;

/// <summary>
/// Emplacement d'un panneau (carte, frise...) qui peut être détaché dans sa propre fenêtre, pour la placer
/// sur un autre écran (mode éclaté). Fermer la fenêtre, ou « Rattacher », remet le panneau à sa place.
/// F11 dans la fenêtre détachée bascule en plein écran.
/// </summary>
public class DetachableHost : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DetachableHost, string>(nameof(Title), "");

    public static readonly StyledProperty<Control?> PanelProperty =
        AvaloniaProperty.Register<DetachableHost, Control?>(nameof(Panel));

    private readonly Grid _attached = new();
    private readonly Button _detachButton;
    private readonly Border _placeholder;
    private Window? _window;

    public DetachableHost()
    {
        _detachButton = new Button
        {
            Content = "⧉",
            FontSize = 12,
            Padding = new Thickness(6, 1),
            Margin = new Thickness(4),
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 10,
        };
        ToolTip.SetTip(_detachButton, "Afficher dans une fenêtre séparée (pour un autre écran)");
        _detachButton.Click += (_, _) => Detach();

        var reattach = new Button { Content = "Rattacher", HorizontalAlignment = HorizontalAlignment.Center };
        reattach.Click += (_, _) => _window?.Close();
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Affiché dans une fenêtre séparée", Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center },
                    reattach,
                },
            },
        };

        Content = _attached;
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Le panneau à afficher (ici ou dans la fenêtre détachée).</summary>
    public Control? Panel { get => GetValue(PanelProperty); set => SetValue(PanelProperty, value); }

    public bool IsDetached => _window is not null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PanelProperty && _window is null)
            Attach();
    }

    /// <summary>Ouvre le panneau dans sa propre fenêtre, éventuellement sur un écran donné (agrandie).</summary>
    public void Detach(Screen? screen = null)
    {
        if (_window is not null || Panel is null || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        _attached.Children.Clear();
        _attached.Children.Add(_placeholder);
        _window = new Window
        {
            Title = $"{Title} — Airsoft Planner",
            Width = Math.Max(600, Bounds.Width),
            Height = Math.Max(400, Bounds.Height),
            DataContext = DataContext,
            Content = Panel,
            Icon = owner.Icon,
        };
        _window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.F11)
                return;
            _window.WindowState = _window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        };
        _window.Closed += (_, _) =>
        {
            _window.Content = null;
            _window = null;
            Attach();
        };

        if (screen is not null)
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Position = new PixelPoint(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 40);
            _window.Opened += (_, _) => _window!.WindowState = WindowState.Maximized;
        }

        _window.Show(owner);
    }

    /// <summary>Ferme la fenêtre détachée (le panneau revient à sa place).</summary>
    public void Reattach() => _window?.Close();

    private void Attach()
    {
        _attached.Children.Clear();
        if (Panel is null)
            return;

        _attached.Children.Add(Panel);
        _attached.Children.Add(_detachButton);
    }

    /// <summary>
    /// Mode éclaté : détache les panneaux et les répartit sur les autres écrans (un par écran, la fenêtre
    /// principale restant sur le sien). Avec un seul écran, les fenêtres s'ouvrent simplement par-dessus.
    /// </summary>
    public static void Explode(Window owner, IReadOnlyList<DetachableHost> hosts)
    {
        var mainScreen = owner.Screens.ScreenFromWindow(owner);
        var others = owner.Screens.All.Where(s => !Equals(s, mainScreen)).ToList();
        for (var i = 0; i < hosts.Count; i++)
            hosts[i].Detach(others.Count == 0 ? null : others[i % others.Count]);
    }
}
