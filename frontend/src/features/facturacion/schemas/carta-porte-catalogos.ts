import { z } from 'zod';

/**
 * Schemas Zod de los catálogos de Carta Porte (vehículos/operadores)
 * para la página de administración (/admin/carta-porte-catalogos).
 * Mirror de <c>CrearVehiculoValidator</c> / <c>CrearOperadorValidator</c>
 * backend.
 *
 * <para>Números con <c>z.number()</c> + <c>valueAsNumber</c>/<c>setValueAs</c>
 * en el register (mismo patrón que <c>CartaPorteSchema</c>).
 * <c>pesoBrutoVehicular</c> en TONELADAS — el SAT lo exige para timbrar
 * CP 3.1, pero el catálogo lo admite vacío (el builder de emisión lo
 * exige al timbrar). La placa y el RFC son inmutables al editar (identidad
 * del renglón); el form los deshabilita en modo edición.</para>
 */
export const VehiculoSchema = z.object({
  placa: z
    .string()
    .trim()
    .min(1, 'Placa requerida')
    .max(20, 'Máximo 20 caracteres'),
  configVehicular: z
    .string()
    .trim()
    .min(1, 'Configuración vehicular requerida')
    .max(10, 'Máximo 10 caracteres'),
  anioModelo: z
    .number({ message: 'Año modelo numérico' })
    .int('Año modelo entero')
    .min(1990, 'Entre 1990 y 2100')
    .max(2100, 'Entre 1990 y 2100'),
  pesoBrutoVehicular: z
    .number({ message: 'Peso numérico' })
    .positive('Debe ser mayor que 0')
    .nullable(),
  tipoPermisoSct: z.string().trim().max(10, 'Máximo 10 caracteres').optional(),
  numPermisoSct: z.string().trim().max(50, 'Máximo 50 caracteres').optional(),
  aseguradora: z.string().trim().max(100, 'Máximo 100 caracteres').optional(),
  polizaSeguro: z.string().trim().max(50, 'Máximo 50 caracteres').optional(),
});

export type VehiculoValues = z.infer<typeof VehiculoSchema>;

export const OperadorSchema = z.object({
  rfc: z
    .string()
    .trim()
    .min(12, 'RFC de 12 o 13 caracteres')
    .max(13, 'RFC de 12 o 13 caracteres'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  numLicencia: z
    .string()
    .trim()
    .min(1, 'Número de licencia requerido')
    .max(50, 'Máximo 50 caracteres'),
});

export type OperadorValues = z.infer<typeof OperadorSchema>;
