import { z } from 'zod';

/**
 * Schema del form "Nueva Carta Porte" (3.1, FE-F8). Tipo "T" (traslado,
 * sin ingreso) o "I" (ingreso: el flete se factura). Mercancías inline.
 * Vehículo y operador se seleccionan con pickers de los catálogos
 * administrables (/admin/carta-porte-catalogos); el form guarda el id.
 */
export const CartaPorteMercanciaSchema = z.object({
  descripcion: z.string().min(1, 'Describe la mercancía').max(1000),
  bienesTransp: z.string().min(1, 'Clave SAT de bienes transportados').max(10),
  claveUnidad: z.string().min(1, 'Clave de unidad').max(10),
  cantidad: z.number().positive('La cantidad debe ser mayor a 0'),
  pesoEnKg: z.number().positive('El peso debe ser mayor a 0'),
  materialPeligroso: z.boolean(),
});

export const CartaPorteSchema = z.object({
  tipoCfdi: z.enum(['T', 'I']),
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  // Receptor
  receptorRfc: z.string().min(12, 'RFC del receptor').max(13),
  receptorNombre: z.string().min(1, 'Nombre / razón social').max(254),
  receptorRegimenFiscal: z.string().min(1, 'Régimen fiscal del receptor').max(5),
  receptorCodigoPostal: z.string().min(5, 'Código postal').max(10),
  receptorUsoCfdi: z.string().min(1, 'Uso CFDI').max(5),
  receptorPais: z.string().min(3, 'País').max(5),
  // Emisor
  rfcEmisor: z.string().min(12, 'RFC del emisor').max(13),
  regimenFiscalEmisor: z.string().min(1, 'Régimen fiscal del emisor').max(5),
  moneda: z.string().length(3, 'Código ISO de 3 letras'),
  // Tramo
  origen: z.string().min(1, 'Origen del tramo').max(254),
  destino: z.string().min(1, 'Destino del tramo').max(254),
  // F12-PR3: domicilio SAT de las ubicaciones (CP 3.1 exige Domicilio en
  // Origen/Destino para timbrar real).
  origenCodigoPostal: z.string().regex(/^\d{5}$/, 'CP de 5 dígitos'),
  origenEstado: z
    .string()
    .regex(/^[A-Za-z]{3}$/, 'Clave SAT c_Estado de 3 letras (ej. QUE)'),
  destinoCodigoPostal: z.string().regex(/^\d{5}$/, 'CP de 5 dígitos'),
  destinoEstado: z
    .string()
    .regex(/^[A-Za-z]{3}$/, 'Clave SAT c_Estado de 3 letras (ej. QUE)'),
  distanciaKm: z.number().positive('La distancia debe ser mayor a 0'),
  vehiculoId: z.string().uuid('Selecciona un vehículo'),
  operadorId: z.string().uuid('Selecciona un operador'),
  fechaSalida: z.string().min(1, 'Fecha de salida requerida'),
  fechaLlegadaEstimada: z.string().min(1, 'Fecha de llegada requerida'),
  // Tipo I: servicio facturado
  montoServicio: z.number().min(0, 'El monto no puede ser negativo'),
  tasaIvaServicio: z.number().min(0).max(1).nullable(),
  mercancias: z.array(CartaPorteMercanciaSchema).min(1, 'Agrega al menos una mercancía'),
});

export type CartaPorteValues = z.infer<typeof CartaPorteSchema>;

/**
 * Schema de "Crear siguiente tramo" (FE-F8-PR2). Hereda receptor/emisor y
 * mercancías del tramo previo; solo captura el nuevo tramo. POST a
 * <c>/carta-porte/{previaId}/siguiente-tramo</c>.
 */
export const SiguienteTramoSchema = z.object({
  tipoCfdi: z.enum(['T', 'I']),
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  origen: z.string().min(1, 'Origen del tramo').max(254),
  destino: z.string().min(1, 'Destino del tramo').max(254),
  // F12-PR3: el tramo nuevo tiene origen/destino propios — domicilio SAT.
  origenCodigoPostal: z.string().regex(/^\d{5}$/, 'CP de 5 dígitos'),
  origenEstado: z
    .string()
    .regex(/^[A-Za-z]{3}$/, 'Clave SAT c_Estado de 3 letras (ej. QUE)'),
  destinoCodigoPostal: z.string().regex(/^\d{5}$/, 'CP de 5 dígitos'),
  destinoEstado: z
    .string()
    .regex(/^[A-Za-z]{3}$/, 'Clave SAT c_Estado de 3 letras (ej. QUE)'),
  distanciaKm: z.number().positive('La distancia debe ser mayor a 0'),
  vehiculoId: z.string().uuid('Selecciona un vehículo'),
  operadorId: z.string().uuid('Selecciona un operador'),
  fechaSalida: z.string().min(1, 'Fecha de salida requerida'),
  fechaLlegadaEstimada: z.string().min(1, 'Fecha de llegada requerida'),
  montoServicio: z.number().min(0, 'El monto no puede ser negativo'),
  tasaIvaServicio: z.number().min(0).max(1).nullable(),
});

export type SiguienteTramoValues = z.infer<typeof SiguienteTramoSchema>;
