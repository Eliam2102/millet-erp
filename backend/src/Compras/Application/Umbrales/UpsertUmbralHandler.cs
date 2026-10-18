using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Umbrales;

/// <summary>
/// Handler de <see cref="UpsertUmbralCommand"/>. Ver la documentación del
/// comando para las reglas de versionado. Todo ocurre en una sola TX.
/// </summary>
public sealed class UpsertUmbralHandler : IRequestHandler<UpsertUmbralCommand, UmbralAprobacionResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public UpsertUmbralHandler(ComprasDbContext db, ICurrentEmpresaContext currentEmpresa, IClock clock)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task<UmbralAprobacionResponse> Handle(
        UpsertUmbralCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // ponytail: dos PUT simultáneos el mismo día para el mismo depto chocan en la PK
        // (empresa, depto, vigente_desde) → 500; si molesta, traducir la violación a 409.
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var vigente = await _db.UmbralesAprobacionDepartamento
            .FirstOrDefaultAsync(
                u => u.EmpresaId == empresaId
                  && u.DepartamentoId == command.DepartamentoId
                  && u.VigenteHasta == null,
                cancellationToken);

        UmbralAprobacionDepartamento fila;
        if (vigente is not null && vigente.VigenteDesde == hoy)
        {
            // Ya hay una fila de hoy: se corrige en sitio (no cerrar a hoy-1 < VigenteDesde).
            vigente.ActualizarMontos(command.UmbralMenorCuantia, command.UmbralMonto, command.Moneda);
            fila = vigente;
        }
        else
        {
            vigente?.CerrarVigencia(hoy.AddDays(-1));
            fila = new UmbralAprobacionDepartamento(
                empresaId: empresaId,
                departamentoId: command.DepartamentoId,
                vigenteDesde: hoy,
                umbralMonto: command.UmbralMonto,
                moneda: command.Moneda,
                umbralMenorCuantia: command.UmbralMenorCuantia);
            _db.UmbralesAprobacionDepartamento.Add(fila);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new UmbralAprobacionResponse(
            fila.DepartamentoId, fila.UmbralMenorCuantia, fila.UmbralMonto, fila.Moneda, fila.VigenteDesde);
    }
}
