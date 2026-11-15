# Manuel de l'utilisateur — Meu Editor de Vídeo

Ce manuel explique comment installer, utiliser et configurer Meu Editor de Vídeo. Il est écrit pour les personnes qui n'ont jamais fait de montage vidéo. Les noms des menus et des boutons sont ceux qui apparaissent à l'écran.

Les textes en **gras** sont des noms de menus, de boutons et d'options. Le symbole › sépare les niveaux d'un menu : **Fichier › Exporter…** signifie ouvrir le menu Fichier et choisir Exporter.

## Premiers pas

Meu Editor de Vídeo fonctionne sous Windows 11 64 bits. Pour l'installer, utilisez l'un des deux fichiers d'installation :

- **MeuEditorDeVideo-…-Setup.exe** : installateur complet. Recommandé pour la plupart des personnes.
- **MeuEditorDeVideo-…-x64.msi** : paquet Windows Installer, pour ceux qui préfèrent l'installation standard de Windows.

Exécutez le fichier, suivez les écrans et, à la fin, ouvrez le programme depuis le menu Démarrer. Rien d'autre n'est à installer séparément.

> Si Windows affiche l'avertissement « Windows a protégé votre ordinateur », cliquez sur « Informations complémentaires » puis sur « Exécuter quand même ». Cet avertissement apparaît pour les programmes sans certificat de signature numérique.

### Configuration requise

Voici la configuration prévue dans la spécification du produit :

- **Minimum :** Windows 11 64 bits, processeur Intel Core i5 ou AMD Ryzen 5, 16 Go de RAM, carte graphique compatible Direct3D et SSD d'au moins 512 Go.
- **Recommandé :** Windows 11 64 bits, processeur Intel Core i7 ou AMD Ryzen 7, 32 Go de RAM ou plus, carte graphique NVIDIA RTX ou AMD Radeon avec 8 Go de mémoire vidéo ou plus et SSD NVMe de 1 To ou plus.

Une carte graphique dédiée n'est pas obligatoire, mais elle rend l'aperçu, l'exportation et les fonctions d'intelligence artificielle bien plus rapides.

### À l'ouverture du programme

La première fois (ou si une préférence manque ou est vide dans le registre de Windows), la fenêtre **Bienvenue dans Meu Editor de Vídeos** apparaît. Renseignez **Nom complet**, **Langue du système**, **Thème** (Clair, Sombre ou Suivre le système d'exploitation) et **Rechercher les mises à jour chaque mois** (Oui ou Non), puis cliquez sur **Enregistrer les préférences**. Tous les champs sont obligatoires et la fenêtre ne se ferme qu'après l'enregistrement. Si vous choisissez une autre langue, le programme redémarre dans celle-ci. Vous pouvez ensuite modifier ces préférences dans **Outils › Paramètres… › Général**.

Le programme s'ouvre directement sur l'écran de montage, avec un projet nouveau et vide appelé « Sans titre ». S'il existe une copie d'un projet qui n'a pas été fermé correctement, le programme demande si vous voulez la récupérer (voir la section Enregistrement, enregistrement automatique et récupération).

Les cinq derniers projets ouverts apparaissent dans le menu **Fichier**, entre des séparateurs. Cliquez sur l'un d'eux pour l'ouvrir. Si le fichier a été déplacé ou supprimé, le programme vous prévient et le retire de la liste.

### Créer un projet

1. Choisissez **Fichier › Nouveau projet…**.
2. Saisissez un nom dans « Nom du projet ».
3. Choisissez le dossier où le projet sera enregistré dans « Dossier du projet » (utilisez **Parcourir…** si vous le souhaitez).
4. Choisissez un « Modèle de projet ». Les modèles définissent déjà la résolution, le format et la fréquence d'images conseillés. Il existe des modèles pour la vidéo horizontale (Full HD, 4K et cinéma), verticale et carrée, et des modèles pour des plateformes comme YouTube, Instagram, TikTok, WhatsApp et Facebook.
5. Si vous préférez, réglez manuellement la résolution, le format, la fréquence d'images (FPS) et le profil de couleur.
6. Dans « Stockage des médias », choisissez une option :
   - **Lier les médias (les fichiers restent où ils sont)** : le projet n'enregistre que le chemin des fichiers. C'est le plus léger, mais les fichiers doivent rester au même endroit.
   - **Intégrer les médias au projet (.med autonome)** : les médias sont copiés dans le projet. Utilisez cette option pour emporter le projet sur un autre ordinateur.
7. Cliquez sur **Créer le projet**.

### Ouvrir un projet existant

Il y a trois façons :

- Choisissez **Fichier › Ouvrir…** et sélectionnez le fichier .med.
- Cliquez sur l'un des projets récents du menu **Fichier**.
- Double-cliquez sur le fichier .med dans l'Explorateur de fichiers. Le programme ouvre le projet automatiquement.

## L'écran de montage

Après la création ou l'ouverture d'un projet, l'écran de montage apparaît. Il est divisé en zones :

- **Barre supérieure** (en haut de la fenêtre) : à gauche, les menus **Fichier**, **Édition**, **Affichage**, **Projet**, **Outils** et **Aide** et les boutons **Enregistrer**, **Annuler** et **Rétablir** ; au centre, le nom du projet ; à droite, **Thème**, **File de rendu**, **Exporter** et les boutons pour réduire, agrandir et fermer la fenêtre. Faites glisser la barre pour déplacer la fenêtre et double-cliquez dessus pour l'agrandir ou la restaurer.
- **Bibliothèque de médias** (à gauche) : en haut se trouvent les onglets Bibliothèque de médias, Vidéos, Images, Audios, Éléments graphiques, Textes, Transitions, Effets, Couleur et IA. Survolez un onglet pour voir son nom complet. Les médias importés apparaissent en grille, avec miniature et durée.
- **Aperçu** (au centre, en haut) : montre le résultat de la vidéo. En haut se trouvent la résolution de l'aperçu, le bouton Proxy et le bouton **Agrandir le moniteur**, qui fait couvrir la bibliothèque et la timeline par l'aperçu (le panneau Propriétés reste visible ; cliquez de nouveau pour restaurer) ; en bas, la barre de position (faites-la glisser pour aller à n'importe quel point), les boutons lecture, pause et image par image, le volume, la capture d'image (enregistre l'image actuelle en PNG ou JPEG, à la résolution du projet ; aussi dans **Fichier › Exporter l'image actuelle…**) et le plein écran.
- **Propriétés** (à droite) : montre les informations et les réglages de l'élément sélectionné. Elles sont réparties en onglets : **Projet** (résolution, fréquence d'images, durée, nombre de médias et de clips, fichier), **Média**, **Vidéo**, **Texte**, **Composition**, **IA**, **Audio**, **Effets**, **Couleur** et **Historique**. Dans le coin inférieur droit, les raccourcis **Manuel d'utilisation** et **Assistant virtuel** ouvrent l'aide en un clic.
- **Timeline** (en bas) : là où vous montez la vidéo. Chaque ligne horizontale est une piste. Une barre d'outils se trouve juste au-dessus.

> Quand le nom du projet porte la marque « ● non enregistré », il y a des modifications qui n'ont pas encore été enregistrées. Utilisez **Fichier › Enregistrer** ou Ctrl+S.

### Menus

- **Fichier** : nouveau projet, **nouveau à partir d'un modèle**, ouvrir, importer des médias, importer une séquence d'images, enregistrer, enregistrer sous, créer une sauvegarde, exporter, file de rendu, exporter l'image actuelle (PNG ou JPEG) et les cinq projets récents.
- **Édition** : annuler et rétablir ; tout sélectionner ; scinder ; **joindre les coupes** (annule une scission : cliquez sur la règle au-dessus du point de coupe ; disponible seulement quand les deux côtés sont la suite du même média, sans réglages différents) ; couper, copier, coller à la tête de lecture et dupliquer ; supprimer, supprimer et combler l'espace (seulement sur la piste du clip) et **supprimer et joindre** (comble l'espace sur toutes les pistes, en gardant la synchronisation ; les pistes verrouillées ne bougent pas et l'opération est refusée si un clip d'une autre piste occupe une partie du passage supprimé).
- **Affichage** :
  - **Mode de disposition** : **Par défaut** ; **Timeline** (la timeline occupe toute la largeur) ; **Vidéo** (le moniteur occupe la moitié droite, sur toute la hauteur).
  - **Qualité de lecture** : Automatique, Complète, 1/2 ou 1/4.
  - **Niveau de zoom de la vidéo** : Ajuster (tient dans le moniteur) ou de 10 % à 400 % ; au-delà de la taille du moniteur, des barres de défilement apparaissent.
  - **Lecture/Pause**, **Arrêter** (revient au point où la lecture a commencé) et **Intervalle de lecture en boucle** (répète la sélection de la timeline ou, sans sélection, le projet entier).
  - **Règle** : règles en pixels de la vidéo en haut et à gauche de l'image.
