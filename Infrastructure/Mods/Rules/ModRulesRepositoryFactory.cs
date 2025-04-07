using System;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Rules
{
    public class ModRulesRepositoryFactory
    {
        private readonly RulesConfiguration _config;

        public ModRulesRepositoryFactory(RulesConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Console.WriteLine($"[DEBUG] ModRulesRepositoryFactory created with config: StorageType={_config.StorageType}, SqliteConnectionString='{_config.SqliteConnectionString}'");
        }

        public IModRulesRepository CreateRepository()
        {
            Console.WriteLine($"[DEBUG] Creating ModRulesRepository for StorageType: {_config.StorageType}");
            switch (_config.StorageType)
            {
                case RulesStorageType.Json:
                    Console.WriteLine("[DEBUG] Instantiating JsonModRulesRepository");
                    return new JsonModRulesRepository();
                case RulesStorageType.Sqlite:
                    Console.WriteLine($"[DEBUG] Instantiating SqliteModRulesRepository with connection string: '{_config.SqliteConnectionString}'");
                    return new SqliteModRulesRepository(_config.SqliteConnectionString);
                default:
                    Console.WriteLine($"[ERROR] Unsupported StorageType encountered: {_config.StorageType}");
                    throw new NotImplementedException($"Storage type {_config.StorageType} is not implemented");
            }
        }
    }
}
