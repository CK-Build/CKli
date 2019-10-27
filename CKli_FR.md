# CKli Démarrage rapide

## **Installer CKli**
`dotnet tool install CKli -g`

>  `ckli`  

Lancer CKli, à la première ouverture, il demande de créer un mot de passe pour le coffre-fort personnel (personl keyvault), *`a`* suffit.


Par défaut il y a 2 stacks qui sont disponibles.

A tout moment, une liste des commandes est disponible.
Les commandes run et list fonctionnent sur un ensemble de commandes, on peut filtrer ces commandes avec des wildcards et du texte :

> `list *apply*`  

Le filtrage est case insensitive, le mot "apply" est normalement écrit avec une majuscule.

`run` ne fonctionnera que sur les commandes ayant des paramètres (payload) identiques  
> `> run home*`

Donne ce résultat :
```
> Warn: Pattern 'home*' matches require 4 different payloads.
|  - Warn: (stackName, url, isPublic, mappedPath, branchName): Home/EnsureStackDefinition
|          (stackName): Home/DeleteStackDefinition
|          (worldFullName, mappedPath): Home/SetWorldMapping
|          <No payload>: Home/Close, Home/Refresh
```

Pour fermer le World :
> `run *close`  

Va lancer la commande :
```
run Home/Close
```

Les stacks sont stockées dans un repo git.  
Pour cloner un dépot contenant les configurations d'une stack :


> `run *ensure*`  

On nous demande ensuite différentes informations :
```bash
> SC                                                          # nom de la stack
> https://gitlab.com/signature-code/signature-code-stack.git  # URL du git
> false                                                       # le world est privé
> /dev/CK                                                     # chemin local où tout sera cloné
>                                                             # laisser vide
> y                                                           # valider
```
Signature-Code étant une stack privée, CKli ne pourra pas cloner le repo automatiquement.

Appuyez sur `Entrée` pour voir la liste des Worlds (On peut voir les PAT demandés en avertissements).

Créer un PAT sur GitLab :
* https://gitlab.com/profile/personal_access_tokens
* Name: GITLAB_GIT_WRITE_PAT
* cocher Write

Ajouter le PAT sur CKli
> `secret set GITLAB_GIT_WRITE_PAT`

Coller ensuite le token obtenu précédement
``` bash
> 6HFXXXXXXXXXXXXXXmhG
```

En appuyant de nouveau sur `Entrée`, le repo va automatiquement être cloné et les stacks définies dedans seront automatiquement affichées.
Pour ouvrir un World il suffit d'entrer son numéro, et CKli va cloner directement tous les repos présents dans le World



## Glossaire
Un `world` est un ensemble de repositories sur une certaine branche et sont décrits par un fichier xml.

Une `stack` est un ensemble d'au-moins un World ciblant un même ensemble de repo mais pas la même branche.

Un `PAT` ([Personal Access Token](https://docs.gitlab.com/ee/user/profile/personal_access_tokens.html)) est comme un mot de passe généré par un service pour un utilisateur permettant de l'authentifier et de faire des actions en son nom.



# Utilisation courante

## Compilation

Compiler l'ensemble d'un World peut prendre beaucoup de temps. Il faut compiler l'ensemble des projets Git, en respectant l'ordre de dépendance, et changer les versions des dépendances aux packages qui on été mis a jour. Pour se donner une idée de la quantité de travail, on peut facilement générer un graph de dépendance de la stack 
![Graph de dépendance de la stack CK.](CK-Dep-Graph.svg)