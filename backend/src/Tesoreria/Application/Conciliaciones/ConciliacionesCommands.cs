using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Conciliaciones.Actas;
using Millet.Tesoreria.Domain.Conciliaciones.Emparejamiento;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Conciliaciones;

// ============================================================================
// 1. CrearConciliacionCommand
// ============================================================================
public sealed record CrearConciliacionCommand(
    Guid CuentaBancariaId,
    int PeriodoAnio,
    int PeriodoMes,
    decimal SaldoExtracto = 0m) : IRequest<ConciliacionResumenResponse>;

public sealed class CrearConciliacionValidator : AbstractValidator<CrearConciliacionCommand>
{
    public CrearConciliacionValidator()
    {
        RuleFor(x => x.CuentaBancariaId).NotEmpty();
        RuleFor(x => x.PeriodoAnio).InclusiveBetween(2000, 2100);
        RuleFor(x => x.PeriodoMes).InclusiveBetween(1, 12);
    }
}

public sealed class CrearConciliacionHandler(
    TesoreriaDbContext db,
    ICurrentEmpresaContext empresaContext)
    : IRequestHandler<CrearConciliacionCommand, ConciliacionResumenResponse>
{
    public async Task<ConciliacionResumenResponse> Handle(CrearConciliacionCommand command, CancellationToken cancellationToken)
    {
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == command.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        var existente = await db.Conciliaciones
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.CuentaBancariaId == command.CuentaBancariaId
                                      && c.PeriodoAnio == command.PeriodoAnio
                                      && c.PeriodoMes == command.PeriodoMes, cancellationToken);

        if (existente is not null)
        {
            return ConciliacionMapper.ToResumen(existente, cuenta);
        }

        Guid empresaId = empresaContext.Current ?? cuenta.EmpresaId;
        var conciliacion = Conciliacion.Crear(
            empresaId,
            cuenta.Id,
            command.PeriodoAnio,
            command.PeriodoMes,
            command.SaldoExtracto);

        // Calcular saldo libros a fin de mes
        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, command.PeriodoAnio, command.PeriodoMes, cancellationToken);

        conciliacion.ActualizarSaldoLibros(saldoLibros);

        db.Conciliaciones.Add(conciliacion);
        await db.SaveChangesAsync(cancellationToken);

        return ConciliacionMapper.ToResumen(conciliacion, cuenta);
    }
}

// ============================================================================
// 2. CargarExtractoCommand (Importación BBVA y deduplicación CA9.5)
// ============================================================================
public sealed record CargarExtractoCommand(
    Guid ConciliacionId,
    string ContenidoArchivo,
    string Formato = "BBVA",
    decimal? SaldoExtracto = null,
    bool AutoEmparejar = true) : IRequest<ConciliacionResumenResponse>;

public sealed class CargarExtractoValidator : AbstractValidator<CargarExtractoCommand>
{
    public CargarExtractoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ContenidoArchivo).NotEmpty();
    }
}

public sealed class CargarExtractoHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock,
    ExtractoParserFactory parserFactory)
    : IRequestHandler<CargarExtractoCommand, ConciliacionResumenResponse>
{
    public async Task<ConciliacionResumenResponse> Handle(CargarExtractoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación bancaria.");

        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        var parser = parserFactory.ObtenerParser(command.Formato);
        var parseResult = parser.Parse(command.ContenidoArchivo);

        int numLineaActual = conciliacion.Lineas.Count > 0 ? conciliacion.Lineas.Max(l => l.NumeroLinea) + 1 : 1;

        var nuevasLineas = parseResult.Lineas.Select(p => ExtractoLinea.Crear(
            conciliacion.Id,
            numLineaActual++,
            p.Fecha,
            p.Descripcion,
            p.Referencia,
            p.Cargo,
            p.Abono,
            ExtractoFuente.Archivo,
            p.LineaHash)).ToList();

        decimal? saldoExt = command.SaldoExtracto ?? parseResult.SaldoFinal;
        conciliacion.CargarLineas(nuevasLineas, saldoExt);

        if (command.AutoEmparejar)
        {
            var movimientos = await db.MovimientosBancarios
                .Where(m => m.CuentaBancariaId == conciliacion.CuentaBancariaId
                            && m.EstadoConciliacion == EstadoConciliacionMovimiento.NoConciliado)
                .ToListAsync(cancellationToken);

            MotorEmparejamiento.Ejecutar(
                conciliacion.Lineas.ToList(),
                movimientos,
                new EmparejamientoOptions(),
                userContext.UserId ?? Guid.Empty,
                clock.UtcNow);
        }

        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, conciliacion.PeriodoAnio, conciliacion.PeriodoMes, cancellationToken);
        conciliacion.ActualizarSaldoLibros(saldoLibros);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToResumen(conciliacion, cuenta);
    }
}

