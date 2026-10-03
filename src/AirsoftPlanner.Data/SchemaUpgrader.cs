using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace AirsoftPlanner.Data;

/// <summary>
/// Met à niveau un fichier d'OP créé par une version précédente : ajoute les tables et colonnes
/// manquantes (les évolutions du format sont uniquement additives). Les colonnes ajoutées
/// reçoivent la valeur par défaut de leur type (0, texte vide, liste vide...).
/// Règle d'évolution du modèle : ne jamais supprimer ni renommer une propriété persistée,
/// sinon les anciennes colonnes NOT NULL empêcheraient les insertions.
/// </summary>
internal static class SchemaUpgrader
{
    public static void Upgrade(OperationDbContext context)
    {
        var database = context.Database;
        database.OpenConnection();
        try
        {
            using var transaction = database.BeginTransaction();
            var connection = database.GetDbConnection();
            var createStatements = database.GenerateCreateScript()
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var existingTables = Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                var table = entityType.GetTableName()!;
                if (!existingTables.Contains(table))
                {
                    foreach (var statement in createStatements.Where(s =>
                                 s.Contains($"CREATE TABLE \"{table}\"") || s.Contains($"ON \"{table}\"")))
                        database.ExecuteSqlRaw(statement);
                    continue;
                }

                var store = StoreObjectIdentifier.Table(table);
                var existingColumns = Query(connection, $"SELECT name FROM pragma_table_info('{table}')");
                foreach (var property in entityType.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    if (column is null || existingColumns.Contains(column))
                        continue;

                    // Noms et types issus du modèle EF (pas d'une saisie) : pas de risque d'injection.
#pragma warning disable EF1002
                    database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {ColumnDefinition(property, store)}");
#pragma warning restore EF1002
                }
            }

            transaction.Commit();
        }
        finally
        {
            database.CloseConnection();
        }
    }

    private static string ColumnDefinition(IProperty property, StoreObjectIdentifier store)
    {
        var type = property.GetColumnType(store);
        if (property.IsColumnNullable(store))
            return type;

        var mapping = property.GetRelationalTypeMapping();
        // Valeur par défaut configurée dans le modèle (HasDefaultValue), sinon celle du type.
        var clrDefault = property.GetDefaultValue() is { } configured && property.ClrType.IsInstanceOfType(configured)
            ? configured
            : DefaultValue(property.ClrType);
        // GenerateSqlLiteral applique lui-même le convertisseur de valeur (liste → JSON, enum → texte...).
        return $"{type} NOT NULL DEFAULT {mapping.GenerateSqlLiteral(clrDefault)}";
    }

    private static object? DefaultValue(Type type)
    {
        if (type == typeof(string))
            return "";
        if (type == typeof(byte[]))
            return Array.Empty<byte>();
        // Types valeur et collections (List<T>...) : instance par défaut.
        return type.IsValueType || type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
    }

    private static HashSet<string> Query(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }
}