- **Projet** : intégrer les médias au projet, **recadrage automatique**, **enregistrer comme modèle**, **calque de réglage**, **clip composé**, **écran partagé**, **multicaméra** et localiser les médias manquants (voir [Calques de réglage, clips composés, écran partagé et multicaméra](#calques-de-réglage-clips-composés-écran-partagé-et-multicaméra)).
  - **Enregistrer comme modèle…** garde une copie du projet enregistré dans vos modèles. **Fichier › Nouveau à partir d'un modèle…** choisit un modèle (le vôtre ou celui d'un pack), demande où enregistrer le nouveau projet et ouvre la copie ; le modèle reste intact.
  - **Recadrage automatique…** change le format du projet (9:16 pour Reels, TikTok et Shorts ; 1:1 ; 4:5 ; 16:9) et repositionne chaque clip vidéo et image pour couvrir le nouveau cadre. Dans **Suivre**, choisissez **Visage**, **Corps entier** ou **Centre du cadre**. Pour suivre la personne, le programme utilise le modèle de suppression d'arrière-plan et crée des images clés de position, que vous pouvez ajuster ensuite dans l'onglet Vidéo. Les textes restent tels quels. La progression apparaît dans l'onglet **IA**, et tout s'annule avec un seul Ctrl+Z.
- **Outils** : mélangeur audio, éditeur de sous-titres, importer et exporter des sous-titres, **synthèse vocale**, **analyser les médias**, **enregistrer l'écran, la webcam et le micro**, **packs de contenu**, modèles d'IA et paramètres.
  - **Enregistrer l'écran, la webcam et le micro…** enregistre le bureau, une webcam dans un coin de l'écran et le son d'un microphone, le tout en même temps, dans un fichier MP4. Avec plus d'un moniteur, choisissez dans **Écran à enregistrer** quel écran enregistrer (ou tous). La webcam occupe un dixième de la largeur de l'écran enregistré. Quand vous cliquez sur **Enregistrer**, le programme est réduit et un compte à rebours 5, 4, 3, 2, 1 apparaît au centre de l'écran avant le début de l'enregistrement. Pour arrêter, appuyez sur **F10** (fonctionne avec n'importe quel programme au premier plan) ; le programme réapparaît. L'enregistrement va dans le dossier **Enregistrements** (à côté du projet ou dans Vidéos › Meu Editor de Vídeo), entre dans la bibliothèque (catégorie Enregistrements) et, si l'option est cochée, dans la timeline à la tête de lecture. Sans l'écran, seule la webcam est enregistrée.
  - **Packs de contenu…** installe, exporte et supprime des packs d'autocollants, de titres et de modèles de projet (fichiers .medpack). Un pack est un ZIP avec les dossiers `figurinhas` (PNG, GIF, WebP, JPEG ou SVG), `titulos` (.medtitle) et `modelos` (.med) et, en option, un `pacote.json` avec le nom (par exemple `{"nome": "Fête d'été"}`). Les éléments que vous enregistrez vous-même vont dans le pack **Mes éléments** ; utilisez **Exporter…** pour le partager.
  - **Synthèse vocale…** transforme un texte en narration avec les voix installées dans Windows, sans internet. Saisissez le texte, choisissez la **Voix** et la **Vitesse** et cliquez sur **Générer et insérer** : l'audio va dans la bibliothèque (catégorie Narration) et est placé à la tête de lecture, sur la première piste audio libre ou sur une nouvelle piste appelée Narration. Pour avoir plus de voix, installez-les dans **Paramètres Windows › Heure et langue › Voix**.
  - **Analyser les médias** étiquette tous les médias de la bibliothèque : type, orientation (horizontale, verticale, carrée), résolution (HD, Full HD, 4K), durée (court, long) et son. Avec les modèles installés, aussi **parole** ou **sans parole**, le nombre de **scènes** et **personne** ou **sans personnes**. Les étiquettes apparaissent au survol du média et la zone **Rechercher…** de la bibliothèque cherche aussi dedans.
- **Aide** : **Manuel de l'utilisateur** (F1), **Assistant virtuel** (Maj+F1), **Raccourcis clavier…**, **Informations système…**, **Ouvrir le dossier des journaux**, **Licences tierces**, **À propos de Meu Editor de Vídeo…**, **Rechercher des mises à jour** (demande par internet s'il existe une nouvelle version : affiche « Vous avez la version la plus récente ! » ou la fenêtre **Nouvelle version disponible**, avec la version, les langues, les systèmes pris en charge, l'auteur et les liens de téléchargement sur le Microsoft Store et sur le site AplicativosIA.Online ; un clic sur **Fermer cette fenêtre** enregistre la date de la vérification). Dans **À propos de Meu Editor de Vídeo…**, l'icône LinkedIn ouvre le profil de l'auteur dans le navigateur.

Le **Manuel de l'utilisateur** ouvre ce manuel dans le programme, sans internet. La liste de gauche mène à chaque sujet et Ctrl+F recherche un mot. La fenêtre peut rester ouverte pendant le montage.

**Raccourcis clavier…** affiche les raccourcis utilisés, y compris ceux que vous avez modifiés. **Informations système…** affiche la version, Windows, le processeur, la mémoire, la carte graphique, les modèles d'IA et les dossiers du programme ; le bouton **Copier** met le tout dans le presse-papiers, utile pour demander de l'aide.

## Importer des médias

Importer, c'est apporter au projet les fichiers que vous allez utiliser, comme des vidéos, des photos, de la musique et des enregistrements. Les fichiers originaux ne sont pas modifiés.

### Formats pris en charge

- **Vidéo** : MP4, M4V, MOV, MKV, WebM, AVI, WMV, FLV, MPG et MPEG, M2TS, MTS, TS, 3GP, OGV, MXF et VOB.
- **Audio** : MP3, WAV, AAC, M4A, FLAC, OGG, OGA, Opus, WMA, AIFF, AIF, AC3 et AMR.
- **Image** : JPG et JPEG, PNG, BMP, TIF et TIFF, WebP, TGA, JFIF et SVG.
- **GIF animé** : GIF.
- **Séquence d'images** : une série de photos numérotées, comme photo_0001.png, photo_0002.png, etc.

Les fichiers d'autres formats sont refusés, avec le message « Format de fichier non pris en charge. ».

### Comment importer

1. Cliquez sur **Importer des médias** dans le panneau de la bibliothèque, ou utilisez **Fichier › Importer des médias…** (raccourci Ctrl+I).
2. Choisissez un ou plusieurs fichiers et confirmez.
3. Suivez la progression. Le message « Importation 3 sur 10 » indique combien de fichiers ont été lus.

Vous pouvez aussi faire glisser des fichiers ou des dossiers depuis l'Explorateur de fichiers et les déposer sur la bibliothèque de médias. Quand le fichier est au-dessus de la bonne zone, l'indication « Déposez pour importer » apparaît.

Pour une séquence d'images, utilisez **Fichier › Importer une séquence d'images…**. Le programme reconnaît la numérotation automatiquement.

Chaque média importé reçoit une miniature. Quand vous sélectionnez un média, l'onglet **Média** des propriétés affiche la durée, la résolution, la fréquence d'images et d'autres données du fichier.

### Organiser la bibliothèque

- Utilisez la zone **Rechercher…** pour trouver un média par son nom, sa catégorie ou les étiquettes créées dans **Outils › Analyser les médias** (par exemple « parole », « personne » ou « verticale »).
- Faites un clic droit sur un média pour **Renommer…**, **Définir la catégorie…**, **Retirer la catégorie**, **Ajouter à la timeline** ou **Retirer du projet**.
- Retirer un média du projet ne supprime pas le fichier de l'ordinateur.

### Médias manquants

Si un fichier a été déplacé, renommé ou supprimé, le projet affiche l'avertissement « Certains médias du projet sont introuvables » à l'ouverture. L'avertissement apparaît aussi dans la barre d'état sous la forme « Médias manquants », avec le nombre de médias concernés. Pour résoudre le problème :

1. Cliquez sur **Localiser…** pour indiquer le fichier, ou sur **Rechercher dans un dossier…** pour que le programme cherche dans les dossiers et sous-dossiers que vous choisissez.
2. Si vous ne voulez pas les localiser maintenant, cliquez sur **Continuer sans les médias**. Les passages restent marqués comme manquants et pourront être localisés plus tard.

Vous pouvez ouvrir cette fenêtre à tout moment dans **Projet › Localiser les médias manquants…**. Dans un projet téléchargé depuis Internet, le logiciel ne recherche pas les médias ni les LUT situés sur un autre ordinateur du réseau, car le simple fait de contacter cet ordinateur lui enverrait vos informations de connexion Windows : ces médias apparaissent comme manquants et ces LUT sont désactivées. Si vous faites confiance à l'origine du projet, localisez les médias dans cette fenêtre et réactivez les LUT dans le panneau **Propriétés**.

Pour éviter ce problème en emportant le projet sur un autre ordinateur, utilisez **Projet › Intégrer les médias au projet…**. Les médias sont copiés dans le fichier du projet.

### Proxies (versions légères pour le montage)

Un proxy est une copie légère d'une grosse vidéo, en 540p, utilisée seulement pour rendre l'aperçu plus fluide. L'exportation utilise toujours le fichier original.

