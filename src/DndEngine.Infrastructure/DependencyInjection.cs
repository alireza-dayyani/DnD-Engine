using DndEngine.Application;
using DndEngine.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace DndEngine.Infrastructure;

public sealed class RandomDiceRoller : IDiceRoller
{
    public DiceResult Roll(DiceExpression expression) => new(expression,
        Enumerable.Range(0, expression.Count).Select(_ => Random.Shared.Next(1, expression.Sides + 1)));
}
public static class DependencyInjection
{
    public static IServiceCollection AddDndEngine(this IServiceCollection services, string dataDirectory)
    {
        var directory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(directory);
        services.AddDbContext<CampaignDbContext>(o => o.UseSqlite(Connection(directory, "campaign.db")));
        services.AddDbContext<RulesDbContext>(o => o.UseSqlite(Connection(directory, "rules.db")));
        services.AddScoped<ICampaignStore, SqliteCampaignStore>();
        services.AddScoped<IRulesCatalog, RulesCatalog>();
        services.AddScoped<ICombatCatalog, CombatCatalog>(); services.AddScoped<ICombatStore, SqliteCombatStore>();
        services.AddScoped<IEncounterRewardStore, SqliteCombatStore>();
        services.AddScoped<ICharacterRulesCatalog, CharacterRulesCatalog>(); services.AddScoped<IProgressionStore, SqliteProgressionStore>();
        services.AddScoped<ISpellCatalog, SpellCatalog>();
        services.AddScoped<IMonsterCatalog, MonsterCatalog>();
        services.AddScoped<IMonsterStore, SqliteMonsterStore>();
        services.AddScoped<IEncounterItemCatalog, EncounterItemCatalog>();
        services.AddScoped<IInventoryStore, SqliteInventoryStore>();
        services.AddScoped<InventoryService>();
        services.AddScoped<EncounterRewardService>();
        services.AddScoped<MonsterService>();
        services.AddScoped<ProgressionService>();
        services.AddScoped<CombatService>();
        services.AddSingleton<IDiceRoller, RandomDiceRoller>(); services.AddSingleton(TimeProvider.System);
        services.AddScoped<CampaignService>(); services.AddScoped<CharacterService>(); services.AddScoped<MechanicsService>();
        return services;
    }
    private static string Connection(string directory, string file) => new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, file), ForeignKeys = true }.ToString();
    public static async Task InitializeDndEngineAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
        var campaign = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        await rules.Database.MigrateAsync(ct); await RulesCatalog.ImportAsync(rules, ct);
        await CombatCatalog.ImportAsync(rules, ct);
        await CharacterRulesCatalog.ImportAsync(rules, ct);
        await SpellCatalog.ImportAsync(rules, ct);
        await MonsterCatalog.ImportAsync(rules, ct);
        await EncounterItemCatalog.ImportAsync(rules, ct);
        await campaign.Database.MigrateAsync(ct);
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DndEngine.Startup")
            .LogInformation("Initialized SQLite databases; supported ruleset {Ruleset}/{Version}", Ruleset.Current.Id, Ruleset.Current.Version);
    }
}
