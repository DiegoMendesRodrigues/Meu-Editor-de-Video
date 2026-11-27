# Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
"""Gera as figurinhas do pacote Básico (símbolos, formas geométricas, pinos e pontos de interesse da cidade) (arte original, desenhada aqui) em PNG 512x512 com contorno branco e sombra.

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
# ---------------------------------------------------------------- formas geométricas
def geo(name, kind, color):
    im, d = canvas(); w = 26
    if kind == "circle": d.ellipse((152, 152, 872, 872), fill=color, outline=INK, width=w)
    elif kind == "square": d.rounded_rectangle((162, 162, 862, 862), 24, fill=color, outline=INK, width=w)
    elif kind == "rect": d.rounded_rectangle((80, 292, 944, 732), 24, fill=color, outline=INK, width=w)
    else:
        n, rot = {"tri": (3, -90), "diamond": (4, -90), "pent": (5, -90), "hex": (6, 0)}[kind]
        pts = [(512 + 400 * math.cos(math.radians(rot + i * 360 / n)), 560 + 400 * math.sin(math.radians(rot + i * 360 / n))) for i in range(n)]
        if kind == "tri": pts = [(x, y - 60) for x, y in pts]
        d.polygon(pts, fill=color, outline=INK, width=w)
    finish(im, name)


geo("Círculo", "circle", (60, 140, 230, 255)); geo("Quadrado", "square", (240, 70, 60, 255))
geo("Retângulo", "rect", (50, 180, 110, 255)); geo("Triângulo", "tri", (255, 180, 30, 255))
geo("Losango", "diamond", (170, 80, 240, 255)); geo("Pentágono", "pent", (20, 170, 190, 255)); geo("Hexágono", "hex", (240, 90, 160, 255))


# ---------------------------------------------------------------- pinos de mapa
HEAD = (512, 400)
W = (255, 255, 255, 255)


def pin_base(d, color, head="circle"):
    """Pino de mapa: ponta embaixo e cabeça na forma pedida; devolve nada (a cabeça fica em HEAD, raio ~270)."""
    cx, cy = HEAD
    d.polygon([(330, 560), (694, 560), (512, 960)], fill=color, outline=INK, width=24)
    if head == "circle": d.ellipse((cx - 280, cy - 280, cx + 280, cy + 280), fill=color, outline=INK, width=24)
    elif head == "square": d.rounded_rectangle((cx - 270, cy - 270, cx + 270, cy + 270), 50, fill=color, outline=INK, width=24)
    elif head == "hex": d.polygon([(cx + 300 * math.cos(math.radians(30 + i * 60)), cy + 300 * math.sin(math.radians(30 + i * 60))) for i in range(6)], fill=color, outline=INK, width=24)
    elif head == "diamond": d.polygon([(cx, cy - 330), (cx + 330, cy), (cx, cy + 330), (cx - 330, cy)], fill=color, outline=INK, width=24)
    elif head == "star": d.polygon(star_pts(cx, cy, 340, 165), fill=color, outline=INK, width=24)
    elif head == "heart": d.polygon(heart_pts(cx, cy + 20, 19), fill=color, outline=INK, width=24)
    elif head == "tri": d.polygon([(cx, cy - 320), (cx + 330, cy + 250), (cx - 330, cy + 250)], fill=color, outline=INK, width=24)
    # Esconde a junção entre a ponta e a cabeça.
    d.polygon([(352, 560), (672, 560), (512, 920)], fill=color)


def box_pin(name, head, color):
    im, d = canvas(); pin_base(d, color, head)
    cx, cy = HEAD
    if head in ("square",): d.rounded_rectangle((cx - 130, cy - 130, cx + 130, cy + 130), 24, fill=W, outline=INK, width=16)
    elif head == "diamond": d.polygon([(cx, cy - 150), (cx + 150, cy), (cx, cy + 150), (cx - 150, cy)], fill=W, outline=INK, width=16)
    else: d.ellipse((cx - 120, cy - 120, cx + 120, cy + 120), fill=W, outline=INK, width=16)
    finish(im, name)


for nm, hd, col in [("Pino quadrado", "square", (60, 120, 230, 255)), ("Pino redondo", "circle", (235, 60, 60, 255)),
                    ("Pino hexagonal", "hex", (50, 170, 100, 255)), ("Pino losango", "diamond", (255, 150, 20, 255)),
                    ("Pino estrela", "star", (255, 190, 30, 255)), ("Pino coração", "heart", (240, 80, 150, 255)),
                    ("Pino triangular", "tri", (150, 80, 230, 255))]:
    box_pin(nm, hd, col)


# ---------------------------------------------------------------- pontos de interesse da cidade
TRANSP, FOOD, SERV, CULT, NAT, SHOP = (45, 110, 220, 255), (240, 120, 30, 255), (215, 50, 50, 255), (140, 70, 200, 255), (40, 150, 80, 255), (230, 70, 140, 255)


def poi(name, color, draw):
    im, d = canvas(); pin_base(d, color)
    cx, cy = HEAD
    k = 1.75

    def P(x, y): return (cx + x * k, cy + y * k)

    def R(x0, y0, x1, y1): return (*P(x0, y0), *P(x1, y1))

    draw(d, P, R, k, color)
    finish(im, name)


def T(s, size=300):
    def f(d, P, R, k, c): text(d, P(0, 4), s, size, W)
    return f


def airplane(d, P, R, k, c):
    d.polygon([P(0, -100), P(10, -85), P(10, -25), P(95, 20), P(95, 38), P(10, 15), P(10, 60), P(35, 80), P(35, 92), P(0, 82),
               P(-35, 92), P(-35, 80), P(-10, 60), P(-10, 15), P(-95, 38), P(-95, 20), P(-10, -25), P(-10, -85)], fill=W)


def museum(d, P, R, k, c):
    d.polygon([P(-95, -40), P(0, -95), P(95, -40)], fill=W); d.rectangle(R(-95, 70, 95, 90), fill=W); d.rectangle(R(-85, -35, 85, -22), fill=W)
    for x in (-70, -25, 20, 65): d.rectangle(R(x - 2, -15, x + 7, 62), fill=W)


def fish(d, P, R, k, c):
    d.ellipse(R(-80, -50, 45, 50), fill=W); d.polygon([P(30, 0), P(95, -50), P(95, 50)], fill=W); d.ellipse(R(-55, -18, -35, 2), fill=c)
    d.arc(R(-20, -35, 20, 35), 300, 60, fill=c, width=int(8 * k))


def beach(d, P, R, k, c):
    d.pieslice(R(-90, -90, 90, 90), 180, 360, fill=W); d.line([P(0, 0), P(0, 70)], fill=W, width=int(10 * k))
    for i in range(3): d.line([P(-90 + i * 60, 85), P(-60 + i * 60, 75), P(-30 + i * 60, 85)], fill=W, width=int(8 * k), joint="curve")


def bus(d, P, R, k, c):
    d.rounded_rectangle(R(-70, -90, 70, 65), int(22 * k), fill=W); d.rectangle(R(-55, -70, 55, -5), fill=c)
    d.ellipse(R(-55, 20, -35, 40), fill=c); d.ellipse(R(35, 20, 55, 40), fill=c)
    d.rectangle(R(-62, 60, -32, 92), fill=W); d.rectangle(R(32, 60, 62, 92), fill=W)


def train(d, P, R, k, c):
    d.rounded_rectangle(R(-60, -95, 60, 55), int(35 * k), fill=W); d.rounded_rectangle(R(-42, -70, 42, -15), int(10 * k), fill=c)
    d.ellipse(R(-40, 15, -22, 33), fill=c); d.ellipse(R(22, 15, 40, 33), fill=c)
    d.line([P(-40, 60), P(-70, 95)], fill=W, width=int(10 * k)); d.line([P(40, 60), P(70, 95)], fill=W, width=int(10 * k))


def metro(d, P, R, k, c):
    d.ellipse(R(-90, -90, 90, 90), fill=W); text(d, P(0, 2), "M", 250, c)


def casino(d, P, R, k, c):
    d.rounded_rectangle(R(-75, -75, 75, 75), int(22 * k), fill=W)
    for x, y in [(-40, -40), (40, -40), (0, 0), (-40, 40), (40, 40)]: d.ellipse(R(x - 14, y - 14, x + 14, y + 14), fill=c)


def bar(d, P, R, k, c):
    d.polygon([P(-80, -80), P(80, -80), P(8, 0), P(8, 65), P(45, 80), P(45, 90), P(-45, 90), P(-45, 80), P(-8, 65), P(-8, 0)], fill=W)
    d.ellipse(R(20, -110, 50, -80), fill=W); d.polygon([P(-55, -55), P(55, -55), P(0, -5)], fill=c)


def restaurant(d, P, R, k, c):
    for x in (-70, -50, -30): d.rectangle(R(x - 4, -95, x + 4, -35), fill=W)
    d.rounded_rectangle(R(-76, -45, -24, -15), int(10 * k), fill=W); d.rectangle(R(-58, -20, -42, 95), fill=W)
    d.pieslice(R(15, -95, 85, 45), 270, 90, fill=W); d.rectangle(R(42, -95, 58, 95), fill=W)


def cafe(d, P, R, k, c):
    d.rounded_rectangle(R(-80, -20, 50, 75), int(25 * k), fill=W); d.rectangle(R(-80, -20, 50, 20), fill=W)
    d.ellipse(R(30, -10, 90, 50), outline=W, width=int(14 * k)); d.rectangle(R(-95, 82, 70, 95), fill=W)
    for x in (-50, -15, 20): d.line([P(x, -90), P(x + 10, -65), P(x, -40)], fill=W, width=int(9 * k), joint="curve")


def flame(d, P, R, k, c):
    d.polygon([P(0, -100), P(35, -50), P(70, -5), P(75, 40), P(50, 80), P(0, 95), P(-50, 80), P(-75, 40), P(-60, -20), P(-35, 10), P(-30, -45)], fill=W)
    d.polygon([P(0, -10), P(30, 30), P(30, 60), P(0, 78), P(-30, 60), P(-25, 25)], fill=c)


def police(d, P, R, k, c):
    d.polygon([P(0, -100), P(85, -65), P(75, 25), P(0, 100), P(-75, 25), P(-85, -65)], fill=W); d.polygon(star_pts(*P(0, -5), 55 * k, 24 * k), fill=c)


def fuel(d, P, R, k, c):
    d.rounded_rectangle(R(-75, -90, 25, 90), int(14 * k), fill=W); d.rectangle(R(-58, -70, 8, -20), fill=c)
    d.line([P(25, -40), P(60, -20), P(60, 50), P(75, 60), P(85, 40), P(85, -50)], fill=W, width=int(12 * k), joint="curve")


def flag(d, P, R, k, c):
    d.rectangle(R(-75, -95, -60, 95), fill=W); d.polygon([P(-60, -90), P(85, -90), P(55, -50), P(85, -10), P(-60, -10)], fill=W)


def city_hall(d, P, R, k, c):
    d.pieslice(R(-50, -80, 50, 20), 180, 360, fill=W); d.rectangle(R(-3, -110, 3, -75), fill=W)
    d.rectangle(R(-90, -25, 90, -12), fill=W); d.rectangle(R(-95, 72, 95, 92), fill=W)
    for x in (-75, -40, -5, 30, 65): d.rectangle(R(x, -5, x + 12, 65), fill=W)


def hospital(d, P, R, k, c):
    d.rectangle(R(-28, -90, 28, 90), fill=W); d.rectangle(R(-90, -28, 90, 28), fill=W)


def hotel(d, P, R, k, c):
    d.rectangle(R(-95, -60, -78, 80), fill=W); d.rectangle(R(-80, 20, 95, 50), fill=W); d.rectangle(R(80, 0, 95, 80), fill=W)
    d.ellipse(R(-70, -25, -30, 15), fill=W); d.rounded_rectangle(R(-20, -20, 95, 20), int(15 * k), fill=W)


def house(d, P, R, k, c):
    d.polygon([P(0, -95), P(95, -10), P(70, -10), P(70, 85), P(-70, 85), P(-70, -10), P(-95, -10)], fill=W); d.rectangle(R(-20, 20, 20, 85), fill=c)


def factory(d, P, R, k, c):
    d.polygon([P(-95, 85), P(-95, -20), P(-45, 10), P(-45, -20), P(5, 10), P(5, -20), P(55, 10), P(55, -95), P(85, -95), P(85, 85)], fill=W)
    for x in (-75, -35, 5): d.rectangle(R(x, 35, x + 22, 60), fill=c)


def books(d, P, R, k, c):
    d.rectangle(R(-85, -70, -45, 85), fill=W); d.rectangle(R(-35, -90, 5, 85), fill=W)
    d.polygon([P(20, -60), P(55, -72), P(95, 75), P(60, 87)], fill=W)
    for x in (-65, -15): d.rectangle(R(x - 10, -40, x + 10, -30), fill=c)


def tree(d, P, R, k, c):
    d.ellipse(R(-80, -100, 80, 40), fill=W); d.rectangle(R(-14, 20, 14, 95), fill=W); d.rectangle(R(-55, 85, 55, 97), fill=W)


def pill(d, P, R, k, c):
    pts = lambda a, b: [P(a * math.cos(math.radians(45)) - b * math.sin(math.radians(45)), a * math.sin(math.radians(45)) + b * math.cos(math.radians(45)))]
    d.line([*pts(-55, 0), *pts(55, 0)], fill=W, width=int(80 * k))
    for a in (-55, 55): x, y = pts(a, 0)[0]; d.ellipse((x - 40 * k, y - 40 * k, x + 40 * k, y + 40 * k), fill=W)
    d.line([*pts(0, -40), *pts(0, 40)], fill=c, width=int(8 * k))


def envelope(d, P, R, k, c):
    d.rectangle(R(-95, -60, 95, 65), fill=W); d.line([P(-90, -55), P(0, 15), P(90, -55)], fill=c, width=int(10 * k), joint="curve")


def pencil(d, P, R, k, c):
    a = math.radians(-45)
    def Q(x, y): return P(x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a))
    d.polygon([Q(-60, -25), Q(85, -25), Q(85, 25), Q(-60, 25)], fill=W); d.polygon([Q(-60, -25), Q(-110, 0), Q(-60, 25)], fill=W)
    d.line([Q(60, -25), Q(60, 25)], fill=c, width=int(8 * k)); d.polygon([Q(-95, -8), Q(-110, 0), Q(-95, 8)], fill=c)


def bag(d, P, R, k, c):
    d.arc(R(-40, -100, 40, -20), 180, 360, fill=W, width=int(14 * k)); d.rectangle(R(-40, -62, -26, -40), fill=W); d.rectangle(R(26, -62, 40, -40), fill=W)
    d.polygon([P(-75, -45), P(75, -45), P(88, 95), P(-88, 95)], fill=W)


def ball(d, P, R, k, c):
    d.ellipse(R(-90, -90, 90, 90), fill=W)
    d.polygon([P(32 * math.cos(math.radians(-90 + i * 72)), 32 * math.sin(math.radians(-90 + i * 72))) for i in range(5)], fill=c)
    for i in range(5):
        a = math.radians(-90 + i * 72)
        d.line([P(32 * math.cos(a), 32 * math.sin(a)), P(88 * math.cos(a), 88 * math.sin(a))], fill=c, width=int(7 * k))


def mask(d, P, R, k, c):
    d.chord(R(-85, -95, 85, 95), 0, 360, fill=W); d.ellipse(R(-55, -40, -15, -10), fill=c); d.ellipse(R(15, -40, 55, -10), fill=c)
    d.chord(R(-45, 0, 45, 60), 0, 180, fill=c)


def grad_cap(d, P, R, k, c):
    d.polygon([P(0, -80), P(100, -30), P(0, 20), P(-100, -30)], fill=W); d.polygon([P(-55, -5), P(55, -5), P(55, 50), P(0, 70), P(-55, 50)], fill=W)
    d.line([P(80, -25), P(80, 55)], fill=W, width=int(8 * k)); d.ellipse(R(70, 50, 90, 70), fill=W)


def paw(d, P, R, k, c):
    d.ellipse(R(-55, 5, 55, 90), fill=W)
    for x, y in [(-75, -25), (-30, -70), (30, -70), (75, -25)]: d.ellipse(R(x - 22, y - 28, x + 22, y + 28), fill=W)


def radio_tower(d, P, R, k, c):
    lw = int(10 * k)
    d.line([P(-50, 95), P(0, -45), P(50, 95)], fill=W, width=lw, joint="curve"); d.line([P(-32, 45), P(32, 45)], fill=W, width=lw)
    d.line([P(-32, 45), P(20, 0)], fill=W, width=lw); d.ellipse(R(-15, -60, 15, -30), fill=W)
    for r in (45, 75): d.arc(R(-r, -45 - r, r, -45 + r), 200, 340, fill=W, width=int(9 * k))


def pylon(d, P, R, k, c):
    lw = int(9 * k)
    d.line([P(-55, 95), P(-15, -95), P(15, -95), P(55, 95)], fill=W, width=lw, joint="curve")
    for y, w in [(-60, 85), (-15, 70)]: d.line([P(-w, y), P(w, y)], fill=W, width=lw)
    d.line([P(-45, 50), P(25, -15)], fill=W, width=lw); d.line([P(45, 50), P(-25, -15)], fill=W, width=lw); d.line([P(-40, 50), P(40, 50)], fill=W, width=lw)


def dumbbell(d, P, R, k, c):
    d.rectangle(R(-60, -10, 60, 10), fill=W)
    for x in (-80, 60): d.rounded_rectangle(R(x, -55, x + 20, 55), int(8 * k), fill=W)
    for x in (-100, 85): d.rounded_rectangle(R(x, -32, x + 15, 32), int(6 * k), fill=W)


def clapper(d, P, R, k, c):
    d.rectangle(R(-85, -20, 85, 85), fill=W); d.polygon([P(-88, -30), P(80, -78), P(86, -58), P(-82, -10)], fill=W)
    for x in (-50, 0, 50): d.line([P(x, -60 + -x * 0.28), P(x + 20, -30 + -x * 0.28)], fill=c, width=int(9 * k))


def cart(d, P, R, k, c):
    d.line([P(-100, -75), P(-70, -75), P(-45, 40), P(75, 40)], fill=W, width=int(12 * k), joint="curve")
    d.polygon([P(-65, -50), P(95, -50), P(80, 15), P(-52, 15)], fill=W); d.ellipse(R(-50, 55, -20, 85), fill=W); d.ellipse(R(45, 55, 75, 85), fill=W)


def anchor(d, P, R, k, c):
    lw = int(14 * k)
    d.ellipse(R(-22, -100, 22, -56), outline=W, width=int(12 * k)); d.line([P(0, -56), P(0, 85)], fill=W, width=lw); d.line([P(-45, -30), P(45, -30)], fill=W, width=lw)
    d.arc(R(-80, -5, 80, 90), 0, 180, fill=W, width=lw); d.polygon([P(-95, 30), P(-65, 25), P(-80, 55)], fill=W); d.polygon([P(95, 30), P(65, 25), P(80, 55)], fill=W)


def bike(d, P, R, k, c):
    lw = int(9 * k)
    for x in (-55, 55): d.ellipse(R(x - 42, -2, x + 42, 82), outline=W, width=lw)
    d.line([P(-55, 40), P(-15, -30), P(40, -30), P(55, 40)], fill=W, width=lw, joint="curve"); d.line([P(-15, -30), P(0, 40), P(40, -30)], fill=W, width=lw)
    d.line([P(-30, -50), P(0, -50)], fill=W, width=lw); d.line([P(40, -30), P(30, -60), P(55, -60)], fill=W, width=lw)


def wifi(d, P, R, k, c):
    for r in (40, 75, 110): d.arc(R(-r, 50 - r, r, 50 + r), 225, 315, fill=W, width=int(16 * k))
    d.ellipse(R(-16, 34, 16, 66), fill=W)


for args in [("Aeroporto", TRANSP, airplane), ("Estação de ônibus", TRANSP, bus), ("Estação de trem", TRANSP, train), ("Estação de metrô", TRANSP, metro),
             ("Estacionamento", TRANSP, T("P")), ("Ponto de táxi", (240, 180, 20, 255), T("TÁXI", 130)), ("Porto", TRANSP, anchor), ("Ciclovia", NAT, bike),
             ("Posto de combustível", TRANSP, fuel),
             ("Museu", CULT, museum), ("Teatro", CULT, mask), ("Cinema", CULT, clapper), ("Biblioteca", CULT, books), ("Escola", CULT, pencil),
             ("Universidade", CULT, grad_cap), ("Cassino", CULT, casino),
             ("Bar", FOOD, bar), ("Restaurante", FOOD, restaurant), ("Cafeteria", FOOD, cafe),
             ("Bombeiros", SERV, flame), ("Polícia", (35, 60, 140, 255), police), ("Hospital", SERV, hospital), ("Farmácia", NAT, pill),
             ("Correios", (240, 170, 20, 255), envelope), ("Embaixada", SERV, flag), ("Prefeitura", (90, 100, 120, 255), city_hall),
             ("Informações", TRANSP, T("i", 320)), ("Banco", NAT, T("$", 300)), ("Wi-Fi", TRANSP, wifi),
             ("Hotel", (70, 90, 200, 255), hotel), ("Casa", FOOD, house), ("Complexo industrial", (110, 110, 125, 255), factory),
             ("Praia", (30, 170, 210, 255), beach), ("Aquário", (30, 170, 210, 255), fish), ("Parque", NAT, tree), ("Zoológico", NAT, paw),
             ("Shopping center", SHOP, bag), ("Supermercado", SHOP, cart), ("Complexo esportivo", NAT, ball), ("Academia", SERV, dumbbell),
             ("Torre de rádio", (90, 100, 120, 255), radio_tower), ("Torre de energia elétrica", (90, 100, 120, 255), pylon)]:
    poi(*args)


n = len([f for f in os.listdir(OUT) if f.endswith('.png')])
print(n)
