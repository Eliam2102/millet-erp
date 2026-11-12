using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Proformas.EmitirFacturaDesdeProforma;

/// <summary>
/// M-340: factura un pedido desde la versión aprobada de su proforma, sin recaptura. La factura se arma con el mismo armado
/// de la proforma (<see cref="ProformaEmisionDesdePedido"/>) y sale por la emisión de siempre, ligada a esa versión. Se
/// bloquea si la versión no tiene conformidad, si su conformidad está invalidada o si el pedido cambió después de aprobar: en
/// ese caso la conformidad se invalida y hay que generar una versión nueva y registrar su conformidad.
/// </summary>
public sealed record EmitirFacturaDesdeProformaCommand(Guid PedidoFacturableId, Guid ProformaId) : IRequest<EmitirFacturaVentaResponse>;

public sealed class EmitirFacturaDesdeProformaValidator : AbstractValidator<EmitirFacturaDesdeProformaCommand>
{
    public EmitirFacturaDesdeProformaValidator()
    {
        RuleFor(c => c.PedidoFacturableId).NotEmpty();
        RuleFor(c => c.ProformaId).NotEmpty();
    }
}

public sealed class EmitirFacturaDesdeProformaHandler(
    FacturacionDbContext db,
    ISender sender,
    IEmpresaFiscalReadPort empresasFiscal,
    ICurrentEmpresaContext empresa,
    IClock clock) : IRequestHandler<EmitirFacturaDesdeProformaCommand, EmitirFacturaVentaResponse>
{
    /// <summary>Diferencias que se muestran en el mensaje de bloqueo (el resto se resume).</summary>
    private const int DiferenciasEnMensaje = 5;

    public async Task<EmitirFacturaVentaResponse> Handle(EmitirFacturaDesdeProformaCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        // El detalle respeta el alcance del usuario sobre el pedido (404 si no lo ve).
        var pedido = await sender.Send(new PedidoFacturableDetalleQuery(command.PedidoFacturableId), cancellationToken);
        if (pedido.Estado != nameof(EstadoPedidoFacturable.Importado))
            throw new BusinessRuleException("FAC_PROFORMA_PEDIDO_NO_FACTURABLE",
                $"Solo se factura un pedido por facturar (estado actual: {pedido.Estado}).");

        var proforma = await db.ProformasPedido.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == command.ProformaId && p.PedidoFacturableId == pedido.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PROFORMA_NO_ENCONTRADA", $"No existe la proforma '{command.ProformaId}' en el pedido.");
        var conformidad = await db.ProformasConformidad
            .FirstOrDefaultAsync(c => c.ProformaPedidoId == proforma.Id, cancellationToken)
            ?? throw new BusinessRuleException("FAC_PROFORMA_SIN_CONFORMIDAD",
                $"La proforma {proforma.Folio} no tiene la conformidad del cliente: regístrala antes de facturar desde ella.");
        if (conformidad.Invalidada)
            throw new BusinessRuleException("FAC_PROFORMA_CONFORMIDAD_INVALIDADA",
                $"La conformidad de la proforma {proforma.Folio} ya no vale: {conformidad.InvalidadaMotivo} Genera una proforma nueva y registra su conformidad.");

        var comportamiento = Enum.TryParse<ComportamientoFiscal>(pedido.ComportamientoFiscal, out var c) ? c : ComportamientoFiscal.MostradorInmediato;
        if (comportamiento is ComportamientoFiscal.ExportacionConCce or ComportamientoFiscal.VentaActivoFijo
            || pedido.SubtipoVentaDirecta == nameof(SubtipoVentaDirecta.Activo))
            throw new BusinessRuleException("FAC_PROFORMA_EMISION_NO_DISPONIBLE",
                "Este pedido requiere datos que la proforma no lleva (complemento de comercio exterior o autorización): emite con el formulario de facturación.");

        var lectura = await empresasFiscal.ObtenerAsync(empresaId, cancellationToken)
            ?? throw new EntityNotFoundException("EMPRESA_NO_ENCONTRADA", $"La empresa '{empresaId}' no existe o está inactiva.");
        var emisor = await EmisorSnapshot.ResolverAsync(empresasFiscal, empresaId, cancellationToken);

        // Lo que se facturaría hoy contra lo que el cliente aprobó. Se compara el contenido que ve el cliente (emisor, receptor,
        // conceptos, impuestos y totales) con la misma regla que la proforma; fechas y folios no se comparan, así que no hay
        // falsos positivos por campos que cambian solos.
        var armada = ProformaEmisionDesdePedido.Armar(pedido, lectura);
        var ahora = clock.UtcNow;
        var factura = FacturaVentaArmado.Construir(
            armada.Comando, empresaId, ProformaEmisionDesdePedido.FolioArmado, 0, null,
            FacturaVentaArmado.ReceptorDe(armada.Comando), emisor, ahora.Year, ahora.Month);
        var actual = ProformaCfdiMapper.Desde(factura, ahora,
            ProformaEmisionDesdePedido.SeriePrevista(pedido.Origen == nameof(OrigenPedido.Manual) && pedido.ClaseManual is not null, comportamiento));
        var diferencias = ProformaComparador.Comparar(ProformaJson.LeerContenido(proforma.ContenidoJson), actual);

        if (diferencias.Count > 0)
        {
            var motivo = MensajeDiferencias(diferencias);
            conformidad.Invalidar(motivo, ahora);
            await db.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("FAC_PROFORMA_PEDIDO_CAMBIO",
                $"El pedido cambió después de la conformidad de la proforma {proforma.Folio}: {motivo} " +
                "La conformidad queda invalidada: genera una proforma nueva y registra su conformidad para facturar.");
        }

        // Misma emisión de siempre, ligada a la versión aprobada; el folio PF se imprime en el campo «Proforma» si cabe.
        var comando = armada.Comando with
        {
            ProformaPedidoId = proforma.Id,
            Proforma = proforma.Folio.Length <= 40 ? proforma.Folio : null,
        };
        return await sender.Send(comando, cancellationToken);
    }

    private static string MensajeDiferencias(IReadOnlyList<DiferenciaProforma> diferencias)
    {
        var lista = diferencias.Take(DiferenciasEnMensaje)
            .Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.Campo} ({d.Proforma ?? "—"} → {d.Timbrado ?? "—"})"));
        var extra = diferencias.Count > DiferenciasEnMensaje
            ? string.Create(CultureInfo.InvariantCulture, $" y {diferencias.Count - DiferenciasEnMensaje} más")
            : string.Empty;
        return $"{string.Join("; ", lista)}{extra}.";
    }
}
