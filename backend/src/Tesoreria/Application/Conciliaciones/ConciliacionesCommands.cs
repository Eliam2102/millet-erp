using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Depositos;
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
    string? Formato = null,
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
    ExtractoParserFactory parserFactory,
    DepositoAutomaticoProcesador depositos)
    : IRequestHandler<CargarExtractoCommand, ConciliacionResumenResponse>
{
    public Task<ConciliacionResumenResponse> Handle(CargarExtractoCommand command, CancellationToken cancellationToken) =>
        DepositoAutomaticoProcesador.EjecutarBajoLockAsync(db, ct => HandleAsync(command, ct), cancellationToken);

    private async Task<ConciliacionResumenResponse> HandleAsync(CargarExtractoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación bancaria.");

        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        // Sin formato explícito manda el perfil de extracto de la cuenta (BBVA, SANTANDER, MIFEL).
        var parser = parserFactory.ObtenerParser(command.Formato ?? cuenta.PerfilExtracto ?? "BBVA", command.ContenidoArchivo);
        var parseResult = parser.Parse(command.ContenidoArchivo);

        int numLineaActual = conciliacion.Lineas.Count > 0 ? conciliacion.Lineas.Max(l => l.NumeroLinea) + 1 : 1;

        // Una línea ya importada en otra conciliación de la misma cuenta (mismo hash) no se vuelve a cargar.
        var hashes = parseResult.Lineas.Where(p => p.LineaHash != null).Select(p => p.LineaHash!).Distinct().ToList();
        var hashesEnOtras = (await db.ExtractoLineas.AsNoTracking()
                .Where(l => l.LineaHash != null && hashes.Contains(l.LineaHash) && l.ConciliacionId != conciliacion.Id
                    && db.Conciliaciones.Any(c => c.Id == l.ConciliacionId && c.CuentaBancariaId == conciliacion.CuentaBancariaId))
                .Select(l => l.LineaHash!).ToListAsync(cancellationToken)).ToHashSet();
        var idsPrevios = conciliacion.Lineas.Select(l => l.Id).ToHashSet();
        // Las líneas que ya están en esta conciliación tampoco consumen número de línea al reimportar el archivo.
        var hashesAqui = conciliacion.Lineas.Where(l => l.LineaHash != null).Select(l => l.LineaHash!).ToHashSet();

        var nuevasLineas = parseResult.Lineas.Where(p => p.LineaHash == null || (!hashesEnOtras.Contains(p.LineaHash) && !hashesAqui.Contains(p.LineaHash))).Select(p => ExtractoLinea.Crear(
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
        var agregadas = conciliacion.CargarLineas(nuevasLineas, saldoExt);

        ResumenDepositosAutomaticos? resumenDepositos = null;
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

            // 86e3ke6yn: solo las líneas nuevas de este archivo.
            resumenDepositos = await depositos.ProcesarAsync(conciliacion, cuenta,
                conciliacion.Lineas.Where(l => !idsPrevios.Contains(l.Id)).ToList(), crearSinIdentificar: true, cancellationToken);
        }

        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, conciliacion.PeriodoAnio, conciliacion.PeriodoMes, cancellationToken);
        conciliacion.ActualizarSaldoLibros(saldoLibros);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToResumen(conciliacion, cuenta) with
        {
            Depositos = resumenDepositos,
            LineasNuevas = agregadas,
            LineasDuplicadas = parseResult.Lineas.Count - agregadas,
        };
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
    IClock clock,
    DepositoAutomaticoProcesador depositos)
    : IRequestHandler<EmparejarAutomaticoCommand, ResultadoEmparejamiento>
{
    public Task<ResultadoEmparejamiento> Handle(EmparejarAutomaticoCommand command, CancellationToken cancellationToken) =>
        DepositoAutomaticoProcesador.EjecutarBajoLockAsync(db, ct => HandleAsync(command, ct), cancellationToken);

    private async Task<ResultadoEmparejamiento> HandleAsync(EmparejarAutomaticoCommand command, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .Include(c => c.Lineas)
                .ThenInclude(l => l.Matches)
            .FirstOrDefaultAsync(c => c.Id == command.ConciliacionId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado != EstadoConciliacion.Abierta)
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

        // 86e3ke6yn: una línea sin propuesta no crea ingreso aquí (puede ser un alta asistida pendiente); solo al importar.
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");
        await depositos.ProcesarAsync(conciliacion, cuenta, conciliacion.Lineas.ToList(), crearSinIdentificar: false, cancellationToken);

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

        if (conciliacion.Estado != EstadoConciliacion.Abierta)
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

        if (conciliacion.Estado != EstadoConciliacion.Abierta)
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
    decimal? MontoIva = null,
    Guid? AreaId = null,
    Guid? EquipoId = null) : IRequest<ExtractoLineaResponse>;

public sealed class AltaAsistidaDesdeExtractoValidator : AbstractValidator<AltaAsistidaDesdeExtractoCommand>
{
    public AltaAsistidaDesdeExtractoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.ConceptoId).NotEmpty();
    }
}

