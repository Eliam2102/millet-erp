using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Repp;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Repp;

// ============================================================================
// TES-PR8 (§3.6.b, TES-4 sabor b): registro del REPP que el proveedor
// emite a Millet por pagos PPD. UUID único + fecha + XML a Blob
// (ADR-0024) → publica tesoreria.repp-proveedor.recibido.v1 (contrato
// congelado desde TES-PR4) → CxP libera el motivo de revisión FALTA_REPP.
// Validación fiscal del UUID post-MVP [T-G10,
// PLATFORM-TODO(<ValidacionReppRecibido>)].
// ============================================================================

public sealed record RegistrarReppRecibidoCommand(
    Guid FacturaProveedorId,
    Guid UuidComplemento,
    DateOnly FechaComplemento,
    // XML original obligatorio para validar la cobertura fiscal (P4).
    string? XmlBase64 = null, IReadOnlyList<ReppPagoDetalle>? Pagos = null) : IRequest<ReppRecibidoResponse>;

public sealed record ReppRecibidoResponse(
    Guid Id,
    Guid FacturaProveedorId,
    Guid UuidComplemento,
    DateOnly FechaComplemento,
    string? XmlBlobRef,
    DateTimeOffset RegistradoEn);

public sealed class RegistrarReppRecibidoValidator : AbstractValidator<RegistrarReppRecibidoCommand>
{
    public RegistrarReppRecibidoValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.UuidComplemento).NotEmpty();
        RuleFor(c => c.FechaComplemento).NotEmpty();
        RuleFor(c => c.Pagos).NotEmpty().WithMessage("Selecciona al menos un pago y su importe para el REPP.");
        RuleFor(c => c.XmlBase64).NotEmpty().WithMessage("Adjunta el XML del complemento de pago del proveedor.");
        RuleFor(c => c.XmlBase64)
            .Must(x => x is null || EsBase64(x))
            .WithMessage("El XML debe venir codificado en base64.");
    }

    private static bool EsBase64(string valor)
    {
        var buffer = new Span<byte>(new byte[(valor.Length * 3 / 4) + 4]);
        return Convert.TryFromBase64String(valor, buffer, out _);
    }
}

