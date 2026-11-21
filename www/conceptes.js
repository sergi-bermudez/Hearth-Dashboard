/* Hearth · Compra: conceptes genèrics ("Ous", "Plàtans") per triar com a la caixa d'un restaurant.
   El servidor els busca a les botigues configurades i diu on són més barats.
   Columnes: categoria, emoji, català, castellà, portuguès, anglès, francès, extra.
   - A cada idioma, "a|b" vol dir "a o b" (només el primer es mostra).
   - A extra: -paraula exclou productes amb aquesta paraula; @categoria obliga la categoria de la botiga.
   Ordenats pel que es compra més sovint. Per afegir-ne, n'hi ha prou amb una fila nova. */
var CONCEPTES = [
  /* Fruita i verdura */
  ['fruita', '🍌', 'Plàtans', 'Plátanos', 'Banana', 'Bananas', 'Bananes', '@fruita -chips -seca -bebida -iogurte -yogur -bolsa -saqueta -puré'],
  ['fruita', '🍊', 'Mandarines', 'Mandarinas', 'Mandarina|Tangerina', 'Mandarins', 'Mandarines', '@fruita -sumo -zumo -iogurte'],
  ['fruita', '🍑', 'Préssecs', 'Melocotones', 'Pêssego', 'Peaches', 'Pêches', '@fruita -calda -almíbar -iogurte -sumo -néctar'],
  ['fruita', '🥒', 'Cogombre', 'Pepino', 'Pepino', 'Cucumber', 'Concombre', '@fruita'],
  ['fruita', '🥦', 'Bròquil', 'Brócoli', 'Brócolo|Brócolos', 'Broccoli', 'Brocoli', '@fruita'],
  ['fruita', '🥕', 'Pastanagues', 'Zanahorias', 'Cenoura', 'Carrots', 'Carottes', '@fruita -sumo -ralada'],
  ['fruita', '🍅', 'Tomàquet cherry', 'Tomate cherry', 'Tomate cherry|Tomate mini|Tomate cereja', 'Cherry tomatoes', 'Tomates cerises', '@fruita'],
  ['fruita', '🍈', 'Meló', 'Melón', 'Melão|Meloa', 'Melon', 'Melon', '@fruita'],
  ['fruita', '🍉', 'Síndria', 'Sandía', 'Melancia', 'Watermelon', 'Pastèque', '@fruita'],
  ['fruita', '🍑', 'Prunes', 'Ciruelas', 'Ameixa', 'Plums', 'Prunes', '@fruita -seca -passa'],
  ['fruita', '🍑', 'Albercocs', 'Albaricoques', 'Alperce|Damasco', 'Apricots', 'Abricots', '@fruita -seco -seca'],
  ['fruita', '🍇', 'Raïm', 'Uvas', 'Uva', 'Grapes', 'Raisin', '@fruita -passa'],
  ['fruita', '🍎', 'Pomes', 'Manzanas', 'Maçã', 'Apples', 'Pommes', '@fruita -sumo -bolsa -puré'],
  ['fruita', '🫛', 'Mongeta tendra', 'Judías verdes', 'Feijão verde', 'Green beans', 'Haricots verts', '@fruita'],
  ['fruita', '🧅', 'Ceba', 'Cebolla', 'Cebola', 'Onions', 'Oignons', '@fruita -frita'],
  ['fruita', '🥬', 'Amanida', 'Ensalada|Lechuga', 'Salada|Alface', 'Salad', 'Salade', '@fruita -fruta -molho'],
  ['fruita', '🥬', 'Ruca', 'Rúcula', 'Rúcula', 'Rocket', 'Roquette', '@fruita'],
  ['fruita', '🥑', 'Alvocat', 'Aguacate', 'Abacate', 'Avocado', 'Avocat', '@fruita -guacamole'],
  ['fruita', '🥔', 'Patates', 'Patatas', 'Batata', 'Potatoes', 'Pommes de terre', '@fruita -doce -frita'],
  /* Congelats (sobretot verdura) */
  ['congelats', '🥦', 'Bròquil congelat', 'Brócoli congelado', 'Brócolos', 'Frozen broccoli', 'Brocoli surgelé', '@congelats'],
  ['congelats', '🍄', 'Xampinyons congelats', 'Champiñones congelados', 'Cogumelos', 'Frozen mushrooms', 'Champignons surgelés', '@congelats'],
  ['congelats', '🍚', 'Arròs de verdures', 'Arroz de verduras', 'Veggie rice|Arroz de couve|Arroz de legumes', 'Veggie rice', 'Riz de légumes', '@congelats'],
  ['congelats', '🥦', 'Coliflor congelada', 'Coliflor congelada', 'Couve-flor|Couve flor', 'Frozen cauliflower', 'Chou-fleur surgelé', '@congelats'],
  ['congelats', '🧅', 'Ceba congelada', 'Cebolla congelada', 'Cebola', 'Frozen onion', 'Oignon surgelé', '@congelats'],
  ['congelats', '🥬', 'Espinacs congelats', 'Espinacas congeladas', 'Espinafres', 'Frozen spinach', 'Épinards surgelés', '@congelats'],
  ['congelats', '🫛', 'Pèsols i pastanaga', 'Guisantes', 'Ervilhas', 'Peas', 'Petits pois', '@congelats'],
  ['congelats', '🐟', 'Peix congelat', 'Pescado congelado', 'Pescada|Paloco|Tamboril', 'Frozen fish', 'Poisson surgelé', '@congelats -panado -empanado'],
  ['congelats', '🍦', 'Gelats', 'Helados', 'Gelado|Cornetto', 'Ice cream', 'Glaces', '@congelats'],
  /* Formatges i embotit */
  ['xarcuteria', '🧀', 'Formatge ratllat', 'Queso rallado', 'Queijo ralado', 'Grated cheese', 'Fromage râpé', ''],
  ['xarcuteria', '🧀', 'Formatge en llesques', 'Queso en lonchas', 'Queijo flamengo fatiado|Queijo fatiado', 'Sliced cheese', 'Fromage en tranches', '-fundido'],
  ['xarcuteria', '🧀', 'Formatge fos', 'Queso fundido', 'Queijo fundido', 'Processed cheese', 'Fromage fondu', ''],
  ['xarcuteria', '🧀', 'Mozzarella', 'Mozzarella', 'Mozzarella', 'Mozzarella', 'Mozzarella', '-pizza'],
  ['xarcuteria', '🧀', 'Formatge cremós', 'Queso crema', 'Queijo creme|Philadelphia', 'Cream cheese', 'Fromage à tartiner', ''],
  ['xarcuteria', '🌭', 'Xoriço', 'Chorizo', 'Chouriço|Chourição', 'Chorizo', 'Chorizo', '-sabor'],
  ['xarcuteria', '🍖', 'Pernil dolç', 'Jamón cocido', 'Fiambre|Paio york', 'Ham', 'Jambon', '-peru -frango -pavo'],
  ['xarcuteria', '🐟', 'Salmó fumat', 'Salmón ahumado', 'Salmão fumado', 'Smoked salmon', 'Saumon fumé', ''],
  /* Pica-pica */
  ['snacks', '🌽', 'Nachos', 'Nachos', 'Nachos|Tortilla chips', 'Nachos', 'Nachos', '-molho -salsa'],
  ['snacks', '🍘', 'Tortitas', 'Tortitas', 'Tortitas|Tostas de grão|Bolachas de arroz', 'Rice cakes', 'Galettes', ''],
  ['snacks', '🥜', 'Fruits secs', 'Frutos secos', 'Frutos secos|Mistura frutos', 'Mixed nuts', 'Fruits secs', ''],
  ['snacks', '🥜', 'Cacauets', 'Cacahuetes', 'Amendoim', 'Peanuts', 'Cacahuètes', '-manteiga -crema -creme'],
  ['snacks', '🍫', 'Barretes de cereals', 'Barritas de cereales', 'Barra cereais|Barra amêndoa|Barras', 'Cereal bars', 'Barres de céréales', ''],
  ['snacks', '🍎', 'Bossetes de fruita', 'Bolsitas de fruta', 'Bolsa fruta|Saqueta fruta|Puré de fruta|Bolsa maçã', 'Fruit pouches', 'Gourdes de fruits', ''],
  ['snacks', '🥣', 'Cereals de xocolata', 'Cereales de chocolate', 'Cereais chocolate', 'Chocolate cereal', 'Céréales chocolat', '-barra'],
  ['snacks', '🫒', 'Olives', 'Aceitunas', 'Azeitona|Azeitonas', 'Olives', 'Olives', '-azeite -pasta -patê'],
  ['snacks', '🥔', 'Patates xips', 'Patatas fritas', 'Batata frita|Batatas fritas|Palha', 'Crisps', 'Chips', '@rebost -congelad'],
  /* Rebost */
  ['rebost', '🫘', 'Mongetes cuites', 'Alubias cocidas', 'Feijão', 'Beans', 'Haricots', '@rebost -verde'],
  ['rebost', '🫘', 'Cigrons cuits', 'Garbanzos cocidos', 'Grão de bico|Grão-de-bico', 'Chickpeas', 'Pois chiches', '@rebost -húmus -hummus -tostas -tortitas'],
  ['rebost', '🐟', 'Tonyina', 'Atún', 'Atum', 'Tuna', 'Thon', '@rebost -patê -pate -salada'],
  ['rebost', '🐟', 'Sardines', 'Sardinas', 'Sardinha|Sardinhas', 'Sardines', 'Sardines', '@rebost -patê'],
  ['rebost', '🍅', 'Tomàquet triturat', 'Tomate triturado', 'Polpa de tomate|Tomate pelado', 'Chopped tomatoes', 'Tomates concassées', ''],
  ['rebost', '🍝', 'Pasta', 'Pasta', 'Massa|Esparguete|Fusilli', 'Pasta', 'Pâtes', '@rebost -folhada -pizza -bolo -quebrada -molho'],
  ['rebost', '🍚', 'Arròs', 'Arroz', 'Arroz', 'Rice', 'Riz', '@rebost -bolacha -tortitas -bebida -doce'],
  ['rebost', '☕', 'Cafè', 'Café', 'Café|Mistura', 'Coffee', 'Café', '@rebost -iogurte -bolacha -licor'],
  ['rebost', '🌾', 'Farina', 'Harina', 'Farinha', 'Flour', 'Farine', ''],
  ['rebost', '🍬', 'Sucre', 'Azúcar', 'Açúcar', 'Sugar', 'Sucre', '-sem -sin -zero -bolacha -chocolate'],
  ['rebost', '🫒', 'Oli d\'oliva', 'Aceite de oliva', 'Azeite', 'Olive oil', 'Huile d\'olive', '-spray -atum -sardinha'],
  /* Vegetarià */
  ['vegetal', '🌭', 'Salsitxes vegetals', 'Salchichas vegetales', 'Salsichas soja|Salsichas vegetais|Salsichas vegan', 'Veggie sausages', 'Saucisses végétales', ''],
  ['vegetal', '🧆', 'Falafel', 'Falafel', 'Falafel', 'Falafel', 'Falafel', ''],
  ['vegetal', '🫘', 'Hummus', 'Hummus', 'Hummus|Húmus', 'Hummus', 'Houmous', ''],
  ['vegetal', '🌿', 'Pesto', 'Pesto', 'Pesto', 'Pesto', 'Pesto', ''],
  ['vegetal', '🫘', 'Soja texturitzada', 'Soja texturizada', 'Soja granulada|Soja texturizada|Proteína de soja', 'Textured soy', 'Soja texturé', ''],
  ['vegetal', '🥛', 'Nata vegetal', 'Nata vegetal', 'Creme vegetal|Creme de soja|Natas soja', 'Plant cream', 'Crème végétale', ''],
  /* Làctics */
  ['lactis', '🍶', 'Iogurt líquid', 'Yogur líquido', 'Iogurte líquido', 'Drinking yoghurt', 'Yaourt à boire', ''],
  ['lactis', '🍶', 'Iogurt grec', 'Yogur griego', 'Iogurte grego|Oikos', 'Greek yoghurt', 'Yaourt grec', ''],
  ['lactis', '🥛', 'Llet', 'Leche semidesnatada', 'Leite meio gordo', 'Semi-skimmed milk', 'Lait demi-écrémé', '@lactis -chocolate -achocolatado -cacau -infantil -pó -sabor'],
  ['lactis', '🥛', 'Llet amb xocolata', 'Batido de chocolate', 'Leite chocolate|Leite com chocolate|Achocolatado', 'Chocolate milk', 'Lait chocolaté', ''],
  ['lactis', '🥚', 'Ous', 'Huevos', 'Ovos', 'Eggs', 'Oeufs', '-chocolate -kinder -páscoa -pascoa -codorniz -massa -mexidos'],
  ['lactis', '🧈', 'Mantega', 'Mantequilla', 'Manteiga', 'Butter', 'Beurre', '-amendoim -bolacha'],
  /* Pa */
  ['pa', '🥖', 'Panets', 'Panecillos', 'Carcaça|Pão padeirinho|Papo-seco', 'Bread rolls', 'Petits pains', ''],
  ['pa', '🍞', 'Pa de llet', 'Pan de leche', 'Pão de leite', 'Milk bread', 'Pain au lait', ''],
  ['pa', '🧀', 'Pa de formatge', 'Pan de queso', 'Pão de queijo', 'Cheese bread', 'Pain au fromage', ''],
  ['pa', '🍞', 'Pa de motlle', 'Pan de molde', 'Pão de forma', 'Sliced bread', 'Pain de mie', ''],
  ['pa', '🥐', 'Croissants', 'Cruasanes', 'Croissant', 'Croissants', 'Croissants', ''],
  /* Begudes */
  ['begudes', '🧃', 'Sucs', 'Zumos', 'Sumo|Néctar|Compal|Um Bongo', 'Juice', 'Jus', '@begudes'],
  ['begudes', '💧', 'Aigua', 'Agua', 'Água', 'Water', 'Eau', '@begudes -gás -gas -sabor -tónica -coco'],
  ['begudes', '🍺', 'Cervesa', 'Cerveza', 'Cerveja|Coral', 'Beer', 'Bière', '@begudes -sem -sin'],
  ['begudes', '🍷', 'Vi', 'Vino', 'Vinho', 'Wine', 'Vin', '@begudes -vinagre'],
  ['begudes', '🥤', 'Refresc de cola', 'Refresco de cola', 'Coca-Cola|Cola', 'Cola', 'Cola', '@begudes'],
  /* Carn i peix fresc */
  ['carn', '🍖', 'Mandonguilles', 'Albóndigas', 'Almôndegas', 'Meatballs', 'Boulettes', ''],
  ['carn', '🍗', 'Pit de pollastre', 'Pechuga de pollo', 'Peito de frango|Bife de frango', 'Chicken breast', 'Blanc de poulet', '-panado -nuggets -fiambre'],
  ['carn', '🥩', 'Carn picada', 'Carne picada', 'Carne picada', 'Minced meat', 'Viande hachée', ''],
  ['peix', '🐟', 'Salmó', 'Salmón', 'Salmão', 'Salmon', 'Saumon', '-fumado -ahumado -patê'],
  ['peix', '🐟', 'Rap', 'Rape', 'Tamboril', 'Monkfish', 'Lotte', ''],
  /* Neteja i casa */
  ['neteja', '🧻', 'Paper higiènic', 'Papel higiénico', 'Papel higiénico', 'Toilet paper', 'Papier toilette', '-húmido -humedo'],
  ['neteja', '🧻', 'Paper de cuina', 'Papel de cocina', 'Rolo de cozinha|Rolos de cozinha', 'Kitchen roll', 'Essuie-tout', ''],
  ['neteja', '🚽', 'Blocs pel WC', 'Bloques WC', 'Bloco sanitário|Bloco WC', 'Toilet blocks', 'Blocs WC', ''],
  ['neteja', '🧽', 'Netejador multiús', 'Limpiador multiusos', 'Multiusos|Ajax|Cif', 'All-purpose cleaner', 'Nettoyant multi-usages', ''],
  ['neteja', '🎞️', 'Paper film', 'Film transparente', 'Película aderente', 'Cling film', 'Film étirable', ''],
  ['neteja', '🗑️', 'Bosses d\'escombraries', 'Bolsas de basura', 'Sacos do lixo|Sacos lixo', 'Bin bags', 'Sacs poubelle', ''],
  ['neteja', '🧴', 'Rentaplats', 'Lavavajillas', 'Detergente loiça|Detergente da loiça', 'Washing-up liquid', 'Liquide vaisselle', ''],
  /* Higiene */
  ['higiene', '🧻', 'Tovalloletes', 'Toallitas', 'Toalhitas', 'Wipes', 'Lingettes', ''],
  ['higiene', '🪥', 'Pasta de dents infantil', 'Pasta de dientes infantil', 'Pasta dentes júnior|Pasta dentífrica infantil|Pasta de dentes criança', 'Kids toothpaste', 'Dentifrice enfant', ''],
  ['higiene', '🪥', 'Raspall de dents', 'Cepillo de dientes', 'Escova de dentes|Escova dentes', 'Toothbrush', 'Brosse à dents', ''],
  ['higiene', '🦻', 'Bastonets', 'Bastoncillos', 'Cotonetes', 'Cotton buds', 'Cotons-tiges', ''],
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