/// <remarks>
/// H1 paso 3 (86e3z5w25): <c>AreaId</c>/<c>EquipoId</c> = área (Dim2) y equipo (Dim3) que el usuario captura cuando la cuenta del
/// concepto exige dimensiones (regla C-03, p. ej. comisiones a 601). Contabilidad los valida (activos, de la sucursal, equipo del
/// área): un dato inválido rechaza la alta; sin el dato la alta procede y la póliza queda en Rechazos. El centro solo viaja en el
/// evento de la base (el del IVA no genera póliza).
/// </remarks>
public sealed class AltaAsistidaDesdeExtractoHandler(
    TesoreriaDbContext db,
    ICurrentUserContext userContext,
    IClock clock,
    IIntegrationEventPublisher publisher,
    IDimensionesCapturaPort? dimensiones = null)
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

        if (conciliacion.Estado != EstadoConciliacion.Abierta)
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

        // U-2 (86e3yxngq): la póliza de Contabilidad exige sucursal y el extracto no la trae. Supuesto a validar: la de la cuenta
        // bancaria, tomada del último movimiento de esa cuenta que sí la tiene (depósitos de caja, cobros). Sin ninguno va null y
        // Contabilidad deja el evento en su bandeja con «falta SucursalId». Solo viaja en el evento: el movimiento no cambia.
        Guid? sucursalId = await SucursalDeCuentaAsync(db, cuenta.Id, cancellationToken);
        Guid? centroCostoId = await CentroCostoAsync(command, conciliacion.EmpresaId, sucursalId, concepto.Nombre, linea.Fecha, cancellationToken);

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
                Iva: ivaMonto,
                SucursalId: sucursalId,
                CentroCostoId: centroCostoId), cancellationToken);

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
                "Impuestos",
                SucursalId: sucursalId,
                MovimientoBaseId: movBase.Id), cancellationToken);
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
                concepto.Nombre,
                SucursalId: sucursalId,
                CentroCostoId: centroCostoId), cancellationToken);
        }

        decimal saldoLibros = await ConciliacionCalculos.CalcularSaldoLibrosAsync(
            db, cuenta, conciliacion.PeriodoAnio, conciliacion.PeriodoMes, cancellationToken);
        conciliacion.ActualizarSaldoLibros(saldoLibros);

        await db.SaveChangesAsync(cancellationToken);
        return ConciliacionMapper.ToLineaResponse(linea);
    }

    /// <summary>
    /// U-2: sucursal de la cuenta bancaria = la del último movimiento de esa cuenta que la tiene (el extracto no la trae).
    /// H1 paso 3: la misma acota el área y el equipo que se ofrecen y se aceptan.
    /// </summary>
    internal static Task<Guid?> SucursalDeCuentaAsync(TesoreriaDbContext db, Guid cuentaBancariaId, CancellationToken ct) =>
        db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.CuentaBancariaId == cuentaBancariaId && m.SucursalId != null)
            .OrderByDescending(m => m.FechaValor).ThenByDescending(m => m.Id)
            .Select(m => m.SucursalId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// H1 paso 3: centro que viaja en el evento (equipo si se eligió; si no, el área). Un dato inválido rechaza la alta.
    /// M-450 (86e3zr7ft): si la cuenta del concepto exige área o equipo (regla C-03) y no se capturaron, la alta tampoco procede: el
    /// usuario los captura (sin sucursal conocida no hay exigencia que revisar y la póliza resuelve en Rechazos).
    /// </summary>
    private async Task<Guid?> CentroCostoAsync(AltaAsistidaDesdeExtractoCommand command, Guid empresaId, Guid? sucursalId, string concepto,
        DateOnly fecha, CancellationToken ct)
    {
        var capturado = command.AreaId is not null || command.EquipoId is not null;
        if (sucursalId is not { } sucursal)
        {
            if (!capturado) return null;
            throw new BusinessRuleException("TES_DIMENSION_SIN_SUCURSAL",
                "La cuenta bancaria aún no tiene sucursal (ningún movimiento la trae): no se puede validar el área ni el equipo.");
        }
        if (dimensiones is null) return command.EquipoId ?? command.AreaId;
        var r = await dimensiones.ValidarAsync(MovimientoCapturaBancos(empresaId, sucursal, concepto, fecha), command.AreaId, command.EquipoId, ct);
        if (r.Error is not null) throw new BusinessRuleException("TES_DIMENSION_INVALIDA", r.Error);
        if (r.Faltante is not null) throw new BusinessRuleException("TES_DIMENSION_FALTANTE", r.Faltante);
        return r.CentroCostoId;
    }

    /// <summary>La póliza del alta carga el concepto bancario (rol por concepto) en el campo <c>Base</c> de la plantilla.</summary>
    internal static MovimientoCaptura MovimientoCapturaBancos(Guid empresaId, Guid sucursalId, string concepto, DateOnly fecha) =>
        new(empresaId, sucursalId, MovimientoBancarioRegistradoIntegrationEvent.WireEventType, "Base", concepto.Trim(), fecha);
}