- Pour créer un proxy, faites un clic droit sur la vidéo dans la bibliothèque et choisissez **Générer un proxy de montage (540p)**.
- Pour supprimer un proxy, choisissez **Supprimer le proxy**.
- Dans le moniteur, l'option **Utiliser les proxies dans l'aperçu** active ou désactive leur utilisation.
- Dans **Outils › Paramètres…**, vous pouvez demander la génération automatique des proxies pour les vidéos au-delà de 1080p.

## Monter dans la timeline

La timeline est l'endroit où la vidéo est montée de gauche à droite. La **tête de lecture** (une ligne verticale) indique l'instant visible dans l'aperçu. Les morceaux de médias placés sur les pistes s'appellent des clips.

### Placer des médias dans la timeline

- Faites glisser un média de la bibliothèque vers une piste.
- Ou sélectionnez le média dans la bibliothèque et cliquez sur **Ajouter à la timeline** dans la barre d'outils.

### Déplacer, rogner et scinder

- **Déplacer** : faites glisser le clip à un autre endroit de la piste ou sur une autre piste du même type.
- **Rogner le début ou la fin** : faites glisser le bord du clip pour raccourcir ou allonger le passage utilisé.
- **Scinder** : placez la tête de lecture au point voulu et cliquez sur **Scinder** dans la barre d'outils, ou appuyez sur S. Le clip est coupé en deux à cet endroit.
- **Joindre les coupes** (menu Édition) : réunit les deux parties d'un clip scindé. Placez la tête de lecture sur le point de coupe ; cela ne fonctionne que si les deux parties sont la suite du même média, sans réglages différents.
- **Supprimer** : sélectionnez le clip et appuyez sur Suppr. Vous pouvez aussi utiliser le bouton **Supprimer la sélection**.
- **Supprimer et combler l'espace** : supprime le clip et ramène les clips suivants pour combler le vide. Utilisez Maj+Suppr.
- **Copier, couper, coller et dupliquer** : utilisez Ctrl+C, Ctrl+X, Ctrl+V et Ctrl+D. Au collage, le clip va à la position de la tête de lecture.

### Aimant et montage en cascade

- **Aimant d'alignement** (touche N) : activé, le clip « colle » aux bords des autres clips et à la tête de lecture, ce qui facilite l'assemblage sans laisser d'espaces. Le bouton se trouve dans la barre d'outils.
- **Montage en cascade (ripple)** : activé, déplacer ou rogner un clip pousse les clips suivants, sans laisser de trous.

### Pistes

Les pistes sont les lignes horizontales de la timeline. Pour ajouter une piste, cliquez sur le bouton **Ajouter une piste** et choisissez le type :

- **Piste vidéo**
- **Piste audio**
- **Piste d'image**
- **Piste de texte**
- **Piste de sous-titres**
- **Piste d'effets**

Chaque piste a trois boutons dans son en-tête, à gauche :

- **Masquer** : cache la piste dans l'aperçu.
- **Muet** : coupe le son de la piste audio.
- **Verrouiller** : empêche les modifications de la piste, pour ne pas toucher à ce qui est terminé.

Faites un clic droit sur le nom de la piste pour **Renommer la piste…** ou **Supprimer la piste**.

### Grouper et lier

- **Grouper** (Ctrl+G) : réunit plusieurs clips pour qu'ils se déplacent ensemble. Utilisez **Dissocier** (Ctrl+Maj+G) pour les séparer.
- **Lier l'audio et la vidéo** : fait suivre la vidéo par son audio. Utilisez **Délier l'audio et la vidéo** pour les séparer.

### Vitesse du clip

Faites un clic droit sur le clip et choisissez **Vitesse…** pour l'accélérer ou le ralentir. Dans l'onglet **Vidéo** des propriétés, la liste des vitesses va de 0,25× (ralenti) à 8× (accéléré).

### Activer et désactiver des clips

Faites un clic droit sur le clip et choisissez **Activer/désactiver le clip**. Un clip désactivé reste dans la timeline (estompé et marqué du symbole ⊘), mais il n'est ni vu ni entendu à la lecture et à l'exportation.

### Calques de réglage, clips composés, écran partagé et multicaméra

