using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Npgsql;

namespace Millet.ProductoTerminado.Application.Escaneos;

public sealed class RegistrarEscaneoPiezaHandler(ProductoTerminadoDbContext db, EscaneoProcesador procesador, IClock clock)
    : IRequestHandler<RegistrarEscaneoPiezaCommand, EscaneoResultadoDto>
{
    // Máximo de reintentos por pedido inexistente; el worker de reintento usa el valor configurado.
    private const int MaxIntentosInicial = 5;

    public async Task<EscaneoResultadoDto> Handle(RegistrarEscaneoPiezaCommand c, CancellationToken cancellationToken)
    {
        var etiqueta = c.Etiqueta.Trim();
        var existente = await BuscarAsync(c, etiqueta, cancellationToken);
        if (existente is not null) return Dto(existente, true);

        var e = EscaneoPt.Registrar(c.EmpresaId, c.Origen, etiqueta, c.NumeroPedido, c.Posicion, c.UPos, c.NumeroPieza, c.Estacion, c.Area,
            c.RegistradoAt, HoraAw.AUtc(c.RegistradoAt), c.ClaveSucursalAw, null, clock.UtcNow);
        db.Escaneos.Add(e);
        await procesador.ProcesarAsync(e, MaxIntentosInicial, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Carrera con otra ejecución del mismo escaneo: se devuelve la fila ya guardada.
            db.ChangeTracker.Clear();
            var previo = await BuscarAsync(c, etiqueta, cancellationToken);
            if (previo is null) throw;
            return Dto(previo, true);
        }
        return Dto(e, false);
    }

    private Task<EscaneoPt?> BuscarAsync(RegistrarEscaneoPiezaCommand c, string etiqueta, CancellationToken ct) =>
        db.Escaneos.AsNoTracking().FirstOrDefaultAsync(x => x.EmpresaId == c.EmpresaId && x.Origen == c.Origen && x.Etiqueta == etiqueta && x.RegistradoAt == c.RegistradoAt, ct);

    private static EscaneoResultadoDto Dto(EscaneoPt e, bool repetido) => new(e.Id, e.Resultado.ToString(), e.Motivo, e.PiezaId, repetido);
}
