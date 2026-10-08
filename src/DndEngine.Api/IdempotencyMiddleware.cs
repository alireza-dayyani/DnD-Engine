using System.Security.Cryptography;
using System.Text;
using DndEngine.Application;
using DndEngine.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Api;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IdempotencyRequiredAttribute : Attribute;

internal sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CampaignDbContext db, TimeProvider clock)
    {
        if (context.Request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE"))
        {
            await next(context); return;
        }
        var required=context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyRequiredAttribute>() is not null;
        if (!context.Request.Headers.TryGetValue("X-Operation-Id",out var values))
        {
            if (required)
                await Results.Problem(statusCode:400,title:"X-Operation-Id is required for this command.")
                    .ExecuteAsync(context);
            else await next(context);
            return;
        }
        if (values.Count != 1 || !Guid.TryParse(values[0],out var operationId) || operationId==Guid.Empty)
        {
            await Results.Problem(statusCode:400,title:"X-Operation-Id must be a nonempty GUID.")
                .ExecuteAsync(context);
            return;
        }
        context.Request.EnableBuffering();
        using var requestBytes=new MemoryStream();
        await context.Request.Body.CopyToAsync(requestBytes,context.RequestAborted);
        context.Request.Body.Position=0;
        var fingerprint=Fingerprint(context,requestBytes.ToArray());
        var existing=await db.IdempotencyOperations.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.OperationId==operationId,context.RequestAborted);
        if (existing is not null)
        {
            await Replay(context,existing,fingerprint); return;
        }

        await using var transaction=await db.Database.BeginTransactionAsync(context.RequestAborted);
        var row=new IdempotencyOperationRow { OperationId=operationId,RequestHash=fingerprint };
        db.IdempotencyOperations.Add(row);
        try { await db.SaveChangesAsync(context.RequestAborted); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
            { SqliteErrorCode: 19 or 5 or 6 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            IdempotencyOperationRow? committed=null;
            for (var attempt=0;attempt<100 && committed is null;attempt++)
            {
                committed=await db.IdempotencyOperations.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.OperationId==operationId,context.RequestAborted);
                if (committed is null) await Task.Delay(50,context.RequestAborted);
            }
            if (committed is null) throw new StateConflictException("Operation is still in progress; retry with the same ID.");
            await Replay(context,committed,fingerprint); return;
        }
        var originalBody=context.Response.Body;
        await using var captured=new MemoryStream();
        context.Response.Body=captured;
        try
        {
            await next(context);
            if (context.Response.StatusCode < 400)
            {
                row.StatusCode=context.Response.StatusCode;
                row.ResponseBody=captured.ToArray();
                row.ContentType=context.Response.ContentType;
                row.Location=context.Response.Headers.Location;
                row.CompletedAtUtc=clock.GetUtcNow();
                // Existing stores clear their DbContext tracker after SaveChanges.
                // Reattach the claim before persisting the response in the same transaction.
                db.IdempotencyOperations.Update(row);
                await db.SaveChangesAsync(context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
            }
            else await transaction.RollbackAsync(CancellationToken.None);
            context.Response.Body=originalBody;
            captured.Position=0;
            await captured.CopyToAsync(originalBody,context.RequestAborted);
        }
        catch
        {
            context.Response.Body=originalBody;
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string Fingerprint(HttpContext context,byte[] body)
    {
        var prefix=Encoding.UTF8.GetBytes($"{context.Request.Method}\n{context.Request.Path}{context.Request.QueryString}\n");
        var payload=new byte[prefix.Length+body.Length];
        Buffer.BlockCopy(prefix,0,payload,0,prefix.Length);
        Buffer.BlockCopy(body,0,payload,prefix.Length,body.Length);
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static async Task Replay(HttpContext context,IdempotencyOperationRow row,string fingerprint)
    {
        if (row.RequestHash!=fingerprint)
            throw new StateConflictException("Operation ID was already used for a different request.");
        if (row.StatusCode==0)
            throw new StateConflictException("Operation is still in progress; retry with the same ID.");
        context.Response.StatusCode=row.StatusCode;
        if (row.ContentType is not null) context.Response.ContentType=row.ContentType;
        if (row.Location is not null) context.Response.Headers.Location=row.Location;
        await context.Response.Body.WriteAsync(row.ResponseBody,context.RequestAborted);
    }
}