// ============================================================================
// 3. EmparejarAutomaticoCommand
// ============================================================================
public sealed record EmparejarAutomaticoCommand(
    Guid ConciliacionId,
    decimal? ToleranciaImporte = null,
    int? VentanaDias = null) : IRequest<ResultadoEmparejamiento>;

public sealed class EmparejarAutomaticoValidator : AbstractValidator<EmparejarAutomaticoCommand>
{
    public EmparejarAutomaticoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
    }
}

public sealed class EmparejarAutomaticoHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<EmparejarAutomaticoCommand, ResultadoEmparejamiento>
{
    public async Task<ResultadoEmparejamiento> Handle(EmparejarAutomaticoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado == EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede emparejar en una conciliación cerrada.");

        var movimientos = await db.MovimientosBancarios
            .Where(m => m.CuentaBancariaId == conciliacion.CuentaBancariaId
                        && m.EstadoConciliacion == EstadoConciliacionMovimiento.NoConciliado)
            .ToListAsync(cancellationToken);

        var options = new EmparejamientoOptions(
            ToleranciaImporte: command.ToleranciaImporte ?? 1.00m,
            VentanaDias: command.VentanaDias ?? 3);

        var resultado = MotorEmparejamiento.Ejecutar(
            conciliacion.Lineas.ToList(),
            movimientos,
            options,
            userContext.UserId ?? Guid.Empty,
            clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return resultado;
    }
}

// ============================================================================
// 4. EmparejarManualCommand (1 a 1 y varios a 1)
// ============================================================================
public sealed record EmparejarManualCommand(
    Guid ConciliacionId,
    Guid ExtractoLineaId,
    IReadOnlyList<Guid> MovimientoIds) : IRequest<ExtractoLineaResponse>;

public sealed class EmparejarManualValidator : AbstractValidator<EmparejarManualCommand>
{
    public EmparejarManualValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.MovimientoIds).NotEmpty().WithMessage("Selecciona al menos un movimiento para emparejar.");
    }
}

