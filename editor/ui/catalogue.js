// Recensement des objets du jeu (demande de JD, 20/10) : un nom generique dans les quatre langues de l'editeur pour chaque
// objet de la carte preparee (« rowhouse_snow », « kerrostalo », « RallyTent » -> Maison, Maison, Tente), et une recherche
// par debut de mot (« Mai » trouve Maison), sans majuscules ni accents. Les noms du jeu sont decoupes en mots
// (souligne, tiret, chiffres, majuscules) ; une regle reconnait un mot qui COMMENCE par l'une de ses cles. Le nom de
// l'objet d'abord, puis ceux de ses parents (« .../MailBox/BoxHatch/Pivot/hatchmesh » : boite aux lettres) quand le nom
// ne dit rien (mesh, cube, piece...). Les morceaux sans interet (ecrans de l'ordinateur, squelettes des personnages,
// interface) ne sont pas proposes.
//   [id, fr, en, ru, ar, cles]
const CATS = [
  // batiments
  ["maison", "Maison", "House", "Дом", "منزل", "house rowhouse talo kerrostalo cottage cabin flats rintama houseuncle mökki mokki apartments terracehouse"],
  ["garage", "Garage", "Garage", "Гараж", "مرآب", "garage carage"],
  ["sauna", "Sauna", "Sauna", "Сауна", "ساونا", "sauna saunahouse"],
  ["cabane", "Cabane, abri", "Shed, hut", "Сарай, хижина", "كوخ، سقيفة", "shed hut shithouse outhouse kennel woodshed latsa dancehut lato"],
  ["serre", "Serre", "Greenhouse", "Теплица", "دفيئة", "greenhouse"],
  ["etable", "Étable, ferme", "Barn, farm", "Хлев, ферма", "حظيرة، مزرعة", "cowhouse barn farm navetta"],
  ["eglise", "Église", "Church", "Церковь", "كنيسة", "church kirkko"],
  ["ecole", "École", "School", "Школа", "مدرسة", "school koulu"],
  ["magasin", "Magasin", "Shop", "Магазин", "متجر", "store shop kauppa osuus teimo cash register"],
  ["bar", "Bar, restaurant", "Bar, restaurant", "Бар, ресторан", "حانة، مطعم", "pub diner bar grill"],
  ["usine", "Usine, entrepôt", "Factory, warehouse", "Завод, склад", "مصنع، مستودع", "factory facility warehouse hall"],
  ["silo", "Silo", "Silo", "Силос", "صومعة", "silo"],
  ["tour", "Tour", "Tower", "Башня", "برج", "tower"],
  ["prison", "Prison", "Jail", "Тюрьма", "سجن", "jail prison"],
  ["batiment", "Bâtiment", "Building", "Здание", "مبنى", "building buildings office kunnalliskoti theatre barrack hospital station"],
  // parties de batiment
  ["toit", "Toit", "Roof", "Крыша", "سقف", "roof roofpanel roofline carageroof"],
  ["mur", "Mur", "Wall", "Стена", "جدار", "wall walls logwall brickwall churchwall caragewalls plaster wallpaper whitebrick brick messybrick"],
  ["porte", "Porte", "Door", "Дверь", "باب", "door doors doorrear ovi gate hatch hatchmesh"],
  ["fenetre", "Fenêtre, vitre", "Window, glass", "Окно, стекло", "نافذة، زجاج", "window windows glass carageglass ikkuna"],
  ["plancher", "Sol, plancher", "Floor", "Пол", "أرضية", "floor woodfloor lattia tiles"],
  ["escalier", "Escalier", "Stairs", "Лестница", "درج", "stairs stair"],
  ["cheminee", "Cheminée", "Chimney", "Дымоход", "مدخنة", "chimney"],
  ["terrasse", "Terrasse", "Terrace", "Терраса", "شرفة", "terrace porch"],
  // routes et decor
  ["route", "Route", "Road", "Дорога", "طريق", "road asphalt highway dirtroad gravel roadside pavement"],
  ["pont", "Pont", "Bridge", "Мост", "جسر", "bridge"],
  ["rail", "Voie ferrée, train", "Railway, train", "Железная дорога, поезд", "سكة حديد، قطار", "railroad rails rail train carriage"],
  ["ponton", "Ponton, quai", "Pier, dock", "Причал, пирс", "رصيف، مرسى", "pier pontons ponton dock"],
  ["panneau", "Panneau", "Sign", "Знак", "لافتة", "sign signs noparking billboard timetable info arrows"],
  ["banderole", "Banderole, affiche", "Banner, poster", "Баннер, плакат", "لافتة قماشية، ملصق", "banner banners rallybanner lippusiima poster posters"],
  ["drapeau", "Drapeau", "Flag", "Флаг", "علم", "flag"],
  ["poteau", "Poteau", "Pole", "Столб", "عمود", "pole poles concretepole post"],
  ["lampadaire", "Lampadaire", "Street light", "Фонарь", "عمود إنارة", "lightpole yardlight streetlight"],
  ["electricite", "Ligne électrique", "Power line", "Линия электропередачи", "خط كهرباء", "elec electric power wire"],
  ["cloture", "Clôture", "Fence", "Забор", "سياج", "fence aita"],
  ["haie", "Haie", "Hedge", "Живая изгородь", "سياج نباتي", "hedge"],
  ["tente", "Tente", "Tent", "Палатка", "خيمة", "tent rallytent"],
  ["caravane", "Caravane, remorque", "Caravan, trailer", "Караван, прицеп", "كرفان، مقطورة", "caravan trailer"],
  ["puits", "Puits", "Well", "Колодец", "بئر", "well wastewell"],
  ["bois", "Tas de bois, bûches", "Log pile, logs", "Дрова, брёвна", "كومة حطب، جذوع", "log logs logpile pile plank planks firewood"],
  ["rocher", "Rocher, pierre", "Rock, stone", "Скала, камень", "صخرة، حجر", "rock rocks stone kivi"],
  ["arbre", "Arbre", "Tree", "Дерево", "شجرة", "tree trees treewall kuusi forest mänty manty koivu"],
  ["buisson", "Buisson", "Bush", "Куст", "شجيرة", "bush bushes"],
  ["herbe", "Herbe, plantes", "Grass, plants", "Трава, растения", "عشب، نباتات", "grass veg flower flowers plant"],
  ["eau", "Eau, lac", "Water, lake", "Вода, озеро", "ماء، بحيرة", "water lake swamp"],
  ["neige", "Neige, glace", "Snow, ice", "Снег, лёд", "ثلج، جليد", "snow snowpile snowbank ice"],
  ["sol", "Terrain", "Terrain", "Местность", "تضاريس", "terrain ground maa maasto field"],
  ["circuit", "Circuit, piste", "Track, race course", "Трасса", "حلبة، مضمار", "trackfield startline dragstrip finishline"],
  ["chantier", "Matériaux, chantier", "Building materials", "Стройматериалы", "مواد بناء", "slabs concrete pipes cableroll pressu tarp"],
  ["cimetiere", "Cimetière, tombe", "Graveyard, grave", "Кладбище, могила", "مقبرة، قبر", "hautausmaa grave memorial cross"],
  // vehicules
  ["voiture", "Voiture", "Car", "Машина", "سيارة", "car staticcar auto lamore polsa sedan fury fittan victro"],
  ["camion", "Camion, bus", "Truck, bus", "Грузовик, автобус", "شاحنة، حافلة", "truck bus van sisu flatbed"],
  ["tracteur", "Tracteur, engin", "Tractor, machine", "Трактор, техника", "جرار، آلية", "tractor valmet combine"],
  ["velo", "Vélo, moto", "Bike, motorbike", "Велосипед, мотоцикл", "دراجة", "bike bicycle moped"],
  ["roue", "Roue, pneu", "Wheel, tyre", "Колесо, шина", "عجلة، إطار", "tire tyre rim rims wheel"],
  ["piece", "Pièce mécanique", "Car part", "Запчасть", "قطعة غيار", "axle shaft drumbrake brake engine piston susp chassis bootlid hood clutch gear"],
  ["bateau", "Bateau", "Boat", "Лодка", "قارب", "boat"],
  ["avion", "Avion, aéroport", "Plane, airport", "Самолёт, аэропорт", "طائرة، مطار", "plane airport"],
  // meubles
  ["chaise", "Chaise, banc", "Chair, bench", "Стул, скамья", "كرسي، مقعد", "chair bench seat seats saunabench lauteet stool"],
  ["canape", "Canapé, fauteuil", "Sofa, armchair", "Диван, кресло", "أريكة", "sofa armchair couch"],
  ["table", "Table, bureau", "Table, desk", "Стол", "طاولة، مكتب", "table tabledoors desk pooltable"],
  ["lit", "Lit, matelas", "Bed, mattress", "Кровать, матрас", "سرير، فراش", "bed boybed bedtabl mattress matress mattres pillow makuupussi"],
  ["etagere", "Étagère, placard", "Shelf, cupboard", "Полка, шкаф", "رف، خزانة", "shelf bookshelf closet locker cabinet cupboard drawer"],
  ["cuisine", "Cuisine, électroménager", "Kitchen, appliance", "Кухня, техника", "مطبخ، أجهزة", "kitchen fridge freezer oven microwave stove woodstove coffee kahvinkeitin sink tap"],
  ["salle-bain", "Toilettes, salle de bain", "Toilet, bathroom", "Туалет, ванная", "مرحاض، حمام", "toilet wcbowl wc shower bathroom bathtub"],
  ["ecran", "Télé, ordinateur, radio", "TV, computer, radio", "Телевизор, компьютер, радио", "تلفاز، حاسوب، راديو", "tv tvscreen monitor computer radio stereos stereo speaker phone modem keyboard"],
  ["lampe", "Lampe", "Lamp", "Лампа", "مصباح", "lamp light lights bulb bulbs toiletlamp led chandelier"],
  ["rideau", "Rideau, tapis", "Curtain, rug", "Штора, ковёр", "ستارة، سجادة", "curtain carpet rug ryijy"],
  ["tableau", "Tableau, cadre", "Painting, frame", "Картина, рамка", "لوحة، إطار", "painting picture frames photo"],
  ["horloge", "Horloge", "Clock", "Часы", "ساعة", "clock"],
  ["radiateur", "Radiateur, chauffage", "Radiator, heater", "Радиатор, отопление", "مدفأة", "radiator heating heater boiler hiillos"],
  ["piano", "Piano, instrument", "Piano, instrument", "Пианино, инструмент", "بيانو، آلة موسيقية", "piano drumset micstand guitar synth"],
  // objets
  ["bouteille", "Bouteille", "Bottle", "Бутылка", "زجاجة", "bottle bottles beer kaljapullo olut vodka pullo booze soda sourmilk"],
  ["caisse", "Caisse, carton", "Crate, box", "Ящик, коробка", "صندوق، كرتون", "box boxes boxmesh crate berrybox potatobox container cardboard package packages case"],
  ["palette", "Palette", "Pallet", "Поддон", "منصة نقالة", "pallet"],
  ["bidon", "Bidon, fût, réservoir", "Can, barrel, tank", "Канистра, бочка, бак", "جالون، برميل، خزان", "barrel can gasoline tank fueloil fuel canister jerrycan"],
  ["poubelle", "Poubelle, déchets", "Bin, rubbish", "Мусор, урна", "سلة مهملات، نفايات", "garbage bin waste trash junk kaatopaikka komposti landfill"],
  ["boite-lettres", "Boîte aux lettres", "Mailbox", "Почтовый ящик", "صندوق بريد", "mailbox envelopemesh"],
  ["pompe", "Pompe à essence", "Fuel pump", "Топливная колонка", "مضخة وقود", "pump pumps"],
  ["machine", "Machine, outil", "Machine, tool", "Станок, инструмент", "آلة، أداة", "machine dyno lift lifter tool tools equipment anchorlift"],
  ["cible", "Cible", "Target", "Мишень", "هدف", "target"],
  ["sport", "Sport, jeu", "Sport, game", "Спорт, игра", "رياضة، لعبة", "ski ball hoop sport toys"],
  ["animal", "Animal", "Animal", "Животное", "حيوان", "chicken cow pig bird cat fish dog horse moose"],
  ["personnage", "Personnage", "Character", "Персонаж", "شخصية", "man npc granny grandma santa joulupukki"],
  ["vetement", "Vêtement", "Clothes", "Одежда", "ملابس", "clothes hat beanie karvalakki trousers suit jacket shirt eyewear wear glasses"],
  ["nourriture", "Nourriture", "Food", "Еда", "طعام", "food sausage sausages fries frozenfish coffee bread"],
  ["livre", "Livre, papier", "Book, paper", "Книга, бумага", "كتاب، ورق", "books book paper papers magazine magazines brochure folders sheets results registerform recipiet"],
  ["electrique", "Interrupteur, prise, fusible", "Switch, socket, fuse", "Выключатель, розетка", "مفتاح، مقبس", "switch socket fuse fusetable meter"],
];
// noms qui ne disent rien : on regarde les parents
const VAGUE = new Set("mesh cube quad piece pieces inner outer base frame body cap hinge part parts lod pivot anim obj object model main top bottom left right front rear back side upper lower middle small large big none default new old open closed collider shadowcollider handle handles lid cover plate panel panels holder strips stick ring rod gfx mask shaft knob knobs".split(" "));
// morceaux jamais proposes (interface, ecrans de l'ordinateur, squelettes)
const JUNK_PATH = /(^|\/)(COMPUTER\/SYSTEM|GUI|HUD|Systems|Sheets)(\/|$)|\/skeleton\/|\/Char\/|\/(bodymesh|headmesh)$/i;
const JUNK_NAME = /^(screenmask|tausta|bg|bgcolor|titlescreen|intro|ending|menukuvake|menuobjekti|teksti|puhekupla|naama|gunflame|headshot|explosion|pokeri|money|paymoney|pricemoney)/i;

