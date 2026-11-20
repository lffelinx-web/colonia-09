"""Bake the colony's civil works and the matching touch/traffic layout.

Run with Python and Pillow. Coordinates are measured on the 1536 x 1024
terrain plate from the upper left; one original isometric tile spans 26 px.
The eight logical sectors and their saved tile addresses stay in CampaignData.
"""
import heapq
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw


OUT = Path(__file__).resolve().parents[2] / "Assets/Resources/Island"
W, H, TILE = 1536, 1024, 26
CENTERS = [(516, 800), (725, 665), (1175, 270), (1244, 487),
           (326, 326), (292, 589), (1005, 790), (685, 416)]
# Lots are 2 x 2 tiles, four per quarter. Zones with fewer than sixteen lots
# (CampaignData / ColonyData.LotsFor) turn their outer corners into gardens,
# in the same order as CampaignData.GardenCorners. The landmark stands on its
# own plaza six tiles behind the zone center, outside the lots.
LOTS = [12, 12, 14, 14, 14, 14, 16, 16]
GARDENS = [(3.5, 3.5), (3.5, -3.5), (-3.5, 3.5), (-3.5, -3.5)]
LANDMARK = -6
# Intersections between districts. Both the road painting and the moving
# cargo use these same curves, including the bridge across the eastern river.
NODES = CENTERS + [(501, 480), (853, 505), (991, 369), (997, 618), (519, 612)]
EDGES = [
    (0, 1, [(592, 762), (632, 713)]), (0, 5, [(444, 764), (365, 695), (359, 623)]),
    (0, 6, [(587, 835), (736, 838), (882, 851)]),
    (1, 12, [(663, 634), (602, 611)]), (1, 11, [(799, 628), (884, 608)]),
    (1, 6, [(800, 702), (880, 728)]), (2, 10, [(1107, 304), (1053, 331)]),
    (2, 3, [(1243, 304), (1301, 352), (1323, 420), (1301, 458)]),
    (3, 11, [(1175, 522), (1127, 565), (1086, 585)]),
    (3, 10, [(1176, 453), (1124, 421), (1056, 402)]),
    (4, 8, [(390, 358), (429, 405)]), (4, 7, [(396, 291), (480, 289), (565, 351)]),
    (5, 8, [(357, 556), (417, 531)]), (5, 12, [(359, 622), (428, 637)]),
    (6, 11, [(1070, 758), (1105, 704), (1080, 662)]),
    (7, 8, [(620, 449), (566, 470)]), (7, 9, [(750, 448), (796, 477)]),
    (7, 10, [(751, 383), (836, 352), (919, 336)]),
    (8, 12, [(487, 532), (493, 572)]), (9, 11, [(907, 538), (941, 582)]),
]
S = 3
art = Image.new("RGBA", (W*S, H*S))
draw = ImageDraw.Draw(art)
rng = random.Random(913)


def pixels(pts):
    return [(round(x*S), round(y*S)) for x, y in pts]


def line(pts, fill, width=1):
    draw.line(pixels(pts), fill, width=max(1, round(width*S)), joint="curve")


def poly(pts, fill):
    draw.polygon(pixels(pts), fill)


def oval(x, y, rx, ry, fill):
    draw.ellipse(pixels([(x-rx, y-ry), (x+rx, y+ry)]), fill)


def spline(knots):
    p = [knots[0], *knots, knots[-1]]
    curve = []
    for i in range(1, len(p)-2):
        a,b,c,d = p[i-1:i+3]
        steps = max(2, math.ceil(math.dist(b,c)/3))
        for k in range(steps):
            t = k/steps
            curve.append(tuple(.5*(2*b[j]+(-a[j]+c[j])*t+(2*a[j]-5*b[j]+4*c[j]-d[j])*t*t+
                                      (-a[j]+3*b[j]-3*c[j]+d[j])*t*t*t) for j in (0,1)))
    return curve+[knots[-1]]


def markers(pts, pitch, length):
    carry = 0
    for a,b in zip(pts,pts[1:]):
        dx,dy=b[0]-a[0],b[1]-a[1]
        dist=math.hypot(dx,dy)
        if dist == 0:
            continue
        while carry < dist:
            x,y=a[0]+dx*carry/dist,a[1]+dy*carry/dist
            yield (x,y),(x+dx*length/dist,y+dy*length/dist)
            carry+=pitch
        carry-=dist


def road(p, wide=8.0):
    line([(x+1,y+2) for x,y in p], "#4c5047", wide+3)
    line(p, "#9da596", wide+2.4)
    line(p, "#677578", wide+.5)
    line(p, "#34434a", wide-1.0)
    for a,b in markers(p, 9, 3):
        line([a,b], "#c1b783", .7)


