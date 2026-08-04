using Millet.Compras.Application.Historico;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Compras.UnitTests.Application;

/// <summary>
/// Tests del mapeo de entradas de audit_log a HistoricoTipo (B.2). Las
/// strings JSON de Cambios reflejan el shape real que serializa
/// AuditSaveChangesInterceptor.
/// </summary>
public class HistoricoTipoMapperTests
{
    [Fact]
    public void Crear_Requisicion_EsTipo_Creada()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Requisicion",
            Operacion = "crear",
            Cambios = """{ "snapshot": { "Estado": 0 } }""",
        };

        Assert.Equal(HistoricoTipo.Creada, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Theory]
    [InlineData(1, HistoricoTipo.Transmitida)]            // EnAutorizacion
    [InlineData(3, HistoricoTipo.CubrimientoRegistrado)]  // EnSurtido
    [InlineData(4, HistoricoTipo.Cerrada)]                // Cerrada
    [InlineData(5, HistoricoTipo.Cancelada)]              // Cancelada
    [InlineData(6, HistoricoTipo.Rechazada)]              // Rechazada
    [InlineData(7, HistoricoTipo.Eliminada)]              // Eliminada
    public void Update_Requisicion_Estado_Mapea_A_Tipo_Correcto(int nuevoEstado, HistoricoTipo esperado)
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Requisicion",
            Operacion = "actualizar",
            Cambios = $$"""{ "diff": { "Estado": { "antes": 0, "despues": {{nuevoEstado}} } } }""",
        };

        Assert.Equal(esperado, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Update_Requisicion_Sin_Cambio_De_Estado_Es_Cambio_Generico()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Requisicion",
            Operacion = "actualizar",
            Cambios = """{ "diff": { "Descripcion": { "antes": "a", "despues": "b" } } }""",
        };

        Assert.Equal(HistoricoTipo.Cambio, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Crear_Linea_EsTipo_LineaAgregada()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "LineaRequisicion",
            Operacion = "crear",
            Cambios = """{ "snapshot": {} }""",
        };

        Assert.Equal(HistoricoTipo.LineaAgregada, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Borrar_Linea_EsTipo_LineaEliminada()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "LineaRequisicion",
            Operacion = "borrar",
            Cambios = """{ "snapshot_pre_borrado": {} }""",
        };

        Assert.Equal(HistoricoTipo.LineaEliminada, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Update_Linea_Con_CantidadRecibida_EsTipo_RecepcionRegistrada()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "LineaRequisicion",
            Operacion = "actualizar",
            Cambios = """{ "diff": { "CantidadRecibida": { "antes": 0, "despues": 5 } } }""",
        };

        Assert.Equal(HistoricoTipo.RecepcionRegistrada, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Update_Linea_Con_CantidadDeAlmacen_EsTipo_CubrimientoRegistrado()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "LineaRequisicion",
            Operacion = "actualizar",
            Cambios = """{ "diff": { "CantidadDeAlmacen": { "antes": 0, "despues": 10 } } }""",
        };

        Assert.Equal(HistoricoTipo.CubrimientoRegistrado, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Update_Linea_Estructural_EsTipo_LineaActualizada()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "LineaRequisicion",
            Operacion = "actualizar",
            Cambios = """{ "diff": { "Cantidad": { "antes": 1, "despues": 2 } } }""",
        };

        Assert.Equal(HistoricoTipo.LineaActualizada, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Crear_Autorizacion_Nivel1_EsTipo_AutorizadaN1()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Autorizacion",
            Operacion = "crear",
            Cambios = """{ "snapshot": { "Nivel": 1 } }""",
        };

        Assert.Equal(HistoricoTipo.AutorizadaN1, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Crear_Autorizacion_Nivel2_EsTipo_AutorizadaN2()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Autorizacion",
            Operacion = "crear",
            Cambios = """{ "snapshot": { "Nivel": 2 } }""",
        };

        Assert.Equal(HistoricoTipo.AutorizadaN2, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void Entidad_Desconocida_EsTipo_Cambio_Generico()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "AlgoQueNoMapeamos",
            Operacion = "crear",
            Cambios = """{}""",
        };

        Assert.Equal(HistoricoTipo.Cambio, HistoricoTipoMapper.InferirTipo(entry));
    }

    [Fact]
    public void JSON_Malformado_No_Lanza_Y_Devuelve_Cambio_Generico()
    {
        var entry = new AuditLogEntry
        {
            Entidad = "Requisicion",
            Operacion = "actualizar",
            Cambios = "<<not json>>",
        };

        Assert.Equal(HistoricoTipo.Cambio, HistoricoTipoMapper.InferirTipo(entry));
    }
}
