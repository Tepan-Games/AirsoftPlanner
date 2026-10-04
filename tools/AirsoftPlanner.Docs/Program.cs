// Documentation d'Airsoft Planner : captures des écrans du logiciel (rendu sans fenêtre visible) sur une OP
// de démonstration préparée ici, captures de l'application Android (tests d'interaction), puis mise en page PDF.
using System.Reflection;
using AirsoftPlanner.App;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.App.Views;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Symbols;
using AirsoftPlanner.Data;
using AirsoftPlanner.Docs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

if (args.Length < 3)
{
    Console.WriteLine("Usage : AirsoftPlanner.Docs <démo.aop> <dossier captures Android> <documentation.pdf>");
    return 1;
}

var work = Path.Combine(Path.GetTempPath(), "AirsoftPlannerDocs");
Directory.CreateDirectory(work);
var opPath = Path.Combine(work, "op-documentation.aop");
File.Copy(args[0], opPath, overwrite: true);

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
// Réglages du poste non modifiés sur le disque : uniquement en mémoire pour les captures.
AppSettings.Current.CoordinateFormat = AirsoftPlanner.Core.Geo.CoordinateFormat.Utm;
AppSettings.SuppressOpening = true;

var shots = new Dictionary<string, byte[]>();
var main = new MainViewModel(new SilentDialogs(work));
var window = new MainWindow { DataContext = main, Width = 1500, Height = 900 };
window.Show();
Light();
Shot("accueil");

typeof(MainViewModel).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [OperationFile.Open(opPath)]);
Light();
var ws = main.Workspace!;
Prepare(ws);

var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
Tab(0, "general");
Tab(1, "orgas");
Tab(2, "factions");
ws.Teams.Selected = ws.Teams.Items.FirstOrDefault(t => t.Name == "Alpha") ?? ws.Teams.Items.First();
Tab(3, "equipes");
ScrollToEnd();
Shot("equipes-android");
ws.Terrain.SelectedZone = ws.Terrain.Zones.FirstOrDefault(z => z.Name == "Bivouac OTAN");
Tab(4, "terrain");
ws.Terrain.SelectedZone = null;
Tab(5, "materiel");
Tab(6, "finances");
Tab(7, "documents");

tabs.SelectedIndex = 8;
Pump();
var scenario = window.GetVisualDescendants().OfType<TabControl>().First(t => t != tabs && t.Items.Count == 3);
ws.Missions.Selected = ws.Missions.Missions.FirstOrDefault(m => m.ItemUses.Count > 0) ?? ws.Missions.Missions.First();
scenario.SelectedIndex = 0;
Shot("organisation");
ws.Missions.OpenDelayCommand.Execute(null);
Pump();
Shot("organisation-retard");
ws.Missions.Delay.CloseCommand.Execute(null);

scenario.SelectedIndex = 1;
Pump();
Shot("suivi");
var status = window.GetVisualDescendants().OfType<TabControl>()
    .First(t => t.Items.OfType<TabItem>().Any(i => (i.Header as string) == "Objets d'objectif"));
status.SelectedIndex = 1;
Shot("suivi-joueurs");
status.SelectedIndex = 2;
Shot("suivi-messages");
status.SelectedIndex = 3;
Shot("suivi-objets");
status.SelectedIndex = 0;

var settings = new TrackingSettingsWindow { DataContext = ws.Tracking.Gps, Width = 900, Height = 900 };
settings.Show();
Pump();
settings.CaptureRenderedFrame();
Pump();
shots["parametres"] = Png(settings.CaptureRenderedFrame()!);
settings.Close();

scenario.SelectedIndex = 2;
Pump();
ws.Retex.Refresh();
Shot("retex");

// Planche des symboles militaires (annexe).
shots["symboles"] = SymbolSheet.Render();

var android = Directory.Exists(args[1])
    ? Directory.GetFiles(args[1], "android-*.png").OrderBy(f => f).ToDictionary(f => Path.GetFileNameWithoutExtension(f), File.ReadAllBytes)
    : [];
