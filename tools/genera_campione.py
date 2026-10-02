"""Genera samples/demo.dxf: una piastra quotata con layer, blocchi, testi e varie entità.

Uso: python tools/genera_campione.py   (richiede: pip install ezdxf)
"""
import math
from pathlib import Path

import ezdxf
from ezdxf.enums import TextEntityAlignment

doc = ezdxf.new("R2018", setup=True)
doc.header["$INSUNITS"] = 4  # millimetri
msp = doc.modelspace()

doc.layers.add("CONTORNO", color=7)
doc.layers.add("FORI", color=4)
doc.layers.add("ASSI", color=1, linetype="CENTER")
doc.layers.add("QUOTE", color=3)
doc.layers.add("TESTI", color=2)
doc.layers.add("NASCOSTO", color=6).off()

# Contorno: polilinea chiusa con due raccordi (bulge).
r = 20
bulge = math.tan(math.radians(90) / 4)
msp.add_lwpolyline(
    [(0, 0, 0), (200 - r, 0, bulge), (200, r, 0), (200, 120, 0), (r, 120, bulge), (0, 120 - r, 0)],
    format="xyb", close=True, dxfattribs={"layer": "CONTORNO"},
)

# Blocco "FORO": cerchio con croce d'asse; il cerchio è sul layer 0, quindi prende layer e colore dell'inserimento.
foro = doc.blocks.new("FORO", base_point=(0, 0))
foro.add_circle((0, 0), 6)
foro.add_line((-9, 0), (9, 0), dxfattribs={"layer": "ASSI"})
foro.add_line((0, -9), (0, 9), dxfattribs={"layer": "ASSI"})
for x in (30, 100, 170):
    msp.add_blockref("FORO", (x, 30), dxfattribs={"layer": "FORI"})
msp.add_blockref("FORO", (100, 90), dxfattribs={"layer": "FORI", "xscale": 2, "yscale": 2, "rotation": 45})

# Asola: due archi e due linee.
msp.add_arc((40, 90), 8, 90, 270, dxfattribs={"layer": "FORI"})
msp.add_arc((70, 90), 8, 270, 90, dxfattribs={"layer": "FORI"})
msp.add_line((40, 98), (70, 98), dxfattribs={"layer": "FORI"})
msp.add_line((40, 82), (70, 82), dxfattribs={"layer": "FORI"})

# Ellisse e spline.
msp.add_ellipse((150, 90), major_axis=(20, 0), ratio=0.5, dxfattribs={"layer": "FORI", "color": 5})
msp.add_spline([(120, 60), (135, 70), (150, 55), (165, 70), (180, 60)], dxfattribs={"layer": "CONTORNO", "color": 30})

# Quote.
quota = {"dimtxt": 3.5, "dimasz": 3, "dimblk": "OPEN30", "dimlfac": 1, "dimdec": 0, "dimexe": 2, "dimexo": 1, "dimgap": 1}
for base, p1, p2, angle in (((0, -20), (0, 0), (200, 0), 0), ((220, 0), (200, 0), (200, 120), 90)):
    dim = msp.add_linear_dim(base=base, p1=p1, p2=p2, angle=angle, override=quota, dxfattribs={"layer": "QUOTE"})
    dim.render()

# Testi.
msp.add_text("PIASTRA 200x120 sp.10", height=6, dxfattribs={"layer": "TESTI"}).set_placement((0, 135))
msp.add_text("Ruotato 30°", height=4, rotation=30, dxfattribs={"layer": "TESTI"}).set_placement((120, 10))
msp.add_text("Centrato", height=4, dxfattribs={"layer": "TESTI"}).set_placement(
    (100, 60), align=TextEntityAlignment.MIDDLE_CENTER)
msp.add_mtext("Materiale: S235\nFinitura: zincata", dxfattribs={"layer": "TESTI", "char_height": 3.5}).set_location((230, 120))

msp.add_point((100, 60), dxfattribs={"layer": "ASSI"})
msp.add_line((0, 0), (200, 120), dxfattribs={"layer": "NASCOSTO"})
msp.add_line((-10, 60), (210, 60), dxfattribs={"layer": "ASSI"})

out = Path(__file__).resolve().parent.parent / "samples" / "demo.dxf"
doc.saveas(out)
print(f"Scritto {out}")
