using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

/// <summary>Direct-call counterpart of the HTTP idempotency boundary; both use the same durable table.</summary>
public sealed class DurableMcpCommandRunner(CampaignDbContext db, TimeProvider clock)
{
    public async Task<string> RunAsync<T>(Guid operationId,Guid subjectId,string tool,
        object input,Func<CancellationToken,Task<T>> command,CancellationToken ct=default)
    {
        if (operationId==Guid.Empty || subjectId==Guid.Empty ||
            string.IsNullOrWhiteSpace(tool))
            throw new RuleViolation("A nonempty operation ID is required.");
        var serialized=JsonSerializer.Serialize(input,CombatCatalog.Json);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"MCP\n{subjectId:D}\n{tool}\n{serialized}")));
        var existing=await db.IdempotencyOperations.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.OperationId==operationId,ct);
        if (existing is not null) return Replay(existing,fingerprint);

        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var row=new IdempotencyOperationRow { OperationId=operationId,RequestHash=fingerprint };
        db.IdempotencyOperations.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
            { SqliteErrorCode: 19 or 5 or 6 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return await WaitForReplay(operationId,fingerprint,ct);
        }
        try
        {
            var result=await command(ct);
            var json=JsonSerializer.Serialize(result,CombatCatalog.Json);
            row.StatusCode=200;
            row.ContentType="application/json";
            row.ResponseBody=Encoding.UTF8.GetBytes(json);
            row.CompletedAtUtc=clock.GetUtcNow();
            db.IdempotencyOperations.Update(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return json;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<string> WaitForReplay(Guid id,string hash,CancellationToken ct)
    {
        for(var attempt=0;attempt<100;attempt++)
        {
            var row=await db.IdempotencyOperations.AsNoTracking()
                .SingleOrDefaultAsync(x=>x.OperationId==id,ct);
            if (row is not null) return Replay(row,hash);
            await Task.Delay(50,ct);
        }
        throw new StateConflictException("Operation is still in progress; retry with the same ID.");
    }

    private static string Replay(IdempotencyOperationRow row,string hash)
    {
        if (row.RequestHash!=hash)
            throw new StateConflictException("Operation ID was already used for a different request.");
        if (row.StatusCode!=200)
            throw new StateConflictException("Operation is still in progress; retry with the same ID.");
        return Encoding.UTF8.GetString(row.ResponseBody);
    }
}
