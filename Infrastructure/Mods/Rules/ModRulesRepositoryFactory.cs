using System;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Mods.Rules; // Added for specific repository types

namespace RimSharp.Infrastructure.Mods.Rules // Corrected namespace based on usage
{
    public class ModRulesRepositoryFactory
    {
        private readonly RulesConfiguration _config;
        private readonly string _appBasePath; // Store the base path

        // Constructor now accepts appBasePath
        public ModRulesRepositoryFactory(RulesConfiguration config, string appBasePath)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _appBasePath = appBasePath ?? throw new ArgumentNullException(nameof(appBasePath)); // Validate and store
            Console.WriteLine($"[DEBUG] ModRulesRepositoryFactory created with config: StorageType={_config.StorageType}, SqliteConnectionString='{_config.SqliteConnectionString}', AppBasePath='{_appBasePath}'");
        }

        public IModRulesRepository CreateRepository()
        {
            Console.WriteLine($"[DEBUG] Creating ModRulesRepository for StorageType: {_config.StorageType}");
            switch (_config.StorageType)
            {
                case RulesStorageType.Json:
                    Console.WriteLine("[DEBUG] Instantiating JsonModRulesRepository");
                    // Pass the stored appBasePath to the constructor
                    return new JsonModRulesRepository(_appBasePath);
                case RulesStorageType.Sqlite:
                    Console.WriteLine($"[DEBUG] Instantiating SqliteModRulesRepository with connection string: '{_config.SqliteConnectionString}'");
                    // SqliteModRulesRepository constructor might also need appBasePath if it handles file paths
                    // For now, assuming it only needs the connection string based on its current code.
                    // If it needs the path, modify its constructor and pass _appBasePath here too.
                    return new SqliteModRulesRepository(_config.SqliteConnectionString);
                default:
                    Console.WriteLine($"[ERROR] Unsupported StorageType encountered: {_config.StorageType}");
                    throw new NotImplementedException($"Storage type {_config.StorageType} is not implemented");
            }
        }
    }
}
