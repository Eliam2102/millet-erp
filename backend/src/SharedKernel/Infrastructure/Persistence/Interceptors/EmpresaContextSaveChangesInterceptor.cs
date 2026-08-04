using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Asigna <c>empresa_id</c> automáticamente en INSERT desde
/// <c>ICurrentEmpresaContext.Current</c>, y valida en INSERT/UPDATE que
/// ningún registro de <see cref="IPerteneceAEmpresa"/> cruce la frontera de
/// empresa. Si el contexto no tiene empresa actual y no estamos dentro de
/// un Bypass, lanza <see cref="MissingEmpresaContextException"/> (fail
/// closed). Ver ADR-0011.
/// </summary>
public sealed class EmpresaContextSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentEmpresaContext _empresaContext;

    public EmpresaContextSaveChangesInterceptor(ICurrentEmpresaContext empresaContext)
    {
        _empresaContext = empresaContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Validate(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Validate(DbContext context)
    {
        if (_empresaContext.IsBypassed) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IPerteneceAEmpresa empresaOwned) continue;

            var entityType = entry.Entity.GetType().Name;

            if (entry.State == EntityState.Added)
            {
                if (_empresaContext.Current is null)
                    throw new MissingEmpresaContextException(entityType);

                if (empresaOwned.EmpresaId == Guid.Empty)
                {
                    empresaOwned.EmpresaId = _empresaContext.Current.Value;
                }
                else if (empresaOwned.EmpresaId != _empresaContext.Current.Value)
                {
                    throw new CrossTenantViolationException(
                        entityType,
                        _empresaContext.Current.Value,
                        empresaOwned.EmpresaId);
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (_empresaContext.Current is null)
                    throw new MissingEmpresaContextException(entityType);

                if (empresaOwned.EmpresaId != _empresaContext.Current.Value)
                {
                    throw new CrossTenantViolationException(
                        entityType,
                        _empresaContext.Current.Value,
                        empresaOwned.EmpresaId);
                }
            }
        }
    }
}
