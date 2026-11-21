/* Hearth · Compra: conceptes genèrics ("Ous", "Plàtans") per triar com a la caixa d'un restaurant.
   El servidor els busca a les botigues configurades i diu on són més barats.
   Columnes: categoria, emoji, català, castellà, portuguès, anglès, francès, extra.
   - A cada idioma, "a|b" vol dir "a o b" (només el primer es mostra).
   - A extra: -paraula exclou productes amb aquesta paraula; @categoria obliga la categoria de la botiga.
   Ordenats pel que es compra més sovint. Per afegir-ne, n'hi ha prou amb una fila nova. */
var CONCEPTES = [
  /* Fruita i verdura */
  ['fruita', '🍌', 'Bananes', 'Plátanos', 'Banana', 'Bananas', 'Bananes', '@fruita -chips -seca -bebida -iogurte -yogur -bolsa -saqueta -puré ~madeira'],
  ['fruita', '🍊', 'Mandarines', 'Mandarinas', 'Mandarina|Tangerina', 'Mandarins', 'Mandarines', '@fruita -sumo -zumo -iogurte'],
  ['fruita', '🍑', 'Préssecs', 'Melocotones', 'Pêssego', 'Peaches', 'Pêches', '@fruita -calda -almíbar -iogurte -sumo -néctar ~paraguaio'],
  ['fruita', '🥒', 'Cogombre', 'Pepino', 'Pepino', 'Cucumber', 'Concombre', '@fruita'],
  ['fruita', '🥦', 'Bròquil', 'Brócoli', 'Brócolo|Brócolos', 'Broccoli', 'Brocoli', '@fruita ~ilhas'],
  ['fruita', '🥕', 'Pastanagues', 'Zanahorias', 'Cenoura', 'Carrots', 'Carottes', '@fruita -sumo -ralada'],
  ['fruita', '🍅', 'Tomàquet cherry', 'Tomate cherry', 'Tomate cherry|Tomate mini|Tomate cereja', 'Cherry tomatoes', 'Tomates cerises', '@fruita ~mini ~alongado ~pingo ~500'],
  ['fruita', '🍈', 'Meló', 'Melón', 'Melão|Meloa', 'Melon', 'Melon', '@fruita ~branco'],
  ['fruita', '🍉', 'Síndria', 'Sandía', 'Melancia', 'Watermelon', 'Pastèque', '@fruita'],
  ['fruita', '🍑', 'Prunes', 'Ciruelas', 'Ameixa', 'Plums', 'Prunes', '@fruita -seca -passa ~amarela'],
  ['fruita', '🍑', 'Albercocs', 'Albaricoques', 'Alperce|Damasco', 'Apricots', 'Abricots', '@fruita -seco -seca'],
  ['fruita', '🍇', 'Raïm', 'Uvas', 'Uva', 'Grapes', 'Raisin', '@fruita -passa ~honey'],
  ['fruita', '🍎', 'Pomes', 'Manzanas', 'Maçã', 'Apples', 'Pommes', '@fruita -sumo -bolsa -puré ~gala'],
  ['fruita', '🫛', 'Mongeta tendra', 'Judías verdes', 'Feijão verde', 'Green beans', 'Haricots verts', '@fruita ~madeira'],
  ['fruita', '🧅', 'Ceba', 'Cebolla', 'Cebola', 'Onions', 'Oignons', '@fruita -frita ~madeira'],
  ['fruita', '🥬', 'Amanida', 'Ensalada|Lechuga', 'Salada|Alface', 'Salad', 'Salade', '@fruita -fruta -molho'],
  ['fruita', '🥬', 'Ruca', 'Rúcula', 'Rúcula', 'Rocket', 'Roquette', '@fruita ~pingo'],
  ['fruita', '🥑', 'Alvocat', 'Aguacate', 'Abacate', 'Avocado', 'Avocat', '@fruita -guacamole'],
  ['fruita', '🥔', 'Patates', 'Patatas', 'Batata', 'Potatoes', 'Pommes de terre', '@fruita -doce -frita'],
  /* Congelats (sobretot verdura) */
  ['congelats', '🥦', 'Bròquil congelat', 'Brócoli congelado', 'Brócolos', 'Frozen broccoli', 'Brocoli surgelé', '@congelats ~pingo ~400'],
  ['congelats', '🍄', 'Xampinyons congelats', 'Champiñones congelados', 'Cogumelos', 'Frozen mushrooms', 'Champignons surgelés', '@congelats ~gelgampo'],
  ['congelats', '🍚', 'Arròs de verdures', 'Arroz de verduras', 'Veggie rice|Arroz de couve|Arroz de legumes', 'Veggie rice', 'Riz de légumes', '@congelats ~pingo'],
  ['congelats', '🥦', 'Coliflor congelada', 'Coliflor congelada', 'Couve-flor|Couve flor', 'Frozen cauliflower', 'Chou-fleur surgelé', '@congelats ~pingo'],
  ['congelats', '🧅', 'Ceba congelada', 'Cebolla congelada', 'Cebola', 'Frozen onion', 'Oignon surgelé', '@congelats ~pingo'],
  ['congelats', '🥬', 'Espinacs congelats', 'Espinacas congeladas', 'Espinafres', 'Frozen spinach', 'Épinards surgelés', '@congelats ~folha ~pingo'],
  ['congelats', '🫛', 'Pèsols i pastanaga', 'Guisantes', 'Ervilhas', 'Peas', 'Petits pois', '@congelats ~cenoura ~pingo'],
  ['congelats', '🐟', 'Peix congelat', 'Pescado congelado', 'Pescada|Paloco|Tamboril', 'Frozen fish', 'Poisson surgelé', '@congelats -panado -empanado ~paloco ~pacifico'],
  ['congelats', '🍦', 'Gelats', 'Helados', 'Gelado|Cornetto', 'Ice cream', 'Glaces', '@congelats ~olá ~cornetto'],
  /* Formatges i embotit */
  ['xarcuteria', '🧀', 'Formatge ratllat', 'Queso rallado', 'Queijo ralado', 'Grated cheese', 'Fromage râpé', '~ilha ~pingo ~200'],
  ['xarcuteria', '🧀', 'Formatge en llesques', 'Queso en lonchas', 'Queijo flamengo fatiado|Queijo fatiado', 'Sliced cheese', 'Fromage en tranches', '-fundido ~flamengo ~2x200'],
  ['xarcuteria', '🧀', 'Formatge fos', 'Queso fundido', 'Queijo fundido', 'Processed cheese', 'Fromage fondu', '~pingo ~porções'],
  ['xarcuteria', '🧀', 'Mozzarella', 'Mozzarella', 'Mozzarella', 'Mozzarella', 'Mozzarella', '-pizza ~light'],
  ['xarcuteria', '🧀', 'Formatge cremós', 'Queso crema', 'Queijo creme|Philadelphia', 'Cream cheese', 'Fromage à tartiner', '~philadelphia ~original'],
  ['xarcuteria', '🌭', 'Xoriço', 'Chorizo', 'Chouriço|Chourição', 'Chorizo', 'Chorizo', '-sabor ~chourição ~extra'],
  ['xarcuteria', '🍖', 'Pernil dolç', 'Jamón cocido', 'Fiambre|Paio york', 'Ham', 'Jambon', '-peru -frango -pavo ~paio ~york'],
  ['xarcuteria', '🐟', 'Salmó fumat', 'Salmón ahumado', 'Salmão fumado', 'Smoked salmon', 'Saumon fumé', '~noruega ~pingo'],
  /* Pica-pica */
  ['snacks', '🌽', 'Nachos', 'Nachos', 'Nachos|Tortilla chips', 'Nachos', 'Nachos', '-molho -salsa ~sal ~pingo ~300'],
  ['snacks', '🍘', 'Tortitas', 'Tortitas', 'Tortitas|Tostas de grão|Bolachas de arroz', 'Rice cakes', 'Galettes', '~grão ~pingo'],
  ['snacks', '🥜', 'Fruits secs', 'Frutos secos', 'Frutos secos|Mistura frutos', 'Mixed nuts', 'Fruits secs', '~mistura ~pingo'],
  ['snacks', '🥜', 'Cacauets', 'Cacahuetes', 'Amendoim', 'Peanuts', 'Cacahuètes', '-manteiga -crema -creme ~casca ~pingo'],
  ['snacks', '🍫', 'Barretes de cereals', 'Barritas de cereales', 'Barra cereais|Barra amêndoa|Barras', 'Cereal bars', 'Barres de céréales', '~amêndoa ~avelã ~pingo'],
  ['snacks', '🍎', 'Bossetes de fruita', 'Bolsitas de fruta', 'Bolsa fruta|Saqueta fruta|Puré de fruta|Bolsa maçã', 'Fruit pouches', 'Gourdes de fruits', '~maçã ~banana ~pingo'],
  ['snacks', '🥣', 'Cereals de xocolata', 'Cereales de chocolate', 'Cereais chocolate', 'Chocolate cereal', 'Céréales chocolat', '-barra ~nestlé ~proteína'],
  ['snacks', '🫒', 'Olives', 'Aceitunas', 'Azeitona|Azeitonas', 'Olives', 'Olives', '-azeite -pasta -patê ~verde'],
  ['snacks', '🥔', 'Patates xips', 'Patatas fritas', 'Batata frita|Batatas fritas|Palha', 'Crisps', 'Chips', '@rebost -congelad ~palha'],
  /* Rebost */
  ['rebost', '🫘', 'Mongetes cuites', 'Alubias cocidas', 'Feijão', 'Beans', 'Haricots', '@rebost -verde ~pingo'],
  ['rebost', '🫘', 'Cigrons cuits', 'Garbanzos cocidos', 'Grão de bico|Grão-de-bico', 'Chickpeas', 'Pois chiches', '@rebost -húmus -hummus -tostas -tortitas ~pingo'],
  ['rebost', '🐟', 'Tonyina', 'Atún', 'Atum', 'Tuna', 'Thon', '@rebost -patê -pate -salada ~posta'],
  ['rebost', '🐟', 'Sardines', 'Sardinas', 'Sardinha|Sardinhas', 'Sardines', 'Sardines', '@rebost -patê ~dorita ~óleo'],
  ['rebost', '🍅', 'Tomàquet triturat', 'Tomate triturado', 'Polpa de tomate|Tomate pelado', 'Chopped tomatoes', 'Tomates concassées', '~polpa ~pingo'],
  ['rebost', '🍝', 'Pasta', 'Pasta', 'Massa|Esparguete|Fusilli', 'Pasta', 'Pâtes', '@rebost -folhada -pizza -bolo -quebrada -molho ~fusilli ~pingo'],
  ['rebost', '🍚', 'Arròs', 'Arroz', 'Arroz', 'Rice', 'Riz', '@rebost -bolacha -tortitas -bebida -doce ~vaporizado ~pingo'],
  ['rebost', '☕', 'Cafè', 'Café', 'Café|Mistura', 'Coffee', 'Café', '@rebost -iogurte -bolacha -licor ~mistura'],
  ['rebost', '🌾', 'Farina', 'Harina', 'Farinha', 'Flour', 'Farine', '~fermento ~pingo'],
  ['rebost', '🍬', 'Sucre', 'Azúcar', 'Açúcar', 'Sugar', 'Sucre', '-sem -sin -zero -bolacha -chocolate ~natural'],
  ['rebost', '🫒', 'Oli d\'oliva', 'Aceite de oliva', 'Azeite', 'Olive oil', 'Huile d\'olive', '-spray -atum -sardinha'],
  /* Vegetarià */
  ['vegetal', '🌭', 'Salsitxes vegetals', 'Salchichas vegetales', 'Salsichas soja|Salsichas vegetais|Salsichas vegan', 'Veggie sausages', 'Saucisses végétales', '~vegalia ~soja'],
  ['vegetal', '🧆', 'Falafel', 'Falafel', 'Falafel', 'Falafel', 'Falafel', '~pingo'],
  ['vegetal', '🫘', 'Hummus', 'Hummus', 'Hummus|Húmus', 'Hummus', 'Houmous', '~pingo'],
  ['vegetal', '🌿', 'Pesto', 'Pesto', 'Pesto', 'Pesto', 'Pesto', '~vegan ~pingo'],
  ['vegetal', '🫘', 'Soja texturitzada', 'Soja texturizada', 'Soja granulada|Soja texturizada|Proteína de soja', 'Textured soy', 'Soja texturé', '~izidoro'],
  ['vegetal', '🥛', 'Nata vegetal', 'Nata vegetal', 'Creme vegetal|Creme de soja|Natas soja', 'Plant cream', 'Crème végétale', '~soja ~light ~pingo'],
  /* Làctics */
  ['lactis', '🍶', 'Iogurt líquid', 'Yogur líquido', 'Iogurte líquido', 'Drinking yoghurt', 'Yaourt à boire', '~pingo'],
  ['lactis', '🍶', 'Iogurt grec', 'Yogur griego', 'Iogurte grego|Oikos', 'Greek yoghurt', 'Yaourt grec', '~oikos ~natural'],
  ['lactis', '🥛', 'Llet', 'Leche semidesnatada', 'Leite meio gordo', 'Semi-skimmed milk', 'Lait demi-écrémé', '@lactis -chocolate -achocolatado -cacau -infantil -pó -sabor ~pingo'],
  ['lactis', '🥛', 'Llet amb xocolata', 'Batido de chocolate', 'Leite chocolate|Leite com chocolate|Achocolatado', 'Chocolate milk', 'Lait chocolaté', '~pingo'],
  ['lactis', '🥚', 'Ous', 'Huevos', 'Ovos', 'Eggs', 'Oeufs', '-chocolate -kinder -páscoa -pascoa -codorniz -massa -mexidos -bolacha -bolachas -galleta -dinosaurus -biscoito'],
  ['lactis', '🧈', 'Mantega', 'Mantequilla', 'Manteiga', 'Butter', 'Beurre', '-amendoim -bolacha'],
  /* Pa */
  ['pa', '🥖', 'Panets', 'Panecillos', 'Carcaça|Pão padeirinho|Papo-seco', 'Bread rolls', 'Petits pains', '~carcaça ~aldeia'],
  ['pa', '🍞', 'Pa de llet', 'Pan de leche', 'Pão de leite', 'Milk bread', 'Pain au lait', ''],
  ['pa', '🧀', 'Pa de formatge', 'Pan de queso', 'Pão de queijo', 'Cheese bread', 'Pain au fromage', ''],
  ['pa', '🍞', 'Pa de motlle', 'Pan de molde', 'Pão de forma', 'Sliced bread', 'Pain de mie', ''],
  ['pa', '🥐', 'Croissants', 'Cruasanes', 'Croissant', 'Croissants', 'Croissants', ''],
  /* Begudes */
  ['begudes', '🧃', 'Sucs', 'Zumos', 'Sumo|Néctar|Compal|Um Bongo', 'Juice', 'Jus', '@begudes ~compal ~bongo'],
  ['begudes', '💧', 'Aigua', 'Agua', 'Água', 'Water', 'Eau', '@begudes -gás -gas -sabor -tónica -coco ~6'],
  ['begudes', '🍺', 'Cervesa', 'Cerveza', 'Cerveja|Coral', 'Beer', 'Bière', '@begudes -sem -sin ~coral'],
  ['begudes', '🍷', 'Vi', 'Vino', 'Vinho', 'Wine', 'Vin', '@begudes -vinagre ~tinto'],
  ['begudes', '🥤', 'Refresc de cola', 'Refresco de cola', 'Coca-Cola|Cola', 'Cola', 'Cola', '@begudes ~coca ~zero'],
  /* Carn i peix fresc */
  ['carn', '🍖', 'Mandonguilles', 'Albóndigas', 'Almôndegas', 'Meatballs', 'Boulettes', '~porco'],
  ['carn', '🍗', 'Pit de pollastre', 'Pechuga de pollo', 'Peito de frango|Bife de frango', 'Chicken breast', 'Blanc de poulet', '-panado -nuggets -fiambre'],
  ['carn', '🥩', 'Carn picada', 'Carne picada', 'Carne picada', 'Minced meat', 'Viande hachée', ''],
  ['peix', '🐟', 'Salmó', 'Salmón', 'Salmão', 'Salmon', 'Saumon', '-fumado -ahumado -patê'],
  ['peix', '🐟', 'Rap', 'Rape', 'Tamboril', 'Monkfish', 'Lotte', ''],
  /* Neteja i casa */
  ['neteja', '🧻', 'Paper higiènic', 'Papel higiénico', 'Papel higiénico', 'Toilet paper', 'Papier toilette', '-húmido -humedo ~pingo ~compacto'],
  ['neteja', '🧻', 'Paper de cuina', 'Papel de cocina', 'Rolo de cozinha|Rolos de cozinha', 'Kitchen roll', 'Essuie-tout', '~pingo'],
  ['neteja', '🚽', 'Blocs pel WC', 'Bloques WC', 'Bloco sanitário|Bloco WC', 'Toilet blocks', 'Blocs WC', '~ultra'],
  ['neteja', '🧽', 'Netejador multiús', 'Limpiador multiusos', 'Multiusos|Ajax|Cif', 'All-purpose cleaner', 'Nettoyant multi-usages', '~ajax ~cif'],
  ['neteja', '🎞️', 'Paper film', 'Film transparente', 'Película aderente', 'Cling film', 'Film étirable', '~pingo'],
  ['neteja', '🗑️', 'Bosses d\'escombraries', 'Bolsas de basura', 'Sacos do lixo|Sacos lixo', 'Bin bags', 'Sacs poubelle', ''],
  ['neteja', '🧴', 'Rentaplats', 'Lavavajillas', 'Detergente loiça|Detergente da loiça', 'Washing-up liquid', 'Liquide vaisselle', ''],
  /* Higiene */
  ['higiene', '🧻', 'Tovalloletes', 'Toallitas', 'Toalhitas', 'Wipes', 'Lingettes', '~infantil ~pingo'],
  ['higiene', '🪥', 'Pasta de dents infantil', 'Pasta de dientes infantil', 'Pasta dentes júnior|Pasta dentífrica infantil|Pasta de dentes criança', 'Kids toothpaste', 'Dentifrice enfant', '~aquafresh'],
  ['higiene', '🪥', 'Raspall de dents', 'Cepillo de dientes', 'Escova de dentes|Escova dentes', 'Toothbrush', 'Brosse à dents', '~smile'],
  ['higiene', '🦻', 'Bastonets', 'Bastoncillos', 'Cotonetes', 'Cotton buds', 'Cotons-tiges', '~pingo'],
  ['higiene', '🧻', 'Paper higiènic humit', 'Papel húmedo', 'Papel higiénico húmido', 'Moist toilet tissue', 'Papier toilette humide', '']
];
/* Menys habituals: no surten a les categories, però es troben cercant */
var CONCEPTES_MES = [
  ['rebost', '🧂', 'Sal', 'Sal', 'Sal', 'Salt', 'Sel', '-sem -sin -manteiga -bolacha -amendoim -batata'],
  ['rebost', '🌻', 'Oli de gira-sol', 'Aceite de girasol', 'Óleo alimentar|Óleo de girassol', 'Sunflower oil', 'Huile de tournesol', ''],
  ['neteja', '👕', 'Detergent per a roba', 'Detergente ropa', 'Detergente roupa', 'Laundry detergent', 'Lessive', '-amaciador'],
  ['neteja', '🌸', 'Suavitzant', 'Suavizante', 'Amaciador', 'Fabric softener', 'Adoucissant', ''],
  ['neteja', '🧪', 'Lleixiu', 'Lejía', 'Lixívia', 'Bleach', 'Javel', ''],
  ['neteja', '🫧', 'Pastilles rentavaixelles', 'Pastillas lavavajillas', 'Pastilhas máquina loiça', 'Dishwasher tablets', 'Tablettes lave-vaisselle', ''],
  ['higiene', '🧴', 'Xampú', 'Champú', 'Champô', 'Shampoo', 'Shampooing', '-amaciador'],
  ['higiene', '🧼', 'Gel de dutxa', 'Gel de ducha', 'Gel de banho', 'Shower gel', 'Gel douche', ''],
  ['higiene', '🧴', 'Desodorant', 'Desodorante', 'Desodorizante', 'Deodorant', 'Déodorant', ''],
  ['higiene', '🪥', 'Pasta de dents', 'Pasta de dientes', 'Pasta de dentes|Pasta dentífrica', 'Toothpaste', 'Dentifrice', '-escova -júnior -infantil'],
  ['carn', '🌭', 'Salsitxes', 'Salchichas', 'Salsichas', 'Sausages', 'Saucisses', '-soja -vegetais'],
  ['peix', '🐟', 'Bacallà', 'Bacalao', 'Bacalhau', 'Cod', 'Cabillaud', '-pastéis -pasteis -bolinhos'],
  ['congelats', '🍕', 'Pizza', 'Pizza', 'Pizza', 'Pizza', 'Pizza', '-massa -molho'],
  ['pa', '🍪', 'Galetes', 'Galletas', 'Bolacha|Bolachas', 'Biscuits', 'Biscuits', ''],
  ['bebe', '🧷', 'Bolquers', 'Pañales', 'Fraldas', 'Nappies', 'Couches', ''],
  ['animals', '🐱', 'Menjar de gat', 'Comida para gatos', 'Comida gato|Ração gato', 'Cat food', 'Nourriture chat', ''],
  ['animals', '🐶', 'Menjar de gos', 'Comida para perros', 'Comida cão|Ração cão', 'Dog food', 'Nourriture chien', '']
];
/* Idioma de la columna segons l'idioma de la pantalla i segons el país de les botigues */
var CONC_IDX = { ca: 2, es: 3, pt: 4, en: 5, fr: 6 };
var CONC_PAIS = { PT: 4, BR: 4, ES: 3, AD: 3, MX: 3, AR: 3, CO: 3, CL: 3, FR: 6, BE: 6, GB: 5, IE: 5, US: 5, AU: 5 };
function concNom(c) { return (c[CONC_IDX[LANG] || 5] || c[5]).split('|')[0]; }
function concCerca(c) {
  var i = CONC_PAIS[typeof PAIS != 'undefined' ? PAIS : ''] || CONC_IDX[LANG] || 5;
  return (c[i] || c[5]) + (c[7] ? ' ' + c[7] : '');
}

