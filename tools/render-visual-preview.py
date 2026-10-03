#!/usr/bin/env python3
"""Rasterize the isolated source-sliced fixture. Pillow is a presentation renderer, not Direct2D."""
from pathlib import Path
import argparse,json,math
from PIL import Image, ImageDraw, ImageFont
ap=argparse.ArgumentParser();ap.add_argument('folder',type=Path)
ap.add_argument('--outline-mode',choices=['per-stroke','union'],default='union')
ap.add_argument('--prefix',default='scratchhead-clean-reference')
a=ap.parse_args(); data=json.loads((a.folder/'scratchhead-poses.json').read_text())
W,H=1120,660; SUP=2
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'; BOLD='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
def font(s,b=False): return ImageFont.truetype(BOLD if b else FONT,round(s*SUP))
# Figure.Draw.cs ordering, Outline alpha .28; slim-body Seg uses round-capped lines.
bones=[(1,2),(1,3),(3,4),(1,5),(5,6),(2,7),(7,8),(2,9),(9,10)]
draw_order=[(1,5,True),(5,6,True),(2,9,True),(9,10,True),(1,2,False),(2,7,False),(7,8,False),(1,3,False),(3,4,False)]
INK=(229,57,53,255); FAR=(165,41,38,255); OUT=(0,0,0,71)  # Palette.Red, Figure.Draw far multiplier .72
frames=[]
for idx,f in enumerate(data['frames']):
 im=Image.new('RGB',(W*SUP,H*SUP),(15,22,28)); d=ImageDraw.Draw(im)
 def txt(x,y,s,size=18,c='#aabcc8',bold=False):d.text((x*SUP,y*SUP),s,font=font(size,bold),fill=c)
 txt(36,24,'DOODLEFOLK  /  ANIMATION PASS 01',17,'#82d6b5',True)
 txt(36,59,'A gentler reach, scratch, and release',30,'#ecf2f5',True)
 txt(36,105,'Same source motion • 1.4-second fidget • same 120 Hz simulation • same camera',16)
 for panel,label,key,col in [(0,'BEFORE  /  abrupt target changes','before','#ffbd78'),(1,'AFTER  /  eased reach + return','after','#82d6b5')]:
  x=36+panel*548; txt(x+10,153,label,19,col,True)
  d.rounded_rectangle((x*SUP,192*SUP,(x+500)*SUP,527*SUP),radius=12*SUP,fill='#f3f0e7')
  d.line(((x+30)*SUP,485*SUP,(x+470)*SUP,485*SUP),fill='#cecbbf',width=2*SUP)
  scale=4.3*SUP; ox=(x+250)*SUP; oy=484*SUP
  pts=[(ox+p[0]*scale,oy+p[1]*scale) for p in f[key]]
  # A single mask applies outline opacity once across the figure silhouette. Separately
  # compositing translucent strokes darkens their joint overlaps (28% + 28% -> ~48%).
  # The optional old mode is retained solely for controlled preview diagnosis.
  outline_layer=Image.new('RGBA',im.size)
  def shape_layer(c):
   return outline_layer if a.outline_mode=='union' and c==OUT else Image.new('RGBA',im.size)
  def composite(layer,c):
   if not (a.outline_mode=='union' and c==OUT):im.paste(layer,(0,0),layer)
  def line(pa,pb,c,w):
   layer=shape_layer(c); ld=ImageDraw.Draw(layer);r=w/2
   ld.line([pa,pb],fill=c,width=max(1,round(w)))
   for p in [pa,pb]:ld.ellipse([p[0]-r,p[1]-r,p[0]+r,p[1]+r],fill=c)
   composite(layer,c)
  def disc(p,r,c):
   layer=shape_layer(c);ld=ImageDraw.Draw(layer);ld.ellipse([p[0]-r,p[1]-r,p[0]+r,p[1]+r],fill=c);composite(layer,c)
  for u,v in bones:line(pts[u],pts[v],OUT,4.9*scale)
  disc(pts[0],7.3*scale,OUT)
  if a.outline_mode=='union':im.paste(outline_layer,(0,0),outline_layer)
  # Head after torso before near limbs, matching Figure.Draw; flat low-shading treatment.
  for n,(u,v,far) in enumerate(draw_order):
   line(pts[u],pts[v],FAR if far else INK,3.3*scale)
   if n==4:disc(pts[0],6.5*scale,INK)
  d=ImageDraw.Draw(im)
  t=f['t']; phase='Rest' if t<.4 or t>=1.8 else 'Reach' if t<.65 else 'Return' if t>1.55 else 'Scratch'
  txt(x+15,497,f'{phase}   {t:0.2f} s',14,'#59636c')
 # Timeline
 d=ImageDraw.Draw(im);d.rounded_rectangle((46*SUP,548*SUP,1074*SUP,554*SUP),radius=3*SUP,fill='#344652'); end=46+1028*(idx/(len(data['frames'])-1));d.rounded_rectangle((46*SUP,548*SUP,end*SUP,554*SUP),radius=3*SUP,fill='#82d6b5')
 txt(36,576,'ISOLATED CLOUD POSE PREVIEW',15,'#82d6b5',True)
 txt(36,602,'Exact before/after scratch equations + game spring/IK. Flat software rasterization.',15)
 txt(36,626,('Single-mask preview outline. Windows / Direct2D validation pending.' if a.outline_mode=='union' else 'Original per-stroke preview outline. Windows / Direct2D validation pending.'),15)
 frames.append(im.resize((W,H),Image.Resampling.LANCZOS))
# Single shared palette prevents color shimmer. Exactly 2.8 seconds: 30+30+40 ms pattern.
palette=frames[0].quantize(colors=224,method=Image.Quantize.MEDIANCUT)
indexed=[f.quantize(palette=palette,dither=Image.Dither.NONE) for f in frames]
indexed[0].save(a.folder/(a.prefix+'.gif'),save_all=True,append_images=indexed[1:],duration=[30,30,40]*28,loop=0,optimize=False,disposal=2)
frames[26].save(a.folder/(a.prefix+'.png'))
# Contact sheet at matched timestamps, enough to inspect the transition visually.
chosen=[13,17,24,43,50,54]; tw=560; th=330
sheet=Image.new('RGB',(tw*2,th*3))
for k,i in enumerate(chosen):sheet.paste(frames[i].resize((tw,th),Image.Resampling.LANCZOS),((k%2)*tw,(k//2)*th))
sheet.save(a.folder/(a.prefix+'-contact-sheet.png'))
with Image.open(a.folder/(a.prefix+'.gif')) as gif:
 duration=0
 for i in range(gif.n_frames):gif.seek(i);duration+=gif.info['duration']
 assert gif.n_frames==84 and duration==2800,(gif.n_frames,duration)
print('PASS: 84-frame matched GIF, 1120x660, 2800 ms, shared palette; PNG/contact sheet written')