- **Calque de réglage** (**Projet › Ajouter un calque de réglage**) : crée à la tête de lecture un clip de 5 secondes sur une piste d'effets. Les effets et la correction colorimétrique placés dessus s'appliquent à **toutes les pistes en dessous**, pendant la durée du clip. Sélectionnez le calque et ajoutez des effets dans les onglets **Effets** et **Couleur**. L'opacité du calque dose l'effet et les masques limitent la zone. Étirez ou déplacez le calque comme n'importe quel clip.
- **Clip composé** (**Projet › Créer un clip composé…** ou menu contextuel) : réunit les clips sélectionnés (avec leurs audios liés) en un seul clip, qui se déplace, se rogne et reçoit des effets comme un tout. Les pistes et positions d'origine sont conservées à l'intérieur. **Défaire le clip composé** remet les clips dans la timeline, sur de nouvelles pistes, avec seulement le passage que montrait le clip composé. Le son du clip composé n'est joué qu'à vitesse normale.
- **Écran partagé** (**Projet › Écran partagé**) : sélectionnez de 2 à 4 clips vidéo ou image, chacun sur une piste différente, et choisissez une disposition : **2 côte à côte**, **2 superposés**, **3 colonnes**, **3 bandes**, **1 grand et 2 petits** ou **4 en grille**. Chaque clip (dans l'ordre des pistes, de haut en bas) est agrandi pour couvrir sa partie du cadre et rogné au-delà. Ajustez ensuite la position et le recadrage dans l'onglet Vidéo.
- **Multicaméra** (**Projet › Multicaméra**) : placez chaque caméra sur une piste vidéo, synchronisées (par exemple avec **Synchroniser par l'audio**). Pendant la lecture ou à l'arrêt, choisissez **Caméra 1** à **Caméra 4** (Alt+1 à Alt+4, avec le focus sur la timeline) pour afficher cette caméra **à partir de la tête de lecture** : le programme coupe toutes les caméras à la tête de lecture et désactive les autres. La caméra 1 est la piste vidéo la plus haute avec un clip à la tête de lecture. L'audio ne change pas.

### Couleur du clip

Pour repérer les clips visuellement, sélectionnez un ou plusieurs clips, faites un clic droit et choisissez **Couleur du clip**. La couleur est enregistrée dans le projet et peut être annulée avec Ctrl+Z. Choisissez **Automatique (selon le type)** pour revenir à la couleur par défaut (vidéo, image, audio ou texte).

Les projets avec des couleurs de clip utilisent le format 1.3. Les versions antérieures du programme ouvrent ces projets avec un avertissement et ignorent les couleurs.

### Marqueurs

Les marqueurs sont des repères sur la règle temporelle, utiles pour noter des points importants, comme le début d'une prise de parole.

- Placez la tête de lecture et cliquez sur **Ajouter un marqueur** ou appuyez sur M.
- Faites un clic droit sur le marqueur pour **Renommer le marqueur…** ou **Supprimer le marqueur**.

### Zoom de la timeline

Utilisez les boutons de zoom de la barre d'outils, ou les raccourcis :

- Zoom avant : Ctrl + touche plus (ou + du pavé numérique)
- Zoom arrière : Ctrl + touche moins (ou - du pavé numérique)
- Ajuster à la fenêtre : Maj+Z

### Annuler et rétablir

- **Annuler** (Ctrl+Z) et **Rétablir** (Ctrl+Y ou Ctrl+Maj+Z) reculent et avancent dans les dernières actions.
- L'onglet **Historique** des propriétés affiche la liste des actions effectuées. Cliquez sur l'une d'elles pour ramener le projet à ce point.

### Naviguer dans la vidéo

- Espace : lit ou met en pause.
- Flèches gauche et droite : reculent ou avancent d'une image.
- Maj avec les flèches : reculent ou avancent d'une seconde.
- Haut et bas : vont à la coupe précédente et à la suivante.
- Début et Fin : vont au début et à la fin.

## Effets visuels, texte et transitions

Pour appliquer un effet, sélectionnez le clip dans la timeline et utilisez les onglets des propriétés. Vous pouvez aussi utiliser les sections de la bibliothèque, comme **Effets**, **Couleur** et **Transitions** : double-cliquez sur un élément pour l'appliquer à la tête de lecture ou au clip sélectionné, ou utilisez le bouton **Appliquer au clip sélectionné**.

### Onglet Vidéo

Cet onglet regroupe les réglages de mouvement et de temps du clip :

- **Transformation** : position X et Y, échelle, rotation et opacité. À côté de chaque valeur se trouve un bouton ◇ qui crée une image clé.
- **Images clés** : une image clé enregistre une valeur à un instant précis. Quand il y a des images clés, le programme change la valeur progressivement entre elles. Utilisez **Image clé précédente** et **Image clé suivante** pour passer de l'une à l'autre, et **Interpolation** pour choisir la courbe : Douce (entrée et sortie), Linéaire, Accélération, Décélération ou Maintien (saut).
- **3D (perspective)** : rotation X, rotation Y et profondeur, pour donner une impression de profondeur au clip. Le bouton **Retournement de carte 3D** fait tourner le clip comme une carte. Ce n'est pas un modèle 3D complet.
- **Animations prêtes** : effets de mouvement prêts à appliquer, comme entrer par la gauche, apparaître avec zoom, Ken Burns (zoom lent), pulser, tourner, se balancer, rebondir et apparaître et disparaître.
- **Recadrage** : coupe les bords de la vidéo à gauche, en haut, à droite et en bas. Les commandes ne font que masquer les bords ; la taille du projet reste la même. Pour que la vidéo prenne vraiment la taille recadrée, cliquez sur **Appliquer le recadrage à la vidéo** : le projet prend la taille de la zone recadrée, en pixels de la vidéo d'origine (un enregistrement 1920×1080 sans 25 % à gauche devient 1440×1080), la zone recadrée remplit le cadre et l'exportation sort à ces dimensions. Les textes et les autres clips suivent. Le clip ne peut pas être tourné ni avoir d'images clés de position ou d'échelle. Ctrl+Z annule.
- **Fusion** : mode de fusion avec les autres calques, comme Produit, Superposition d'écran, Incrustation, Lumière tamisée, Addition, Obscurcir, Éclaircir et Différence.
- **Fondus** : **Fondu d'entrée** et **Fondu de sortie**, de 0 à 10 secondes, pour que le clip apparaisse et disparaisse en douceur.
- **Vitesse** : vitesses prédéfinies ou autre vitesse. En dessous de 1×, l'option **Ralenti lissé** mélange les images voisines pour réduire les saccades. **Lire à l'envers** montre le clip de la fin au début, avec l'audio lié inversé lui aussi (aussi dans le menu contextuel du clip ; le clip reçoit le symbole ◀ dans la timeline). Ne s'applique pas aux images figées. Sur les longues vidéos, utilisez des proxies pour une lecture à l'envers fluide.
  - **Ralenti par interpolation (flux optique)** : en dessous de 1×, crée les images intermédiaires en suivant le mouvement, ce qui est plus fluide que le simple mélange. Aucun modèle n'est nécessaire, mais la lecture et l'exportation sont plus lentes.
  - **Courbe** : courbe de vitesse (speed ramping). Choisissez **Montage**, **Héros**, **Bullet time**, **Saut**, **Accélérer** ou **Ralentir** pour que le clip accélère et ralentisse en lui-même, sans changer la durée ni le passage de la vidéo utilisé. **Constante (sans courbe)** revient à la normale. Avec une courbe, l'audio du clip est coupé. Rogner ou scinder le clip revient aussi à la vitesse constante.
  - **Flou de mouvement** : traînée de mouvement quand la position, l'échelle ou la rotation sont animées par images clés. À 100 %, la traînée couvre l'intervalle d'une image entière.
- **Figer l'image**, **Répéter le passage**, **Remplir le cadre**, **Pivoter de 90°** et **Retourner** horizontalement et verticalement.
- **Transition d'entrée** : applique une transition entre ce clip et le clip qui le précède sur la même piste. Pour l'utiliser, les deux clips doivent se toucher. Vous choisissez la transition et la **Durée de la transition**.

- **Suivi de caméra** : choisissez **Tête** ou **Corps**, et une caméra virtuelle suit cette partie de la personne. Réglez :
  - **Coupe automatique (zoom)** : rapproche l'image de la cible.
  - **Suivre le mouvement** : dans quelle mesure la caméra suit la cible (à 0 %, elle reste au centre).
  - **Secouer** : tremblement de caméra autour de la cible.
  - **Arrière-plan flou** : floute tout ce qui n'est pas la personne.

  Le suivi utilise le même modèle de détourage de personne que l'onglet **IA** (installez-le dans **Modèles d'IA** si vous ne l'avez pas encore). Sans le modèle ou sans personne dans le cadre, la caméra reste au centre et l'arrière-plan n'est pas flouté. L'effet apparaît aussi dans la liste des effets appliqués de l'onglet **Effets**.
- **Retoucher** : retouche du visage de la personne, image par image. Cliquez sur **Beauté**, **Maquillage** ou **Flouter les visages** pour ajouter l'effet au clip ; les réglages apparaissent juste en dessous.
  - **Flouter les visages** : trouve le visage dans chaque image et le cache par **Flou**, **Pixelisation** ou **Bande noire**. Réglez l'**Intensité** et la **Taille de la zone**. Fonctionne avec un visage par image.
  - **Beauté** : **Peau · lisser** et **Peau · éclaircir**, **Rides d'expression** et **Poches sous les yeux** (lissent ces zones), et réglages de forme : **Visage · affiner**, **Yeux · agrandir**, **Nez · affiner**, **Bouche · taille** et **Sourcils · relever** (les valeurs négatives font l'inverse).
  - **Maquillage** : choisissez un **Look** prêt (Naturel, Glamour, Soirée ou Rosé) ou **Aucun**, et réglez chaque élément : **Cils**, **Fard à paupières**, **Eye-liner**, **Sourcils**, **Rouge à lèvres**, **Fard à joues**, **Contouring**, **Highlighter**, **Couleur des yeux** et **Taches de rousseur**. Le fard, les sourcils, le rouge à lèvres, le fard à joues et l'iris ont leur propre couleur. Un élément dont la valeur dépasse 0 % utilise sa valeur et sa couleur ; à 0 %, c'est le réglage du look qui s'applique.

  La retouche utilise le modèle **Face Mesh V2**, qui localise 478 points du visage. Installez-le dans **Outils › Modèles d'IA › Télécharger des modèles…** (environ 5 Mo). Si le modèle de détourage de personne est installé, il aide à trouver la tête dans le cadre. Sans le modèle Face Mesh ou sans visage visible, l'image reste telle quelle. La retouche fonctionne mieux avec un visage de face qui occupe une bonne partie de l'image.

Les transitions disponibles sont : Coupe franche, Fondu enchaîné, Fondu au noir, Zoom, Glissement (vers la gauche, la droite, le haut et le bas), Poussée (vers la gauche et la droite), Volet (vers la gauche, la droite, le haut et le bas), Cercle, Losange, Horloge et Stores.

### Effets

L'onglet **Effets** comporte deux parties. En haut, une grille avec une miniature de chaque effet, qui montre le résultat sur un paysage d'exemple : cliquez sur l'une d'elles pour ajouter l'effet au clip. En bas, la liste **Effets appliqués**, dans l'ordre où les effets sont traités. Chaque effet de la liste a des curseurs pour ses réglages et les boutons **Monter**, **Descendre** et **Supprimer l'effet**. La flèche à gauche du nom affiche ou masque les réglages. Double-cliquez sur un curseur pour revenir à la valeur par défaut. Dans les réglages de couleur, une bande colorée sous le curseur montre l'effet de la valeur (par exemple, du bleu à l'orange pour la température).

Effets visuels disponibles :

- **Flou**, **Netteté**, **Vignette**, **Noir et blanc**, **Sépia**, **Négatif**, **Pixelisation**, **Grain**, **Postérisation** et **Lueur douce**.
- **Incrustation chroma** : supprime un fond vert, bleu ou d'une autre couleur choisie. Il comporte la tolérance, la douceur de la transition, le lissage des bords, la suppression du reflet coloré et l'option de rendre le fond transparent ou de couleur unie.
- **Affiner les bords** : contracte ou étend le bord de la transparence, l'adoucit et nettoie la couleur des bords. À utiliser après l'incrustation chroma ou un masque.
- **Recadrage 360°** : transforme une vidéo 360° (équirectangulaire, largeur égale au double de la hauteur) en vue normale. Réglez la **Direction**, l'**Inclinaison**, la **Rotation** et le **Champ de vision** et choisissez le **Format de la vue** (16:9, 9:16, 1:1, 4:3 ou 4:5). Créez des images clés sur ces valeurs pour que la caméra virtuelle se déplace dans la vidéo.
- **Correction d'objectif** : corrige la courbure des lignes droites causée par les objectifs grand-angle et les caméras d'action (valeurs négatives) ou par les téléobjectifs (positives). **Zoom pour masquer les bords** agrandit l'image pour supprimer les coins vides.
- **Réduction du bruit vidéo** : adoucit le grain des enregistrements en faible lumière. **Préserver les détails** garde plus de texture.
- **Visualiseur audio** : barres (normales ou en miroir) ou une onde qui dansent sur le son d'une musique. À l'ajout, choisissez la musique dans la bibliothèque ; elle doit être dans la timeline. Réglez le nombre de barres, la hauteur, la couleur et l'opacité. À utiliser sur un clip vidéo ou image (par exemple un fond).

Dans les onglets **Effets** et **Couleur**, l'option **Comparer avant/après dans le moniteur** montre, à gauche d'une ligne dans le moniteur, la vidéo sans effets et sans correction colorimétrique, pour comparer. Faites glisser la poignée de la ligne vers la droite ou la gauche pour changer la séparation.

Les cartes des options déjà appliquées au clip ont une bordure mise en évidence. Un double-clic sur une carte appliquée la retire : il supprime l'effet ou, pour un préréglage de couleur, les réglages qu'il a créés (à condition que vous ne les ayez pas modifiés).

### Couleur

L'onglet **Couleur** commence par les **Préréglages** : Noir et blanc, Noir, Cinéma, Vif, Chaud, Froid, Délavé, Rétro et Clair et aéré. Cliquez sur une miniature pour appliquer le style au clip. Le préréglage entre dans la liste des corrections appliquées sous forme de réglages ordinaires, que vous pouvez modifier ou supprimer ; **Annuler** (Ctrl+Z) revient sur tout d'un coup.

En dessous, dans **Outils de couleur** (et dans la section **Couleur** de la bibliothèque), se trouvent les outils de correction colorimétrique :

- **Réglages de couleur** : exposition, luminosité, contraste, saturation, température, teinte (vert ou magenta), tonalité, ombres et hautes lumières.
- **Balance des blancs** : indiquez la couleur qui devrait être un gris neutre, et le programme corrige la dominante.
- **Courbes** : courbes de tons de toute la vidéo et par canal (rouge, vert et bleu).
- **Roues chromatiques** : réglage séparé des ombres, des tons moyens et des hautes lumières.
- **LUT 3D (.cube)** : applique une table de couleurs au format .cube, très utilisée pour donner un style à la vidéo. Choisissez l'intensité avec le curseur.
- **TSL** : règle la teinte, la saturation et la luminance de chaque plage de couleur (rouge, orange, jaune, vert, cyan, bleu, violet et magenta) sans toucher aux autres, par exemple seulement le bleu du ciel. Les gris, le noir et le blanc ne changent pas.
- **HDR** : aspect HDR, qui récupère les ombres et les hautes lumières et renforce les détails. Il comporte l'intensité, les ombres, les hautes lumières, les détails et la vibrance.
- **Harmoniser les couleurs** : aligne la couleur et le contraste d'un clip sur ceux d'un autre. Sélectionnez le clip et utilisez **Harmoniser les couleurs avec le clip précédent** dans l'onglet **IA** : le programme compare trois images de chaque clip et crée l'effet, avec gain et décalage par canal. Réglez l'**Intensité** pour un compromis.

### Masques, suivi et stabilisation

Ces fonctions se trouvent dans l'onglet **Composition**. Elles marchent pour les vidéos, images, textes et éléments graphiques.

- **Masques** : créez une forme avec **+ Rectangle**, **+ Ellipse** ou **+ Libre** (points, avec Ctrl+clic dans le moniteur). Le masque découpe ce qui est visible du clip. Vous pouvez additionner, soustraire ou intersecter des masques, inverser le masque et adoucir le contour.
- **Masque de couleur (effets seulement)** : coché, le masque ne découpe pas le clip ; il limite la correction colorimétrique et les effets à sa zone (par exemple, éclaircir seulement le visage ou flouter seulement une plaque). Le bord adouci du masque fait la transition.
- **Forme clé** : avec le bouton **◇ Forme clé**, le masque change de forme au fil du temps.
- **Suivi de mouvement** : marquez un objet dans le moniteur, cliquez sur **Suivre** et le programme suit cet objet. Un autre clip peut alors **Suivre** le mouvement, avec l'option **Suivre**. Utilisez **Libérer** pour le désactiver.
- **Stabilisation** : **Analyser et stabiliser** mesure le mouvement de la caméra et compense les tremblements. Réglez la **Douceur** et l'**Agrandissement** de l'image. Utilisez **Supprimer la stabilisation** pour annuler.

### Éléments graphiques et formes

Dans la section **Éléments graphiques** de la bibliothèque, double-cliquez sur une forme pour l'insérer à la tête de lecture. Les formes disponibles sont : Rectangle, Rectangle arrondi, Cercle, Ligne, Flèche, Étoile, Hexagone, Bulle de dialogue et Cœur. Vous pouvez régler le remplissage et la couleur du contour dans l'onglet **Composition**.

Vous pouvez aussi importer un graphique vectoriel avec le bouton **Importer un SVG…**.

**Autocollants** affiche les autocollants des packs installés. Le programme est livré avec le pack **Basique**, avec 40 autocollants courants (flèches, cercle de mise en évidence, juste et faux, bulles de dialogue, cœur, étoile, emojis, boutons S'abonner et J'aime, badges comme Nouveau et Promo, entre autres). Il ne peut pas être supprimé, mais il peut être exporté. Cliquez sur l'un d'eux pour l'insérer à la tête de lecture pendant 3 secondes, à 40 % de la taille (réglable dans l'onglet Vidéo), sur une piste d'image libre (ou sur une nouvelle piste appelée Autocollants). L'image entre aussi dans la bibliothèque, dans la catégorie Autocollants.

### Textes et sous-titres

### Insérer des textes

Dans la section **Textes** de la bibliothèque, double-cliquez sur un modèle pour l'insérer à la tête de lecture :

- **Texte simple** : texte en gras avec une ombre douce.
- **Titre** : grand titre au centre, avec une entrée en zoom.
- **Bandeau (lower third)** : nom et fonction dans le coin inférieur gauche, avec encadré et barre d'accent.
- **Générique défilant** : liste de crédits qui monte à l'écran pendant le clip.
- **Sous-titre** : sous-titre sur la piste de sous-titres, avec un encadré sombre en bas du cadre.

Sélectionnez le texte dans la timeline pour le modifier. L'onglet **Texte** des propriétés comporte :

- **Contenu** : le texte. Appuyez sur Entrée pour aller à la ligne.
- **Police** : polices de Windows ou polices ajoutées au programme avec **+ Police** (fichiers .ttf ou .otf).
- **Gras**, **Italique** et **Souligné**.
- **Couleurs** du texte, du contour, de l'encadré de fond et de l'ombre. Utilisez le format #RRGGBB ou #AARRGGBB pour définir la couleur.
- **Alignement** et **Point d'ancrage**.
- **Animation d'entrée** et **Animation de sortie** : par exemple, fondu, glissement ou zoom.

La position, l'échelle, la rotation et l'opacité du texte se trouvent dans l'onglet **Vidéo**.

Pour réutiliser un texte prêt (style, animations et durée), sélectionnez-le et utilisez **Enregistrer le texte sélectionné comme titre…** dans la section Textes (ou dans le menu contextuel du clip). Il apparaît dans **Titres enregistrés** ; choisissez-le et cliquez sur **Insérer le titre enregistré** pour le placer à la tête de lecture. Les titres des packs installés apparaissent dans la même liste.

### Sous-titres

Les sous-titres sont des textes horodatés, organisés sur la piste de sous-titres. Pour travailler avec eux :

- **Outils › Importer des sous-titres (SRT/VTT)…** : apporte un fichier de sous-titres prêt.
- **Outils › Exporter des sous-titres (SRT/VTT)…** : enregistre les sous-titres du projet en fichier SRT ou VTT.
- **Outils › Éditeur de sous-titres** : ouvre une fenêtre pour voir et modifier tous les sous-titres. Vous pouvez ajouter, supprimer et modifier le texte et les horaires, aller à un sous-titre précis et tous les décaler d'un coup, en secondes.
- **Générer des sous-titres** : crée des sous-titres automatiquement à partir de la parole. Voir la section Intelligence artificielle.

À l'exportation, vous pouvez choisir entre enregistrer un fichier de sous-titres à côté de la vidéo ou dessiner les sous-titres sur la vidéo elle-même (sous-titres incrustés).

## Audio

### Mélangeur audio

Ouvrez le mélangeur dans **Outils › Mélangeur audio** ou avec le bouton de la barre d'outils. Il affiche une colonne pour chaque piste audio, avec :

- **Volume** (fader) et **panoramique** (gauche et droite). Double-cliquez sur le fader ou le panoramique pour revenir à la valeur par défaut.
- **M** (muet) : coupe la piste.
- **S** (solo) : ne laisse jouer que les pistes en solo.
- Des vumètres, qui bougent pendant la lecture.

### Onglet Audio du clip

Sélectionnez un clip audio, ou une vidéo avec du son, et utilisez l'onglet **Audio** :

- **Volume** : 100 % est le volume d'origine. Avec le bouton ◇, vous créez une image clé de volume. Vous pouvez ainsi baisser la musique pendant une prise de parole, par exemple.
- **Fondu d'entrée** et **Fondu de sortie** : fondus de 0 à 10 secondes, pour que le son commence et finisse en douceur.
- **Normaliser l'intensité sonore** : ajuste le volume à une cible d'intensité sonore (norme EBU R128), sans dépasser une crête sûre. Choisissez la **Cible d'intensité sonore** avant d'appliquer.
- **Supprimer les silences** : coupe les passages silencieux du clip. Réglez le **Seuil**, en dessous duquel le son est considéré comme du silence, et la **Durée minimale** du silence.
- **Battements** : avec la musique sélectionnée, **Marquer les battements** crée un marqueur sur chaque battement et **Couper les vidéos sur les battements** scinde les clips vidéo et image qui jouent avec elle à chaque battement (pour des montages rythmés). Réglez la **Sensibilité** : plus elle est élevée, plus il y a de battements trouvés.
- **Atténuer sous la parole** : avec la musique sélectionnée, cliquez sur **Atténuer sous la parole** et le programme baisse son volume là où il y a de la parole sur les autres pistes (narration, interview). Choisissez la **Réduction** (−12 dB par défaut) et la **Transition** (durée de la descente et de la remontée). Le résultat est une série d'images clés de volume, que vous pouvez ajuster ; **Supprimer l'atténuation** efface cette animation. Avec le modèle de détection de la parole installé, la détection est plus précise ; sans lui, le programme utilise le niveau sonore.
- **Synchroniser par l'audio** : si vous avez filmé le même moment avec deux caméras ou un enregistreur audio, sélectionnez les deux clips et cliquez sur **Synchroniser**. Le programme aligne les deux sons. Le clip de la piste la plus haute reste en place et l'autre est déplacé.
- **Conserver la hauteur avec la vitesse** : quand le clip est accéléré ou ralenti, la voix garde une hauteur naturelle.

### Effets audio

Dans le même onglet, sous **Ajouter un effet audio**, se trouvent les effets :

- **Égaliseur** : graves, médiums et aigus, avec coupe-bas.
- **Compresseur** : rapproche les passages forts des passages faibles, avec seuil, ratio, attaque, relâchement et gain de compensation.
- **Réduction du bruit** : diminue les souffles et bruits constants, comme un ventilateur ou la climatisation. Il comporte intensité et sensibilité.
- **Écho** : répétitions retardées du son.
- **Réverbération** : simule le son d'une pièce.
- **Téléphone / radio** : son de téléphone ou de vieille radio.
- **Distorsion** : saturation du son.
- **Changer la voix** : voix prêtes (**Grave**, **Aiguë**, **Écureuil**, **Monstre** et **Robot**) ou, en **Personnalisé**, la **Hauteur** en demi-tons et l'intensité du **Robot**. La vitesse ne change pas.

Les effets sont appliqués dans l'ordre où ils apparaissent dans la liste **Effets audio appliqués**.

## Intelligence artificielle

Les fonctions d'intelligence artificielle du programme s'exécutent uniquement sur votre ordinateur. Aucune vidéo, aucun audio, aucune image ni aucun texte n'est envoyé sur internet.

Les fonctions se trouvent dans l'onglet **IA** des propriétés, quand vous sélectionnez un clip dans la timeline. La section **IA** de la bibliothèque résume les fonctions et comporte le bouton **Gérer les modèles d'IA…**, qui ouvre le gestionnaire de modèles.

### Modèles nécessaires

Chaque fonction utilise un modèle d'IA. Le modèle de **Supprimer l'arrière-plan** est fourni avec le programme. Les autres doivent être installés, une seule fois, avec l'outil **Télécharger des modèles d'IA**. Quand le modèle d'une fonction n'est pas installé, le bouton est désactivé et le message « Indisponible — installez le modèle dans Outils › Modèles d'IA. » apparaît.

| Fonction | Modèle | Installation nécessaire | Exécution sur |
|---|---|---|---|
| Supprimer l'arrière-plan | MODNet | Non, fourni avec le programme | CPU ou GPU |
| Calculer les masques en arrière-plan | MODNet | Non, fourni avec le programme | CPU ou GPU |
| Suivre la personne | MODNet | Non, fourni avec le programme | CPU ou GPU |
| Générer des sous-titres | Whisper large-v3 turbo | Oui, environ 550 Mo | CPU seulement |
| Isoler la voix | GTCRN | Oui, environ 0,5 Mo | CPU seulement |
| Réduire le bruit par IA | GTCRN | Oui, environ 0,5 Mo | CPU seulement |
| Supprimer les passages sans parole | Silero VAD 5 | Oui, environ 2,3 Mo | CPU seulement |
| Détecter les scènes | TransNetV2 | Oui, environ 31 Mo | CPU ou GPU |
| Améliorer l'image | Swin2SR léger 2× | Oui, environ 8 Mo | CPU ou GPU |
| Retoucher (beauté et maquillage) et flouter les visages | Face Mesh V2 | Oui, environ 5 Mo | CPU seulement |
| Assistant virtuel (réponses en langage naturel) | Phi-4-mini-instruct | Oui, environ 3,2 Go | CPU ou GPU |

La détection des scènes fonctionne même sans le modèle TransNetV2, en comparant les images de la vidéo. Dans ce cas, la précision est moindre.

La reconnaissance vocale (sous-titres) utilise Silero VAD pour trouver les passages avec de la parole. Quand vous installez Whisper, Silero VAD est installé avec, automatiquement.

> Les modèles du tableau occupent de l'espace disque et aussi de la mémoire lors de leur utilisation. Whisper, par exemple, a besoin d'environ 1,5 Go de mémoire libre.

### Supprimer l'arrière-plan

Sépare la personne de l'arrière-plan et rend l'arrière-plan transparent, ou le remplace par autre chose. À utiliser dans l'onglet **IA**, avec **Supprimer l'arrière-plan par IA**.

- **Nouvel arrière-plan** : choisissez **Transparent**, **Couleur unie**, **Flou** ou **Image ou vidéo de la bibliothèque**. Dans ce dernier cas, choisissez le fichier dans **Média d'arrière-plan**.
- **Conserver** : gardez la personne (premier plan) ou l'arrière-plan, pour supprimer la personne.
- **Réglages** : le seuil du masque, la contraction ou l'extension du bord, l'adoucissement du bord et le lissage entre images se trouvent dans l'onglet **Effets**, sous **Effets appliqués**. Le lissage entre images réduit le scintillement du bord d'une image à l'autre.

Le modèle a été entraîné principalement pour les portraits. Dans les scènes avec des objets qui ressemblent à des personnes, il peut garder des parties de l'arrière-plan.

Utilisez **Calculer les masques en arrière-plan** pour traiter le clip entier à l'avance. La lecture et l'exportation sont alors plus rapides. Le résultat est conservé dans le cache.

Utilisez **Suivre la personne par IA** pour créer un suivi de la personne. Ensuite, dans l'onglet **Composition**, vous pouvez y attacher des textes ou des éléments graphiques.

### Générer des sous-titres

Crée des sous-titres automatiquement à partir de la parole du clip.

1. Sélectionnez le clip et allez dans l'onglet **IA**.
2. Dans **Langue parlée**, choisissez Portugais, Anglais, Espagnol ou la langue automatique.
3. Cliquez sur **Générer des sous-titres par IA**.
4. Les sous-titres apparaissent sur la piste de sous-titres. Relisez-les dans l'**Éditeur de sous-titres**.

La reconnaissance s'exécute sur le processeur et prend environ 4 minutes pour une vidéo de 10 minutes. Suivez l'avancement dans la barre de progression de l'onglet.

### Isoler la voix et réduire le bruit

- **Isoler la voix par IA** : sépare la voix de l'ambiance. Le programme crée deux nouvelles pistes, l'une avec la voix et l'autre avec l'ambiance.
- **Réduire le bruit par IA** : diminue le bruit de fond, avec l'**Intensité de la réduction du bruit** choisie. Utile pour les enregistrements avec souffle ou bourdonnement.

Ces opérations utilisent l'audio du clip tel qu'il sonne dans la timeline. L'option **Couper l'audio d'origine** évite d'entendre l'ancien son en même temps que le nouveau.

### Supprimer les passages sans parole

Trouve les passages sans parole et les supprime du clip. Utilisez **Pause minimale** pour définir la durée minimale de pause qui sera coupée, puis cliquez sur **Supprimer les passages sans parole par IA**. Cette fonction nécessite le modèle Silero VAD installé ; sans lui, le bouton est indisponible. Pour couper les silences sans IA, utilisez l'onglet **Audio** (**Supprimer les silences**).

### Détecter les scènes

Cherche les changements de scène de la vidéo et crée un marqueur sur chacun.

1. Sélectionnez le clip et allez dans l'onglet **IA**.
2. Réglez la **Sensibilité** des scènes. Plus elle est élevée, plus il y a de scènes trouvées.
3. Cochez **Scinder le clip à chaque scène** si vous voulez que le clip soit coupé à chaque scène.
4. Cliquez sur **Détecter les scènes**.

### Améliorer l'image

Agrandit une photo 2× en reconstruisant les détails. Sélectionnez l'image, allez dans l'onglet **IA** et cliquez sur **Améliorer l'image par IA**. Le clip utilise désormais la nouvelle image. Pour revenir à l'original, utilisez **Annuler**.

### Assistant virtuel

Répond aux questions sur le logiciel et sur le montage vidéo d'après ce manuel, dans la langue du logiciel et sans internet. Ouvrez-le avec **Aide › Assistant virtuel** (Maj+F1) : le panneau apparaît à droite de l'éditeur. Tapez la question et appuyez sur Entrée (Maj+Entrée passe à la ligne) ; sans conversation, le panneau propose quelques questions.

- Avec le modèle de langage **Phi-4-mini** installé, l'assistant rédige une réponse courte en utilisant uniquement les extraits du manuel trouvés et affiche la **Source dans le manuel** : cliquez dessus pour ouvrir le manuel à cette section. Le bouton d'arrêt interrompt la réponse.
- Sans le modèle, l'assistant affiche les sections du manuel qui traitent du sujet, avec un extrait de chacune et le bouton **Ouvrir dans le manuel**. L'avis en haut du panneau installe le modèle (environ 3,2 Go, téléchargés une seule fois depuis les sources officielles) sans quitter l'éditeur ; **Pas maintenant** masque l'avis.
- Le modèle utilise la carte graphique quand le **Périphérique d'IA** est **GPU (DirectML)** (dans **Paramètres › Performances**), sinon le processeur. La première question prend quelques secondes de plus, car le modèle est chargé ; à la fermeture du panneau, il quitte la mémoire.
- **Nouvelle conversation** efface la conversation. Les réponses peuvent contenir des erreurs : vérifiez dans la section indiquée.

### Annuler un traitement

Pendant qu'une fonction d'IA s'exécute, une barre de progression apparaît. Cliquez sur **Annuler le traitement IA** pour l'interrompre.

## Exporter la vidéo

Exporter, c'est générer le fichier final de la vidéo. Cliquez sur **Exporter** dans le coin supérieur droit de la fenêtre, ou utilisez **Fichier › Exporter…** (raccourci Ctrl+E).

### Modèles d'exportation

Dans la liste à gauche de la fenêtre d'exportation, choisissez un modèle. Chaque modèle contient déjà les bons réglages pour une plateforme :

- YouTube
- YouTube Shorts
- Instagram Feed
- Instagram Reels
- TikTok
- WhatsApp Status
- Partage WhatsApp
- Facebook Feed
- Facebook Reels
- Présentations
- Ordinateurs et portables
- Exportation personnalisée

Réglez les paramètres et, si vous le souhaitez, cliquez sur **Enregistrer comme nouveau…** pour garder votre configuration. Vos propres modèles portent le badge « Mon modèle » et peuvent être modifiés, dupliqués ou supprimés. Les modèles par défaut ne peuvent pas être supprimés, mais vous pouvez utiliser **Restaurer par défaut** pour rétablir la configuration d'origine d'un modèle, ou **Tout restaurer**.

### Paramètres

- **Format du fichier** : MP4, MOV (QuickTime), MKV, WebM, AVI, MPEG-2 (.mpg) et GIF animé. Tous les formats n'acceptent pas tous les codecs. Si la combinaison n'est pas possible, la fenêtre vous prévient.
- **Encodeur** : **Automatique (carte graphique si disponible)**, **Carte graphique** ou **Logiciel (plus compatible)**. Le mode Logiciel est le plus sûr pour les fichiers qui doivent s'ouvrir sur n'importe quel appareil.
- **Codec vidéo** : H.264 (AVC), H.265 (HEVC), AV1, VP9, Apple ProRes, FFV1, QuickTime Animation, Motion JPEG, MPEG-2 ou GIF. Dans la plupart des cas, choisissez H.264.
- **Profil** : Par défaut, High, Main ou Baseline du codec. Gardez Par défaut si vous ne savez pas.
- **Contrôle du débit** : **Débit variable (VBR)**, **Débit constant (CBR)** ou **Qualité constante**. Avec la qualité constante, utilisez des valeurs entre 18 et 23 ; les nombres plus petits signifient une meilleure qualité.
- **Débit vidéo (kbps)** : plus il est élevé, meilleure est la qualité et plus le fichier est gros. Pour internet, utilisez la valeur suggérée par le modèle.
- **Résolution** : cochez **Utiliser la résolution du projet** ou indiquez la largeur et la hauteur en pixels.
- **Fréquence d'images** : **Du projet**, ou l'une des fréquences standard (23,976, 24, 25, 29,97, 30, 50, 59,94 et 60 FPS).
- **Codec audio**, **Fréquence d'échantillonnage** (44,1 kHz, 48 kHz ou 96 kHz), **Canaux** (Mono, Stéréo ou 5.1) et **Débit audio (kbps)**.
- **Exporter la transparence (canal alpha)** : garde le fond transparent. À utiliser avec ProRes 4444, QuickTime Animation, FFV1, VP9 ou GIF.

### Intervalle et division du fichier

- **Intervalle** : choisissez **Séquence entière** pour exporter tout le projet, ou **Intervalle personnalisé** pour n'exporter qu'un passage. Définissez le **Début** et la **Fin**, ou utilisez **Début à la tête de lecture**.
- **Diviser en fichiers** : divise l'exportation en parties d'une durée maximale en secondes, ou avec une limite de taille en Mo. Utile pour les plateformes à taille limitée.

### Sous-titres à l'exportation

Dans **Sous-titres**, choisissez **Aucun**, **SubRip (.srt)** ou **WebVTT (.vtt)** pour enregistrer un fichier de sous-titres à côté de la vidéo. Cochez **Dessiner les sous-titres sur la vidéo (sous-titres incrustés)** pour qu'ils soient gravés dans l'image.

### Destination et lancement

1. Choisissez le **Dossier de destination** et le **Nom du fichier**.
2. Cochez **Rendu en arrière-plan** si vous voulez continuer le montage pendant la génération de la vidéo.
3. Cliquez sur **Exporter** pour commencer.

### File de rendu

Chaque exportation entre dans la file de rendu. Ouvrez la file avec le bouton **File de rendu** ou dans **Fichier › File de rendu…**. Pour chaque élément, la file affiche l'état, la progression, la vitesse et le temps restant :

- **En file** : attend son tour.
- **Rendu en cours** : en cours.
- **Terminée** : prête. Utilisez **Ouvrir le fichier exporté** ou **Afficher dans le dossier**.
- **Interrompue** : peut être poursuivie avec **Reprendre**. À la reprise, le programme réutilise les blocs déjà générés.
- **Échec** : le message explique la raison.
- **Annulée** : interrompue par l'utilisateur.

Utilisez **Annuler l'exportation** pour arrêter un élément, **Retirer de la file** pour l'enlever de la liste et **Effacer les terminées** pour vider les éléments finis.

## Enregistrement, enregistrement automatique et récupération

### Enregistrer le projet

- **Fichier › Enregistrer** (Ctrl+S) : écrit les modifications dans le fichier du projet.
- **Fichier › Enregistrer sous…** (Ctrl+Maj+S) : écrit une copie du projet sous un autre nom ou dans un autre dossier.
- **Fichier › Créer une sauvegarde** : crée une sauvegarde du projet à l'instant présent. Le programme garde jusqu'à 10 copies de chaque projet, dans le dossier de sauvegardes de l'application.

Le fichier du projet a l'extension .med. Le programme ne modifie pas les fichiers des médias d'origine.

### Enregistrement automatique

Par défaut, le programme enregistre une copie de récupération toutes les 5 minutes. Vous pouvez désactiver cette option, ou changer l'intervalle, dans **Outils › Paramètres…**, onglet **Général**.

La copie de récupération ne remplace pas le fichier du projet. Elle n'est proposée que lorsque le programme est ouvert après une fermeture inattendue, comme une coupure de courant ou un plantage.

### Récupérer un projet

Si le programme a été fermé sans enregistrer, à sa réouverture la question « Récupérer le projet » apparaît :

- **Oui** : ouvre le projet avec les dernières modifications.
- **Non** : ouvre un projet vide ; la copie reste conservée et sera proposée de nouveau la prochaine fois.

> Il est recommandé d'enregistrer manuellement avec Ctrl+S souvent, même avec l'enregistrement automatique activé.

## Paramètres et performances

Ouvrez les paramètres dans **Outils › Paramètres…** (raccourci Ctrl+,). Les paramètres sont répartis en quatre onglets. Rien n'est enregistré tant que vous ne cliquez pas sur **Enregistrer**.

### Onglet Général

- **Utilisateur › Nom complet** : votre nom.
- **Langue du système › Langue** : Allemand, Espagnol, Français, Anglais, Portugais (Brésil) ou Portugais (Portugal). Au premier lancement, le programme utilise la langue de Windows si c'est l'une de celles-ci, sinon l'anglais. À l'enregistrement, le programme redémarre dans la nouvelle langue : il demande d'abord quoi faire des modifications non enregistrées et rouvre un projet enregistré. Le manuel s'ouvre aussi dans la langue choisie.
- **Thème** : **Sombre**, **Clair** ou **Comme Windows**. Vous pouvez aussi basculer avec le bouton **Thème** de la barre supérieure.
- **Rechercher des mises à jour › Vérifier tous les mois** : **Oui** (par défaut) ou **Non**. La date de la dernière vérification apparaît à côté. Avec **Oui**, le programme vérifie de lui-même à l'ouverture s'il existe une nouvelle version lorsque plus de 30 jours se sont écoulés depuis la dernière vérification. La requête n'envoie que la langue de l'interface ; sans internet, rien n'apparaît.
- **Ouvrir le dernier projet au démarrage**.
- **Dossiers par défaut** : le dossier des projets et le dossier des exportations.
- **Enregistrement automatique** : activer ou désactiver la copie de récupération et définir l'intervalle.

Le nom, la langue, le thème et la préférence de mises à jour sont stockés dans le Registre de Windows, sous `HKEY_CURRENT_USER\Software\Meu Editor de Vídeo`.

### Onglet Performances

- **Matériel détecté** : affiche le processeur, la mémoire, les cartes graphiques, les disques et si la carte graphique peut décoder et encoder la vidéo. Le programme indique aussi les limites de l'ordinateur et une recommandation d'utilisation.
- **Aperçu** : la **Qualité d'aperçu par défaut** peut être **Automatique**, **Complète**, **1/2 de la résolution** ou **1/4 de la résolution**. En Automatique, le programme choisit selon l'ordinateur et le projet, et réduit la résolution pendant la lecture si l'ordinateur ne suit pas. La **Mémoire pour les images de l'aperçu** définit l'espace utilisé pour garder des images prêtes.
- **Proxies** : **Utiliser les proxies dans l'aperçu s'ils existent** et **Générer automatiquement des proxies pour les vidéos au-delà de 1080p**.
- **Accélération** : **Utiliser la carte graphique pour décoder et encoder la vidéo** et **Effectuer le rendu de la file d'exportation en arrière-plan**.
- **Intelligence artificielle** : le **Périphérique d'IA** peut être **GPU (DirectML), avec repli sur le processeur**, ou **Processeur uniquement**. Avec l'option GPU, si la carte ou le pilote échoue, le modèle est chargé automatiquement sur le processeur. Le bouton **Gérer les modèles…** ouvre la fenêtre des modèles d'IA.

### Onglet Cache

Le cache conserve les miniatures, les formes d'onde, les proxies et les masques d'IA, pour qu'ils n'aient pas à être recalculés. Dans cet onglet, vous voyez l'espace utilisé par chaque zone, définissez la **Limite du cache** en Go (4 Go par défaut) et pouvez utiliser **Appliquer la limite maintenant** ou **Vider le cache**.

Quand la limite est dépassée, les éléments les moins récemment utilisés sont supprimés. Les projets et les médias d'origine ne sont jamais supprimés par le nettoyage du cache.

### Onglet Raccourcis

Cet onglet affiche toutes les actions du programme, regroupées par catégorie. Pour changer un raccourci :

1. Cliquez dans le champ du raccourci.
2. Appuyez sur la nouvelle combinaison de touches.
3. Cliquez sur **Enregistrer**.

Si la combinaison est déjà utilisée par une autre action, le programme vous prévient et ne vous laisse pas enregistrer. Utilisez **Par défaut** sur une action pour revenir au raccourci d'origine, ou **Tout restaurer par défaut** pour annuler toutes les modifications. Les menus affichent toujours le raccourci utilisé.

## Raccourcis clavier

Voici les raccourcis par défaut. Vous pouvez modifier chacun d'eux dans **Outils › Paramètres… › Raccourcis**.

| Action | Raccourci |
|---|---|
| Nouveau projet | Ctrl+N |
| Ouvrir un projet | Ctrl+O |
| Enregistrer | Ctrl+S |
| Enregistrer sous | Ctrl+Maj+S |
| Importer des médias | Ctrl+I |
| Exporter | Ctrl+E |
| Annuler | Ctrl+Z |
| Rétablir | Ctrl+Y ou Ctrl+Maj+Z |
| Paramètres | Ctrl+, |
| Manuel de l'utilisateur | F1 |
| Assistant virtuel | Maj+F1 |
| Lecture / pause | Espace |
| Plein écran | F11 (Échap ou F11 pour quitter) |
| Image précédente | ← |
| Image suivante | → |
| Reculer d'1 seconde | Maj+← |
| Avancer d'1 seconde | Maj+→ |
| Aller au début | Début |
| Aller à la fin | Fin |
| Coupe précédente | ↑ |
| Coupe suivante | ↓ |
| Scinder le clip à la tête de lecture | S ou Ctrl+K |
| Supprimer | Suppr ou Retour arrière |
| Supprimer et combler l'espace | Maj+Suppr |
| Copier | Ctrl+C |
| Couper | Ctrl+X |
| Coller | Ctrl+V |
| Dupliquer | Ctrl+D |
| Tout sélectionner | Ctrl+A |
| Grouper | Ctrl+G |
| Dissocier | Ctrl+Maj+G |
| Ajouter un marqueur | M |
| Activer/désactiver l'aimant | N |
| Zoom avant sur la timeline | Ctrl++ (touche plus) |
| Zoom arrière sur la timeline | Ctrl+- (touche moins) |
| Ajuster à la fenêtre | Maj+Z |
| Multicaméra : caméra 1 à 4 | Alt+1 à Alt+4 |

## Confidentialité

Meu Editor de Vídeo est conçu pour fonctionner sur votre ordinateur :

- Vos vidéos, photos, audios, textes et projets sont traités uniquement sur votre ordinateur.
- Les fonctions d'intelligence artificielle s'exécutent aussi sur votre ordinateur. Aucun contenu n'est envoyé sur internet.
- Le programme ne collecte pas de données d'utilisation et n'envoie pas de télémétrie.
- Le programme ne se met pas à jour tout seul par internet.

Il existe une seule exception. Quand vous cliquez sur **Installer** dans l'outil **Télécharger des modèles d'IA**, le programme télécharge les fichiers des modèles d'IA depuis les sites officiels de leurs auteurs, à savoir huggingface.co et github.com. Seuls les modèles choisis sont téléchargés, et aucune donnée de votre projet n'est envoyée. Une fois installés, les modèles fonctionnent sans internet. Vous pouvez aussi installer des packs de modèles déjà téléchargés, avec **Installer un pack…**, sans utiliser internet.

Pour voir les licences et les composants tiers utilisés, ouvrez **Aide › À propos de Meu Editor de Vídeo…**. Le texte complet des licences se trouve dans **Aide › Licences tierces**.

## Résolution des problèmes

### La fonction d'IA est « Indisponible »

Le modèle de la fonction n'a pas encore été installé. Allez dans **Outils › Modèles d'IA…**, cliquez sur **Télécharger des modèles…**, choisissez les modèles et cliquez sur **Installer**. L'outil installe d'abord les dépendances, s'il y en a. Pour la fonction Supprimer l'arrière-plan, aucune installation n'est nécessaire.

### L'installation d'un modèle a été interrompue

Ouvrez de nouveau **Télécharger des modèles…** et cliquez sur **Installer**. Le téléchargement reprend là où il s'était arrêté. Si le fichier téléchargé est incomplet ou altéré, il est supprimé et téléchargé à nouveau.

### L'aperçu saccade ou est lent

- En haut du moniteur, dans **Résolution de l'aperçu**, choisissez **1/2** ou **1/4**.
- Activez les proxies, en créant des proxies des grosses vidéos avec **Générer un proxy de montage (540p)**.
- Fermez les autres programmes gourmands pendant le montage.
- Dans **Paramètres › Performances**, vérifiez que l'accélération par carte graphique est activée.

L'exportation utilise toujours la résolution d'origine, donc une vidéo peut saccader dans l'aperçu et quand même s'exporter correctement.

### Un média apparaît comme manquant

Le fichier a été déplacé, renommé ou supprimé. Utilisez **Projet › Localiser les médias manquants…** pour indiquer le nouveau chemin. Pour l'éviter, utilisez **Intégrer les médias au projet** avant de déplacer le projet.

### « Format de fichier non pris en charge »

Le programme ne lit pas ce format. Convertissez le fichier dans un format courant, comme MP4 (vidéo) ou MP3 et WAV (audio), avec un autre programme de conversion, puis importez-le de nouveau.

### L'exportation a échoué

- Lisez le message dans la file de rendu. Il indique la raison.
- Si le message indique un codec incompatible avec le format, choisissez un autre codec ou un autre format.
- Si l'erreur concerne la carte graphique, choisissez l'encodeur **Logiciel (plus compatible)** et exportez de nouveau.
- Si le fichier ne s'ouvre pas sur d'autres appareils, utilisez le modèle **Ordinateurs et portables** ou le format MP4 avec H.264.

### J'ai perdu des modifications après un plantage

À l'ouverture du programme, répondez **Oui** à la question « Récupérer le projet ». Si elle n'apparaît pas, cherchez la sauvegarde du projet, qui se trouve dans le dossier de sauvegardes de l'application, dans `%LOCALAPPDATA%\MeuEditorDeVideo\backups`.

### Le programme ne s'ouvre pas ou affiche des erreurs

Les journaux de fonctionnement (logs) se trouvent dans le dossier `%LOCALAPPDATA%\MeuEditorDeVideo\logs`. Pour l'ouvrir, utilisez **Aide › Ouvrir le dossier des journaux** (ou appuyez sur Windows + R, collez ce chemin et appuyez sur Entrée). Pour demander de l'aide, envoyez le fichier journal le plus récent avec le texte de **Aide › Informations système…**. Les journaux ne contiennent pas le contenu de vos vidéos.

### Questions sur la version installée

Ouvrez **Aide › À propos de Meu Editor de Vídeo…** pour voir la version du programme, la licence, les informations de confidentialité et la liste des composants et modèles d'IA utilisés.