public sealed class EmparejarManualHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<EmparejarManualCommand, ExtractoLineaResponse>
{
    public async Task<ExtractoLineaResponse> Handle(EmparejarManualCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado == EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede modificar una conciliación cerrada.");

        var linea = conciliacion.Lineas.FirstOrDefault(l => l.Id == command.ExtractoLineaId)
            ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la línea de extracto.");

        var movimientos = await db.MovimientosBancarios
            .Where(m => command.MovimientoIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        if (movimientos.Count != command.MovimientoIds.Count)
            throw new BusinessRuleException("MOVIMIENTOS_NO_ENCONTRADOS", "Uno o más movimientos seleccionados no existen.");

        foreach (var mov in movimientos)
        {
            if (mov.CuentaBancariaId != conciliacion.CuentaBancariaId)
                throw new BusinessRuleException("MOVIMIENTO_OTRA_CUENTA", "El movimiento pertenece a otra cuenta bancaria.");
            if (mov.EstadoConciliacion == EstadoConciliacionMovimiento.Conciliado)
                throw new BusinessRuleException("MOVIMIENTO_YA_CONCILIADO", $"El movimiento de ${mov.Monto:N2} ya está conciliado.");
            if (mov.Sentido != linea.Sentido)
                throw new BusinessRuleException("SENTIDO_NO_COINCIDE", "El sentido del movimiento no coincide con el cargo/abono de la línea.");
        }

        decimal totalMovs = movimientos.Sum(m => m.Monto);
        if (Math.Abs(totalMovs - linea.Monto) > 0.01m)
        {
            throw new BusinessRuleException("MATCH_IMPORTE_NO_COINCIDE",
                $"El importe total de los movimientos (${totalMovs:N2}) no coincide con el importe de la línea (${linea.Monto:N2}).");
        }

        DateTimeOffset ahora = clock.UtcNow;
        Guid usuarioId = userContext.UserId ?? Guid.Empty;

        if (movimientos.Count == 1)
        {
            var mov = movimientos[0];
            linea.Emparejar(mov.Id, MatchTipo.Manual, linea.Monto, usuarioId, ahora);
            mov.MarcarConciliado();
        }
        else
        {
            linea.EmparejarMultiple(
                movimientos.Select(m => (m.Id, m.Monto)),
                MatchTipo.Manual,
                usuarioId,
                ahora);

            foreach (var mov in movimientos)
            {
                mov.MarcarConciliado();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToLineaResponse(linea);
    }
}

// ============================================================================
// 5. DesemparejarLineaCommand (con motivo y bitácora obligatorios, CA9.5 / T9.2-a)
// ============================================================================
public sealed record DesemparejarLineaCommand(
    Guid ConciliacionId,
    Guid ExtractoLineaId,
    string Motivo) : IRequest<ExtractoLineaResponse>;

public sealed class DesemparejarLineaValidator : AbstractValidator<DesemparejarLineaCommand>
{
    public DesemparejarLineaValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo del desemparejamiento es obligatorio.");
    }
}

public sealed class DesemparejarLineaHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<DesemparejarLineaCommand, ExtractoLineaResponse>
{
    public async Task<ExtractoLineaResponse> Handle(DesemparejarLineaCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado == EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede desemparejar en una conciliación cerrada.");

        var linea = conciliacion.Lineas.FirstOrDefault(l => l.Id == command.ExtractoLineaId)
            ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la línea de extracto.");

        if (linea.Estado != EstadoExtractoLinea.Emparejada && linea.Estado != EstadoExtractoLinea.EnTransito)
            throw new BusinessRuleException("LINEA_NO_EMPAREJADA", "La línea no está emparejada.");

        var movIds = linea.Matches.Select(m => m.MovimientoBancarioId).Distinct().ToList();
        if (linea.MatchMovimientoId.HasValue && !movIds.Contains(linea.MatchMovimientoId.Value))
        {
            movIds.Add(linea.MatchMovimientoId.Value);
        }

        if (movIds.Count > 0)
        {
            var movimientos = await db.MovimientosBancarios
                .Where(m => movIds.Contains(m.Id))
                .ToListAsync(cancellationToken);

            foreach (var mov in movimientos)
            {
                mov.DesmarcarConciliado();
            }
        }

        DateTimeOffset ahora = clock.UtcNow;
        Guid usuarioId = userContext.UserId ?? Guid.Empty;

        conciliacion.RegistrarEnBitacora(
            linea.Id,
            linea.MatchMovimientoId,
            "Desemparejamiento",
            command.Motivo.Trim(),
            usuarioId,
            ahora);

        linea.Desemparejar();

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToLineaResponse(linea);
    }
}

// ============================================================================
// 6. AltaAsistidaDesdeExtractoCommand (Comisión + IVA, interés, depósito no registrado)
// ============================================================================
public sealed record AltaAsistidaDesdeExtractoCommand(
    Guid ConciliacionId,
    Guid ExtractoLineaId,
    Guid ConceptoId,
    bool IncluyeIva = false,
    decimal TasaIva = 0.16m,
    Guid? ConceptoIvaId = null,
    decimal? MontoBase = null,
    decimal? MontoIva = null) : IRequest<ExtractoLineaResponse>;

public sealed class AltaAsistidaDesdeExtractoValidator : AbstractValidator<AltaAsistidaDesdeExtractoCommand>
{
    public AltaAsistidaDesdeExtractoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.ConceptoId).NotEmpty();
    }
}

