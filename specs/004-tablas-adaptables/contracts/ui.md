# UI contract
PaginacionTabla conserva total/offset/limit/onChange(offset,limit). Selector, modo automático y primera/última/página directa accesibles; texto de rango sin negativos. Prop explícita permite tablas servidor con límite menor. Hook común devuelve visibles,offset,limit,onChange/cambiar y mantiene imports de módulos.
ListadoModulo: filtros/tabs/pie fuera del cuerpo con scroll, ancho disponible y alto útil medido. Edición/captura no se desmonta por resize. Tablas anchas desplazan columnas dentro del contenedor.
