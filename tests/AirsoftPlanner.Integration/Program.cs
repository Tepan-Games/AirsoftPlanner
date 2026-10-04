// Tests d'interaction entre l'application Android (émulateur, pilotée par adb) et le logiciel
// (code réel, piloté par programme comme le ferait l'orga).
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AirsoftPlanner.App;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.App.Views;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;
using AirsoftPlanner.Data;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

const string Package = "com.tepangames.airsoftplanner";
const string Activity = Package + "/crc64f68f6e5c0b1c5f44.MainActivity";
const int Port = 5098;
var adbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe");
var results = new List<(string Name, bool Ok, string Detail)>();

// ----- PC de l'OP (code réel du logiciel) -----
var opPath = Path.Combine(AppContext.BaseDirectory, "op-integration.aop");
File.Copy(args[0], opPath, overwrite: true);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var main = new MainViewModel(new SilentDialogs());
new MainWindow { DataContext = main }.Show();
typeof(MainViewModel).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [OperationFile.Open(opPath)]);
var ws = main.Workspace!;
var tracking = ws.Tracking;
var gps = tracking.Gps!;
var alpha = ws.Teams.Items.First(t => t.Name == "Alpha");
tracking.IsSimulation = true;
tracking.SimulatedMinutes = 10 * 60 + 20;
ws.General.EmergencyPhone = "06 99 99 99 99";
gps.ShareMode = AllyShareModeOption.Of(AllyShareMode.Map);
gps.IntervalSeconds = 5;
alpha.EnrollmentCode = "K7P4QZ";
gps.ServerPort = Port;
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
Console.WriteLine($"PC de l'OP : serveur {(gps.IsServerRunning ? "actif" : "ARRÊTÉ")} sur {Port}");

var positionsField = typeof(TrackingViewModel).GetField("_positions", BindingFlags.NonPublic | BindingFlags.Instance)!;
List<TeamPosition> AlphaPositions() => ((List<TeamPosition>)positionsField.GetValue(tracking)!)
    .Where(p => p.TeamId == alpha.Model.Id && p.Source == "Appli Android").ToList();

// ----- Téléphone (émulateur) -----
Adb($"shell pm clear {Package}");
foreach (var permission in new[] { "ACCESS_FINE_LOCATION", "ACCESS_COARSE_LOCATION", "POST_NOTIFICATIONS" })
    Adb($"shell pm grant {Package} android.permission.{permission}");
Geo(43.6492, 5.9871);

// 1. Code faux saisi à la main
Adb($"shell am start -n {Activity}");
Check("Écran d'enrôlement affiché", () => Screen().Contains("S'enrôler"), 15);
TypeInto(0, $"10.0.2.2:{Port}");
TypeInto(1, "ZZZ-ZZZ");
Tap("S'enrôler");
Check("Code faux : refusé par le PC", () => gps.Journal.Any(j => j.Contains("Enrôlement refusé")), 15);
var activeBefore = gps.Devices.Count(d => !d.IsRevoked);
Check("Code faux : le téléphone reste sur l'enrôlement, aucun téléphone ajouté", () => Screen().Contains("S'enrôler") && gps.Devices.Count(d => !d.IsRevoked) == activeBefore, 3);

// 2. Enrôlement par saisie manuelle (code en minuscules, avec tiret)
TypeInto(1, "k7p-4qz");
Tap("S'enrôler");
Check("Saisie manuelle : téléphone enrôlé côté PC", () => gps.Devices.Count(d => !d.IsRevoked) == activeBefore + 1 && gps.Devices.First(d => !d.IsRevoked).Team == "Alpha", 20);
Check("Saisie manuelle : écran de suivi de l'équipe Alpha", () => Screen().Contains("Suivi actif"), 20);