/* iOS 9 (iPad 3) no té els emojis de menjar nous (Unicode 9 o posterior): es canvien pel més semblant que sí que té */
var EMO_VELL = { '🥐': '🍞', '🥑': '🍐', '🥒': '🌿', '🥔': '🍠', '🥕': '🍠', '🥖': '🍞', '🥚': '🍳', '🥛': '🍼', '🥜': '🌰', '🥣': '🍜', '🥤': '🍹', '🥦': '🌳', '🥩': '🍖', '🥬': '🌿', '🦻': '👂', '🧂': '🍚', '🧃': '🍹', '🧅': '🌰', '🧆': '🍘', '🧈': '🧀', '🧪': '⚗', '🧴': '🍶', '🧷': '📎', '🧻': '🚽', '🧼': '🛁', '🧽': '💦', '🪥': '🚿', '🫒': '🍇', '🫘': '🌰', '🫛': '🌿', '🫧': '💦', '🧊': '❄', '🥫': '🍯', '🥓': '🍖', '🥞': '🍰', '🦐': '🐟', '🦑': '🐙', '🥗': '🍴', '🥘': '🍲', '🥟': '🍘', '🥯': '🍩', '🧇': '🍰', '🧁': '🍰', '🥧': '🍰', '🥨': '🍞', '🦪': '🐚', '🍿': '🍬', '🧹': '🗑', '🧺': '👕', '🧦': '👕', '🦴': '🐶' };
var EMO_ANTIC = (function () { var m = /OS (\d+)_\d+(_\d+)? like Mac OS X/.exec(navigator.userAgent); return !!(m && +m[1] < 10); })();
function emo(s) {
  if (!EMO_ANTIC || !s) return s;
  var r = '', i = 0;
  while (i < s.length) {
    var c = s.charAt(i), n = s.charCodeAt(i);
    if (n >= 0xD800 && n <= 0xDBFF && i + 1 < s.length) c = s.substring(i, i + 2);
    r += EMO_VELL.hasOwnProperty(c) ? EMO_VELL[c] : c;
    i += c.length;
  }
  return r;
}
(function () {
  if (!EMO_ANTIC) return;
  var ls = [typeof CONCEPTES != 'undefined' ? CONCEPTES : [], typeof CONCEPTES_MES != 'undefined' ? CONCEPTES_MES : []];
  for (var k = 0; k < ls.length; k++) for (var i = 0; i < ls[k].length; i++) if (ls[k][i] && ls[k][i][1]) ls[k][i][1] = emo(ls[k][i][1]);
})();
