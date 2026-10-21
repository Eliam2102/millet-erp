import { createContext, useContext } from 'react';
export const NuevoActivoContext = createContext({ abrir: () => {} });
export const useNuevoActivo = () => useContext(NuevoActivoContext);