// 3. Positions reçues par le PC
Geo(43.6502, 5.9896);
Check("Positions du téléphone reçues par le PC", () => AlphaPositions().Count >= 1, 25);
Geo(43.6542, 5.9946);
Check("Nouvelle position transmise (≈ 43.6542, 5.9946)",
    () => AlphaPositions().LastOrDefault() is { } p && Math.Abs(p.Latitude - 43.6542) < 1e-4 && Math.Abs(p.Longitude - 5.9946) < 1e-4, 25);
Check("PC : téléphone vu récemment", () => gps.Devices.First(d => !d.IsRevoked).LastSeen.StartsWith("dernier envoi"), 10);

// 4. Mission diffusée sur décision de l'orga (jamais automatiquement), notifiée sur le téléphone
var dispatch = tracking.Dispatch!;
Check("Aucune mission tant que l'orga n'a rien diffusé", () => Screen().Contains("Aucune mission diffusée par l'orga"), 15);
Check("Le logiciel propose de diffuser la mission d'Alpha",
    () => dispatch.Prompts.FirstOrDefault(p => p.Team == alpha) is { } p && p.Question.Contains("Reconnaissance du village"), 5);
dispatch.AcceptCommand.Execute(dispatch.Prompts.First(p => p.Team == alpha));
Check("Mission diffusée affichée sur le téléphone", () => Screen().Contains("MISSION : Reconnaissance du village"), 25);
Check("Notification « Nouvelle mission » sur le téléphone", () => Notifications().Contains("Nouvelle mission"), 10);
Check("Le logiciel indique le message reçu par Alpha", () => dispatch.Messages.First().Delivery.Contains("reçu"), 10);

// Message à toute la faction d'Alpha
dispatch.SelectedTarget = dispatch.Targets.First(t => t.Target == MessageTarget.Faction && t.Id == alpha.Model.FactionId);
dispatch.ComposeText = "Regroupement au point Bravo à 11 h";
dispatch.SendCommand.Execute(null);
Check("Message de faction affiché sur le téléphone", () => Screen().Contains("Regroupement au point Bravo à 11 h"), 25);
Check("Message de faction notifié", () => Notifications().Contains("Regroupement au point Bravo"), 10);

// Photo jointe à un message du QG
dispatch.SelectedTarget = dispatch.Targets.First(t => t.Target == MessageTarget.Team && t.Id == alpha.Model.Id);
dispatch.ComposeText = "Photo de reconnaissance du village";
dispatch.ComposePhoto = AirsoftPlanner.App.Services.PhotoResizer.ToJpeg(
    AirsoftPlanner.App.Services.MapSnapshot.Render(ws.Terrain.Layers.First().Model, [], maxSide: 800));
dispatch.SendCommand.Execute(null);
Check("Photo jointe affichée sur le téléphone", () => { Swipe(up: true); Swipe(up: false); return ScreenNodes().Any(n => n.Desc == "Photo jointe"); }, 40);

Check("Messages du QG regroupés sous la mission en cours", () => Screen().Contains("— Mission « Reconnaissance du village » —"), 10);

// Message de l'orga (hors jeu) : section Orga
dispatch.ComposeAsHq = false;
dispatch.SelectedTarget = dispatch.Targets.First(t => t.Target == MessageTarget.AllTeams);
dispatch.ComposeText = "Fin de partie à 17 h, retour au parking";
dispatch.SendCommand.Execute(null);
Check("Message de l'orga signalé sur l'onglet ORGA", () => Screen().Contains("ORGA (1)"), 25);
Check("Message de l'orga absent de l'onglet QG", () => !Screen().Contains("Fin de partie à 17 h"), 1);
Tap("ORGA");
Check("Onglet ORGA : message et contacts de l'orga", () => Screen().Contains("Fin de partie à 17 h") && Screen().Contains("Fréquence orga : PMR 446 canal 8"), 10);
Tap("QG");
Check("Message de l'orga notifié comme tel", () => Notifications().Contains("Message de l'orga"), 10);
dispatch.ComposeAsHq = true;

