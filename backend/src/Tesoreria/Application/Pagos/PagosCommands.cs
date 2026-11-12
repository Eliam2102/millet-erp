using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pagos;

// ============================================================================
// TES-PR4: pago a proveedor end-to-end (§3.1). El pasivo llega YA DECIDIDO
// desde CxP (RN-1: solo se paga contra pasivo.autorizado-para-pago
// proyectado en la bandeja); el operador ejecuta la transferencia EN LA
// BANCA (fuera del sistema en MVP) y aquí registra el hecho bancario.
// Un pago cubre N pasivos, pero `aplicado.v1` se emite POR FACTURA (RN-4,
// contrato congelado). La reversa genera contramovimiento (RN-10).
// ============================================================================

public sealed record AplicacionPagoItem(Guid FacturaProveedorId, decimal Importe);

public sealed record AplicacionPagoResponse(
    Guid PagoId,
    Guid FacturaProveedorId,
    decimal Importe,
    bool Revertida, string? MotivoReversa = null);

public sealed record PagoProveedorResponse(
    Guid MovimientoId,
    Guid CuentaBancariaId,
    Guid ProveedorId,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    IReadOnlyList<AplicacionPagoResponse> Aplicaciones);

// --------------------------------------------------- Registrar pago

public sealed record RegistrarPagoProveedorCommand(
    Guid CuentaBancariaId,
    DateOnly FechaValor,
    IReadOnlyList<AplicacionPagoItem> Aplicaciones,
    string? ReferenciaBancaria = null,
    Guid? ConceptoId = null,
    // 86e3pdmee: cuenta del proveedor elegida (null = la predeterminada activa de la moneda).
    Guid? CuentaProveedorId = null,
    // T9.1-B: corrida de pagos que origina el pago (liga la aplicación con la corrida).
    Guid? CorridaId = null,
    // TES-09: TC que aplicó el banco (solo moneda distinta de MXN); null = el del catálogo K10.6 a la fecha valor.
    decimal? TipoCambioPago = null,
    string? MotivoTipoCambio = null) : IRequest<PagoProveedorResponse>;

public sealed class RegistrarPagoProveedorValidator : AbstractValidator<RegistrarPagoProveedorCommand>
{
    public RegistrarPagoProveedorValidator()
    {
        RuleFor(c => c.CuentaBancariaId).NotEmpty();
        RuleFor(c => c.FechaValor).NotEmpty();
        // §7.3 paso 3: el pago se registra con el folio que dio el banco.
        RuleFor(c => c.ReferenciaBancaria).NotEmpty().WithMessage("Captura la referencia (folio) que dio el banco.")
            .MaximumLength(120);
        RuleFor(c => c.Aplicaciones).NotEmpty()
            .WithMessage("El pago debe aplicar al menos a un pasivo.");
        RuleForEach(c => c.Aplicaciones).ChildRules(a =>
        {
            a.RuleFor(x => x.FacturaProveedorId).NotEmpty();
            a.RuleFor(x => x.Importe).GreaterThan(0);
        });
        RuleFor(c => c.Aplicaciones)
            .Must(apls => apls is null || apls.Select(a => a.FacturaProveedorId).Distinct().Count() == apls.Count)
            .WithMessage("No se puede aplicar dos veces a la misma factura en el mismo pago.");
        RuleFor(c => c.TipoCambioPago).Must(TipoCambioDelPago.EsValido)
            .WithMessage("El TC del pago debe ser mayor a cero y con 6 decimales como máximo.");
        // M-265b (Laura Cerón, 10-nov): el TC es editable, pero capturarlo a mano pide el motivo y queda en el movimiento.
        RuleFor(c => c.MotivoTipoCambio).NotEmpty().When(c => c.TipoCambioPago is not null)
            .WithMessage("Indica por qué capturas el tipo de cambio a mano.").MaximumLength(300);
    }
}