public sealed class AltaAsistidaDesdeExtractoHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock,
    IIntegrationEventPublisher publisher)
    : IRequestHandler<AltaAsistidaDesdeExtractoCommand, ExtractoLineaResponse>
{
    public static readonly Guid ConceptoImpuestosId = Guid.Parse("0000000b-1001-0000-0000-000000000005");

    public async Task<ExtractoLineaResponse> Handle(AltaAsistidaDesdeExtractoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado == EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede dar de alta movimientos en una conciliación cerrada.");

        var linea = conciliacion.Lineas.FirstOrDefault(l => l.Id == command.ExtractoLineaId)
            ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la línea de extracto.");

        if (linea.Estado == EstadoExtractoLinea.Emparejada)
            throw new BusinessRuleException("LINEA_YA_EMPAREJADA", "La línea ya está emparejada.");

        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        var concepto = await db.ConceptosMovimiento.FirstOrDefaultAsync(c => c.Id == command.ConceptoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCEPTO_NO_ENCONTRADO", "No se encontró el concepto bancario.");

        DateTimeOffset ahora = clock.UtcNow;
        Guid usuarioId = userContext.UserId ?? Guid.Empty;

        if (command.IncluyeIva)
        {
            decimal baseMonto = command.MontoBase ?? Math.Round(linea.Monto / (1 + command.TasaIva), 2);
            decimal ivaMonto = command.MontoIva ?? (linea.Monto - baseMonto);

            Guid conceptoIvaId = command.ConceptoIvaId ?? ConceptoImpuestosId;

            var movBase = MovimientoBancario.RegistrarMovimientoConciliacion(
                conciliacion.EmpresaId,
                cuenta,
                linea.Sentido,
                baseMonto,
                linea.Fecha,
                linea.Referencia,
                concepto.Id,
                usuarioId,
                ahora);

            var movIva = MovimientoBancario.RegistrarMovimientoConciliacion(
                conciliacion.EmpresaId,
                cuenta,
                linea.Sentido,
                ivaMonto,
                linea.Fecha,
                linea.Referencia != null ? $"{linea.Referencia} IVA" : "IVA",
                conceptoIvaId,
                usuarioId,
                ahora);

            db.MovimientosBancarios.Add(movBase);
            db.MovimientosBancarios.Add(movIva);

            linea.EmparejarMultiple(
                new[] { (movBase.Id, baseMonto), (movIva.Id, ivaMonto) },
                MatchTipo.AltaAsistida,
                usuarioId,
                ahora);

            conciliacion.RegistrarEnBitacora(
                linea.Id,
                movBase.Id,
                "Alta asistida comisión + IVA",
                $"Alta asistida desde extracto: {concepto.Nombre} (${baseMonto:N2}) + IVA (${ivaMonto:N2})",
                usuarioId,
                ahora);

            // Emitir eventos para contabilización automática (C1.5 / póliza de bancos)
            await publisher.PublishAsync(new MovimientoBancarioRegistradoIntegrationEvent(
                conciliacion.EmpresaId,
                ahora,
                movBase.Id,
                cuenta.Id,
                movBase.Sentido,
                baseMonto,
                cuenta.Moneda,
                linea.Fecha,
                movBase.ReferenciaBancaria,
                concepto.Id,
                concepto.Nombre,
                Iva: ivaMonto), cancellationToken);

            await publisher.PublishAsync(new MovimientoBancarioRegistradoIntegrationEvent(
                conciliacion.EmpresaId,
                ahora,
                movIva.Id,
                cuenta.Id,
                movIva.Sentido,
                ivaMonto,
                cuenta.Moneda,
                linea.Fecha,
                movIva.ReferenciaBancaria,
                conceptoIvaId,
                "Impuestos"), cancellationToken);
        }
        else
        {
            var mov = MovimientoBancario.RegistrarMovimientoConciliacion(
                conciliacion.EmpresaId,
                cuenta,
                linea.Sentido,
                linea.Monto,
                linea.Fecha,
                linea.Referencia,
                concepto.Id,
                usuarioId,
                ahora);

            db.MovimientosBancarios.Add(mov);
            linea.Emparejar(mov.Id, MatchTipo.AltaAsistida, linea.Monto, usuarioId, ahora);

            conciliacion.RegistrarEnBitacora(
                linea.Id,
                mov.Id,
                "Alta asistida",
                $"Alta asistida desde extracto: {concepto.Nombre} (${linea.Monto:N2})",
                usuarioId,
                ahora);

            await publisher.PublishAsync(new MovimientoBancarioRegistradoIntegrationEvent(
                conciliacion.EmpresaId,
                ahora,
                mov.Id,
                cuenta.Id,
                mov.Sentido,
                linea.Monto,
                cuenta.Moneda,
                linea.Fecha,
                mov.ReferenciaBancaria,
                concepto.Id,
                concepto.Nombre), cancellationToken);
        }

        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, conciliacion.PeriodoAnio, conciliacion.PeriodoMes, cancellationToken);
        conciliacion.ActualizarSaldoLibros(saldoLibros);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToLineaResponse(linea);
    }
}

