const etiquetas: Record<string, string> = {
  SolicitudCatalogo: 'Solicitud de catálogo',
  ImportacionCatalogo: 'Importación de catálogo',
  FacturaProveedor: 'Factura de proveedor',
  OrdenCompra: 'Orden de compra',
  Requisicion: 'Requisición',
  SesionUsuario: 'Sesión de usuario',
};

export function nombreEntidad(entidad: string): string {
  if (etiquetas[entidad]) return etiquetas[entidad];
  const palabras = entidad
    .replace(/([A-ZÁÉÍÓÚÑ]+)([A-ZÁÉÍÓÚÑ][a-záéíóúñ])/g, '$1 $2')
    .replace(/([a-záéíóúñ0-9])([A-ZÁÉÍÓÚÑ])/g, '$1 $2')
    .toLocaleLowerCase('es-MX');
  return palabras.charAt(0).toLocaleUpperCase('es-MX') + palabras.slice(1);
}

/** Traduce los nombres técnicos y evita repetir la misma entidad en un resumen. */
export function textoActividad(texto: string, entidad: string): string {
  const nombres = new Set([nombreEntidad(entidad)]);
  let resultado = texto.replace(/\b[A-Z][a-z]+(?:[A-Z][a-z0-9]*)+\b/g, (token) => {
    const nombre = nombreEntidad(token);
    nombres.add(nombre);
    return nombre;
  });
  for (const nombre of nombres) {
    const literal = nombre.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    resultado = resultado.replace(new RegExp(`(${literal})(?:\\s+${literal})+`, 'gi'), '$1');
  }
  return resultado;
}
