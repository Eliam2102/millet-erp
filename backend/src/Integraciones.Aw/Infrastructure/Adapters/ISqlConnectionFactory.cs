using System.Data.Common;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Abstracción de creación de conexiones SQL para el adapter
/// <see cref="HybridConnectionAwSqlReader"/>. Permite que los tests
/// unit del reader mockeen toda la capa SQL sin tener que reemplazar
/// el adapter entero (un fake retorna un <see cref="DbConnection"/>
/// fake; el código de parsing/error-mapping del reader se prueba intacto).
///
/// <para>
/// Implementación productiva: <see cref="SqlConnectionFactory"/> que
/// retorna <c>Microsoft.Data.SqlClient.SqlConnection</c> contra la
/// connection string de <c>ConnectionStrings:AwSqlServer</c>.
/// </para>
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>
    /// Crea una conexión NO abierta. El caller hace <c>OpenAsync</c>
    /// dentro del bloque <c>using</c>; el pooling de SqlClient maneja
    /// la eficiencia entre llamadas.
    /// </summary>
    DbConnection CreateConnection();
}
