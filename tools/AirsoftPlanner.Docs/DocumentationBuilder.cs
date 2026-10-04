using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace AirsoftPlanner.Docs;

/// <summary>Mise en page de la documentation : chapitres, captures d'écran, tableaux.</summary>
public static class DocumentationBuilder
{
    private const string Accent = "#2E5E3E";
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static void Write(string path, IReadOnlyDictionary<string, byte[]> shots, IReadOnlyDictionary<string, byte[]> android)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        var images = shots.ToDictionary(s => s.Key, s => Jpeg(s.Value, 1500));
        var phone = android.ToDictionary(s => s.Key, s => Jpeg(s.Value, 720));

        Document.Create(doc =>
        {
            Cover(doc, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "icone.png"));
            doc.Page(page =>
            {
                Setup(page);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    Contents(col);
                    Introduction(col, images);
                    Installation(col);
                    Preparation(col, images);
                    DuringOp(col, images);
                    Gps(col, images);
                    Android(col, phone);
                    Documents(col);
                    Retex(col, images);
                    Annexes(col, images);
                });
            });
        }).GeneratePdf(path);
    }

    // ----- Chapitres -----

    private static void Cover(IDocumentContainer doc, string iconPath) => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(0);
        page.Content().Background(Accent).Padding(60).Column(col =>
        {
            if (File.Exists(iconPath))
                col.Item().PaddingTop(60).Width(4, Unit.Centimetre).Image(iconPath);
            col.Item().PaddingTop(File.Exists(iconPath) ? 30 : 160).Text("Airsoft Planner").FontSize(44).Bold().FontColor(Colors.White);
            col.Item().Text("Préparer, mener et analyser une opération d'airsoft").FontSize(18).FontColor(Colors.Grey.Lighten3);
            col.Item().PaddingTop(30).Text("Guide d'utilisation — logiciel Windows et application Android").FontSize(14).FontColor(Colors.White);
            col.Item().PaddingTop(200).Text(DateTime.Now.ToString("MMMM yyyy", French)).FontColor(Colors.Grey.Lighten2);
            col.Item().Text("https://github.com/Tepan-Games/AirsoftPlanner").FontColor(Colors.Grey.Lighten2);
            col.Item().Text("Tepan Games").FontColor(Colors.Grey.Lighten2);
        });
    });

    private static void Contents(ColumnDescriptor col)
    {
        H1(col, "Sommaire");
        foreach (var line in new[]
                 {
                     "1. Présentation", "2. Installation", "3. Préparer l'OP (onglets du logiciel)", "4. Pendant l'OP : le suivi",
                     "5. Collecte des positions GPS : toutes les interconnexions", "6. L'application Android",
                     "7. Documents imprimés et packages des équipes", "8. Après l'OP : le RETEX", "9. Annexes : symboles, formats, raccourcis",
                 })
            P(col, line);
        col.Item().PageBreak();
    }

    private static void Introduction(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "1. Présentation");
        P(col, "Airsoft Planner rassemble dans un seul logiciel tout ce qu'il faut pour organiser une OP : factions, équipes et inscriptions, terrain et cartes IGN, scénario et frise des missions, matériel de jeu, finances, documents à remettre aux équipes, puis le suivi en direct pendant la partie (positions GPS, retards, diffusion des missions, messages) et le retour d'expérience après.");
        Bullets(col,
            "Fonctionne hors ligne : une connexion n'est nécessaire que pour télécharger les fonds de carte (ou pour un nom DynDNS).",
            "Une OP = un fichier .aop (base SQLite) qu'on peut copier, envoyer à un autre orga, fusionner, ou partager dans un dossier OneDrive pour travailler à plusieurs en même temps.",
            "L'application Android du chef d'équipe envoie sa position au PC de l'OP et reçoit la mission diffusée, les messages du QG et de l'orga, le plan radio, les points d'intérêt et la carte.",
            "Coordonnées en UTM (avec gestion des terrains à cheval sur deux fuseaux), degrés décimaux ou degrés-minutes-secondes, au choix.",
            "Disponible en français, anglais, allemand, espagnol et italien : menu « ⋯ » › Langue dans le logiciel (il redémarre dans la langue choisie) ; l'application suit la langue du téléphone ou se règle avec le bouton 🌐.");
        Shot(col, img, "accueil", "Écran d'accueil : créer une OP ou ouvrir un fichier .aop.");
    }

    private static void Installation(ColumnDescriptor col)
    {
        H1(col, "2. Installation");
        H2(col, "Logiciel Windows");
        Bullets(col,
            "Dans l'archive, dossier « 1 - Logiciel Windows » : double-cliquer sur Installer.cmd. Le logiciel est installé pour votre compte Windows (aucun droit administrateur, .NET inclus), avec des raccourcis dans le menu Démarrer et sur le bureau, et les fichiers .aop s'ouvrent par double-clic.",
            "Sans installation : lancer directement AirsoftPlanner.exe depuis le dossier. Désinstallation : Desinstaller.ps1 dans le dossier d'installation (%LOCALAPPDATA%\\Programs\\AirsoftPlanner).",
            "Au premier démarrage du serveur de positions, Windows demande d'autoriser le logiciel dans le pare-feu : accepter pour les réseaux privés (port TCP 5055 par défaut et UDP 5056 pour la recherche automatique).",
            "Un fichier d'exemple (dossier « 4 - Exemple ») permet de découvrir le logiciel avec une OP complète.",
            "Option de lancement « --serveur-gps » : démarre le serveur de positions dès l'ouverture (utile pour le PC du terrain).");
        H2(col, "Application Android");
        Bullets(col,
            "Copier AirsoftPlanner.apk (dossier « 2 - Application Android ») sur le téléphone du chef d'équipe ou de l'orga et l'ouvrir pour l'installer (autoriser l'installation d'applications externes).",
            "Accorder la localisation « toujours » et les notifications : l'envoi de la position continue écran éteint (notification permanente).",
            "Android 8 ou plus récent ; Wi-Fi du terrain (ou réseau mobile si le PC est joignable par un nom DynDNS).");
        H2(col, "Mises à jour");
        Bullets(col,
            "Le logiciel vérifie une fois par jour si une nouvelle version est publiée sur GitHub (et à la demande : menu « ⋯ » › Rechercher une mise à jour).",
            "Un bandeau propose alors « Mettre à jour » : la nouvelle version est téléchargée, installée, et le logiciel redémarre (l'OP en cours est enregistrée d'abord). « Plus tard » ne signale plus cette version.",
            "L'application affiche un bouton de téléchargement de la nouvelle APK.",
            "Sans connexion Internet, ou tant que le dépôt du projet n'est pas public, aucune alerte n'est affichée.");
    }

    private static void Preparation(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "3. Préparer l'OP");
        H2(col, "Général");
        P(col, "Nom de l'OP, organisateur (équipe ou association), terrain, dates et heures de début et de fin (OP sur plusieurs jours possible), fréquence radio et numéro d'urgence de l'orga, vitesses estimées à pied et en véhicule (calcul des retards), format des coordonnées des documents imprimés.");
        Shot(col, img, "general", "Onglet Général.");
        H2(col, "Orgas");
        P(col, "Liste des organisateurs avec leur rôle (directeur de jeu, arbitre, secouriste…), téléphone, radio et e-mail. Chaque orga peut enrôler son téléphone : sa position apparaît alors sur la carte du suivi (point blanc « ★ nom (rôle) »).");
        Shot(col, img, "orgas", "Onglet Orgas : contacts de l'organisation et téléphone de chaque orga.");
        H2(col, "Factions");
        P(col, "Effectif minimum et maximum, brassard (couleur), tenue, fréquence radio de la faction et équipe de commandement. La couleur de la faction sert aux symboles militaires sur toutes les cartes.");
        Shot(col, img, "factions", "Onglet Factions.");
        H2(col, "Équipes et inscriptions");
        P(col, "Inscriptions (en attente, confirmée, liste d'attente, import CSV), membres complets (chef, indicatif, rôle, portable, e-mail), véhicules (mis en jeu, traceur GPS, compteur), fréquence radio, symbole sur les cartes, identifiants GPS et enrôlement de l'application Android (code, QR code, téléphones enrôlés, révocation).");
        Shot(col, img, "equipes", "Onglet Équipes.");
        Shot(col, img, "equipes-android", "Bas de l'onglet Équipes : véhicules et application Android de l'équipe.");
        H2(col, "Terrain et zones");
        P(col, "Emprise du terrain (centre et taille, ou deux coins), téléchargement des fonds IGN (photo aérienne, Plan IGN) enregistrés dans le fichier, import d'une image calée. Zones (polygones) et points : catégorie (bivouac, campement, respawn, infirmerie, ravitaillement, parking, PC orga, danger, objectif), symbole militaire, faction propriétaire (couleur), taille d'unité et visibilité (orga seulement, toutes les équipes, une faction, ou pendant une mission : un dépôt d'armes ou un point de contact n'est alors envoyé aux téléphones des équipes de la mission que tant qu'elle leur est diffusée). Le quadrillage suit le format de coordonnées choisi ; un terrain à cheval sur deux fuseaux UTM affiche un quadrillage par fuseau et la limite en jaune.");
        Shot(col, img, "terrain", "Onglet Terrain & zones : symboles militaires aux couleurs des factions, limite de fuseau UTM en jaune.");
        H2(col, "Matériel de jeu");
        P(col, "Caisses, artifices, fumigènes, accessoires, documents de renseignement… avec la quantité disponible, le caractère consommable et l'utilisation par les missions (alerte si une mission en demande plus que le stock). Les objets d'objectif sont suivis pendant l'OP (qui l'a, quand, où).");
        Shot(col, img, "materiel", "Onglet Matériel de jeu.");
        H2(col, "Finances");
        P(col, "Suivi comptable : participation par joueur, montants dus et payés par équipe, remises et cadeaux, remboursement du carburant des véhicules mis en jeu (au kilomètre), dépenses de l'orga et bilan.");
        Shot(col, img, "finances", "Onglet Finances.");
        H2(col, "Scénario — Organisation");
        P(col, "Frise verticale des missions par équipe (glisser pour déplacer, bord bas pour la durée, double-clic pour créer), zone, matériel, missions essentielles ou optionnelles, enchaînements et effectif maximum. La carte de droite reste visible (détachable sur un autre écran) : points d'intérêt, matériel des missions regroupé par zone, zone de la mission sélectionnée.");
        Shot(col, img, "organisation", "Scénario › Organisation : frise, carte et fiche de la mission.");
        P(col, "« Gérer un retard » répercute un retard sur la suite du planning (décalage en cascade) et propose les missions optionnelles à désactiver pour rattraper le temps.");
        Shot(col, img, "organisation-retard", "Gestion d'un retard.");
    }

    private static void DuringOp(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "4. Pendant l'OP : le suivi");
        P(col, "Scénario › Suivi de l'OP rassemble la frise (ligne de l'heure), la carte avec la dernière position de chaque équipe (symbole aux couleurs de la faction, contour selon l'état : à l'heure, juste, en retard compte tenu de la distance à parcourir) et l'état des équipes. Le plan radio reste affiché en permanence ; il se replie sur quelques lignes avec une vingtaine d'équipes. Les fréquences saisies en double sont signalées (bandeau orange) et peuvent être déclarées normales.");
        Shot(col, img, "suivi", "Suivi de l'OP : plan radio, bandeau « À diffuser », frise et carte.");
        H2(col, "Diffusion des missions : toujours sur décision de l'orga");
        P(col, "Rien n'est envoyé automatiquement aux téléphones. Le bandeau « À diffuser » pose la question au bon moment : mission qui approche, mission dont l'heure de fin est dépassée (« terminer et diffuser la suivante ? »), mission urgente créée pendant la partie. Réponses : Diffuser, Terminer seulement, Plus tard (la question revient 10 minutes après). Le tableau « Missions diffusées » permet aussi de diffuser n'importe quelle mission ou de terminer celle en cours.");
        H2(col, "Messages QG et Orga, photos");
        P(col, "Onglet « Messages et missions » : message à toutes les équipes, à une faction ou à une équipe, envoyé par le QG (ordre en jeu, pour le roleplay : affiché dans l'onglet QG de l'application) ou par l'orga (organisation, sécurité : onglet ORGA). Une photo peut être jointe ; elle est réduite automatiquement. L'historique indique quelles équipes ont reçu chaque message.");
        Shot(col, img, "suivi-messages", "Messages et missions : envoi, historique avec réception, diffusion des missions.");
        H2(col, "Effectif, objets d'objectif, mission urgente");
        Bullets(col,
            "Joueurs : sorties de jeu et retours, avec la raison (blessure réelle, pause, matériel, sanction…) ; l'effectif de chaque équipe est suivi tout au long de l'OP.",
            "Objets d'objectif : placé, récupéré par une équipe, transmis, déposé, rendu, perdu — avec l'heure et le lieu pour aller le récupérer sur le terrain.",
            "Mission urgente : créée à l'heure suivie pour l'équipe sélectionnée, puis placée d'un clic sur la carte ; sa diffusion est proposée aussitôt.",
            "Saisie manuelle d'une position (coordonnées dans tous les formats ou clic sur la carte) quand une équipe donne sa position à la radio.");
        Shot(col, img, "suivi-joueurs", "Effectif et joueurs hors jeu.");
        Shot(col, img, "suivi-objets", "Objets d'objectif.");
        H2(col, "Simulation et relecture");
        P(col, "Le mode Simulation fixe l'instant suivi : barre de temps, sauts de 5 minutes, lecture, avance rapide et retour (de × 1 à × 900). Pratique pour préparer l'OP, la rejouer après coup ou expliquer un incident.");
        H2(col, "Mode éclaté (plusieurs écrans)");
        P(col, "« Mode éclaté » place la carte, la frise et l'état des équipes dans des fenêtres séparées à répartir sur plusieurs écrans (F11 : plein écran). « Tout rattacher » les remet dans la fenêtre principale.");
    }

    private static void Gps(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "5. Collecte des positions GPS : toutes les interconnexions");
        P(col, "Les positions arrivent par plusieurs canaux, utilisables ensemble. Chaque position est attribuée à une équipe par son identifiant d'appareil (liste « Identifiants GPS » de l'équipe, ou nom de l'équipe), à un véhicule mis en jeu (ID GPS du véhicule) ou à un orga (téléphone enrôlé). Les réglages sont dans Scénario › Suivi de l'OP › bouton d'état du serveur (« Paramètres du suivi »).");
        Shot(col, img, "parametres", "Paramètres du suivi : serveur local, second poste, application, Meshtastic, Traccar, import, journal.");

        Table(col, ["Source", "Principe", "Ce qu'il faut"],
        [
            ["Application Airsoft Planner (Android)", "Le téléphone envoie sa position toutes les X secondes au serveur du PC et reçoit mission, messages, plan radio, points d'intérêt, alliés.", "Serveur local actif ; code d'équipe (ou QR code) ; même Wi-Fi ou nom DynDNS."],
            ["Traccar Client, OsmAnd, GPSLogger…", "Protocole OsmAnd : requête HTTP avec l'identifiant et la position.", "Adresse du serveur local ; identifiant de l'appareil ajouté aux identifiants GPS de l'équipe."],
            ["Page web /saisie", "Formulaire dans le navigateur d'un téléphone : équipe + coordonnées tapées.", "Serveur local actif ; aucune application."],
            ["Meshtastic (LoRa)", "Les nœuds publient leurs positions via une passerelle MQTT (JSON).", "Broker MQTT joignable (ex. Mosquitto sur le PC) ; identifiant du nœud (« !a1b2c3d4 ») dans l'équipe."],
            ["Serveur Traccar", "Le logiciel interroge l'API d'un serveur Traccar (positions de tous les appareils).", "URL du serveur, e-mail et mot de passe ou jeton ; identifiants Traccar dans les équipes."],
            ["Second poste", "Un autre PC Airsoft Planner récupère toutes les 10 s les positions collectées par le PC de l'OP.", "Adresse du serveur du PC de l'OP."],
            ["Fichiers GPX / CSV", "Import après coup (traces de montres GPS, export d'autres outils).", "GPX : attribué à l'équipe sélectionnée ; CSV : colonnes Équipe (ou Appareil), Latitude, Longitude, Heure."],
            ["JSON (autres systèmes)", "Envoi HTTP POST d'une liste de positions.", "POST /api/positions avec [{\"deviceId\":…, \"latitude\":…, \"longitude\":…, \"time\":…}]."],
        ]);

        H2(col, "Serveur local (PC de l'OP)");
        Bullets(col,
            "Port TCP 5055 par défaut (modifiable) ; démarrage par le bouton « Serveur actif », à l'ouverture de l'OP (« Au lancement ») ou par l'option « --serveur-gps ».",
            "Adresses affichées : une par carte réseau ; à donner aux téléphones sur le même Wi-Fi que le PC.",
            "Points d'entrée : / (protocole OsmAnd, page de vérification), /saisie (saisie web), /api/enroll et /api/track (application), /api/positions (lecture par un second poste, envoi JSON), /api/map/image et /api/message/photo (carte et photos pour l'application).",
            "Pare-feu Windows : autoriser le logiciel au premier démarrage (réseaux privés).");
        H2(col, "Application Android : enrôlement, adresse publiée, DynDNS et recherche sur le Wi-Fi");
        Bullets(col,
            "Chaque équipe a un code d'enrôlement (6 caractères faciles à dicter) et un QR code, imprimés aussi dans son ordre de mission. Les orgas ont le leur (onglet Orgas).",
            "Adresse publiée : nom DynDNS (DuckDNS, No-IP, Dynu…) ou adresse fixe, utilisée dans les QR codes et les packages. Le logiciel met à jour le nom DynDNS avec l'IP du PC (adresse de mise à jour contenant {ip}, jeton chiffré) au démarrage du serveur puis dès que l'IP change.",
            "Recherche sur le Wi-Fi (UDP 5056, sans Internet) : si l'adresse ne répond plus (autre box, IP changée), l'application retrouve seule le PC de son OP, à l'enrôlement comme pendant la partie.",
            "Intervalle d'envoi réglable pendant l'OP (appliqué au prochain échange) ; positions gardées sur le téléphone pendant une coupure du Wi-Fi et envoyées au retour.",
            "Ce que voient les téléphones : rien, les alliés en coordonnées (version difficile) ou les alliés sur la carte du terrain. Un téléphone peut être révoqué à tout moment.");
        H2(col, "Traccar Client / OsmAnd (sans l'application Airsoft Planner)");
        Bullets(col,
            "Adresse du serveur : http://<adresse du PC>:5055 ; identifiant de l'appareil : par exemple « alpha-chef ».",
            "Requête reçue : http://PC:5055/?id=alpha-chef&lat=48.85&lon=2.35&timestamp=1700000000 (format OsmAnd).",
            "Ajouter l'identifiant dans « Identifiants GPS » de l'équipe ; un appareil inconnu apparaît dans « Appareils inconnus » et s'associe d'un clic à l'équipe sélectionnée.");
        H2(col, "Meshtastic");
        Bullets(col,
            "Activer sur la passerelle Meshtastic la publication MQTT au format JSON vers un broker (ex. Mosquitto installé sur le PC de l'OP).",
            "Dans les paramètres du suivi : broker, port (1883), sujet (msh/# par défaut), identifiants éventuels, puis « Connecté ».",
            "Seuls les messages de position sont retenus ; l'identifiant du nœud (« !a1b2c3d4 ») est à ajouter aux identifiants GPS de l'équipe ou du véhicule.");
        H2(col, "Véhicules mis en jeu");
        P(col, "Un véhicule « en jeu » avec un ID GPS (traceur, téléphone, nœud Meshtastic) apparaît sur la carte avec le symbole véhicule de sa faction ; ses positions alimentent son kilométrage sur l'OP (remboursement du carburant).");
    }

    private static void Android(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> phone)
    {
        H1(col, "6. L'application Android");
        P(col, "L'application est pensée pour le chef d'équipe : une icône ▶ / ⏸ en haut à gauche démarre ou arrête l'envoi de la position, le bouton de droite passe en mode nuit. Deux onglets séparent le jeu (QG) de l'organisation (ORGA), pour préserver le roleplay.");
        PhoneRow(col, phone, [("android-1-enrolement", "Enrôlement : QR code ou adresse + code d'équipe."), ("android-2-mission", "Onglet QG : mission diffusée par l'orga, messages du QG groupés par mission.")]);
        PhoneRow(col, phone, [("android-3-photo", "Message du QG avec photo."), ("android-5-carte", "Carte : points d'intérêt et alliés en symboles militaires, position de l'équipe.")]);
        PhoneRow(col, phone, [("android-4-orga", "Onglet ORGA : fréquence et numéro d'urgence de l'orga, messages de l'orga."), ("android-6-notifications", "Notifications : nouvelle mission, messages, plan radio, points d'intérêt.")]);
        PhoneRow(col, phone, [("android-7-nuit", "Mode nuit : rouge sur noir, carte assombrie, luminosité minimale."), ("android-8-telephone-orga", "Téléphone d'un orga : toutes les équipes et tous les points.")]);
        Bullets(col,
            "Historique complet des messages conservé sur le téléphone (consultable hors réseau), regroupé par mission ; les messages non lus sont signalés sur chaque onglet.",
            "Plan radio de la faction (équipes alliées, équipe de commandement ★) dans l'onglet QG ; fréquence et numéro d'urgence de l'orga (bouton d'appel) dans l'onglet ORGA.",
            "Points d'intérêt communiqués par l'orga (bivouac de la faction, infirmerie…) sur la carte et en liste avec leurs coordonnées.",
            "Révocation par l'orga : le téléphone revient à l'écran d'enrôlement avec un message.");
    }

    private static void Documents(ColumnDescriptor col)
    {
        H1(col, "7. Documents imprimés et packages des équipes");
        Bullets(col,
            "Ordre de mission de chaque équipe (PDF) : identification, transmissions, effectif, missions avec zones, coordonnées et matériel, points d'intérêt de l'équipe, QR code et code d'enrôlement de l'application, carte avec les zones de ses missions et les points qui lui sont visibles.",
            "Règles du jeu : rédigées dans le logiciel (mise en page PDF) ou importées (PDF, Word…).",
            "Package par équipe : dossier et archive ZIP (ordre de mission + règles), avec le suivi de l'envoi et de la réception (« reçu par… »), et l'alerte « à renvoyer » si le contenu a changé.");
    }

    private static void Retex(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "8. Après l'OP : le RETEX");
        P(col, "Scénario › RETEX dresse le bilan à partir de tout ce qui a été enregistré : missions diffusées et terminées, retard moyen de diffusion, distance parcourue, sorties de jeu, messages reçus, et la chronologie de l'OP (ou d'une équipe).");
        Shot(col, img, "retex", "RETEX : bilan par équipe et chronologie.");
        Bullets(col,
            "RETEX global (PDF) : chiffres clés, tableau des équipes, chronologie complète, carte de tous les trajets aux couleurs des factions.",
            "RETEX par équipe (PDF) : missions prévues, diffusées et terminées avec leur durée, messages transmis (photos comprises), objets et effectif, carte du trajet.");
    }

    private static void Annexes(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img)
    {
        H1(col, "9. Annexes");
        H2(col, "Symboles militaires");
        P(col, "Symboles inspirés de l'APP-6 (cadre rempli de la couleur de la faction, pictogramme noir), avec l'indicateur de taille au-dessus du cadre.");
        if (img.TryGetValue("symboles", out var symbols))
            col.Item().Image(symbols).FitWidth();
        H2(col, "Formats de coordonnées acceptés à la saisie");
        Bullets(col,
            "UTM : 31T 448251 5411952 (fuseau et bande, abscisse, ordonnée).",
            "Degrés décimaux : 48.8583, 2.2944 ou 48,858370° N, 2,294481° E.",
            "Degrés-minutes-secondes : 48°51'30,1\"N 2°17'40,1\"E (ou degrés-minutes).");
        H2(col, "Travail à plusieurs");
        Bullets(col,
            "Fusionner une copie : intègre les modifications d'un fichier .aop reçu d'un autre orga (la modification la plus récente l'emporte).",
            "Travail partagé : fichier placé dans un dossier OneDrive (ou autre dossier synchronisé), synchronisé automatiquement toutes les 2 minutes ; chaque orga garde une copie locale.");
        H2(col, "Raccourcis");
        Bullets(col,
            "F11 : plein écran d'une fenêtre détachée (mode éclaté).",
            "Frise : double-clic pour créer une mission, molette pour défiler, Ctrl + molette pour zoomer.",
            "Carte : molette pour zoomer, glisser pour déplacer, double-clic pour la vue d'ensemble.");
    }

    // ----- Éléments de mise en page -----

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(1.6f, Unit.Centimetre);
        page.DefaultTextStyle(t => t.FontSize(10).LineHeight(1.3f));
        page.Header().BorderBottom(1).BorderColor(Accent).PaddingBottom(3).Row(row =>
        {
            row.RelativeItem().Text("Airsoft Planner — guide d'utilisation").FontSize(9).FontColor(Accent).SemiBold();
            row.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Darken1));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    private static void H1(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(10).Text(text).FontSize(20).Bold().FontColor(Accent);

    private static void H2(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(6).Text(text).FontSize(13).Bold();

    private static void P(ColumnDescriptor col, string text) => col.Item().Text(text);

    private static void Bullets(ColumnDescriptor col, params string[] items)
    {
        foreach (var item in items)
            col.Item().PaddingLeft(10).Row(row =>
            {
                row.ConstantItem(12).Text("•").FontColor(Accent);
                row.RelativeItem().Text(item);
            });
    }

    private static void Shot(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> img, string key, string caption)
    {
        if (!img.TryGetValue(key, out var image))
            return;
        col.Item().ShowEntire().Column(c =>
        {
            c.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Image(image).FitWidth();
            c.Item().PaddingTop(2).Text(caption).FontSize(8.5f).Italic().FontColor(Colors.Grey.Darken2);
        });
    }

    private static void PhoneRow(ColumnDescriptor col, IReadOnlyDictionary<string, byte[]> phone, (string Key, string Caption)[] items)
    {
        if (!items.Any(i => phone.ContainsKey(i.Key)))
            return;
        col.Item().ShowEntire().Row(row =>
        {
            row.Spacing(20);
            foreach (var (key, caption) in items)
                row.RelativeItem().Column(c =>
                {
                    if (phone.TryGetValue(key, out var image))
                        c.Item().AlignCenter().Height(11, Unit.Centimetre).Image(image).FitHeight();
                    c.Item().PaddingTop(2).AlignCenter().Text(caption).FontSize(8.5f).Italic().FontColor(Colors.Grey.Darken2);
                });
        });
    }

    private static void Table(ColumnDescriptor col, string[] headers, string[][] rows) => col.Item().Table(table =>
    {
        table.ColumnsDefinition(c =>
        {
            c.RelativeColumn(2.2f);
            c.RelativeColumn(4);
            c.RelativeColumn(3.5f);
        });
        table.Header(h =>
        {
            foreach (var header in headers)
                h.Cell().Background(Accent).Padding(4).Text(header).FontColor(Colors.White).SemiBold();
        });
        for (var i = 0; i < rows.Length; i++)
            foreach (var cell in rows[i])
                table.Cell().Background(i % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White).Padding(4).Text(cell).FontSize(9);
    });

    /// <summary>Captures converties en JPEG (documentation plus légère).</summary>
    private static byte[] Jpeg(byte[] png, int maxWidth)
    {
        using var bitmap = SKBitmap.Decode(png);
        var scale = Math.Min(1.0, (double)maxWidth / bitmap.Width);
        using var resized = scale < 1
            ? bitmap.Resize(new SKImageInfo((int)(bitmap.Width * scale), (int)(bitmap.Height * scale)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : bitmap.Copy();
        using var image = SKImage.FromBitmap(resized);
        return image.Encode(SKEncodedImageFormat.Jpeg, 85).ToArray();
    }
}