Console.WriteLine($"{shots.Count} captures du logiciel, {android.Count} captures Android");
// L'OP préparée sert aussi de fichier d'exemple pour l'archive de distribution.
var save = main.SaveCommand.ExecuteAsync(null);
while (!save.IsCompleted)
    Pump();
Console.WriteLine($"OP d'exemple : {opPath}");
main.Dispose();

DocumentationBuilder.Write(args[2], shots, android);
Console.WriteLine($"Documentation écrite : {args[2]}");
return 0;

// ----- Préparation de l'OP de démonstration -----

void Prepare(WorkspaceViewModel w)
{
    if (w.Organizers.Items.Count == 0)
    {
        AddOrga("Marc Dubois", "Directeur de jeu", "06 10 20 30 40", w.General.OrgaRadioFrequency, "marc@example.org");
        AddOrga("Sophie Laurent", "Arbitre zone nord", "06 11 21 31 41", w.General.OrgaRadioFrequency, "sophie@example.org");
        AddOrga("Karim Benali", "Secouriste", "06 12 22 32 42", w.General.OrgaRadioFrequency, "karim@example.org");
    }

    var factions = w.Factions.Items.ToList();
    var terrain = w.Terrain;
    var area = terrain.Area;
    if (area.IsValid && !terrain.Zones.Any(z => z.Name == "Bivouac OTAN"))
    {
        double Lat(double f) => area.South + (area.North - area.South) * f;
        double Lon(double f) => area.West + (area.East - area.West) * f;
        AddPoi("Bivouac OTAN", Lat(0.72), Lon(0.38), PoiCategory.Bivouac, MilSymbol.Auto, factions.FirstOrDefault(), ZoneVisibility.Faction);
        AddPoi("Campement adverse", Lat(0.30), Lon(0.75), PoiCategory.Camp, MilSymbol.Auto, factions.Skip(1).FirstOrDefault(), ZoneVisibility.Faction);
        AddPoi("Infirmerie", Lat(0.52), Lon(0.28), PoiCategory.Medical, MilSymbol.Auto, null, ZoneVisibility.AllTeams);
        AddPoi("Poste d'observation", Lat(0.60), Lon(0.62), PoiCategory.Other, MilSymbol.ObservationPost, factions.FirstOrDefault(), ZoneVisibility.Orga);
        AddPoi("PC orga", Lat(0.47), Lon(0.55), PoiCategory.Command, MilSymbol.Auto, null, ZoneVisibility.AllTeams);
        terrain.SelectedZone = null;
    }

    // Pendant l'OP : instant simulé, missions diffusées, messages, trajet.
    var tracking = w.Tracking;
    tracking.IsSimulation = true;
    tracking.SimulatedMinutes = tracking.SimulationStart + 80;

    // Résultats des premières missions finies (score et RETEX).
    var finished = w.Missions.Missions.Where(m => m.Model.IsEnabled && m.EndMinutes <= tracking.SimulatedMinutes).OrderBy(m => m.StartMinutes).Take(3).ToList();
    if (finished.All(m => !m.IsEvaluated))
        foreach (var (mission, result, notes) in finished.Zip(
                     new[] { MissionResult.Success, MissionResult.Partial, MissionResult.Failure },
                     new[] { "Objectif tenu sans perte", "Documents récupérés, otage perdu", "Convoi intercepté avant le point de rendez-vous" }))
        {
            mission.Result = MissionResultOption.Of(result);
            mission.ResultNotes = notes;
        }
    var dispatch = tracking.Dispatch!;
    foreach (var team in w.Missions.Columns.Take(2))
    {
        var first = w.Missions.Missions.Where(m => m.Model.TeamIds.Contains(team.Model.Id) && m.Model.IsEnabled).OrderBy(m => m.StartMinutes).FirstOrDefault();
        if (first is not null && team.Model.PublishedMissionId is null)
            dispatch.Publish(team, first.Model, null);
    }

    if (dispatch.AllMessages.All(m => m.Kind != MessageKind.Text))
    {
        dispatch.SelectedTarget = dispatch.Targets.Skip(1).FirstOrDefault() ?? dispatch.Targets.First();
        dispatch.ComposeAsHq = true;
        dispatch.ComposeText = "Contact signalé au nord du village, progressez prudemment";
        if (terrain.Layers.FirstOrDefault() is { } layer)
            dispatch.ComposePhoto = PhotoResizer.ToJpeg(MapSnapshot.Render(layer.Model, [], maxSide: 700));
        dispatch.SendCommand.Execute(null);
        dispatch.ComposeAsHq = false;
        dispatch.SelectedTarget = dispatch.Targets.First();
        dispatch.ComposeText = "Fin de partie à 17 h, retour au parking";
        dispatch.SendCommand.Execute(null);
    }

    if (w.Teams.Items.FirstOrDefault(t => t.Name == "Alpha") is { } alpha && area.IsValid && alpha.EnrollmentCode.Length == 0)
        alpha.EnrollmentCode = "K7P4QZ";
    Pump();
}

