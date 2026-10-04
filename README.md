# Airsoft Planner

Logiciel de gestion d'OP (parties organisées) d'airsoft : création des factions et du scénario,
inscription des équipes, frise temporelle des missions avec gestion des retards, suivi GPS des
équipes, génération des ordres de mission et des règles du jeu, suivi des finances.

Application de bureau **hors ligne d'abord**, en C# / .NET 10 avec [Avalonia](https://avaloniaui.net/).

## Fichiers d'OP partageables (`.aop`)

Une OP = un fichier `.aop`. Il contient toute l'opération (base SQLite autonome) et peut être
transmis tel quel entre orgas (mail, clé USB, Drive…) pour travailler sur la même OP.

Les fichiers créés par une version précédente sont **mis à niveau automatiquement** à l'ouverture
(ajout des nouvelles tables et colonnes) ; un fichier créé par une version plus récente est refusé
avec un message invitant à mettre le logiciel à jour.

Chaque donnée porte un identifiant global (Guid), une date de modification et un marqueur de
suppression : c'est ce qui permettra de **fusionner** deux copies d'une même OP modifiées en
parallèle par des orgas différents.

## Feuille de route

| Phase | Contenu | État |
|---|---|---|
| 0 | Socle : solution, fichier `.aop`, fenêtre principale | ✅ |
| 1 | OP, factions, équipes, terrain et zones (fonds IGN, coordonnées UTM / degrés) | ✅ |
| 2 | Missions et frise temporelle verticale, détection des conflits | ✅ |
| 3 | Retards : décalage en cascade, missions optionnelles à désactiver | ✅ |
| 4 | Inscriptions (statuts, liste d'attente, CSV) et finances (paiements, remises, carburant, dépenses, bilan) | ✅ |
| 5 | Règles et packages d'équipe (ordre de mission PDF avec carte, suivi de réception) | ✅ |
| 6 | GPS : serveur local (Traccar Client, saisie web), Meshtastic, Traccar, fichiers ; second poste | ✅ |
| — | Suivi de l'OP : effectifs, hors-jeu, objets d'objectif, trajets, mission urgente, mode éclaté | ✅ |
| — | Fusion de copies et travail partagé (OneDrive, Google Drive, Dropbox, dossier réseau) | ✅ |
| — | Version autonome Windows, association des fichiers `.aop` | ✅ |
| 7 | Application Android (enrôlement par code ou QR, positions, alliés, mission, plan radio, onglets QG / ORGA, mode nuit) | ✅ |
| — | Messages QG / Orga avec photos, diffusion des missions décidée par l'orga, notifications | ✅ |
| — | Points d'intérêt par faction, symboles militaires (APP-6) aux couleurs des factions, quadrillage par fuseau UTM | ✅ |
| — | DynDNS et recherche du PC sur le Wi-Fi, téléphones des orgas, RETEX (global et par équipe), relecture de la simulation | ✅ |
| — | Documentation PDF avec captures, archive de distribution complète | ✅ |
| 8 | Multilingue (anglais, allemand, espagnol, italien) | À faire |

## Cartes et coordonnées

- **Fonds de carte IGN** (Géoplateforme, Licence Ouverte) : photo aérienne et Plan IGN v2, jusqu'à
  ~20 cm par pixel. On les télécharge pendant la préparation, et ils sont stockés dans le fichier `.aop` :
  la carte fonctionne ensuite **hors ligne** et voyage avec le fichier. Une image personnelle (plan
  du terrain orienté nord) peut aussi être importée.
- **Coordonnées** : UTM (`31T 448251 5411952`), degrés décimaux (`48,858370° N, 2,294481° E`) et
  degrés-minutes-secondes (`48°51'30,1"N 2°17'40,1"E`). Le format d'affichage se règle par OP ;
  la saisie accepte tous les formats (y compris un copier-coller depuis Google Maps).
- **Carte** : quadrillage UTM, zones (polygones) et points, molette pour zoomer, glisser pour se
  déplacer, double-clic pour la vue d'ensemble, sommets de la zone sélectionnée déplaçables.

## Organisation

- **OP sur plusieurs jours** : date et heure de début et de fin ; la frise marque chaque changement de jour.
- **Factions** : couleur, effectif minimum et maximum (comparés à l'effectif réel des équipes),
  brassard, tenue ou camouflage imposé, équipe chef de faction, fréquence radio de commandement.
- **Équipes** : membres (nom, pseudo / indicatif, rôle, portable, e-mail, chef d'équipe), effectif
  annoncé tant que les membres ne sont pas saisis, fréquence radio, véhicules (type, nombre, remarques).
- **Matériel de jeu** : caisses, artifices, fumigènes, accessoires… avec leur stock ; chaque élément
  indique dans quelles missions il est utilisé. Un manque de stock est signalé (au total pour un
  consommable, en simultané pour un élément réutilisable).

## Scénario et frise

- **Missions** : nom, briefing, zone, une ou plusieurs équipes, début et durée, essentielle ou
  optionnelle, activée ou non, prérequis (missions à terminer avant).
- **Frise verticale** : une colonne par équipe (regroupées par faction), le temps qui descend.
  Glisser une mission pour la déplacer ou la confier à une autre équipe, étirer son bord bas pour
  changer sa durée, double-cliquer dans une colonne pour créer une mission. Les missions qui se
  chevauchent s'affichent côte à côte.
- **Contrôle du planning** : sont signalés en rouge une équipe sur deux missions à la fois, une
  mission qui commence avant la fin d'un prérequis (ou dont le prérequis est désactivé), une
  mission hors des horaires de l'OP, une mission sans équipe, et les boucles de prérequis (qu'on
  ne peut d'ailleurs pas créer).

## Suivi de l'OP

Dans l'onglet Scénario, le mode **Suivi de l'OP** affiche la frise avec la ligne « maintenant », la
carte avec la dernière position reçue de chaque équipe, et l'état de chaque équipe : à l'heure,
juste, en retard (distance jusqu'à la zone de sa mission, temps de marche estimé et marge restante).
L'heure suivie est l'heure réelle, ou une heure simulée pour préparer ou rejouer l'OP. Les positions
se saisissent pour l'instant à la main (clic sur la carte ou coordonnées reçues par radio).

## Travail à plusieurs

- **Fusionner une copie** : intègre les modifications d'une autre copie de la même OP (la version la
  plus récente de chaque élément l'emporte, suppressions comprises).
- **Travail partagé** : chaque orga garde sa copie de travail ; le logiciel la synchronise toutes les
  2 minutes (et à chaque enregistrement) avec un fichier placé dans un dossier synchronisé (OneDrive…).
  Le fichier partagé n'est jamais ouvert directement ; les copies en conflit créées par le service de
  synchronisation sont fusionnées puis supprimées.

## Réception GPS et application Android

Le PC qui mène l'OP active son **serveur local** (onglet Suivi → Réception GPS), sur le Wi-Fi du terrain :

| Adresse | Usage |
|---|---|
| `/` (protocole OsmAnd) | Traccar Client, OsmAnd, GPSLogger |
| `/saisie` | Page pour taper ses coordonnées depuis un téléphone, sans application |
| `/api/positions` | Dernières positions, pour un second poste Airsoft Planner |
| `/api/enroll`, `/api/track`, `/api/map/image` | Application Android : enrôlement par code d'équipe (QR code), envoi des positions ; en retour, mission en cours, plan radio, numéro d'urgence et positions des alliés (rien, coordonnées ou carte, selon l'OP) |

Construire l'application Android nécessite le module .NET pour Android, à installer une fois dans un
terminal administrateur : `dotnet workload install android`.

## Application Android

```bash
dotnet build src/AirsoftPlanner.Mobile -c Release
```

produit `src/AirsoftPlanner.Mobile/bin/Release/net10.0-android/com.tepangames.airsoftplanner-Signed.apk`,
installable directement sur les téléphones. Première fois : `dotnet workload install android`
(terminal administrateur), puis `dotnet build src/AirsoftPlanner.Mobile -t:InstallAndroidDependencies
-p:AcceptAndroidSDKLicenses=True` (SDK Android et JDK dans le profil).

**Tests d'interaction application ↔ logiciel** (émulateur démarré, APK installé) :

```bash
dotnet run --project tests/AirsoftPlanner.Integration -- "Démo Fontainebleau v12.aop"
```

Enrôlement (code faux, saisie manuelle, lien du QR code), positions reçues, mission affichée,
intervalle et partage des alliés modifiés par l'orga, numéro d'urgence, coupure du serveur et
rattrapage des positions, arrêt de l'envoi, révocation.

## Documentation

[`docs/Airsoft Planner - Guide d'utilisation.pdf`](docs/) : tous les écrans du logiciel et de
l'application, la diffusion des missions, les messages, toutes les sources de positions GPS, les
documents imprimés et le RETEX. Régénérée par l'outil `tools/AirsoftPlanner.Docs` (captures du
logiciel sur une OP de démonstration) et les captures du téléphone produites par les tests
d'interaction :

```bash
dotnet run --project tools/AirsoftPlanner.Docs -- "exemple.aop" "<dossier des captures Android>" "docs/Airsoft Planner - Guide d'utilisation.pdf"
```

L'icône de l'application est générée par `tools/make_icons.py` (Windows et Android).

## Distribution

```bash
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Exemple "exemple.aop"
```

produit `artifacts/AirsoftPlanner-<version>.zip` : programme d'installation Windows
(`AirsoftPlanner-<version>-Setup.exe`, Inno Setup 6 requis : `winget install JRSoftware.InnoSetup` ;
installation pour l'utilisateur sans .NET à installer, raccourcis, fichiers `.aop` ouverts par double-clic,
désinstallation depuis Paramètres › Applications), application Android (`AirsoftPlanner.apk`), guide
d'utilisation PDF et OP d'exemple.
`scripts/publish.ps1` seul produit uniquement le logiciel Windows. Avec `-Publier`, la version est publiée sur
GitHub avec les notes de `docs/versions/<version>.md` ; les logiciels et applications installés la proposent
alors en mise à jour.

## Signature de l'application Android

Les mises à jour de l'APK doivent toujours être signées avec la même clé : Android refuse sinon de les
installer par-dessus la version précédente. La clé de publication se crée une fois (mot de passe choisi et
conservé par le mainteneur, jamais dans le dépôt) :

```powershell
& "$env:LOCALAPPDATA\Android\jdk\bin\keytool.exe" -genkeypair -v -keystore "$env:USERPROFILE\AirsoftPlanner-signature\airsoftplanner.keystore" -alias airsoftplanner -keyalg RSA -keysize 4096 -validity 36500 -dname "CN=Tepan Games, O=Tepan Games, C=FR"
```

`scripts/release.ps1` signe ensuite l'APK avec cette clé (mot de passe demandé à chaque publication, ou lu dans
la variable d'environnement `AIRSOFTPLANNER_KEYSTORE_PASS`). Sauvegarder ce fichier et son mot de passe hors du PC :
sans eux, aucune mise à jour de l'application ne pourra plus être installée sur les téléphones existants.

## Licence

Airsoft Planner est un logiciel libre distribué sous licence **GNU GPL v3.0 ou ultérieure** (voir [LICENSE](LICENSE)) :
chacun peut l'utiliser, l'étudier, le modifier et le redistribuer, à condition que les versions redistribuées
restent sous la même licence, avec leur code source. Une permission supplémentaire (section 7) autorise la
combinaison avec QuestPDF, distribuée sous sa propre licence. Le copyright, cette permission, les composants
tiers et l'attribution des fonds de carte IGN sont dans [NOTICE](NOTICE).

## Structure

```
src/AirsoftPlanner.Core   Modèle métier (sans dépendance d'interface ni de stockage)
src/AirsoftPlanner.Data   Stockage : fichier .aop (EF Core + SQLite)
src/AirsoftPlanner.App    Application de bureau Avalonia (MVVM, CommunityToolkit.Mvvm)
tests/                    Tests xUnit
```

## Développement

Prérequis : SDK .NET 10.

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/AirsoftPlanner.App
```
