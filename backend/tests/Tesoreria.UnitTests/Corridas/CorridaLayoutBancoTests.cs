using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Corridas;

/// <summary>
/// Pruebas unitarias para T9.3 (86e3ke5ck):
/// Layout de pago en lote por banco (BBVA primero, configurable).
/// Cubre los 4 Criterios de Aceptación:
/// CA1: Corrida autorizada de 12 pagos nacionales BBVA genera archivo con 12 líneas y totales coincidentes.
/// CA2: Proveedor con CLABE inválida sale del archivo y aparece en excepciones con la causa.
/// CA3: Archivo ya generado se entrega marcado como reimpresión sin crear otro registro de salida.
/// CA4: Corrida en borrador no permite generar el archivo.
/// </summary>
public sealed class CorridaLayoutBancoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Armador = Guid.NewGuid();
    private static readonly Guid Jefe = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 14, 0, 0, TimeSpan.Zero);

    // CLABEs válidas (Banxico 3-7-1)
    private const string ClabeValida1 = "032180000118359719";
    private const string ClabeValida2 = "012180015678912342";
    // CLABE con dígito verificador inválido
    private const string ClabeDigitoInvalido = "032180000118359718";

    private static TesoreriaDbContext Db() =>
        new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static async Task<(TesoreriaDbContext Db, CorridaResponse Corrida, List<PasivoPendientePago> Pasivos)> CrearCorridaAutorizada(
        IProveedorBancoReadPort proveedoresPort,
        params (Guid Proveedor, decimal Monto)[] items)
    {
        var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var pasivos = items.Select((x, i) =>
        {
            var p = CorridaPagoDominioTests.Pasivo(x.Proveedor, x.Monto, x.Monto, 0m, 0m, empresa: Empresa);
            p.ActualizarDesdeEvento(x.Monto, x.Monto, "MXN", null, new DateOnly(2026, 11, 15), null, $"FAC-{1000 + i}", Ahora);
            return p;
        }).ToList();

        db.CuentasBancarias.Add(cuenta);
        db.PasivosPendientesPago.AddRange(pasivos);
        await db.SaveChangesAsync();

        var c = await new CrearCorridaHandler(db, new EmpresaContext(), new User(Armador), proveedoresPort, new Clock())
            .Handle(new(cuenta.Id, pasivos.Select(p => p.FacturaProveedorId).ToList(), "Corrida Pagos BBVA"), default);

        c = await new EnviarCorridaAAutorizacionHandler(db, proveedoresPort)
            .Handle(new(c.Id, c.Version), default);

        c = await new AutorizarCorridaHandler(db, new EmpresaContext(), new User(Jefe), new Roles(), proveedoresPort, new Clock())
            .Handle(new(c.Id, c.Version), default);

        c.Estado.Should().Be("Autorizada");
        return (db, c, pasivos);
    }

    [Fact]
    public async Task CA1_corrida_autorizada_de_12_pagos_nacionales_bbva_genera_archivo_con_12_lineas_y_totales_coincidentes()
    {
        // 12 proveedores con CLABE válida
        var proveedoresDict = new Dictionary<Guid, ProveedorBancoDto>();
        var items = new List<(Guid Proveedor, decimal Monto)>();

        for (var i = 1; i <= 12; i++)
        {
            var provId = Guid.NewGuid();
            var monto = 1_000m * i;
            items.Add((provId, monto));
            proveedoresDict[provId] = new ProveedorBancoDto(
                provId,
                $"P-{i:D2}",
                $"Proveedor {i} SA de CV",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", $"Proveedor {i} SA", true, "Activa")]);
        }

        var provPort = new ProveedoresFake(proveedoresDict);
        var (db, corrida, pasivos) = await CrearCorridaAutorizada(provPort, items.ToArray());
        await using var _ = db;

        var handler = new GenerarLayoutCorridaHandler(db, provPort, new User(Jefe), new Clock());
        var res = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);

        // Verificaciones CA1
        res.LineasValidas.Should().Be(12);
        res.LineasExcluidas.Should().Be(0);
        res.Excepciones.Should().BeEmpty();
        res.EsReimpresion.Should().BeFalse();
        res.Banco.Should().Be("BBVA");
        res.Total.Should().Be(corrida.Total);

        var lineasTexto = res.Contenido.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lineasTexto.Should().HaveCount(12);

        // Cada línea incluye importe neto, CLABE, titular, referencia y concepto
        for (var i = 0; i < 12; i++)
        {
            var linea = lineasTexto[i];
            var pasivo = pasivos[i];
            var prov = proveedoresDict[pasivo.ProveedorId];

            linea.Should().Contain(ClabeValida1);
            linea.Should().Contain(pasivo.SaldoPendiente.ToString("0.00"));
            linea.Should().Contain(pasivo.FolioProveedor!);
            linea.Should().Contain(prov.Cuentas[0].Titular);
        }

        // Totales coinciden con la corrida
        var sumaNetosLineas = res.Lineas.Sum(l => l.ImporteNeto);
        sumaNetosLineas.Should().Be(corrida.Total);
        res.Total.Should().Be(corrida.Total);

        // Se guardó en la base de datos con detalle
        var archivoDb = await db.ArchivosLayoutPago.Include(a => a.Lineas).SingleAsync();
        archivoDb.Total.Should().Be(corrida.Total);
        archivoDb.LineasValidas.Should().Be(12);
        archivoDb.Lineas.Should().HaveCount(12);
    }

    [Fact]
    public async Task CA2_proveedor_con_clabe_invalida_sale_del_archivo_y_aparece_en_excepciones_con_causa()
    {
        var provValido1 = Guid.NewGuid();
        var provInvalido = Guid.NewGuid();
        var provValido2 = Guid.NewGuid();

        // provInvalido tiene CLABE con dígito verificador inválido
        var proveedoresDict = new Dictionary<Guid, ProveedorBancoDto>
        {
            [provValido1] = new(provValido1, "P-01", "Proveedor Uno SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Titular Uno", true, "Activa")]),
            [provInvalido] = new(provInvalido, "P-INV", "Proveedor Invalido SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeDigitoInvalido, true, null, "MXN", "Titular Invalido", true, "Activa")]),
            [provValido2] = new(provValido2, "P-02", "Proveedor Dos SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida2, true, null, "MXN", "Titular Dos", true, "Activa")]),
        };

        var provPort = new ProveedoresFake(proveedoresDict);
        var (db, corrida, pasivos) = await CrearCorridaAutorizada(
            provPort,
            (provValido1, 5_000m),
            (provInvalido, 2_500m),
            (provValido2, 3_000m));
        await using var _ = db;

        var handler = new GenerarLayoutCorridaHandler(db, provPort, new User(Jefe), new Clock());
        var res = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);

        // Verificaciones CA2:
        // Sale del archivo y aparece en excepciones con la causa
        res.LineasValidas.Should().Be(2);
        res.LineasExcluidas.Should().Be(1);

        res.Excepciones.Should().ContainSingle();
        var ex = res.Excepciones[0];
        ex.ProveedorId.Should().Be(provInvalido);
        ex.ImporteNeto.Should().Be(2_500m);
        ex.Causa.Should().Contain("dígito verificador");

        // El archivo contiene únicamente los 2 proveedores válidos (total 8,000m)
        var lineasTexto = res.Contenido.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lineasTexto.Should().HaveCount(2);
        res.Total.Should().Be(8_000m);
        res.Contenido.Should().NotContain(ClabeDigitoInvalido);
        res.Contenido.Should().Contain(ClabeValida1);
        res.Contenido.Should().Contain(ClabeValida2);
    }

    [Fact]
    public async Task CA3_archivo_ya_generado_se_entrega_marcado_como_reimpresion_sin_crear_otro_registro()
    {
        var prov = Guid.NewGuid();
        var provPort = new ProveedoresFake(new()
        {
            [prov] = new(prov, "P-01", "Proveedor SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Proveedor SA", true, "Activa")])
        });

        var (db, corrida, _) = await CrearCorridaAutorizada(provPort, (prov, 10_000m));
        await using var _ = db;

        var handler = new GenerarLayoutCorridaHandler(db, provPort, new User(Jefe), new Clock());

        // Primera generación: nuevo registro, no es reimpresión
        var res1 = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);
        res1.EsReimpresion.Should().BeFalse();
        (await db.ArchivosLayoutPago.CountAsync()).Should().Be(1);

        // Segunda llamada con la misma corrida y banco: se entrega marcado como reimpresión y NO crea otro registro
        var res2 = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);
        res2.EsReimpresion.Should().BeTrue();
        res2.ArchivoId.Should().Be(res1.ArchivoId);
        res2.Contenido.Should().Be(res1.Contenido);
        res2.NombreArchivo.Should().Be(res1.NombreArchivo);
        res2.Total.Should().Be(res1.Total);

        // La cantidad de registros de archivo en la base de datos sigue siendo 1 exactamente
        (await db.ArchivosLayoutPago.CountAsync()).Should().Be(1);
        var archivo = await db.ArchivosLayoutPago.SingleAsync();
        archivo.VecesDescargado.Should().Be(2);
    }

    [Theory]
    [InlineData(EstadoCorridaPago.Borrador)]
    [InlineData(EstadoCorridaPago.EnAutorizacion)]
    public async Task CA4_corrida_no_autorizada_no_permite_generar_el_archivo(EstadoCorridaPago estado)
    {
        await using var db = Db();
        var prov = Guid.NewGuid();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var pasivo = CorridaPagoDominioTests.Pasivo(prov, 1_000m, 1_000m, 0m, 0m, empresa: Empresa);
        db.CuentasBancarias.Add(cuenta);
        db.PasivosPendientesPago.Add(pasivo);
        await db.SaveChangesAsync();

        var provPort = new ProveedoresFake(new()
        {
            [prov] = new(prov, "P-01", "Proveedor SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Proveedor SA", true, "Activa")])
        });

        var c = await new CrearCorridaHandler(db, new EmpresaContext(), new User(Armador), provPort, new Clock())
            .Handle(new(cuenta.Id, [pasivo.FacturaProveedorId], "Corrida Borrador"), default);

        if (estado == EstadoCorridaPago.EnAutorizacion)
        {
            c = await new EnviarCorridaAAutorizacionHandler(db, provPort).Handle(new(c.Id, c.Version), default);
        }

        var handler = new GenerarLayoutCorridaHandler(db, provPort, new User(Jefe), new Clock());
        var act = () => handler.Handle(new GenerarLayoutCorridaCommand(c.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);

        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Code.Should().Be("CORRIDA_LAYOUT_NO_AUTORIZADA");

        // Ningún archivo debe haberse guardado
        (await db.ArchivosLayoutPago.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Proveedor_con_cuenta_en_moneda_distinta_o_inactiva_se_lista_en_excepciones()
    {
        var provMxn = Guid.NewGuid();
        var provUsd = Guid.NewGuid();
        var provInactiva = Guid.NewGuid();

        // Al crear la corrida, los proveedores tienen cuentas válidas
        var proveedoresIniciales = new Dictionary<Guid, ProveedorBancoDto>
        {
            [provMxn] = new(provMxn, "P-01", "Proveedor MXN",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Titular 1", true, "Activa")]),
            [provUsd] = new(provUsd, "P-02", "Proveedor USD",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida2, true, null, "MXN", "Titular USD", true, "Activa")]),
            [provInactiva] = new(provInactiva, "P-03", "Proveedor Inactivo",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Titular Inactiva", true, "Activa")]),
        };

        var provPortInicial = new ProveedoresFake(proveedoresIniciales);
        var (db, corrida, _) = await CrearCorridaAutorizada(
            provPortInicial,
            (provMxn, 2_000m),
            (provUsd, 3_000m),
            (provInactiva, 4_000m));
        await using var _ = db;

        // Al momento de generar el layout, provUsd solo tiene cuenta en USD y provInactiva fue inhabilitada en Datos Maestros
        var proveedoresModificados = new Dictionary<Guid, ProveedorBancoDto>
        {
            [provMxn] = proveedoresIniciales[provMxn],
            [provUsd] = new(provUsd, "P-02", "Proveedor USD",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180015678912342", true, null, "USD", "Titular USD", true, "Activa")]),
            [provInactiva] = new(provInactiva, "P-03", "Proveedor Inactivo",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180015678912342", true, null, "MXN", "Titular Inactiva", true, "Inhabilitada")]),
        };

        var provPortModificado = new ProveedoresFake(proveedoresModificados);
        var handler = new GenerarLayoutCorridaHandler(db, provPortModificado, new User(Jefe), new Clock());
        var res = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);

        res.LineasValidas.Should().Be(1);
        res.LineasExcluidas.Should().Be(2);
        res.Total.Should().Be(2_000m);

        res.Excepciones.Should().Contain(e => e.ProveedorId == provUsd && e.Causa.Contains("moneda"));
        res.Excepciones.Should().Contain(e => e.ProveedorId == provInactiva);
    }

    [Fact]
    public async Task Perfil_configurable_permite_personalizar_separador_y_encabezado()
    {
        var prov = Guid.NewGuid();
        var provPort = new ProveedoresFake(new()
        {
            [prov] = new(prov, "P-01", "Proveedor SA",
                [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", ClabeValida1, true, null, "MXN", "Titular SA", true, "Activa")])
        });

        var (db, corrida, pasivos) = await CrearCorridaAutorizada(provPort, (prov, 7_500m));
        await using var _ = db;

        // Configuramos un perfil personalizado con separador de tabulador y encabezado
        var perfilCustom = new PerfilLayoutBanco(
            Empresa,
            "BBVA_SPEI",
            "BBVA",
            "BBVA Custom Tab",
            TipoPagoLayout.NacionalSpei,
            separador: "\t",
            incluyeEncabezado: true);

        db.PerfilesLayoutBanco.Add(perfilCustom);
        await db.SaveChangesAsync();

        var handler = new GenerarLayoutCorridaHandler(db, provPort, new User(Jefe), new Clock());
        var res = await handler.Handle(new GenerarLayoutCorridaCommand(corrida.Id, "BBVA", TipoPagoLayout.NacionalSpei), default);

        var lineasTexto = res.Contenido.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lineasTexto.Should().HaveCount(2); // 1 encabezado + 1 línea de datos
        lineasTexto[0].Should().Be("Cuenta\tImporteNeto\tTitular\tReferencia\tConcepto");
        lineasTexto[1].Should().Contain($"{ClabeValida1}\t7500.00\tTitular SA");
    }

    // ──────────────────────────────────────────────────────── Test doubles

    private sealed class ProveedoresFake(Dictionary<Guid, ProveedorBancoDto> data) : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(data.GetValueOrDefault(id));

        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(
                data.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class User(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Prueba";
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class Roles : IFirmanteRolesReadPort
    {
        public Task<IReadOnlyCollection<string>> CodigosRolAsync(Guid u, Guid e, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>(["jefe-tesoreria"]);
    }
}