void AddOrga(string name, string role, string phone, string radio, string mail)
{
    ws!.Organizers.AddCommand.Execute(null);
    var o = ws.Organizers.Selected!;
    (o.Name, o.Role, o.Phone, o.RadioFrequency, o.Email) = (name, role, phone, radio, mail);
}

void AddPoi(string name, double lat, double lon, PoiCategory category, MilSymbol symbol, FactionViewModel? owner, ZoneVisibility visibility)
{
    var terrain = ws!.Terrain;
    terrain.AddPointCommand.Execute(null);
    var z = terrain.SelectedZone!;
    z.Name = name;
    z.PositionText = FormattableString.Invariant($"{lat:0.000000}, {lon:0.000000}");
    z.Category = PoiCategoryOption.All.First(c => c.Value == category);
    z.Symbol = MilSymbolOption.All.First(o => o.Value == symbol);
    terrain.SelectedZoneOwner = terrain.OwnerOptions.First(o => o.Id == owner?.Model.Id);
    terrain.SelectedZoneVisibility = terrain.VisibilityOptions.First(o => o.Value == visibility && (visibility != ZoneVisibility.Faction || o.FactionId == owner?.Model.Id));
    terrain.IsDrawing = false;
}

// ----- Captures -----

void Light()
{
    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    Pump();
}

void Pump()
{
    for (var i = 0; i < 5; i++)
        Dispatcher.UIThread.RunJobs();
}

void Tab(int index, string name)
{
    tabs!.SelectedIndex = index;
    Shot(name);
}

void Shot(string name)
{
    Pump();
    window.CaptureRenderedFrame();
    Pump();
    shots[name] = Png(window.CaptureRenderedFrame()!);
    Console.WriteLine("  " + name);
}

void ScrollToEnd()
{
    foreach (var scroll in window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible))
        scroll.ScrollToEnd();
    Pump();
}

static byte[] Png(Avalonia.Media.Imaging.Bitmap bitmap)
{
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    return stream.ToArray();
}

/// <summary>Boîtes de dialogue sans interaction (le dossier de travail sert aux fichiers produits).</summary>
class SilentDialogs(string folder) : IFileDialogService
{
    public Task<string?> PickNewOperationFileAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> PickExistingOperationFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickImageFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickDocumentFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult<string?>(folder);
    public Task<string?> PickOpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string extension) => Task.FromResult<string?>(null);
    public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    public Task ShowImageAsync(string title, string message, byte[] png) => Task.CompletedTask;
    public Task<SaveChoice> AskSaveChangesAsync(string fileName) => Task.FromResult(SaveChoice.Discard);
    public Task ShowErrorAsync(string message) { Console.WriteLine("  [erreur] " + message); return Task.CompletedTask; }
}
