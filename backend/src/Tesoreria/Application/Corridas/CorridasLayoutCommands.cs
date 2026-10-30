using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Corridas;

// ============================================================================
// T9.3 (86e3ke5ck): Generador de archivo de layout de pago por banco
// (BBVA primero, configurable). Permite generar desde la corrida autorizada
// el archivo de transferencias masivas para la banca (SPEI e internacional),
// valida CLABE de 18 dígitos con dígito verificador y controla reimpresiones.
// ============================================================================

public sealed record ExcepcionLineaLayoutResponse(
    Guid FacturaProveedorId,
    Guid ProveedorId,
    string? ProveedorRazonSocial,
    string? FolioProveedor,
    decimal ImporteNeto,
    string Causa);

public sealed record LineaLayoutResponse(
    Guid FacturaProveedorId,
    Guid ProveedorId,
    string? ProveedorRazonSocial,
    string? FolioProveedor,
    decimal ImporteNeto,
    string CuentaDestino,
    string Titular,
    string Referencia,
    string Concepto);

public sealed record LayoutCorridaResponse(
    Guid ArchivoId,
    Guid CorridaId,
    string Banco,
    string TipoPago,
    string NombreArchivo,
    string Contenido,
    decimal Total,
    int LineasValidas,
    int LineasExcluidas,
    bool EsReimpresion,
    DateTimeOffset GeneradoEn,
    IReadOnlyList<ExcepcionLineaLayoutResponse> Excepciones,
    IReadOnlyList<LineaLayoutResponse> Lineas);

public sealed record GenerarLayoutCorridaCommand(
    Guid CorridaId,
    string Banco = "BBVA",
    TipoPagoLayout TipoPago = TipoPagoLayout.NacionalSpei) : IRequest<LayoutCorridaResponse>;

public sealed class GenerarLayoutCorridaValidator : AbstractValidator<GenerarLayoutCorridaCommand>
{
    public GenerarLayoutCorridaValidator()
    {
        RuleFor(c => c.CorridaId).NotEmpty().WithMessage("El ID de la corrida es obligatorio.");
        RuleFor(c => c.Banco).NotEmpty().MaximumLength(50).WithMessage("El banco es obligatorio.");
    }
}

