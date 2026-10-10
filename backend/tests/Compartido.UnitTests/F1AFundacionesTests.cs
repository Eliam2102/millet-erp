using Microsoft.EntityFrameworkCore;
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
    [Fact]
    public async Task V52_bloqueo_y_reactivacion_conservan_id_y_datos_fiscales()
    {
        using var db=Db(); var s=new AplicarClienteAwService(db); var snap=ClienteSnapshot() with { EstadoOrigenCrudo=0,BloqueoOrigenCrudo=1 };
        var r=await s.AplicarAsync(snap,CancellationToken.None); var id=r.Cliente!.Id; Assert.True(r.Cliente.BloqueadoPorAw); Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo,r.Cliente.Estatus);
        var c=await s.AplicarAsync(snap with { BloqueoOrigenCrudo=0,LeidoEnUtc=snap.LeidoEnUtc.AddMinutes(1) },CancellationToken.None);
        Assert.Equal(id,c.Cliente!.Id); Assert.False(c.Cliente.BloqueadoPorAw); Assert.Equal(Millet.Catalogos.Domain.EstatusCatalogo.Activo,c.Cliente.Estatus);
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
        using var db = Db(); var servicio = new AplicarClienteAwService(db);
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

}