// Fin de mission décidée par l'orga
dispatch.End(alpha);
Check("Mission terminée : le téléphone attend les ordres", () => Screen().Contains("Aucune mission diffusée par l'orga"), 25);
Check("Notification « Mission terminée »", () => Notifications().Contains("Mission terminée"), 10);

// Points d'intérêt : bivouac de la faction d'Alpha, visible sur son téléphone
var terrain = ws.Terrain;
terrain.AddPointCommand.Execute(null);
var bivouac = terrain.SelectedZone!;
bivouac.Name = "Bivouac nord";
bivouac.Category = PoiCategoryOption.All.First(c => c.Value == PoiCategory.Bivouac);
bivouac.PositionText = "43.6600, 5.9900";
terrain.SelectedZoneVisibility = terrain.VisibilityOptions.First(o => o.FactionId == alpha.Model.FactionId);
Check("Point d'intérêt de faction affiché sur le téléphone", () => Screen().Contains("Bivouac nord — Bivouac"), 25);
Check("Point d'intérêt notifié", () => Notifications().Contains("Points d'intérêt mis à jour"), 10);
terrain.SelectedZoneVisibility = terrain.VisibilityOptions.First(o => o.Value == ZoneVisibility.Orga);
Check("Point repassé « orga seulement » : retiré du téléphone", () => !Screen().Contains("Bivouac nord"), 25);

// 5. Intervalle modifié par l'orga
gps.IntervalSeconds = 8;
Check("Intervalle 8 s appliqué par le téléphone", () => Screen().Contains("envoi toutes les 8 s"), 30);
gps.IntervalSeconds = 5;
Check("Intervalle 5 s rétabli", () => Screen().Contains("envoi toutes les 5 s"), 30);

// 6. Partage des alliés
Check("Mode carte : carte affichée", () => { Swipe(up: false); return ScreenNodes().Any(n => n.Desc == "Carte du terrain"); }, 25);
gps.ShareMode = AllyShareModeOption.Of(AllyShareMode.Coordinates);
Check("Mode coordonnées : plus de carte, alliés en coordonnées",
    () => { Swipe(up: false); return !ScreenNodes().Any(n => n.Desc == "Carte du terrain") && Regex.IsMatch(Screen(), @"Charlie \(.*\)\s*3[12][A-Z] \d{6} \d{7}"); }, 25);
gps.ShareMode = AllyShareModeOption.Of(AllyShareMode.None);
Check("Mode rien : alliés non partagés", () => Screen().Contains("non partagées par l'orga"), 25);
gps.ShareMode = AllyShareModeOption.Of(AllyShareMode.Map);

// 7. Numéro d'urgence modifié
ws.General.EmergencyPhone = "06 11 22 33 44";
Tap("ORGA");
Check("Nouveau numéro d'urgence reçu (onglet ORGA)", () => Screen().Contains("Urgence orga : 06 11 22 33 44"), 25);
Tap("QG");
Check("Changement du numéro d'urgence notifié", () => Notifications().Contains("Nouveau numéro d'urgence de l'orga : 06 11 22 33 44"), 10);

// 8. Coupure du serveur (Wi-Fi perdu) puis retour
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
var outageStart = DateTimeOffset.Now;
Check("Serveur coupé : le téléphone signale le PC injoignable", () => Screen().Contains("injoignable"), 30);
Geo(43.6592, 5.9996);
Wait(7);
Geo(43.6642, 6.0046);
Wait(7);
Check("Serveur coupé : positions mises en attente", () => Regex.Match(Screen(), @"(\d+) position\(s\) en attente") is { Success: true } m && int.Parse(m.Groups[1].Value) >= 2, 20);
var beforeReconnect = AlphaPositions().Count;
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
Check("Retour du serveur : positions en attente rattrapées",
    () => AlphaPositions().Count(p => p.ReceivedAt >= outageStart) >= 2 && AlphaPositions().Count >= beforeReconnect + 2, 30);
Check("Retour du serveur : téléphone de nouveau à jour", () => Screen().Contains("Dernier envoi"), 20);

