using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Devoluciones;

// ============================================================================
// T9.6 · CA9.11: pago de una devolución autorizada. En depósito erróneo el
// egreso es el contramovimiento ligado al depósito (misma cuenta); en NC es un
// egreso desde la cuenta que elige Tesorería. El tope se revalida bajo el mismo
// advisory lock que solicitar/editar. No hay integración bancaria: el operador
// confirma que verificó fondos en la banca.
// ============================================================================

public sealed record PagarDevolucionClienteCommand(
    Guid Id, DateOnly FechaValor, Guid? CuentaBancariaId, string? ReferenciaBancaria,
    bool FondosVerificados, int VersionEsperada) : IRequest<DevolucionClienteResponse>;

public sealed class PagarDevolucionClienteValidator : AbstractValidator<PagarDevolucionClienteCommand>
{
    public PagarDevolucionClienteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.FechaValor).NotEmpty();
        RuleFor(c => c.ReferenciaBancaria).MaximumLength(120);
        RuleFor(c => c.FondosVerificados).Equal(true)
            .WithMessage("Confirma que verificaste en la banca que la cuenta tiene fondos para esta devolución.");
    }
}

public sealed class PagarDevolucionClienteHandler
    : IRequestHandler<PagarDevolucionClienteCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPeriodoContablePort _periodoContable;
    private readonly IClock _clock;

    public PagarDevolucionClienteHandler(
        TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        IPeriodoContablePort periodoContable, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _periodoContable = periodoContable; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(PagarDevolucionClienteCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await PagarAsync(command, cancellationToken);
        DevolucionClienteResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, DevolucionLectura.LockKey,
            async ct => resultado = await PagarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<DevolucionClienteResponse> PagarAsync(PagarDevolucionClienteCommand command, CancellationToken ct)
    {
        var (empresaId, usuarioId) = DevolucionTransiciones.Contexto(_currentEmpresa, _currentUser, "registra el pago");
        var d = await DevolucionTransiciones.CargarAsync(_db, command.Id, command.VersionEsperada, ct);
        if (d.Estado != EstadoDevolucionCliente.Autorizada)
            throw new BusinessRuleException("DEV_NO_AUTORIZADA", "Solo una devolución autorizada puede pagarse.");

        // RN-8: candado real de Contabilidad.
        if (!await _periodoContable.EstaAbiertoAsync(command.FechaValor.Year, command.FechaValor.Month, ct))
            throw new BusinessRuleException("MOV_PERIODO_CERRADO",
                $"El período {command.FechaValor.Year}/{command.FechaValor.Month:00} está cerrado.");

        var ahora = _clock.UtcNow;
        var destino = new CuentaDestinoPago(d.Id, d.DestinoBanco!, d.DestinoClabe!, d.DestinoTitular!);
        MovimientoBancario egreso;

        if (d.Origen == OrigenDevolucionCliente.DepositoErroneo)
        {
            var deposito = await _db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == d.MovimientoOrigenId, ct)
                ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", $"No se encontró el movimiento '{d.MovimientoOrigenId}'.");
            if (command.CuentaBancariaId is Guid c && c != deposito.CuentaBancariaId)
                throw new BusinessRuleException("DEV_CUENTA_DISTINTA_AL_DEPOSITO",
                    "La devolución de un depósito sale de la misma cuenta donde entró.");
            var cuenta = await _db.CuentasBancarias.FirstOrDefaultAsync(x => x.Id == deposito.CuentaBancariaId, ct)
                ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", $"No se encontró la cuenta bancaria '{deposito.CuentaBancariaId}'.");
            if (!cuenta.Activa)
                throw new BusinessRuleException("MOV_CUENTA_INACTIVA", $"La cuenta bancaria '{cuenta.Banco}' está inactiva.");
            cuenta.ValidarFechaMovimiento(command.FechaValor);

            var pagado = await _db.DevolucionesCliente
                .Where(x => x.MovimientoOrigenId == d.MovimientoOrigenId && x.Estado == EstadoDevolucionCliente.Pagada)
                .SumAsync(x => (decimal?)x.Importe, ct) ?? 0m;
            if (pagado + d.Importe > deposito.Monto)
                throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_DEPOSITO",
                    $"La devolución ({d.Importe:N2}) excede lo disponible del depósito ({deposito.Monto - pagado:N2}).");

            egreso = deposito.CrearDevolucionDeposito(d.Importe, command.FechaValor, d.ClienteId, destino,
                command.ReferenciaBancaria, command.FondosVerificados, usuarioId, ahora, d.Motivo);
            // El depósito devuelto ya no puede confirmarse contra una propuesta de CxC (S7).
            deposito.ActualizarEstadoAplicacion(pagado + d.Importe);
        }
        else
        {
            if (command.CuentaBancariaId is not Guid cuentaId)
                throw new BusinessRuleException("DEV_CUENTA_REQUERIDA", "Selecciona la cuenta bancaria de la que sale el reembolso.");
            var cuenta = await _db.CuentasBancarias.FirstOrDefaultAsync(x => x.Id == cuentaId, ct)
                ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", $"No se encontró la cuenta bancaria '{cuentaId}'.");
            if (!string.Equals(cuenta.Moneda, d.Moneda, StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("DEV_CUENTA_OTRA_MONEDA",
                    $"La cuenta está en {cuenta.Moneda} y la devolución es en {d.Moneda}.");

            var pagado = await _db.DevolucionesCliente
                .Where(x => x.NotaCreditoId == d.NotaCreditoId && x.Estado == EstadoDevolucionCliente.Pagada)
                .SumAsync(x => (decimal?)x.Importe, ct) ?? 0m;
            if (pagado + d.Importe > d.ImporteMaximoOrigen)
                throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_NC", "La devolución excede el total de la nota de crédito.");

            egreso = MovimientoBancario.RegistrarDevolucionCliente(empresaId, cuenta, d.ClienteId, d.Importe,
                command.FechaValor, command.ReferenciaBancaria ?? $"DEV NC {d.NotaCreditoFolio}", destino,
                command.FondosVerificados, usuarioId, ahora, d.SucursalId);
        }

        _db.MovimientosBancarios.Add(egreso);
        d.MarcarPagada(egreso.Id, usuarioId, ahora);
        await _db.SaveChangesAsync(ct);

        var disponible = d.ImporteMaximoOrigen - await DevolucionLectura.ComprometidoAsync(_db, d, ct);
        return DevolucionLectura.ToResponse(d, null, disponible, verCuentaCompleta: false);
    }
}