// ============================================================================
// 7. MarcarPartidaTransitoCommand (P9.4)
// ============================================================================
public sealed record MarcarPartidaTransitoCommand(
    Guid ConciliacionId,
    Guid ExtractoLineaId,
    PartidaTransitoTipo Tipo,
    string Responsable,
    Guid? AutorizadaPor = null) : IRequest<ExtractoLineaResponse>;

public sealed class MarcarPartidaTransitoValidator : AbstractValidator<MarcarPartidaTransitoCommand>
{
    public MarcarPartidaTransitoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.Responsable).NotEmpty().MaximumLength(150);
    }
}

public sealed class MarcarPartidaTransitoHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<MarcarPartidaTransitoCommand, ExtractoLineaResponse>
{
    public async Task<ExtractoLineaResponse> Handle(MarcarPartidaTransitoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        var linea = conciliacion.Lineas.FirstOrDefault(l => l.Id == command.ExtractoLineaId)
            ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la línea de extracto.");

        Guid autorizador = command.AutorizadaPor ?? (userContext.UserId ?? Guid.Empty);
        linea.MarcarPartidaTransito(command.Tipo, command.Responsable, autorizador);

        conciliacion.RegistrarEnBitacora(
            linea.Id,
            null,
            "Partida en tránsito",
            $"Marcada como partida en tránsito ({command.Tipo}) asignada a {command.Responsable}",
            userContext.UserId ?? Guid.Empty,
            clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToLineaResponse(linea);
    }
}

// ============================================================================
// 8. CerrarConciliacionCommand (Solo cierra con diferencia 0 y genera acta, RN-7)
// ============================================================================
public sealed record CerrarConciliacionCommand(
    Guid ConciliacionId,
    string FirmanteNombre,
    string FirmantePuesto) : IRequest<byte[]>;

public sealed class CerrarConciliacionValidator : AbstractValidator<CerrarConciliacionCommand>
{
    public CerrarConciliacionValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.FirmanteNombre).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FirmantePuesto).NotEmpty().MaximumLength(100);
    }
}

public sealed class CerrarConciliacionHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<CerrarConciliacionCommand, byte[]>
{
    public async Task<byte[]> Handle(CerrarConciliacionCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, conciliacion.PeriodoAnio, conciliacion.PeriodoMes, cancellationToken);

        conciliacion.Cerrar(
            userContext.UserId ?? Guid.Empty,
            command.FirmanteNombre,
            command.FirmantePuesto,
            saldoLibros,
            clock.UtcNow);

        var partidasTransito = conciliacion.Lineas
            .Where(l => l.PartidaTransito)
            .Select(l => new DatosPartidaTransito(
                l.Fecha,
                l.Descripcion,
                l.Referencia,
                l.Monto,
                l.Sentido.ToString(),
                l.PartidaTransitoTipo?.ToString() ?? "Otro",
                l.PartidaTransitoResponsable ?? "No asignado"))
            .ToList();