// 9. Arrêt de l'envoi depuis le téléphone
Tap("Arrêter l'envoi de la position");
Check("Téléphone : suivi arrêté", () => Screen().Contains("Suivi arrêté"), 15);
var stoppedCount = AlphaPositions().Count;
Geo(43.6692, 6.0096);
Wait(12);
Check("PC : plus aucune position reçue après l'arrêt", () => AlphaPositions().Count == stoppedCount, 1);
Tap("Démarrer l'envoi de la position");
Check("Téléphone : suivi relancé, positions de nouveau reçues", () => AlphaPositions().Count > stoppedCount, 30);

// 10. Révocation par l'orga
gps.SelectedDevice = gps.Devices.First(d => !d.IsRevoked);
gps.RevokeDeviceCommand.Execute(null);
Check("Révocation : le téléphone revient à l'écran d'enrôlement", () => Screen().Contains("S'enrôler"), 30);
Check("Révocation : message « plus autorisé » affiché", () => Screen().Contains("n'est plus autorisé par l'orga"), 3);
Check("Révocation : PC indique le téléphone révoqué", () => gps.Devices.Any(d => d.IsRevoked), 1);

// 11. Nouvel enrôlement par le lien du QR code
var link = EnrollmentLink.Create($"http://10.0.2.2:{Port}", "K7P4QZ");
Adb($"shell am start -a android.intent.action.VIEW -d '{link}' {Package}");
Check("Lien du QR code : nouvel enrôlement accepté", () => gps.Devices.Count(d => !d.IsRevoked) == activeBefore + 1 && Screen().Contains("Suivi actif"), 30);

// 12. Le PC change d'adresse (ici de port) : le téléphone le retrouve sur le Wi-Fi
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
gps.ServerPort = Port - 1;
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
var moved = DateTimeOffset.Now;
Check("Changement d'adresse du PC : le téléphone le retrouve et reprend l'envoi",
    () => AlphaPositions().Any(p => p.ReceivedAt >= moved) && gps.Devices.First(d => !d.IsRevoked).LastSeen.StartsWith("dernier envoi"), 45);

// 13. Enrôlement avec une adresse périmée (package imprimé avant le changement) : recherche sur le Wi-Fi
gps.SelectedDevice = gps.Devices.First(d => !d.IsRevoked);
gps.RevokeDeviceCommand.Execute(null);
Check("Révocation avant le test d'adresse périmée", () => Screen().Contains("S'enrôler"), 30);
var staleLink = EnrollmentLink.Create($"http://10.0.2.2:{Port}", "K7P4QZ");
Adb($"shell am start -a android.intent.action.VIEW -d '{staleLink}' {Package}");
Check("Adresse périmée : PC retrouvé sur le Wi-Fi et enrôlement accepté",
    () => gps.Devices.Count(d => !d.IsRevoked) == activeBefore + 1 && Screen().Contains("Suivi actif"), 40);
gps.ServerPort = Port;

// 14. Mode nuit de l'application
Tap("Mode nuit");
Check("Mode nuit activé (bouton « Mode jour » affiché)", () => Screen().Contains("Mode jour") && Screen().Contains("Suivi actif"), 15);
Tap("Mode jour");
Check("Retour au mode jour", () => Screen().Contains("Mode nuit"), 15);

// ----- Bilan -----
Console.WriteLine();
Console.WriteLine("=== RÉSULTATS ===");
foreach (var (name, ok, detail) in results)
    Console.WriteLine($"{(ok ? "OK   " : "ÉCHEC")} {name}{(ok ? "" : $" — {detail}")}");
Console.WriteLine($"{results.Count(r => r.Ok)}/{results.Count} vérifications réussies");
Pump(gps.ToggleServerCommand.ExecuteAsync(null));
main.Dispose();
return results.All(r => r.Ok) ? 0 : 1;

// ----- Outils -----
void Pump(Task t) { while (!t.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } t.GetAwaiter().GetResult(); }

