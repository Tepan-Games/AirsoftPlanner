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
| 1 | OP, factions, équipes, terrain et zones sur carte hors ligne (fonds IGN, coordonnées UTM / degrés) | ✅ |
| 2 | Missions et frise temporelle verticale par équipe, détection des conflits | ✅ |
| 3 | Gestion des retards : décalage en cascade, désactivation des missions non essentielles | |
| 4 | Inscriptions des équipes et suivi financier (paiements, dépenses, bilan) | |
| 5 | Génération PDF : ordres de mission, règles du jeu | |
| 6 | GPS en direct : saisie manuelle/import, smartphone sur réseau local, Meshtastic, webservice | |
| — | Fusion de deux copies d'un fichier `.aop` | |

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