        var datosActa = new DatosActaConciliacion(
            conciliacion.Id,
            "Vidrios Millet de México S.A. de C.V.",
            "VMI900101XYZ",
            cuenta.Banco,
            Millet.SharedKernel.Domain.Bancos.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado,
            cuenta.Moneda,
            conciliacion.PeriodoAnio,
            conciliacion.PeriodoMes,
            conciliacion.SaldoExtracto,
            conciliacion.SaldoLibros,
            conciliacion.Lineas.Where(l => l.Cargo.HasValue).Sum(l => l.Cargo!.Value),
            conciliacion.Lineas.Where(l => l.Abono.HasValue).Sum(l => l.Abono!.Value),
            conciliacion.Lineas.Count,
            conciliacion.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Emparejada),
            partidasTransito,
            conciliacion.FirmanteNombre!,
            conciliacion.FirmantePuesto!,
            conciliacion.CerradaEn!.Value);

        byte[] pdfBytes = ActaConciliacionPdfGenerator.Generar(datosActa);

        await db.SaveChangesAsync(cancellationToken);
        return pdfBytes;
    }
}

// ============================================================================
// 9. ReabrirConciliacionCommand (Exige motivo obligatorio)
// ============================================================================
public sealed record ReabrirConciliacionCommand(
    Guid ConciliacionId,
    string Motivo) : IRequest<ConciliacionResumenResponse>;

public sealed class ReabrirConciliacionValidator : AbstractValidator<ReabrirConciliacionCommand>
{
    public ReabrirConciliacionValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo de reapertura es obligatorio.");
    }
}

public sealed class ReabrirConciliacionHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock)
    : IRequestHandler<ReabrirConciliacionCommand, ConciliacionResumenResponse>
{
    public async Task<ConciliacionResumenResponse> Handle(ReabrirConciliacionCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        conciliacion.Reabrir(userContext.UserId ?? Guid.Empty, command.Motivo, clock.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToResumen(conciliacion, cuenta);
    }
}

// ============================================================================
// Helper de cálculos y mapeos
// ============================================================================
internal static class ConciliacionCalculos
{
    public static async Task<decimal> CalcularSaldoLibrosAsync(
        TesoreriaDbContext db,
        CuentaBancaria cuenta,
        int anio,
        int mes,
        CancellationToken ct)
    {
        var fechaFin = new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes));

        decimal saldoMovs = await db.MovimientosBancarios
            .Where(m => m.CuentaBancariaId == cuenta.Id && m.FechaValor <= fechaFin)
            .SumAsync(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto, ct);

        return (cuenta.SaldoInicial ?? 0m) + saldoMovs;
    }
}

internal static class ConciliacionMapper
{
    public static ConciliacionResumenResponse ToResumen(Conciliacion c, CuentaBancaria cuenta) => new(
        c.Id,
        c.CuentaBancariaId,
        cuenta.Banco,
        Millet.SharedKernel.Domain.Bancos.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado,
        cuenta.Moneda,
        c.PeriodoAnio,
        c.PeriodoMes,
        c.SaldoExtracto,
        c.SaldoLibros,
        c.Diferencia,
        c.Estado,
        c.Lineas.Count,
        c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Emparejada),
        c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Pendiente),
        c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.EnRevision),
        c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.EnTransito),
        c.CerradaEn,
        c.FirmanteNombre);

    public static ExtractoLineaResponse ToLineaResponse(ExtractoLinea l) => new(
        l.Id,
        l.NumeroLinea,
        l.Fecha,
        l.Descripcion,
        l.Referencia,
        l.Cargo,
        l.Abono,
        l.Monto,
        l.Sentido,
        l.Estado,
        l.MatchTipo,
        l.MatchMovimientoId,
        l.Matches.Select(m => new ExtractoLineaMatchResponse(m.Id, m.MovimientoBancarioId, m.Monto, m.FechaMatch)).ToList(),
        l.CandidatosRevisionIds,
        l.PartidaTransito,
        l.PartidaTransitoTipo,
        l.PartidaTransitoResponsable);
}
