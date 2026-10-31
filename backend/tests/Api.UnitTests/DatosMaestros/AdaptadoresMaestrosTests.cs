using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Endpoints.DatosMaestros.Cargas;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Cargas;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests.DatosMaestros;

public sealed class AdaptadoresMaestrosTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Vacio();
        private sealed class Vacio : IDisposable { public void Dispose() { } }
    }
    private sealed class Permisos : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
    private static async Task ProbarDuplicadosAsync(IAdaptadorCargaMaestros adaptador, FilaMaestros fila)
    {
        var csv = string.Join(',', adaptador.Columnas.Select(c => c.Nombre)) + "\n";
        var texto = string.Join(',', adaptador.Columnas.Select(c => "\"" + fila.Valores[c.Nombre].Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        csv += texto + "\n" + texto;
        using var servicios = new ServiceCollection().BuildServiceProvider();
        var motor = new CargaMaestrosServicio([adaptador], null!, servicios);
        var resultado = await motor.EjecutarAsync(adaptador.Catalogo, Encoding.UTF8.GetBytes(csv), "prueba.csv", false, null, default);
        Assert.Equal(2, resultado.Errores);
        Assert.All(resultado.Renglones, r => Assert.Contains(r.Errores, e => e.Codigo == "DUPLICADO_EN_ARCHIVO"));
    }
    private static CompartidoDbContext Compartido() => new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
    private static CentrosCostoDbContext Centros() => new(new DbContextOptionsBuilder<CentrosCostoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
    private static FilaMaestros Fila(IAdaptadorCargaMaestros adaptador, params (string Campo, string Valor)[] datos)
    {
        var valores = adaptador.Columnas.ToDictionary(c => c.Nombre, _ => "");
        foreach (var (campo, valor) in datos) valores[campo] = valor;
        return new(2, valores);
    }
    [Fact]
    public async Task Alta_cliente_sin_fiscales_y_dim1_de_doce_caracteres_son_invalidos()
    {
        var fiscal = new Millet.Facturacion.Infrastructure.DatosMaestros.ValidacionAltaClienteFiscalAdapter(
            new Millet.Facturacion.UnitTests.TestDoubles.FakeCatalogosSatReadPort());
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => fiscal.ValidarAsync(
            new("", "Cliente ficticio", "", "", "", null), default));
        Assert.Equal("CLIENTE_FISCAL_INVALIDO", error.Code);
        await fiscal.ValidarAsync(new("CMA261029ABC", "Cliente ficticio", "601", "97000", "G03", null, "03", "PUE"), default);
        var validador = new Millet.CentrosCosto.Application.Catalogo.CrearDim1Validator();
        Assert.False((await validador.ValidateAsync(new Millet.CentrosCosto.Application.Catalogo.CrearDim1Command("CC1234567890", "Dimensión ficticia"))).IsValid);
        Assert.True((await validador.ValidateAsync(new Millet.CentrosCosto.Application.Catalogo.CrearDim1Command("CC12345678", "Dimensión ficticia"))).IsValid);
    }
    [Fact]
    public async Task Proveedores_generan_claves_sucesivas_y_multimoneda_limpia_preferencia()
    {
        await using var db = Compartido(); var moneda = new Moneda(Guid.NewGuid(), "MXN", "Peso mexicano"); db.Add(moneda);
        var proveedor = new Proveedor(Guid.NewGuid(), "P000010", "Proveedor ficticio", "XEXX010101000", TipoPersonaProveedor.Moral, claveLegacy: "SAP01", monedaPreferidaId: moneda.Id); db.Add(proveedor); await db.SaveChangesAsync();
        var adaptador = new ProveedoresCarga(db);
        var filas = new[] { Fila(adaptador, ("Clave legacy", "SAP02"), ("Razón social", "Ficticio dos")) with { Numero = 2 }, Fila(adaptador, ("Clave legacy", "SAP03"), ("Razón social", "Ficticio tres")) with { Numero = 3 } };
        await adaptador.PrepararArchivoAsync(new(filas, []), default);
        Assert.Equal("P000011", Assert.IsType<CrearProveedorCommand>((await adaptador.PrepararAsync(filas[0], default)).Comando).Clave);
        Assert.Equal("P000012", Assert.IsType<CrearProveedorCommand>((await adaptador.PrepararAsync(filas[1], default)).Comando).Clave);
        var multimoneda = Fila(adaptador, ("Clave legacy", "SAP01"), ("Moneda", "##"));
        var plan = await adaptador.PrepararAsync(multimoneda, default);
        Assert.True(Assert.IsType<ActualizarProveedorCommand>(plan.Comando).LimpiarMonedaPreferida);
        Assert.Contains(plan.Cambios, c => c.Campo == "Moneda");
    }
    [Fact]
    public async Task Previa_usa_validadores_del_dominio_sin_escribir()
    {
        await using var db = Compartido(); var adaptador = new ProveedoresCarga(db);
        using var servicios = new ServiceCollection().AddSingleton<FluentValidation.IValidator<CrearProveedorCommand>>(new CrearProveedorValidator()).BuildServiceProvider();
        var motor = new CargaMaestrosServicio([adaptador], null!, servicios);
        var csv = "Clave legacy,Razón social,RFC,Email\nPRUEBA,Proveedor ficticio,XEXX010101000,correo-inválido";
        var resultado = await motor.EjecutarAsync("proveedores", Encoding.UTF8.GetBytes(csv), "prueba.csv", false, null, default);
        Assert.Equal(1, resultado.Errores); Assert.Equal("Email", Assert.Single(resultado.Renglones[0].Errores).Columna); Assert.Empty(db.Proveedores); Assert.False(db.ChangeTracker.HasChanges());
    }
    [Fact]
    public async Task Articulos_acepta_csv_exacto_sap_y_mapea_servicio_y_legacy()
    {
        await using var db = Compartido();
        var unidad = new UnidadMedida(Guid.NewGuid(), "PZA", "Pieza", DimensionUnidad.Conteo, 1, 0, true);
        db.Add(unidad); await db.SaveChangesAsync(); var adaptador = new ArticulosCarga(db);
        var csv = "item_code,nombre,unidad_medida,invnt_item,grupo_cod,categoria,ultima_compra\nSAP001,Servicio ficticio,PIEZA,N,100,,2026-01-01";
        var fila = Assert.Single(ArchivoMaestros.Leer(Encoding.UTF8.GetBytes(csv), "sap.csv", adaptador.Columnas).Filas);
        var plan = await adaptador.PrepararAsync(fila, default);
        var alta = Assert.IsType<CrearArticuloCommand>(plan.Comando);
        Assert.Equal(Naturaleza.Servicio, alta.Naturaleza); Assert.Equal("SAP001", alta.ClaveLegacy);
        var articulo = new Articulo(Guid.NewGuid(), "SAP001", "Servicio ficticio", "PZA", Naturaleza.Servicio, unidadMedidaId: unidad.Id);
        db.Add(articulo); await db.SaveChangesAsync();
        Assert.Null((await adaptador.PrepararAsync(fila, default)).Comando);
        fila.Valores["Nombre"] = "Servicio corregido";
        Assert.Contains((await adaptador.PrepararAsync(fila, default)).Cambios, c => c.Campo == "Nombre" && c.Antes == "Servicio ficticio" && c.Despues == "Servicio corregido");
        fila.Valores["Inventariable"] = "otro";
        Assert.Contains("Inventariable", (await Assert.ThrowsAsync<BusinessRuleException>(() => adaptador.PrepararAsync(fila, default))).Code);
        await ProbarDuplicadosAsync(adaptador, fila);
    }
    [Fact]
    public async Task Articulos_previa_reporta_grupos_tipos_duplicados_unidades_y_posible_aw_sin_insertar()
    {
        await using var db = Compartido();
        db.Add(new UnidadMedida(Guid.NewGuid(), "PZA", "Pieza", DimensionUnidad.Conteo, 1, 0, true)); await db.SaveChangesAsync();
        var motor = new CargaMaestrosServicio([new ArticulosCarga(db)], null!, new ServiceCollection().BuildServiceProvider());
        var csv = "item_code,nombre,unidad_medida,invnt_item,grupo_cod\nA1,Uno,PZA,Y,101\nA2,Dos,PZA,N,101\nA3,Tres,CAJA,Y,115\nA4,Cuatro,PZA,Y,143\nA5,Cinco,PZA,Y,101\nA5,Cinco bis,PZA,Y,101";
        var r = await motor.EjecutarAsync("articulos", Encoding.UTF8.GetBytes(csv), "sap.csv", false, null, default);
        int T(string s, string k) => r.Resumen!.SingleOrDefault(c => c.Seccion == s && c.Clave == k)?.Total ?? 0;
        Assert.Equal(4, T("Grupo", "101")); Assert.Equal(1, T("Grupo", "115")); Assert.Equal(1, T("Tipo", "Servicio")); Assert.Equal(5, T("Tipo", "Estándar"));
        Assert.Equal(2, T("Duplicado", "A5")); Assert.Equal(1, T("Unidad sin equivalencia", "CAJA")); Assert.Equal(1, T("Posible A+W", "A4"));
        Assert.Empty(db.Articulos); Assert.False(db.ChangeTracker.HasChanges());
    }
    [Fact]
    public async Task Proveedores_acepta_csv_exacto_sap_sin_bancarios()
    {
        await using var db = Compartido(); var adaptador = new ProveedoresCarga(db);
        var csv = "card_code,group_name,prefijo,razon_social,nombre_comercial,rfc,email,telefono,currency,condiciones_pago_dias\nV001,PROV. EXTRANJEROS,EXT,Proveedor ficticio,,XEXX010101000,prueba@example.test,,##,30";
        var fila = Assert.Single(ArchivoMaestros.Leer(Encoding.UTF8.GetBytes(csv), "sap.csv", adaptador.Columnas).Filas);
        var plan = await adaptador.PrepararAsync(fila, default); var alta = Assert.IsType<CrearProveedorCommand>(plan.Comando);
        Assert.Equal("V001", alta.ClaveLegacy); Assert.True(alta.ActivarCargaSap); Assert.Equal("Proveedor ficticio", alta.NombreComercial);
        db.Add(new Proveedor(Guid.NewGuid(), "V001", "Proveedor ficticio", "XEXX010101000", TipoPersonaProveedor.Moral, claveLegacy: "V001", nombreComercial: "Proveedor ficticio", condicionesPagoDias: 30, email: "prueba@example.test"));
        await db.SaveChangesAsync(); Assert.Null((await adaptador.PrepararAsync(fila, default)).Comando);
        fila.Valores["Razón social"] = "Corregido";
        Assert.Contains((await adaptador.PrepararAsync(fila, default)).Cambios, c => c.Campo == "Razón social");
        fila.Valores["Días de pago"] = "-1";
        Assert.Contains("Días de pago", (await Assert.ThrowsAsync<BusinessRuleException>(() => adaptador.PrepararAsync(fila, default))).Code);
        await ProbarDuplicadosAsync(adaptador, fila);
    }
    [Fact]
    public async Task Clientes_preserva_origen_referencia_y_vacios()
    {
        await using var db = Compartido(); var adaptador = new ClientesCarga(db, new Permisos());
        var clasificacion = new ClasificacionCliente(Guid.NewGuid(), "Prueba", "Ficticia"); db.Add(clasificacion);
        var cliente = new Cliente(Guid.NewGuid(), "C01", "Cliente ficticio", OrigenMaster.Aw, referenciaExterna: "AW001", email: "cliente@example.test"); cliente.Clasificar(clasificacion.Id); db.Add(cliente); await db.SaveChangesAsync();
        var fila = Fila(adaptador, ("Clave", "C01")); Assert.Null((await adaptador.PrepararAsync(fila, default)).Comando);
        fila.Valores["Razón social"] = "Cliente corregido";
        var plan = await adaptador.PrepararAsync(fila, default); Assert.False(plan.Nuevo); Assert.Single(plan.Cambios);
        Assert.Equal("AW001", cliente.ReferenciaExterna); Assert.Equal(OrigenMaster.Aw, cliente.Origen);
        var nueva = Fila(adaptador, ("Clave", "C02"), ("Razón social", "Nuevo"), ("Clasificación", clasificacion.Id.ToString()));
        Assert.True((await adaptador.PrepararAsync(nueva, default)).Nuevo);
        nueva.Valores["Clasificación"] = "inválida";
        Assert.Contains("Clasificación", (await Assert.ThrowsAsync<BusinessRuleException>(() => adaptador.PrepararAsync(nueva, default))).Code);
        await ProbarDuplicadosAsync(adaptador, nueva);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Centros_costo_alta_diff_sin_cambio_y_error(int dimension)
    {
        await using var db = Centros(); var adaptador = new CentrosCostoCarga(db, dimension);
        var dim1 = new Dim1(Guid.NewGuid(), "D1", "Dimensión uno"); var g2 = new GrupoDim2(Guid.NewGuid(), "Grupo dos"); var g3 = new GrupoDim3(Guid.NewGuid(), "Grupo tres");
        var dim2 = new Dim2(Guid.NewGuid(), dim1.Id, "D2", "Dimensión dos", g2.Id);
        db.AddRange(dim1, g2, g3, dim2); await db.SaveChangesAsync();
        var fila = Fila(adaptador, ("Clave", "NUEVO"), ("Nombre", "Nuevo"));
        if (dimension > 1) { fila.Valores["Padre"] = dimension == 2 ? "D1" : "D2"; fila.Valores["Grupo"] = dimension == 2 ? "Grupo dos" : "Grupo tres"; }
        Assert.True((await adaptador.PrepararAsync(fila, default)).Nuevo);
        if (dimension == 1) db.Add(new Dim1(Guid.NewGuid(), "NUEVO", "Nuevo"));
        if (dimension == 2) db.Add(new Dim2(Guid.NewGuid(), dim1.Id, "NUEVO", "Nuevo", g2.Id));
        if (dimension == 3) db.Add(new Dim3(Guid.NewGuid(), dim2.Id, "NUEVO", "Nuevo", g3.Id));
        await db.SaveChangesAsync(); Assert.Null((await adaptador.PrepararAsync(fila, default)).Comando);
        fila.Valores["Nombre"] = "Corregido"; Assert.Single((await adaptador.PrepararAsync(fila, default)).Cambios);
        await ProbarDuplicadosAsync(adaptador, fila);
        fila.Valores["Clave"] = ""; Assert.Contains("Clave", (await Assert.ThrowsAsync<BusinessRuleException>(() => adaptador.PrepararAsync(fila, default))).Code);
    }
    [Fact]
    public void Plan_compara_tasas_por_valor_sin_depender_de_escala_postgres()
    {
        var plan = CargaMaestrosDatos.Plan(new object(), new object(), [], ("Tasa IVA", 0.160000m, 0.16m));
        Assert.Null(plan.Comando);
        Assert.Empty(plan.Cambios);
    }
    [Fact]
    public async Task Fiscal_solo_ficha_conserva_vacios_y_calcula_diff()
    {
        await using var db = Compartido(); var adaptador = new ProductosAwFiscalCarga(db);
        var producto = new ProductoAw(Guid.NewGuid(), "AW001", "Producto ficticio", "PZA"); db.Add(producto); await db.SaveChangesAsync();
        var fila = Fila(adaptador, ("Referencia externa", "AW001"), ("Clave de producto SAT", "301 glass"), ("Clave de unidad SAT", "H87"), ("Objeto de impuesto", "02"), ("Tasa IVA", "0.16"));
        Assert.Contains("Clave de producto SAT", (await Assert.ThrowsAsync<BusinessRuleException>(() => adaptador.PrepararAsync(fila, default))).Code);
        fila.Valores["Clave de producto SAT"] = "30101500";
        var plan = await adaptador.PrepararAsync(fila, default); Assert.NotEmpty(plan.Cambios);
        producto.CapturarExcepcionFiscal(new("30101500", "H87", "02", 0.16m)); await db.SaveChangesAsync();
        Assert.Null((await adaptador.PrepararAsync(fila, default)).Comando);
        fila.Valores["Tasa IVA"] = "0"; Assert.Single((await adaptador.PrepararAsync(fila, default)).Cambios);
        fila.Valores["Clase"] = "Regla"; fila.Valores["Campo"] = "Tipo"; fila.Valores["Referencia externa"] = "VIDRIO";
        Assert.True((await adaptador.PrepararAsync(fila, default)).Nuevo);
        await ProbarDuplicadosAsync(adaptador, fila);
    }
}