void Wait(double seconds)
{
    var end = DateTime.Now.AddSeconds(seconds);
    while (DateTime.Now < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
}

void Check(string name, Func<bool> condition, double timeoutSeconds)
{
    var end = DateTime.Now.AddSeconds(timeoutSeconds);
    while (true)
    {
        Dispatcher.UIThread.RunJobs();
        bool ok;
        try { ok = condition(); } catch (Exception ex) { ok = false; Console.WriteLine($"  ({ex.Message})"); }
        if (ok) { results.Add((name, true, "")); Console.WriteLine($"OK    {name}"); return; }
        if (DateTime.Now > end)
        {
            var detail = $"écran : {Screen().Replace('\n', ' ')[..Math.Min(260, Screen().Length)]}";
            results.Add((name, false, detail));
            Console.WriteLine($"ÉCHEC {name}\n      {detail}");
            return;
        }
        Wait(1);
    }
}

string Adb(string arguments)
{
    var info = new ProcessStartInfo(adbPath, arguments)
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
    };
    using var process = Process.Start(info)!;
    var task = process.StandardOutput.ReadToEndAsync();
    while (!process.HasExited) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    return task.Result;
}

void Geo(double lat, double lon) => Adb(FormattableString.Invariant($"emu geo fix {lon} {lat}"));

List<(string Text, string Class, string Desc, int X, int Y)> ScreenNodes()
{
    Adb("shell uiautomator dump /sdcard/ui.xml");
    var xml = Adb("exec-out cat /sdcard/ui.xml");
    var start = xml.IndexOf("<?xml", StringComparison.Ordinal);
    if (start < 0) return [];
    var document = XDocument.Parse(xml[start..]);
    return document.Descendants("node")
        .Where(n => (string?)n.Attribute("package") == Package)
        .Select(n =>
        {
            var b = Regex.Matches((string?)n.Attribute("bounds") ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToArray();
            return ((string?)n.Attribute("text") ?? "", (string?)n.Attribute("class") ?? "", (string?)n.Attribute("content-desc") ?? "",
                b.Length == 4 ? (b[0] + b[2]) / 2 : 0, b.Length == 4 ? (b[1] + b[3]) / 2 : 0);
        })
        .ToList();
}

string Notifications() => Adb("shell dumpsys notification --noredact");

// Le tableau de bord dépasse la hauteur de l'écran : on lit le haut puis le bas (uiautomator ne voit que l'écran).
string Screen()
{
    Swipe(up: true);
    var top = ScreenNodes().Select(n => n.Text).Where(t => t.Length > 0).ToList();
    Swipe(up: false);
    var bottom = ScreenNodes().Select(n => n.Text).Where(t => t.Length > 0).ToList();
    return string.Join("\n", top.Concat(bottom.Where(t => !top.Contains(t))));
}

void Swipe(bool up) => Adb(up ? "shell input swipe 540 700 540 2000 120" : "shell input swipe 540 2000 540 700 120");

void Tap(string text)
{
    Swipe(up: true);
    var node = ScreenNodes().FirstOrDefault(n => n.Text.Contains(text) || n.Desc.Contains(text));
    if (node.Text is null)
    {
        Swipe(up: false);
        node = ScreenNodes().First(n => n.Text.Contains(text) || n.Desc.Contains(text));
    }
    Adb($"shell input tap {node.X} {node.Y}");
    Wait(1);
}

void TypeInto(int fieldIndex, string text)
{
    var field = ScreenNodes().Where(n => n.Class == "android.widget.EditText").ElementAt(fieldIndex);
    Adb($"shell input tap {field.X} {field.Y}");
    Adb("shell input keyevent KEYCODE_MOVE_END");
    Adb("shell input keyevent " + string.Join(" ", Enumerable.Repeat("KEYCODE_DEL", 40)));
    Adb($"shell input text '{text.Replace(" ", "%s")}'");
    Wait(0.5);
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
    public Task ShowErrorAsync(string message) { Console.WriteLine("  [PC] erreur : " + message); return Task.CompletedTask; }
}
