import { z } from 'zod';

// .NET y la siembra usan GUID 8-4-4-4-12 sin versión/variante RFC;
// la validación UUID estricta de Zod rechaza esos identificadores.
export const zId = (mensaje?: string) => z.string().guid(mensaje);
