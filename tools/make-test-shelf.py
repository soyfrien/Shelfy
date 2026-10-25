# Generates a synthetic bookshelf photo (4 rotated spines) for end-to-end recognition tests. Needs Pillow.
from PIL import Image, ImageDraw, ImageFont
W,H=1300,1000
img=Image.new("RGB",(W,H),(120,85,55))
d=ImageDraw.Draw(img)
books=[("The Hobbit","J.R.R. Tolkien",(30,90,60),(240,220,140)),
       ("Dune","Frank Herbert",(200,170,90),(40,30,20)),
       ("Foundation","Isaac Asimov",(25,40,100),(240,240,240)),
       ("Neuromancer","William Gibson",(110,20,30),(240,240,240))]
fb="/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"; fr="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
x=70; sw=270; gap=30; top=60; bot=H-60
for t,a,bg,fg in books:
    d.rectangle([x,top,x+sw,bot],fill=bg,outline=(20,20,20),width=4)
    sp=Image.new("RGB",(bot-top,sw),bg); sd=ImageDraw.Draw(sp)
    ft=ImageFont.truetype(fb,88); fa=ImageFont.truetype(fr,52)
    tw=sd.textlength(t,font=ft); aw=sd.textlength(a,font=fa)
    sd.text(((bot-top-tw)/2,40),t,font=ft,fill=fg)
    sd.text(((bot-top-aw)/2,sw-120),a,font=fa,fill=fg)
    sp=sp.rotate(270,expand=True)   # text reads top-to-bottom
    img.paste(sp,(x,top)); x+=sw+gap
img.save("test-shelf.jpg",quality=90)
