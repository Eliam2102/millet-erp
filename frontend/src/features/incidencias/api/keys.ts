export interface FiltrosMias {
  estado?: string[];
  modulo?: string;
  offset: number;
  limit: number;
}

export interface FiltrosDocumento {
  modulo: string;
  tipoDocumento: string;
  documentoId: string;
}

export const incidenciasKeys = {
  all: ['incidencias'] as const,
  mias: (f: FiltrosMias) => ['incidencias', 'mias', f] as const,
  resumen: () => ['incidencias', 'mias', 'resumen'] as const,
  documento: (f: FiltrosDocumento) => ['incidencias', 'documento', f] as const,
  detalle: (id: string) => ['incidencias', 'detalle', id] as const,
  pasos: () => ['incidencias', 'pasos'] as const,
};
