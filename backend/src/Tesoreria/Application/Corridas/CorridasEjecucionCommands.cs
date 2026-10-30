using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.Reportes.Comun;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Corridas;

// ============================================================================
// T9.1-B (86e3jvvhf): oficio de la corrida autorizada y registro de la
// ejecución. El dinero se mueve en la banca; aquí se capturan las referencias.
// Cada pago reutiliza RegistrarPagoProveedorCommand (un pago = un proveedor,
// varias facturas; revalida contra CxP con el elegible y el bloqueo).
// ============================================================================

// --------------------------------------------------- Oficio

public sealed record GenerarOficioCorridaCommand(Guid Id) : IRequest<ReporteJsonResponse>;

public sealed class GenerarOficioCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores, IClock clock)
    : IRequestHandler<GenerarOficioCorridaCommand, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(GenerarOficioCorridaCommand command, CancellationToken cancellationToken)
    {
        var c = await db.CorridasPago.Include(x => x.Lineas).Include(x => x.Firmas).FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CORRIDA_NO_ENCONTRADA", $"No se encontró la corrida '{command.Id}'.");
        var ahora = clock.UtcNow;
        c.RegistrarOficio(ahora);   // lanza CORRIDA_OFICIO_NO_AUTORIZADA en borrador, autorización, rechazada o cancelada
        var cuenta = await db.CuentasBancarias.AsNoTracking().FirstAsync(x => x.Id == c.CuentaBancariaId, cancellationToken);
        var info = await proveedores.ObtenerVariosAsync(c.Lineas.Select(l => l.ProveedorId).Distinct().ToList(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var filas = c.Lineas.GroupBy(l => l.ProveedorId).Select(g => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["proveedor"] = info.TryGetValue(g.Key, out var p) ? p.RazonSocial : g.Key.ToString(),
            ["facturas"] = g.Count(),
            ["bruto"] = g.Sum(l => l.Bruto),
            ["retenciones"] = g.Sum(l => l.Retenciones),
            ["neto"] = g.Sum(l => l.ImporteProgramado),
        }).OrderBy(f => (string?)f["proveedor"]).ToList();

        return new ReporteJsonResponse(
            Titulo: $"Oficio de corrida de pagos — {c.Nombre}",
            GeneradoEn: ahora,
            FiltrosAplicados:
            [
                new("Corrida", c.Nombre),
                new("Estado", c.Estado.ToString()),
                new("Cuenta de egreso", $"{cuenta.Banco} {Clabe.Enmascarar(cuenta.NumeroCuenta)} ({cuenta.Moneda})"),
                new("Autorizada el", c.AutorizadaEn?.ToString("yyyy-MM-dd HH:mm") ?? "—"),
                new("Firmas", $"{c.FirmasRecibidas} de {c.FirmasRequeridas}"),
            ],
            Columnas:
            [
                new("proveedor", "Proveedor", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
                new("facturas", "Facturas", TipoColumnaReporte.Entero, AlineacionColumna.Derecha),
                new("bruto", "Bruto", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("retenciones", "Retenciones", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("neto", "Neto a transferir", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
            ],
            Filas: filas,
            Totales: new Dictionary<string, object?>
            {
                ["facturas"] = c.Lineas.Count,
                ["bruto"] = c.Lineas.Sum(l => l.Bruto),
                ["retenciones"] = c.Lineas.Sum(l => l.Retenciones),
                ["neto"] = c.Total,
            });
    }
}

// --------------------------------------------------- Registrar ejecución

/// <summary>Un pago hecho en la banca: pasivos de un solo proveedor, con su referencia y fecha.</summary>
public sealed record PagoCorridaItem(IReadOnlyList<Guid> Facturas, string ReferenciaBancaria, DateOnly FechaValor);

public sealed record RegistrarEjecucionCorridaCommand(Guid Id, IReadOnlyList<PagoCorridaItem> Pagos, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class RegistrarEjecucionCorridaValidator : AbstractValidator<RegistrarEjecucionCorridaCommand>
{
    public RegistrarEjecucionCorridaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Pagos).NotEmpty().WithMessage("Indica al menos un pago.");
        RuleForEach(c => c.Pagos).ChildRules(p =>
        {
            p.RuleFor(x => x.Facturas).NotEmpty();
            p.RuleFor(x => x.ReferenciaBancaria).NotEmpty().MaximumLength(120);
            p.RuleFor(x => x.FechaValor).NotEmpty();
        });
    }
}

public sealed class RegistrarEjecucionCorridaHandler(TesoreriaDbContext db, ISender mediator, IProveedorBancoReadPort proveedores)
    : IRequestHandler<RegistrarEjecucionCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(RegistrarEjecucionCorridaCommand command, CancellationToken cancellationToken)
    {
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, cancellationToken);
        foreach (var pago in command.Pagos)
        {
            var lineas = c.RegistrarEjecucion(pago.Facturas.Distinct().ToList(), pago.ReferenciaBancaria, pago.FechaValor);
            if (lineas.Count == 0) continue;   // misma referencia ya registrada: no se suma otra vez

            // Revalidación contra CxP: un pasivo retirado o modificado después de autorizar no se paga.
            var ids = lineas.Select(l => l.FacturaProveedorId).ToList();
            var pasivos = await db.PasivosPendientesPago.Where(p => ids.Contains(p.FacturaProveedorId)).ToDictionaryAsync(p => p.FacturaProveedorId, cancellationToken);
            foreach (var l in lineas)
            {
                if (!pasivos.TryGetValue(l.FacturaProveedorId, out var p) || p.PagoBloqueado)
                    throw new BusinessRuleException("CORRIDA_PASIVO_RETIRADO",
                        $"CxP retiró la autorización del pasivo {l.FacturaProveedorId}; no se paga. Quítalo de la corrida o pide que CxP lo autorice de nuevo.");
                if (p.SaldoPendiente != l.ImporteProgramado)
                    throw new BusinessRuleException("CORRIDA_PASIVO_MODIFICADO",
                        $"El pasivo {l.FacturaProveedorId} cambió después de autorizar la corrida (autorizado {l.ImporteProgramado:N2}, vigente {p.SaldoPendiente:N2}); no se paga.");
            }

            // Un pago por proveedor. Guarda las líneas marcadas junto con el pago (misma transacción).
            await mediator.Send(new RegistrarPagoProveedorCommand(
                c.CuentaBancariaId, pago.FechaValor,
                lineas.Select(l => new AplicacionPagoItem(l.FacturaProveedorId, l.ImporteProgramado)).ToList(),
                pago.ReferenciaBancaria.Trim(), CorridaId: c.Id), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, cancellationToken);
    }
}

// --------------------------------------------------- Cerrar

public sealed record CerrarCorridaCommand(Guid Id, int VersionEsperada) : IRequest<CorridaResponse>;

public sealed class CerrarCorridaHandler(TesoreriaDbContext db, IProveedorBancoReadPort proveedores) : IRequestHandler<CerrarCorridaCommand, CorridaResponse>
{
    public async Task<CorridaResponse> Handle(CerrarCorridaCommand command, CancellationToken cancellationToken)
    {
        var c = await CorridaArmado.CargarAsync(db, command.Id, command.VersionEsperada, cancellationToken);
        c.CerrarEjecucion();
        await db.SaveChangesAsync(cancellationToken);
        return await CorridaLectura.ToResponseAsync(db, proveedores, c, cancellationToken);
    }
}