def bridge(a,b):
    # Elevated deck, visible piers, stanchions and the near-side handrail.
    for (x,y),_ in markers([a,b], 20, 1):
        poly([(x-3,y),(x+2,y+1),(x+2,y+14),(x-2,y+15)], "#45575b")
        line([(x-2,y+1),(x-2,y+13)], "#8c9994", 1.3)
    road([a,b], 10)
    dx,dy=b[0]-a[0],b[1]-a[1]
    dist=math.hypot(dx,dy)
    for side in (-1,1):
        ux,uy=-dy/dist*5*side,dx/dist*5*side
        railing=[(x+ux,y+uy) for x,y in [a,b]]
        for (x,y),_ in markers(railing,8,1):
            line([(x,y),(x,y-3.6)], "#859c9e", 1)
        line([(x,y-3.6) for x,y in railing], "#c1ccc1", .9)


def prism(x,y,w,d,h,top,left,right):
    a,b,c,e=(x-w,y),(x,y-d),(x+w,y),(x,y+d)
    poly([a,e,(e[0],e[1]-h),(a[0],a[1]-h)],left)
    poly([e,c,(c[0],c[1]-h),(e[0],e[1]-h)],right)
    poly([(px,py-h) for px,py in [a,b,c,e]],top)


def tree(x,y,size,blossom=False):
    oval(x+size*.45,y,size*.89,size*.32,"#626c50")
    line([(x,y),(x,y-size*1.25)],"#655c4d",size*.25)
    colors=("#ad7280","#ce9198","#dfadb1") if blossom else ("#4d6c4d","#72905b","#96a969")
    for dx,dy,r,col in [(0,-1.2,.91,colors[0]),(-.35,-1.42,.65,colors[1]),(.28,-1.59,.61,colors[1]),(-.19,-1.75,.48,colors[2])]:
        oval(x+dx*size,y+dy*size,r*size,r*size*.82,col)


def ellipse_path(x,y,rx,ry):
    return [(x+rx*math.cos(k*math.pi/60),y+ry*math.sin(k*math.pi/60)) for k in range(121)]


def landscaping():
    # An orbital walk around the civic precinct and pocket gardens along its edge.
    x,y=CENTERS[7]
    loop=ellipse_path(x,y,141,68)
    for width,col in [(6,"#677462"),(4.1,"#b6b9a1"),(2.7,"#969f8e")]:
        line(loop,col,width)
    for angle in (20,36,55,73,112,133,152,192,213,234,294,312,335):
        a=math.radians(angle)
        px,py=x+137*math.cos(a),y+66*math.sin(a)
        oval(px,py,6,2.7,"#929b79")
        tree(px,py,3.8,angle in (36,112,213,294))
    # Rows of planted crop beds on the agricultural terrace, with a service path.
    for x,y in [(1270,553),(1340,521)]:
        for j in range(5):
            a=(x+j*4.4,y+j*2.0); b=(a[0]+40,a[1]-19)
            line([a,b],"#827959",3.3)
            line([(a[0],a[1]-.65),(b[0],b[1]-.65)],"#acb64f" if j%2 else "#a1ab4d",2.2)
    # Pipes with couplers from the water precinct to the river intake.
    pipe=spline([(1124,293),(1087,314),(1066,317),(1043,303),(1010,294)])
    line([(px+1,py+2) for px,py in pipe],"#485c53",4.4)
    line(pipe,"#7f9993",3.8)
    line([(px,py-.65) for px,py in pipe],"#abcdba",1)
    for (px,py),_ in markers(pipe,10,1):
        oval(px,py,1.65,1.6,"#718886")
    prism(1008,293,7,3.5,4,"#a2afa2","#637b76","#495f62")
    # Quarry haul track and material stockpiles inside the excavation.
    road(spline([(296,313),(277,302),(280,286),(328,267),(350,269)]),3.8)
    for px,py in [(322,278),(332,278),(340,273)]:
        prism(px,py,4.6,2.4,2.1,"#ab9b7b","#776d58","#8b8268")
    # The port's mooring apron, freight containers and bollards.
    for px,py in [(1148,863),(1160,869),(1172,874),(1184,880),(1197,885)]:
        line([(px,py),(px,py-3.4)],"#7e9391",1.65)
        oval(px,py-3.4,1.1,.65,"#f0d58d")
    for i,(px,py) in enumerate([(1194,884),(1203,888),(1213,891),(1210,883)]):
        prism(px,py,5.3,2.5,3.5,"#bd8c4b" if i%2 else "#a26c54","#735a4b","#77513f")
    hull=[(1180,909),(1187,903),(1204,908),(1221,917),(1218,922),(1202,919)]
    poly([(px+1,py+2) for px,py in hull],"#2e6169")
    poly(hull,"#bec8ba")
    poly([(1187,907),(1191,906),(1214,916),(1215,919),(1209,918)],"#4e666b")
    prism(1205,915,5,2.3,4,"#d8dacc","#7f9692","#617c7e")
    line([(1205,909),(1205,904)],"#acbdb5",.9)