public sealed class GenerarLayoutCorridaHandler(
    TesoreriaDbContext db,
    IProveedorBancoReadPort proveedores,
    ICurrentUserContext user,
    IClock clock)
    : IRequestHandler<GenerarLayoutCorridaCommand, LayoutCorridaResponse>
{
    public async Task<LayoutCorridaResponse> Handle(GenerarLayoutCorridaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var banco = string.IsNullOrWhiteSpace(command.Banco) ? "BBVA" : command.Banco.Trim().ToUpperInvariant();
        var tipoPago = command.TipoPago;

        // 1. Si el archivo ya fue generado, se entrega como reimpresión y NO se crea otro registro (CA 3, T9.3-a).
        var archivoExistente = await db.ArchivosLayoutPago
            .Include(a => a.Lineas)
            .FirstOrDefaultAsync(a => a.CorridaId == command.CorridaId && a.Banco == banco && a.TipoPago == tipoPago, ct);

        if (archivoExistente is not null)
        {
            archivoExistente.RegistrarReimpresion(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return ToResponse(archivoExistente, esReimpresion: true, excepciones: []);
        }

        // 2. Solo se permite generar para corridas autorizadas (CA 4, RN-5).
        var corrida = await db.CorridasPago
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == command.CorridaId, ct)
            ?? throw new EntityNotFoundException("CORRIDA_NO_ENCONTRADA", $"No se encontró la corrida '{command.CorridaId}'.");

        if (corrida.Estado != EstadoCorridaPago.Autorizada)
        {
            throw new BusinessRuleException("CORRIDA_LAYOUT_NO_AUTORIZADA",
                $"El archivo de layout solo se genera para corridas autorizadas; la corrida está en estado {corrida.Estado}.");
        }

        if (corrida.Lineas.Count == 0)
        {
            throw new BusinessRuleException("CORRIDA_SIN_LINEAS", "La corrida no tiene pasivos seleccionados.");
        }

        // 3. Cargar perfil de layout configurable (BBVA por omisión).
        var perfil = await db.PerfilesLayoutBanco
            .FirstOrDefaultAsync(p => p.EmpresaId == corrida.EmpresaId && p.Banco == banco && p.TipoPago == tipoPago && p.Activo, ct);

        if (perfil is null)
        {
            if (banco == "BBVA" && tipoPago == TipoPagoLayout.NacionalSpei)
                perfil = PerfilLayoutBanco.BbvaNacionalSpei(corrida.EmpresaId);
            else if (banco == "BBVA" && tipoPago == TipoPagoLayout.Internacional)
                perfil = PerfilLayoutBanco.BbvaInternacional(corrida.EmpresaId);
            else
                perfil = new PerfilLayoutBanco(corrida.EmpresaId, $"{banco}_{tipoPago}", banco, $"{banco} {tipoPago}", tipoPago);
        }

        // 4. Cargar datos maestros de proveedores y pasivos pendientes.
        var proveedorIds = corrida.Lineas.Select(l => l.ProveedorId).Distinct().ToList();
        var proveedoresInfo = await proveedores.ObtenerVariosAsync(proveedorIds, ct);

        var facturaIds = corrida.Lineas.Select(l => l.FacturaProveedorId).ToList();
        var pasivos = await db.PasivosPendientesPago.AsNoTracking()
            .Where(p => facturaIds.Contains(p.FacturaProveedorId))
            .ToDictionaryAsync(p => p.FacturaProveedorId, ct);

        // 5. Validar cada línea: CLABE 18 dígitos con DV, cuenta vigente, moneda coincidente (CA 2).
        var excepciones = new List<ExcepcionLineaLayoutResponse>();
        var validas = new List<(CorridaPagoLinea Linea, CuentaProveedorDto Cuenta, string Titular, string Referencia, string Concepto, string? RazonSocial, string? Folio)>();

        foreach (var linea in corrida.Lineas)
        {
            pasivos.TryGetValue(linea.FacturaProveedorId, out var pasivo);
            var folio = pasivo?.FolioProveedor;
            proveedoresInfo.TryGetValue(linea.ProveedorId, out var pInfo);
            var razonSocial = pInfo?.RazonSocial;

            if (pInfo is null)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    "Proveedor no encontrado en Datos Maestros."));
                continue;
            }

            if (!pInfo.Activo)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    "El proveedor no está activo."));
                continue;
            }

            if (pInfo.Bloqueado)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    $"El proveedor está bloqueado: {pInfo.MotivoBloqueo ?? "Sin motivo"}."));
                continue;
            }

            var cuenta = pInfo.CuentaPara(corrida.Moneda);
            if (cuenta is null)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    $"El proveedor no tiene cuenta bancaria activa y vigente en moneda {corrida.Moneda}."));
                continue;
            }

            if (!cuenta.Activa)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    "La cuenta bancaria del proveedor no está vigente o activa."));
                continue;
            }

            if (cuenta.Moneda != corrida.Moneda)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    $"La moneda de la cuenta ({cuenta.Moneda}) no coincide con la moneda de la corrida ({corrida.Moneda})."));
                continue;
            }

            if (tipoPago == TipoPagoLayout.NacionalSpei)
            {
                try
                {
                    _ = Clabe.Crear(cuenta.Numero);
                }
                catch (BusinessRuleException ex)
                {
                    excepciones.Add(new ExcepcionLineaLayoutResponse(
                        linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                        ex.Message));
                    continue;
                }
            }

            if (linea.ImporteProgramado <= 0)
            {
                excepciones.Add(new ExcepcionLineaLayoutResponse(
                    linea.FacturaProveedorId, linea.ProveedorId, razonSocial, folio, linea.ImporteProgramado,
                    "El importe neto programado debe ser mayor a cero."));
                continue;
            }

            var titular = !string.IsNullOrWhiteSpace(cuenta.Titular) ? cuenta.Titular : (pInfo.RazonSocial ?? "PROVEEDOR");
            var referencia = !string.IsNullOrWhiteSpace(folio) ? folio : linea.FacturaProveedorId.ToString("N")[..8].ToUpperInvariant();
            var concepto = $"PAGO {(!string.IsNullOrWhiteSpace(folio) ? folio : "FACTURA")}";

            validas.Add((linea, cuenta, titular, referencia, concepto, razonSocial, folio));
        }

        if (validas.Count == 0)
        {
            throw new BusinessRuleException("CORRIDA_LAYOUT_SIN_LINEAS_VALIDAS",
                "No se encontraron pagos válidos para generar el archivo de layout. Verifique las cuentas bancarias de los proveedores.");
        }

        // 6. Generar líneas de texto del archivo según el perfil.
        var lineasTexto = new List<string>();
        if (perfil.IncluyeEncabezado)
        {
            var header = perfil.GenerarEncabezado();
            if (!string.IsNullOrEmpty(header)) lineasTexto.Add(header);
        }

        var lineasArchivo = new List<ArchivoLayoutPagoLinea>();
        var lineasResponse = new List<LineaLayoutResponse>();

        foreach (var (linea, cuenta, titular, referencia, concepto, razonSocial, folio) in validas)
        {
            var lineaFormateada = perfil.GenerarLinea(
                cuentaOClabe: cuenta.Numero,
                importeNeto: linea.ImporteProgramado,
                titular: titular,
                referencia: referencia,
                concepto: concepto);

            lineasTexto.Add(lineaFormateada);

            lineasArchivo.Add(new ArchivoLayoutPagoLinea(
                facturaProveedorId: linea.FacturaProveedorId,
                proveedorId: linea.ProveedorId,
                importeNeto: linea.ImporteProgramado,
                cuentaDestino: cuenta.Numero,
                titular: titular,
                referencia: referencia,
                concepto: concepto));

            lineasResponse.Add(new LineaLayoutResponse(
                FacturaProveedorId: linea.FacturaProveedorId,
                ProveedorId: linea.ProveedorId,
                ProveedorRazonSocial: razonSocial,
                FolioProveedor: folio,
                ImporteNeto: linea.ImporteProgramado,
                CuentaDestino: cuenta.Numero,
                Titular: titular,
                Referencia: referencia,
                Concepto: concepto));
        }

        var contenido = string.Join("\r\n", lineasTexto);
        var totalValido = validas.Sum(v => v.Linea.ImporteProgramado);
        var ahora = clock.UtcNow;
        var usuarioId = user.UserId ?? Guid.Empty;
        var sufijo = ahora.ToString("yyyyMMdd_HHmmss");
        var nombreArchivo = $"PAGOS_{banco}_{tipoPago}_{corrida.Nombre.Replace(" ", "_")}_{sufijo}.txt";

        // 7. Persistir el archivo generado con el detalle de pagos para control y trazabilidad.
        var archivo = ArchivoLayoutPago.Crear(
            empresaId: corrida.EmpresaId,
            corridaId: corrida.Id,
            banco: banco,
            tipoPago: tipoPago,
            nombreArchivo: nombreArchivo,
            contenido: contenido,
            total: totalValido,
            lineasExcluidas: excepciones.Count,
            generadoPor: usuarioId,
            ahora: ahora,
            lineas: lineasArchivo);

        db.ArchivosLayoutPago.Add(archivo);
        await db.SaveChangesAsync(ct);

        return new LayoutCorridaResponse(
            ArchivoId: archivo.Id,
            CorridaId: corrida.Id,
            Banco: banco,
            TipoPago: tipoPago.ToString(),
            NombreArchivo: nombreArchivo,
            Contenido: contenido,
            Total: totalValido,
            LineasValidas: validas.Count,
            LineasExcluidas: excepciones.Count,
            EsReimpresion: false,
            GeneradoEn: ahora,
            Excepciones: excepciones,
            Lineas: lineasResponse);
    }

    private static LayoutCorridaResponse ToResponse(
        ArchivoLayoutPago a,
        bool esReimpresion,
        IReadOnlyList<ExcepcionLineaLayoutResponse> excepciones) =>
        new(
            ArchivoId: a.Id,
            CorridaId: a.CorridaId,
            Banco: a.Banco,
            TipoPago: a.TipoPago.ToString(),
            NombreArchivo: a.NombreArchivo,
            Contenido: a.Contenido,
            Total: a.Total,
            LineasValidas: a.LineasValidas,
            LineasExcluidas: a.LineasExcluidas,
            EsReimpresion: esReimpresion,
            GeneradoEn: a.GeneradoEn,
            Excepciones: excepciones,
            Lineas: a.Lineas.Select(l => new LineaLayoutResponse(
                FacturaProveedorId: l.FacturaProveedorId,
                ProveedorId: l.ProveedorId,
                ProveedorRazonSocial: null,
                FolioProveedor: null,
                ImporteNeto: l.ImporteNeto,
                CuentaDestino: l.CuentaDestino,
                Titular: l.Titular,
                Referencia: l.Referencia,
                Concepto: l.Concepto)).ToList());
}
