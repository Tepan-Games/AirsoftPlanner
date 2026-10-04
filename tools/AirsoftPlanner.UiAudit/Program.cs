// Contrôle de mise en page dans une langue : chaque écran est affiché sans fenêtre visible, puis chaque bouton, case,
// liste ou champ est comparé à ses conteneurs. Un élément qui en dépasse (coupé ou masqué par le panneau voisin)
// ou dont le texte ne tient pas est signalé. Code de sortie : nombre de problèmes (0 = tout tient).
using System.Reflection;
using AirsoftPlanner.App;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.App.Views;
using AirsoftPlanner.Core.Localization;
using AirsoftPlanner.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

if (args.Length < 4)
{
    Console.WriteLine("Usage : AirsoftPlanner.UiAudit <op.aop> <langue> <largeur> <hauteur> [dossier des captures]");
    return -1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var language = args[1];
var (width, height) = (int.Parse(args[2]), int.Parse(args[3]));
var shotsFolder = args.Length > 4 ? args[4] : null;
if (shotsFolder is not null)
    Directory.CreateDirectory(shotsFolder);
L.SetLanguage(language);

var work = Path.Combine(Path.GetTempPath(), "AirsoftPlannerUiAudit", language);
Directory.CreateDirectory(work);
var opPath = Path.Combine(work, "audit.aop");
File.Copy(args[0], opPath, overwrite: true);

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
AppSettings.SuppressOpening = true;
Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

var main = new MainViewModel(new SilentDialogs());
var window = new MainWindow { DataContext = main, Width = width, Height = height };
window.Show();
typeof(MainViewModel).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [OperationFile.Open(opPath)]);
var ws = main.Workspace!;
ws.Teams.Selected ??= ws.Teams.Items.FirstOrDefault();
ws.Organizers.Selected ??= ws.Organizers.Items.FirstOrDefault();
ws.Factions.Selected ??= ws.Factions.Items.FirstOrDefault();
ws.Terrain.SelectedZone ??= ws.Terrain.Zones.FirstOrDefault();
ws.Missions.Selected ??= ws.Missions.Missions.FirstOrDefault(m => m.ItemUses.Count > 0) ?? ws.Missions.Missions.FirstOrDefault();
Pump();

var problems = new List<string>();
var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
string[] names = ["general", "orgas", "factions", "equipes", "terrain", "materiel", "finances", "documents"];
for (var i = 0; i < names.Length; i++)
{
    tabs.SelectedIndex = i;
    Audit(window, names[i]);
}

// Terrain : panneau d'une zone (sommets) en plus de celui d'un point.
if (ws.Terrain.Zones.FirstOrDefault(z => z.IsArea) is { } area)
{
    tabs.SelectedIndex = 4;
    ws.Terrain.SelectedZone = area;
    Audit(window, "terrain-zone");
}

tabs.SelectedIndex = 8;
Pump();
var scenario = window.GetVisualDescendants().OfType<TabControl>().First(t => t != tabs && t.Items.Count == 3);
scenario.SelectedIndex = 0;
Audit(window, "organisation");
scenario.SelectedIndex = 1;
ws.Missions.Selected ??= ws.Missions.Missions.FirstOrDefault();
Pump();
var status = window.GetVisualDescendants().OfType<TabControl>().Where(t => t != tabs && t != scenario).OrderByDescending(t => t.Items.Count).First();
for (var i = 0; i < status.Items.Count; i++)
{
    status.SelectedIndex = i;
    Audit(window, $"suivi-{i}");
}
scenario.SelectedIndex = 2;
ws.Retex.Refresh();
Audit(window, "retex");

var settings = new TrackingSettingsWindow { DataContext = ws.Tracking.Gps, Width = Math.Min(900, width), Height = height };
settings.Show();
Audit(settings, "parametres-suivi");
settings.Close();

foreach (var problem in problems)
    Console.WriteLine(problem);
Console.WriteLine($"{language} {width}x{height} : {problems.Count} problème(s)");
main.Dispose();
return problems.Count;

// ----- Contrôle -----

