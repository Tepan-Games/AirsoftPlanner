# Airsoft Planner

Logiciel de gestion d'OP (parties organisées) d'airsoft : création des factions et du scénario,
inscription des équipes, frise temporelle des missions avec gestion des retards, suivi GPS des
équipes, génération des ordres de mission et des règles du jeu, suivi des finances.

Application de bureau **hors ligne d'abord**, en C# / .NET 10 avec [Avalonia](https://avaloniaui.net/).

## Fichiers d'OP partageables (`.aop`)

Une OP = un fichier `.aop`. Il contient toute l'opération (base SQLite autonome) et peut être
transmis tel quel entre orgas (mail, clé USB, Drive…) pour travailler sur la même OP.

Chaque donnée porte un identifiant global (Guid), une date de modification et un marqueur de
suppression : c'est ce qui permettra de **fusionner** deux copies d'une même OP modifiées en
parallèle par des orgas différents.

## Feuille de route

| Phase | Contenu | État |
|---|---|---|
| 0 | Socle : solution, fichier `.aop`, fenêtre principale | ✅ |
| 1 | OP, factions, équipes, terrain et zones sur carte hors ligne | |
| 2 | Missions et frise temporelle verticale par équipe | |
| 3 | Gestion des retards : décalage en cascade, désactivation des missions non essentielles | |
| 4 | Inscriptions des équipes et suivi financier (paiements, dépenses, bilan) | |
| 5 | Génération PDF : ordres de mission, règles du jeu | |
| 6 | GPS en direct : saisie manuelle/import, smartphone sur réseau local, Meshtastic, webservice | |
| — | Fusion de deux copies d'un fichier `.aop` | |

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