const LANGS = { fr: 1, en: 2, ru: 3, ar: 4 };
const fold = s => String(s || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
const words = s => fold(s).replace(/ё/g, "е").split(/[^a-z0-9а-яа-я؀-ۿäöå]+/).filter(Boolean);
const tokens = n => String(n || "").replace(/([a-zäöå])([A-Z])/g, "$1 $2").toLowerCase().split(/[^a-zäöå]+/).filter(t => t.length > 1);
const RULES = CATS.map(c => ({ id: c[0], names: c.slice(1, 5), keys: c[5].split(" ") }));

// le mot qui compte est le dernier (« house_door1 » : une porte, « rintama_roof » : un toit, « chicken_house » : une maison...)
// sauf quelques categories qui l'emportent ou que soit leur mot (« sign_road2 » : un panneau, « garage_shelf_flats » : une etagere)
const STRONG = new Set(["panneau", "banderole", "drapeau", "etagere", "boite-lettres", "lampadaire", "cible", "piano"]);
function ruleFor(toks) {
  for (const r of RULES) if (STRONG.has(r.id)) for (const t of toks) for (const k of r.keys) if (t.startsWith(k)) return r;
  for (let i = toks.length - 1; i >= 0; i--) for (const r of RULES) for (const k of r.keys) if (toks[i].startsWith(k)) return r;
  return null;
}

// categorie d'un objet du cache : { id, names } ou null (autre), "junk" pour les morceaux jamais proposes
export function classify(name, path) {
  if (JUNK_NAME.test(name || "") || JUNK_PATH.test(path || "")) return "junk";
  const own = tokens(name).filter(t => !VAGUE.has(t));
  let r = own.length ? ruleFor(own) : null;
  if (!r && path) {
    const segs = path.split("/").map(s => s.replace(/#\d+$/, ""));
    for (let i = segs.length - 2; i >= 0 && !r; i--) {
      const t = tokens(segs[i]).filter(x => !VAGUE.has(x));
      if (t.length) r = ruleFor(t);
    }
  }
  return r;
}

// catalogue d'une carte preparee : categories (avec leurs variantes : un nom du jeu, son nombre, un objet a copier)
export function buildCatalogue(objets) {
  const cats = new Map(), other = { id: "autre", names: ["Autres", "Other", "Прочее", "أخرى"], vars: new Map(), n: 0 };
  objets.forEach((o, i) => {
    if (!o.p) return;
    const r = classify(o.n, o.p);
    if (r === "junk") return;
    let c = other;
    if (r) { c = cats.get(r.id); if (!c) { c = { id: r.id, names: r.names, vars: new Map(), n: 0 }; cats.set(r.id, c); } }
    c.n++;
    const v = c.vars.get(o.n);
    if (v) v.c++; else c.vars.set(o.n, { n: o.n, i, k: o.k, c: 1, toks: tokens(o.n).map(fold) });
  });
  const order = RULES.map(r => r.id);
  const list = [...cats.values()].sort((a, b) => order.indexOf(a.id) - order.indexOf(b.id));
  if (other.n) list.push(other);
  for (const c of list) c.vars = [...c.vars.values()].sort((a, b) => b.c - a.c || a.n.localeCompare(b.n));
  return list;
}

export function catName(c, lang) { return c.names[(LANGS[lang] || 2) - 1] || c.names[1]; }

// recherche : chaque mot tape doit etre le debut d'un mot du nom generique (langue de l'editeur, ou anglais), ou d'un mot
// du nom du jeu ; rend [{ cat, vars, all }] -- all : la categorie entiere (son nom correspond)
export function search(list, q, lang) {
  const qs = words(q);
  if (!qs.length) return list.map(c => ({ cat: c, vars: c.vars, all: true }));
  const res = [];
  for (const c of list) {
    const label = words(catName(c, lang) + " " + c.names[1] + " " + c.names[0]);
    if (qs.every(w => label.some(l => l.startsWith(w)))) { res.push({ cat: c, vars: c.vars, all: true }); continue; }
    const vars = c.vars.filter(v => qs.every(w => v.toks.some(t => t.startsWith(w)) || label.some(l => l.startsWith(w))));
    if (vars.length) res.push({ cat: c, vars, all: false });
  }
  return res;
}