// ============================================================================
// 6b. Área y equipo para la alta asistida (H1 paso 3)
// ============================================================================

/// <summary>Qué exige la cuenta del concepto y la sucursal con la que se ofrecen los centros. Sin sucursal no se exige nada.</summary>
public sealed record DimensionesAltaResponse(Guid? SucursalId, bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);

public sealed record DimensionesAltaQuery(Guid ConciliacionId, Guid ConceptoId, DateOnly? Fecha) : IRequest<DimensionesAltaResponse>;

public sealed class DimensionesAltaHandler(TesoreriaDbContext db, IDimensionesCapturaPort dimensiones, IClock clock)
    : IRequestHandler<DimensionesAltaQuery, DimensionesAltaResponse>
{
    public async Task<DimensionesAltaResponse> Handle(DimensionesAltaQuery request, CancellationToken cancellationToken)
    {
        var (conciliacion, sucursal) = await ConciliacionYSucursalAsync(db, request.ConciliacionId, cancellationToken);
        var concepto = await db.ConceptosMovimiento.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ConceptoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONCEPTO_NO_ENCONTRADO", "No se encontró el concepto bancario.");
        if (sucursal is not { } s) return new(null, false, false, null);
        var fecha = request.Fecha ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var e = await dimensiones.ExigenciasAsync(
            AltaAsistidaDesdeExtractoHandler.MovimientoCapturaBancos(conciliacion.EmpresaId, s, concepto.Nombre, fecha), cancellationToken);
        return new(s, e.ExigeArea, e.ExigeEquipo, e.CuentaCodigo);
    }

    internal static async Task<(Conciliacion Conciliacion, Guid? Sucursal)> ConciliacionYSucursalAsync(TesoreriaDbContext db, Guid id, CancellationToken ct)
    {
        var conciliacion = await db.Conciliaciones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");
        return (conciliacion, await AltaAsistidaDesdeExtractoHandler.SucursalDeCuentaAsync(db, conciliacion.CuentaBancariaId, ct));
    }
}

/// <summary>Áreas (Dim2) o equipos (Dim3) activos de la sucursal de la cuenta bancaria; vacío si la cuenta aún no tiene sucursal.</summary>
public sealed record CentrosAltaQuery(Guid ConciliacionId, bool Equipos, Guid? AreaId, string? Q, int Limit) : IRequest<IReadOnlyList<CentroOpcionDto>>;