paths = {}
adj = {n: [] for n in range(len(NODES))}
for a,b,via in EDGES:
    p=spline([NODES[a],*via,NODES[b]])
    paths[a,b], paths[b,a] = p, list(reversed(p))
    distance=sum(math.dist(x,y) for x,y in zip(p,p[1:]))
    adj[a].append((distance,b))
    adj[b].append((distance,a))
    road(p)

# Viaducts follow the exact painted spline rather than cutting across a bend.
for a,b,lo,hi in [(7,10,.79,.99),(3,10,.50,.77),(9,11,.48,.75),(6,11,.28,.53),(0,5,.25,.43)]:
    p=paths[a,b]
    span=p[int(len(p)*lo):int(len(p)*hi)+1]
    for i in range(0,len(span)-1,5):
        bridge(span[i],span[min(i+5,len(span)-1)])


def shortest(start,end):
    queue=[(0,start,[])]
    visited=set()
    while queue:
        dist,n,route=heapq.heappop(queue)
        if n in visited:
            continue
        visited.add(n)
        if n==end:
            return route
        for step,m in adj[n]:
            heapq.heappush(queue,(dist+step,m,route+[(n,m)]))
    raise ValueError("Unreachable district")


def district(n):
    cx,cy=CENTERS[n]
    def at(u,v):
        return cx+(u-v)*TILE*.5, cy+(u+v)*TILE*.25
    def shape(uv,col):
        poly([at(u,v) for u,v in uv],col)
    def slab(u,v,r,col,cut=.1):
        shape([(u-r+cut,v-r),(u+r-cut,v-r),(u+r,v-r+cut),(u+r,v+r-cut),
               (u+r-cut,v+r),(u-r+cut,v+r),(u-r,v+r-cut),(u-r,v-r+cut)],col)
    def pavement(r,cut):
        return [at(u,v) for u,v in [(-r+cut,-r),(r-cut,-r),(r,-r+cut),(r,r-cut),(r-cut,r),(-r+cut,r),(-r,r-cut),(-r,-r+cut),(-r+cut,-r)]]
    # A planted boulevard around each group of blocks. The central district has
    # an oval civic walk; the port and works yard use hardstanding instead.
    rim=pavement(4.78,1.15 if n in (2,4,5,6) else 2.5)
    # Industrial docks wrap around a yard. Inland neighborhoods are open to
    # the hillside; paving ends at the individual blocks rather than a slab.
    if n in (5,6):
        poly([(x,y+2) for x,y in rim],"#51594f")
        poly(rim,"#77847b" if n==5 else "#7c8582")
        road(rim,4.8)
    elif n in (0,1):
        road(rim[1:6] if n==0 else rim[3:8],4.0)
    elif n==2:
        road(rim[4:8],3.7)
    elif n==4:
        road(rim[:4],4.7)
    for u in (-2.43,2.43):
        for v in (-2.43,2.43):
            slab(u,v,1.91,"#7d8878",.46)
            slab(u,v,1.83,"#a7ac95",.40)
            slab(u,v,1.74,"#78846c" if n in (0,1,3,7) else "#7d8983",.31)
    # Survey plots show fine curbs, landscaping and material variation, while
    # leaving the whole addressable area free for real player buildings.
    colors=[("#80905d","#869461"),("#909d6b","#8b9767"),("#788c82","#81958c"),("#80904e","#8b974e"),
            ("#8f8b73","#999279"),("#84908a","#909890"),("#899591","#929d99"),("#899b68","#91a578")][n]
    gardens=GARDENS[:16-LOTS[n]]
    for k,u in enumerate((-3.5,-1.5,1.5,3.5)):
        for j,v in enumerate((-3.5,-1.5,1.5,3.5)):
            if (u,v) in gardens:
                slab(u,v,.95,"#6f8a4f",.22)
                slab(u,v,.84,"#5f8443",.18)
                for du,dv in [(-.45,-.3),(.35,-.45),(-.1,.25),(.45,.4),(-.5,.5)]:
                    x,y=at(u+du,v+dv)
                    tree(x,y,2.4,rng.random()<.25)
                continue
            slab(u,v,.95,colors[(k+j)%2],.20)
            if n==3:
                for i in range(7):
                    line([at(u-.7+i*.23,v-.75),at(u-.7+i*.23,v+.75)],"#b0ae6b",.67)
            elif n in (4,5,6):
                line([at(u+.85,v+.25),at(u+.85,v+.85),at(u+.25,v+.85)],"#c8bf91",.72)
            else:
                for _ in range(8):
                    x,y=at(u+rng.uniform(-.75,.75),v+rng.uniform(-.75,.75))
                    oval(x,y,.47,.25,"#a9b28a")
    for u,v in [(4.55,0),(0,4.55)]:
        road([at(-u,-v),at(u,v)],6.4)
    for k in (-3.65,3.65):
        for j in range(4):
            t=-.23+j*.15
            line([at(k-.09,t),at(k+.09,t)],"#d3d4bf",1.2)
            line([at(t,k-.09),at(t,k+.09)],"#d3d4bf",1.2)
    # Landmark precinct on its own plaza behind the far corner, joined to it by a short walk.
    L=LANDMARK
    o=L+2
    slab(L+1.1,L+1.1,.75,"#8f978c",.25)
    x,y=at(L,L)
    if n in (0,7):
        for radius,color in [(1.5,"#5e7272"),(1.4,"#b3baa5"),(1.3,"#738582"),(1.12,"#9ca79b")]:
            oval(x,y,TILE*radius,TILE*radius*.5,color)
        for angle in range(0,360,30):
            a=math.radians(angle)
            px,py=x+math.cos(a)*TILE*1.43,y+math.sin(a)*TILE*.72
            oval(px,py,1.15,.60,"#e0c884")
    elif n==2:
        for r,col in [(1.5,"#76908a"),(1.36,"#bcc0ac"),(1.22,"#4e999d")]:
            slab(L,L,r,col,.45)
    elif n==3:
        for j in range(7):
            u=o-3.5+j*.48
            line([at(u,o-3.6),at(u,o-.6)],"#9da26a",2.8)
            line([at(u,o-3.6),at(u,o-.6)],"#81a549",1.65)
    elif n==1:
        for j in (o-3.3,o-.8):
            line([at(j,o-3.5),at(j,o-.55)],"#c2bda5",2.9)
        for j in range(4):
            px,py=at(o-3.46,o-3.0+j*.72)
            tree(px,py,2.5,j%2==0)
    else:
        slab(L,L,1.45,"#979889" if n!=4 else "#ac9c7f",.4)
        for j in range(6):
            line([at(o-3.3+j*.36,o-.63),at(o-3.04+j*.36,o-.93)],"#d4bd81",1.25)
    # Short shade trees just outside the corners; every tile inside is a lot.
    for u,v in [(-4,4),(4,-4),(4,4)]:
        x,y=at(u*1.22,v*1.22)
        for dx,dy in [(-3,1),(2,1.8),(.5,-1)]:
            oval(x+dx+1,y+dy+1,2.6,1,"#596650")
            line([(x+dx,y+dy),(x+dx,y+dy-3)],"#544c3d",1)
            oval(x+dx,y+dy-3,2.4,2.4,"#456648")
            oval(x+dx-.65,y+dy-4,1.65,1.2,"#7e995b")


# Cargo apron out to sea, connected directly to the port street.
pier=[(1005,790),(1060,817.5),(1130,850),(1220,890)]
for (x,y),_ in markers(pier,20,1):
    line([(x,y),(x,y+9)],"#495e64",3)
road(pier,13)
road([(1170,900),(1220,890),(1244,872)],12)
landscaping()
for n in range(len(CENTERS)):
    district(n)

def point(p):
    return {"x": round(p[0],2), "y": round(p[1],2)}

links=[]
for n in range(7):
    pts=[]
    for edge in shortest(n,n+1):
        pts.extend(paths[edge][:-1])
    pts.append(CENTERS[n+1])
    links.append({"points":[point(p) for p in pts]})
OUT.mkdir(parents=True,exist_ok=True)
art.resize((4095,2730),Image.Resampling.LANCZOS).save(OUT/"DistrictWorks.png",optimize=True)
(OUT/"IslandLayout.json").write_text(json.dumps({"width":W,"height":H,"pixelsPerTile":TILE,
    "centers":[point(p) for p in CENTERS],"links":links},separators=(",",":")),encoding="utf-8")
