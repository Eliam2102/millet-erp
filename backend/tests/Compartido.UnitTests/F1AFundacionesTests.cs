using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compartido.UnitTests;

public sealed class F1AFundacionesTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null; public bool IsBypassed => true;
        public IDisposable Bypass() => new Scope(); private sealed class Scope : IDisposable { public void Dispose() { } }
    }
    private sealed class Usuario : ICurrentUserContext { public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "F1A prueba"; }
    private static CompartidoDbContext Db() => new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,new Empresa());
    private static AplicarClienteAwSnapshot ClienteSnapshot(string referencia="F1A-1") => new(referencia,"CLIENTE FICTICIO",DateTime.UtcNow,"1","F1A",MonedaDefault:"MXN");
    [Fact]
    public async Task CA24_alta_sin_clasificacion_repetida_no_duplica_y_actualizacion_conserva_clasificacion()
    {
        using var db=Db(); var servicio=new AplicarClienteAwService(db); var snap=ClienteSnapshot();
        var primero=await servicio.AplicarAsync(snap,CancellationToken.None); Assert.Null(primero.Cliente!.ClasificacionId);
        var clasificacion=Guid.NewGuid(); primero.Cliente.Clasificar(clasificacion); await db.SaveChangesAsync();
        var segundo=await servicio.AplicarAsync(snap with { RazonSocial="NOMBRE OPERATIVO NUEVO",LeidoEnUtc=snap.LeidoEnUtc.AddMinutes(1) },CancellationToken.None);
        Assert.Equal(primero.Cliente.Id,segundo.Cliente!.Id); Assert.Equal(clasificacion,segundo.Cliente.ClasificacionId); Assert.Equal(1,await db.Clientes.CountAsync());
    }
    [Fact]
    public void M21b_validadores_exigen_clasificacion_y_el_catalogo_tiene_baja_logica()
    {
        Assert.False(new ActualizarClienteValidator().Validate(new ActualizarClienteCommand(Guid.NewGuid(),RazonSocial:"Nombre")).IsValid);
        var c=new ClasificacionCliente(Guid.NewGuid(),"Constructora","Por validar con Millet"); c.Editar("Constructora","Por validar con Millet",false); Assert.False(c.Activa);
    }
    [Fact]
    public async Task V38_mismo_rfc_manual_genera_conflicto_idempotente_sin_duplicar_ni_ligar()
    {
        using var db=Db(); var manual=new Cliente(Guid.NewGuid(),"MAN-F1A","CLIENTE FICTICIO",rfc:"AAA010101AAA"); db.Add(manual); await db.SaveChangesAsync();
        var servicio=new AplicarClienteAwService(db); var snap=ClienteSnapshot() with { Rfc=" aaa010101aaa " };
        var r=await servicio.AplicarAsync(snap,CancellationToken.None); Assert.Equal(AplicarClienteAwAccion.Conflicto,r.Accion); Assert.Null(r.Cliente);
        await servicio.AplicarAsync(snap,CancellationToken.None); Assert.Equal(1,await db.Set<ClienteLigadoAw>().CountAsync()); Assert.Equal(1,await db.Clientes.CountAsync()); Assert.Null(manual.ReferenciaExterna);
    }
    [Fact]
    public async Task V38_revision_con_mapeo_conserva_snapshot_y_version_se_compone_una_sola_vez()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "F1A-MAN", "FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();
        var servicio = ServicioConMapeo(db);
        var snap = ClienteSnapshot() with { Rfc = manual.Rfc, EstadoOrigenCrudo = 1 };
        await servicio.AplicarAsync(snap, CancellationToken.None);
        var conflicto = await db.Set<ClienteLigadoAw>().SingleAsync();
        var recibido = System.Text.Json.JsonSerializer.Deserialize<AplicarClienteAwSnapshot>(conflicto.SnapshotJson)!;
        Assert.Equal("F1A", recibido.VersionMapeo);
        manual.LigarAw(recibido.ReferenciaExterna, recibido.Rfc!); await db.SaveChangesAsync();
        await servicio.AplicarAsync(recibido, CancellationToken.None);
        Assert.True(manual.BloqueadoPorAw);
        Assert.Equal("F1A|DEMO-v1", (await db.ClientesSincronizacionAw.SingleAsync()).VersionMapeo);
    }
    [Theory]
    [InlineData("XAXX010101000")]
    [InlineData("XEXX010101000")]
    public async Task V38_rfc_generico_nunca_liga(string rfc)
    {
        using var db=Db(); var manual=new Cliente(Guid.NewGuid(),"MAN-F1A","CLIENTE FICTICIO",rfc:rfc); db.Add(manual); await db.SaveChangesAsync();
        var r=await new AplicarClienteAwService(db).AplicarAsync(ClienteSnapshot() with { Rfc=rfc },CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado,r.Accion); Assert.Equal(2,await db.Clientes.CountAsync()); Assert.Empty(await db.Set<ClienteLigadoAw>().ToListAsync());
        Assert.Throws<BusinessRuleException>(()=>manual.LigarAw("1",rfc));
    }
    private static AplicarClienteAwService ServicioConMapeo(CompartidoDbContext db) => new(db, Options.Create(new MapeoEstatusClientesAwOptions
    {
        Version = "DEMO-v1", KzStatus = new() { [0] = false, [1] = true, [2] = true },
        KzGesperrt = new() { [0] = false, [1] = true }
    }));
    [Fact]
    public async Task V52_bloqueo_y_reactivacion_conservan_id_y_datos_fiscales()
    {
        using var db=Db(); var s=ServicioConMapeo(db); var snap=ClienteSnapshot() with { EstadoOrigenCrudo=0,BloqueoOrigenCrudo=1 };
        var r=await s.AplicarAsync(snap,CancellationToken.None); var id=r.Cliente!.Id; Assert.True(r.Cliente.BloqueadoPorAw); Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo,r.Cliente.Estatus);
        var c=await s.AplicarAsync(snap with { BloqueoOrigenCrudo=0,LeidoEnUtc=snap.LeidoEnUtc.AddMinutes(1) },CancellationToken.None);
        Assert.Equal(id,c.Cliente!.Id); Assert.False(c.Cliente.BloqueadoPorAw); Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Activo,c.Cliente.Estatus);
    }
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(0, null, false)]
    [InlineData(null, 1, true)]
    [InlineData(1, null, true)]
    [InlineData(2, 0, true)]
    public async Task V52_alta_solo_inactiva_por_bloqueo_explicito(int? estado, int? bloqueo, bool inactivo)
    {
        using var db = Db();
        var snapshot = ClienteSnapshot() with { EstadoOrigenCrudo = estado, BloqueoOrigenCrudo = bloqueo };
        var resultado = await ServicioConMapeo(db).AplicarAsync(snapshot, CancellationToken.None);
        Assert.Equal(inactivo, resultado.Cliente!.BloqueadoPorAw);
        Assert.Equal(inactivo ? Millet.Catalogos.Domain.EstatusCatalogo.Inactivo : Millet.Catalogos.Domain.EstatusCatalogo.Activo, resultado.Cliente.Estatus);
    }
    [Fact]
    public async Task V52_lectura_sin_estado_no_modifica_cliente_y_cero_explicito_reactiva()
    {
        using var db = Db(); var servicio = ServicioConMapeo(db);
        var snapshot = ClienteSnapshot() with { Rfc = "AAA010101AAA", EstadoOrigenCrudo = 1 };
        var alta = await servicio.AplicarAsync(snapshot, CancellationToken.None);
        var clasificacion = Guid.NewGuid(); alta.Cliente!.Clasificar(clasificacion); await db.SaveChangesAsync();
        var sinDato = await servicio.AplicarAsync(snapshot with { EstadoOrigenCrudo = null, LeidoEnUtc = snapshot.LeidoEnUtc.AddMinutes(1) }, CancellationToken.None);
        Assert.True(sinDato.Cliente!.BloqueadoPorAw);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo, sinDato.Cliente.Estatus);
        var reactivado = await servicio.AplicarAsync(snapshot with { EstadoOrigenCrudo = null, BloqueoOrigenCrudo = 0, LeidoEnUtc = snapshot.LeidoEnUtc.AddMinutes(2) }, CancellationToken.None);
        Assert.Equal(alta.Cliente.Id, reactivado.Cliente!.Id);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Activo, reactivado.Cliente.Estatus);
        Assert.Equal("AAA010101AAA", reactivado.Cliente.Rfc); Assert.Equal(clasificacion, reactivado.Cliente.ClasificacionId);
        Assert.Single(await db.Clientes.ToListAsync());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(7, 9)]
    public async Task V52_sin_mapeo_solo_registra_crudos_y_no_inactiva_ni_reactiva(int? estado, int? bloqueo)
    {
        using var db = Db(); var servicio = new AplicarClienteAwService(db);
        var snap = ClienteSnapshot() with { EstadoOrigenCrudo = estado, BloqueoOrigenCrudo = bloqueo };
        var alta = await servicio.AplicarAsync(snap, CancellationToken.None);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Activo, alta.Cliente!.Estatus);
        Assert.False(alta.Cliente.BloqueadoPorAw);
        alta.Cliente.AplicarBloqueoAw(true); await db.SaveChangesAsync();
        await servicio.AplicarAsync(snap with { LeidoEnUtc = snap.LeidoEnUtc.AddMinutes(1) }, CancellationToken.None);
        Assert.True(alta.Cliente.BloqueadoPorAw);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo, alta.Cliente.Estatus);
        var registro = await db.ClientesSincronizacionAw.SingleAsync();
        Assert.Equal(estado, registro.EstadoOrigenCrudo); Assert.Equal(bloqueo, registro.BloqueoOrigenCrudo);
        Assert.Equal("F1A", registro.VersionMapeo);
    }

    [Fact]
    public async Task V52_cambio_de_version_reprocesa_mismo_origen_y_baja_local_no_se_reactiva()
    {
        using var db = Db(); var snap = ClienteSnapshot() with { EstadoOrigenCrudo = 42 };
        var alta = await new AplicarClienteAwService(db).AplicarAsync(snap, CancellationToken.None);
        var mapeo = new MapeoEstatusClientesAwOptions { Version = "DEMO-v1", KzStatus = new() { [42] = true } };
        var bloqueado = await new AplicarClienteAwService(db, Options.Create(mapeo)).AplicarAsync(snap, CancellationToken.None);
        Assert.Equal(alta.Cliente!.Id, bloqueado.Cliente!.Id); Assert.True(bloqueado.Cliente.BloqueadoPorAw);
        Assert.Equal("F1A|DEMO-v1", (await db.ClientesSincronizacionAw.SingleAsync()).VersionMapeo);
        mapeo = new() { Version = "DEMO-v2", KzStatus = new() { [42] = false } };
        var activo = await new AplicarClienteAwService(db, Options.Create(mapeo)).AplicarAsync(snap, CancellationToken.None);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Activo, activo.Cliente!.Estatus);
        Assert.Equal("F1A|DEMO-v2", (await db.ClientesSincronizacionAw.SingleAsync()).VersionMapeo);
        activo.Cliente.CambiarEstatus(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo); await db.SaveChangesAsync();
        await new AplicarClienteAwService(db, Options.Create(mapeo)).AplicarAsync(snap, CancellationToken.None);
        Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo, activo.Cliente.Estatus);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(0, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(42, null, true)]
    [InlineData(0, 9, true)]
    [InlineData(7, 0, null)]
    [InlineData(7, 9, true)]
    public void V52_mapeo_no_deduce_codigos_y_bloqueo_aprobado_prevalece(int? estado, int? bloqueo, bool? esperado)
    {
        var mapeo = new MapeoEstatusClientesAwOptions
        { Version = "DEMO-v1", KzStatus = new() { [0] = false, [42] = true }, KzGesperrt = new() { [0] = false, [9] = true } };
        Assert.Equal(esperado, mapeo.Resolver(estado, bloqueo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("demasiado-larga")]
    [InlineData("v|1")]
    public void V52_mapeo_configurado_exige_version_valida(string? version)
    {
        using var db = Db();
        Assert.Throws<OptionsValidationException>(() => new AplicarClienteAwService(db,
            Options.Create(new MapeoEstatusClientesAwOptions { Version = version, KzStatus = new() { [1] = true } })));
    }
    [Fact]
    public async Task U19ba_regla_de_familia_y_sin_regla_no_bloquean_alta()
    {
        using var db=Db(); var regla=new ReglaFiscalAw(Guid.NewGuid(),"Tipo","Templado DEMO","DEMO",new("30171706","H87","02",0.16m)); db.Add(regla); await db.SaveChangesAsync();
        var s=new AplicarProductoAwService(db); var snap=new AplicarProductoAwSnapshot("F1A-P","PRODUCTO FICTICIO","PZA",false,[],DateTime.UtcNow,"1","F1A",SoloCrear:true,Tipo:"Templado DEMO");
        var r=await s.AplicarAsync(snap,CancellationToken.None); Assert.Equal(regla.Id,r.Producto!.ReglaFiscalId); Assert.Equal(OrigenFiscalProducto.ReglaFamilia,r.Producto.OrigenFiscal); Assert.Equal(EstadoFiscalProducto.PorReglaSinRevisar,r.Producto.EstadoFiscal);
        var sin=await s.AplicarAsync(snap with { ReferenciaExterna="F1A-SIN",Tipo="Proceso" },CancellationToken.None);
        Assert.Equal(EstadoFiscalProducto.FiscalPendiente,sin.Producto!.EstadoFiscal); Assert.Null(sin.Producto.ClaveProdServSat);
    }
    [Fact]
    public async Task U19bd_actualizar_regla_conserva_excepcion_y_registra_historial()
    {
        using var db=Db(); var regla=new ReglaFiscalAw(Guid.NewGuid(),"Tipo","VTE","DEMO",new("30171706","H87","02",0.16m));
        var heredero=new ProductoAw(Guid.NewGuid(),"F1A-H","FICTICIO H","PZA"); heredero.AplicarReglaFiscal(regla);
        var excepcion=new ProductoAw(Guid.NewGuid(),"F1A-E","FICTICIO E","PZA"); excepcion.AplicarReglaFiscal(regla); excepcion.CapturarExcepcionFiscal(new("30171708","H87","02",0m));
        db.AddRange(regla,heredero,excepcion); await db.SaveChangesAsync();
        await new GuardarReglaFiscalAwHandler(db,new Usuario()).Handle(new(regla.Id,"Tipo","VTE","DEMO nueva",new("30171710","MTK","02",0.16m),true,regla.Version),CancellationToken.None);
        Assert.Equal("30171710",heredero.ClaveProdServSat); Assert.Equal("30171708",excepcion.ClaveProdServSat); Assert.Equal(0m,excepcion.TasaIvaTraslado);
        Assert.Equal(2,await db.Set<CambioFiscalAw>().CountAsync());
    }
    [Fact]
    public void U19be_confirmar_exige_ficha_completa_y_registra_usuario_fecha()
    {
        var p=new ProductoAw(Guid.NewGuid(),"F1A","FICTICIO","PZA"); Assert.Throws<BusinessRuleException>(()=>p.ConfirmarFiscal(Guid.NewGuid(),DateTimeOffset.UtcNow));
        p.CapturarExcepcionFiscal(new("30171706","H87","08",0m)); var usuario=Guid.NewGuid(); var ahora=DateTimeOffset.UtcNow;
        p.ConfirmarFiscal(usuario,ahora); Assert.Equal(usuario,p.ConfirmadoPor); Assert.Equal(ahora,p.ConfirmadoEn); Assert.Equal(EstadoFiscalProducto.Confirmada,p.EstadoFiscal);
    }
    [Fact]
    public void CSV_acepta_comas_comillas_y_saltos_de_linea_y_rechaza_comillas_sin_cerrar()
    {
        var filas=CsvFiscalAw.Leer("a,b\r\n\"texto, con comas\",\"doble \"\"comilla\"\"\nsegunda línea\"\r\n");
        Assert.Equal(2,filas.Count); Assert.Equal("texto, con comas",filas[1][0]); Assert.Equal("doble \"comilla\"\nsegunda línea",filas[1][1]);
        Assert.Throws<BusinessRuleException>(()=>CsvFiscalAw.Leer("\"sin cierre"));
    }
    private sealed class FiscalAlta : IValidacionAltaClienteFiscal
    {
        public AltaClienteFiscal? Recibido { get; private set; }
        public Task ValidarAsync(AltaClienteFiscal datos, CancellationToken ct) { Recibido = datos; return Task.CompletedTask; }
    }
    [Fact]
    public void Cliente_normaliza_rfc()
    {
        Assert.Equal("AAA010101AAA", new Cliente(Guid.NewGuid(), "C-1", "X", rfc: " aaa010101aaa ").Rfc);
        Assert.Null(new Cliente(Guid.NewGuid(), "C-2", "X", rfc: "   ").Rfc);
    }

    [Fact]
    public async Task M22a_alta_normaliza_rfc_y_ofrece_existente_sin_crear_segundo_cliente()
    {
        using var db = Db();
        var clasificacion = new ClasificacionCliente(Guid.NewGuid(), "FICTICIA", "Prueba F1A");
        db.Add(clasificacion); await db.SaveChangesAsync();
        var fiscal = new FiscalAlta(); var handler = new CrearClienteHandler(db, fiscal);
        var command = new CrearClienteCommand("F1A-MAN", "CLIENTE FICTICIO", null, " aaa010101aaa ", "601", "97000", "G03", "03", "PUE", "MXN", false, null, null, ClasificacionId: clasificacion.Id);
        var alta = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("AAA010101AAA", fiscal.Recibido!.Rfc);
        Assert.Equal(OrigenMaster.Manual, (await db.Clientes.SingleAsync()).Origen);
        var error = await Assert.ThrowsAsync<ClienteRfcDuplicadoException>(() => handler.Handle(command with { Clave = "OTRA" }, CancellationToken.None));
        Assert.Equal(alta.Id, Assert.Single(error.Clientes).Id);
        Assert.Equal(1, await db.Clientes.CountAsync());
    }
    [Fact]
    public async Task V38_coincidencia_manual_con_referencia_previa_tambien_requiere_revision()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "F1A-MAN", "FICTICIO", referenciaExterna: "F1A-1", rfc: "AAA010101AAA");
        var clasificacion = Guid.NewGuid(); manual.Clasificar(clasificacion); db.Add(manual); await db.SaveChangesAsync();
        var resultado = await new AplicarClienteAwService(db).AplicarAsync(ClienteSnapshot() with { Rfc = "AAA010101AAA" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Conflicto, resultado.Accion);
        Assert.Single(await db.Set<ClienteLigadoAw>().ToListAsync()); Assert.Equal(OrigenMaster.Manual, manual.Origen);
        var id = manual.Id; manual.LigarAw("F1A-1", "AAA010101AAA");
        Assert.Equal(id, manual.Id); Assert.Equal(clasificacion, manual.ClasificacionId); Assert.Equal(OrigenMaster.Aw, manual.Origen);
    }
    [Fact]
    public async Task Nueva_regla_completa_pendientes_y_edicion_producto_registra_actor_y_excepcion()
    {
        using var db = Db(); var usuario = new Usuario();
        var p = new ProductoAw(Guid.NewGuid(), "F1A-R", "FICTICIO", "PZA"); p.AplicarClasificacion(null, "GRUPO FICTICIO", "Proceso");
        db.Add(p); await db.SaveChangesAsync();
        var regla = await new GuardarReglaFiscalAwHandler(db, usuario).Handle(new(null, "Grupo", "GRUPO FICTICIO", "DEMO", new("30171706", "H87", "02", 0.16m), true, null), CancellationToken.None);
        Assert.Equal(regla, p.ReglaFiscalId); Assert.Equal(EstadoFiscalProducto.PorReglaSinRevisar, p.EstadoFiscal);
        await new ActualizarProductoAwHandler(db, usuario).Handle(new(p.Id, TasaIvaTraslado: 0m, VersionEsperada: p.Version), CancellationToken.None);
        Assert.Equal(OrigenFiscalProducto.ExcepcionProducto, p.OrigenFiscal);
        Assert.Equal(usuario.UserId, (await db.Set<CambioFiscalAw>().SingleAsync(c => c.Accion == "Capturar excepción")).UsuarioId);
    }
    [Fact]
    public void Regla_solo_completa_vacios_y_no_pisa_excepcion_fiscal_previa()
    {
        var p = new ProductoAw(Guid.NewGuid(), "F1A-MIX", "FICTICIO", "PZA", claveProdServSat: "30171708", tasaRetencionIva: 0.04m);
        var regla = new ReglaFiscalAw(Guid.NewGuid(), "Tipo", "VTE", "DEMO", new("30171706", "H87", "02", 0.16m));
        p.AplicarReglaFiscal(regla, soloVacios: true);
        Assert.Equal("30171708", p.ClaveProdServSat); Assert.Equal(0.04m, p.TasaRetencionIva);
        Assert.Equal("H87", p.ClaveUnidadSat); Assert.Equal(OrigenFiscalProducto.ExcepcionProducto, p.OrigenFiscal);
    }

    [Fact]
    public async Task V52_relectura_del_hash_anterior_reclasifica_pendiente_como_aplicado()
    {
        using var db = Db(); var servicio = ServicioConMapeo(db);
        var snap = ClienteSnapshot() with { EstadoOrigenCrudo = 1 };
        var resultado = await servicio.AplicarAsync(snap, CancellationToken.None);
        var registro = await db.ClientesSincronizacionAw.SingleAsync();
        // Simula el resultado previo a V52, conservando hash y versión de mapeo.
        db.Entry(registro).Property(r => r.Resultado).CurrentValue = ResultadoSincronizacionAw.Pendiente;
        await db.SaveChangesAsync();
        var nuevo = await servicio.AplicarAsync(snap with { LeidoEnUtc = snap.LeidoEnUtc.AddMinutes(1) }, CancellationToken.None);
        Assert.Equal(resultado.Cliente!.Id, nuevo.Cliente!.Id);
        Assert.Equal(ResultadoSincronizacionAw.Aplicado, nuevo.Resultado);
        Assert.Equal(ResultadoSincronizacionAw.Aplicado, registro.Resultado);
    }

    [Fact]
    public async Task M79_reactivar_cliente_bloqueado_por_aw_es_422()
    {
        using var db = Db();
        var cliente = new Cliente(Guid.NewGuid(), "MAN-R", "FICTICIO", rfc: "AAA010101AAA");
        cliente.AplicarBloqueoAw(true);
        db.Add(cliente); await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new ReactivarClienteHandler(db).Handle(new(cliente.Id), CancellationToken.None));
        Assert.Equal("CLIENTE_BLOQUEADO_POR_AW", ex.Code);
    }

    [Theory]
    [InlineData("abc-010101-ab1", "ABC010101AB1")]
    [InlineData("ABC 010101 AB1", "ABC010101AB1")]
    [InlineData("ABCD010101AB1", "ABCD010101AB1")]
    [InlineData("XAXX010101000", null)]
    [InlineData("XEXX-010101-000", null)]
    [InlineData("DEMO005", null)]
    [InlineData("123456789012", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void V38_clave_de_ligado(string? entrada, string? esperado)
        => Assert.Equal(esperado, RfcCliente.ClaveLigado(entrada));

    [Fact]
    public async Task V38_buscar_por_clave_encuentra_por_rfc_y_por_ust_id_y_excluye_el_propio()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "MAN-F1A", "CLIENTE FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();
        var aw = await new AplicarClienteAwService(db).AplicarAsync(ClienteSnapshot("AW-1") with { CandidatoFiscalUstId = "bbb-010101-bbb" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, aw.Accion);
        Assert.Null(aw.Cliente!.Rfc);
        var awId = aw.Cliente.Id;
        await db.SaveChangesAsync();

        var claves = new[] { "AAA010101AAA", "BBB010101BBB" };
        var todos = await ClientesPorRfc.BuscarAsync(db, claves, null, CancellationToken.None);
        Assert.Equal(2, todos.Count);
        Assert.Contains(todos, x => x.Clave == "AAA010101AAA" && x.Cliente.Id == manual.Id);
        Assert.Contains(todos, x => x.Clave == "BBB010101BBB" && x.Cliente.Id == awId);

        var sinPropio = await ClientesPorRfc.BuscarAsync(db, claves, awId, CancellationToken.None);
        Assert.Single(sinPropio);
    }

    [Fact]
    public async Task V38_ust_id_con_la_clave_de_un_manual_queda_en_conflicto_sin_copiar_rfc()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "MAN-F1A", "CLIENTE FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();
        var r = await new AplicarClienteAwService(db).AplicarAsync(ClienteSnapshot("AW-1") with { Rfc = null, CandidatoFiscalUstId = "aaa-010101-aaa" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Conflicto, r.Accion);
        Assert.Equal(1, await db.Clientes.CountAsync());
        Assert.Equal("AAA010101AAA", (await db.Set<ClienteLigadoAw>().SingleAsync()).Rfc);
    }

    [Fact]
    public async Task V38_ust_id_sin_forma_de_rfc_no_liga()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "MAN-F1A", "CLIENTE FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();
        var r = await new AplicarClienteAwService(db).AplicarAsync(ClienteSnapshot("AW-1") with { CandidatoFiscalUstId = "DEMO005" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, r.Accion);
        Assert.Equal(2, await db.Clientes.CountAsync());
        Assert.Equal(0, await db.Set<ClienteLigadoAw>().CountAsync());
    }

    [Fact]
    public async Task Provisionar_desde_pedido_con_rfc_ya_usado_crea_sin_rfc_y_conserva_el_candidato()
    {
        using var db = Db();
        var handler = new ProvisionarClienteDesdeAwHandler(new AplicarClienteAwService(db));
        ProvisionarClienteDesdeAwCommand Cmd(string r) => new(r, "CLIENTE FICTICIO", "AAA010101AAA", null, null, null, null, null, "MXN");
        var primero = await handler.Handle(Cmd("61"), CancellationToken.None);
        var segundo = await handler.Handle(Cmd("62"), CancellationToken.None);

        Assert.Equal("AAA010101AAA", primero.Rfc);
        Assert.Null(segundo.Rfc);
        Assert.Equal("AAA010101AAA", (await db.Set<ClienteSincronizacionAw>().SingleAsync(x => x.ClienteId == segundo.ClienteId)).CandidatoFiscalUstId);
        Assert.Contains(await db.Set<CambioMaestroAw>().ToListAsync(), c => c.EntidadId == segundo.ClienteId && c.Tipo == TipoCambioMaestroAw.RfcCompartido);
    }

    [Fact]
    public async Task V38_conflicto_obsoleto_se_elimina_y_se_crea_el_cliente()
    {
        using var db = Db();
        var manual = new Cliente(Guid.NewGuid(), "MAN-F1A", "CLIENTE FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();
        var servicio = new AplicarClienteAwService(db);
        var primero = await servicio.AplicarAsync(ClienteSnapshot("AW-1") with { Rfc = "AAA010101AAA" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Conflicto, primero.Accion);
        Assert.Equal(1, await db.Set<ClienteLigadoAw>().CountAsync());

        manual.ActualizarDatos(rfc: "BBB010101BBB"); await db.SaveChangesAsync();
        var segundo = await servicio.AplicarAsync(ClienteSnapshot("AW-1") with { Rfc = "AAA010101AAA", LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, segundo.Accion);
        Assert.Equal(0, await db.Set<ClienteLigadoAw>().CountAsync());
    }

    [Fact]
    public async Task V38_no_completa_rfc_que_ya_tiene_otro_cliente()
    {
        using var db = Db();
        var servicio = new AplicarClienteAwService(db);
        var primero = await servicio.AplicarAsync(ClienteSnapshot("AW-1"), CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, primero.Accion);
        var manual = new Cliente(Guid.NewGuid(), "MAN-F1A", "CLIENTE FICTICIO", rfc: "AAA010101AAA");
        db.Add(manual); await db.SaveChangesAsync();

        var segundo = await servicio.AplicarAsync(ClienteSnapshot("AW-1") with { Rfc = "AAA010101AAA", LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Actualizado, segundo.Accion);
        Assert.Null((await db.Clientes.SingleAsync(c => c.ReferenciaExterna == "AW-1")).Rfc);
        Assert.Equal(1, await db.Clientes.CountAsync(c => c.Rfc == "AAA010101AAA"));
    }

    [Fact]
    public async Task V38_dos_clientes_aw_con_el_mismo_rfc_no_duplican_el_campo_rfc_y_el_ligado_entre_aw_esta_diferido()
    {
        using var db = Db();
        var servicio = new AplicarClienteAwService(db);
        var uno = await servicio.AplicarAsync(ClienteSnapshot("AW-1") with { Rfc = "AAA010101AAA" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, uno.Accion);
        Assert.Equal("AAA010101AAA", uno.Cliente!.Rfc);
        var dos = await servicio.AplicarAsync(ClienteSnapshot("AW-2") with { Rfc = "AAA010101AAA" }, CancellationToken.None);
        Assert.Equal(AplicarClienteAwAccion.Creado, dos.Accion);
        Assert.Null(dos.Cliente!.Rfc);
        Assert.Equal(2, await db.Clientes.CountAsync());
        Assert.Equal(0, await db.Set<ClienteLigadoAw>().CountAsync());
        Assert.Equal(1, await db.Clientes.CountAsync(c => c.Rfc == "AAA010101AAA"));
    }

}
