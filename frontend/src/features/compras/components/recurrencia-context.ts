import { createContext } from 'react';

/** Indicador recibido de la bandeja; undefined cuando el servidor no lo aporta. */
export const RecurrenciaContext = createContext<boolean | undefined>(undefined);
