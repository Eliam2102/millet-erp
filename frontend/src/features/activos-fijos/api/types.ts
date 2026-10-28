export interface Custodia {
  responsableId: string;
  sucursalId: string;
  ubicacion: string;
  centroCostoId: string;
  equipoId: string | null;
}
export interface DatosActivo {
  activoMejoradoId?: string | null;
  vidaUtilMeses?: number | null;
  descripcion: string;
  claseId: string;
  marca: string | null;
  modelo: string | null;
  numeroSerie: string | null;
  fechaAdquisicion: string;
  inicioUso: string | null;
  valorMonedaOriginal: number;
  moneda: string;
  tipoCambio: number;
  custodia: Custodia;
  importacion: number;
  pedimento: string | null;
  arrendado: boolean;
  documentoSoporte: string | null;
}
export interface CustodiaDescripcion {
  responsable: string | null;
  sucursal: string | null;
  centroCosto: string | null;
  equipo: string | null;
}
export interface Activo extends Omit<DatosActivo, 'custodia'>, Custodia {
  custodiaDescripcion?: CustodiaDescripcion;
  id: string;
  numero: string;
  estado: number;
  version: number;
  valorOriginal: number;
  valorActual: number;
  valorEnLibros: number;
  depreciacionAcumulada: number;
  enTraslado: boolean;
  ordenCompraId: string | null;
  recepcionId: string | null;
  facturaProveedorId: string | null;
  polizaAltaId: string | null;
  historial: {
    id: string;
    fecha: string;
    importe: number;
    descripcion: string;
    facturaId: string;
    polizaId: string;
  }[];
  traslados: {
    id: string;
    origenDescripcion?: CustodiaDescripcion;
    destinoDescripcion?: CustodiaDescripcion;
    origen: Custodia;
    destino: Custodia;
    registradoEn: string;
    registradoPor: string;
    registradoPorNombre?: string | null;
    motivo: string;
    fechaEfectiva: string;
    aceptadoEn: string | null;
  }[];
}
export interface Clase {
  id: string;
  version: number;
  clave: string;
  nombre: string;
  tasaFiscalAnual: number | null;
  tasaContableAnual?: number | null;
  depreciable: boolean;
  tasaConfirmada: boolean;
  requiereRevision: boolean;
  cuentaActivoId: string | null;
  cuentaDepreciacionId: string | null;
  cuentaGastoId: string | null;
  cuentaPuenteId: string | null;
  activa: boolean;
}
export const estadoActivo = ['Por capitalizar', 'Activo', 'Dado de baja'] as const;
export const dinero = (valor: number) =>
  new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(valor);