void Audit(TopLevel top, string screen)
{
    Pump();
    if (shotsFolder is not null)
    {
        top.CaptureRenderedFrame();
        Pump();
        top.CaptureRenderedFrame()!.Save(Path.Combine(shotsFolder, $"{language}-{screen}.png"));
    }

    foreach (var control in top.GetVisualDescendants().OfType<Control>())
    {
        // Éléments internes des listes, tableaux, onglets et champs numériques : gérés par leur contrôle.
        if (control is not (Button or ToggleButton or CheckBox or RadioButton or ComboBox or TextBox or NumericUpDown or Slider)
            || !control.IsEffectivelyVisible || control.Bounds.Width < 1 || control.Bounds.Height < 1
            || control.GetVisualAncestors().Any(a => a is DataGrid or TabItem or NumericUpDown or ComboBox or ScrollBar or CalendarDatePicker or TimePicker))
            continue;
        if (control.TranslatePoint(default, top) is not { } origin)
            continue;
        var rect = new Rect(origin, control.Bounds.Size);
        var issue = Clipping(control, rect, top);
        // Taille voulue (marges comprises) plus grande que la place obtenue : le texte est tronqué.
        var wanted = control.DesiredSize.Width - control.Margin.Left - control.Margin.Right;
        if (issue is null && control is ContentControl { Content: string } && wanted > control.Bounds.Width + 1)
            issue = $"texte coupé ({wanted:0} px pour {control.Bounds.Width:0})";
        if (issue is not null)
            problems.Add($"{language} {width}x{height} [{screen}] {Describe(control)} : {issue}");
    }
}

// Premier conteneur dont l'élément dépasse (sauf dans le sens où une zone de défilement le rend accessible).
string? Clipping(Control control, Rect rect, TopLevel top)
{
    var (scrollsVertically, scrollsHorizontally) = (false, false);
    foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
    {
        if (ancestor is ScrollContentPresenter presenter)
        {
            var viewer = presenter.FindAncestorOfType<ScrollViewer>();
            scrollsVertically |= viewer?.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            scrollsHorizontally |= viewer?.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
            continue;
        }
        if (ancestor.TranslatePoint(default, top) is not { } origin || ancestor.Bounds.Width < 1)
            continue;
        var box = new Rect(origin, ancestor.Bounds.Size);
        var right = scrollsHorizontally ? 0 : rect.Right - box.Right;
        var left = scrollsHorizontally ? 0 : box.Left - rect.Left;
        var bottom = scrollsVertically ? 0 : rect.Bottom - box.Bottom;
        if (right > 1.5 || left > 1.5 || bottom > 1.5)
            return $"dépasse de {ancestor.GetType().Name}{(ancestor.Name is { Length: > 0 } n ? " " + n : "")} de {Math.Max(right, Math.Max(left, bottom)):0} px";
        if (ancestor is TopLevel)
            break;
    }
    return null;
}

static string Describe(Control control)
{
    var label = control switch
    {
        ContentControl { Content: string s } => s,
        ContentControl { Content: TextBlock t } => t.Text,
        TextBox t => t.PlaceholderText ?? t.Text,
        ComboBox c => c.PlaceholderText ?? c.SelectedItem?.ToString(),
        _ => null,
    };
    var tip = ToolTip.GetTip(control) as string;
    return $"{control.GetType().Name} « {label ?? tip ?? control.Name ?? "?"} »";
}

void Pump()
{
    for (var i = 0; i < 6; i++)
        Dispatcher.UIThread.RunJobs();
}

class SilentDialogs : IFileDialogService
{
    public Task<string?> PickNewOperationFileAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> PickExistingOperationFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickImageFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickDocumentFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult<string?>(null);
    public Task<string?> PickOpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string extension) => Task.FromResult<string?>(null);
    public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    public Task ShowImageAsync(string title, string message, byte[] png) => Task.CompletedTask;
    public Task<SaveChoice> AskSaveChangesAsync(string fileName) => Task.FromResult(SaveChoice.Discard);
    public Task ShowErrorAsync(string message) => Task.CompletedTask;
}
