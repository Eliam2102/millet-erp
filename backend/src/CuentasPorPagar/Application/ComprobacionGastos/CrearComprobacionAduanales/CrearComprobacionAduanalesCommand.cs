using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.CrearComprobacionAduanales;

/// <summary>
/// Crea una <see cref="Domain.ComprobacionGastos.ComprobacionGastos"/>
/// de tipo <c>GastosAduanales</c> (§7.2 caso especial Aduanales, F7-PR2).
///
/// <para>
/// A diferencia de Caja Chica, Aduanales <b>no genera</b> facturas: las
/// facturas ya existen porque fueron capturadas con OC vía
/// <c>CapturarFacturaConOcCommand</c> (la OC con la agencia aduanal es
/// fija para exportación o ad-hoc por cotización para importación). La
/// comprobación las agrupa para aplicar la doble firma (Comercio
/// Exterior + Dirección de Finanzas).
/// </para>
///
/// <para>
/// Validaciones:
/// <list type="bullet">
///   <item>Cada factura existe, es del mismo <c>ProveedorId</c> y está
///   en <see cref="EstadoPasivo.Capturada"/> (no autorizada, no
///   cancelada, no pagada).</item>
///   <item>Ninguna factura ya pertenece a otra comprobación (índice
///   único <c>ux_linea_comp_factura</c>).</item>
///   <item><c>NumeroPedimento</c> es obligatorio.</item>
/// </list>
/// </para>
/// </summary>
public sealed record CrearComprobacionAduanalesCommand(
    Guid SucursalId,
    Guid ResponsableId,
    Guid ProveedorId,
    string NumeroPedimento,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    string Moneda,
    string? Observaciones,
    IReadOnlyList<Guid> FacturaProveedorIds) : IRequest<CrearComprobacionAduanalesResponse>;

public sealed record CrearComprobacionAduanalesResponse(
    Guid Id,
    EstadoComprobacionGastos Estado,
    decimal MontoTotal,
    int NumeroLineas,
    string NumeroPedimento,
    int Version);

public sealed class CrearComprobacionAduanalesValidator : AbstractValidator<CrearComprobacionAduanalesCommand>
{
    public CrearComprobacionAduanalesValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.ResponsableId).NotEmpty();
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.NumeroPedimento).NotEmpty().MaximumLength(40);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.FacturaProveedorIds).NotEmpty()
            .WithMessage("Una comprobación de aduanales requiere al menos una factura.");
    }
}

public sealed class CrearComprobacionAduanalesHandler
    : IRequestHandler<CrearComprobacionAduanalesCommand, CrearComprobacionAduanalesResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public CrearComprobacionAduanalesHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _clock = clock;
    }

    public async Task<CrearComprobacionAduanalesResponse> Handle(
        CrearComprobacionAduanalesCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var idsDistintos = command.FacturaProveedorIds.Distinct().ToList();
        if (idsDistintos.Count != command.FacturaProveedorIds.Count)
        {
            throw new BusinessRuleException(
                "COMP_FACTURA_DUPLICADA",
                "La lista de facturas tiene IDs repetidos.");
        }

        var facturas = await _db.FacturasProveedor
            .Where(f => idsDistintos.Contains(f.Id))
            .ToListAsync(cancellationToken);

        if (facturas.Count != idsDistintos.Count)
        {
            var faltantes = idsDistintos.Except(facturas.Select(f => f.Id)).ToList();
            throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontraron las facturas: {string.Join(", ", faltantes)}.");
        }

        // Todas deben ser del mismo proveedor (la agencia aduanal) y
        // estar capturadas, no autorizadas / no canceladas.
        foreach (var f in facturas)
        {
            if (f.ProveedorId != command.ProveedorId)
                throw new BusinessRuleException(
                    "COMP_FACTURA_PROVEEDOR_MISMATCH",
                    $"La factura {f.Id} no pertenece al proveedor {command.ProveedorId}.");
            if (f.Estado != EstadoPasivo.Capturada)
                throw new BusinessRuleException(
                    "COMP_FACTURA_ESTADO_INVALIDO",
                    $"La factura {f.Id} debe estar en Capturada (actual: {f.Estado}).");
            if (f.OrdenCompraId is null)
                throw new BusinessRuleException(
                    "COMP_FACTURA_SIN_OC",
                    $"Aduanales requiere facturas con OC; la factura {f.Id} no la tiene.");
        }

        // Defensa: ¿alguna factura ya está ligada a una comprobación?
        var yaLigadas = await _db.Set<LineaComprobacionGastos>()
            .Where(l => idsDistintos.Contains(l.FacturaProveedorId))
            .Select(l => l.FacturaProveedorId)
            .ToListAsync(cancellationToken);
        if (yaLigadas.Count > 0)
        {
            throw new BusinessRuleException(
                "COMP_FACTURA_YA_LIGADA",
                $"Las facturas {string.Join(", ", yaLigadas)} ya están ligadas a otra comprobación.");
        }

        var ahora = _clock.UtcNow;

        var comprobacion = global::Millet.CuentasPorPagar.Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: empresaId,
            tipo: TipoComprobacionGastos.GastosAduanales,
            sucursalId: command.SucursalId,
            responsableId: command.ResponsableId,
            fechaInicio: command.FechaInicio,
            fechaFin: command.FechaFin,
            moneda: command.Moneda,
            observaciones: command.Observaciones,
            ahora: ahora,
            numeroPedimento: command.NumeroPedimento);

        _db.ComprobacionesGastos.Add(comprobacion);

        foreach (var f in facturas)
        {
            comprobacion.AgregarLinea(
                facturaProveedorId: f.Id,
                cfdiRecibidoId: f.CfdiRecibidoId,
                uuidCfdi: f.UuidCfdi,
                proveedorId: f.ProveedorId,
                folioProveedor: f.FolioProveedor,
                fechaCfdi: f.FechaDocumento,
                subtotal: f.Subtotal,
                impuestosTrasladados: f.ImpuestosTrasladados,
                retenciones: f.Retenciones,
                total: f.Total,
                moneda: command.Moneda,
                concepto: "Aduanales");
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CrearComprobacionAduanalesResponse(
            Id: comprobacion.Id,
            Estado: comprobacion.Estado,
            MontoTotal: comprobacion.MontoTotal,
            NumeroLineas: comprobacion.Lineas.Count,
            NumeroPedimento: comprobacion.NumeroPedimento!,
            Version: comprobacion.Version);
    }
}