public sealed class RegistrarReppRecibidoHandler
    : IRequestHandler<RegistrarReppRecibidoCommand, ReppRecibidoResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IReppXmlBlobStorage _blob;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly Domain.Ports.DatosMaestros.IProveedorBancoReadPort _proveedores;

    public RegistrarReppRecibidoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IReppXmlBlobStorage blob,
        IIntegrationEventPublisher publisher,
        IClock clock, Domain.Ports.DatosMaestros.IProveedorBancoReadPort proveedores)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _blob = blob; _publisher = publisher; _clock = clock; _proveedores = proveedores;
    }

    public async Task<ReppRecibidoResponse> Handle(RegistrarReppRecibidoCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await RegistrarAsync(command, cancellationToken);
        ReppRecibidoResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, 0x50335F5041474F,
            async ct => resultado = await RegistrarAsync(command, ct), cancellationToken);
        return resultado!;
    }
    private async Task<ReppRecibidoResponse> RegistrarAsync(RegistrarReppRecibidoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el REPP.");

        // El pasivo debe existir en la proyección (todo pasivo autorizado
        // pasó por aquí; la fila persiste con saldo 0 tras el pago).
        var pasivo = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == command.FacturaProveedorId, cancellationToken);
        if (pasivo is null)
            throw new EntityNotFoundException("PASIVO_NO_ENCONTRADO",
                $"El pasivo '{command.FacturaProveedorId}' no está en la proyección de Tesorería.");

        // Pre-check amable del UUID único; el índice ux_repp_recibido_uuid
        // es la red final ante concurrencia.
        var registroAnterior = await _db.ReppsProveedorRecibidos.FirstOrDefaultAsync(r => r.UuidComplemento == command.UuidComplemento, cancellationToken);
        // Un registro anterior a P4 sin desglose se completa una sola vez, validando el XML original.
        if (registroAnterior is not null && (registroAnterior.FacturaProveedorId != command.FacturaProveedorId ||
            registroAnterior.FechaComplemento != command.FechaComplemento || await _db.ReppPagosProveedor.AnyAsync(p => p.ReppId == registroAnterior.Id, cancellationToken)))
            throw new BusinessRuleException("REPP_UUID_DUPLICADO",
                $"El complemento con UUID '{command.UuidComplemento}' ya está registrado.");

        if (pasivo.MetodoPago != "PPD" || pasivo.UuidCfdi is not Guid uuidFactura)
            throw new BusinessRuleException("REPP_FACTURA_NO_PPD", "El complemento requiere una factura PPD con UUID fiscal.");
        if (command.Pagos is not { Count: > 0 } || command.Pagos.Any(p => p.PagoId == Guid.Empty || p.Importe <= 0) || command.Pagos.Select(p => p.PagoId).Distinct().Count() != command.Pagos.Count)
            throw new BusinessRuleException("REPP_PAGOS_INVALIDOS", "Selecciona pagos distintos y sus importes positivos.");
        var ids = command.Pagos.Select(p => p.PagoId).ToArray();
        var aplicaciones = await _db.AplicacionesPagoProveedor.Where(a => ids.Contains(a.Id) && !a.Revertida && a.FacturaProveedorId == command.FacturaProveedorId).ToListAsync(cancellationToken);
        if (aplicaciones.Count != ids.Length) throw new BusinessRuleException("REPP_PAGO_PREVIO_REQUERIDO", "Cada pago debe existir, estar aplicado a esta factura y no estar revertido.");
        var movimientos = await _db.MovimientosBancarios.Where(m => aplicaciones.Select(a => a.MovimientoId).Contains(m.Id)).ToListAsync(cancellationToken);
        var proveedor = await _proveedores.ObtenerAsync(pasivo.ProveedorId, cancellationToken);
        if (string.IsNullOrWhiteSpace(proveedor?.Rfc)) throw new BusinessRuleException("REPP_RFC_PROVEEDOR_REQUERIDO", "Completa el RFC del proveedor antes de validar el complemento.");
        if (string.IsNullOrWhiteSpace(command.XmlBase64)) throw new BusinessRuleException("REPP_XML_REQUERIDO", "Adjunta el XML del complemento emitido por el proveedor.");
        byte[] xmlBytes;
        try { xmlBytes = Convert.FromBase64String(command.XmlBase64); }
        catch (FormatException) { throw new BusinessRuleException("REPP_XML_INVALIDO", "El XML debe venir codificado en base64."); }
        var fiscales = ReppXmlValidator.Validar(xmlBytes, command.UuidComplemento, command.FechaComplemento, proveedor.Rfc, uuidFactura, pasivo.Moneda);
        var esperados = command.Pagos.GroupBy(p => movimientos.Single(m => m.Id == aplicaciones.Single(a => a.Id == p.PagoId).MovimientoId).FechaValor).ToDictionary(g => g.Key, g => g.Sum(p => p.Importe));
        var emitidos = fiscales.GroupBy(p => p.Fecha).ToDictionary(g => g.Key, g => g.Sum(p => p.Importe));
        if (esperados.Count != emitidos.Count || esperados.Any(p => !emitidos.TryGetValue(p.Key, out var importe) || Math.Abs(importe - p.Value) > 0.01m))
            throw new BusinessRuleException("REPP_IMPORTE_NO_COINCIDE", "Las fechas e importes del XML no coinciden con los pagos seleccionados.");
        foreach (var pago in command.Pagos)
        {
            var aplicado = aplicaciones.Single(a => a.Id == pago.PagoId);
            var cubierto = await _db.ReppPagosProveedor.Where(r => r.PagoId == pago.PagoId).SumAsync(r => r.Importe, cancellationToken);
            if (pago.Importe + cubierto > aplicado.ImporteAplicado || movimientos.Single(m => m.Id == aplicado.MovimientoId).Moneda != pasivo.Moneda)
                throw new BusinessRuleException("REPP_IMPORTE_EXCEDE_PAGO", "El complemento excede el importe pendiente del pago o tiene otra moneda.");
        }
        var ahora = _clock.UtcNow;

        string? xmlBlobRef = null;
        if (!string.IsNullOrWhiteSpace(command.XmlBase64))
        {
            using var xml = new MemoryStream(Convert.FromBase64String(command.XmlBase64), writable: false);
            xmlBlobRef = await _blob.GuardarXmlAsync(
                command.UuidComplemento.ToString("D"), command.FechaComplemento, xml, cancellationToken);
        }

        var repp = registroAnterior ?? ReppProveedorRecibido.Registrar(
            empresaId: empresaId,
            facturaProveedorId: command.FacturaProveedorId,
            uuidComplemento: command.UuidComplemento,
            fechaComplemento: command.FechaComplemento,
            xmlBlobRef: xmlBlobRef,
            registradoPor: usuarioId,
            ahora: ahora);
        if (registroAnterior is null) _db.ReppsProveedorRecibidos.Add(repp);
        else if (xmlBlobRef is not null) repp.CompletarXmlAnterior(xmlBlobRef);
        foreach (var pago in command.Pagos) _db.ReppPagosProveedor.Add(new(empresaId, repp.Id, pago.PagoId, pago.Importe));

        // Contrato congelado (TES-PR4): UuidComplementoPago viaja como
        // string y FechaComplemento como DateTimeOffset — así los espera
        // CxP para MarcarReppRecibido() (libera FALTA_REPP).
        await _publisher.PublishAsync(new ReppProveedorRecibidoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: command.FacturaProveedorId,
            UuidComplementoPago: command.UuidComplemento.ToString("D").ToUpperInvariant(),
            FechaComplemento: new DateTimeOffset(
                command.FechaComplemento.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), Pagos: command.Pagos), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new ReppRecibidoResponse(
            repp.Id, repp.FacturaProveedorId, repp.UuidComplemento,
            repp.FechaComplemento, repp.XmlBlobRef, repp.RegistradoEn);
    }
}
