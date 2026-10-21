"""Extrae exclusivamente la hoja fuente; sin dependencias externas."""
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


def extraer(path):
    ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    with zipfile.ZipFile(path) as z:
        strings = [''.join(n.itertext()) for n in ET.fromstring(z.read('xl/sharedStrings.xml'))]
        sheets = ET.fromstring(z.read('xl/workbook.xml')).find('s:sheets', ns)
        sheet = next(s for s in sheets if s.attrib['name'] == 'Plan de cuentas (5)')
        rid = sheet.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id']
        rel = next(r for r in ET.fromstring(z.read('xl/_rels/workbook.xml.rels')) if r.attrib['Id'] == rid)
        target = rel.attrib['Target']
        xml = ET.fromstring(z.read(target.lstrip('/') if target.startswith('/') else 'xl/' + target))
        rows = []
        for row in xml.findall('.//s:row', ns):
            cells = {}
            for c in row.findall('s:c', ns):
                v = c.find('s:v', ns)
                value = v.text if v is not None else ''
                cells[re.sub(r'\d', '', c.attrib['r'])] = strings[int(value)] if c.get('t') == 's' and value else value
            code, name = cells.get('A', ''), cells.get('B', '')
            if re.match(r'^(1[56]\d|170|171|613)\.', code) or code.startswith(('505.01.', '505.02.')) or ('rdida' in name.lower() and ('venta' in name.lower() or 'baja' in name.lower())):
                rows.append({'numero': code, 'nombre': name})
    assert len({r['numero'] for r in rows}) == len(rows)
    assert sum(r['numero'].startswith(tuple(str(n) + '.01.' for n in range(151, 171))) for r in rows) == 13
    return rows


if __name__ == '__main__':
    print(json.dumps(extraer(Path(sys.argv[1] if len(sys.argv) > 1 else 'CATALOGO_MILLET.xlsx')), ensure_ascii=False, indent=2))
