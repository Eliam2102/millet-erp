using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.CrearComprobacionCajaChica;

/// <summary>
/// Crea una <see cref="Domain.ComprobacionGastos.ComprobacionGastos"/>
/// de tipo <c>ReembolsoCajaChica</c> (§7.4.1 del 00-levantamiento,
/// F7-PR1). Cada CFDI del payload se persiste como una
/// <see cref="Domain.FacturaProveedor.FacturaProveedor"/> sin OC y se
/// liga vía <c>LineaComprobacionGastos</c> para que afecte gasto/IVA/DIOT
/// individualmente (requerimiento explícito del área).
///
/// <para>
/// La comprobación nace en Borrador. El responsable de sucursal la
/// autoriza con <c>AutorizarComprobacionGastosCommand</c>, luego CxP la
/// aplica con <c>AplicarComprobacionGastosCommand</c> para pasar a
/// Tesorería.
/// </para>
/// </summary>
public sealed record CrearComprobacionCajaChicaCommand(
    Guid SucursalId,
    Guid ResponsableId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    string Moneda,
    string? Observaciones,
    IReadOnlyList<CrearComprobacionCajaChicaLinea> Cfdis,
    // GI-PR1 (doc 12 Q1): destino de la reposición. Default CuentaSucursal
    // para no romper al FE hasta que el Sheet gane el selector (GI-PR4).
    DestinoReposicionCaja DestinoReposicion = DestinoReposicionCaja.CuentaSucursal) : IRequest<CrearComprobacionCajaChicaResponse>;

public sealed record CrearComprobacionCajaChicaLinea(
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    decimal Subtotal,
    decimal Descuentos,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    DateOnly FechaVencimiento,
    string? Concepto);

public sealed record CrearComprobacionCajaChicaResponse(
    Guid Id,
    EstadoComprobacionGastos Estado,
    decimal MontoTotal,
    int NumeroLineas,
    IReadOnlyList<Guid> FacturaIds,
    int Version);

public sealed class CrearComprobacionCajaChicaValidator : AbstractValidator<CrearComprobacionCajaChicaCommand>
{
    public CrearComprobacionCajaChicaValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.ResponsableId).NotEmpty();
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.DestinoReposicion).IsInEnum();
        RuleFor(c => c.Cfdis).NotEmpty().WithMessage("Una comprobación de caja chica requiere al menos un CFDI.");
        RuleForEach(c => c.Cfdis).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ProveedorId).NotEmpty();
            linea.RuleFor(l => l.Total).GreaterThan(0);
            linea.RuleFor(l => l.Subtotal).GreaterThanOrEqualTo(0);
            linea.RuleFor(l => l.ImpuestosTrasladados).GreaterThanOrEqualTo(0);
            linea.RuleFor(l => l.Retenciones).GreaterThanOrEqualTo(0);
            linea.RuleFor(l => l.Descuentos).GreaterThanOrEqualTo(0);
            linea.RuleFor(l => l.FolioProveedor).MaximumLength(40);
            linea.RuleFor(l => l.SerieProveedor).MaximumLength(25);
        });
    }
}

