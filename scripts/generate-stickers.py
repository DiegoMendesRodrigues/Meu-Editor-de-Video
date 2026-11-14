"""Gera as 40 figurinhas do pacote Básico (arte original, desenhada aqui) em PNG 512x512 com contorno branco e sombra.

Uso: python scripts/generate-stickers.py resources/stickers/basico/figurinhas   (requer Pillow e a fonte Segoe UI Bold)
"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1]
S = 1024  # desenha em 1024 e reduz para 512 (antisserrilhado)
FONT = r"C:\Windows\Fonts\segoeuib.ttf"
os.makedirs(OUT, exist_ok=True)

INK = (40, 40, 52, 255)


def canvas():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def star_pts(cx, cy, r1, r2, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = r1 if i % 2 == 0 else r2
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def heart_pts(cx, cy, s):
    pts = []
    for i in range(360):
        t = math.radians(i)
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + x * s, cy - y * s))
    return pts


def text(d, xy, s, size, fill, anchor="mm", stroke=0, stroke_fill=None):
    f = ImageFont.truetype(FONT, size)
    d.text(xy, s, font=f, fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=stroke_fill)


def fit_text(s, max_w, start):
    size = start
    while size > 40:
        f = ImageFont.truetype(FONT, size)
        if f.getbbox(s)[2] - f.getbbox(s)[0] <= max_w:
            return size
        size -= 8
    return size


def finish(im, name):
    # Contorno branco (dilatação do alfa) + sombra suave, como uma figurinha recortada.
    alpha = im.split()[3]
    border = alpha.filter(ImageFilter.MaxFilter(41)).filter(ImageFilter.GaussianBlur(2))
    shadow = border.filter(ImageFilter.GaussianBlur(18)).point(lambda v: v * 0.35)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste((0, 0, 0, 255), (0, 14), shadow)
    out.paste((255, 255, 255, 255), (0, 0), border)
    out.alpha_composite(im)
    bbox = out.getbbox()
    # Centraliza e deixa margem igual em todas.
    crop = out.crop(bbox)
    w, h = crop.size
    k = 920 / max(w, h)
    crop = crop.resize((max(1, int(w * k)), max(1, int(h * k))), Image.LANCZOS)
    final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    final.alpha_composite(crop, ((S - crop.width) // 2, (S - crop.height) // 2))
    final.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, name + ".png"), optimize=True)


# ---------------------------------------------------------------- rostos
def face(name, mouth, eyes="dots", color=(255, 205, 60, 255), extra=None):
    im, d = canvas()
    d.ellipse((112, 112, 912, 912), fill=color, outline=INK, width=24)
    if eyes == "dots":
        d.ellipse((330, 360, 420, 480), fill=INK); d.ellipse((604, 360, 694, 480), fill=INK)
    elif eyes == "wink":
        d.ellipse((330, 360, 420, 480), fill=INK); d.arc((590, 380, 710, 480), 200, 340, fill=INK, width=28)
    elif eyes == "laugh":
        d.arc((310, 370, 440, 480), 200, 340, fill=INK, width=30); d.arc((584, 370, 714, 480), 200, 340, fill=INK, width=30)
    elif eyes == "wide":
        for x in (375, 649):
            d.ellipse((x - 70, 330, x + 70, 490), fill=(255, 255, 255, 255), outline=INK, width=18)
            d.ellipse((x - 30, 380, x + 30, 450), fill=INK)
    elif eyes == "cool":
        d.rounded_rectangle((250, 350, 495, 490), 50, fill=INK); d.rounded_rectangle((529, 350, 774, 490), 50, fill=INK)
        d.rectangle((480, 380, 545, 410), fill=INK)
        d.line((300, 380, 360, 380), fill=(120, 120, 140, 255), width=14)
    elif eyes == "hearts":
        for x in (375, 649):
            d.polygon(heart_pts(x, 420, 5.2), fill=(230, 40, 70, 255))
    elif eyes == "sad":
        d.ellipse((330, 380, 420, 490), fill=INK); d.ellipse((604, 380, 694, 490), fill=INK)
        d.line((300, 330, 420, 360), fill=INK, width=22); d.line((724, 330, 604, 360), fill=INK, width=22)
    elif eyes == "angry":
        d.ellipse((330, 400, 420, 500), fill=INK); d.ellipse((604, 400, 694, 500), fill=INK)
        d.line((290, 330, 430, 390), fill=INK, width=28); d.line((734, 330, 594, 390), fill=INK, width=28)
    if mouth == "smile":
        d.arc((300, 420, 724, 760), 20, 160, fill=INK, width=30)
    elif mouth == "big":
        d.chord((290, 500, 734, 800), 0, 180, fill=INK); d.chord((380, 650, 644, 800), 200, 340, fill=(235, 90, 100, 255))
    elif mouth == "o":
        d.ellipse((440, 590, 584, 760), fill=INK)
    elif mouth == "sad":
        d.arc((330, 620, 694, 880), 200, 340, fill=INK, width=30)
    elif mouth == "flat":
        d.line((380, 680, 644, 680), fill=INK, width=30)
    if extra == "tear":
        d.polygon([(700, 500), (650, 600), (750, 600)], fill=(80, 170, 250, 255)); d.ellipse((650, 560, 750, 660), fill=(80, 170, 250, 255))
    if extra == "tears2":
        for x in (300, 724):
            d.ellipse((x - 50, 500, x + 50, 620), fill=(80, 170, 250, 255))
    finish(im, name)


# ---------------------------------------------------------------- formas e símbolos
def shape_heart():
    im, d = canvas(); d.polygon(heart_pts(512, 500, 26), fill=(235, 45, 80, 255), outline=INK, width=24)
    d.ellipse((300, 280, 400, 380), fill=(255, 255, 255, 140)); finish(im, "Coração")


def shape_star():
    im, d = canvas(); d.polygon(star_pts(512, 540, 440, 190), fill=(255, 200, 40, 255), outline=INK, width=24); finish(im, "Estrela")


def sparkles():
    im, d = canvas()
    d.polygon(star_pts(430, 560, 360, 80, n=4, rot=-90), fill=(255, 210, 60, 255), outline=INK, width=18)
    d.polygon(star_pts(780, 260, 170, 40, n=4, rot=-90), fill=(255, 235, 140, 255), outline=INK, width=14)
    d.polygon(star_pts(820, 760, 110, 28, n=4, rot=-90), fill=(255, 235, 140, 255), outline=INK, width=12)
    finish(im, "Brilhos")


def arrow(name, angle, color=(240, 70, 60, 255)):
    im, d = canvas()
    pts = [(120, 420), (560, 420), (560, 230), (900, 512), (560, 794), (560, 604), (120, 604)]
    d.polygon(pts, fill=color, outline=INK, width=24)
    finish(im.rotate(angle, resample=Image.BICUBIC), name)


def curved_arrow():
    im, d = canvas()
    d.arc((160, 220, 820, 880), 180, 330, fill=INK, width=150)
    d.arc((180, 240, 800, 860), 180, 330, fill=(240, 70, 60, 255), width=110)
    tip = [(700, 210), (930, 380), (660, 470)]
    d.polygon(tip, fill=(240, 70, 60, 255), outline=INK, width=20)
    finish(im, "Seta curva")


def circle_highlight():
    im, d = canvas()
    d.ellipse((120, 160, 904, 864), outline=(240, 60, 50, 255), width=70)
    finish(im, "Círculo de destaque")


def check():
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=(50, 190, 100, 255), outline=INK, width=24)
    d.line([(300, 520), (450, 680), (740, 360)], fill=(255, 255, 255, 255), width=100, joint="curve"); finish(im, "Certo")


def cross():
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=(235, 60, 60, 255), outline=INK, width=24)
    d.line([(340, 340), (684, 684)], fill=(255, 255, 255, 255), width=100); d.line([(684, 340), (340, 684)], fill=(255, 255, 255, 255), width=100)
    finish(im, "Errado")


def mark(name, ch, color):
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=color, outline=INK, width=24)
    text(d, (512, 530), ch, 640, (255, 255, 255, 255)); finish(im, name)


def bubble(name, words, think=False):
    im, d = canvas()
    if think:
        d.ellipse((90, 120, 934, 720), fill=(255, 255, 255, 255), outline=INK, width=24)
        d.ellipse((200, 720, 320, 820), fill=(255, 255, 255, 255), outline=INK, width=20)
        d.ellipse((120, 840, 190, 910), fill=(255, 255, 255, 255), outline=INK, width=16)
    else:
        d.rounded_rectangle((90, 140, 934, 700), 160, fill=(255, 255, 255, 255), outline=INK, width=24)
        d.polygon([(260, 680), (220, 900), (460, 690)], fill=(255, 255, 255, 255))
        d.line([(262, 690), (220, 900), (460, 696)], fill=INK, width=24, joint="curve")
    if words:
        text(d, (512, 420), words, fit_text(words, 680, 220), INK)
    finish(im, name)


def fire():
    im, d = canvas()
    outer = [(512, 70), (640, 300), (760, 250), (840, 520), (800, 760), (650, 930), (380, 930), (220, 760), (190, 520), (300, 340), (390, 420)]
    d.polygon(outer, fill=(245, 90, 30, 255), outline=INK, width=24)
    inner = [(520, 380), (610, 560), (660, 520), (690, 700), (620, 860), (420, 860), (350, 720), (380, 580), (440, 620)]
    d.polygon(inner, fill=(255, 200, 50, 255)); finish(im, "Fogo")


def bolt():
    im, d = canvas(); d.polygon([(600, 60), (240, 560), (480, 560), (400, 960), (790, 420), (550, 420), (680, 60)], fill=(255, 210, 40, 255), outline=INK, width=24)
    finish(im, "Raio")


def sun():
    im, d = canvas()
    for i in range(12):
        a = math.radians(i * 30)
        d.line([(512 + 260 * math.cos(a), 512 + 260 * math.sin(a)), (512 + 440 * math.cos(a), 512 + 440 * math.sin(a))], fill=(255, 170, 30, 255), width=60)
    d.ellipse((262, 262, 762, 762), fill=(255, 205, 50, 255), outline=INK, width=24); finish(im, "Sol")


def moon():
    im, d = canvas(); d.ellipse((140, 140, 884, 884), fill=(255, 225, 120, 255), outline=INK, width=24)
    d.ellipse((380, 60, 1000, 680), fill=(0, 0, 0, 0)); finish(im.copy(), "Lua")


def cloud():
    im, d = canvas(); c = (235, 245, 255, 255)
    for b in [(140, 420, 480, 760), (320, 260, 700, 640), (560, 380, 900, 720), (220, 520, 820, 800)]:
        d.ellipse(b, fill=INK)
    for b in [(164, 444, 456, 736), (344, 284, 676, 616), (584, 404, 876, 696), (244, 544, 796, 776)]:
        d.ellipse(b, fill=c)
    finish(im, "Nuvem")


def drop():
    im, d = canvas(); d.polygon([(512, 90), (300, 520), (724, 520)], fill=(60, 160, 245, 255)); d.ellipse((280, 380, 744, 900), fill=(60, 160, 245, 255), outline=INK, width=24)
    d.line([(512, 110), (300, 520)], fill=INK, width=24); d.line([(512, 110), (724, 520)], fill=INK, width=24)
    d.polygon([(512, 150), (330, 520), (694, 520)], fill=(60, 160, 245, 255)); d.ellipse((380, 560, 470, 700), fill=(255, 255, 255, 160)); finish(im, "Gota")


def gift():
    im, d = canvas(); d.rectangle((180, 440, 844, 900), fill=(230, 60, 90, 255), outline=INK, width=24)
    d.rectangle((140, 320, 884, 460), fill=(240, 80, 110, 255), outline=INK, width=24)
    d.rectangle((462, 320, 562, 900), fill=(255, 210, 60, 255), outline=INK, width=16)
    d.ellipse((300, 150, 510, 330), outline=(255, 210, 60, 255), width=50); d.ellipse((514, 150, 724, 330), outline=(255, 210, 60, 255), width=50)
    finish(im, "Presente")


def balloon():
    im, d = canvas(); d.ellipse((260, 80, 764, 700), fill=(240, 60, 90, 255), outline=INK, width=24)
    d.polygon([(470, 690), (554, 690), (512, 740)], fill=INK); d.line([(512, 740), (470, 830), (540, 940)], fill=INK, width=14, joint="curve")
    d.ellipse((340, 200, 420, 330), fill=(255, 255, 255, 150)); finish(im, "Balão de festa")


def confetti():
    im, d = canvas(); d.polygon([(160, 900), (380, 380), (680, 680)], fill=(255, 190, 40, 255), outline=INK, width=22)
    cols = [(240, 60, 90, 255), (60, 160, 245, 255), (60, 200, 120, 255), (170, 90, 240, 255)]
    for i, (x, y) in enumerate([(560, 200), (760, 300), (840, 520), (660, 420), (480, 120), (880, 160), (780, 700)]):
        d.rounded_rectangle((x - 40, y - 18, x + 40, y + 18), 10, fill=cols[i % 4], outline=INK, width=8)
    finish(im, "Confete")


def crown():
    im, d = canvas(); d.polygon([(140, 760), (180, 300), (360, 520), (512, 220), (664, 520), (844, 300), (884, 760)], fill=(255, 200, 40, 255), outline=INK, width=24)
    d.rectangle((140, 740, 884, 860), fill=(240, 170, 30, 255), outline=INK, width=24)
    for x in (300, 512, 724):
        d.ellipse((x - 40, 760, x + 40, 840), fill=(230, 60, 90, 255))
    finish(im, "Coroa")


def trophy():
    im, d = canvas(); g = (255, 200, 40, 255)
    d.arc((120, 200, 420, 560), 90, 270, fill=INK, width=84); d.arc((604, 200, 904, 560), 270, 90, fill=INK, width=84)
    d.arc((132, 212, 408, 548), 90, 270, fill=g, width=48); d.arc((616, 212, 892, 548), 270, 90, fill=g, width=48)
    d.chord((260, 40, 764, 640), 0, 180, fill=g, outline=INK, width=24); d.rectangle((260, 150, 764, 340), fill=g); d.line((260, 150, 260, 340), fill=INK, width=24); d.line((764, 150, 764, 340), fill=INK, width=24); d.line((248, 150, 776, 150), fill=INK, width=24)
    d.rectangle((462, 630, 562, 780), fill=g, outline=INK, width=20); d.rectangle((300, 770, 724, 900), fill=(140, 90, 50, 255), outline=INK, width=24)
    text(d, (512, 340), "1", 260, INK); finish(im, "Troféu")


def medal():
    im, d = canvas(); d.polygon([(330, 60), (470, 60), (560, 420), (420, 420)], fill=(60, 120, 230, 255), outline=INK, width=18)
    d.polygon([(694, 60), (554, 60), (464, 420), (604, 420)], fill=(230, 60, 80, 255), outline=INK, width=18)
    d.ellipse((242, 380, 782, 920), fill=(255, 200, 40, 255), outline=INK, width=24); d.polygon(star_pts(512, 660, 170, 75), fill=(255, 235, 140, 255), outline=INK, width=12)
    finish(im, "Medalha")


def bell():
    im, d = canvas(); y = (255, 200, 40, 255)
    d.chord((240, 160, 784, 760), 180, 360, fill=y, outline=INK, width=24); d.polygon([(240, 460), (784, 460), (870, 760), (154, 760)], fill=y)
    d.line([(240, 460), (154, 760)], fill=INK, width=24); d.line([(784, 460), (870, 760)], fill=INK, width=24)
    d.rounded_rectangle((120, 740, 904, 820), 40, fill=y, outline=INK, width=24); d.ellipse((440, 800, 584, 920), fill=INK); d.ellipse((462, 100, 562, 190), fill=y, outline=INK, width=18)
    finish(im, "Sino")


def pill_button(name, label, color, icon=None):
    im, d = canvas(); d.rounded_rectangle((60, 330, 964, 694), 182, fill=color, outline=INK, width=24)
    x = 512
    if icon == "play":
        d.polygon([(150, 410), (150, 614), (310, 512)], fill=(255, 255, 255, 255)); x = 600
    if icon == "heart":
        d.polygon(heart_pts(230, 510, 6.5), fill=(255, 255, 255, 255)); x = 600
    if icon == "bell":
        w = (255, 255, 255, 255)
        d.chord((150, 400, 310, 560), 180, 360, fill=w); d.polygon([(150, 480), (310, 480), (330, 590), (130, 590)], fill=w)
        d.ellipse((205, 585, 255, 630), fill=w); x = 600
    text(d, (x, 505), label, fit_text(label, 560 if icon else 780, 160), (255, 255, 255, 255))
    finish(im, name)


def word(name, label, color, shape="burst"):
    im, d = canvas()
    if shape == "burst":
        d.polygon(star_pts(512, 512, 470, 360, n=16), fill=color, outline=INK, width=22)
    else:
        d.rounded_rectangle((70, 270, 954, 754), 80, fill=color, outline=INK, width=24)
    text(d, (512, 512), label, fit_text(label, 640, 240), (255, 255, 255, 255), stroke=14, stroke_fill=INK)
    finish(im, name)


def pin():
    im, d = canvas(); r = (235, 60, 60, 255)
    d.polygon([(250, 470), (774, 470), (512, 940)], fill=r); d.ellipse((230, 100, 794, 664), fill=r, outline=INK, width=24)
    d.line([(262, 520), (512, 940), (762, 520)], fill=INK, width=24, joint="curve"); d.ellipse((392, 262, 632, 502), fill=(255, 255, 255, 255), outline=INK, width=18)
    finish(im, "Localização")


def clock():
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=(255, 255, 255, 255), outline=INK, width=36)
    for i in range(12):
        a = math.radians(i * 30); d.ellipse((512 + 330 * math.cos(a) - 18, 512 + 330 * math.sin(a) - 18, 512 + 330 * math.cos(a) + 18, 512 + 330 * math.sin(a) + 18), fill=INK)
    d.line([(512, 512), (512, 260)], fill=INK, width=40); d.line([(512, 512), (680, 600)], fill=(235, 60, 60, 255), width=30); d.ellipse((480, 480, 544, 544), fill=INK)
    finish(im, "Relógio")


def camera():
    im, d = canvas(); d.rounded_rectangle((110, 280, 914, 820), 80, fill=(70, 80, 100, 255), outline=INK, width=24)
    d.rounded_rectangle((340, 190, 620, 320), 30, fill=(70, 80, 100, 255), outline=INK, width=24)
    d.ellipse((322, 370, 702, 750), fill=(220, 230, 245, 255), outline=INK, width=24); d.ellipse((412, 460, 612, 660), fill=(60, 140, 230, 255))
    d.ellipse((760, 340, 840, 420), fill=(255, 200, 40, 255)); finish(im, "Câmera")


def note():
    im, d = canvas(); c = (150, 80, 240, 255)
    d.polygon([(400, 160), (880, 80), (880, 240), (400, 320)], fill=c, outline=INK, width=20)
    d.rectangle((380, 160, 430, 780), fill=INK); d.rectangle((850, 80, 900, 700), fill=INK)
    d.ellipse((160, 680, 440, 900), fill=c, outline=INK, width=24); d.ellipse((620, 600, 900, 820), fill=c, outline=INK, width=24)
    finish(im, "Música")


def mic():
    im, d = canvas(); d.rounded_rectangle((362, 80, 662, 600), 150, fill=(90, 100, 120, 255), outline=INK, width=24)
    for y in (220, 320, 420):
        d.line([(400, y), (624, y)], fill=(150, 160, 180, 255), width=16)
    d.arc((240, 300, 784, 760), 0, 180, fill=INK, width=36); d.rectangle((492, 760, 532, 880), fill=INK); d.rounded_rectangle((320, 860, 704, 920), 30, fill=INK)
    finish(im, "Microfone")


def bulb():
    im, d = canvas(); y = (255, 215, 60, 255)
    d.ellipse((222, 90, 802, 670), fill=y, outline=INK, width=24); d.polygon([(360, 600), (664, 600), (624, 740), (400, 740)], fill=y)
    d.line([(360, 600), (400, 740)], fill=INK, width=24); d.line([(664, 600), (624, 740)], fill=INK, width=24)
    d.rounded_rectangle((390, 730, 634, 900), 30, fill=(160, 170, 190, 255), outline=INK, width=22); d.ellipse((330, 200, 420, 330), fill=(255, 255, 255, 170))
    finish(im, "Ideia")


def target():
    im, d = canvas()
    for i, c in enumerate([(235, 60, 60, 255), (255, 255, 255, 255), (235, 60, 60, 255), (255, 255, 255, 255), (235, 60, 60, 255)]):
        r = 400 - i * 80; d.ellipse((512 - r, 512 - r, 512 + r, 512 + r), fill=c, outline=INK, width=20 if i == 0 else 0)
    finish(im, "Alvo")


def money():
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=(60, 190, 100, 255), outline=INK, width=24)
    text(d, (512, 530), "$", 640, (255, 255, 255, 255)); finish(im, "Dinheiro")


def play():
    im, d = canvas(); d.ellipse((112, 112, 912, 912), fill=(40, 150, 240, 255), outline=INK, width=24)
    d.polygon([(410, 300), (410, 724), (740, 512)], fill=(255, 255, 255, 255)); finish(im, "Botão play")


def thumbs():
    im, d = canvas(); s = (255, 200, 80, 255)
    d.rounded_rectangle((120, 440, 300, 900), 30, fill=(60, 120, 230, 255), outline=INK, width=22)
    d.polygon([(320, 460), (520, 120), (600, 140), (620, 240), (580, 420), (860, 420), (900, 500), (850, 880), (320, 880)], fill=s, outline=INK, width=24)
    for y in (560, 680, 790):
        d.line([(660, y), (880, y)], fill=INK, width=14)
    finish(im, "Joinha")


# ---------------------------------------------------------------- 40 figurinhas
shape_heart(); shape_star(); sparkles(); thumbs()
arrow("Seta para a direita", 0); arrow("Seta para a esquerda", 180); arrow("Seta para cima", 90); arrow("Seta para baixo", 270)
curved_arrow(); circle_highlight(); check(); cross()
mark("Exclamação", "!", (255, 170, 30, 255)); mark("Interrogação", "?", (60, 140, 230, 255))
bubble("Balão de fala", ""); bubble("Balão de pensamento", "", think=True)
fire(); bolt(); sun(); cloud(); drop(); gift(); balloon(); confetti(); crown(); trophy(); medal(); bell()
pill_button("Inscreva-se", "INSCREVA-SE", (235, 70, 50, 255), icon="bell")
pill_button("Curtir", "CURTIR", (60, 120, 230, 255), icon="heart")
word("Novo", "NOVO!", (240, 60, 90, 255)); word("Uau", "UAU!", (170, 80, 240, 255)); word("Oferta", "OFERTA", (255, 150, 20, 255), shape="box"); word("Obrigado", "OBRIGADO!", (50, 180, 110, 255), shape="box")
pin(); camera(); note(); bulb(); play()
face("Sorriso", "smile")
n = len([f for f in os.listdir(OUT) if f.endswith('.png')])
print(n)