public sealed class RegistrarPagoProveedorHandler
    : IRequestHandler<RegistrarPagoProveedorCommand, PagoProveedorResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IProveedorBancoReadPort _proveedorBancoPort;
    private readonly IElegibleFacturaReadPort _elegible;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly IPasivoComercioExteriorReadPort? _ce;
    private readonly ITipoCambioPagoReadPort? _tiposCambio;

    public RegistrarPagoProveedorHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IProveedorBancoReadPort proveedorBancoPort,
        IElegibleFacturaReadPort elegible,
        IIntegrationEventPublisher publisher,
        IClock clock, IPasivoComercioExteriorReadPort? ce = null, ITipoCambioPagoReadPort? tiposCambio = null)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _currentUser = currentUser;
        _periodoContable = periodoContable;
        _proveedorBancoPort = proveedorBancoPort;
        _elegible = elegible;
        _publisher = publisher;
        _clock = clock; _ce = ce; _tiposCambio = tiposCambio;
    }

    public async Task<PagoProveedorResponse> Handle(
        RegistrarPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await RegistrarAsync(command, cancellationToken);
        PagoProveedorResponse? resultado = null;
        // Serializa pagos manuales: el saldo local y los pagos aún no proyectados se comprueban bajo el mismo lock.
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, 0x50335F5041474F,
            async ct => resultado = await RegistrarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<PagoProveedorResponse> RegistrarAsync(RegistrarPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el pago.");

        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaBancariaId}'.");

        // Brecha 12 (§7.3 paso 3): un folio del banco respalda un solo pago por cuenta. Va antes de las
        // reglas de saldo porque un reintento idéntico ya consumió lo elegible: recupera el resultado previo.
        if (await ReferenciaPagoUnica.BuscarAsync(_db, cuenta.Id, command.ReferenciaBancaria, cancellationToken) is { } previo)
        {
            var previas = await _db.AplicacionesPagoProveedor
                .Where(a => a.MovimientoId == previo.Id && !a.Revertida).ToListAsync(cancellationToken);
            var mismo = previas.Count > 0 && previas.Count == command.Aplicaciones.Count
                && previo.Monto == command.Aplicaciones.Sum(a => a.Importe)
                && command.Aplicaciones.All(c => previas.Any(a => a.FacturaProveedorId == c.FacturaProveedorId && a.ImporteAplicado == c.Importe))
                && !await ReferenciaPagoUnica.RevertidoAsync(_db, previo.Id, cancellationToken);
            if (!mismo) throw ReferenciaPagoUnica.Duplicada(previo);
            return new PagoProveedorResponse(
                previo.Id, previo.CuentaBancariaId, previas[0].ProveedorId, previo.Monto, previo.Moneda, previo.FechaValor,
                previo.ReferenciaBancaria,
                previas.Select(a => new AplicacionPagoResponse(a.Id, a.FacturaProveedorId, a.ImporteAplicado, a.Revertida)).ToList());
        }

        // RN-8: candado real de Contabilidad.
        if (command.ConceptoId is Guid conceptoId && !await _db.ConceptosMovimiento.AnyAsync(c => c.Id == conceptoId && c.Activo, cancellationToken))
            throw new BusinessRuleException("MOV_CONCEPTO_INVALIDO", "Selecciona un concepto activo.");

        var abierto = await _periodoContable.EstaAbiertoAsync(
            command.FechaValor.Year, command.FechaValor.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        // RN-1: todos los pasivos deben estar en la bandeja (proyección del
        // evento autorizado de CxP).
        var facturaIds = command.Aplicaciones.Select(a => a.FacturaProveedorId).ToList();
        var pasivos = await _db.PasivosPendientesPago
            .Where(p => facturaIds.Contains(p.FacturaProveedorId))
            .ToDictionaryAsync(p => p.FacturaProveedorId, cancellationToken);

        var faltantes = facturaIds.Where(id => !pasivos.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
            throw new BusinessRuleException("PAGO_PASIVO_NO_AUTORIZADO",
                $"Pasivo(s) no presentes en la bandeja de autorizados: {string.Join(", ", faltantes)} (RN-1).");
        // I13.3: un pasivo de comercio exterior necesita sus dos autorizaciones también fuera de la corrida.
        await Pasivos.PagosComercioExterior.ExigirAutorizadosAsync(_db, facturaIds, "PAGO_PASIVO_CE_SIN_AUTORIZAR", cancellationToken);
        foreach (var anticipo in pasivos.Values.Where(p => p.OrigenTipo == "AnticipoAgente"))
        {
            if (_ce is null) throw new BusinessRuleException("CE_VALIDACION_NO_DISPONIBLE", "No se pudo validar la solicitud del anticipo.");
            await _ce.ExigirAnticipoVigenteAsync(anticipo.FacturaProveedorId, cancellationToken);
        }

        // GI-PR3 (doc 12): los pasivos internos SÍ se pagan por este flujo,
        // pero emiten su propio evento (préstamo de viáticos → vuelta a
        // CxP) o ninguno (reposición de caja) — nunca
        // pago-factura-proveedor.aplicado.v1, que apuntaría a una factura
        // inexistente. Un pago no mezcla internos con facturas: son
        // beneficiarios de naturaleza distinta.
        var hayInternos = pasivos.Values.Any(p => p.EsInterno);
        if (hayInternos && pasivos.Values.Any(p => !p.EsInterno))
            throw new BusinessRuleException("PAGO_MEZCLA_INTERNOS",
                "Un pago no puede mezclar pasivos internos (reposición/viáticos) con facturas de proveedor.");

        // Una transferencia = un beneficiario: todos los pasivos del mismo
        // proveedor.
        var proveedores = pasivos.Values.Select(p => p.ProveedorId).Distinct().ToList();
        if (proveedores.Count > 1)
            throw new BusinessRuleException("PAGO_MULTIPROVEEDOR",
                "Un pago cubre pasivos de un solo proveedor; registra un pago por proveedor.");
        var proveedorId = proveedores[0];

        // RN-3: la cuenta de egreso coincide en moneda con los pasivos
        // (cross-moneda bloqueado en MVP; T-G6). Va antes que la cuenta del
        // proveedor para no pedir una cuenta en otra moneda que no hace falta (F-TES-05).
        var monedasDistintas = pasivos.Values
            .Select(p => p.Moneda).Distinct()
            .Where(m => m != cuenta.Moneda)
            .ToList();
        if (monedasDistintas.Count > 0)
            throw new BusinessRuleException("PAGO_CROSS_MONEDA",
                $"La cuenta es {cuenta.Moneda} y hay pasivos en {string.Join(", ", monedasDistintas)} (RN-3; cross-moneda fuera del MVP).");

        // F1-ADM-05 G1.1 / 86e3pdmee: proveedor Activo, fuera de revisión y con cuenta activa en la moneda
        // del egreso; si el puerto no devuelve datos del proveedor el pago se bloquea.
        CuentaDestinoPago? destino = null;
        if (!hayInternos)
        {
            var infoProveedor = await _proveedorBancoPort.ObtenerAsync(proveedorId, cancellationToken);
            var elegida = ReglasDestinoProveedor.Resolver(infoProveedor, cuenta.Moneda, command.CuentaProveedorId);
            // Misma detención por documentación que la corrida (sin 32-D vigente no se paga por ninguna vía).
            PasivosPagables.ExigirOpinionCumplimiento(infoProveedor!, "PAGO_PASIVO_DETENIDO_DOCUMENTACION");
            destino = new CuentaDestinoPago(elegida.Id, elegida.Banco, elegida.Numero, elegida.Titular);
        }

        foreach (var grupo in command.Aplicaciones.GroupBy(a => a.FacturaProveedorId))
        {
            if (pasivos[grupo.Key].EsInterno) continue;
            if (pasivos[grupo.Key].PagoBloqueado)
                throw new BusinessRuleException("PASIVO_NO_AUTORIZADO_CXP", "CxP retiró la autorización del pasivo. Debe autorizarse de nuevo antes de pagar.");
            // El saldo va antes que el elegible: si el importe excede la factura, el aviso de «retenido» no aplica.
            pasivos[grupo.Key].ExigirSaldo(grupo.Sum(a => a.Importe));
            var limite = await _elegible.ObtenerLimiteAcumuladoAsync(grupo.Key, cancellationToken);
            if (limite <= 0)
                throw new BusinessRuleException("PASIVO_NO_PAGABLE_CXP", "CxP no autoriza un importe pagable para esta factura. Revisa su estado y saldo en CxP.");
            var pagado = await _db.AplicacionesPagoProveedor.Where(a => a.FacturaProveedorId == grupo.Key && !a.Revertida)
                .SumAsync(a => a.ImporteAplicado, cancellationToken);
            var disponible = Math.Max(0, limite - pagado);
            if (grupo.Sum(a => a.Importe) > disponible)
                throw new BusinessRuleException("PAGO_EXCEDE_ELEGIBLE",
                    $"Solo {disponible:N2} es elegible para pago de la factura {grupo.Key}. El resto está retenido hasta recibir la mercancía; registra como máximo ese importe.");
        }
        var ahora = _clock.UtcNow;
        var monto = command.Aplicaciones.Sum(a => a.Importe);

        var movimiento = MovimientoBancario.RegistrarPagoProveedor(
            empresaId: empresaId,
            cuenta: cuenta,
            proveedorId: proveedorId,
            monto: monto,
            fechaValor: command.FechaValor,
            referenciaBancaria: command.ReferenciaBancaria,
            conceptoId: command.ConceptoId,
            creadoPor: usuarioId,
            ahora: ahora,
            destino: destino);
        var tipoCambioPago = await TipoCambioDelPago.FijarAsync(movimiento, _tiposCambio, command.TipoCambioPago, cancellationToken, command.MotivoTipoCambio);
        _db.MovimientosBancarios.Add(movimiento);

        var aplicaciones = new List<AplicacionPagoProveedor>(command.Aplicaciones.Count);
        foreach (var item in command.Aplicaciones)
        {
            var pasivo = pasivos[item.FacturaProveedorId];
            // Validación local; la autoridad final es CxP (PAGO_EXCEDE_SALDO).
            pasivo.AplicarPago(item.Importe);

            var aplicacion = new AplicacionPagoProveedor(
                movimientoId: movimiento.Id,
                facturaProveedorId: item.FacturaProveedorId,
                proveedorId: proveedorId,
                importeAplicado: item.Importe,
                creadoEn: ahora,
                corridaId: command.CorridaId);
            aplicaciones.Add(aplicacion);
            _db.AplicacionesPagoProveedor.Add(aplicacion);

            if (pasivo.EsInterno)
            {
                // GI-PR3: préstamo de viáticos → vuelta hacia CxP para
                // transicionar la solicitud a Anticipada. La reposición de
                // caja chica se liquida local (CxP no consume su pago hoy).
                if (pasivo.OrigenTipo == "PrestamoViaticos")
                {
                    await _publisher.PublishAsync(new PagoPrestamoViaticosAplicadoIntegrationEvent(
                        EmpresaId: empresaId,
                        OcurridoEn: ahora,
                        SolicitudViaticosId: pasivo.OrigenId,
                        PagoId: aplicacion.Id,
                        MontoPagado: item.Importe,
                        Moneda: movimiento.Moneda,
                        FechaPago: command.FechaValor,
                        ReferenciaBancaria: movimiento.ReferenciaBancaria), cancellationToken);
                }
            }
            else
            {
                // RN-4: aplicado.v1 POR FACTURA, al outbox en la misma TX
                // (ADR-0009 — el interceptor drena el buffer en SaveChanges).
                await _publisher.PublishAsync(new PagoFacturaProveedorAplicadoIntegrationEvent(
                    EmpresaId: empresaId,
                    OcurridoEn: ahora,
                    FacturaProveedorId: item.FacturaProveedorId,
                    PagoId: aplicacion.Id,
                    Monto: item.Importe,
                    Moneda: movimiento.Moneda,
                    FechaPago: command.FechaValor,
                    MetodoPago: pasivo.MetodoPago,
                    ReferenciaBancaria: movimiento.ReferenciaBancaria,
                    CuentaBancariaId: movimiento.CuentaBancariaId,
                    TipoCambio: pasivo.TipoCambio) { EsAnticipoAgente = pasivo.OrigenTipo == "AnticipoAgente",
                        SucursalId = pasivo.SucursalAnticipoCeId, ProveedorId = pasivo.ProveedorId,
                        TipoCambioPago = tipoCambioPago }, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new PagoProveedorResponse(
            movimiento.Id, movimiento.CuentaBancariaId, proveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria,
            aplicaciones.Select(a => new AplicacionPagoResponse(
                a.Id, a.FacturaProveedorId, a.ImporteAplicado, a.Revertida)).ToList());
    }
}

// --------------------------------------------------- Revertir pago

public sealed record RevertirPagoProveedorCommand(Guid PagoId, string Motivo)
    : IRequest<PagoProveedorResponse>;

public sealed class RevertirPagoProveedorValidator : AbstractValidator<RevertirPagoProveedorCommand>
{
    public RevertirPagoProveedorValidator()
    {
        RuleFor(c => c.PagoId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RevertirPagoProveedorHandler
    : IRequestHandler<RevertirPagoProveedorCommand, PagoProveedorResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly Millet.Tesoreria.Domain.Ports.IPasivoComercioExteriorReadPort? _ce;

    public RevertirPagoProveedorHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable,
        IIntegrationEventPublisher publisher,
        IClock clock, Millet.Tesoreria.Domain.Ports.IPasivoComercioExteriorReadPort? ce = null)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _publisher = publisher; _clock = clock; _ce = ce;
    }

    public async Task<PagoProveedorResponse> Handle(RevertirPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await RevertirAsync(command, cancellationToken);
        PagoProveedorResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, 0x50335F5041474F,
            async ct => resultado = await RevertirAsync(command, ct), cancellationToken);
        return resultado!;
    }
    private async Task<PagoProveedorResponse> RevertirAsync(RevertirPagoProveedorCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que revierte el pago.");

        var aplicacion = await _db.AplicacionesPagoProveedor
            .FirstOrDefaultAsync(a => a.Id == command.PagoId, cancellationToken)
            ?? throw new EntityNotFoundException("PAGO_NO_ENCONTRADO",
                $"No se encontró el pago '{command.PagoId}'.");

        var movimiento = await _db.MovimientosBancarios
            .FirstOrDefaultAsync(m => m.Id == aplicacion.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento del pago '{command.PagoId}'.");

        var ahora = _clock.UtcNow;
        var fechaReversa = DateOnly.FromDateTime(ahora.UtcDateTime);

        // RN-8 sobre la fecha del contramovimiento.
        var abierto = await _periodoContable.EstaAbiertoAsync(
            fechaReversa.Year, fechaReversa.Month, cancellationToken);
        if (!abierto)
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {fechaReversa.Year}/{fechaReversa.Month:00} está cerrado.");

        if (movimiento.MotivoNoAplicado is not null)
            throw new BusinessRuleException("PAGO_CUENTA_USAR_DESLIGAR", "Este es un pago a cuenta. Usa Desligar para corregir su aplicación sin registrar un ingreso bancario.");

        var pasivoCe = await _db.PasivosPendientesPago.FirstOrDefaultAsync(p => p.FacturaProveedorId == aplicacion.FacturaProveedorId, cancellationToken);
        if (pasivoCe?.OrigenTipo == "AnticipoAgente")
        {
            if (_ce is null) throw new BusinessRuleException("CE_REVERSA_NO_DISPONIBLE", "No se pudo validar la comprobación del anticipo.");
            await _ce.ExigirSinComprobacionAsync(aplicacion.FacturaProveedorId, cancellationToken);
        }
        // RN-10: marca + contramovimiento; nada se borra.
        aplicacion.Revertir(command.Motivo);
        var contramovimiento = movimiento.CrearContramovimiento(
            importe: aplicacion.ImporteAplicado,
            fechaValor: fechaReversa,
            creadoPor: usuarioId,
            ahora: ahora, motivo: command.Motivo);
        _db.MovimientosBancarios.Add(contramovimiento);

        // El pasivo regresa a la bandeja con el saldo restaurado (si CxP
        // re-emite el autorizado, la re-proyección lo corrige).
        var pasivo = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == aplicacion.FacturaProveedorId, cancellationToken);

        // GI-PR3: la reversa de un pago interno no tiene contraparte en
        // CxP (el préstamo ya transicionó la solicitud a Anticipada y no
        // existe la transición inversa). Se bloquea hasta modelarla.
        if (pasivo?.EsInterno == true)
            throw new BusinessRuleException("REVERSA_PASIVO_INTERNO",
                "La reversa de pagos de pasivos internos (reposición/viáticos) no está soportada; corrígelo en el módulo de origen.");

        pasivo?.RevertirPago(aplicacion.ImporteAplicado);

        // T9.1-B: el pago venía de una corrida → su línea vuelve a quedar pendiente (misma transacción).
        if (aplicacion.CorridaId is Guid corridaId)
            (await _db.CorridasPago.Include(c => c.Lineas).FirstOrDefaultAsync(c => c.Id == corridaId, cancellationToken))
                ?.RevertirEjecucion(aplicacion.FacturaProveedorId);

        await _publisher.PublishAsync(new PagoFacturaProveedorRevertidoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: aplicacion.FacturaProveedorId,
            PagoOriginalId: aplicacion.Id,
            MontoRevertido: aplicacion.ImporteAplicado,
            Moneda: movimiento.Moneda,
            FechaReversa: fechaReversa,
            Motivo: command.Motivo.Trim()), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new PagoProveedorResponse(
            movimiento.Id, movimiento.CuentaBancariaId, aplicacion.ProveedorId,
            movimiento.Monto, movimiento.Moneda, movimiento.FechaValor,
            movimiento.ReferenciaBancaria,
            [new AplicacionPagoResponse(aplicacion.Id, aplicacion.FacturaProveedorId, aplicacion.ImporteAplicado, aplicacion.Revertida, aplicacion.MotivoReversa)]);
    }
}

// --------------------------------------------------- Solicitar cancelación de pasivo

public sealed record SolicitarCancelacionPasivoCommand(Guid FacturaProveedorId, string Motivo)
    : IRequest;

public sealed class SolicitarCancelacionPasivoValidator : AbstractValidator<SolicitarCancelacionPasivoCommand>
{
    public SolicitarCancelacionPasivoValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class SolicitarCancelacionPasivoHandler : IRequestHandler<SolicitarCancelacionPasivoCommand>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly Millet.Tesoreria.Domain.Ports.IAnticipoCeCancelacionPort? _ce;

    public SolicitarCancelacionPasivoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IIntegrationEventPublisher publisher,
        IClock clock, Millet.Tesoreria.Domain.Ports.IAnticipoCeCancelacionPort? ce = null)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _publisher = publisher; _clock = clock; _ce = ce;
    }

    public async Task Handle(SolicitarCancelacionPasivoCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) { await CancelarAsync(command, cancellationToken); return; }
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, 0x50335F5041474F,
            ct => CancelarAsync(command, ct), cancellationToken);
    }
    private async Task CancelarAsync(SolicitarCancelacionPasivoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario solicitante.");

        var pasivo = await _db.PasivosPendientesPago
            .FirstOrDefaultAsync(p => p.FacturaProveedorId == command.FacturaProveedorId, cancellationToken);
        if (pasivo is null)
            throw new EntityNotFoundException("PASIVO_NO_ENCONTRADO",
                $"El pasivo '{command.FacturaProveedorId}' no está en la bandeja.");

        if (pasivo.OrigenTipo == "AnticipoAgente")
        {
            if (pasivo.MontoTotal != pasivo.SaldoPendiente || await _db.AplicacionesPagoProveedor.AnyAsync(a => a.FacturaProveedorId == pasivo.FacturaProveedorId && !a.Revertida, cancellationToken)) throw new BusinessRuleException("CE_ANTICIPO_PAGADO", "Revierta el pago antes de cancelar el anticipo.");
            if (_ce is null) throw new BusinessRuleException("CE_CANCELACION_NO_DISPONIBLE", "No se pudo validar el anticipo.");
            await _ce.CancelarAnticipoAsync(command.FacturaProveedorId, command.Motivo, cancellationToken);
            pasivo.RetirarDePago(command.Motivo, _clock.UtcNow);
            var firma = await _db.AutorizacionesPagoComercioExterior.SingleOrDefaultAsync(a => a.FacturaProveedorId == command.FacturaProveedorId, cancellationToken);
            if (firma is not null) _db.AutorizacionesPagoComercioExterior.Remove(firma);
        }
        // GI-PR2: la cancelación de un pasivo interno no viaja a CxP como
        // solicitud de factura — se resuelve en su módulo de origen.
        if (pasivo.EsInterno)
            throw new BusinessRuleException("PASIVO_INTERNO_NO_CANCELABLE",
                "Los pasivos internos no se cancelan desde Tesorería; se resuelven en CxP (comprobación/solicitud de origen).");

        // Sin mutación local: CxP decide (EnviarARevision); su resolución
        // regresa por el flujo normal de eventos.
        await _publisher.PublishAsync(new CancelacionPasivoSolicitadaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: _clock.UtcNow,
            FacturaProveedorId: command.FacturaProveedorId,
            UsuarioSolicitanteId: usuarioId,
            Motivo: command.Motivo.Trim()), cancellationToken);

        // SaveChanges drena el buffer del publisher a la outbox (ADR-0009)
        // aunque no haya cambios de entidades.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