public sealed class CrearComprobacionCajaChicaHandler
    : IRequestHandler<CrearComprobacionCajaChicaCommand, CrearComprobacionCajaChicaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public CrearComprobacionCajaChicaHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _clock = clock;
    }

    public async Task<CrearComprobacionCajaChicaResponse> Handle(
        CrearComprobacionCajaChicaCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // Resolución de CFDIs vinculados: cada CfdiRecibidoId debe existir
        // en el repositorio y seguir PorProcesar; al persistir la factura se
        // marca ConvertidoEnPasivo (mismo patrón que CapturarFacturaConOc).
        var cfdiIds = command.Cfdis
            .Where(l => l.CfdiRecibidoId is not null)
            .Select(l => l.CfdiRecibidoId!.Value)
            .ToList();
        if (cfdiIds.Distinct().Count() != cfdiIds.Count)
        {
            throw new BusinessRuleException(
                "COMP_LINEA_CFDI_DUPLICADO",
                "La comprobación tiene líneas que referencian el mismo CFDI recibido.");
        }

        var cfdisPorId = cfdiIds.Count == 0
            ? new Dictionary<Guid, CfdiRecibido>()
            : await _db.CfdisRecibidos
                .Where(c => cfdiIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        foreach (var linea in command.Cfdis)
        {
            if (linea.CfdiRecibidoId is not Guid cfdiId)
                continue;
            if (!cfdisPorId.TryGetValue(cfdiId, out var cfdi))
            {
                throw new EntityNotFoundException(
                    "COMP_CFDI_NO_ENCONTRADO",
                    $"No se encontró el CFDI recibido '{cfdiId}'.");
            }
            if (cfdi.Estado != EstadoCfdiRecibido.PorProcesar)
            {
                throw new BusinessRuleException(
                    "COMP_CFDI_YA_PROCESADO",
                    $"El CFDI {cfdi.UuidCfdi.Valor} ya no está PorProcesar (estado actual: {cfdi.Estado}).");
            }
            if (!string.IsNullOrWhiteSpace(linea.UuidCfdi) &&
                !string.Equals(linea.UuidCfdi.Trim(), cfdi.UuidCfdi.Valor, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException(
                    "COMP_CFDI_UUID_NO_COINCIDE",
                    $"El UUID de la línea ({linea.UuidCfdi}) no coincide con el del CFDI vinculado ({cfdi.UuidCfdi.Valor}).");
            }
        }

        // Dedup defensivo: si algún UUID viene repetido en la lista, lo
        // rechazamos. El UUID efectivo de una línea vinculada sin UUID
        // explícito es el del CfdiRecibido.
        var uuidsConValor = command.Cfdis
            .Select(l => l.UuidCfdi
                ?? (l.CfdiRecibidoId is Guid id ? cfdisPorId[id].UuidCfdi.Valor : null))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!.Trim().ToUpperInvariant())
            .ToList();
        if (uuidsConValor.Distinct(StringComparer.Ordinal).Count() != uuidsConValor.Count)
        {
            throw new BusinessRuleException(
                "COMP_LINEA_UUID_DUPLICADO",
                "La comprobación tiene CFDIs con UUID duplicado.");
        }

        // Verificamos que ninguno de los UUIDs ya esté ligado a una factura
        // capturada previamente — el SAT no permite duplicidad. Las
        // Canceladas no cuentan (una comprobación rechazada cancela sus
        // facturas y libera los CFDIs para re-captura, P7-H1).
        if (uuidsConValor.Count > 0)
        {
            var yaCapturados = await _db.FacturasProveedor
                .AsNoTracking()
                .Where(f => f.UuidCfdi != null && uuidsConValor.Contains(f.UuidCfdi)
                    && f.Estado != Domain.FacturaProveedor.EstadoPasivo.Cancelada)
                .Select(f => f.UuidCfdi!)
                .ToListAsync(cancellationToken);
            if (yaCapturados.Count > 0)
            {
                throw new BusinessRuleException(
                    "COMP_LINEA_FACTURA_DUPLICADA",
                    $"Los CFDIs {string.Join(", ", yaCapturados)} ya están capturados como facturas.");
            }
        }

        var ahora = _clock.UtcNow;

        var comprobacion = global::Millet.CuentasPorPagar.Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: empresaId,
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: command.SucursalId,
            responsableId: command.ResponsableId,
            fechaInicio: command.FechaInicio,
            fechaFin: command.FechaFin,
            moneda: command.Moneda,
            observaciones: command.Observaciones,
            ahora: ahora,
            destinoReposicion: command.DestinoReposicion);

        _db.ComprobacionesGastos.Add(comprobacion);

        var facturaIds = new List<Guid>(command.Cfdis.Count);
        foreach (var linea in command.Cfdis)
        {
            var cfdiVinculado = linea.CfdiRecibidoId is Guid vinculadoId
                ? cfdisPorId[vinculadoId]
                : null;
            var uuidNorm = (linea.UuidCfdi ?? cfdiVinculado?.UuidCfdi.Valor)?.Trim().ToUpperInvariant();
            var factura = global::Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
                empresaId: empresaId,
                cfdiRecibidoId: linea.CfdiRecibidoId,
                uuidCfdi: uuidNorm,
                proveedorId: linea.ProveedorId,
                sucursalId: command.SucursalId,
                folioProveedor: linea.FolioProveedor,
                serieProveedor: linea.SerieProveedor,
                fechaDocumento: linea.FechaCfdi,
                fechaContabilizacion: linea.FechaCfdi,
                fechaVencimiento: linea.FechaVencimiento,
                moneda: command.Moneda,
                tipoCambio: null,
                subtotal: linea.Subtotal,
                descuentos: linea.Descuentos,
                impuestosTrasladados: linea.ImpuestosTrasladados,
                retenciones: linea.Retenciones,
                total: linea.Total,
                motivoCaptura: $"Caja chica — comprobación {comprobacion.Id}",
                ahora: ahora);

            _db.FacturasProveedor.Add(factura);
            facturaIds.Add(factura.Id);

            if (cfdiVinculado is not null)
            {
                cfdiVinculado.MarcarConvertidoEnPasivo(factura.Id);
                factura.AsignarMetodoPago(cfdiVinculado.MetodoPago);
            }

            comprobacion.AgregarLinea(
                facturaProveedorId: factura.Id,
                cfdiRecibidoId: linea.CfdiRecibidoId,
                uuidCfdi: uuidNorm,
                proveedorId: linea.ProveedorId,
                folioProveedor: linea.FolioProveedor,
                fechaCfdi: linea.FechaCfdi,
                subtotal: linea.Subtotal,
                impuestosTrasladados: linea.ImpuestosTrasladados,
                retenciones: linea.Retenciones,
                total: linea.Total,
                moneda: command.Moneda,
                concepto: linea.Concepto);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CrearComprobacionCajaChicaResponse(
            Id: comprobacion.Id,
            Estado: comprobacion.Estado,
            MontoTotal: comprobacion.MontoTotal,
            NumeroLineas: comprobacion.Lineas.Count,
            FacturaIds: facturaIds,
            Version: comprobacion.Version);
    }
}