public sealed class CentrosAltaHandler(TesoreriaDbContext db, IDimensionesCapturaPort dimensiones)
    : IRequestHandler<CentrosAltaQuery, IReadOnlyList<CentroOpcionDto>>
{
    public async Task<IReadOnlyList<CentroOpcionDto>> Handle(CentrosAltaQuery request, CancellationToken cancellationToken)
    {
        var (conciliacion, sucursal) = await DimensionesAltaHandler.ConciliacionYSucursalAsync(db, request.ConciliacionId, cancellationToken);
        return sucursal is { } s
            ? await dimensiones.CentrosAsync(conciliacion.EmpresaId, s, request.Equipos, request.AreaId, request.Q, request.Limit, cancellationToken)
            : [];
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
    string Causa,
    DateOnly FechaPrevistaResolucion) : IRequest<ExtractoLineaResponse>;

public sealed class MarcarPartidaTransitoValidator : AbstractValidator<MarcarPartidaTransitoCommand>
{
    public MarcarPartidaTransitoValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.ExtractoLineaId).NotEmpty();
        RuleFor(x => x.Responsable).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Causa).NotEmpty().WithMessage("La causa es obligatoria.").MaximumLength(300);
        RuleFor(x => x.FechaPrevistaResolucion).NotEmpty().WithMessage("La fecha prevista de resolución es obligatoria.");
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

        if (conciliacion.Estado != EstadoConciliacion.Abierta)
            throw new BusinessRuleException("CONCILIACION_CERRADA", "La conciliación no está abierta.");
        // Javier Canché 11-nov: la partida en tránsito la autoriza el Director de Planeación Financiera
        // (facultad tesoreria.conciliacion.autorizar-transito, exigida por el endpoint); quien la marca es quien autoriza.
        linea.MarcarPartidaTransito(command.Tipo, command.Responsable, userContext.UserId ?? Guid.Empty,
            command.Causa, command.FechaPrevistaResolucion);

        conciliacion.RegistrarEnBitacora(
            linea.Id,
            null,
            "Partida en tránsito",
            $"Marcada como partida en tránsito ({command.Tipo}) asignada a {command.Responsable}. Causa: {command.Causa.Trim()}; se resuelve el {command.FechaPrevistaResolucion:dd/MM/yyyy}",
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
    IClock clock,
    Millet.Tesoreria.Domain.Ports.DatosMaestros.IEmpresaActaReadPort empresas)
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

        // El acta lleva la razón social y el RFC vigentes de la empresa de la conciliación.
        var empresa = await empresas.ObtenerAsync(conciliacion.EmpresaId, cancellationToken)
            ?? throw new EntityNotFoundException("EMPRESA_NO_ENCONTRADA", "No se encontró la empresa de la conciliación.");

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
                l.PartidaTransitoResponsable ?? "No asignado",
                l.PartidaTransitoCausa,
                l.PartidaTransitoFechaPrevista))
            .ToList();

        var datosActa = new DatosActaConciliacion(
            conciliacion.Id,
            empresa.RazonSocial,
            empresa.Rfc,
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
// 10. Visto bueno de Contabilidad y devolución a Tesorería (M-202)
// ============================================================================
public sealed record DarVistoBuenoConciliacionCommand(Guid ConciliacionId) : IRequest<ConciliacionResumenResponse>;

public sealed record DevolverConciliacionCommand(Guid ConciliacionId, string Motivo) : IRequest<ConciliacionResumenResponse>;

public sealed class DevolverConciliacionValidator : AbstractValidator<DevolverConciliacionCommand>
{
    public DevolverConciliacionValidator()
    {
        RuleFor(x => x.ConciliacionId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio.").MaximumLength(500);
    }
}

public sealed class VistoBuenoConciliacionHandler(TesoreriaDbContext db, ICurrentUserContext userContext, IClock clock)
    : IRequestHandler<DarVistoBuenoConciliacionCommand, ConciliacionResumenResponse>,
      IRequestHandler<DevolverConciliacionCommand, ConciliacionResumenResponse>
{
    public Task<ConciliacionResumenResponse> Handle(DarVistoBuenoConciliacionCommand command, CancellationToken cancellationToken) =>
        EjecutarAsync(command.ConciliacionId, c => c.DarVistoBueno(userContext.UserId ?? Guid.Empty, clock.UtcNow), cancellationToken);

    public Task<ConciliacionResumenResponse> Handle(DevolverConciliacionCommand command, CancellationToken cancellationToken) =>
        EjecutarAsync(command.ConciliacionId, c => c.DevolverATesoreria(userContext.UserId ?? Guid.Empty, command.Motivo, clock.UtcNow), cancellationToken);

    private async Task<ConciliacionResumenResponse> EjecutarAsync(Guid id, Action<Conciliacion> accion, CancellationToken ct)
    {
        var conciliacion = await db.Conciliaciones.Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");
        var cuenta = await db.CuentasBancarias.FirstAsync(c => c.Id == conciliacion.CuentaBancariaId, ct);
        accion(conciliacion);
        await db.SaveChangesAsync(ct);
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

        // La suma va a la base: los movimientos recién agregados (alta asistida) todavía no están ahí,
        // se suman del ChangeTracker para que el saldo se guarde en la misma transacción.
        saldoMovs += db.ChangeTracker.Entries<MovimientoBancario>()
            .Where(e => e.State == EntityState.Added && e.Entity.CuentaBancariaId == cuenta.Id && e.Entity.FechaValor <= fechaFin)
            .Sum(e => e.Entity.Sentido == SentidoMovimiento.Ingreso ? e.Entity.Monto : -e.Entity.Monto);

        return (cuenta.SaldoInicial ?? 0m) + saldoMovs;
    }

    /// <summary>
    /// Saldo en libros para mostrar: abierta = calculado al momento (un movimiento registrado desde Movimientos bancarios no
    /// toca la conciliación, así que el guardado podía quedar viejo); cerrada = el congelado al cerrar.
    /// </summary>
    public static Task<decimal> SaldoLibrosVigenteAsync(TesoreriaDbContext db, Conciliacion c, CuentaBancaria cuenta, CancellationToken ct) =>
        c.Estado != EstadoConciliacion.Abierta
            ? Task.FromResult(c.SaldoLibros)
            : CalcularSaldoLibrosAsync(db, cuenta, c.PeriodoAnio, c.PeriodoMes, ct);
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
        l.PartidaTransitoResponsable,
        l.PartidaTransitoCausa,
        l.PartidaTransitoFechaPrevista);
}
